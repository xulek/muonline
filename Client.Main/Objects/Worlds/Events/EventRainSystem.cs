#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Events
{
    /// <summary>
    /// Faithful recreation of SourceMain5.2 CreateDevilSquareRain / CreateChaosCastleRain
    /// plus the global rain oscillators (RainSpeed / RainAngle / RainPosition).
    /// Renders camera-facing streak quads tilted by the per-drop wind angle and
    /// expanding ground splash rings on terrain contact.
    /// </summary>
    public sealed class EventRainSystem : EffectObject
    {
        private const string RainTexturePath = "World1/rain01.tga";
        private const string SplashTextureAPath = "World1/rain02.tga";
        private const string SplashTextureBPath = "World10/rain03.tga";

        private const float LegacyFramesPerSecond = 50f;

        private struct Drop
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TiltRadians;
        }

        private struct Splash
        {
            public Vector3 Position;
            public float Age;
            public bool AlternateTexture;
        }

        private readonly WalkableWorldControl _world;
        private readonly int _maxDrops;
        private readonly float _speedBonus;
        private readonly float _streakLength;
        private readonly bool _spawnSplashes;
        private readonly bool _lightningFlicker;

        private Texture2D? _rainTexture;
        private Texture2D? _splashTextureA;
        private Texture2D? _splashTextureB;
        private readonly Drop[] _drops;
        private int _dropCount;
        private readonly Splash[] _splashes;
        private int _splashCount;
        private readonly DynamicLight? _flashLight;
        private Vector3 _flashColor = Vector3.Zero;
        private float _flashRemaining;

        public EventRainSystem(
            WalkableWorldControl world,
            int maxDrops,
            float speedBonus,
            float streakLength,
            bool spawnSplashes,
            bool lightningFlicker)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _maxDrops = Math.Max(1, maxDrops);
            _speedBonus = speedBonus;
            _streakLength = streakLength;
            _spawnSplashes = spawnSplashes;
            _lightningFlicker = lightningFlicker;

            _drops = new Drop[_maxDrops];
            _splashes = new Splash[32];

            if (lightningFlicker)
            {
                _flashLight = new DynamicLight
                {
                    Owner = this,
                    Radius = Constants.TERRAIN_SCALE * 12f,
                    Color = Vector3.Zero,
                    Intensity = 0f
                };
            }
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _rainTexture = await TextureLoader.Instance.PrepareAndGetTexture(RainTexturePath);
            if (_spawnSplashes)
            {
                _splashTextureA = await TextureLoader.Instance.PrepareAndGetTexture(SplashTextureAPath);
                _splashTextureB = await TextureLoader.Instance.PrepareAndGetTexture(SplashTextureBPath);
            }

            if (_flashLight != null && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_flashLight);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (Status != GameControlStatus.Ready || walker == null || Camera.Instance == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            Vector3 heroPosition = walker.Position;
            double worldMs = gameTime.TotalGameTime.TotalMilliseconds % 100000.0;

            SpawnMissingDrops(heroPosition, worldMs);

            for (int i = 0; i < _dropCount; i++)
            {
                ref Drop drop = ref _drops[i];
                drop.Position += drop.Velocity * dt;

                float groundZ = RequestGroundHeight(drop.Position.X, drop.Position.Y, heroPosition.Z);
                if (drop.Position.Z <= groundZ)
                {
                    if (_spawnSplashes && _splashCount < _splashes.Length &&
                        !IsNoGroundTile(drop.Position.X, drop.Position.Y))
                    {
                        _splashes[_splashCount++] = new Splash
                        {
                            Position = new Vector3(drop.Position.X, drop.Position.Y, groundZ + 4f),
                            Age = 0f,
                            AlternateTexture = MuGame.Random.Next(4) == 0
                        };
                    }

                    RespawnDrop(ref drop, heroPosition, worldMs);
                }

                _drops[i] = drop;
            }

            for (int i = _splashCount - 1; i >= 0; i--)
            {
                _splashes[i].Age += dt;
                if (_splashes[i].Age >= 0.35f)
                {
                    _splashes[i] = _splashes[--_splashCount];
                }
            }

            UpdateLightningFlash(gameTime, heroPosition, dt);
        }

        private void SpawnMissingDrops(Vector3 heroPosition, double worldMs)
        {
            while (_dropCount < _maxDrops)
            {
                ref Drop drop = ref _drops[_dropCount++];
                RespawnDrop(ref drop, heroPosition, worldMs);

                float groundZ = RequestGroundHeight(drop.Position.X, drop.Position.Y, heroPosition.Z);
                float span = MathF.Max(1f, drop.Position.Z - groundZ);
                drop.Position -= drop.Velocity * ((float)MuGame.Random.NextDouble() * span / -drop.Velocity.Z);
            }
        }

        private void RespawnDrop(ref Drop drop, Vector3 heroPosition, double worldMs)
        {
            // SourceMain: Hero + (rand()%1600-800), +(rand()%1400-500), +(rand()%200+300)
            float x = heroPosition.X + MuGame.Random.Next(1600) - 800f;
            float y = heroPosition.Y + MuGame.Random.Next(1400) - 500f;
            float z = heroPosition.Z + MuGame.Random.Next(200) + 300f;

            // Global oscillators:
            // RainSpeed = ((int)sinf(WorldTime * 0.001f) * 10 + 30)
            // RainAngle = (int)sinf(WorldTime * 0.0005f + 50.f) * 20
            float ms = (float)worldMs;
            float rainSpeed = (int)MathF.Sin(ms * 0.001f) * 10f + 30f;
            float rainAngle = (int)MathF.Sin(ms * 0.0005f + 50f) * 20f;

            float tiltDegrees = MuGame.Random.Next(2) == 0
                ? -(MuGame.Random.Next(20) + 20f)
                : -(MuGame.Random.Next(20) + 30f + rainAngle);
            float tiltRadians = MathHelper.ToRadians(tiltDegrees);

            // Velocity.Z = -((rand()%40 + RainSpeed [+bonus]) ) legacy units/frame -> units/sec
            float fallSpeed = (MuGame.Random.Next(40) + rainSpeed + _speedBonus) * LegacyFramesPerSecond;

            drop.Position = new Vector3(x, y, z);
            drop.Velocity = new Vector3(MathF.Sin(-tiltRadians) * fallSpeed * 0.22f, 0f, -fallSpeed);
            drop.TiltRadians = tiltRadians;
        }

        private void UpdateLightningFlash(GameTime gameTime, Vector3 heroPosition, float dt)
        {
            if (_flashLight == null)
                return;

            if (_flashRemaining > 0f)
            {
                _flashRemaining -= dt;
                float fraction = MathHelper.Clamp(_flashRemaining / 0.22f, 0f, 1f);
                _flashLight.Color = _flashColor * fraction;
                _flashLight.Intensity = fraction > 0f ? 1f : 0f;
                return;
            }

            // rand_fps_check(100): expected once every 100 legacy frames (~2s)
            if ((float)MuGame.Random.NextDouble() < dt * LegacyFramesPerSecond / 100f)
            {
                float luminosity = (MuGame.Random.Next(12) + 4) * 0.1f;
                _flashColor = new Vector3(luminosity * 0.2f, luminosity * 0.3f, luminosity * 0.5f);
                _flashLight.Position = new Vector3(
                    heroPosition.X + MuGame.Random.Next(1200) - 600f,
                    heroPosition.Y + MuGame.Random.Next(1200) - 600f,
                    heroPosition.Z + 150f);
                _flashRemaining = 0.22f;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if ((_dropCount == 0 && _splashCount == 0) || Status != GameControlStatus.Ready)
                return;

            Camera camera = Camera.Instance;
            var spriteBatch = GraphicsManager.Instance.Sprite;
            var device = GraphicsManager.Instance.GraphicsDevice;
            if (camera == null || spriteBatch == null || device == null)
                return;

            var viewport = device.Viewport;
            Matrix viewProjection = camera.View * camera.Projection;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.NonPremultiplied,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    DrawStreaks(spriteBatch, viewport, viewProjection, camera);
                }

                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.Additive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    DrawSplashes(spriteBatch, viewport, viewProjection, camera);
                }
            }
            else
            {
                DrawStreaks(spriteBatch, viewport, viewProjection, camera);
            }
        }

        private void DrawStreaks(SpriteBatch spriteBatch, Viewport viewport, Matrix viewProjection, Camera camera)
        {
            if (_rainTexture == null || _dropCount == 0)
                return;

            float pixelsPerUnit = ComputePixelsPerUnit(viewport, viewProjection, camera);
            if (pixelsPerUnit <= 0f)
                return;

            Vector2 center = new(_rainTexture.Width * 0.5f, _rainTexture.Height * 0.5f);
            float widthPx = 1f * pixelsPerUnit;
            float heightPx = _streakLength * pixelsPerUnit;

            for (int i = 0; i < _dropCount; i++)
            {
                ref readonly Drop drop = ref _drops[i];

                Vector4 clip = Vector4.Transform(drop.Position, viewProjection);
                if (clip.W <= 0.001f)
                    continue;

                float invW = 1f / clip.W;
                float screenX = (clip.X * invW * 0.5f + 0.5f) * viewport.Width;
                float screenY = (0.5f - clip.Y * invW * 0.5f) * viewport.Height;
                float depth = clip.Z * invW;
                if (depth < 0f || depth > 1f ||
                    screenX < -100f || screenX > viewport.Width + 100f ||
                    screenY < -100f || screenY > viewport.Height + 100f)
                {
                    continue;
                }

                spriteBatch.Draw(
                    _rainTexture,
                    new Vector2(screenX, screenY),
                    null,
                    new Color(0.85f, 0.88f, 1f, 0.72f),
                    drop.TiltRadians,
                    center,
                    new Vector2(widthPx / _rainTexture.Width, heightPx / _rainTexture.Height),
                    SpriteEffects.None,
                    depth);
            }
        }

        private void DrawSplashes(SpriteBatch spriteBatch, Viewport viewport, Matrix viewProjection, Camera camera)
        {
            if (_splashCount == 0)
                return;

            float pixelsPerUnit = ComputePixelsPerUnit(viewport, viewProjection, camera);
            if (pixelsPerUnit <= 0f)
                return;

            for (int i = 0; i < _splashCount; i++)
            {
                ref readonly Splash splash = ref _splashes[i];
                Texture2D? texture = splash.AlternateTexture ? _splashTextureB : _splashTextureA;
                if (texture == null)
                    continue;

                Vector4 clip = Vector4.Transform(splash.Position, viewProjection);
                if (clip.W <= 0.001f)
                    continue;

                float invW = 1f / clip.W;
                float screenX = (clip.X * invW * 0.5f + 0.5f) * viewport.Width;
                float screenY = (0.5f - clip.Y * invW * 0.5f) * viewport.Height;
                float depth = clip.Z * invW;
                if (depth < 0f || depth > 1f ||
                    screenX < -80f || screenX > viewport.Width + 80f ||
                    screenY < -80f || screenY > viewport.Height + 80f)
                {
                    continue;
                }

                float t = splash.Age / 0.35f;
                float scale = (6f + t * 34f) * pixelsPerUnit / texture.Width;
                float alpha = (1f - t) * 0.85f;

                spriteBatch.Draw(
                    texture,
                    new Vector2(screenX, screenY),
                    null,
                    new Color(1f, 1f, 1f, alpha),
                    0f,
                    new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                    scale,
                    SpriteEffects.None,
                    depth);
            }
        }

        private static float ComputePixelsPerUnit(Viewport viewport, Matrix viewProjection, Camera camera)
        {
            Vector3 forward = camera.Target - camera.Position;
            float lengthSq = forward.LengthSquared();
            if (lengthSq < 0.000001f)
                return 0f;
            forward *= 1f / MathF.Sqrt(lengthSq);

            Vector3 origin = camera.Position + forward * 600f;
            Vector4 c0 = Vector4.Transform(origin, viewProjection);
            Vector4 c1 = Vector4.Transform(origin + camera.Up * 100f, viewProjection);
            if (c0.W <= 0.001f || c1.W <= 0.001f)
                return 0f;

            float y0 = (0.5f - c0.Y / c0.W * 0.5f) * viewport.Height;
            float y1 = (0.5f - c1.Y / c1.W * 0.5f) * viewport.Height;
            return MathF.Abs(y1 - y0) / 100f;
        }

        private float RequestGroundHeight(float x, float y, float fallback)
        {
            var terrain = _world.Terrain;
            if (terrain != null)
            {
                float height = terrain.RequestTerrainHeight(x, y);
                if (float.IsFinite(height))
                    return height;
            }
            return fallback - 400f;
        }

        private bool IsNoGroundTile(float x, float y)
        {
            const int terrainSize = Constants.TERRAIN_SIZE;
            int tileX = (int)(x / Constants.TERRAIN_SCALE);
            int tileY = (int)(y / Constants.TERRAIN_SCALE);
            if (tileX < 0 || tileX >= terrainSize || tileY < 0 || tileY >= terrainSize)
                return true;

            return _world.Terrain.RequestTerrainFlag(tileX, tileY).HasFlag(Client.Data.ATT.TWFlags.NoGround);
        }

        public override void Dispose()
        {
            if (_flashLight != null && World?.Terrain != null)
                World.Terrain.RemoveDynamicLight(_flashLight);

            base.Dispose();
        }
    }
}
