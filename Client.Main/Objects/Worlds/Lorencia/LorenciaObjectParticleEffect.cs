#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Lorencia
{
    internal enum LorenciaObjectEffectKind
    {
        Fire,
        Smoke,
        SmokeSubtype2,
    }

    /// <summary>
    /// SourceMain's CreateFire/CreateParticle emitter used by Lorencia map objects.
    /// The source evaluates the emitter every object update and gates particles with
    /// rand_fps_check(2), from a rotated local offset.
    /// </summary>
    internal sealed class LorenciaObjectParticleEffect : SourceParticleSystem
    {
        private const float DefaultLightRadius = Constants.TERRAIN_SCALE * 4f;
        private const double LightStepSeconds = 1.0 / 25.0;

        private readonly ModelObject _owner;
        private Vector3 _localOffset;
        private readonly DynamicLight _terrainLight;
        private Texture2D? _texture;
        private Vector2 _textureCenter;
        private double _lightAccumulator = LightStepSeconds;

        public LorenciaObjectEffectKind Kind { get; set; }
        public Vector3 LocalOffset { get => _localOffset; set => _localOffset = value; }

        /// <summary>Radius of the CreateFire terrain light (source: AddTerrainLight range 4).</summary>
        public float LightRadius { get => _terrainLight.Radius; set => _terrainLight.Radius = value; }

        /// <summary>Per-channel multiplier of the flickering luminosity used for the terrain light.</summary>
        public Vector3 LightTint { get; set; } = new(1f, 0.6f, 0.4f);

        public float LightIntensity { get; set; } = 1f;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public LorenciaObjectParticleEffect(
            ModelObject owner,
            LorenciaObjectEffectKind kind,
            Vector3 localOffset)
            : base(64)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Kind = kind;
            _localOffset = localOffset;

            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            MaxDistance = 2400f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;

            _terrainLight = new DynamicLight
            {
                Owner = this,
                Color = Vector3.Zero,
                Radius = DefaultLightRadius,
                Intensity = 0f,
            };
        }

        public override async Task LoadContent()
        {
            string texturePath = Kind == LorenciaObjectEffectKind.Fire
                ? "Effect/Fire01.jpg"
                : "Effect/smoke01.jpg";

            _texture = await TextureLoader.Instance.PrepareAndGetTexture(texturePath);
            if (_texture != null)
            {
                _textureCenter = Kind == LorenciaObjectEffectKind.Fire
                    ? new Vector2(_texture.Width * 0.125f, _texture.Height * 0.5f)
                    : new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            if (Kind == LorenciaObjectEffectKind.Fire && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_terrainLight);
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Camera.Instance == null || !_owner.Visible)
                return;

            float animationFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            UpdateParticles(animationFactor);
            _lightAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
            EmitLegacyTick();
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            if (Kind == LorenciaObjectEffectKind.Fire)
            {
                particle.LifeTime = 24f;
                particle.MaxLifeTime = 24f;
                particle.Velocity = particle.SubType switch
                {
                    0 or 1 => new Vector3(
                        0f,
                        -MuGame.Random.Next(32, 48) * 0.1f,
                        0f),
                    _ => new Vector3(
                        0f,
                        -(MuGame.Random.Next(32) - 16) * 0.1f,
                        0f),
                };

                if (particle.SubType == 0)
                    particle.Scale = MuGame.Random.Next(128, 192) * 0.01f;
                else if (particle.SubType == 1)
                    particle.Scale = MuGame.Random.Next(10, 14) * 0.01f;

                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                particle.Gravity = 0f;
                return;
            }

            particle.LifeTime = 16f;
            particle.MaxLifeTime = 16f;
            particle.Scale = MuGame.Random.Next(48, 80) * 0.01f;
            particle.Velocity = Vector3.Zero;
            particle.Gravity = 0f;
            particle.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float animationFactor)
        {
            if (Kind == LorenciaObjectEffectKind.Fire)
            {
                Matrix rotation = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(particle.Angle));
                particle.Position += Vector3.TransformNormal(particle.Velocity, rotation) * animationFactor;
                particle.Gravity += 0.004f * animationFactor;
                particle.Position.Z += particle.Gravity * 10f * animationFactor;
                particle.Scale -= 0.04f * animationFactor;
                particle.Frame = (int)((23f - particle.LifeTime) / 6f);
                return;
            }

            float luminosity = particle.LifeTime / 8f;
            particle.Light = new Vector3(luminosity);
            particle.Gravity += 0.2f * animationFactor;
            particle.Position.Z += particle.Gravity * animationFactor;
            particle.Scale += 0.05f * animationFactor;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            Vector3 light = particle.Light;

            light = Vector3.Clamp(light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        protected override float GetParticleRotation(in SourceParticle particle) =>
            Kind == LorenciaObjectEffectKind.Fire
                ? particle.Angle.X
                : base.GetParticleRotation(particle);

        protected override Rectangle? GetParticleSourceRectangle(
            Texture2D texture,
            in SourceParticle particle)
        {
            if (Kind != LorenciaObjectEffectKind.Fire)
                return base.GetParticleSourceRectangle(texture, particle);

            int frameWidth = texture.Width / 4;
            if (frameWidth <= 0)
                return null;

            int frame = Math.Clamp(particle.Frame, 0, 3);
            return new Rectangle(frame * frameWidth, 0, frameWidth, texture.Height);
        }

        protected override float GetParticleScale(
            in SourceParticle particle,
            float lifeRatio,
            float distanceScale,
            float perspectiveScale) => particle.Scale * distanceScale * perspectiveScale;

        private void EmitLegacyTick()
        {
            if (_texture == null || _owner.World == null)
                return;

            Matrix rotation = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(_owner.Angle));
            Vector3 flamePosition = _owner.WorldPosition.Translation +
                                    Vector3.TransformNormal(_localOffset, rotation);
            Vector3 position = flamePosition + Vector3.TransformNormal(new Vector3(
                MuGame.Random.Next(-8, 8),
                MuGame.Random.Next(-8, 8),
                MuGame.Random.Next(-8, 8)), rotation);

            if (Kind == LorenciaObjectEffectKind.Fire)
            {
                float luminosity = MuGame.Random.Next(6, 12) * 0.1f;
                Vector3 light = new(luminosity, luminosity * 0.6f, luminosity * 0.4f);

                // The terrain light stays at the un-jittered flame position (static for the
                // light selection cache); only its colour flickers, at the legacy 25 Hz step.
                if (_terrainLight.Position != flamePosition)
                    _terrainLight.Position = flamePosition;
                if (_lightAccumulator >= LightStepSeconds)
                {
                    _lightAccumulator = 0d;
                    _terrainLight.Color = LightTint * luminosity;
                    _terrainLight.Intensity = LightIntensity;
                }

                if (FPSCounter.Instance.RandFPSCheck(2))
                {
                    CreateParticle(
                        0,
                        position,
                        _owner.Angle,
                        light,
                        MuGame.Random.Next(4),
                        scale: 1f);
                }
            }
            else if (FPSCounter.Instance.RandFPSCheck(2))
            {
                CreateParticle(0, position, _owner.Angle, _owner.Light,
                    Kind == LorenciaObjectEffectKind.SmokeSubtype2 ? 2 : 0);
            }
        }
    }

    internal sealed class LorenciaLightSpriteEffect : SpriteObject
    {
        public override string TexturePath => "Effect/flare01.jpg";

        public LorenciaLightSpriteEffect()
        {
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            LightEnabled = true;
            IsTransparent = true;
            AffectedByTransparency = false;
        }
    }
}
