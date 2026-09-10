#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Raklion
{
    /// <summary>
    /// CGM_Raklion atmosphere: RenderBaseSmoke (both layers additive, diagonal +u/-v drift)
    /// and CreateSnow (BITMAP_LEAF1 flakes, scale rand()%10+3 with 1-in-10 large flakes,
    /// tilt -(rand()%30+50) degrees, fall -(rand()%20+30)).
    /// </summary>
    public sealed class RaklionWeatherSystem : EffectObject
    {
        private const float LegacyFramesPerSecond = 50f;

        private struct Flake
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
        }

        private readonly WalkableWorldControl _world;
        private readonly Events.ScrollingSmokeOverlay _smokeOverlay;
        private readonly string _flakeTextureBase;
        private Texture2D? _flakeTexture;
        private readonly Flake[] _flakes;
        private bool _texturesReady;

        public RaklionWeatherSystem(WalkableWorldControl world, int flakeCount, string flakeTextureBase = "World58")
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _flakes = new Flake[Math.Max(1, flakeCount)];
            _flakeTextureBase = flakeTextureBase;

            _smokeOverlay = new Events.ScrollingSmokeOverlay(
                new Events.ScrollingSmokeOverlay.Layer
                {
                    TexturePath = "Effect/Map_Smoke2.tga",
                    Tint = new Color(0.4f, 0.4f, 0.45f),
                    USpeed = 0.0006f,
                    VSpeed = -0.0006f,
                    TileU = 3f,
                    TileV = 2f,
                    Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                },
                new Events.ScrollingSmokeOverlay.Layer
                {
                    TexturePath = "Effect/Map_Smoke1.jpg",
                    Tint = new Color(0.4f, 0.4f, 0.45f),
                    USpeed = 0.0001f,
                    VSpeed = 0f,
                    TileU = 0.3f,
                    TileV = 0.3f,
                    Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                });
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            await _smokeOverlay.Load();
            _flakeTexture = await LoadFlakeTexture();
            _texturesReady = true;
        }

        private async Task<Texture2D?> LoadFlakeTexture()
        {
            foreach (string path in new[] { $"{_flakeTextureBase}/leaf01.OZJ", $"{_flakeTextureBase}/leaf01.jpg" })
            {
                var texture = await TextureLoader.Instance.PrepareAndGetTexture(path);
                if (texture != null)
                    return texture;
            }
            return GraphicsManager.Instance.Pixel;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (!_texturesReady || Status != GameControlStatus.Ready || walker == null || Camera.Instance == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            Vector3 heroPosition = walker.Position;

            for (int i = 0; i < _flakes.Length; i++)
            {
                ref Flake flake = ref _flakes[i];

                if (flake.Velocity == Vector3.Zero)
                {
                    RespawnFlake(ref flake, heroPosition, prefillHeight: true);
                    continue;
                }

                flake.Position += flake.Velocity * dt;
                flake.Position.X += MathF.Sin(flake.Position.Z * 0.05f) * 8f * dt;

                if (flake.Position.Z <= heroPosition.Z - 300f ||
                    MathF.Abs(flake.Position.X - heroPosition.X) > 1000f ||
                    MathF.Abs(flake.Position.Y - heroPosition.Y) > 1100f)
                {
                    RespawnFlake(ref flake, heroPosition, prefillHeight: false);
                }
            }
        }

        private void RespawnFlake(ref Flake flake, Vector3 heroPosition, bool prefillHeight)
        {
            // Scale = rand()%10+3 ; every 10th flake rand()%3+10
            // Angle = -(rand()%30+50) degrees ; fall speed = rand()%20+30 legacy units/frame
            float scale = MuGame.Random.Next(10) == 0
                ? MuGame.Random.Next(3) + 10
                : MuGame.Random.Next(10) + 3;
            float tiltDegrees = -(MuGame.Random.Next(30) + 50);
            float fallSpeed = (MuGame.Random.Next(20) + 30) * LegacyFramesPerSecond * 0.35f;

            flake.Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(1600) - 800f,
                heroPosition.Y + MuGame.Random.Next(1400) - 500f,
                heroPosition.Z + 200f + MuGame.Random.Next(500));
            flake.Scale = scale;
            flake.Velocity = new Vector3(
                MathF.Cos(MathHelper.ToRadians(tiltDegrees)) * fallSpeed,
                0f,
                -fallSpeed);

            if (prefillHeight)
            {
                float drop = (float)MuGame.Random.NextDouble() * 700f;
                flake.Position -= flake.Velocity * (drop / -flake.Velocity.Z);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (_texturesReady && Camera.Instance != null && _world.Status == GameControlStatus.Ready)
            {
                var spriteBatch = GraphicsManager.Instance.Sprite;
                var device = GraphicsManager.Instance.GraphicsDevice;
                var camera = Camera.Instance;
                if (spriteBatch != null && device != null && _flakeTexture != null)
                {
                    DrawFlakes(spriteBatch, device.Viewport, camera.ViewProjection, camera);
                }
            }
        }

        private void DrawFlakes(SpriteBatch spriteBatch, Viewport viewport, Matrix viewProjection, Camera camera)
        {
            void draw()
            {
                Vector3 forward = camera.Target - camera.Position;
                float lengthSq = forward.LengthSquared();
                if (lengthSq < 0.000001f)
                    return;
                forward *= 1f / MathF.Sqrt(lengthSq);

                Vector3 origin = camera.Position + forward * 600f;
                Vector4 c0 = Vector4.Transform(origin, viewProjection);
                Vector4 c1 = Vector4.Transform(origin + camera.Up * 100f, viewProjection);
                if (c0.W <= 0.001f || c1.W <= 0.001f)
                    return;

                float y0 = (0.5f - c0.Y / c0.W * 0.5f) * viewport.Height;
                float y1 = (0.5f - c1.Y / c1.W * 0.5f) * viewport.Height;
                float pixelsPerUnit = MathF.Abs(y1 - y0) / 100f;
                if (pixelsPerUnit <= 0f)
                    return;

                Vector2 center = new(_flakeTexture.Width * 0.5f, _flakeTexture.Height * 0.5f);

                for (int i = 0; i < _flakes.Length; i++)
                {
                    ref readonly Flake flake = ref _flakes[i];
                    if (flake.Velocity == Vector3.Zero)
                        continue;

                    Vector4 clip = Vector4.Transform(flake.Position, viewProjection);
                    if (clip.W <= 0.001f)
                        continue;

                    float invW = 1f / clip.W;
                    float screenX = (clip.X * invW * 0.5f + 0.5f) * viewport.Width;
                    float screenY = (0.5f - clip.Y * invW * 0.5f) * viewport.Height;
                    float depth = clip.Z * invW;
                    if (depth < 0f || depth > 1f ||
                        screenX < -60f || screenX > viewport.Width + 60f ||
                        screenY < -60f || screenY > viewport.Height + 60f)
                    {
                        continue;
                    }

                    float sizePx = flake.Scale * pixelsPerUnit;
                    spriteBatch.Draw(
                        _flakeTexture,
                        new Vector2(screenX, screenY),
                        null,
                        Color.White,
                        0f,
                        center,
                        sizePx / _flakeTexture.Width,
                        SpriteEffects.None,
                        depth);
                }
            }

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
                    draw();
                }
            }
            else
            {
                draw();
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            _smokeOverlay.DrawOverlay(gameTime);
        }

        public override void Dispose()
        {
            _smokeOverlay.Dispose();
            base.Dispose();
        }
    }
}

