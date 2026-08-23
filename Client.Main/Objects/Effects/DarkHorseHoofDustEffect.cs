#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Natural dust kicked up under vehicle hooves/feet while moving.
    /// Recreates the subtle, non-glowing ground sand/dirt puff from SourceMain5.2 GOBoid.cpp.
    /// </summary>
    public sealed class DarkHorseHoofDustEffect : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private const int MaxParticles = 48;
        private const float EmissionRate = 10f; // ~10 puffs per second while running

        private Texture2D _texture = null!;
        private Vector2 _textureCenter;
        private float _emissionAccumulator;

        /// <summary>True while the mount is running; the vehicle toggles this.</summary>
        public bool Emitting { get; set; }

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public DarkHorseHoofDustEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.NonPremultiplied; // Natural semi-transparent dust (not neon glowing additive)
            MaxDistance = 1800f;
            ReferenceDistance = 350f;
            ScaleGrowth = 0.4f;
        }

        public override async Task LoadContent()
        {
            await TextureLoader.Instance.Prepare(SmokeTexturePath);
            _texture = TextureLoader.Instance.GetTexture2D(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
            _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (!Emitting || _texture == null)
                return;

            _emissionAccumulator += EmissionRate * dt;
            int emitCount = (int)_emissionAccumulator;
            if (emitCount <= 0)
                return;
            _emissionAccumulator -= emitCount;

            Matrix worldMatrix = WorldPosition;
            Vector3 mountPos = worldMatrix.Translation;
            float groundZ = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(mountPos.X, mountPos.Y)
                : mountPos.Z;

            for (int i = 0; i < emitCount; i++)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        mountPos.X + RandomRange(-24f, 24f),
                        mountPos.Y + RandomRange(-24f, 24f),
                        groundZ + 4f),
                    angle: Vector3.Zero,
                    light: new Vector3(0.55f, 0.48f, 0.38f)); // Warm sand/dirt tone
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float lifetime = RandomRange(0.45f, 0.65f); // Short, crisp lifetime (doesn't linger like a smoke wall)
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            particle.Scale = RandomRange(0.20f, 0.35f); // Compact ground puff
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            particle.Velocity = new Vector3(
                RandomRange(-6f, 6f),
                RandomRange(-6f, 6f),
                RandomRange(4f, 12f));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Velocity *= MathF.Pow(0.85f, dt * 25f);
            particle.Rotation += dt * 0.5f;

            if (World?.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(particle.Position.X, particle.Position.Y);
                if (particle.Position.Z < groundZ + 2f)
                {
                    particle.Position.Z = groundZ + 2f;
                }
            }
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            float alpha = MathHelper.Clamp(lifeRatio, 0f, 1f) * 0.45f;
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, alpha);
        }
    }
}
