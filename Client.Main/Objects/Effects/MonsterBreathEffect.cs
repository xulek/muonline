using System;
using System.Collections.Generic;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Reusable breath/smoke-from-mouth effect that emits particles from a bone
    /// during configurable animation frame windows.
    ///
    /// Mirrors original C++:  if (CurrentAction == X && AnimationFrame in [a,b]) { CreateParticle(BITMAP_SMOKE, bonePos, ...) }
    ///
    /// Example — BullFighter snort smoke from bone 24 during idle/walk frames.
    /// </summary>
    public class MonsterBreathEffect : EffectObject
    {
        private const int MaxParticles = 32;

        // --- owned particle state ---
        private readonly BreathParticle[] _particles = new BreathParticle[MaxParticles];
        private int _activeCount;

        private const string SmokeTexturePath = "Effect/smoke01.jpg";

        private Texture2D _texture;
        private Vector2 _texCenter;

        // --- emission accumulator ---
        private float _emissionAccumulator;

        // ========================================================================
        // Configuration
        // ========================================================================

        /// <summary>Bone index the breath comes from (e.g. 24 for BullFighter mouth).</summary>
        public int SourceBone { get; set; } = -1;

        /// <summary>Emit while no animation windows are configured.</summary>
        public bool EmitWhenNoTriggers { get; set; }

        /// <summary>Local offset applied before transforming the source bone.</summary>
        public Vector3 SourceOffset { get; set; } = new(0f, -4f, 0f);

        /// <summary>How many particles per second during active trigger windows.</summary>
        public float EmissionRate { get; set; } = 12.5f;

        /// <summary>Base color of the smoke particles.</summary>
        public Color BreathColor { get; set; } = Color.White;

        /// <summary>How long each particle lives (seconds).</summary>
        public float ParticleLifetime { get; set; } = 16f / 25f;

        /// <summary>Particle scale range.</summary>
        public float MinScale { get; set; } = 0.48f;
        public float MaxScale { get; set; } = 0.79f;

        /// <summary>Max camera distance before culling.</summary>
        public float MaxDistance { get; set; } = 2000f;

        /// <summary>Reference distance for perspective scale calculation.</summary>
        public float ReferenceDistance { get; set; } = 800f;

        /// <summary>Minimum scale multiplier at max distance.</summary>
        public float MinDistanceScale { get; set; } = 0.3f;

        /// <summary>
        /// Animation trigger windows. Each entry defines an action index and a frame range
        /// during which breath particles are emitted. Time is accumulated only while the parent
        /// is within a matching window.
        /// </summary>
        public List<BreathTrigger> Triggers { get; set; } = new();

        // ========================================================================
        // Constructor
        // ========================================================================

        public MonsterBreathEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
            if (_texture != null)
                _texCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        // ========================================================================
        // Update — age particles and emit new ones during trigger windows
        // ========================================================================

        public override void Update(GameTime gameTime)
        {
            var parentModel = Parent as ModelObject;
            if (parentModel != null)
            {
                Hidden = parentModel.Hidden || parentModel.Model == null;
                if (parentModel.LowQuality || Hidden)
                {
                    _activeCount = 0;
                    base.Update(gameTime);
                    return;
                }
            }

            if (Hidden || SourceBone < 0)
            {
                base.Update(gameTime);
                return;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f) { base.Update(gameTime); return; }

            // Age and cull particles
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                ref var p = ref _particles[i];
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    _activeCount--;
                    if (i != _activeCount)
                        _particles[i] = _particles[_activeCount];
                    _particles[_activeCount] = default;
                }
                else
                {
                    float frameFactor = dt * 25f;
                    p.Gravity += 0.2f * frameFactor;
                    p.Position.Z += p.Gravity * frameFactor;
                    p.Position += p.Velocity * dt;
                    p.Scale += 0.05f * frameFactor;
                }
            }

            // Check trigger windows and emit
            bool inWindow = Triggers.Count == 0
                ? EmitWhenNoTriggers
                : IsInTriggerWindow(parentModel);
            if (inWindow)
            {
                _emissionAccumulator += EmissionRate * dt;
                int emit = Math.Min((int)_emissionAccumulator, MaxParticles - _activeCount);
                _emissionAccumulator -= emit;

                Vector3 worldBonePos = GetBoneWorldPosition(parentModel);
                for (int e = 0; e < emit; e++)
                    SpawnParticle(worldBonePos);
            }
            else
            {
                _emissionAccumulator = 0f;
            }

            base.Update(gameTime);
        }

        // ========================================================================
        // DrawAfter — billboarded particle sprites
        // ========================================================================

        public override void DrawAfter(GameTime gameTime)
        {
            if (_activeCount == 0 || Hidden)
            {
                base.DrawAfter(gameTime);
                return;
            }

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var spriteBatch = GraphicsManager.Instance.Sprite;
            var camera = Camera.Instance;
            if (gd == null || spriteBatch == null || camera == null || _texture == null)
            {
                base.DrawAfter(gameTime);
                return;
            }

            var viewProj = camera.ViewProjection;
            var vp = gd.Viewport;
            float maxDistSq = MaxDistance * MaxDistance;

            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                this.BlendState ?? Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone);

            for (int i = 0; i < _activeCount; i++)
            {
                ref readonly var p = ref _particles[i];

                Vector3 toCam = p.Position - camera.Position;
                if (Vector3.Dot(toCam, Vector3.Normalize(camera.Target - camera.Position)) < 0f)
                    continue;

                float distSq = toCam.LengthSquared();
                if (distSq > maxDistSq)
                    continue;

                Vector4 clip = Vector4.Transform(p.Position, viewProj);
                if (clip.W <= 0.001f)
                    continue;

                float invW = 1f / clip.W;
                float sx = (clip.X * invW * 0.5f + 0.5f) * vp.Width;
                float sy = (0.5f - clip.Y * invW * 0.5f) * vp.Height;
                float depth = clip.Z * invW;
                if (depth < 0f || depth > 1f) continue;

                float lifeRatio = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                float luminosity = MathHelper.Clamp(p.Life * 25f / 8f, 0f, 1f);
                float distScale = MathHelper.Clamp(ReferenceDistance / clip.W, MinDistanceScale, 2.5f);
                float scale = p.Scale * distScale * Constants.RENDER_SCALE;
                Vector3 colorVector = p.Tint * luminosity;

                var color = new Color(
                    colorVector.X,
                    colorVector.Y,
                    colorVector.Z,
                    1f);

                spriteBatch.Draw(_texture, new Vector2(sx, sy), null, color,
                    p.Rotation, _texCenter, scale, SpriteEffects.None, depth);
            }

            spriteBatch.End();
            base.DrawAfter(gameTime);
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private bool IsInTriggerWindow(ModelObject parentModel)
        {
            if (Triggers.Count == 0)
                return false;

            double framePos = parentModel.GetLoopedAnimationTime();
            int currentAction = parentModel.CurrentAction;

            foreach (var t in Triggers)
            {
                if (currentAction == t.ActionIndex &&
                    framePos >= t.FrameStart && framePos <= t.FrameEnd)
                {
                    return true;
                }
            }
            return false;
        }

        private Vector3 GetBoneWorldPosition(ModelObject parentModel)
        {
            var bones = parentModel.GetBoneTransforms();
            if (bones == null || SourceBone < 0 || SourceBone >= bones.Length)
                return parentModel.WorldPosition.Translation;

            Vector3 bonePosition = Vector3.Transform(SourceOffset, bones[SourceBone]);
            return Vector3.Transform(bonePosition, parentModel.WorldPosition);
        }

        internal void EmitBurst(Color color, int count, float speed)
        {
            if (Parent is not ModelObject parentModel || _texture == null)
                return;

            Vector3 origin = GetBoneWorldPosition(parentModel);
            Vector3 tint = color.ToVector3();
            for (int i = 0; i < count && _activeCount < MaxParticles; i++)
            {
                float yaw = parentModel.Angle.Z + RandomRange(-0.3f, 0.3f);
                float velocity = speed * RandomRange(0.6f, 1.2f);
                SpawnParticle(origin, new Vector3(MathF.Sin(yaw) * velocity, -MathF.Cos(yaw) * velocity,
                    RandomRange(-25f, 55f)), tint);
            }
        }

        private void SpawnParticle(Vector3 origin, Vector3 velocity = default, Vector3? tint = null)
        {
            if (_activeCount >= MaxParticles)
                return;

            ref var p = ref _particles[_activeCount++];
            p.Position = origin;
            p.Velocity = velocity;
            p.Tint = tint ?? BreathColor.ToVector3();
            p.Gravity = RandomRange(2f, 2.9f);
            p.MaxLife = ParticleLifetime;
            p.Life = p.MaxLife;
            p.Scale = RandomRange(MinScale, MaxScale);
            p.Rotation = 0f;
        }

        private float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        public override void Dispose()
        {
            _texture = null;
            base.Dispose();
        }

        // ========================================================================
        // Types
        // ========================================================================

        private struct BreathParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Tint;
            public float Life;
            public float MaxLife;
            public float Scale;
            public float Rotation;
            public float Gravity;
        }
    }

    /// <summary>
    /// Defines an animation window during which breath particles are emitted.
    /// </summary>
    public class BreathTrigger
    {
        /// <summary>Animation action index (e.g. 0=Stop1, 2=Walk).</summary>
        public byte ActionIndex;

        /// <summary>Start of the animation frame range (inclusive).</summary>
        public float FrameStart;

        /// <summary>End of the animation frame range (inclusive).</summary>
        public float FrameEnd;
    }
}
