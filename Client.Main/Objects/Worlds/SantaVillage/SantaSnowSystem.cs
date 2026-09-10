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

namespace Client.Main.Objects.Worlds.SantaVillage
{
    /// <summary>
    /// CGMSantaTown::CreateSnow — the slowest snowfall in the game:
    /// BITMAP_LEAF1 flakes (scale rand()%10+5, 1-in-10 LEAF2 at 12), tilt -20 degrees,
    /// fall speed -(rand()%8+4) legacy units per frame.
    /// </summary>
    public sealed class SantaSnowSystem : EffectObject
    {
        private const float LegacyFramesPerSecond = 50f;

        private struct Flake
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
        }

        private readonly WalkableWorldControl _world;
        private readonly Flake[] _flakes;
        private Texture2D? _flakeTextureSmall;
        private Texture2D? _flakeTextureLarge;
        private bool _texturesReady;

        public SantaSnowSystem(WalkableWorldControl world, int flakeCount)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _flakes = new Flake[Math.Max(1, flakeCount)];
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _flakeTextureSmall = await LoadFirstAvailable("World63/leaf01.OZJ", "World63/leaf01.jpg");
            _flakeTextureLarge = await LoadFirstAvailable("World63/leaf02.OZJ", "World63/leaf02.jpg")
                                 ?? _flakeTextureSmall;
            _texturesReady = true;
        }

        private static async Task<Texture2D?> LoadFirstAvailable(params string[] paths)
        {
            foreach (string path in paths)
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
                    RespawnFlake(ref flake, heroPosition, prefill: true);
                    continue;
                }

                flake.Position += flake.Velocity * dt;

                if (flake.Position.Z <= heroPosition.Z - 300f ||
                    MathF.Abs(flake.Position.X - heroPosition.X) > 1000f ||
                    MathF.Abs(flake.Position.Y - heroPosition.Y) > 1100f)
                {
                    RespawnFlake(ref flake, heroPosition, prefill: false);
                }
            }
        }

        private void RespawnFlake(ref Flake flake, Vector3 heroPosition, bool prefill)
        {
            // Scale=rand()%10+5 ; every 10th BITMAP_LEAF2 scale 12
            // Angle=(-20,0,0); Velocity z=-(rand()%8+4)
            bool largeFlake = MuGame.Random.Next(10) == 0;
            float fallSpeed = (MuGame.Random.Next(8) + 4) * LegacyFramesPerSecond * 0.5f;

            flake.Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(1600) - 800f,
                heroPosition.Y + MuGame.Random.Next(1400) - 500f,
                heroPosition.Z + 200f + MuGame.Random.Next(400));
            flake.Scale = largeFlake ? 12f : MuGame.Random.Next(10) + 5;
            flake.Velocity = new Vector3(MathF.Cos(MathHelper.ToRadians(-20f)) * fallSpeed * 0.35f, 0f, -fallSpeed);

            if (prefill)
            {
                float drop = (float)MuGame.Random.NextDouble() * 600f;
                flake.Position -= flake.Velocity * (drop / -flake.Velocity.Z);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!_texturesReady || Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var device = GraphicsManager.Instance.GraphicsDevice;
            var camera = Camera.Instance;
            if (spriteBatch == null || device == null)
                return;

            void draw()
            {
                Vector3 forward = camera.Target - camera.Position;
                float lengthSq = forward.LengthSquared();
                if (lengthSq < 0.000001f)
                    return;
                forward *= 1f / MathF.Sqrt(lengthSq);

                Vector3 origin = camera.Position + forward * 600f;
                Vector4 c0 = Vector4.Transform(origin, camera.ViewProjection);
                Vector4 c1 = Vector4.Transform(origin + camera.Up * 100f, camera.ViewProjection);
                if (c0.W <= 0.001f || c1.W <= 0.001f)
                    return;

                float y0 = (0.5f - c0.Y / c0.W * 0.5f) * device.Viewport.Height;
                float y1 = (0.5f - c1.Y / c1.W * 0.5f) * device.Viewport.Height;
                float pixelsPerUnit = MathF.Abs(y1 - y0) / 100f;
                if (pixelsPerUnit <= 0f)
                    return;

                for (int i = 0; i < _flakes.Length; i++)
                {
                    ref readonly Flake flake = ref _flakes[i];
                    if (flake.Velocity == Vector3.Zero)
                        continue;

                    Texture2D texture = flake.Scale >= 12f
                        ? (_flakeTextureLarge ?? _flakeTextureSmall)!
                        : _flakeTextureSmall!;
                    if (texture == null || texture.IsDisposed)
                        continue;

                    Vector4 clip = Vector4.Transform(flake.Position, camera.ViewProjection);
                    if (clip.W <= 0.001f)
                        continue;

                    float invW = 1f / clip.W;
                    float screenX = (clip.X * invW * 0.5f + 0.5f) * device.Viewport.Width;
                    float screenY = (0.5f - clip.Y * invW * 0.5f) * device.Viewport.Height;
                    float depth = clip.Z * invW;
                    if (depth < 0f || depth > 1f ||
                        screenX < -60f || screenX > device.Viewport.Width + 60f ||
                        screenY < -60f || screenY > device.Viewport.Height + 60f)
                    {
                        continue;
                    }

                    spriteBatch.Draw(
                        texture,
                        new Vector2(screenX, screenY),
                        null,
                        Color.White,
                        0f,
                        new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                        flake.Scale * pixelsPerUnit / texture.Width,
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
    }
}

