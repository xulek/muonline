#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Standard monster hit burst from SourceMain5.2's BITMAP_BLOOD + 1 particle.
    /// </summary>
    public sealed class MonsterHitEffect : SourceParticleSystem
    {
        private const int ParticleCount = 10;
        private const float OriginalFps = 25f;
        private const float LifeTime = 12f / OriginalFps;
        private const string TexturePath = "Effect/blood.tga";

        private Texture2D? _texture;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _texture == null
            ? Vector2.Zero
            : new Vector2(_texture.Width * 0.25f, _texture.Height * 0.25f);

        public MonsterHitEffect(Vector3 position, Vector3 angle)
            : base(ParticleCount)
        {
            Position = position;
            Angle = angle;
            BlendState = BlendState.NonPremultiplied;
            IsTransparent = true;
            AffectedByTransparency = false;
            LightEnabled = false;
            MaxDistance = 2000f;
            ScaleGrowth = 0f;

            for (int i = 0; i < ParticleCount; i++)
            {
                var particlePosition = new Vector3(
                    position.X + MuGame.Random.Next(-32, 32),
                    position.Y + MuGame.Random.Next(-32, 32),
                    position.Z + 90f + MuGame.Random.Next(0, 64));

                CreateParticle(0, particlePosition, angle, Vector3.One);
            }
        }

        public static Task PreloadAsync() => TextureLoader.Instance.PrepareAndGetTexture(TexturePath);

        public static async Task PrewarmRenderingAsync(WorldControl world)
        {
            // Exercise the real particle projection/draw path before combat. Position
            // particles in front of the camera so CPU culling does not skip that work,
            // then translate the sprite batch off-screen without changing render targets.
            var camera = Camera.Instance;
            Vector3 forward = camera.Target - camera.Position;
            if (forward.LengthSquared() < 0.000001f)
                return;
            Vector3 position = camera.Position + Vector3.Normalize(forward) * 500f - Vector3.UnitZ * 100f;
            using var hit = new MonsterHitEffect(position, Vector3.Zero) { World = world };
            using var spark = new MonsterHitSparkEffect(position) { World = world };
            await Task.WhenAll(hit.Load(), spark.Load());
            await MuGame.YieldToNextFrameAsync(
                "MonsterHitEffect.PrewarmRendering", MainThreadDispatcher.WorkPriority.Low);

            var graphics = GraphicsManager.Instance;
            var previousBlend = graphics.GraphicsDevice.BlendState;
            try
            {
                var gameTime = new GameTime();
                foreach (var effect in new SourceParticleSystem[] { hit, spark })
                {
                    using var scope = new SpriteBatchScope(
                        graphics.Sprite, SpriteSortMode.Deferred, effect.BlendState,
                        SamplerState.LinearClamp, DepthStencilState.DepthRead, RasterizerState.CullNone,
                        transform: Matrix.CreateTranslation(-16384f, -16384f, 0f));
                    effect.Draw(gameTime);
                }
            }
            finally
            {
                graphics.GraphicsDevice.BlendState = previousBlend;
            }
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(TexturePath)
                ?? throw new InvalidOperationException($"Unable to load hit texture '{TexturePath}'.");
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.Ready && ActiveCount == 0)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = LifeTime;
            particle.MaxLifeTime = LifeTime;
            particle.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
            particle.Light = new Vector3(0.1f, 0f, 0f);

            var velocity = new Vector3(
                0f,
                -MuGame.Random.Next(8, 24),
                MuGame.Random.Next(-3, 3));
            particle.Velocity = Vector3.Transform(velocity, Matrix.CreateRotationZ(Angle.Z));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float frameFactor = dt * OriginalFps;
            particle.Position += particle.Velocity * frameFactor;
            particle.Velocity *= MathF.Pow(0.95f, frameFactor);

            float lifeRatio = particle.MaxLifeTime > 0f
                ? MathHelper.Clamp(particle.LifeTime / particle.MaxLifeTime, 0f, 1f)
                : 0f;
            particle.Frame = MathHelper.Clamp((int)((1f - lifeRatio) * 4f), 0, 3);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            new(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha);

        protected override Rectangle? GetParticleSourceRectangle(Texture2D texture, in SourceParticle particle)
        {
            int frameWidth = texture.Width / 2;
            int frameHeight = texture.Height / 2;
            int frame = particle.Frame & 3;
            return new Rectangle(
                (frame & 1) * frameWidth,
                (frame >> 1) * frameHeight,
                frameWidth,
                frameHeight);
        }

        public override void Dispose()
        {
            _texture = null;
            base.Dispose();
        }
    }
}
