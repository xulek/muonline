#nullable enable
using System;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Soft ground puff under the hero's feet on each footstep (goal directive E):
    /// BlendState.NonPremultiplied, warm terrain-tinted color, compact scale 0.20-0.35,
    /// short ~0.5 s fade. Reuses the DarkHorseHoofDustEffect tuning.
    /// </summary>
    public sealed class PlayerFootstepDustEffect : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";

        private Texture2D _texture = null!;
        private Vector2 _textureCenter;

        public enum DustTone { Grass, Soil, Snow }

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public PlayerFootstepDustEffect()
            : base(capacity: 24)
        {
            BlendState = BlendState.NonPremultiplied;
            MaxDistance = 1200f;
            ReferenceDistance = 350f;
            ScaleGrowth = 0.4f;
        }

        public override async Task LoadContent()
        {
            await TextureLoader.Instance.Prepare(SmokeTexturePath);
            _texture = TextureLoader.Instance.GetTexture2D(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
            _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        public void EmitFootstep(Vector3 position, DustTone tone)
        {
            if (_texture == null || ActiveCount >= Particles.Length - 2)
                return;

            float groundZ = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(position.X, position.Y)
                : position.Z;

            Vector3 light = tone switch
            {
                DustTone.Grass => new Vector3(0.42f, 0.46f, 0.30f),
                DustTone.Snow => new Vector3(0.85f, 0.90f, 1.00f),
                _ => new Vector3(0.55f, 0.48f, 0.38f)
            };

            for (int i = 0; i < 2; i++)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        position.X + RandomRange(-10f, 10f),
                        position.Y + RandomRange(-10f, 10f),
                        groundZ + 4f),
                    angle: Vector3.Zero,
                    light: light);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float lifetime = RandomRange(0.40f, 0.60f);
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            particle.Scale = RandomRange(0.20f, 0.35f);
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            particle.Velocity = new Vector3(
                RandomRange(-5f, 5f),
                RandomRange(-5f, 5f),
                RandomRange(4f, 10f));
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
                    particle.Position.Z = groundZ + 2f;
            }
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            float alpha = MathHelper.Clamp(lifeRatio, 0f, 1f) * 0.40f;
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, alpha);
        }
    }
}
