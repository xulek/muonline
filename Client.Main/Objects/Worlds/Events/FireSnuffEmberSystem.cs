#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Events
{
    /// <summary>
    /// SourceMain5.2 CreateFireSnuff / CreateFireSpark embers (BattleCastle siege,
    /// Doppelganger2, Vulcanus, Balgass Refuge): drifting red fire flakes
    /// (BITMAP_FIRE_SNUFF) rendered as vertically stretched additive billboards.
    /// </summary>
    public sealed class FireSnuffEmberSystem : EffectObject
    {
        private const string EmberTexturePath = "Effect/FireSnuff.jpg";
        private const float SpawnOffsetX = 800f;
        private const float SpawnOffsetYMin = -500f;
        private const float SpawnOffsetYMax = 900f;

        private struct Ember
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
            public float Age;
            public float LifeTime;
        }

        private readonly WalkableWorldControl _world;
        private readonly int _maxEmbers;
        private readonly float _scaleBias;
        private Texture2D? _texture;
        private readonly Ember[] _embers;

        public FireSnuffEmberSystem(WalkableWorldControl world, int maxEmbers, float scaleBias)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _maxEmbers = Math.Max(1, maxEmbers);
            _scaleBias = scaleBias;
            _embers = new Ember[_maxEmbers];
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(EmberTexturePath);
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
            Vector3 cameraPosition = Camera.Instance.Position;

            for (int i = 0; i < _maxEmbers; i++)
            {
                ref Ember ember = ref _embers[i];
                if (ember.LifeTime <= 0f)
                {
                    RespawnEmber(ref ember, heroPosition);
                    continue;
                }

                ember.Age += dt;
                ember.Position += ember.Velocity * dt;

                // SourceMain: drift flips forward (+2.2) when the emitter is behind the camera plane
                bool behindCamera = Vector3.Dot(ember.Position - cameraPosition,
                    cameraPosition - Camera.Instance.Target) > 0f;
                float targetDriftX = behindCamera ? 2.2f : -(MuGame.Random.Next(64) + 64) * 0.1f;
                ember.Velocity.X += (targetDriftX - ember.Velocity.X) * dt;

                if (ember.Age >= ember.LifeTime ||
                    MathF.Abs(ember.Position.X - heroPosition.X) > SpawnOffsetX + 200f)
                {
                    RespawnEmber(ref ember, heroPosition);
                }
            }
        }

        private void RespawnEmber(ref Ember ember, Vector3 heroPosition)
        {
            // Scale = rand()%50/100 + bias; spawn Hero±(800,-500..900), Z+50..350
            // Velocity[0] = -(rand()%64+64)*0.1 ; Light = {1,0,0}
            ember.Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(1600) - 800f,
                heroPosition.Y + MuGame.Random.Next(1400) - 500f,
                heroPosition.Z + 50f + MuGame.Random.Next(300));
            ember.Velocity = new Vector3(
                -(MuGame.Random.Next(64) + 64) * 0.1f,
                (MuGame.Random.Next(32) - 16) * 0.1f,
                12f + (float)MuGame.Random.NextDouble() * 20f);
            ember.Scale = MuGame.Random.Next(50) / 100f + _scaleBias;
            ember.LifeTime = 4f + (float)MuGame.Random.NextDouble() * 6f;
            ember.Age = 0f;
        }

        public override void Draw(GameTime gameTime)
        {
            if (_texture == null || Status != GameControlStatus.Ready)
                return;

            Camera camera = Camera.Instance;
            var spriteBatch = GraphicsManager.Instance.Sprite;
            var device = GraphicsManager.Instance.GraphicsDevice;
            if (camera == null || spriteBatch == null || device == null)
                return;

            var viewport = device.Viewport;
            Matrix viewProjection = camera.ViewProjection;

            void drawEmbers()
            {
                float pixelsPerUnit = ComputePixelsPerUnit(viewport, viewProjection, camera);
                if (pixelsPerUnit <= 0f)
                    return;

                Vector2 center = new(_texture.Width * 0.5f, _texture.Height * 0.5f);

                for (int i = 0; i < _embers.Length; i++)
                {
                    ref readonly Ember ember = ref _embers[i];
                    if (ember.LifeTime <= 0f)
                        continue;

                    Vector4 clip = Vector4.Transform(ember.Position, viewProjection);
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

                    float flicker = 0.7f + 0.3f * MathF.Sin(ember.Age * 14f);
                    float alpha = MathHelper.Clamp(ember.LifeTime - ember.Age, 0f, 1f);
                    float widthPx = ember.Scale * 2f * pixelsPerUnit;
                    float heightPx = ember.Scale * 4f * pixelsPerUnit;

                    spriteBatch.Draw(
                        _texture,
                        new Vector2(screenX, screenY),
                        null,
                        new Color(1f, 0.15f, 0.05f, 0.85f * alpha * flicker),
                        0f,
                        center,
                        new Vector2(widthPx / _texture.Width, heightPx / _texture.Height),
                        SpriteEffects.None,
                        depth);
                }
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    drawEmbers();
                }
            }
            else
            {
                drawEmbers();
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
    }
}

