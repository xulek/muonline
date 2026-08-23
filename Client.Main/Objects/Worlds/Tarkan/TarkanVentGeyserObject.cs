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
    /// Implements Object 83 in Tarkan: Periodic desert vent / geyser eruption every 10s.
    /// Emits smoke bursts (subtypes 8 and 4) and flying rock debris.
    /// SourceMain5.2 ZzzObject.cpp case 83.
    /// </summary>
    public class TarkanVentGeyserObject : ModelObject
    {
        private TarkanVentParticleSystem _particleSystem;

        public TarkanVentGeyserObject()
        {
            HiddenMesh = -2;
            Hidden = false;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object9/Object84.bmd");
            _particleSystem = new TarkanVentParticleSystem(this);
            Children.Add(_particleSystem);

            await base.Load();
        }
    }

    public sealed class TarkanVentParticleSystem : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke01.jpg";
        private const int Capacity = 128;
        private const float EmissionDistance = 3500f;

        private readonly ModelObject _owner;
        private readonly List<VentRockDebris> _rocks = new();
        private Texture2D _texture;
        private Vector2 _textureCenter;

        private double _globalMs;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public TarkanVentParticleSystem(ModelObject owner) : base(Capacity)
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
            // SourceMain: (int)WorldTime % 10000 — global clock keeps all vents in sync
            _globalMs = gameTime.TotalGameTime.TotalMilliseconds;
            base.Update(gameTime);

            for (int i = _rocks.Count - 1; i >= 0; i--)
            {
                var rock = _rocks[i];
                rock.Update(gameTime);
                if (rock.IsExpired)
                {
                    _owner.Children.Remove(rock);
                    rock.Dispose();
                    _rocks.RemoveAt(i);
                }
            }
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (_texture == null || _owner == null)
                return;

            Vector3 pos = _owner.WorldPosition.Translation;
            float angleDeg = MathHelper.ToDegrees(_owner.Angle.Z);
            int inter = (int)(angleDeg * 10f);
            int timing = (int)_globalMs % 10000;

            if (timing > 3500 + inter && timing < 4000 + inter)
            {
                // Main eruptive column
                CreateParticle(type: 8, position: pos, angle: _owner.Angle, light: Vector3.One);

                if (MuGame.Random.Next(3) == 0)
                {
                    Vector3 subPos = pos + new Vector3(
                        MuGame.Random.Next(-64, 64),
                        MuGame.Random.Next(-64, 64),
                        0f);

                    // SourceMain: CreateEffectFpsChecked(MODEL_STONE1 + rand()%2, ...)
                    SpawnRockDebris(subPos);
                    CreateParticle(type: 4, position: subPos, angle: _owner.Angle, light: Vector3.One);
                }
            }
        }

        private void SpawnRockDebris(Vector3 position)
        {
            var rock = new VentRockDebris
            {
                Position = position,
                ModelPath = (MuGame.Random.Next(2) + 1).ToString()
            };
            _rocks.Add(rock);
            _owner.Children.Add(rock);
            _ = rock.Load();
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float scale = _owner.Scale;
            if (particle.Type == 8)
            {
                particle.LifeTime = 24f / 25f;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (1.0f + (float)MuGame.Random.NextDouble() * 0.5f) * scale * 0.8f;
                particle.Velocity = new Vector3(0f, 0f, 8f + (float)MuGame.Random.NextDouble() * 4f);
                particle.Rotation = (float)(MuGame.Random.NextDouble() * Math.PI * 2);
            }
            else
            {
                // Subtype 4
                particle.LifeTime = 16f / 25f;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Scale = (0.5f + (float)MuGame.Random.NextDouble() * 0.3f) * scale * 0.5f;
                particle.Velocity = new Vector3(0f, 0f, 6f);
                particle.Rotation = (float)(MuGame.Random.NextDouble() * Math.PI * 2);
            }
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float legacyDelta = dt * 25f;
            particle.Position += particle.Velocity * legacyDelta;
            particle.Velocity *= MathF.Pow(0.95f, legacyDelta);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            float alpha = lifeRatio * 0.7f;
            return new Color(alpha, alpha * 0.9f, alpha * 0.8f, alpha);
        }
    }
}

