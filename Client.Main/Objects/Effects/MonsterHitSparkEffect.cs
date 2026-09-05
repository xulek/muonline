#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Melee impact sparks from SourceMain5.2's CreateSpark
    /// (ZzzCharacter.cpp: CreateSpark(0, tc, to->Position, o->Angle) on sword
    /// skill hits; BITMAP_SPARK particles burst around the victim).
    /// </summary>
    public sealed class MonsterHitSparkEffect : SourceParticleSystem
    {
        private const int ParticleCount = 6;
        private const float OriginalFps = 25f;
        private const float LifeTime = 8f / OriginalFps;
        private const string TexturePath = "Effect/Spark01.jpg";

        private Texture2D? _texture;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _texture == null
            ? Vector2.Zero
            : new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);

        public MonsterHitSparkEffect(Vector3 position)
            : base(ParticleCount)
        {
            Position = position;
            BlendState = BlendState.Additive;
            IsTransparent = true;
            AffectedByTransparency = false;
            LightEnabled = false;
            MaxDistance = 2000f;
            ScaleGrowth = 0f;

            for (int i = 0; i < ParticleCount; i++)
            {
                var particlePosition = new Vector3(
                    position.X + MuGame.Random.Next(-20, 20),
                    position.Y + MuGame.Random.Next(-20, 20),
                    position.Z + 60f + MuGame.Random.Next(0, 70));

                CreateParticle(0, particlePosition, Vector3.Zero, Vector3.One);
            }
        }

        public override async Task Load()
        {
            await base.Load();

            if (Status != GameControlStatus.Ready)
                return;

            var textureData = await TextureLoader.Instance.Prepare(TexturePath);
            if (textureData == null)
            {
                Status = GameControlStatus.Error;
                return;
            }

            _texture = TextureLoader.Instance.GetTexture2D(TexturePath);
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
            particle.Scale = (MuGame.Random.Next(3) + 4) * 0.1f;
            particle.Light = new Vector3(0.9f, 0.85f, 0.6f);

            double direction = MuGame.Random.NextDouble() * MathHelper.TwoPi;
            float speed = MuGame.Random.Next(6, 18) * 0.1f * 10f;
            particle.Velocity = new Vector3(
                MathF.Cos((float)direction) * speed,
                MathF.Sin((float)direction) * speed,
                MuGame.Random.Next(-4, 12));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float frameFactor = dt * OriginalFps;
            particle.Position += particle.Velocity * frameFactor;
            particle.Velocity *= MathF.Pow(0.88f, frameFactor);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            new(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha);

        public override void Dispose()
        {
            _texture = null;
            base.Dispose();
        }
    }
}
