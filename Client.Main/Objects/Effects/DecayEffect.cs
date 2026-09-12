#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Summoner "Decay" (skill 38, AT_SKILL_BLAST_POISON) port of SourceMain5.2.
    ///
    /// Cast (ZzzCharacter.cpp):
    ///   Position = (SkillX + 0.5, SkillY + 0.5) * TERRAIN_SCALE, Z = RequestTerrainHeight
    ///   Vector(0.8f, 0.5f, 0.1f, Light);
    ///   CreateEffect(MODEL_FIRE, Position, o->Angle, Light, 6, NULL, 0);   // x2
    ///   PlayBuffer(SOUND_DEATH_POISON1);
    ///
    /// MODEL_FIRE subtype 6 (ZzzEffect.cpp) is a *hidden* model (o->HiddenMesh = -2, the
    /// engine skips the whole body) that falls from 500..800 units above the target with
    /// Direction[2] = -(50..100) and a 20 tail BITMAP_FLARE trail
    /// (CreateJoint(BITMAP_SMOKE, ..., 0, o, 100.f)). Each frame MoveEffect adds a bluish
    /// BITMAP_LIGHT and two BITMAP_SHINY + 1 sprites at its position.
    ///
    /// On ground contact (Position[2] &lt; terrain height) it explodes:
    ///   CreateEffect(MODEL_SKILL_INFERNO, pos, Angle(0), Light(0, 0.5, 0), 2, o, 30, 0)
    ///   15x CreateParticleFpsChecked(BITMAP_SMOKE, ..., 11, (rand() % 32 + 80) * 0.025)
    ///   6x  CreateEffectFpsChecked(MODEL_STONE1 + rand() % 2, ...)
    ///   PlayBuffer(SOUND_DEATH_POISON2);  o->Live = false;
    /// </summary>
    public sealed class DecayEffect : EffectObject
    {
        private const string InfernoModelPath = "Skill/inferno01.bmd";
        private const string StoneBaseName = "Stone";
        private const string TrailTexturePath = "Effect/Flare.jpg";     // BITMAP_FLARE (joint ribbon)
        private const string GlowTexturePath = "Effect/flare01.jpg";    // BITMAP_LIGHT (per-frame sprite)
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";   // BITMAP_SHINY + 1
        private const string CastSoundPath = "Sound/eBlastPoison_1.wav";   // SOUND_DEATH_POISON1
        private const string ImpactSoundPath = "Sound/eBlastPoison_2.wav"; // SOUND_DEATH_POISON2

        private const int CoreCount = 2;              // the original calls CreateEffect twice
        private const float CoreLifeFrames = 40f;
        private const float TrailTailCount = 20f;     // CreateJoint(BITMAP_SMOKE, ..., 100.f): MaxTails 20
        private const float TrailTailScale = 100f;
        private const float TrailJointLifeFrames = 20f;  // joint LifeTime 20; fAlpha = min(life, 20) * 0.1
        private const float GlowSpriteScale = 3f;        // CreateSprite(BITMAP_LIGHT, pos, 3.f, (0.5,0.5,1))
        private const float SparkleSmallScale = 3f;      // CreateSprite(BITMAP_SHINY + 1, pos, 3.f, white)
        private const float SparkleLargeScale = 4f;      // CreateSprite(BITMAP_SHINY + 1, pos, 4.f, o->Light)
        private const float ImpactSmokePuffs = 15f;
        private const float ImpactStoneCount = 6f;
        private const float ImpactInfernoScale = 0.3f;   // PKKey 30 -> Scale = 30 / 100
        private const float ImpactRingBrightness = 0.1f; // BlendMeshLight = 0.1 (never modified by MoveEffect)
        private const float ImpactSettleFrames = 60f;
        private const float LegacyFramesPerSecond = 25f;

        /// <summary>HeadAngle = (0, 20, 0) - the joint ribbon's width axis lives in that local frame.</summary>
        private static readonly Vector3 HeadAngleDegrees = new Vector3(0f, 20f, 0f);

        /// <summary>Vector(0.8f, 0.5f, 0.1f, Light) from the ZzzCharacter cast block.</summary>
        private static readonly Vector3 CoreLight = new Vector3(0.8f, 0.5f, 0.1f);

        /// <summary>The cast passes o->Angle as the rain angle too (only X/Z are randomised per puff).</summary>
        private static readonly Vector3 SpawnAngle = new Vector3(0f, MathHelper.ToRadians(20f), 0f);

        private readonly WalkerObject? _caster;
        private readonly Vector3 _target;

        private readonly PoisonCore[] _cores = new PoisonCore[CoreCount];
        private PoisonSmokeParticles? _smoke;
        private string _infernoModelPath = InfernoModelPath;
        private readonly string[] _stonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };

        private Texture2D? _trailTexture;
        private Texture2D? _glowTexture;
        private Texture2D? _shinyTexture;
        private BasicEffect? _billboardEffect;

        private readonly VertexPositionColorTexture[] _vertices =
            new VertexPositionColorTexture[MaxQuads * 4];
        private const int MaxQuads = 256;
        private static readonly short[] QuadIndices = QuadIndexCache.Get(MaxQuads);

        private Vector3 _impactLight = new(0.1f, 0.5f, 0.1f);
        private float _settleFrames = -1f;
        private int _stonesAlive;
        private bool _initialized;
        private bool _disposed;

        public DecayEffect(WalkerObject? caster, Vector3 target)
        {
            _caster = caster;
            _target = target;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-900f, -900f, -100f),
                new Vector3(900f, 900f, 1400f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (!await BMDLoader.Instance.AssestExist(_infernoModelPath))
            {
                const string fallback = "Skill/Inferno01.bmd";
                if (await BMDLoader.Instance.AssestExist(fallback))
                    _infernoModelPath = fallback;
            }

            for (int i = 0; i < _stonePaths.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(_stonePaths[i]))
                    continue;

                string upper = "Skill/" + Path.GetFileName(_stonePaths[i]).Replace("stone", "Stone");
                if (await BMDLoader.Instance.AssestExist(upper))
                    _stonePaths[i] = upper;
            }

            _ = await TextureLoader.Instance.Prepare(TrailTexturePath);
            _ = await TextureLoader.Instance.Prepare(GlowTexturePath);
            _ = await TextureLoader.Instance.Prepare(ShinyTexturePath);
            _trailTexture = TextureLoader.Instance.GetTexture2D(TrailTexturePath) ?? GraphicsManager.Instance.Pixel;
            _glowTexture = TextureLoader.Instance.GetTexture2D(GlowTexturePath) ?? GraphicsManager.Instance.Pixel;
            _shinyTexture = TextureLoader.Instance.GetTexture2D(ShinyTexturePath) ?? GraphicsManager.Instance.Pixel;

            if (_disposed)
                return;

            _billboardEffect = new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            if (!_initialized)
                InitializeEffect();

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

            for (int i = 0; i < _cores.Length; i++)
                UpdateCore(ref _cores[i], factor);

            if (_settleFrames >= 0f)
            {
                _settleFrames -= factor;
                if (_settleFrames <= 0f && _stonesAlive == 0 &&
                    (_smoke == null || !_smoke.HasActiveParticles))
                {
                    RemoveSelf();
                }
            }
        }

        private void InitializeEffect()
        {
            _initialized = true;

            SoundController.Instance.PlayBuffer(CastSoundPath);

            for (int i = 0; i < _cores.Length; i++)
            {
                // MODEL_FIRE subtype 6 spawn offsets (CreateEffect, multiplied by the FPS factor).
                var position = new Vector3(
                    _target.X + MuGame.Random.Next(100) + 200f,
                    _target.Y + MuGame.Random.Next(100) - 50f,
                    _target.Z + MuGame.Random.Next(300) + 500f);

                _cores[i] = new PoisonCore
                {
                    Position = position,
                    Light = CoreLight,
                    FallSpeed = -(50f + MuGame.Random.Next(50)),   // Direction[2] = -50 - rand() % 50
                    LifeFrames = CoreLifeFrames,
                    JointLifeFrames = TrailJointLifeFrames,
                    Trail = new Vector3[(int)TrailTailCount - 1]   // CreateTail caps NumTails at MaxTails - 1
                };

                PushTrail(ref _cores[i], position);
            }

            _smoke = new PoisonSmokeParticles();
            World?.Objects.Add(_smoke);
            _ = _smoke.Load();
        }

        private void UpdateCore(ref PoisonCore core, float factor)
        {
            // MoveJoint() ages the trail joint independently of the falling object.
            if (core.JointLifeFrames > 0f)
                core.JointLifeFrames -= factor;

            if (core.Impacted)
                return;

            core.LifeFrames -= factor;
            core.Position += new Vector3(0f, 0f, core.FallSpeed * factor);
            PushTrail(ref core, core.Position);

            float groundZ = World?.Terrain?.RequestTerrainHeight(core.Position.X, core.Position.Y) ?? core.Position.Z;

            if (core.Position.Z <= groundZ || core.LifeFrames <= 0f)
            {
                core.Position = new Vector3(core.Position.X, core.Position.Y, groundZ);
                core.Impacted = true;
                TriggerImpact(core.Position);
            }
        }

        private void TriggerImpact(Vector3 position)
        {
            // MODEL_SKILL_INFERNO sub 2 - green additive ring, mesh 0 hidden, scale 30/100.
            var inferno = new DecayInfernoModel(_infernoModelPath, ImpactInfernoScale)
            {
                Position = position,
                Angle = Vector3.Zero,
                Light = new Vector3(0f, 0.5f, 0f)
            };

            World?.Objects.Add(inferno);
            _ = inferno.Load();

            // 15x BITMAP_SMOKE sub 11 with Light (0.1, 0.5, 0.1).
            if (_smoke != null)
            {
                _impactLight = new Vector3(0.1f, 0.5f, 0.1f);
                for (int i = 0; i < ImpactSmokePuffs; i++)
                {
                    _smoke.EmitImpactPuff(new Vector3(
                        position.X + MuGame.Random.Next(160) - 80f,
                        position.Y + MuGame.Random.Next(160) - 100f,
                        position.Z + 50f), _impactLight);
                }
            }

            // 6x CreateEffectFpsChecked(MODEL_STONE1 + rand() % 2, o->Position, o->Angle, Light)
            for (int j = 0; j < ImpactStoneCount; j++)
            {
                string path = _stonePaths[MuGame.Random.Next(_stonePaths.Length)];
                var stone = new DecayStoneModel(path)
                {
                    Position = position,
                    Angle = SpawnAngle,
                    Light = _impactLight
                };

                stone.Removed += OnStoneRemoved;
                _stonesAlive++;
                World?.Objects.Add(stone);
                _ = stone.Load();
            }

            SoundController.Instance.PlayBuffer(ImpactSoundPath);
            _settleFrames = ImpactSettleFrames;
        }

        private void OnStoneRemoved() => _stonesAlive--;

        private void PushTrail(ref PoisonCore core, Vector3 position)
        {
            Vector3[] trail = core.Trail;
            trail[core.TrailHead] = position;
            core.TrailHead = (core.TrailHead + 1) % trail.Length;
            if (core.TrailCount < trail.Length)
                core.TrailCount++;
        }

        private static Vector3 GetTrail(PoisonCore core, int index)
        {
            int start = core.TrailCount < core.Trail.Length ? 0 : core.TrailHead;
            return core.Trail[(start + index) % core.Trail.Length];
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || !_initialized)
                return;

            DrawBatch(_trailTexture, WriteTrailQuads());
            DrawBatch(_glowTexture, WriteGlowQuads());
            DrawBatch(_shinyTexture, WriteSparkleQuads());
        }

        /// <summary>
        /// RenderJoints(): for BITMAP_SMOKE / SubType 0 the tails form a ribbon between
        /// consecutive positions (Tails[j][0..1] are the +/- Scale/2 corners on the joint's
        /// local X axis), coloured with a single fAlpha = min(LifeTime, 20) * 0.1 and tiled by
        /// u = (NumTails - j) / (MaxTails - 1). The segment closest to the object (j &lt; 1) is skipped.
        /// </summary>
        private int WriteTrailQuads()
        {
            if (_trailTexture == null)
                return 0;

            Vector3 halfWidth = SourceJointMath.Rotate(
                new Vector3(TrailTailScale * 0.5f, 0f, 0f), HeadAngleDegrees);

            int quad = 0;

            for (int i = 0; i < _cores.Length && quad < MaxQuads; i++)
            {
                PoisonCore core = _cores[i];
                if (core.Trail == null || core.JointLifeFrames <= 0f || core.TrailCount < 3)
                    continue;

                float alpha = MathHelper.Clamp(
                    MathF.Min(core.JointLifeFrames, TrailJointLifeFrames) * 0.1f, 0f, 1f);
                if (alpha <= 0.001f)
                    continue;

                Color color = new Color(alpha, alpha, alpha, 1f);

                for (int t = 1; t + 1 < core.TrailCount && quad < MaxQuads; t++)
                {
                    // GetTrail(0) is the oldest tail, so the source's tail index is
                    // j = TrailCount - 1 - t and its coordinate (NumTails - j) / (MaxTails - 1)
                    // becomes (t + 1) / (MaxTails - 1).
                    Vector3 start = GetTrail(core, t);
                    Vector3 end = GetTrail(core, t + 1);
                    float uStart = (t + 1) / (TrailTailCount - 1f);
                    float uEnd = (t + 2) / (TrailTailCount - 1f);
                    WriteRibbon(quad++, start, end, halfWidth, uStart, uEnd, color);
                }
            }

            return quad;
        }

        /// <summary>MoveEffect(): CreateSprite(BITMAP_LIGHT, o->Position, 3.f, Light(0.5, 0.5, 1)).</summary>
        private int WriteGlowQuads()
        {
            if (_glowTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            float halfSize = _glowTexture.Width * GlowSpriteScale * 0.5f;
            int quad = 0;

            for (int i = 0; i < _cores.Length && quad < MaxQuads; i++)
            {
                PoisonCore core = _cores[i];
                if (core.Trail == null || core.Impacted)
                    continue;

                WriteQuad(quad++, core.Position, right * halfSize, up * halfSize,
                    new Color(0.5f, 0.5f, 1f, 1f));
            }

            return quad;
        }

        /// <summary>
        /// MoveEffect(): CreateSprite(BITMAP_SHINY + 1, o->Position, 3.f, white, rand() % 360) and
        /// CreateSprite(BITMAP_SHINY + 1, o->Position, 4.f, o->Light, rand() % 360).
        /// </summary>
        private int WriteSparkleQuads()
        {
            if (_shinyTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            int quad = 0;

            for (int i = 0; i < _cores.Length && quad < MaxQuads; i++)
            {
                PoisonCore core = _cores[i];
                if (core.Trail == null || core.Impacted)
                    continue;

                for (int s = 0; s < 2 && quad < MaxQuads; s++)
                {
                    float scale = s == 0 ? SparkleSmallScale : SparkleLargeScale;
                    Color color = s == 0 ? Color.White : ToColor(core.Light);
                    float rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                    float cosine = MathF.Cos(rotation);
                    float sine = MathF.Sin(rotation);

                    float halfSize = _shinyTexture.Width * scale * 0.5f;
                    WriteQuad(quad++, core.Position,
                        (right * cosine + up * sine) * halfSize,
                        (up * cosine - right * sine) * halfSize,
                        color);
                }
            }

            return quad;
        }

        private void WriteRibbon(int quadIndex, Vector3 start, Vector3 end, Vector3 halfWidth,
            float uStart, float uEnd, Color color)
        {
            int vertex = quadIndex * 4;
            if (vertex + 3 >= _vertices.Length)
                return;

            _vertices[vertex] = new VertexPositionColorTexture(start - halfWidth, color, new Vector2(uStart, 0f));
            _vertices[vertex + 1] = new VertexPositionColorTexture(start + halfWidth, color, new Vector2(uStart, 1f));
            _vertices[vertex + 2] = new VertexPositionColorTexture(end + halfWidth, color, new Vector2(uEnd, 1f));
            _vertices[vertex + 3] = new VertexPositionColorTexture(end - halfWidth, color, new Vector2(uEnd, 0f));
        }

        private void WriteQuad(int quadIndex, Vector3 center, Vector3 right, Vector3 up, Color color)
        {
            int vertex = quadIndex * 4;
            if (vertex + 3 >= _vertices.Length)
                return;

            _vertices[vertex] = new VertexPositionColorTexture(center - right - up, color, new Vector2(0f, 1f));
            _vertices[vertex + 1] = new VertexPositionColorTexture(center + right - up, color, new Vector2(1f, 1f));
            _vertices[vertex + 2] = new VertexPositionColorTexture(center + right + up, color, new Vector2(1f, 0f));
            _vertices[vertex + 3] = new VertexPositionColorTexture(center - right + up, color, new Vector2(0f, 0f));
        }

        private void DrawBatch(Texture2D? texture, int quadCount)
        {
            if (texture == null || texture.IsDisposed || _billboardEffect == null || quadCount <= 0)
                return;

            GraphicsDevice device = GraphicsDevice;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            try
            {
                device.BlendState = Blendings.OneOneAdditive;
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                _billboardEffect.Texture = texture;
                _billboardEffect.World = Matrix.Identity;
                _billboardEffect.View = Camera.Instance.View;
                _billboardEffect.Projection = Camera.Instance.Projection;
                _billboardEffect.DiffuseColor = Vector3.One;
                _billboardEffect.Alpha = 1f;

                EffectPass pass = _billboardEffect.CurrentTechnique.Passes[0];
                pass.Apply();
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    quadCount * 4,
                    QuadIndices,
                    0,
                    quadCount * 2);
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
            }
        }

        private static Color ToColor(Vector3 color)
        {
            color = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
            return new Color(color.X, color.Y, color.Z, 1f);
        }

        private static float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        private void RemoveSelf()
        {
            if (_smoke != null)
            {
                if (_smoke.Parent != null)
                    _smoke.Parent.Children.Remove(_smoke);
                else
                    World?.RemoveObject(_smoke);

                _smoke.Dispose();
                _smoke = null;
            }

            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _billboardEffect?.Dispose();
            _billboardEffect = null;
            _trailTexture = null;
            _glowTexture = null;
            _shinyTexture = null;
            base.Dispose();
        }

        private struct PoisonCore
        {
            public Vector3 Position;
            public Vector3 Light;
            public float FallSpeed;
            public float LifeFrames;
            public float JointLifeFrames;
            public bool Impacted;
            public Vector3[] Trail;
            public int TrailHead;
            public int TrailCount;
        }

        /// <summary>
        /// MODEL_SKILL_INFERNO subtype 2: Scale = PKKey / 100, LifeTime 12 frames,
        /// HiddenMesh = mesh 0, BlendMesh = -2, BlendMeshLight = LifeTime / 20,
        /// Scale += 0.04 per frame, BodyLight = the green (0, 0.5, 0) passed by the cast.
        /// </summary>
        private sealed class DecayInfernoModel : ModelObject
        {
            private readonly string _modelPath;
            private float _lifeFrames = 12f;

            public DecayInfernoModel(string modelPath, float scale)
            {
                _modelPath = modelPath;

                Scale = scale;
                ContinuousAnimation = false;
                LightEnabled = true;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                HiddenMesh = 0;
                BlendMesh = -2;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
                BlendMeshLight = ImpactRingBrightness;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                Scale += 0.04f * factor;                 // MoveEffect(): subtype 2 only grows in scale
                _lifeFrames -= factor;
                BlendMeshLight = ImpactRingBrightness;   // never touched by the original

                if (_lifeFrames <= 0f)
                {
                    if (Parent != null)
                        Parent.Children.Remove(this);
                    else
                        World?.RemoveObject(this);

                    Dispose();
                }
            }
        }

        /// <summary>
        /// MODEL_STONE1 / MODEL_STONE2 subtype 0 (the block shared with MODEL_ICE_SMALL):
        ///   LifeTime = rand() % 16 + 32; Scale = (rand() % 4 + 8) * 0.1f; Angle[2] = rand() % 360;
        ///   p1 = (0, (rand() % 256 + 64) * 0.1f, 0) rotated by AngleMatrix(o->Angle) -> Direction;
        ///   Gravity = rand() % 16 + 8.
        /// MoveEffect: Position += Direction; Direction *= 0.9; Position[2] += Gravity; Gravity -= 3;
        ///   on terrain contact it bounces (Gravity = -Gravity * 0.5), loses 4 life frames and
        ///   Angle[0] -= Scale * 128, otherwise Angle[0] -= Scale * 32 (tumbling debris).
        /// </summary>
        private sealed class DecayStoneModel : ModelObject
        {
            private readonly string _modelPath;
            private Vector3 _velocity;
            private float _gravity;
            private float _lifeFrames;

            public event Action? Removed;

            public DecayStoneModel(string modelPath)
            {
                _modelPath = modelPath;

                _lifeFrames = MuGame.Random.Next(16) + 32f;
                _gravity = MuGame.Random.Next(16) + 8f;
                Scale = (MuGame.Random.Next(4) + 8) * 0.1f;

                ContinuousAnimation = false;
                LightEnabled = true;
                RenderShadow = false;
                IsTransparent = false;
                DepthState = DepthStencilState.DepthRead;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);

                if (Status == GameControlStatus.NonInitialized)
                {
                    // The spawn code overwrites Angle[2] with rand() % 360 and derives the launch
                    // velocity by rotating (0, (rand() % 256 + 64) * 0.1f, 0) with the stone's own
                    // angle matrix (Angle[1] keeps the 20 degree tilt passed by the cast).
                    Angle = new Vector3(
                        Angle.X,
                        Angle.Y,
                        MathHelper.ToRadians(MuGame.Random.Next(360)));

                    Vector3 local = new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f);
                    _velocity = SourceJointMath.Rotate(local, new Vector3(
                        MathHelper.ToDegrees(Angle.X),
                        MathHelper.ToDegrees(Angle.Y),
                        MathHelper.ToDegrees(Angle.Z)));
                }

                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                Position += _velocity * factor;
                _velocity *= MathF.Pow(0.9f, factor);

                Position = new Vector3(Position.X, Position.Y, Position.Z + _gravity * factor);
                _gravity -= 3f * factor;

                float groundZ = World?.Terrain?.RequestTerrainHeight(Position.X, Position.Y) ?? Position.Z;
                if (Position.Z < groundZ)
                {
                    Position = new Vector3(Position.X, Position.Y, groundZ);
                    _gravity = -_gravity * 0.5f;
                    _lifeFrames -= 4f * factor;
                    Angle = new Vector3(
                        Angle.X - MathHelper.ToRadians(Scale * 128f * factor), Angle.Y, Angle.Z);
                }
                else
                {
                    Angle = new Vector3(
                        Angle.X - MathHelper.ToRadians(Scale * 32f * factor), Angle.Y, Angle.Z);
                }

                _lifeFrames -= factor;

                if (_lifeFrames <= 0f)
                {
                    if (Parent != null)
                        Parent.Children.Remove(this);
                    else
                        World?.RemoveObject(this);

                    Removed?.Invoke();
                    Dispose();
                }
            }
        }

        /// <summary>
        /// BITMAP_SMOKE subtype 11 (ZzzEffectParticle.cpp): life 50 frames, spawn scatter of
        /// (+/-32, +/-32, +32..+95), random heading, light copied into TurningForce and a
        /// local velocity of (0, -(rand() % 8 + 40), 0). Per frame the light is
        /// (LifeTime / 50) * TurningForce, the velocity decays *0.4, scale grows +0.05 and
        /// Z sinks by 1.
        /// </summary>
        private sealed class PoisonSmokeParticles : SourceParticleSystem
        {
            private const string SmokeTexturePath = "Effect/smoke01.jpg";
            private const int MaxParticles = 64;
            private const float LegacyFramesPerSecond = 25f;

            private Texture2D _texture = null!;
            private Vector2 _textureCenter;

            protected override Texture2D? ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public PoisonSmokeParticles()
                : base(MaxParticles)
            {
                BlendState = BlendState.Additive;
                MaxDistance = 2500f;
                ReferenceDistance = 800f;
                ScaleGrowth = 0f;
            }

            public override async Task LoadContent()
            {
                await TextureLoader.Instance.Prepare(SmokeTexturePath);
                _texture = TextureLoader.Instance.GetTexture2D(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            /// <summary>
            /// CreateParticleFpsChecked(BITMAP_SMOKE, Position, o->Angle, Light, 11,
            /// (rand() % 32 + 80) * 0.025f)
            /// </summary>
            public void EmitImpactPuff(Vector3 position, Vector3 light) =>
                CreateParticle(
                    type: 0,              // BITMAP_SMOKE
                    position: position,
                    angle: SpawnAngle,
                    light: light,
                    subType: 11,
                    scale: (MuGame.Random.Next(32) + 80) * 0.025f);

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                particle.LifeTime = 50f / LegacyFramesPerSecond;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Position += new Vector3(
                    MuGame.Random.Next(64) - 32,
                    MuGame.Random.Next(64) - 32,
                    MuGame.Random.Next(64) + 32);

                particle.Angle = new Vector3(
                    MathHelper.ToRadians(MuGame.Random.Next(90) - 45),
                    0f,
                    MathHelper.ToRadians(MuGame.Random.Next(360)));
                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));

                float speed = MuGame.Random.Next(8) + 40f;
                particle.Velocity = SourceJointMath.Rotate(
                    new Vector3(0f, -speed, 0f),
                    new Vector3(
                        MathHelper.ToDegrees(particle.Angle.X),
                        0f,
                        MathHelper.ToDegrees(particle.Angle.Z)));

                // sub 11: VectorCopy(o->Light, o->TurningForce)
                particle.TurningForce = particle.Light;
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                float legacyDelta = dt * LegacyFramesPerSecond;

                particle.Position += particle.Velocity * legacyDelta;
                particle.Velocity *= MathF.Pow(0.4f, legacyDelta);
                particle.Scale += 0.05f * legacyDelta;
                particle.Position.Z -= 1f * legacyDelta;

                float luminosity = particle.LifeTime * LegacyFramesPerSecond / 50f;
                particle.Light = particle.TurningForce * luminosity;
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
            {
                // sub 11 rewrites o->Light every frame; no additional life fade.
                return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, 1f);
            }
        }
    }
}
