#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Kalima
{
    /// <summary>
    /// GMHellas.cpp CreateBigMon/MoveBigMon: ambient MODEL_BAHAMUT (Monster34.bmd)
    /// whale gliding above the flooded ruins, spawned every 4000 ms behind-left of
    /// the hero, banking with a random-flip turn rate and fading out on expiry.
    /// </summary>
    public sealed class KalimaBahamutBoid : ModelObject
    {
        private const float LegacyFramesPerSecond = 25f;
        private const float FlyDistance = 3000f;

        private float _gravityDegrees;
        private float _lifeFrames = 200f;
        private float _timer;

        public bool IsFadedOut { get; private set; }

        public float Velocity { get; set; }

        public KalimaBahamutBoid()
        {
            RenderShadow = false;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster34.bmd");
            if (Model != null)
            {
                // Source MoveBigMon renders dragons/whales at PlaySpeed 0.5:
                // 0.5 * REFERENCE_FPS(25) = 12.5 animation keys per second
                CurrentAction = 0;
                AnimationSpeed = 12.5f;
            }
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || World?.Terrain == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            float legacyFrames = dt * LegacyFramesPerSecond;

            // rand_fps_check(5) flip of the bank direction
            if ((float)MuGame.Random.NextDouble() < dt * LegacyFramesPerSecond / 5f)
                _gravityDegrees = -_gravityDegrees;

            Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + MathHelper.ToRadians(_gravityDegrees) * legacyFrames);

            // Forward motion: MoveBoidGroup Velocity*25 units per legacy frame
            // Forward motion: repo convention (sinθ, -cosθ) — verified in-game against
            // Monster34.bmd authoring (source AngleMatrix is transposed vs standard RotZ).
            Vector3 forward = new(MathF.Sin(Angle.Z), -MathF.Cos(Angle.Z), 0f);
            Position += forward * (Velocity * 25f * LegacyFramesPerSecond) * dt;

            _timer += Scale * 0.05f * legacyFrames;
            float terrainHeight = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);
            float bobbing = -MathF.Abs(MathF.Sin(_timer)) * 100f + 100f;
            Position = new Vector3(Position.X, Position.Y, terrainHeight + 200f + bobbing);

            _lifeFrames -= legacyFrames;
            if (_lifeFrames < 20f && !IsFadedOut)
            {
                // Death fade: Alpha *= pow(1/1.2), climb and accelerate
                Alpha *= MathF.Pow(1f / 1.2f, legacyFrames);
                Velocity += 0.5f * legacyFrames;
                Angle = new Vector3(Angle.X + MathHelper.ToRadians(2f) * legacyFrames, Angle.Y, Angle.Z);
                if (Alpha <= 0.05f)
                    IsFadedOut = true;
            }
        }
    }

    /// <summary>
    /// Spawns at most two ambient Bahamut boids every 4000 ms (source BigMonInterval).
    /// </summary>
    public sealed class KalimaAmbientManager
    {
        private const int MaxAlive = 2;
        private const float SpawnIntervalSeconds = 4f;
        private const float DespawnDistance = 3000f;

        private readonly WalkableWorldControl _world;
        private readonly List<KalimaBahamutBoid> _boids = new();
        private readonly List<KalimaWisp> _wisps = new();
        private float _spawnCooldown;

        public KalimaAmbientManager(WalkableWorldControl world)
        {
            _world = world;
        }

        public void Update(GameTime gameTime)
        {
            if (_world.Status != GameControlStatus.Ready || _world.Walker == null)
                return;

            Vector3 heroPosition = _world.Walker.Position;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            for (int i = _boids.Count - 1; i >= 0; i--)
            {
                var boid = _boids[i];
                float distance = Vector2.Distance(
                    new Vector2(boid.Position.X, boid.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));

                if (boid.IsFadedOut || boid.Status == GameControlStatus.Disposed || distance > DespawnDistance)
                {
                    _world.Objects.Remove(boid);
                    boid.Dispose();
                    _boids.RemoveAt(i);
                }
            }

            _spawnCooldown -= dt;
            if (_spawnCooldown > 0f || _boids.Count >= MaxAlive)
                return;

            _spawnCooldown = SpawnIntervalSeconds;

            TrySpawnWisp(heroPosition);

            var boid2 = new KalimaBahamutBoid
            {
                // Source spawn: X-1000-rand%200, Y-500+rand%200 relative to the hero
                Position = new Vector3(
                    heroPosition.X - 1000f - MuGame.Random.Next(200),
                    heroPosition.Y - 500f + MuGame.Random.Next(200),
                    heroPosition.Z + 250f),
                Scale = 2.5f + (MuGame.Random.Next(3) + 6) * 0.05f,
                Velocity = (MuGame.Random.Next(10) + 10) * 0.04f,
                Alpha = 1f
            };

            float toHeroDx = heroPosition.X - boid2.Position.X;
            float toHeroDy = heroPosition.Y - boid2.Position.Y;
            float headingDeg = AngleUtils.CreateAngleDegrees(0f, 0f, toHeroDx, toHeroDy);
            boid2.Angle = new Vector3(0f, 0f, MathHelper.ToRadians(headingDeg));

            _boids.Add(boid2);
            _world.Objects.Add(boid2);
            _ = boid2.Load();
        }

        private void TrySpawnWisp(Vector3 heroPosition)
        {
            // Source: wisp slots 0-2, spawned near the hero on walkable tiles
            if (_wisps.Count >= 3)
                return;

            double angle = MuGame.Random.NextDouble() * Math.PI * 2.0;
            float distance = 400f + (float)MuGame.Random.NextDouble() * 500f;
            var spawnPos = new Vector3(
                heroPosition.X + MathF.Cos((float)angle) * distance,
                heroPosition.Y + MathF.Sin((float)angle) * distance,
                heroPosition.Z);

            const int terrainSize = Constants.TERRAIN_SIZE;
            int tileX = (int)(spawnPos.X / Constants.TERRAIN_SCALE);
            int tileY = (int)(spawnPos.Y / Constants.TERRAIN_SCALE);
            if (tileX < 0 || tileX >= terrainSize || tileY < 0 || tileY >= terrainSize)
                return;

            var flag = _world.Terrain.RequestTerrainFlag(tileX, tileY);
            if (flag.HasFlag(Client.Data.ATT.TWFlags.NoGround) || flag.HasFlag(Client.Data.ATT.TWFlags.SafeZone))
                return;

            float toHeroDx = heroPosition.X - spawnPos.X;
            float toHeroDy = heroPosition.Y - spawnPos.Y;
            float headingDeg = AngleUtils.CreateAngleDegrees(0f, 0f, toHeroDx, toHeroDy);

            var wisp = new KalimaWisp
            {
                Position = spawnPos,
                Angle = new Vector3(0f, 0f, MathHelper.ToRadians(headingDeg))
            };
            wisp.BeginLife(scale: 0.8f + (float)MuGame.Random.NextDouble() * 0.3f);

            _wisps.Add(wisp);
            _world.Objects.Add(wisp);
            _ = wisp.LoadContent();
        }

        public void Clear()
        {
            for (int i = 0; i < _boids.Count; i++)
            {
                var boid = _boids[i];
                _world.Objects.Remove(boid);
                boid.Dispose();
            }
            _boids.Clear();

            foreach (var wisp in _wisps)
            {
                _world.Objects.Remove(wisp);
                wisp.Dispose();
            }
            _wisps.Clear();
        }
    }

    /// <summary>
    /// GOBoid.cpp Hellas wisps: invisible-mesh boids rendered as BITMAP_FLARE+1 joints
    /// (size 50), Gravity=9 turn rate, Velocity 2.5/scale, LifeTime 70 legacy frames.
    /// </summary>
    public sealed class KalimaWisp : EffectObject
    {
        private const float LegacyFramesPerSecond = 25f;
        private const string FlareTexturePath = "Effect/flare01.jpg";

        private float _turnDegrees;
        private float _lifeFrames;
        private Texture2D? _flareTexture;
        private bool _textureRequested;

        public float Speed { get; set; }

        public KalimaWisp()
        {
            Alpha = 0f;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (!_textureRequested)
            {
                _textureRequested = true;
                _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture(FlareTexturePath);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || World?.Terrain == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            float legacyFrames = dt * LegacyFramesPerSecond;

            // Gravity=9 max-turn wander with random flips
            if ((float)MuGame.Random.NextDouble() < dt * 1.5f)
                _turnDegrees = -_turnDegrees;

            Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + MathHelper.ToRadians(_turnDegrees) * legacyFrames);

            Vector3 forward = new(MathF.Sin(Angle.Z), -MathF.Cos(Angle.Z), 0f);
            Position += forward * Speed * LegacyFramesPerSecond * dt * 0.5f;

            float terrainHeight = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);
            Position = new Vector3(Position.X, Position.Y, MathF.Max(Position.Z, terrainHeight + 20f));

            // Fade-in then fade-out across the 70-frame life
            if (_lifeFrames > 15f)
                Alpha = MathF.Min(1f, Alpha + dt * 2f);
            else
                Alpha -= dt / 0.3f;

            _lifeFrames -= legacyFrames;
            if (_lifeFrames <= 0f || Alpha <= 0.02f)
                IsFadedOut = true;
        }

        public bool IsFadedOut { get; private set; }

        public void BeginLife(float scale)
        {
            Scale = MathF.Max(0.4f, scale);
            Speed = 2.5f / Scale;
            _lifeFrames = 70f;
            _turnDegrees = 9f;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || Camera.Instance == null || _flareTexture == null)
                return;

            var spriteBatch = Controllers.GraphicsManager.Instance.Sprite;
            var device = Controllers.GraphicsManager.Instance.GraphicsDevice;
            if (spriteBatch == null || device == null)
                return;

            Vector3 projected = device.Viewport.Project(
                Position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            float sizePx = 50f * 0.25f * (float)Math.Max(0.2, projected.Z / 800.0);

            void drawFlare()
            {
                spriteBatch.Draw(
                    _flareTexture,
                    new Vector2(projected.X, projected.Y),
                    null,
                    new Color(1f, 1f, 1f, MathHelper.Clamp(Alpha, 0f, 1f)),
                    0f,
                    new Vector2(_flareTexture.Width * 0.5f, _flareTexture.Height * 0.5f),
                    sizePx / _flareTexture.Width,
                    SpriteEffects.None,
                    projected.Z);
            }

            if (!Helpers.SpriteBatchScope.BatchIsBegun)
            {
                using (new Helpers.SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    Graphics.Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    drawFlare();
                }
            }
            else
            {
                drawFlare();
            }
        }
    }
}




