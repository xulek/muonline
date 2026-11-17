#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// Icarus wall flame (ZzzObject.cpp WD_10HEAVEN case 10): every legacy tick
    /// CreateParticleFpsChecked(BITMAP_LIGHT, bone 3, Light (1,1,1), SubType 0, Scale 1).
    /// BITMAP_LIGHT sub 0 (ZzzEffectParticle.cpp): Scale (50..99)% of 1, rises 2.5/tick with a small
    /// random drift, shrinks 0.05/tick, Light = Scale, dies at Scale &lt;= 0.1. Adds one static,
    /// softly flickering cool-white terrain light at the flame (not in the original).
    /// </summary>
    internal sealed class IcarusWallFlareEffect : SourceParticleSystem
    {
        private const int FlameBone = 3;
        private const float LightRadius = Constants.TERRAIN_SCALE * 5f;
        private const float LightIntensity = 1.8f;
        private const double LightStepSeconds = 1.0 / 25.0;
        private static readonly Vector3 LightTint = new(0.7f, 0.82f, 1f);

        private readonly ModelObject _owner;
        private readonly DynamicLight _light;
        private Texture2D? _texture;
        private Vector2 _textureCenter;
        private Vector3 _flamePosition;
        private bool _positioned;
        private bool _lightAdded;
        private double _lightAccumulator;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public IcarusWallFlareEffect(ModelObject owner)
            : base(24)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            MaxDistance = 2400f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;

            _light = new DynamicLight
            {
                Owner = this,
                Color = Vector3.Zero,
                Radius = LightRadius,
                Intensity = 0f,
            };
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/flare01.jpg"); // BITMAP_LIGHT
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            if (!_positioned)
                TryResolveFlamePosition();

            UpdateLight(gameTime);

            if (!_owner.Visible)
                return;

            UpdateParticles(FPSCounter.Instance.FPS_ANIMATION_FACTOR);

            if (_positioned && _texture != null && FPSCounter.Instance.RandFPSCheck(1))
            {
                float scale = MuGame.Random.Next(50, 100) / 100f;
                CreateParticle(0, _flamePosition, Vector3.Zero, new Vector3(scale), 0, scale);
            }
        }

        private void TryResolveFlamePosition()
        {
            Matrix[]? bones = _owner.GetBoneTransforms();
            if (bones == null || bones.Length <= FlameBone)
                return;

            _flamePosition = Vector3.Transform(bones[FlameBone].Translation, _owner.WorldPosition);
            _light.Position = _flamePosition;
            _positioned = true;
        }

        private void UpdateLight(GameTime gameTime)
        {
            if (!_positioned)
                return;

            if (!_lightAdded && World?.Terrain != null)
            {
                World.Terrain.AddDynamicLight(_light);
                _lightAdded = true;
            }

            _lightAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
            if (_lightAccumulator < LightStepSeconds)
                return;

            _lightAccumulator = 0d;
            float flicker = 0.85f + MuGame.Random.Next(31) * 0.01f; // 0.85..1.15
            _light.Color = LightTint * flicker;
            _light.Intensity = LightIntensity;
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            // Lives until the scale shrinks to 0.1 (at most ~18 ticks).
            particle.LifeTime = 40f;
            particle.MaxLifeTime = 40f;
            particle.Gravity = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float factor)
        {
            const float fScale = 0.5f;
            particle.Position.X += (MuGame.Random.Next(2001) - 1000) * (0.001f * 0.4f) * factor * fScale;
            particle.Position.Y += (MuGame.Random.Next(2001) - 1000) * (0.001f * 0.4f) * factor * fScale;
            particle.Position.Z += 5f * fScale * factor;
            particle.Scale -= 0.05f * factor;
            particle.Light = new Vector3(MathF.Max(particle.Scale, 0f));
            if (particle.Scale <= 0.1f)
                particle.Live = false;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            Vector3 light = Vector3.Clamp(particle.Light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        protected override float GetParticleScale(
            in SourceParticle particle,
            float lifeRatio,
            float distanceScale,
            float perspectiveScale) => particle.Scale * distanceScale * perspectiveScale;

        public override void Dispose()
        {
            if (_lightAdded)
            {
                World?.Terrain?.RemoveDynamicLight(_light);
                _lightAdded = false;
            }
            base.Dispose();
        }
    }
}
