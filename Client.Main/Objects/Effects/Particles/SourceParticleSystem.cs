#nullable enable
using System;
using System.Runtime.CompilerServices;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Particles
{
    public abstract class SourceParticleSystem : EffectObject
    {
        private const float ActiveBoundsPadding = 300f;

        private BoundingBox _activeParticleBounds;
        private bool _hasActiveParticleBounds;


        internal readonly struct RenderContext
        {
            public RenderContext(Viewport viewport, Matrix viewProjection, Vector3 cameraPosition, Vector3 cameraForward, BoundingFrustum frustum)
            {
                Viewport = viewport;
                ViewProjection = viewProjection;
                CameraPosition = cameraPosition;
                CameraForward = cameraForward;
                Frustum = frustum;
            }

            public Viewport Viewport { get; }
            public Matrix ViewProjection { get; }
            public Vector3 CameraPosition { get; }
            public Vector3 CameraForward { get; }
            public BoundingFrustum Frustum { get; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RenderContext CreateRenderContext(Camera camera, Viewport viewport)
        {
            Vector3 direction = camera.Target - camera.Position;
            float lengthSquared = direction.LengthSquared();
            Vector3 forward = lengthSquared > 0.000001f
                ? direction * (1f / MathF.Sqrt(lengthSquared))
                : Vector3.UnitY;

            return new RenderContext(
                viewport,
                camera.ViewProjection,
                camera.Position,
                forward,
                camera.Frustum);
        }

        internal int ParticleBatchBlendKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeHelpers.GetHashCode(BlendState ?? Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        }

        internal int ParticleBatchTextureKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Texture2D texture = ParticleTexture;
                return texture == null ? 0 : RuntimeHelpers.GetHashCode(texture);
            }
        }

        internal BlendState ParticleBatchBlendState =>
            BlendState ?? Microsoft.Xna.Framework.Graphics.BlendState.Additive;

        internal bool CanUseDedicatedWorldBatch
        {
            get
            {
                if (!Constants.ENABLE_PARTICLE_BATCHING ||
                    IsTransparent ||
                    Status != GameControlStatus.Ready)
                {
                    return false;
                }

                BlendState blend = ParticleBatchBlendState;
                return blend.ColorBlendFunction == BlendFunction.Add &&
                       blend.ColorDestinationBlend == Blend.One &&
                       blend.ColorSourceBlend is Blend.One or Blend.SourceAlpha or Blend.SourceColor or Blend.InverseSourceAlpha;
            }
        }

        protected SourceParticleSystem(int capacity)
        {
            Particles = new SourceParticle[Math.Max(1, capacity)];
            IsTransparent = false;
            AffectedByTransparency = false;
            BlendState = BlendState.Additive;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-5000f, -5000f, -5000f),
                new Vector3(5000f, 5000f, 5000f));
        }

        protected SourceParticle[] Particles { get; }
        protected int ActiveCount { get; private set; }
        internal bool HasActiveParticles => ActiveCount > 0;

        public float MaxDistance { get; set; } = 1500f;
        public float ReferenceDistance { get; set; } = 800f;
        public float MinDistanceScale { get; set; } = 0.5f;
        public float ScaleGrowth { get; set; } = 0.4f;

        /// <summary>
        /// World units the depth test uses closer to the camera than the particle. A sprite has one depth, so a large
        /// one (a mist bank) is cut in a hard line where it passes through the ground or water; testing it as if it
        /// stood its own radius closer avoids the cut.
        /// </summary>
        public float DepthTowardCamera { get; set; }

        protected abstract Texture2D? ParticleTexture { get; }
        protected abstract Vector2 ParticleTextureCenter { get; }

        public int CreateParticle(
            int type,
            Vector3 position,
            Vector3 angle,
            Vector3 light,
            int subType = 0,
            float scale = 0f,
            WorldObject? owner = null)
        {
            if (ActiveCount >= Particles.Length)
                return -1;

            int index = ActiveCount++;
            ref SourceParticle particle = ref Particles[index];
            particle = new SourceParticle
            {
                Live = true,
                Type = type,
                TexType = type,
                SubType = subType,
                Scale = scale,
                Position = position,
                Angle = angle,
                Light = light,
                Alpha = 1f,
                LifeTime = 1f,
                MaxLifeTime = 1f,
                EnableMove = true,
                StartPosition = position,
                Owner = owner,
            };

            OnParticleCreated(ref particle);
            ExpandActiveBounds(particle.Position);
            return index;
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            OnBeforeParticlesUpdated(dt);
            UpdateParticles(dt);
        }

        public override void Draw(GameTime gameTime)
        {
            if (ActiveCount == 0 || Status != GameControlStatus.Ready)
                return;

            Camera camera = Camera.Instance;
            if (camera == null)
                return;

            GraphicsDevice device = GraphicsManager.Instance.GraphicsDevice;
            SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
            if (device == null || spriteBatch == null)
                return;

            RenderContext context = CreateRenderContext(camera, device.Viewport);
            if (!SpriteBatchScope.BatchIsBegun)
            {
                using var scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    ParticleBatchBlendState,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone);
                DrawIntoActiveBatch(context);
            }
            else
            {
                DrawIntoActiveBatch(context);
            }
        }

        internal int DrawIntoActiveBatch(in RenderContext context)
        {
            if (ActiveCount == 0 || Status != GameControlStatus.Ready)
                return 0;

            Texture2D texture = ParticleTexture;
            if (texture == null || texture.IsDisposed)
                return 0;

            if (_hasActiveParticleBounds &&
                context.Frustum.Contains(_activeParticleBounds) == ContainmentType.Disjoint)
            {
                return -1;
            }

            return DrawParticles(context, texture);
        }

        public override float Depth => Position.Y + Position.Z;

        protected virtual void OnBeforeParticlesUpdated(float dt)
        {
        }

        protected virtual void OnParticleCreated(ref SourceParticle particle)
        {
        }

        protected virtual void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            if (particle.EnableMove)
            {
                particle.Position += particle.Velocity * dt;
                particle.Velocity.Z += particle.Gravity * dt;
            }
        }

        protected virtual Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            new(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha * lifeRatio * lifeRatio);

        protected virtual float GetParticleScale(in SourceParticle particle, float lifeRatio, float distanceScale, float perspectiveScale)
        {
            float growth = 1f + ScaleGrowth * (1f - lifeRatio);
            return particle.Scale * growth * distanceScale * perspectiveScale;
        }

        protected virtual float GetParticleRotation(in SourceParticle particle) => particle.Rotation;

        protected virtual Rectangle? GetParticleSourceRectangle(Texture2D texture, in SourceParticle particle) => null;

        protected float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExpandActiveBounds(Vector3 position)
        {
            Vector3 padding = new(ActiveBoundsPadding);
            Vector3 particleMin = position - padding;
            Vector3 particleMax = position + padding;
            if (!_hasActiveParticleBounds)
            {
                _activeParticleBounds = new BoundingBox(particleMin, particleMax);
                _hasActiveParticleBounds = true;
                return;
            }

            _activeParticleBounds = new BoundingBox(
                Vector3.Min(_activeParticleBounds.Min, particleMin),
                Vector3.Max(_activeParticleBounds.Max, particleMax));
        }

        protected void UpdateParticles(float dt)
        {
            Vector3 min = new(float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity);
            int i = 0;
            while (i < ActiveCount)
            {
                ref SourceParticle particle = ref Particles[i];
                particle.LifeTime -= dt;

                if (!particle.Live || particle.LifeTime <= 0f)
                {
                    RemoveAt(i);
                    continue;
                }

                UpdateLiveParticle(ref particle, dt);
                min = Vector3.Min(min, particle.Position);
                max = Vector3.Max(max, particle.Position);
                i++;
            }

            _hasActiveParticleBounds = ActiveCount > 0;
            if (_hasActiveParticleBounds)
            {
                Vector3 padding = new(ActiveBoundsPadding);
                _activeParticleBounds = new BoundingBox(min - padding, max + padding);
            }
        }

        private int DrawParticles(in RenderContext context, Texture2D texture)
        {
            Viewport viewport = context.Viewport;
            Vector3 cameraPosition = context.CameraPosition;
            Vector3 forward = context.CameraForward;
            Matrix viewProjection = context.ViewProjection;
            Vector2 textureCenter = ParticleTextureCenter;
            float maxDistanceSq = MaxDistance * MaxDistance;
            float inverseMaxDistanceSq = maxDistanceSq > 0.0001f ? 1f / maxDistanceSq : 0f;
            float referenceDistance = ReferenceDistance;
            float renderScale = Constants.RENDER_SCALE;
            // Farthest a texel can be from the sprite's origin (rotated sprites reach their corners).
            float textureRadius = Vector2.Distance(Vector2.Zero, Vector2.Max(textureCenter, new Vector2(texture.Width, texture.Height) - textureCenter));
            int drawn = 0;

            for (int i = 0; i < ActiveCount; i++)
            {
                ref readonly SourceParticle particle = ref Particles[i];
                Vector3 toParticle = particle.Position - cameraPosition;
                if (Vector3.Dot(toParticle, forward) < 0f)
                    continue;

                float distanceSq = toParticle.LengthSquared();
                if (distanceSq > maxDistanceSq)
                    continue;

                Vector4 clipPosition = Vector4.Transform(particle.Position, viewProjection);
                if (clipPosition.W <= 0.001f)
                    continue;

                float invW = 1f / clipPosition.W;
                float screenX = (clipPosition.X * invW * 0.5f + 0.5f) * viewport.Width;
                float screenY = (0.5f - clipPosition.Y * invW * 0.5f) * viewport.Height;
                float depth = clipPosition.Z * invW;
                if (depth < 0f || depth > 1f)
                    continue;
                if (DepthTowardCamera > 0f)
                {
                    float distance = MathF.Sqrt(distanceSq);
                    Vector3 nearer = particle.Position - toParticle * (MathF.Min(DepthTowardCamera, distance * 0.9f) / distance);
                    Vector4 nearClip = Vector4.Transform(nearer, viewProjection);
                    if (nearClip.W > 0.001f)
                        depth = MathHelper.Clamp(nearClip.Z / nearClip.W, 0f, 1f);
                }

                float lifeRatio = particle.MaxLifeTime > 0f
                    ? MathHelper.Clamp(particle.LifeTime / particle.MaxLifeTime, 0f, 1f)
                    : 1f;
                float distanceScale = MathHelper.Lerp(1f, MinDistanceScale, distanceSq * inverseMaxDistanceSq);
                float perspectiveScale = MathF.Max(0.1f, referenceDistance * invW) * renderScale;
                float scale = GetParticleScale(particle, lifeRatio, distanceScale, perspectiveScale);

                // Skip only when the whole sprite is off screen: a fixed margin made big sprites (mist banks)
                // pop in and out at the screen edges.
                float margin = MathF.Max(100f, textureRadius * scale);
                if (screenX < -margin || screenX > viewport.Width + margin ||
                    screenY < -margin || screenY > viewport.Height + margin)
                {
                    continue;
                }

                GraphicsManager.Instance.Sprite.Draw(
                    texture,
                    new Vector2(screenX, screenY),
                    GetParticleSourceRectangle(texture, particle),
                    GetParticleColor(particle, lifeRatio),
                    GetParticleRotation(particle),
                    textureCenter,
                    scale,
                    SpriteEffects.None,
                    depth);
                drawn++;
            }

            return drawn;
        }

        private void RemoveAt(int index)
        {
            int last = --ActiveCount;
            if (index != last)
                Particles[index] = Particles[last];

            Particles[last] = default;
        }

    }
}
