using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Recreates the legacy EquipmentLevelSet aura independently from the armor material shader.
    /// A valid full set receives six bone-linked lights from +9 and the denser +11/+13 particle
    /// and orbit layers described by the original client.
    /// </summary>
    public sealed class FullSetAuraEffect : EffectObject
    {
        private const string LightTexturePath = "Effect/flare01.jpg";
        private const string FlareTexturePath = "Effect/flare.jpg";
        // BITMAP_LIGHT + 2: texture of the WATERFALL_2 subtype 3 torso particles.
        private const string TorsoTexturePath = "Effect/cra_04.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 5;

        private const int MaxTorsoParticles = 96;
        private const int MaxOrbitParticles = 20;
        private const int MaxTrails = 16;
        private const int MaxTailPoints = 20;
        private const int MaxTrailRenderPoints = MaxTailPoints + 1;
        private const int MaxBillboardQuads = 6 + MaxTorsoParticles + MaxOrbitParticles + 12;

        private static readonly Vector3[] TorsoOffsets11 =
        {
            new(0f, -18f, 50f),
            new(0f,   0f, 70f),
            new(0f,  18f, 50f)
        };

        private static readonly Vector3[] TorsoOffsets13 =
        {
            new(0f, -20f, 50f),
            new(0f,   0f, 70f),
            new(0f,  20f, 50f)
        };

        private static readonly float[] TorsoScales = { 0.54f, 0.72f, 0.90f };

        // Every PlayerObject owns this controller, but only a small subset of players has a
        // complete +9 or higher set. Allocate the large transient simulation and geometry
        // buffers only when the aura becomes active for the first time.
        private TorsoParticle[] _torsoParticles;
        private OrbitParticle[] _orbitParticles;
        private OrbitTrail[] _trails;
        private VertexPositionColorTexture[] _billboardVertices;
        private static readonly short[] BillboardIndices = QuadIndexCache.Get(MaxBillboardQuads);
        private VertexPositionColorTexture[] _trailVertices;
        private short[] _trailIndices;

        private BasicEffect _effect;
        private Texture2D _lightTexture;
        private Texture2D _flareTexture;
        private Texture2D _torsoTexture;
        private bool _ownsLightTexture;
        private bool _ownsFlareTexture;
        private Task _texturePreparationTask;
        private volatile bool _disposed;
        private float _legacyAccumulator;
        private int _activeSetIndex = -1;
        private int _auraTier;
        private Vector3 _auraColor = new(0.72f, 0.78f, 0.90f);

        // EnableAlphaBlend2: glBlendFunc(GL_ONE_MINUS_SRC_COLOR, GL_ONE), used by BITMAP_FLARE subtype 18.
        private static readonly BlendState InverseSourceAdditive = new()
        {
            ColorSourceBlend = Blend.InverseSourceColor,
            AlphaSourceBlend = Blend.InverseSourceColor,
            ColorDestinationBlend = Blend.One,
            AlphaDestinationBlend = Blend.One,
        };
        private Vector3 _torsoColor;
        private bool _hasTorsoParticles;
        private bool _primeAuraPending;
        private uint _spawnSequence;
        private float _renderInterpolation;
        private float _auraTime;

        public FullSetAuraEffect()
        {
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-260f, -260f, -80f),
                new Vector3(260f, 260f, 360f));
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;

            Hidden = true;
        }

        public override Task LoadContent() => base.LoadContent();

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
                return;
            if (ItemEffectSystem.TryDeferUpdate(this))
                return;
            TickEffect(gameTime);
        }

        /// <summary>One frame of set detection and legacy ticks (called by <see cref="ItemEffectSystem"/>).</summary>
        internal void TickEffect(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
                return;

            if (!RefreshSetState())
            {
                base.Update(gameTime);
                return;
            }

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);
            _auraTime += elapsed;
            _legacyAccumulator += elapsed;

            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy();
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            _renderInterpolation = MathHelper.Clamp(
                _legacyAccumulator / LegacyStepSeconds,
                0f,
                1f);

            base.Update(gameTime);
        }

        private bool RefreshSetState()
        {
            if (Parent is not WalkerObject player ||
                player is not IEquippedCharacter gear ||
                player.Hidden ||
                player.Model == null ||
                player.World == null ||
                IsChaosCastleMap(player.World.WorldIndex))
            {
                ResetSetState();
                return false;
            }

            int setIndex = -1;
            int minimumLevel = int.MaxValue;

            bool requireHelm = player is not PlayerObject classed || !IsMagicGladiatorClass(classed.CharacterClass);
            bool requireGloves = player is not PlayerObject gloved || !IsRageFighterClass(gloved.CharacterClass);

            if (requireHelm && !AcceptPart(gear.Helm, ref setIndex, ref minimumLevel))
            {
                ResetSetState();
                return false;
            }

            if (!AcceptPart(gear.Armor, ref setIndex, ref minimumLevel) ||
                !AcceptPart(gear.Pants, ref setIndex, ref minimumLevel) ||
                (requireGloves && !AcceptPart(gear.Gloves, ref setIndex, ref minimumLevel)) ||
                !AcceptPart(gear.Boots, ref setIndex, ref minimumLevel))
            {
                ResetSetState();
                return false;
            }

            if (setIndex < 0 || minimumLevel < 9)
            {
                ResetSetState();
                return false;
            }

            EnsureTransientBuffers();
            EnsureTexturePreparationStarted();

            // EquipmentLevelSet: the lowest level of the set (Level & 0xF), every level 10-15 differs.
            int tier = Math.Min(minimumLevel, 15);
            if (_activeSetIndex != setIndex || _auraTier != tier)
            {
                ClearTransientEffects();
                _activeSetIndex = setIndex;
                _auraTier = tier;
                _auraColor = GetSetAuraColor(setIndex);
                _hasTorsoParticles = TryGetTorsoParticleColor(setIndex, out _torsoColor);
                _primeAuraPending = tier >= 11;
            }

            Hidden = false;
            return true;
        }

        private void EnsureTexturePreparationStarted()
        {
            // TextureLoader already deduplicates the underlying assets. Deferring these two
            // awaits still removes them from every remote-player initialization path, which
            // reduces warp/spawn fan-out when almost all players have no qualifying full set.
            _texturePreparationTask ??= PrepareTexturesAsync();
        }

        private async Task PrepareTexturesAsync()
        {
            Task<Texture2D> lightTask = PrepareTexture(LightTexturePath);
            Task<Texture2D> flareTask = PrepareTexture(FlareTexturePath);
            Task<Texture2D> torsoTask = PrepareTexture(TorsoTexturePath);
            await Task.WhenAll(lightTask, flareTask, torsoTask).ConfigureAwait(false);

            if (_disposed)
                return;

            _lightTexture = lightTask.Result;
            _flareTexture = flareTask.Result;
            _torsoTexture = torsoTask.Result;
        }

        private void EnsureFallbackTextures()
        {
            // Fallback Texture2D creation must remain on the graphics thread. The asynchronous
            // preparation method only resolves cached assets and never creates GPU resources.
            if (_lightTexture == null || _lightTexture.IsDisposed)
            {
                _lightTexture = CreateRadialTexture(GraphicsDevice, 64, 2.4f);
                _ownsLightTexture = true;
            }

            if (_flareTexture == null || _flareTexture.IsDisposed)
            {
                _flareTexture = CreateRadialTexture(GraphicsDevice, 64, 1.45f);
                _ownsFlareTexture = true;
            }
        }

        private void EnsureTransientBuffers()
        {
            if (_torsoParticles != null)
                return;

            _torsoParticles = new TorsoParticle[MaxTorsoParticles];
            _orbitParticles = new OrbitParticle[MaxOrbitParticles];
            _trails = new OrbitTrail[MaxTrails];
            for (int i = 0; i < _trails.Length; i++)
                _trails[i] = new OrbitTrail();

            _billboardVertices = new VertexPositionColorTexture[MaxBillboardQuads * 4];
            _trailVertices = new VertexPositionColorTexture[MaxTrails * MaxTrailRenderPoints * 2];
            _trailIndices = new short[MaxTrails * (MaxTrailRenderPoints - 1) * 6];
        }

        private void EnsureRenderEffect()
        {
            if (_effect != null && !_effect.IsDisposed)
                return;

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };
        }

        private static bool AcceptPart(ModelObject part, ref int setIndex, ref int minimumLevel)
        {
            if (part == null ||
                part.Hidden ||
                part.Model == null ||
                part.MaterialItemIndex < 0 ||
                part.ItemLevel < 9)
            {
                return false;
            }

            if (setIndex < 0)
                setIndex = part.MaterialItemIndex;
            else if (part.MaterialItemIndex != setIndex)
                return false;

            minimumLevel = Math.Min(minimumLevel, part.ItemLevel);
            return true;
        }

        private void TickLegacy()
        {
            if (Parent is not WalkerObject player)
                return;

            UpdateTorsoParticles();
            UpdateOrbitParticles(player.IsMoving || player.MovementIntent);
            UpdateTrails(player);

            // Distant/throttled players, and every player on the Low preset, retain their six skeletal
            // lights but do not emit the expensive transient layers.
            if (player.LowQuality || GraphicsQualityManager.ActivePreset == GraphicsQualityPreset.Low)
                return;

            _primeAuraPending = false;

            // BITMAP_WATERFALL_2 at the torso for the Dragon Knight - Sunlight / Aura boots:
            // +10 1/4, +11 1/3, +12 1/2, +13 and higher every tick.
            if (_hasTorsoParticles && _auraTier >= 10)
            {
                int chance = _auraTier switch { 10 => 4, 11 => 3, 12 => 2, _ => 1 };
                if (chance == 1 || MuGame.Random.Next(chance) == 0)
                    SpawnTorsoBurst(player, _auraTier >= 13 ? TorsoOffsets13 : TorsoOffsets11);
            }

            // SourceMain: EquipmentLevelSet > 9 && rand_fps_check(20).
            if (_auraTier <= 9 || MuGame.Random.Next(20) != 0)
                return;

            switch (_auraTier)
            {
                case 10:
                    SpawnOrbitParticle(player);
                    break;

                case 11:
                    if (MuGame.Random.Next(8) == 0)
                        SpawnClassicTrail(player);
                    else
                        SpawnOrbitParticle(player);
                    break;

                case 12:
                    // CreateJoint(BITMAP_FLARE, ..., 18, o, 20, -1, 0 / 1): two counter-rotating spirals.
                    if (MuGame.Random.Next(6) == 0)
                    {
                        SpawnExpandingTrail(player, direction: 1);
                        SpawnExpandingTrail(player, direction: -1);
                    }
                    else if (MuGame.Random.Next(3) == 0)
                    {
                        SpawnOrbitParticle(player);
                    }
                    break;

                default: // 13 - 15
                    if (MuGame.Random.Next(6) == 0)
                    {
                        SpawnExpandingTrail(player, direction: 1);
                        SpawnExpandingTrail(player, direction: -1);
                    }

                    if (MuGame.Random.Next(4) == 0)
                    {
                        SpawnOrbitParticle(player);
                        SpawnRisingTrail(player);
                    }
                    break;
            }
        }


        private void SpawnTorsoBurst(WalkerObject player, Vector3[] offsets)
        {
            Matrix[] bones = player.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            Matrix rootBone = bones[0];
            Matrix playerWorld = player.WorldPosition;

            for (int i = 0; i < offsets.Length; i++)
            {
                int slot = FindTorsoParticleSlot();
                ref TorsoParticle particle = ref _torsoParticles[slot];

                // CreateParticle applies the (+-10, +-10, +-20) jitter twice: once for every
                // BITMAP_WATERFALL_2 and again in the subtype 3 branch.
                Vector3 local = offsets[i] + new Vector3(
                    RandomRange(-10f, 10f) + RandomRange(-10f, 10f),
                    RandomRange(-10f, 10f) + RandomRange(-10f, 10f),
                    RandomRange(-20f, 20f) + RandomRange(-20f, 20f));
                Vector3 modelPosition = Vector3.Transform(local, rootBone);

                particle.Position = Vector3.Transform(modelPosition, playerWorld);
                // CreateParticle: Velocity[2] = 2, Scale = (rand() % 3 + 3) * 0.18.
                particle.VelocityPerTick = new Vector3(0f, 0f, 2f);
                particle.DriftXPerTick = 0f;
                particle.DriftYPerTick = 0f;
                particle.Color = _torsoColor;
                particle.Scale = (MuGame.Random.Next(3) + 3) * 0.18f;
                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                particle.LifeTicks = 12;
                particle.MaxLifeTicks = 12;
            }
        }

        private void UpdateTorsoParticles()
        {
            for (int i = 0; i < _torsoParticles.Length; i++)
            {
                ref TorsoParticle particle = ref _torsoParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                particle.LifeTicks--;
                if (particle.LifeTicks <= 0)
                    continue;

                // MoveParticles: x/y velocity re-rolled every tick, z += 0.1 - 0.02,
                // scale += 0.03 - 0.038, light /= 1.1 during the last ten ticks.
                particle.VelocityPerTick.X = (MuGame.Random.Next(20) - 10) * 0.1f;
                particle.VelocityPerTick.Y = (MuGame.Random.Next(20) - 10) * 0.1f;
                particle.VelocityPerTick.Z += 0.08f;
                particle.Position += particle.VelocityPerTick;
                particle.Scale = MathF.Max(0f, particle.Scale - 0.008f);
                if (particle.LifeTicks < 10)
                    particle.Color /= 1.1f;
                particle.Color /= 1.02f;
            }
        }

        private void SpawnOrbitParticle(WalkerObject player)
        {
            int slot = FindOrbitParticleSlot();
            ref OrbitParticle particle = ref _orbitParticles[slot];
            // count = (Velocity[0] + LifeTime) * 0.1: the phase always decreases, around the
            // position where the particle was created (StartPosition), starting at the feet.
            particle.Phase = (MuGame.Random.Next(300) - 150 + 60) * 0.1f;
            particle.Direction = -1f;
            particle.Radius = 40f;
            particle.Center = player.WorldPosition.Translation;
            particle.Height = 0f;
            particle.VerticalSpeedPerTick = MuGame.Random.Next(100) / 100f * 4f + 1f;
            particle.Scale = 0.19f + MuGame.Random.Next(6) * 0.01f;
            particle.Color = Vector3.One;
            particle.LifeTicks = 60;
            particle.MaxLifeTicks = 60;
        }

        private void UpdateOrbitParticles(bool ownerMoving)
        {
            for (int i = 0; i < _orbitParticles.Length; i++)
            {
                ref OrbitParticle particle = ref _orbitParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                // A walking owner turns the particle into subtype 1: it stops where it is and
                // LifeTime = min(20, LifeTime).
                if (ownerMoving && particle.Direction != 0f)
                {
                    particle.Direction = 0f;
                    particle.VerticalSpeedPerTick = 0f;
                    particle.LifeTicks = Math.Min(20, particle.LifeTicks);
                }

                if (particle.Direction != 0f)
                {
                    particle.Phase += particle.Direction * 0.1f;
                    particle.Height += particle.VerticalSpeedPerTick;
                    particle.Scale = MathF.Max(0.04f, particle.Scale - 0.002f);
                }

                // Light /= 1.1 per tick during the last 20 ticks.
                if (particle.LifeTicks <= 20)
                    particle.Color /= 1.1f;

                particle.LifeTicks--;
            }
        }

        private void SpawnClassicTrail(WalkerObject player)
        {
            OrbitTrail trail = GetTrailSlot();
            // CreateJoint(BITMAP_FLARE, ..., 0, o): Scale 10, Velocity 40, Direction[2] 0..1.5,
            // count = (Direction[1] + LifeTime) * 0.1, Light = character light.
            trail.Activate(
                TrailKind.Classic,
                player,
                Vector3.One,
                direction: -1,
                lifeTicks: 100,
                tailLimit: 20,
                phase: (MuGame.Random.Next(500) - 250 + 100) * 0.1f,
                radius: 40f,
                width: 10f,
                verticalSpeed: MuGame.Random.Next(150) / 100f,
                seed: NextSeed());
        }

        private void SpawnRisingTrail(WalkerObject player)
        {
            OrbitTrail trail = GetTrailSlot();
            // CreateJoint(BITMAP_FLARE + 1, ..., 7, o, 20, 40, 1): LifeTime 40, Scale 20, MaxTails 16,
            // white, starts on a random point of a 40 unit circle and climbs with Velocity 10.
            float angle = MuGame.Random.Next(360);
            trail.ActivateRising(
                player,
                player.WorldPosition.Translation + new Vector3(MathF.Cos(angle) * 40f, -MathF.Sin(angle) * 40f, 0f),
                seed: NextSeed());
        }

        private void SpawnExpandingTrail(WalkerObject player, int direction)
        {
            OrbitTrail trail = GetTrailSlot();
            // CreateJoint(BITMAP_FLARE, ..., 18, o, 20): Scale 20 (> 10) -> LifeTime 50,
            // Direction[2] = (rand() % 250 + 200) / 100, count = (Direction[1] + LifeTime * 2) * 0.1.
            trail.Activate(
                TrailKind.Expanding,
                player,
                Vector3.One,
                direction,
                lifeTicks: 50,
                tailLimit: 20,
                phase: (MuGame.Random.Next(500) - 250 + 100) * 0.1f,
                radius: 40f,
                width: 20f,
                verticalSpeed: (MuGame.Random.Next(250) + 200) / 100f,
                seed: NextSeed());
        }

        private void UpdateTrails(WalkerObject player)
        {
            for (int i = 0; i < _trails.Length; i++)
            {
                OrbitTrail trail = _trails[i];
                if (!trail.Active)
                    continue;

                trail.Tick(player, player.IsMoving || player.MovementIntent);
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (ItemEffectSystem.TryDeferDraw(this))
                return;
            RenderEffect();
        }

        /// <summary>Draws the aura (into the world batch when it is open; called by <see cref="ItemEffectSystem"/>).</summary>
        internal void RenderEffect()
        {
            if (Status == GameControlStatus.Disposed || !Visible || _activeSetIndex < 0 || Parent is not WalkerObject player)
                return;

            EnsureTransientBuffers();
            EnsureTexturePreparationStarted();
            if (_texturePreparationTask is { IsCompleted: false })
                return;

            EnsureFallbackTextures();

            float parentAlpha = MathHelper.Clamp(player.TotalAlpha, 0f, 1f);
            if (parentAlpha <= 0.05f)
                return;

            if (s_worldBatchOpen)
            {
                // Collected with the other auras of the frame and drawn once (see EndWorldBatch).
                DrawSoftLights(player, parentAlpha, _renderInterpolation);
                DrawOrbitFlares(player, parentAlpha, _renderInterpolation);
                DrawTrails(parentAlpha, player, _renderInterpolation, expanding: false);
                DrawTrails(parentAlpha, player, _renderInterpolation, expanding: true);
                return;
            }

            EnsureRenderEffect();
            BlendState previousBlend = GraphicsDevice.BlendState;
            DepthStencilState previousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = GraphicsDevice.RasterizerState;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.World = Matrix.Identity;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;

                DrawSoftLights(player, parentAlpha, _renderInterpolation);
                DrawOrbitFlares(player, parentAlpha, _renderInterpolation);
                DrawTrails(parentAlpha, player, _renderInterpolation, expanding: false);
                GraphicsDevice.BlendState = InverseSourceAdditive;
                DrawTrails(parentAlpha, player, _renderInterpolation, expanding: true);
            }
            finally
            {
                GraphicsDevice.BlendState = previousBlend;
                GraphicsDevice.DepthStencilState = previousDepth;
                GraphicsDevice.RasterizerState = previousRasterizer;
                GraphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        private void DrawSoftLights(WalkerObject player, float parentAlpha, float interpolation)
        {
            if (_lightTexture == null || _lightTexture.IsDisposed)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            int quadCount = 0;

            // The original suppresses these six lights while cloaked. Alpha is the reliable
            // representation available in this client for the same visual state.
            if (parentAlpha >= 0.8f)
            {
                Matrix[] bones = player.GetBoneTransforms();
                if (bones != null)
                {
                    // SourceMain draws these BITMAP_LIGHT sprites every frame at constant brightness.
                    Vector3 boneLightColor = _auraColor * (0.5f * parentAlpha);
                    var gear = player as IEquippedCharacter;
                    int leftLinkBone = gear?.Weapon1 != null && gear.Weapon1.ParentBoneLink >= 0
                        ? gear.Weapon1.ParentBoneLink
                        : PlayerObject.LeftHandBoneIndex;
                    int rightLinkBone = gear?.Weapon2 != null && gear.Weapon2.ParentBoneLink >= 0
                        ? gear.Weapon2.ParentBoneLink
                        : PlayerObject.RightHandBoneIndex;

                    AppendBoneLightGroup(
                        player,
                        bones,
                        leftLinkBone,
                        boneLightColor,
                        cameraRight,
                        cameraUp,
                        ref quadCount);
                    AppendBoneLightGroup(
                        player,
                        bones,
                        rightLinkBone,
                        boneLightColor,
                        cameraRight,
                        cameraUp,
                        ref quadCount);
                }
            }

            // Bone lights first; the torso particles use a different texture (cra_04).
            DrawBillboardBatch(_lightTexture, quadCount);
            quadCount = 0;
            Texture2D torsoTexture = _torsoTexture != null && !_torsoTexture.IsDisposed ? _torsoTexture : _lightTexture;

            for (int i = 0; i < _torsoParticles.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref TorsoParticle particle = ref _torsoParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                float intensity = parentAlpha;
                Vector3 predictedPosition = particle.Position + particle.VelocityPerTick * interpolation;
                float predictedScale = MathF.Max(0f, particle.Scale - 0.008f * interpolation);
                float predictedRotation = particle.Rotation;
                // RenderSprite: width = Bitmap->Width * Scale.
                float halfSize = torsoTexture.Width * 0.5f * predictedScale;
                float cosine = MathF.Cos(predictedRotation);
                float sine = MathF.Sin(predictedRotation);
                Vector3 rotatedRight = cameraRight * cosine + cameraUp * sine;
                Vector3 rotatedUp = cameraUp * cosine - cameraRight * sine;
                WriteBillboardQuad(
                    quadCount++,
                    predictedPosition,
                    rotatedRight * halfSize,
                    rotatedUp * halfSize,
                    ToColor(particle.Color * intensity));
            }

            DrawBillboardBatch(torsoTexture, quadCount);
        }

        private void AppendBoneLightGroup(
            WalkerObject player,
            Matrix[] bones,
            int linkBone,
            Vector3 color,
            Vector3 cameraRight,
            Vector3 cameraUp,
            ref int quadCount)
        {
            for (int offset = 0; offset < 3 && quadCount < MaxBillboardQuads; offset++)
            {
                int boneIndex = offset switch
                {
                    0 => linkBone,
                    1 => linkBone - 6,
                    _ => linkBone - 7
                };

                if (boneIndex < 0 || boneIndex >= bones.Length)
                    continue;

                Vector3 worldPosition = Vector3.Transform(
                    bones[boneIndex].Translation,
                    player.WorldPosition);
                // CreateSprite(BITMAP_LIGHT, Position, 1.3f, ...): width = 64 * 1.3.
                float halfSize = (_lightTexture?.Width ?? 64) * 1.3f * 0.5f;
                WriteBillboardQuad(
                    quadCount++,
                    worldPosition,
                    cameraRight * halfSize,
                    cameraUp * halfSize,
                    ToColor(color));
            }
        }

        private void DrawOrbitFlares(WalkerObject player, float parentAlpha, float interpolation)
        {
            if (_flareTexture == null || _flareTexture.IsDisposed)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            int quadCount = 0;

            for (int i = 0; i < _orbitParticles.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref OrbitParticle particle = ref _orbitParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                float predictedPhase = particle.Phase + particle.Direction * 0.1f * interpolation;
                float predictedHeight = particle.Height + particle.VerticalSpeedPerTick * interpolation;
                Vector3 position = particle.Center + new Vector3(
                    MathF.Sin(predictedPhase) * particle.Radius,
                    -MathF.Cos(predictedPhase) * particle.Radius,
                    predictedHeight);
                float intensity = parentAlpha;
                float predictedScale = particle.Direction != 0f
                    ? MathF.Max(0f, particle.Scale - 0.002f * interpolation)
                    : particle.Scale;
                // RenderSprite: width = Bitmap->Width * Scale.
                float halfSize = _flareTexture.Width * 0.5f * predictedScale;
                WriteBillboardQuad(
                    quadCount++,
                    position,
                    cameraRight * halfSize,
                    cameraUp * halfSize,
                    ToColor(particle.Color * intensity));
            }

            DrawBillboardBatch(_flareTexture, quadCount);
        }

        private void DrawTrails(float parentAlpha, WalkerObject player, float interpolation, bool expanding)
        {
            if (_flareTexture == null || _flareTexture.IsDisposed)
                return;

            int vertexCount = 0;
            int indexCount = 0;
            Vector3 cameraPosition = Camera.Instance.Position;
            Vector3 cameraRight = Camera.Instance.Right;

            for (int trailIndex = 0; trailIndex < _trails.Length; trailIndex++)
            {
                OrbitTrail trail = _trails[trailIndex];
                if (!trail.Active || trail.TailCount < 2 || (trail.Kind != TrailKind.Classic) != expanding)
                    continue;

                int renderPointCount = Math.Min(
                    MaxTrailRenderPoints,
                    trail.TailCount + (interpolation > 0.001f && !trail.Frozen ? 1 : 0));
                if (renderPointCount < 2)
                    continue;

                int baseVertex = vertexCount;
                for (int pointIndex = 0; pointIndex < renderPointCount; pointIndex++)
                {
                    Vector3 current = trail.GetRenderPoint(player, pointIndex, interpolation);
                    Vector3 tangent;
                    if (pointIndex == 0)
                        tangent = trail.GetRenderPoint(player, 1, interpolation) - current;
                    else if (pointIndex == renderPointCount - 1)
                        tangent = current - trail.GetRenderPoint(player, pointIndex - 1, interpolation);
                    else
                        tangent = trail.GetRenderPoint(player, pointIndex + 1, interpolation) -
                                  trail.GetRenderPoint(player, pointIndex - 1, interpolation);

                    Vector3 view = cameraPosition - current;
                    Vector3 side = Vector3.Cross(tangent, view);
                    if (side.LengthSquared() < 0.0001f)
                        side = cameraRight;
                    else
                        side.Normalize();

                    float progress = pointIndex / (float)(renderPointCount - 1);
                    Vector3 offset = side * (trail.Width * 0.5f);
                    Color color = ToColor(trail.GetRenderColor(interpolation) * parentAlpha);

                    _trailVertices[vertexCount++] = new VertexPositionColorTexture(
                        current - offset,
                        color,
                        new Vector2(progress, 0f));
                    _trailVertices[vertexCount++] = new VertexPositionColorTexture(
                        current + offset,
                        color,
                        new Vector2(progress, 1f));
                }

                for (int segment = 0; segment < renderPointCount - 1; segment++)
                {
                    short left0 = checked((short)(baseVertex + segment * 2));
                    short right0 = checked((short)(left0 + 1));
                    short left1 = checked((short)(left0 + 2));
                    short right1 = checked((short)(left0 + 3));

                    _trailIndices[indexCount++] = left0;
                    _trailIndices[indexCount++] = right0;
                    _trailIndices[indexCount++] = left1;
                    _trailIndices[indexCount++] = right0;
                    _trailIndices[indexCount++] = right1;
                    _trailIndices[indexCount++] = left1;
                }
            }

            if (vertexCount == 0 || indexCount == 0)
                return;

            if (s_worldBatchOpen)
            {
                AppendToWorldBatch(_flareTexture, expanding ? InverseSourceAdditive : Blendings.OneOneAdditive,
                    _trailVertices, vertexCount, _trailIndices, indexCount);
                return;
            }

            _effect.Texture = _flareTexture;
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _trailVertices,
                0,
                vertexCount,
                _trailIndices,
                0,
                indexCount / 3);
        }

        // World batch: like ItemLinkEffect, every aura of the DrawAfter passes appends its quads and trails per
        // texture and blend state, and the world draws them at once afterwards. All aura passes are additive
        // (destination factor One) with read-only depth, so the order between auras does not change the image;
        // one shared effect replaces a BasicEffect, its shader switch and up to five draws per aura.
        private sealed class WorldBatchBucket
        {
            public Texture2D Texture;
            public BlendState Blend;
            public VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[1024];
            public int[] Indices = new int[1536];
            public int VertexCount;
            public int IndexCount;
        }

        private static bool s_worldBatchOpen;
        private static readonly List<WorldBatchBucket> s_worldBatchBuckets = new();
        private static BasicEffect s_worldBatchEffect;

        /// <summary>Starts collecting aura geometry instead of drawing it per aura.</summary>
        internal static void BeginWorldBatch() => s_worldBatchOpen = true;

        private static void AppendToWorldBatch(Texture2D texture, BlendState blend,
            VertexPositionColorTexture[] vertices, int vertexCount, short[] indices, int indexCount)
        {
            if (texture == null || texture.IsDisposed || vertexCount <= 0 || indexCount <= 0)
                return;

            WorldBatchBucket bucket = null;
            for (int i = 0; i < s_worldBatchBuckets.Count; i++)
            {
                var candidate = s_worldBatchBuckets[i];
                if (ReferenceEquals(candidate.Texture, texture) && ReferenceEquals(candidate.Blend, blend))
                {
                    bucket = candidate;
                    break;
                }
            }

            if (bucket == null)
            {
                bucket = new WorldBatchBucket { Texture = texture, Blend = blend };
                s_worldBatchBuckets.Add(bucket);
            }

            if (bucket.Vertices.Length < bucket.VertexCount + vertexCount)
                Array.Resize(ref bucket.Vertices, Math.Max(bucket.VertexCount + vertexCount, bucket.Vertices.Length * 2));
            if (bucket.Indices.Length < bucket.IndexCount + indexCount)
                Array.Resize(ref bucket.Indices, Math.Max(bucket.IndexCount + indexCount, bucket.Indices.Length * 2));

            int baseVertex = bucket.VertexCount;
            Array.Copy(vertices, 0, bucket.Vertices, baseVertex, vertexCount);
            for (int i = 0; i < indexCount; i++)
                bucket.Indices[bucket.IndexCount + i] = baseVertex + indices[i];
            bucket.VertexCount += vertexCount;
            bucket.IndexCount += indexCount;
        }

        /// <summary>Draws and clears the collected aura geometry; called by the world after its DrawAfter passes.</summary>
        internal static void EndWorldBatch(GraphicsDevice gd)
        {
            s_worldBatchOpen = false;
            if (gd == null || s_worldBatchBuckets.Count == 0)
                return;

            BlendState previousBlend = gd.BlendState;
            DepthStencilState previousDepth = gd.DepthStencilState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];
            try
            {
                s_worldBatchEffect ??= new BasicEffect(gd)
                {
                    VertexColorEnabled = true,
                    TextureEnabled = true,
                    LightingEnabled = false,
                    World = Matrix.Identity,
                };
                s_worldBatchEffect.View = Camera.Instance.View;
                s_worldBatchEffect.Projection = Camera.Instance.Projection;
                gd.DepthStencilState = DepthStencilState.DepthRead;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearClamp;

                for (int i = 0; i < s_worldBatchBuckets.Count; i++)
                {
                    var bucket = s_worldBatchBuckets[i];
                    if (bucket.IndexCount == 0 || bucket.Texture.IsDisposed)
                        continue;

                    gd.BlendState = bucket.Blend;
                    s_worldBatchEffect.Texture = bucket.Texture;
                    s_worldBatchEffect.CurrentTechnique.Passes[0].Apply();
                    gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, bucket.Vertices, 0, bucket.VertexCount,
                        bucket.Indices, 0, bucket.IndexCount / 3);
                }
            }
            finally
            {
                // Buckets keep their arrays for the next frame; a bucket whose texture is gone is dropped.
                for (int i = s_worldBatchBuckets.Count - 1; i >= 0; i--)
                {
                    var bucket = s_worldBatchBuckets[i];
                    bucket.VertexCount = 0;
                    bucket.IndexCount = 0;
                    if (bucket.Texture.IsDisposed)
                        s_worldBatchBuckets.RemoveAt(i);
                }

                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
            }
        }

        private void DrawBillboardBatch(Texture2D texture, int quadCount)
        {
            if (quadCount <= 0)
                return;

            if (s_worldBatchOpen)
            {
                AppendToWorldBatch(texture, Blendings.OneOneAdditive, _billboardVertices, quadCount * 4,
                    BillboardIndices, quadCount * 6);
                return;
            }

            _effect.Texture = texture;
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _billboardVertices,
                0,
                quadCount * 4,
                BillboardIndices,
                0,
                quadCount * 2);
        }

        private void WriteBillboardQuad(
            int quadIndex,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            Color color)
        {
            int vertex = quadIndex * 4;
            _billboardVertices[vertex] = new VertexPositionColorTexture(
                center - right - up, color, new Vector2(0f, 1f));
            _billboardVertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up, color, new Vector2(1f, 1f));
            _billboardVertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up, color, new Vector2(1f, 0f));
            _billboardVertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up, color, new Vector2(0f, 0f));
        }

        private OrbitTrail GetTrailSlot()
        {
            OrbitTrail oldest = _trails[0];
            int smallestLife = int.MaxValue;

            for (int i = 0; i < _trails.Length; i++)
            {
                if (!_trails[i].Active)
                    return _trails[i];

                if (_trails[i].LifeTicks < smallestLife)
                {
                    smallestLife = _trails[i].LifeTicks;
                    oldest = _trails[i];
                }
            }

            return oldest;
        }

        private int FindTorsoParticleSlot()
        {
            int oldest = 0;
            int smallestLife = int.MaxValue;
            for (int i = 0; i < _torsoParticles.Length; i++)
            {
                if (_torsoParticles[i].LifeTicks <= 0)
                    return i;

                if (_torsoParticles[i].LifeTicks < smallestLife)
                {
                    smallestLife = _torsoParticles[i].LifeTicks;
                    oldest = i;
                }
            }

            return oldest;
        }

        private int FindOrbitParticleSlot()
        {
            int oldest = 0;
            int smallestLife = int.MaxValue;
            for (int i = 0; i < _orbitParticles.Length; i++)
            {
                if (_orbitParticles[i].LifeTicks <= 0)
                    return i;

                if (_orbitParticles[i].LifeTicks < smallestLife)
                {
                    smallestLife = _orbitParticles[i].LifeTicks;
                    oldest = i;
                }
            }

            return oldest;
        }

        private void ResetSetState()
        {
            if (_activeSetIndex >= 0)
                ClearTransientEffects();

            _activeSetIndex = -1;
            _auraTier = 0;
            _hasTorsoParticles = false;
            _primeAuraPending = false;
            _renderInterpolation = 0f;
            Hidden = true;
        }

        private void ClearTransientEffects()
        {
            if (_torsoParticles != null)
                Array.Clear(_torsoParticles, 0, _torsoParticles.Length);
            if (_orbitParticles != null)
                Array.Clear(_orbitParticles, 0, _orbitParticles.Length);
            if (_trails != null)
            {
                for (int i = 0; i < _trails.Length; i++)
                    _trails[i].Reset();
            }

            _legacyAccumulator = 0f;
            _renderInterpolation = 0f;
        }

        // ZzzCharacter.cpp: PartObjectColor(c->BodyPart[BODYPART_BOOTS].Type, Alpha, 0.5f, Light).
        // The 0.5 brightness is applied where the six bone lights are drawn.
        private static Vector3 GetSetAuraColor(int bootsIndex) => ItemVisualRules.GetChromeColor(11, bootsIndex);

        private static bool TryGetTorsoParticleColor(int bootsIndex, out Vector3 color)
        {
            string name = null;
            try
            {
                name = ItemDatabase.GetItemDefinition(11, (short)bootsIndex)?.Name;
            }
            catch
            {
                // The fallback indices below cover the standard Season 5.2 item table.
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                string normalized = name.ToLowerInvariant();
                if (normalized.Contains("dragon knight"))
                {
                    color = new Vector3(0.65f, 0.30f, 0.10f);
                    return true;
                }
                if (normalized.Contains("venom mist"))
                {
                    color = new Vector3(0.10f, 0.10f, 0.90f);
                    return true;
                }
                if (normalized.Contains("sylphid ray") || normalized.Contains("sylpid ray"))
                {
                    color = new Vector3(0.00f, 0.32f, 0.24f);
                    return true;
                }
                if (normalized.Contains("volcano"))
                {
                    color = new Vector3(0.50f, 0.24f, 0.80f);
                    return true;
                }
                if (normalized.Contains("sunlight"))
                {
                    color = new Vector3(0.60f, 0.40f, 0.00f);
                    return true;
                }
                if (normalized.Contains("aura"))
                {
                    color = new Vector3(0.60f, 0.30f, 0.40f);
                    return true;
                }
            }

            color = bootsIndex switch
            {
                29 => new Vector3(0.65f, 0.30f, 0.10f),
                30 => new Vector3(0.10f, 0.10f, 0.90f),
                31 => new Vector3(0.00f, 0.32f, 0.24f),
                32 => new Vector3(0.50f, 0.24f, 0.80f),
                33 => new Vector3(0.60f, 0.40f, 0.00f),
                _ => Vector3.Zero
            };
            return color != Vector3.Zero;
        }

        private static bool IsMagicGladiatorClass(CharacterClassNumber cls) =>
            cls == CharacterClassNumber.MagicGladiator ||
            cls == CharacterClassNumber.DuelMaster;

        private static bool IsRageFighterClass(CharacterClassNumber cls) =>
            cls == CharacterClassNumber.RageFighter ||
            cls == CharacterClassNumber.FistMaster;

        private static bool IsChaosCastleMap(short worldIndex) =>
            (worldIndex >= 18 && worldIndex <= 23) || worldIndex == 53 || worldIndex == 97;

        private uint NextSeed()
        {
            _spawnSequence++;
            return unchecked((uint)MuGame.Random.Next() ^ (_spawnSequence * 747796405u));
        }

        private static float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        private static Color ToColor(Vector3 value) => new(
            MathHelper.Clamp(value.X, 0f, 1f),
            MathHelper.Clamp(value.Y, 0f, 1f),
            MathHelper.Clamp(value.Z, 0f, 1f),
            1f);

        private static async Task<Texture2D> PrepareTexture(string path)
        {
            try
            {
                return await TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D CreateRadialTexture(GraphicsDevice graphicsDevice, int size, float exponent)
        {
            var texture = new Texture2D(graphicsDevice, size, size);
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float intensity = MathF.Pow(MathHelper.Clamp(1f - distance, 0f, 1f), exponent);
                    pixels[y * size + x] = new Color(intensity, intensity, intensity, intensity);
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        public override void Dispose()
        {
            _disposed = true;
            _effect?.Dispose();
            _effect = null;
            if (_ownsLightTexture)
                _lightTexture?.Dispose();
            if (_ownsFlareTexture)
                _flareTexture?.Dispose();
            _lightTexture = null;
            _flareTexture = null;
            base.Dispose();
        }

        private struct TorsoParticle
        {
            public Vector3 Position;
            public Vector3 VelocityPerTick;
            public Vector3 Color;
            public float DriftXPerTick;
            public float DriftYPerTick;
            public float Scale;
            public float Rotation;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private struct OrbitParticle
        {
            public Vector3 Center;
            public Vector3 Color;
            public float Phase;
            public float Direction;
            public float Radius;
            public float Height;
            public float VerticalSpeedPerTick;
            public float Scale;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private enum TrailKind : byte
        {
            Classic,   // BITMAP_FLARE subtype 0
            Expanding, // BITMAP_FLARE subtype 18
            Rising     // BITMAP_FLARE + 1 subtype 7
        }

        private sealed class OrbitTrail
        {
            private readonly Vector3[] _tails = new Vector3[MaxTailPoints];
            private int _tailStart;
            private int _tailLimit;
            private float _phase;
            private Vector3 _anchor;
            private float _radius;
            private float _height;
            private float _verticalSpeed;
            private int _direction;
            private uint _seed;
            private float _velocity;
            private float _wiggle;

            public bool Active { get; private set; }
            public TrailKind Kind { get; private set; }
            public Vector3 Color { get; private set; }
            public bool Frozen { get; private set; }
            public float Width { get; private set; }
            public int LifeTicks { get; private set; }
            public int MaxLifeTicks { get; private set; }
            public int TailCount { get; private set; }
            public void Activate(
                TrailKind kind,
                WalkerObject player,
                Vector3 color,
                int direction,
                int lifeTicks,
                int tailLimit,
                float phase,
                float radius,
                float width,
                float verticalSpeed,
                uint seed)
            {
                Kind = kind;
                Color = color;
                _direction = direction >= 0 ? 1 : -1;
                LifeTicks = Math.Max(1, lifeTicks);
                MaxLifeTicks = LifeTicks;
                _tailLimit = Math.Clamp(tailLimit, 2, MaxTailPoints);
                _phase = phase;
                _radius = radius;
                Width = width;
                _verticalSpeed = verticalSpeed;
                _height = 0f;
                _anchor = player.WorldPosition.Translation;
                _seed = seed == 0 ? 1u : seed;
                _tailStart = 0;
                TailCount = 0;
                Frozen = false;
                Active = true;

                Vector3 first = CalculatePosition(player);
                PushTail(first);
            }

            public void ActivateRising(WalkerObject player, Vector3 start, uint seed)
            {
                Kind = TrailKind.Rising;
                Color = Vector3.One;
                _direction = 1;
                LifeTicks = 40;
                MaxLifeTicks = 40;
                _tailLimit = Math.Min(16, MaxTailPoints);
                _phase = 0f;
                _radius = 0f;
                Width = 20f;
                _verticalSpeed = 0f;
                _velocity = 10f;
                _wiggle = 15f;
                _height = 0f;
                _anchor = start;
                _seed = seed == 0 ? 1u : seed;
                _tailStart = 0;
                TailCount = 0;
                Frozen = false;
                Active = true;
                PushTail(CalculatePosition(player));
            }

            private bool TickRising(WalkerObject player)
            {
                // Position.x = Start.x + sin(LifeTime / 2.5) * Direction[0]; z += Velocity;
                // Velocity -= 0.1; Direction[0] -= 0.1; Light /= 1.2 below 15 ticks.
                _height += _velocity;
                _velocity -= 0.1f;
                _wiggle -= 0.1f;
                if (LifeTicks < 15)
                    Color /= 1.2f;
                PushTail(CalculatePosition(player));
                LifeTicks--;
                if (LifeTicks < 0)
                {
                    Active = false;
                    return true;
                }
                return false;
            }

            public bool Tick(WalkerObject player, bool ownerMoving)
            {
                if (!Active)
                    return false;

                if (Kind == TrailKind.Rising)
                    return TickRising(player);

                // MoveJoint: a moving owner turns the joint into subtype 1 - no new tails,
                // LifeTime = min(10, LifeTime), so the ribbon stops where it is and fades out.
                if (!Frozen && ownerMoving && Width < 40f)
                {
                    Frozen = true;
                    LifeTicks = Math.Min(10, LifeTicks);
                }

                // Light /= 1.3 per tick during the last 10 ticks.
                if (LifeTicks < 10)
                    Color /= 1.3f;

                LifeTicks--;
                if (LifeTicks < 0)
                {
                    Active = false;
                    return true;
                }

                if (Frozen)
                    return false;

                switch (Kind)
                {
                    case TrailKind.Classic:
                        _phase += _direction * 0.1f;
                        _height += _verticalSpeed;
                        break;

                    case TrailKind.Expanding:
                        _phase += _direction * 0.2f;
                        _radius += 0.1f;
                        Width += 0.2f;
                        _height += _verticalSpeed * 1.1f;
                        break;
                }

                PushTail(CalculatePosition(player));
                return false;
            }

            private Vector3 CalculatePosition(WalkerObject player, float tickFraction = 0f)
            {
                if (Kind == TrailKind.Rising)
                {
                    // The last tail was pushed with LifeTime + 1 (before the decrement).
                    float life = LifeTicks + 1 - tickFraction;
                    return _anchor + new Vector3(
                        MathF.Sin(life / 2.5f) * (_wiggle - 0.1f * tickFraction),
                        0f,
                        _height + _velocity * tickFraction);
                }

                // TargetPosition is copied once at creation: the spiral stays where it started.
                Vector3 center = _anchor;
                float phaseSpeed = Kind == TrailKind.Expanding ? 0.2f : 0.1f;
                float phase = _phase + _direction * phaseSpeed * tickFraction;
                float radius = _radius + (Kind == TrailKind.Expanding ? 0.1f * tickFraction : 0f);
                float verticalMultiplier = Kind == TrailKind.Expanding ? 1.1f : 1f;
                float height = _height + _verticalSpeed * verticalMultiplier * tickFraction;
                return center + new Vector3(
                    MathF.Cos(phase) * radius,
                    -MathF.Sin(phase) * radius,
                    height);
            }

            public Vector3 GetRenderPoint(WalkerObject player, int index, float interpolation)
            {
                // A frozen ribbon gets no predicted head point (DrawTrails does not count one).
                if (interpolation > 0.001f && !Frozen)
                {
                    if (index == 0)
                        return CalculatePosition(player, interpolation);
                    index--;
                }

                return GetTailPoint(Math.Clamp(index, 0, TailCount - 1));
            }

            /// <summary>Light interpolated toward the next tick's value (smooth fade between ticks).</summary>
            public Vector3 GetRenderColor(float interpolation) =>
                Kind == TrailKind.Rising
                    ? (LifeTicks < 15 ? Color * MathHelper.Lerp(1f, 1f / 1.2f, MathHelper.Clamp(interpolation, 0f, 1f)) : Color)
                    : LifeTicks < 10 ? Color * MathHelper.Lerp(1f, 1f / 1.3f, MathHelper.Clamp(interpolation, 0f, 1f)) : Color;

            public float GetRenderLifeRatio(float interpolation) =>
                MaxLifeTicks > 0
                    ? MathHelper.Clamp((LifeTicks - interpolation) / MaxLifeTicks, 0f, 1f)
                    : 0f;

            // Ring buffer of _tailLimit points. The modulus must be the limit, not the array size:
            // with a smaller limit (Rising: 16) wrapping by 20 left stale (0,0,0) points in the
            // window and stretched the ribbon to the world origin.
            private void PushTail(Vector3 position)
            {
                if (TailCount < _tailLimit)
                {
                    int index = (_tailStart + TailCount) % _tailLimit;
                    _tails[index] = position;
                    TailCount++;
                    return;
                }

                _tails[_tailStart] = position;
                _tailStart = (_tailStart + 1) % _tailLimit;
            }

            public Vector3 GetTailPoint(int index)
            {
                int newestToOldest = TailCount - 1 - index;
                int actual = (_tailStart + newestToOldest) % _tailLimit;
                if (actual < 0)
                    actual += _tailLimit;
                return _tails[actual];
            }

            public void Reset()
            {
                Active = false;
                LifeTicks = 0;
                MaxLifeTicks = 0;
                TailCount = 0;
                _tailStart = 0;
            }

            private static float RandomHeight(uint seed)
            {
                seed = seed * 1664525u + 1013904223u;
                return 24f + (seed & 0xFFFFu) / 65535f * 36f;
            }
        }
    }
}
