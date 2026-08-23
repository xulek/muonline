#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.LandOfTrials
{
    /// <summary>
    /// M31HuntingGround::CreateMist — BITMAP_CLOUD puffs (Light 0.05/0.05/0.1, scale 0.4)
    /// spawned in a ring 200-650 units around the hero at terrain height.
    /// </summary>
    public sealed class LandOfTrialsMistSystem : SourceParticleSystem
    {
        private const string CloudTexturePath = "Effect/clouds.jpg";
        private const int MaxPuffs = 24;

        private readonly WalkableWorldControl _world;
        private Texture2D? _texture;
        private float _spawnAccumulator;

        public LandOfTrialsMistSystem(WalkableWorldControl world)
            : base(capacity: MaxPuffs)
        {
            _world = world;
            MaxDistance = 2200f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter =>
            _texture == null ? Vector2.Zero : new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(CloudTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (_texture == null || Status != GameControlStatus.Ready || walker == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // rand_fps_check(30) emission gate
            _spawnAccumulator += dt * LegacyFramesPerSecond / 30f;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                if (ActiveCount >= MaxPuffs)
                    break;

                double angle = MuGame.Random.NextDouble() * Math.PI * 2.0;
                float radius = 200f + (float)MuGame.Random.NextDouble() * 450f;
                var position = new Vector3(
                    walker.Position.X + MathF.Cos((float)angle) * radius,
                    walker.Position.Y + MathF.Sin((float)angle) * radius,
                    0f);

                if (_world.Terrain != null)
                {
                    float height = _world.Terrain.RequestTerrainHeight(position.X, position.Y);
                    if (float.IsFinite(height))
                        position.Z = height + MuGame.Random.Next(20);
                }

                CreateParticle(8, position, Vector3.Zero, new Vector3(0.05f, 0.05f, 0.1f), 8, 0.4f);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 6f;
            particle.MaxLifeTime = 6f;
            particle.Velocity = new Vector3(MuGame.Random.Next(15) - 7f, MuGame.Random.Next(15) - 7f, 4f);
            particle.Gravity = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Scale += 0.05f * dt;
        }

        private const float LegacyFramesPerSecond = 50f;
    }
}

