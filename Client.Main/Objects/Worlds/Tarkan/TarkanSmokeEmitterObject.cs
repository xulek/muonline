using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements smoke emitter map objects in Tarkan (Objects 60, 70, 76).
    /// Object 60: Single burst of 20 smoke particles on start.
    /// Object 70: Continuous random smoke.
    /// Object 76: Periodic smoke burst every 5s.
    /// SourceMain5.2 ZzzObject.cpp.
    /// </summary>
    public class TarkanSmokeEmitterObject : ModelObject
    {
        private TarkanSmokeParticleSystem _particleSystem;

        public TarkanSmokeEmitterObject()
        {
            HiddenMesh = -2;
            Hidden = false;
        }

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object9/Object{idx}.bmd");

            _particleSystem = new TarkanSmokeParticleSystem(this);
            Children.Add(_particleSystem);

            await base.Load();
        }
    }

    public sealed class TarkanSmokeParticleSystem : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke01.jpg";
        private const int Capacity = 128;
        private const float EmissionDistance = 3500f;

        private readonly ModelObject _owner;
        private Texture2D _texture;
        private Vector2 _textureCenter;
        private bool _hasEmittedInitial;
        private double _globalMs;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public TarkanSmokeParticleSystem(ModelObject owner) : base(Capacity)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            MaxDistance = EmissionDistance;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        public override void Update(GameTime gameTime)
        {
            // SourceMain gates use the GLOBAL WorldTime clock so all Type-76 vents
            // burst in sync: (int)WorldTime % 5000 > 4500
            _globalMs = gameTime.TotalGameTime.TotalMilliseconds;
            base.Update(gameTime);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (_texture == null || _owner == null)
                return;

            Vector3 pos = _owner.WorldPosition.Translation;

            // Object 60: One-time burst of 20 particles
            if (_owner.Type == 60)
            {
                if (!_hasEmittedInitial)
                {
                    _hasEmittedInitial = true;
                    for (int i = 0; i < 20; i++)
                    {
                        Vector3 particlePos = pos + new Vector3(
                            MuGame.Random.Next(-100, 100) * _owner.Scale,
                            MuGame.Random.Next(-100, 100) * _owner.Scale,
                            MuGame.Random.Next(20, 40));
                        CreateParticle(type: 6, position: particlePos, angle: _owner.Angle, light: Vector3.One);
                    }
                }
                return;
            }

            // Object 70: Continuous smoke
            if (_owner.Type == 70)
            {
                if (MuGame.Random.Next(5) == 0)
                {
                    CreateParticle(type: 7, position: pos, angle: _owner.Angle, light: Vector3.One);
                }
                return;
            }

            // Object 76: Burst every 5 seconds (when WorldTime % 5000 > 4500)
            if (_owner.Type == 76)
            {
                if (_globalMs % 5000.0 > 4500.0)
                {
                    CreateParticle(type: 4, position: pos, angle: _owner.Angle, light: Vector3.One);
                }
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float scale = _owner.Scale;
            if (particle.Type == 6)
            {
                particle.LifeTime = 30f / 25f;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (0.8f + (float)MuGame.Random.NextDouble() * 0.4f) * scale;
                particle.Velocity = new Vector3(0f, 0f, 1f);
                particle.Rotation = (float)(MuGame.Random.NextDouble() * Math.PI * 2);
            }
            else if (particle.Type == 7)
            {
                particle.LifeTime = 30f / 25f;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (0.8f + (float)MuGame.Random.NextDouble() * 0.4f) * scale;
                particle.Velocity = new Vector3(
                    (float)(MuGame.Random.NextDouble() * 2 - 1) * 2f,
                    (float)(MuGame.Random.NextDouble() * 2 - 1) * 2f,
                    4f + (float)MuGame.Random.NextDouble() * 4f);
                particle.Rotation = (float)(MuGame.Random.NextDouble() * Math.PI * 2);
            }
            else
            {
                // Subtype 4
                particle.LifeTime = 16f / 25f;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (0.5f + (float)MuGame.Random.NextDouble() * 0.4f) * scale;
                particle.Velocity = new Vector3(0f, 0f, 6f);
                particle.Rotation = (float)(MuGame.Random.NextDouble() * Math.PI * 2);
            }
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float legacyDelta = dt * 25f;
            particle.Position += particle.Velocity * legacyDelta;
            particle.Velocity *= MathF.Pow(0.96f, legacyDelta);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            float alpha = lifeRatio * 0.6f;
            return new Color(alpha, alpha * 0.9f, alpha * 0.7f, alpha);
        }
    }
}
