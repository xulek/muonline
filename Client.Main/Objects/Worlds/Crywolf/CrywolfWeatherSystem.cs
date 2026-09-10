#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Worlds.Events;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Crywolf
{
    /// <summary>
    /// SourceMain5.2 M34CryWolf1st weather state machine: weather = rand()%3 chosen at
    /// boot and re-rolled every 70000 legacy frames inside RenderCryWolf1stObjectVisual.
    /// 0 = ash/smoke haze (RenderBaseSmoke tint 0.4/0.4/0.45),
    /// 1 = rain (CreateDevilSquareRain, iMaxLeaves=60),
    /// 2 = mist (CreateMist: BITMAP_CLOUD puffs, Light 0.07 grey, scale 0.4, budget 50).
    /// </summary>
    public sealed class CrywolfWeatherSystem : EffectObject
    {
        private const float LegacyFramesPerSecond = 50f;
        private const float WeatherRerollFrames = 70000f;

        private readonly WalkableWorldControl _world;
        private readonly Random _sharedRandom;

        private ScrollingSmokeOverlay? _smokeOverlay;
        private EventRainSystem? _rainSystem;
        private CrywolfMistEmitter? _mistEmitter;

        private int _weather;
        private float _rerollAccumulatorFrames;
        private int _activeWeather = -1;

        public CrywolfWeatherSystem(WalkableWorldControl world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sharedRandom = MuGame.Random;
            _weather = _sharedRandom.Next(3);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            await ApplyWeather(_weather);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _rerollAccumulatorFrames += dt * LegacyFramesPerSecond;
            if (_rerollAccumulatorFrames >= WeatherRerollFrames && _sharedRandom.Next(3) == 0)
            {
                // Source re-rolls only after the 70000-frame threshold; keep cadence faithful
                // with a light random gate so the switch is not frame-exact.
                _rerollAccumulatorFrames = 0f;
                _ = ApplyWeather(_sharedRandom.Next(3));
            }
        }

        private async Task ApplyWeather(int weather)
        {
            if (_activeWeather == weather)
                return;

            _weather = weather;
            _activeWeather = weather;

            DeactivateAll();

            switch (weather)
            {
                case 0:
                    _smokeOverlay = ScrollingSmokeOverlay.CreateStandard(new Color(0.4f, 0.4f, 0.45f));
                    await _smokeOverlay.Load();
                    break;

                case 1:
                    _rainSystem = new EventRainSystem(
                        _world,
                        maxDrops: 60,
                        speedBonus: 0f,
                        streakLength: 20f,
                        spawnSplashes: true,
                        lightningFlicker: false);
                    Children.Add(_rainSystem);
                    await _rainSystem.LoadContent();
                    break;

                case 2:
                    _mistEmitter = new CrywolfMistEmitter(_world);
                    Children.Add(_mistEmitter);
                    await _mistEmitter.LoadContent();
                    break;
            }
        }

        private void DeactivateAll()
        {
            if (_rainSystem != null)
            {
                Children.Remove(_rainSystem);
                _rainSystem.Dispose();
                _rainSystem = null;
            }

            if (_mistEmitter != null)
            {
                Children.Remove(_mistEmitter);
                _mistEmitter.Dispose();
                _mistEmitter = null;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            _smokeOverlay?.DrawOverlay(gameTime);
        }

        public override void Dispose()
        {
            DeactivateAll();
            _smokeOverlay?.Dispose();
            _smokeOverlay = null;
            base.Dispose();
        }
    }

    /// <summary>
    /// M34CryWolf1st::CreateMist — slow drifting grey cloud puffs around the hero.
    /// </summary>
    internal sealed class CrywolfMistEmitter : SourceParticleSystem
    {
        private const string CloudTexturePath = "Effect/clouds.jpg";
        private const int MaxPuffs = 50;

        private Texture2D? _texture;
        private readonly WalkableWorldControl _world;
        private float _spawnAccumulator;

        public CrywolfMistEmitter(WalkableWorldControl world)
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

                SpawnPuff(walker.Position);
            }
        }

        private void SpawnPuff(Vector3 heroPosition)
        {
            // offsets (300..550)-200 -> ring radius 100..350, z = hero+400
            double angle = MuGame.Random.NextDouble() * Math.PI * 2.0;
            float radius = 100f + (float)MuGame.Random.NextDouble() * 250f;
            var position = new Vector3(
                heroPosition.X + MathF.Cos((float)angle) * radius,
                heroPosition.Y + MathF.Sin((float)angle) * radius,
                heroPosition.Z + 400f);

            CreateParticle(
                type: 8,
                position,
                angle: Vector3.Zero,
                light: new Vector3(0.07f, 0.07f, 0.07f),
                subType: 8,
                scale: 0.4f);
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 5f;
            particle.MaxLifeTime = 5f;
            particle.Velocity = new Vector3(
                MuGame.Random.Next(21) - 10f,
                MuGame.Random.Next(21) - 10f,
                -2f);
            particle.Gravity = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Scale += 0.06f * dt;
        }

        private const float LegacyFramesPerSecond = 50f;
    }
}



