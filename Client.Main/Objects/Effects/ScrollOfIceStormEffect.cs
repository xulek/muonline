#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Classic Ice Storm (skill 39): ten independent Blizzard models fall over the selected
    /// terrain tile, emit short icy trails, and create a fading Blizzard impact on contact.
    /// Simulation decisions use the original 25 Hz reference step while rendering remains smooth.
    /// </summary>
    public sealed class ScrollOfIceStormEffect : EffectObject
    {
        private const string DefaultBlizzardPath = "Skill/Blizzard.bmd";
        private const string DefaultIceSmallPath = "Skill/Ice02.bmd";

        private const string SmokeTexturePath = "Effect/smoke01.jpg";
        private const string EnergyTexturePath = "Effect/energy.jpg";
        private const string AlternateTrailTexturePath = "Effect/fire03.jpg";
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";
        private const string LightTexturePath = "Effect/flare01.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 6;
        private const int BlizzardCount = 10;
        private const int MaxParticles = 512;
        private const int MaxBillboardQuads = MaxParticles + BlizzardCount;

        private static readonly Vector3 ImpactColor = new(0.24f, 0.28f, 0.80f);

        private readonly WalkerObject _caster;
        private readonly FallingBlizzardModel?[] _shards = new FallingBlizzardModel?[BlizzardCount];
        private readonly BillboardParticle[] _particles = new BillboardParticle[MaxParticles];
        private readonly VertexPositionColorTexture[] _vertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private static readonly short[] Indices = QuadIndexCache.Get(MaxBillboardQuads);

        private Vector3 _center;
        private string _blizzardPath = DefaultBlizzardPath;
        private string _iceSmallPath = DefaultIceSmallPath;
        private string _startSoundPath = "Sound/eSuddenIce_1.wav";
        private string _impactSoundPath = "Sound/eSuddenIce_2.wav";

        private Texture2D? _smokeTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _alternateTrailTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _lightTexture;

        private int _particleCount;
        private int _activeShardCount;
        private int _activeImpactCount;
        private bool _spawned;
        private bool _removing;

        public ScrollOfIceStormEffect(WalkerObject caster, Vector3 center)
        {
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _center = center;
            Position = center;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-520f, -420f, -120f),
                new Vector3(520f, 420f, 1180f));

        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _blizzardPath = await ResolveModelPath(
                DefaultBlizzardPath,
                "Skill/blizzard.bmd",
                "Skill/Blizzard01.bmd",
                "Skill/Blizzard1.bmd");
            _iceSmallPath = await ResolveModelPath(
                DefaultIceSmallPath,
                "Skill/Ice2.bmd",
                "Skill/ice02.bmd");

            _smokeTexture = await PrepareFirstTexture(
                SmokeTexturePath,
                "Effect/Smoke01.jpg");
            _energyTexture = await PrepareFirstTexture(
                EnergyTexturePath,
                "Effect/Energy.jpg",
                "Effect/flare.jpg");
            _alternateTrailTexture = await PrepareFirstTexture(
                AlternateTrailTexturePath,
                "Effect/Fire03.jpg",
                "Effect/firehik03.jpg",
                "Effect/firehik01.jpg");
            _shinyTexture = await PrepareFirstTexture(
                ShinyTexturePath,
                "Effect/shiny02.jpg",
                "Effect/Shiny01.jpg");
            _lightTexture = await PrepareFirstTexture(
                LightTexturePath,
                "Effect/Flare01.jpg");

            _startSoundPath = "Sound/eSuddenIce_1.wav";
            _impactSoundPath = "Sound/eSuddenIce_2.wav";

            await Task.WhenAll(
                SoundController.Instance.PreloadSoundAsync(_startSoundPath),
                SoundController.Instance.PreloadSoundAsync(_impactSoundPath));
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            if (!_spawned)
            {
                if (World == null || _caster.Status == GameControlStatus.Disposed)
                {
                    RemoveSelf();
                    return;
                }

                SpawnBlizzards();
                _spawned = true;
            }

            float elapsedSeconds = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);
            UpdateParticles(elapsedSeconds);

            if (_activeShardCount == 0 && _activeImpactCount == 0 && _particleCount == 0)
                RemoveSelf();
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready)
                return;

            if (_smokeTexture == null &&
                _energyTexture == null &&
                _alternateTrailTexture == null &&
                _shinyTexture == null &&
                _lightTexture == null)
            {
                return;
            }

            GraphicsDevice graphicsDevice = GraphicsDevice;
            BasicEffect? effect = GraphicsManager.Instance.BasicEffect3D;
            if (effect == null)
                return;

            BlendState previousBlend = graphicsDevice.BlendState;
            DepthStencilState previousDepth = graphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = graphicsDevice.RasterizerState;
            SamplerState previousSampler = graphicsDevice.SamplerStates[0];

            bool previousTextureEnabled = effect.TextureEnabled;
            bool previousVertexColorEnabled = effect.VertexColorEnabled;
            bool previousLightingEnabled = effect.LightingEnabled;
            bool previousFogEnabled = effect.FogEnabled;
            Vector3 previousDiffuseColor = effect.DiffuseColor;
            float previousAlpha = effect.Alpha;
            Texture2D? previousTexture = effect.Texture;
            Matrix previousWorld = effect.World;
            Matrix previousView = effect.View;
            Matrix previousProjection = effect.Projection;

            try
            {
                graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                graphicsDevice.RasterizerState = RasterizerState.CullNone;
                graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                effect.TextureEnabled = true;
                effect.VertexColorEnabled = true;
                effect.LightingEnabled = false;
                effect.FogEnabled = false;
                effect.DiffuseColor = Vector3.One;
                effect.Alpha = 1f;
                effect.World = Matrix.Identity;
                effect.View = Camera.Instance.View;
                effect.Projection = Camera.Instance.Projection;

                if (_smokeTexture != null)
                {
                    // MU smoke textures are JPEG/OZJ assets with a black background.
                    // The original EnableAlphaBlend path uses ONE + ONE so black texels
                    // contribute zero instead of becoming an opaque dark rectangle.
                    DrawParticleKind(effect, ParticleKind.Smoke, _smokeTexture, Blendings.OneOneAdditive);
                    DrawParticleKind(effect, ParticleKind.ImpactSmoke, _smokeTexture, Blendings.OneOneAdditive);
                }

                if (_energyTexture != null)
                    DrawParticleKind(effect, ParticleKind.Energy, _energyTexture, Blendings.OneOneAdditive);

                if (_alternateTrailTexture != null)
                    DrawParticleKind(effect, ParticleKind.AlternateTrail, _alternateTrailTexture, Blendings.OneOneAdditive);

                if (_shinyTexture != null)
                    DrawShardCores(effect, _shinyTexture, strongCore: true);

                if (_lightTexture != null)
                    DrawShardCores(effect, _lightTexture, strongCore: false);
            }
            finally
            {
                effect.TextureEnabled = previousTextureEnabled;
                effect.VertexColorEnabled = previousVertexColorEnabled;
                effect.LightingEnabled = previousLightingEnabled;
                effect.FogEnabled = previousFogEnabled;
                effect.DiffuseColor = previousDiffuseColor;
                effect.Alpha = previousAlpha;
                effect.Texture = previousTexture;
                effect.World = previousWorld;
                effect.View = previousView;
                effect.Projection = previousProjection;

                graphicsDevice.BlendState = previousBlend;
                graphicsDevice.DepthStencilState = previousDepth;
                graphicsDevice.RasterizerState = previousRasterizer;
                graphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        private void SpawnBlizzards()
        {
            if (World == null)
                return;

            if (World.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(_center.X, _center.Y);
                _center = new Vector3(_center.X, _center.Y, groundZ);
                Position = _center;
            }

            SoundController.Instance.PlayBuffer(_startSoundPath);
            _activeShardCount = BlizzardCount;

            for (int i = 0; i < BlizzardCount; i++)
            {
                float extraHeight = MuGame.Random.Next(0, 50) * i;
                Vector3 spawnPosition = new(
                    _center.X + MuGame.Random.Next(-150, 150) + 100f,
                    _center.Y + MuGame.Random.Next(-150, 150),
                    _center.Z + 600f + extraHeight);

                var shard = new FallingBlizzardModel(
                    _blizzardPath,
                    this,
                    i,
                    spawnPosition)
                {
                    Position = spawnPosition,
                    Angle = new Vector3(0f, 0f, MuGame.Random.Next(0, 360)),
                    Scale = 0.5f
                };

                _shards[i] = shard;
                World.Objects.Add(shard);
                _ = shard.Load();
            }
        }

        private void EmitTrail(Vector3 position, Vector3 light)
        {
            Vector3 white = Vector3.Lerp(new Vector3(0.72f, 0.82f, 1f), Vector3.One, light.X);

            SpawnParticle(new BillboardParticle
            {
                Kind = ParticleKind.Smoke,
                Position = position,
                Velocity = new Vector3(RandomRange(-8f, 8f), RandomRange(-8f, 8f), RandomRange(18f, 34f)),
                Color = white * 0.62f,
                Age = 0f,
                Life = RandomRange(0.30f, 0.46f),
                StartSize = RandomRange(78f, 96f),
                EndSize = RandomRange(118f, 148f),
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = RandomRange(-1.1f, 1.1f)
            });

            bool alternate = MuGame.Random.Next(2) != 0;
            SpawnParticle(new BillboardParticle
            {
                Kind = alternate ? ParticleKind.AlternateTrail : ParticleKind.Energy,
                Position = position,
                Velocity = new Vector3(RandomRange(-4f, 4f), RandomRange(-4f, 4f), RandomRange(4f, 14f)),
                Color = alternate
                    ? new Vector3(0.48f, 0.68f, 1f) * MathF.Max(0.35f, light.X)
                    : white * MathF.Max(0.45f, light.X),
                Age = 0f,
                Life = RandomRange(0.14f, 0.24f),
                StartSize = RandomRange(36f, 52f),
                EndSize = RandomRange(15f, 25f),
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = RandomRange(-3.2f, 3.2f)
            });
        }

        private void HandleImpact(int shardIndex, Vector3 fallingPosition, float terrainHeight)
        {
            if (World == null)
                return;

            Vector3 impactSmokePosition = fallingPosition + new Vector3(0f, 0f, 50f);
            float impactScale = MuGame.Random.Next(80, 112) * 0.025f;
            SpawnParticle(new BillboardParticle
            {
                Kind = ParticleKind.ImpactSmoke,
                Position = impactSmokePosition,
                Velocity = new Vector3(RandomRange(-14f, 14f), RandomRange(-14f, 14f), RandomRange(28f, 48f)),
                Color = ImpactColor,
                Age = 0f,
                Life = RandomRange(0.44f, 0.62f),
                StartSize = impactScale * 66f,
                EndSize = impactScale * 98f,
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = RandomRange(-0.8f, 0.8f)
            });

            if (MuGame.Random.Next(5) == 0)
            {
                var smallIce = new IceSmallModel(_iceSmallPath)
                {
                    Position = new Vector3(fallingPosition.X, fallingPosition.Y, terrainHeight + 8f),
                    Angle = new Vector3(
                        RandomRange(0f, 360f),
                        RandomRange(0f, 360f),
                        RandomRange(0f, 360f)),
                    Scale = RandomRange(0.34f, 0.58f)
                };

                World.Objects.Add(smallIce);
                _ = smallIce.Load();
            }

            var impact = new BlizzardImpactModel(
                _blizzardPath,
                this,
                playImpactSound: shardIndex == 0,
                _impactSoundPath)
            {
                // Preserve the legacy overshoot: the impact model starts at the falling
                // shard position instead of being snapped exactly to terrain height.
                Position = fallingPosition,
                Angle = Vector3.Zero,
                Scale = 0.5f
            };

            _activeImpactCount++;
            World.Objects.Add(impact);
            _ = impact.Load();
        }

        private void NotifyShardEnded(int index)
        {
            if ((uint)index < (uint)_shards.Length)
                _shards[index] = null;

            if (_activeShardCount > 0)
                _activeShardCount--;
        }

        private void NotifyImpactEnded()
        {
            if (_activeImpactCount > 0)
                _activeImpactCount--;
        }

        private void UpdateParticles(float elapsedSeconds)
        {
            int index = 0;
            while (index < _particleCount)
            {
                ref BillboardParticle particle = ref _particles[index];
                particle.Age += elapsedSeconds;
                if (particle.Age >= particle.Life)
                {
                    _particles[index] = _particles[--_particleCount];
                    continue;
                }

                particle.Position += particle.Velocity * elapsedSeconds;
                particle.Velocity *= MathF.Pow(0.88f, elapsedSeconds * 25f);
                particle.Rotation += particle.RotationSpeed * elapsedSeconds;
                index++;
            }
        }

        private void SpawnParticle(in BillboardParticle particle)
        {
            if (_particleCount >= _particles.Length)
                return;

            _particles[_particleCount++] = particle;
        }

        private void DrawParticleKind(
            BasicEffect effect,
            ParticleKind kind,
            Texture2D texture,
            BlendState blendState)
        {
            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            int quadCount = 0;

            for (int i = 0; i < _particleCount && quadCount < MaxBillboardQuads; i++)
            {
                ref BillboardParticle particle = ref _particles[i];
                if (particle.Kind != kind)
                    continue;

                float progress = MathHelper.Clamp(particle.Age / particle.Life, 0f, 1f);
                float fadeIn = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(progress / 0.16f, 0f, 1f));
                float fadeOut = 1f - MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp((progress - 0.38f) / 0.62f, 0f, 1f));
                float intensity = fadeIn * fadeOut;
                if (intensity <= 0.003f)
                    continue;

                float size = MathHelper.Lerp(particle.StartSize, particle.EndSize, progress);
                float cosine = MathF.Cos(particle.Rotation);
                float sine = MathF.Sin(particle.Rotation);
                Vector3 rotatedRight = cameraRight * cosine + cameraUp * sine;
                Vector3 rotatedUp = cameraUp * cosine - cameraRight * sine;

                // All Ice Storm billboard layers use additive RGB blending. The fade must
                // therefore be encoded in RGB; alpha is intentionally not used by ONE + ONE.
                Vector3 rgb = particle.Color * intensity;
                Color color = ToAdditiveColor(rgb);

                WriteBillboardQuad(
                    quadCount++,
                    particle.Position,
                    rotatedRight * (size * 0.5f),
                    rotatedUp * (size * 0.5f),
                    color);
            }

            DrawBillboardBatch(effect, texture, blendState, quadCount);
        }

        private void DrawShardCores(BasicEffect effect, Texture2D texture, bool strongCore)
        {
            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            int quadCount = 0;

            for (int i = 0; i < _shards.Length && quadCount < MaxBillboardQuads; i++)
            {
                FallingBlizzardModel? shard = _shards[i];
                if (shard == null || !shard.IsVisuallyActive)
                    continue;

                float intensity = MathHelper.Clamp(shard.VisualLight.X, 0f, 1f);
                if (intensity <= 0.01f)
                    continue;

                float size = strongCore ? shard.ShinySize : 74f;
                float rotation = shard.CoreRotation + (strongCore ? 0f : MathHelper.PiOver4);
                float cosine = MathF.Cos(rotation);
                float sine = MathF.Sin(rotation);
                Vector3 rotatedRight = cameraRight * cosine + cameraUp * sine;
                Vector3 rotatedUp = cameraUp * cosine - cameraRight * sine;
                Vector3 color = strongCore
                    ? Vector3.One * (0.88f * intensity)
                    : new Vector3(0.52f, 0.68f, 1f) * (0.56f * intensity);

                WriteBillboardQuad(
                    quadCount++,
                    shard.VisualPosition,
                    rotatedRight * (size * 0.5f),
                    rotatedUp * (size * 0.5f),
                    ToAdditiveColor(color));
            }

            DrawBillboardBatch(effect, texture, Blendings.OneOneAdditive, quadCount);
        }

        private void DrawBillboardBatch(
            BasicEffect effect,
            Texture2D texture,
            BlendState blendState,
            int quadCount)
        {
            if (quadCount <= 0)
                return;

            GraphicsDevice.BlendState = blendState;
            effect.Texture = texture;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    quadCount * 4,
                    Indices,
                    0,
                    quadCount * 2);
            }
        }

        private void WriteBillboardQuad(
            int quadIndex,
            Vector3 position,
            Vector3 right,
            Vector3 up,
            Color color)
        {
            int vertexIndex = quadIndex * 4;
            _vertices[vertexIndex] = new VertexPositionColorTexture(
                position - right - up,
                color,
                new Vector2(0f, 1f));
            _vertices[vertexIndex + 1] = new VertexPositionColorTexture(
                position + right - up,
                color,
                new Vector2(1f, 1f));
            _vertices[vertexIndex + 2] = new VertexPositionColorTexture(
                position + right + up,
                color,
                new Vector2(1f, 0f));
            _vertices[vertexIndex + 3] = new VertexPositionColorTexture(
                position - right + up,
                color,
                new Vector2(0f, 0f));
        }



        private static async Task<Texture2D?> PrepareFirstTexture(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Texture2D? texture = await TextureLoader.Instance.PrepareAndGetTexture(candidates[i]);
                    if (texture != null)
                        return texture;
                }
                catch
                {
                    // Try the next known filename variant. A missing optional layer must
                    // be skipped rather than replaced with a stretched one-pixel quad.
                }
            }

            return null;
        }

        private static async Task<string> ResolveModelPath(string primary, params string[] candidates)
        {
            if (await BMDLoader.Instance.AssestExist(primary))
                return primary;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(candidates[i]))
                    return candidates[i];
            }

            return primary;
        }

        private static Color ToAdditiveColor(Vector3 value) => new(
            MathHelper.Clamp(value.X, 0f, 1f),
            MathHelper.Clamp(value.Y, 0f, 1f),
            MathHelper.Clamp(value.Z, 0f, 1f),
            1f);

        private static float RandomRange(float minimum, float maximum) =>
            minimum + (float)MuGame.Random.NextDouble() * (maximum - minimum);

        private void RemoveSelf()
        {
            if (_removing)
                return;

            _removing = true;
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        private enum ParticleKind : byte
        {
            Smoke,
            Energy,
            AlternateTrail,
            ImpactSmoke
        }

        private struct BillboardParticle
        {
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Color;
            public float Age;
            public float Life;
            public float StartSize;
            public float EndSize;
            public float Rotation;
            public float RotationSpeed;
        }

        private sealed class FallingBlizzardModel : ModelObject
        {
            private readonly string _path;
            private readonly ScrollOfIceStormEffect _parent;
            private readonly int _index;
            private Vector3 _startPosition;
            private Vector3 _previousPosition;
            private Vector3 _simulatedPosition;
            private Vector3 _lightColor;
            private float _gravity;
            private float _legacyAccumulator;
            private int _lifeTicks;
            private bool _active = true;
            private bool _notified;

            public FallingBlizzardModel(
                string path,
                ScrollOfIceStormEffect parent,
                int index,
                Vector3 startPosition)
            {
                _path = path;
                _parent = parent;
                _index = index;
                _startPosition = startPosition;
                _previousPosition = startPosition;
                _simulatedPosition = startPosition;
                _gravity = -MuGame.Random.Next(30, 60);
                _lifeTicks = MuGame.Random.Next(15, 30);

                ShinySize = MuGame.Random.Next(4, 8) * 0.2f * 54f;
                CoreRotation = RandomRange(0f, MathHelper.TwoPi);

                ContinuousAnimation = true;
                AnimationSpeed = 1f;
                LightEnabled = false;
                Light = Vector3.Zero;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
                BlendMesh = -2;
                BlendMeshLight = 0f;
            }

            public bool IsVisuallyActive => _active && Status == GameControlStatus.Ready;
            public Vector3 VisualPosition => Position;
            public Vector3 VisualLight => _lightColor;
            public float ShinySize { get; }
            public float CoreRotation { get; private set; }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_path);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (!_active || Status != GameControlStatus.Ready)
                    return;

                float elapsedSeconds = MathHelper.Clamp(
                    (float)gameTime.ElapsedGameTime.TotalSeconds,
                    0f,
                    LegacyStepSeconds * MaxLegacyStepsPerFrame);
                _legacyAccumulator += elapsedSeconds;

                int steps = 0;
                while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame && _active)
                {
                    TickLegacy();
                    _legacyAccumulator -= LegacyStepSeconds;
                    steps++;
                }

                if (steps == MaxLegacyStepsPerFrame)
                    _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

                float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
                Position = Vector3.Lerp(_previousPosition, _simulatedPosition, interpolation);
                CoreRotation += elapsedSeconds * 2.3f;
            }

            private void TickLegacy()
            {
                _previousPosition = _simulatedPosition;
                _startPosition.X -= 10f;

                _simulatedPosition = new Vector3(
                    _startPosition.X + MathF.Sin(MuGame.Random.Next(0, 1000) * 0.01f) * 10f,
                    _startPosition.Y + MathF.Sin(MuGame.Random.Next(0, 1000) * 0.01f) * 10f,
                    _simulatedPosition.Z + _gravity);

                _gravity -= MuGame.Random.Next(0, 5);
                _lightColor.X = MathF.Min(1f, _lightColor.X + 0.1f);
                _lightColor.Y = _lightColor.X;
                _lightColor.Z = _lightColor.X;
                Light = _lightColor;
                BlendMeshLight = _lightColor.X;

                _parent.EmitTrail(_simulatedPosition, _lightColor);
                _lifeTicks--;

                if (World?.Terrain != null)
                {
                    float terrainHeight = World.Terrain.RequestTerrainHeight(
                        _simulatedPosition.X,
                        _simulatedPosition.Y);
                    if (_simulatedPosition.Z < terrainHeight)
                    {
                        _parent.HandleImpact(_index, _simulatedPosition, terrainHeight);
                        EndEffect();
                        return;
                    }
                }

                if (_lifeTicks <= 0)
                    EndEffect();
            }

            private void EndEffect()
            {
                if (!_active)
                    return;

                _active = false;
                NotifyParent();
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);
                Dispose();
            }

            private void NotifyParent()
            {
                if (_notified)
                    return;

                _notified = true;
                _parent.NotifyShardEnded(_index);
            }

            public override void Dispose()
            {
                NotifyParent();
                base.Dispose();
            }
        }

        private sealed class BlizzardImpactModel : ModelObject
        {
            private readonly string _path;
            private readonly ScrollOfIceStormEffect _parent;
            private readonly bool _playImpactSound;
            private readonly string _impactSoundPath;
            private float _ageSeconds;
            private int _lastWholeTick;
            private bool _soundPlayed;
            private bool _ended;
            private bool _notified;

            public BlizzardImpactModel(
                string path,
                ScrollOfIceStormEffect parent,
                bool playImpactSound,
                string impactSoundPath)
            {
                _path = path;
                _parent = parent;
                _playImpactSound = playImpactSound;
                _impactSoundPath = impactSoundPath;

                ContinuousAnimation = true;
                AnimationSpeed = 1f;
                LightEnabled = false;
                Light = ImpactColor;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
                BlendMesh = -2;
                BlendMeshLight = 1f;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_path);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (_ended || Status != GameControlStatus.Ready)
                    return;

                _ageSeconds += MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, 0.2f);
                float ageTicks = _ageSeconds * 25f;
                int wholeTicks = (int)ageTicks;

                BlendMeshLight = MathF.Pow(1f / 1.1f, ageTicks);

                if (_playImpactSound && !_soundPlayed && _lastWholeTick < 2 && wholeTicks >= 2)
                {
                    SoundController.Instance.PlayBuffer(_impactSoundPath);
                    _soundPlayed = true;
                }

                _lastWholeTick = wholeTicks;
                if (ageTicks >= 20f)
                    EndEffect();
            }

            private void EndEffect()
            {
                if (_ended)
                    return;

                _ended = true;
                NotifyParent();
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);
                Dispose();
            }

            private void NotifyParent()
            {
                if (_notified)
                    return;

                _notified = true;
                _parent.NotifyImpactEnded();
            }

            public override void Dispose()
            {
                NotifyParent();
                base.Dispose();
            }
        }

        private sealed class IceSmallModel : ModelObject
        {
            private readonly string _path;
            private float _ageSeconds;
            private readonly float _lifeSeconds = RandomRange(0.52f, 0.78f);
            private bool _ended;

            public IceSmallModel(string path)
            {
                _path = path;
                ContinuousAnimation = true;
                AnimationSpeed = 1.5f;
                LightEnabled = false;
                Light = new Vector3(0.48f, 0.64f, 1f);
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
                BlendMesh = -2;
                BlendMeshLight = 1f;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_path);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (_ended || Status != GameControlStatus.Ready)
                    return;

                float elapsedSeconds = MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, 0.2f);
                _ageSeconds += elapsedSeconds;
                float remaining = MathHelper.Clamp(1f - _ageSeconds / _lifeSeconds, 0f, 1f);
                BlendMeshLight = remaining;
                Angle += new Vector3(30f, 24f, 38f) * elapsedSeconds;

                if (_ageSeconds >= _lifeSeconds)
                    EndEffect();
            }

            private void EndEffect()
            {
                if (_ended)
                    return;

                _ended = true;
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);
                Dispose();
            }
        }
    }
}
