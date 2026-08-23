#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.DevilSquare
{
    /// <summary>
    /// Implements SourceMain5.2 WD_9DEVILSQUARE map-object visuals (ZzzObject.cpp,
    /// RenderObjectVisual case WD_9DEVILSQUARE). Object Type 2 is the rain-ring statue:
    /// BITMAP_RAIN_CIRCLE+1 particles spawn from bones 23/31 behind a rand_fps_check(4)
    /// gate plus one unconditional emission on bone 23 every legacy frame.
    /// </summary>
    public class DevilSquareObject : MapTileObject
    {
        private const float LegacyFramesPerSecond = 50f;
        private const int RainCircleBoneA = 23;
        private const int RainCircleBoneB = 31;

        private DevilSquareRingEmitter? _ringEmitter;
        private float _legacyFrameAccumulator;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        public override async Task Load()
        {
            await base.Load();
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (Type == 2 && _ringEmitter == null)
            {
                _ringEmitter = new DevilSquareRingEmitter(this);
                Children.Add(_ringEmitter);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Type != 2 || Status != GameControlStatus.Ready || _ringEmitter == null || !Visible)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            // Legacy rand_fps_check(4) gates evaluated once per reference frame (~50 fps),
            // plus the unconditional per-frame bone-23 emission from the source.
            _legacyFrameAccumulator += dt * LegacyFramesPerSecond;
            int steps = Math.Min((int)_legacyFrameAccumulator, 10);
            _legacyFrameAccumulator -= steps;

            for (int i = 0; i < steps; i++)
            {
                if (MuGame.Random.Next(4) == 0)
                    EmitRainRing(RainCircleBoneA);

                if (MuGame.Random.Next(4) == 0)
                    EmitRainRing(RainCircleBoneB);

                EmitRainRing(RainCircleBoneA);
            }
        }

        private void EmitRainRing(int boneIndex)
        {
            if (_ringEmitter == null)
                return;

            Vector3 position = WorldPosition.Translation;
            var bones = GetBoneTransforms();
            if (bones != null && bones.Length > boneIndex)
                position = Vector3.Transform(bones[boneIndex].Translation, WorldPosition);

            position += new Vector3(-15f, 0f, 0f);
            _ringEmitter.CreateParticle(0, position, Vector3.Zero, Vector3.One);
        }
    }

    /// <summary>
    /// Expanding, rising rain-ring billboards (BITMAP_RAIN_CIRCLE+1) rendered additively.
    /// </summary>
    internal sealed class DevilSquareRingEmitter : SourceParticleSystem
    {
        private const string RingTexturePath = "World10/rain03.tga";
        private Texture2D? _texture;
        private readonly DevilSquareObject _owner;

        public DevilSquareRingEmitter(DevilSquareObject owner)
            : base(capacity: 96)
        {
            _owner = owner;
            MaxDistance = 1800f;
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
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(RingTexturePath);
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 0.8f;
            particle.MaxLifeTime = 0.8f;
            particle.Scale = 0.35f + (float)MuGame.Random.NextDouble() * 0.2f;
            particle.Velocity = new Vector3(
                MuGame.Random.Next(17) - 8f,
                MuGame.Random.Next(17) - 8f,
                45f);
            particle.Gravity = 0f;
            particle.Light = Vector3.One;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Scale += 0.9f * dt;
        }
    }
}
