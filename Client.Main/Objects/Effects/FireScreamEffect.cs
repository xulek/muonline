#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dark Lord "Fire Scream" (skill 78 / AT_SKILL_DARK_SCREAM) port of SourceMain5.2.
    ///
    /// ZzzCharacter.cpp (skill switch for AT_SKILL_DARK_SCREAM and the master variants)
    /// spawns three pillar pairs around the caster, each pair being
    /// MODEL_DARK_SCREAM (Skill/darkfirescrem02.bmd - dark ground ring, chainring3 texture)
    /// plus MODEL_DARK_SCREAM_FIRE (Skill/darkfirescrem01.bmd - fire streak, motion_blurr
    /// texture), with a BITMAP_JOINT_FORCE sub 7 swirl (bluish BITMAP_INFERNO ribbon) on the
    /// dark member:
    ///
    ///   CreateEffect(MODEL_DARK_SCREAM,      pObj-&gt;Position, ...);
    ///   CreateEffect(MODEL_DARK_SCREAM_FIRE, pObj-&gt;Position, ...);
    ///   P = ( 80, 0, 0); pObj-&gt;Angle[2] += 10;  ... second pair
    ///   P = (-80, 0, 0); pObj-&gt;Angle[2] -= 20;  ... third pair
    ///
    /// CreateEffect (ZzzEffect.cpp) then applies the shared hand offset (-10, -60, 135) to
    /// every one of them, so the three pairs end up in front of the caster in an arc.
    ///
    /// ZzzEffect.cpp MoveEffect() for MODEL_DARK_SCREAM / MODEL_DARK_SCREAM_FIRE:
    ///   * Scale decays per frame (0.04 for the ring, 0.14 for the fire streak),
    ///   * Z is re-pinned to terrain height + 3 every frame (both models hug the ground),
    ///   * one BITMAP_FLAME sub 8 particle per frame with scale (Scale - 0.4) * 3.5.
    /// LifeTime is 19 render frames (0.76 s at the legacy 25 FPS clock); the generic
    /// MoveEffect tail decrements it and calls EffectDestructor().
    /// </summary>
    public sealed class FireScreamEffect : EffectObject
    {
        private const string DarkModelPath = "Skill/darkfirescrem02.bmd";   // MODEL_DARK_SCREAM
        private const string FireModelPath = "Skill/darkfirescrem01.bmd";   // MODEL_DARK_SCREAM_FIRE
        private const string FlameTexturePath = "Effect/Flame01.jpg";       // BITMAP_FLAME
        private const string BlurTexturePath = "Effect/PoundingBall.jpg";   // BITMAP_BLUE_BLUR sub 1
        private const string JointTexturePath = "Effect/Inferno.jpg";       // BITMAP_INFERNO
        private const string GlowTexturePath = "Effect/flare01.jpg";        // BITMAP_LIGHT

        private const float LifeFrames = 19f;
        private const float GroundZOffset = 3f;
        private const float DarkStartScale = 0.9f;
        private const float DarkScaleDecay = 0.04f;
        private const float FireStartScale = 2.3f;
        private const float FireScaleDecay = 0.14f;
        private const float PillarSideOffset = 80f;
        private const float PillarForwardOffset = -60f;   // source hand offset (-10, -60, 135)
        private const float PillarLateralOffset = -10f;
        private const float ParticleLifeFrames = 33f;
        private const float ParticleScaleCutoff = 0.4f;
        private const float ParticleScalePerFrame = 3.5f;

        /// <summary>
        /// CreateEffect() sets `o-&gt;Direction[1] = -35` for both dark scream models and the
        /// generic MoveEffect() tail calls MoveParticle(o, true) - i.e.
        /// `Position += VectorRotate(Direction, AngleMatrix(Angle)) * FPS_ANIMATION_FACTOR`.
        /// Direction[1] -35 maps to the model's -Y, which is the caster's facing direction,
        /// so each pillar flies 35 units per frame (~6.6 tiles over the 19 frame lifetime)
        /// straight ahead while its Z stays pinned to the ground.
        /// </summary>
        private const float PillarForwardSpeed = -35f;

        /// <summary>
        /// The fire streak is drawn additively (see DarkScreamPillarModel) which stacks the
        /// five overlapping flame volumes much brighter than the engine's opaque pass, so its
        /// terrain light is scaled down to keep the same perceived intensity.
        /// </summary>
        private const float FireBrightness = 0.55f;

        private const int PillarCount = 3;
        private const int MaxParticles = 384;
        private const int MaxQuadsPerBatch = MaxParticles + 64;

        /// <summary>0 deg (centre), +10 deg (right) and -20 deg (left) pillar headings.</summary>
        private static readonly float[] PillarYawOffsetsDegrees = { 0f, 10f, -20f };
        private static readonly float[] PillarSideOffsets = { 0f, PillarSideOffset, -PillarSideOffset };

        // BITMAP_BLUE_BLUR sub 1 is spawned with Vector(0, 1, 0) but MoveParticles()
        // immediately rewrites its light, so only the joint sprite tint is kept here.
        private static readonly Vector3 JointLight = new(0.3f, 0.3f, 1f);   // JOINT_FORCE sub 7 sprite tint

        private readonly WalkerObject _caster;
        private readonly DarkScreamPillarModel[] _pillars = new DarkScreamPillarModel[PillarCount * 2];
        private readonly ForceSwirlJoint[] _joints = new ForceSwirlJoint[PillarCount];
        private readonly BlurSprite[] _blurs = new BlurSprite[PillarCount];
        private readonly FlameParticle[] _particles = new FlameParticle[MaxParticles];
        private int _particleCount;

        private readonly VertexPositionColorTexture[] _vertices =
            new VertexPositionColorTexture[MaxQuadsPerBatch * 4];
        private static readonly short[] QuadIndices = QuadIndexCache.Get(MaxQuadsPerBatch);

        private Texture2D? _flameTexture;
        private Texture2D? _blurTexture;
        private Texture2D? _jointTexture;
        private Texture2D? _glowTexture;
        private BasicEffect? _billboardEffect;

        private string _darkModelPath = DarkModelPath;
        private string _fireModelPath = FireModelPath;

        private bool _initialized;
        private bool _disposed;
        private float _lifeFrames = LifeFrames;

        public FireScreamEffect(WalkerObject caster)
        {
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-900f, -900f, -200f),
                new Vector3(900f, 900f, 900f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _darkModelPath = await ResolveModelPath(DarkModelPath, "Skill/DarkFireScrem02.bmd");
            _fireModelPath = await ResolveModelPath(FireModelPath, "Skill/DarkFireScrem01.bmd");

            _ = await TextureLoader.Instance.Prepare(FlameTexturePath);
            _ = await TextureLoader.Instance.Prepare(BlurTexturePath);
            _ = await TextureLoader.Instance.Prepare(JointTexturePath);
            _ = await TextureLoader.Instance.Prepare(GlowTexturePath);

            _flameTexture = TextureLoader.Instance.GetTexture2D(FlameTexturePath) ?? GraphicsManager.Instance.Pixel;
            _blurTexture = TextureLoader.Instance.GetTexture2D(BlurTexturePath) ?? GraphicsManager.Instance.Pixel;
            _jointTexture = TextureLoader.Instance.GetTexture2D(JointTexturePath) ?? GraphicsManager.Instance.Pixel;
            _glowTexture = TextureLoader.Instance.GetTexture2D(GlowTexturePath) ?? GraphicsManager.Instance.Pixel;

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

            if (_caster.Status == GameControlStatus.Disposed)
            {
                RemoveSelf();
                return;
            }

            if (!_initialized)
                InitializeEffect();

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

            UpdateParticles(factor);
            UpdateBlurs(factor);

            for (int i = 0; i < _joints.Length; i++)
                _joints[i]?.Update(factor);

            // The source particles/joints are independent objects: the 19 frame pillar
            // lifetime only stops new emissions, the flames keep rising afterwards.
            _lifeFrames -= factor;
            if (_lifeFrames <= 0f && _particleCount == 0 && !HasLiveBlur() && !HasLiveJoint())
                RemoveSelf();
        }

        private bool HasLiveBlur()
        {
            for (int i = 0; i < _blurs.Length; i++)
            {
                if (_blurs[i].Active)
                    return true;
            }

            return false;
        }

        private bool HasLiveJoint()
        {
            for (int i = 0; i < _joints.Length; i++)
            {
                if (_joints[i] != null && _joints[i].LifeFrames > 0f)
                    return true;
            }

            return false;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || !_initialized)
                return;

            DrawBatch(_flameTexture, WriteParticleQuads());
            DrawBatch(_jointTexture, WriteJointQuads());
            DrawBatch(_glowTexture, WriteGlowQuads());
            DrawBatch(_blurTexture, WriteBlurQuads());
        }

        private void InitializeEffect()
        {
            _initialized = true;

            Vector3 origin = _caster.WorldPosition.Translation;
            float yaw = _caster.Angle.Z;

            for (int i = 0; i < PillarCount; i++)
            {
                float pillarYaw = yaw + MathHelper.ToRadians(PillarYawOffsetsDegrees[i]);

                // CreateEffect applies the (-10, -60, 135) hand offset with the *pillar's*
                // angle, and the extra +/-80 side offset is rotated by the same angle.
                Vector3 handOffset = RotateLocal(
                    new Vector3(PillarLateralOffset, PillarForwardOffset, 0f), pillarYaw);
                Vector3 sideOffset = RotateLocal(new Vector3(PillarSideOffsets[i], 0f, 0f), pillarYaw);

                Vector3 position = origin + sideOffset + handOffset;
                SetGroundHeight(ref position, GroundZOffset);

                var angle = new Vector3(_caster.Angle.X, _caster.Angle.Y, pillarYaw);

                // The source copies the caster's terrain light into o->Light, which is the
                // additive body colour of the fire streak model.
                Vector3 pillarLight = (World?.Terrain?.EvaluateTerrainLight(position.X, position.Y)
                    ?? Vector3.One) * FireBrightness;

                _pillars[i * 2] = SpawnPillar(_darkModelPath, DarkStartScale, DarkScaleDecay, position, angle, Vector3.Zero);
                _pillars[i * 2 + 1] = SpawnPillar(_fireModelPath, FireStartScale, FireScaleDecay, position, angle, pillarLight);

                // BITMAP_JOINT_FORCE sub 7, created at the ground level (terrain height + 3)
                // before the source adds its own 20 unit lift to the blur particle.
                _joints[i] = new ForceSwirlJoint(position, pillarYaw);

                // CreateEffect() dark-scream block: +20 Z, then a local (0, -20, 0) offset for
                // the blur particle, which BITMAP_BLUE_BLUR sub 1 additionally drops by 45.
                Vector3 blurPosition = position;
                blurPosition.Z += 20f * FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                blurPosition += RotateLocal(new Vector3(0f, -20f, 0f), pillarYaw);
                blurPosition.Z -= 45f * FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                _blurs[i] = new BlurSprite
                {
                    Active = true,
                    Position = blurPosition,
                    LifeFrames = 30f,
                    Scale = (MuGame.Random.Next(0, 64) + 64) * 0.01f,
                    Rotation = MathHelper.ToRadians(MuGame.Random.Next(0, 360))
                };
            }
        }

        private DarkScreamPillarModel SpawnPillar(
            string modelPath,
            float startScale,
            float scaleDecay,
            Vector3 position,
            Vector3 angle,
            Vector3 light)
        {
            bool additive = string.Equals(modelPath, _fireModelPath, StringComparison.OrdinalIgnoreCase);

            var model = new DarkScreamPillarModel(this, modelPath, startScale, scaleDecay, additive)
            {
                Position = position,
                Angle = angle,
                Light = light
            };

            World.Objects.Add(model);
            _ = model.Load();
            return model;
        }

        /// <summary>
        /// MoveEffect() for MODEL_DARK_SCREAM / MODEL_DARK_SCREAM_FIRE:
        /// one BITMAP_FLAME sub 8 particle per render frame with
        /// scale = (o-&gt;Scale - 0.4) * 3.5 (CreateParticleFpsChecked => rand_fps_check(1)).
        /// </summary>
        internal void EmitFlame(Vector3 position, float scale, float factor, float yaw, Vector3 light)
        {
            if (_particleCount >= MaxParticles)
                return;

            if (MuGame.Random.NextDouble() > MathHelper.Clamp(factor, 0f, 1f))
                return;

            // ZzzEffectParticle.cpp CreateParticle, BITMAP_FLAME sub 8.
            float particleScale = scale - MuGame.Random.Next(0, 20) / 100f;
            float velocityY = MuGame.Random.Next(0, 10) >= 3
                ? (MuGame.Random.Next(0, 20) / 10f - 1f) * particleScale
                : 0f;

            var particle = new FlameParticle
            {
                Position = position,
                StartX = position.X,
                LifeFrames = ParticleLifeFrames,
                Scale = particleScale,
                Gravity = (MuGame.Random.Next(0, 100) / 100f + 1.8f) * particleScale,
                Rotation = MuGame.Random.Next(0, 360) - 180f,
                // The source passes o->Light of the emitting model, i.e. the caster's
                // terrain light.
                Light = light,
                // MovePosition() rotates the local velocity by the particle angle, which is
                // the pillar heading the model was spawned with.
                Velocity = RotateLocal(new Vector3(0f, velocityY, 0f), yaw)
            };

            particle.Position.X += (MuGame.Random.Next(0, 2) / 2f) * particleScale * factor;
            particle.Position.Y += (particle.Position.X - particle.StartX) * factor;

            _particles[_particleCount++] = particle;
        }

        private void UpdateParticles(float factor)
        {
            float lightDecay = MathF.Pow(1f / 1.007f, factor);

            for (int i = 0; i < _particleCount;)
            {
                ref FlameParticle particle = ref _particles[i];

                // MoveParticles() BITMAP_FLAME sub 8.
                particle.Rotation += (particle.StartX < particle.Position.X ? 2f : -2f) * factor;
                particle.Position.Z += particle.Gravity / 2f * factor;
                particle.Scale -= particle.Gravity * factor / 95f;
                particle.Light *= lightDecay;
                particle.LifeFrames -= factor;

                // Generic MoveParticles() prologue: bEnableMove integrates the velocity
                // rotated by the particle angle (the pillar heading).
                particle.Position += particle.Velocity * factor;

                if (particle.Scale <= 0f || particle.LifeFrames <= 0f)
                {
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                i++;
            }
        }

        private void UpdateBlurs(float factor)
        {
            for (int i = 0; i < _blurs.Length; i++)
            {
                ref BlurSprite blur = ref _blurs[i];
                if (!blur.Active)
                    continue;

                // ZzzEffectParticle.cpp BITMAP_BLUE_BLUR sub 1: grows 0.19/frame, rises
                // 5/frame and fades with Light = LifeTime / 20.
                blur.Scale += 0.19f * factor;
                blur.Position.Z += 5f * factor;
                blur.LifeFrames -= factor;
                if (blur.LifeFrames <= 0f)
                    blur.Active = false;
            }
        }

        // ------------------------------- drawing -------------------------------

        private int WriteParticleQuads()
        {
            if (_flameTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            int quad = 0;

            for (int i = 0; i < _particleCount && quad < MaxQuadsPerBatch; i++)
            {
                ref FlameParticle particle = ref _particles[i];
                float cosine = MathF.Cos(MathHelper.ToRadians(particle.Rotation));
                float sine = MathF.Sin(MathHelper.ToRadians(particle.Rotation));
                Vector3 rotatedRight = right * cosine + up * sine;
                Vector3 rotatedUp = up * cosine - right * sine;

                float halfWidth = _flameTexture.Width * particle.Scale * 0.5f;
                float halfHeight = _flameTexture.Height * particle.Scale * 0.5f;

                WriteQuad(quad++, particle.Position,
                    rotatedRight * halfWidth, rotatedUp * halfHeight,
                    ToColor(particle.Light));
            }

            return quad;
        }

        private int WriteJointQuads()
        {
            if (_jointTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            int quad = 0;

            for (int i = 0; i < _joints.Length; i++)
            {
                ForceSwirlJoint? joint = _joints[i];
                if (joint == null || joint.LifeFrames <= 0f)
                    continue;

                float halfSize = MathF.Max(1f, joint.Scale * 0.5f);
                Color color = ToColor(joint.Light * (joint.LifeFrames < 10f
                    ? MathHelper.Clamp(joint.LifeFrames / 10f, 0f, 1f)
                    : 1f));

                for (int t = 0; t < joint.TailCount && quad < MaxQuadsPerBatch; t++)
                {
                    Vector3 center = joint.GetTail(t);
                    WriteQuad(quad++, center, right * halfSize, up * halfSize, color);
                }
            }

            return quad;
        }

        private int WriteGlowQuads()
        {
            if (_glowTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            int quad = 0;

            for (int i = 0; i < _joints.Length && quad < MaxQuadsPerBatch; i++)
            {
                ForceSwirlJoint? joint = _joints[i];
                if (joint == null || joint.LifeFrames <= 0f)
                    continue;

                // MoveJoint(): CreateSprite(BITMAP_LIGHT, o->Position, 4 + (20 - LifeTime) / 5, Light)
                // with Light = (0.3, 0.3, 1) while the joint is still young, then the fading
                // o->Light instead.
                float scale = 4f + (20f - joint.LifeFrames) / 5f;
                float halfSize = _glowTexture.Width * scale * 0.25f;
                Vector3 glowColor = joint.LifeFrames >= 10f ? JointLight : joint.Light;
                WriteQuad(quad++, joint.Position, right * halfSize, up * halfSize,
                    ToColor(glowColor));
            }

            return quad;
        }

        private int WriteBlurQuads()
        {
            if (_blurTexture == null)
                return 0;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            int quad = 0;

            for (int i = 0; i < _blurs.Length && quad < MaxQuadsPerBatch; i++)
            {
                ref BlurSprite blur = ref _blurs[i];
                if (!blur.Active)
                    continue;

                // MoveParticles() BITMAP_BLUE_BLUR sub 1 rewrites the spawn light with
                // white * (LifeTime / 20).
                float light = MathHelper.Clamp(blur.LifeFrames / 20f, 0f, 1f);
                float cosine = MathF.Cos(blur.Rotation);
                float sine = MathF.Sin(blur.Rotation);
                Vector3 rotatedRight = right * cosine + up * sine;
                Vector3 rotatedUp = up * cosine - right * sine;

                float halfSize = _blurTexture.Width * blur.Scale * 0.5f;
                WriteQuad(quad++, blur.Position,
                    rotatedRight * halfSize, rotatedUp * halfSize,
                    ToColor(new Vector3(light)));
            }

            return quad;
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

        // ------------------------------ helpers -------------------------------

        private static Vector3 RotateLocal(Vector3 local, float yawRadians) =>
            MathUtils.VectorRotate(local, Matrix.CreateRotationZ(yawRadians));

        private static Color ToColor(Vector3 color)
        {
            color = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
            return new Color(color.X, color.Y, color.Z, 1f);
        }

        private void SetGroundHeight(ref Vector3 position, float offset)
        {
            if (World?.Terrain != null)
                position.Z = World.Terrain.RequestTerrainHeight(position.X, position.Y) + offset;
            else
                position.Z += offset;
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

        private void RemoveSelf()
        {
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
            _flameTexture = null;
            _blurTexture = null;
            _jointTexture = null;
            _glowTexture = null;
            _particleCount = 0;
            base.Dispose();
        }

        // ------------------------------ state ---------------------------------

        private struct FlameParticle
        {
            public Vector3 Position;
            public float StartX;
            public Vector3 Light;
            public Vector3 Velocity;
            public float Gravity;
            public float Scale;
            public float Rotation;
            public float LifeFrames;
        }

        private struct BlurSprite
        {
            public bool Active;
            public Vector3 Position;
            public float Scale;
            public float Rotation;
            public float LifeFrames;
        }

        /// <summary>
        /// MODEL_DARK_SCREAM / MODEL_DARK_SCREAM_FIRE: static BMD (single action key) spawned
        /// on the ground, re-pinned to terrain height + 3 every frame, scale decaying until the
        /// 19 frame lifetime runs out.
        /// </summary>
        private sealed class DarkScreamPillarModel : ModelObject
        {
            private readonly FireScreamEffect _owner;
            private readonly string _modelPath;
            private readonly float _scaleDecay;
            private float _lifeFrames = LifeFrames;

            /// <param name="additive">
            /// MODEL_DARK_SCREAM_FIRE (darkfirescrem01.bmd) carries two glow textures that are
            /// painted on black (motion_blurr.jpg, SwordEffor2.jpg) - the same kind of texture
            /// the original marks with a "bright" texture script (see Skill/darklordskill.bmd
            /// and its KingS_R.jpg). Drawing them with the default opaque RENDER_TEXTURE path
            /// exposes that black box, so the whole body is drawn additively
            /// (o->BlendMesh = -2 in the original joint/skill effect vocabulary).
            /// MODEL_DARK_SCREAM (darkfirescrem02.bmd, chainring3.tga) keeps its own alpha and
            /// stays on the regular alpha-tested path.
            /// </param>
            public DarkScreamPillarModel(
                FireScreamEffect owner,
                string modelPath,
                float startScale,
                float scaleDecay,
                bool additive)
            {
                _owner = owner;
                _modelPath = modelPath;
                _scaleDecay = scaleDecay;

                Scale = startScale;
                ContinuousAnimation = false;
                LightEnabled = true;
                RenderShadow = false;
                DepthState = DepthStencilState.Default;

                if (additive)
                {
                    IsTransparent = true;
                    AffectedByTransparency = true;
                    BlendMesh = -2;
                    BlendState = Blendings.OneOneAdditive;
                    BlendMeshState = Blendings.OneOneAdditive;
                }
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

                Scale = MathF.Max(0f, Scale - _scaleDecay * factor);
                if (Scale < 0.1f)
                    Scale = 0f;

                var position = Position;
                if (World?.Terrain != null)
                {
                    position.Z = World.Terrain.RequestTerrainHeight(position.X, position.Y) + GroundZOffset;
                    Position = position;
                }

                float particleScale = (Scale - ParticleScaleCutoff) * ParticleScalePerFrame;
                if (particleScale > 0f)
                {
                    Vector3 light = World?.Terrain?.EvaluateTerrainLight(Position.X, Position.Y)
                        ?? Vector3.One;
                    _owner.EmitFlame(Position, particleScale, factor, Angle.Z, light);
                }

                // MoveEffect() generic tail: MoveParticle(o, true) integrates o->Direction
                // rotated by the object angle, so the pillar slides forward over the ground
                // (the Z pin above keeps it glued to the terrain).
                Position += RotateLocal(new Vector3(0f, PillarForwardSpeed, 0f), Angle.Z) * factor;

                _lifeFrames -= factor;
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
        /// BITMAP_JOINT_FORCE sub 7 (ZzzEffectJoint.cpp): a BITMAP_INFERNO swirl that keeps 13
        /// tails and spins up while rising. Position is StartPosition + VectorRotate(Direction,
        /// AngleMatrix) with the yaw advancing 10 deg per substep, three substeps per frame.
        /// </summary>
        private sealed class ForceSwirlJoint
        {
            private const int MaxTails = 13;
            private const int SubstepsPerFrame = 3;

            private readonly Vector3[] _tails = new Vector3[MaxTails];
            private Vector3 _localDirection;
            private float _weapon;
            private int _tailHead;
            private int _tailCount;

            public Vector3 StartPosition { get; private set; }
            public Vector3 Position { get; private set; }
            public float Yaw;
            public float Scale = 150f;
            public float LifeFrames = 20f;

            /// <summary>
            /// CreateJoint() leaves BITMAP_JOINT_FORCE o-&gt;Light at its default (1,1,1); the
            /// blue tint is applied only to the per-frame BITMAP_LIGHT glow sprite.
            /// </summary>
            public Vector3 Light = Vector3.One;

            public ForceSwirlJoint(Vector3 position, float yaw)
            {
                StartPosition = position;
                Position = position;
                Yaw = yaw;
                _localDirection = new Vector3(3.5f, 1f, 1f);
            }

            public int TailCount => _tailCount;

            public Vector3 GetTail(int index)
            {
                int start = _tailCount < MaxTails ? 0 : _tailHead;
                return _tails[(start + index) % MaxTails];
            }

            public void Update(float factor)
            {
                if (LifeFrames <= 0f)
                    return;

                for (int i = 0; i < SubstepsPerFrame; i++)
                {
                    Yaw += MathHelper.ToRadians(10f) * factor;
                    Position = StartPosition + RotateLocal(_localDirection, Yaw);
                    PushTail(Position);

                    if (_weapon == 0f)
                    {
                        _localDirection.Y *= MathF.Pow(0.95f, factor);
                        if (_localDirection.Y < 10f)
                            _localDirection.Y = -10f;
                    }

                    if (_localDirection.Y < 0f)
                    {
                        _weapon += factor;
                        if (_weapon < 20f)
                        {
                            _localDirection.Y = -30f;
                            Scale = 40f;
                        }
                        else
                        {
                            StartPosition += new Vector3(0f, 0f, 5f * factor);
                        }
                    }
                }

                Scale -= 5f * factor;

                if (LifeFrames < 10f)
                    Light *= MathF.Pow(1f / 1.2f, factor);

                LifeFrames -= factor;
            }

            private void PushTail(Vector3 position)
            {
                _tails[_tailHead] = position;
                _tailHead = (_tailHead + 1) % MaxTails;
                if (_tailCount < MaxTails)
                    _tailCount++;
            }
        }
    }
}
