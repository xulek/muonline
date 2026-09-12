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
    /// Power Wave (skill 11 / AT_SKILL_POWERWAVE) port of SourceMain5.2.
    ///
    /// The original only fires one model (ZzzCharacter.cpp):
    ///   CreateEffect(MODEL_MAGIC2, o->Position, Angle, o->Light);
    ///   PlayBuffer(SOUND_MAGIC);
    ///
    /// MODEL_MAGIC2 = Skill/Magic02.bmd (ZzzOpenData.cpp) and its CreateEffect / MoveEffect
    /// cases give the behaviour:
    ///   o->BlendMesh = 0;  o->LifeTime = 20;  Vector(0, -60, 0, o->Direction);
    ///   o->BlendMeshLight  = o->LifeTime * 0.1f;      // 2.0 -> 0.1 fade
    ///   o->BlendMeshTexCoordU = -o->LifeTime * 0.2f;  // scrolling energy texture
    ///   4x CreateParticleFpsChecked(BITMAP_SMOKE, o->Position, o->Angle, o->Light, 3);
    ///
    /// The generic MoveEffect tail then runs MoveParticle(o, true) with that Direction, i.e.
    /// the wave slides 60 units per frame (12 tiles over its 20 frame lifetime) along the
    /// caster's facing direction. The model keeps the default Scale 0.9 (CreateEffect).
    /// </summary>
    public sealed class PowerWaveEffect : EffectObject
    {
        private const string DefaultModelPath = "Skill/Magic02.bmd";

        private const float LifeFrames = 20f;
        private const float StartScale = 0.9f;

        /// <summary>CreateEffect: Vector(0, -60, 0, o->Direction) - 60 units per frame.</summary>
        private const float ForwardSpeed = -60f;

        /// <summary>MoveEffect: 4x CreateParticleFpsChecked(BITMAP_SMOKE, ..., 3).</summary>
        private const int SmokeAttemptsPerFrame = 4;

        private readonly WalkerObject _caster;
        private readonly Vector3 _yaw;
        private readonly float _yawZ;

        private PowerWaveModel? _model;
        private PowerWaveSmoke? _smoke;
        private string _modelPath = DefaultModelPath;

        private Vector3 _position;
        private Vector3 _light = Vector3.One;
        private float _lifeFrames = LifeFrames;
        private bool _initialized;
        private bool _disposed;

        public PowerWaveEffect(WalkerObject caster)
        {
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _yaw = _caster.Angle;
            _yawZ = _caster.Angle.Z;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-1600f, -1600f, -400f),
                new Vector3(1600f, 1600f, 800f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (!await BMDLoader.Instance.AssestExist(_modelPath))
            {
                const string fallback = "Skill/magic02.bmd";
                if (await BMDLoader.Instance.AssestExist(fallback))
                    _modelPath = fallback;
            }
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

            // MoveEffect() generic tail: MoveParticle(o, true) integrates o->Direction
            // rotated by the object angle.
            _position += RotateLocal(new Vector3(0f, ForwardSpeed, 0f), _yawZ) * factor;

            _lifeFrames -= factor;
            float life = MathF.Max(0f, _lifeFrames);

            if (_model != null)
            {
                _model.Position = _position;

                // o->BlendMeshLight = o->LifeTime * 0.1f (the client bakes this value into
                // the vertex colour, so it saturates at 1.0 there).
                _model.BlendMeshLight = MathHelper.Clamp(life * 0.1f, 0f, 1f);

                // o->BlendMeshTexCoordU = -o->LifeTime * 0.2f
                _model.ScrollU = -life * 0.2f;
            }

            if (life > 0f)
                _smoke?.EmitTick(_position, _yaw, _light, factor, SmokeAttemptsPerFrame);

            if (life <= 0f && (_smoke == null || !_smoke.HasActiveParticles))
                RemoveSelf();
        }

        private void InitializeEffect()
        {
            _initialized = true;

            _position = _caster.WorldPosition.Translation;
            _light = World?.Terrain?.EvaluateTerrainLight(_position.X, _position.Y) ?? Vector3.One;

            _model = new PowerWaveModel(_modelPath)
            {
                Position = _position,
                Angle = _yaw,
                Scale = StartScale,
                Light = _light,
                BlendMeshLight = 1f
            };

            World?.Objects.Add(_model);
            _ = _model.Load();

            _smoke = new PowerWaveSmoke();
            World?.Objects.Add(_smoke);
            _ = _smoke.Load();
        }

        private static Vector3 RotateLocal(Vector3 local, float yawRadians) =>
            MathUtils.VectorRotate(local, Matrix.CreateRotationZ(yawRadians));

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

            if (_model != null)
            {
                if (_model.Parent != null)
                    _model.Parent.Children.Remove(_model);
                else
                    World?.RemoveObject(_model);

                _model.Dispose();
                _model = null;
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
            _model = null;
            _smoke = null;
            base.Dispose();
        }

        /// <summary>
        /// MODEL_MAGIC2 body: BlendMesh = 0 in the original (the model has a single mesh, so
        /// the whole body is drawn additively) with a scrolling UV and a life driven fade.
        /// </summary>
        private sealed class PowerWaveModel : ModelObject
        {
            private readonly string _modelPath;
            private readonly UvScrollDeformer _scrollDeformer = new();
            private float _scrollU;
            private bool _scrollInitialized;

            public PowerWaveModel(string modelPath)
            {
                _modelPath = modelPath;

                Scale = StartScale;
                ContinuousAnimation = false;
                LightEnabled = true;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendMesh = -2;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
            }

            /// <summary>o->BlendMeshTexCoordU - set once per frame by the effect.</summary>
            public float ScrollU
            {
                get => _scrollU;
                set
                {
                    if (_scrollInitialized && MathF.Abs(_scrollU - value) < 0.0001f)
                        return;

                    _scrollU = value;
                    _scrollInitialized = true;
                    _scrollDeformer.OffsetU = value;
                    InvalidateBuffers(BufferFlagTexture);
                }
            }

            protected override IVertexDeformer GetVertexDeformer() => _scrollDeformer;

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }
        }

        /// <summary>
        /// BITMAP_SMOKE subtype 3 (ZzzEffectParticle.cpp): life 10 frames, scale
        /// (rand() % 32 + 80) * 0.01, random heading and a local velocity of
        /// (0, -(rand() % 8 + 40), 0) which decays *0.4 per frame while the sprite grows
        /// +0.1 per frame and its light fades with LifeTime / 8.
        /// </summary>
        private sealed class PowerWaveSmoke : SourceParticleSystem
        {
            private const string SmokeTexturePath = "Effect/smoke01.jpg";
            private const int MaxParticles = 64;
            private const float LegacyFramesPerSecond = 25f;

            private Texture2D _texture = null!;
            private Vector2 _textureCenter;

            protected override Texture2D? ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public PowerWaveSmoke()
                : base(MaxParticles)
            {
                BlendState = BlendState.Additive;
                MaxDistance = 2000f;
                ReferenceDistance = 800f;
                ScaleGrowth = 0f;
            }

            public override async Task LoadContent()
            {
                await TextureLoader.Instance.Prepare(SmokeTexturePath);
                _texture = TextureLoader.Instance.GetTexture2D(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            /// <summary>CreateParticleFpsChecked(BITMAP_SMOKE, pos, angle, light, 3) x4.</summary>
            public void EmitTick(Vector3 position, Vector3 angle, Vector3 light, float factor, int attempts)
            {
                if (_texture == null)
                    return;

                float chance = MathHelper.Clamp(factor, 0f, 1f);
                for (int i = 0; i < attempts; i++)
                {
                    if (MuGame.Random.NextDouble() > chance)
                        continue;

                    CreateParticle(
                        type: 0,          // BITMAP_SMOKE
                        position: position,
                        angle: angle,
                        light: light,
                        subType: 3);
                }
            }

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                particle.LifeTime = 10f / LegacyFramesPerSecond;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                particle.Angle = new Vector3(
                    MathHelper.ToRadians(MuGame.Random.Next(90) - 45),
                    0f,
                    MathHelper.ToRadians(MuGame.Random.Next(360)));

                float speed = MuGame.Random.Next(8) + 40f;
                particle.Velocity = SourceJointMath.Rotate(
                    new Vector3(0f, -speed, 0f),
                    new Vector3(
                        MathHelper.ToDegrees(particle.Angle.X),
                        0f,
                        MathHelper.ToDegrees(particle.Angle.Z)));
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                float legacyDelta = dt * LegacyFramesPerSecond;

                particle.Position += particle.Velocity * legacyDelta;
                particle.Velocity *= MathF.Pow(0.4f, legacyDelta);
                particle.Scale += 0.1f * legacyDelta;

                // Luminosity = LifeTime / 8 -> Vector(L * 0.8, L * 0.8, L)
                float lifeFrames = particle.LifeTime * LegacyFramesPerSecond;
                float luminosity = lifeFrames / 8f;
                particle.Light = new Vector3(luminosity * 0.8f, luminosity * 0.8f, luminosity);
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
            {
                // The source overwrites o->Light every frame; no extra life fade.
                return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, 1f);
            }
        }

        /// <summary>Scrolls UV coordinates (o->BlendMeshTexCoordU) without touching positions.</summary>
        private sealed class UvScrollDeformer : IVertexDeformer, ITexCoordDeformer
        {
            public float OffsetU;

            public Vector3 DeformVertex(in Client.Data.BMD.BMDTextureVertex vertex, in Vector3 transformedPosition)
                => transformedPosition;

            public Vector2 DeformTexCoord(float u, float v) => new Vector2(u + OffsetU, v);
        }
    }
}
