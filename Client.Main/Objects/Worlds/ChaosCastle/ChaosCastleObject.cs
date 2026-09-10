#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.ChaosCastle
{
    /// <summary>
    /// Shared event-state mirror of SourceMain5.2's g_byCurrCastleLevel.
    /// 255 means "no active event" (the source default when outside a running match).
    /// </summary>
    public static class ChaosCastleState
    {
        public static byte CurrentLevel = 255;
    }

    /// <summary>
    /// Implements the CSChaosCastle.cpp map-object visuals (RenderChaosCastleVisual /
    /// CreateChaosCastleObject): one-shot rock-fall dust bursts for Types 6-12,
    /// level-gated mesh hiding for Types 18-35, and inert thunder-pillar state for 0-3.
    /// </summary>
    public class ChaosCastleObject : MapTileObject
    {
        private ChaosCastleDustEmitter? _dustEmitter;
        private bool _burstEmitted;

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

            if (IsDustRock(Type) && _dustEmitter == null)
            {
                _dustEmitter = new ChaosCastleDustEmitter(this);
                Children.Add(_dustEmitter);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            byte level = ChaosCastleState.CurrentLevel;

            switch (Type)
            {
                case 18 or 19 or 20 or 21:
                    HiddenMesh = level == 7 || level == 8 ? -1 : -2;
                    break;

                case >= 24 and <= 29:
                    if (level == 4 || level == 5)
                        HiddenMesh = -1;
                    else if (level >= 8)
                        HiddenMesh = -2;
                    break;

                case >= 30 and <= 35:
                    if (level == 1 || level == 2)
                        HiddenMesh = -1;
                    else if (level >= 5)
                        HiddenMesh = -2;
                    break;

                case 4 or 5 or >= 13 and <= 17:
                    if (level >= 2 && level != 255)
                        HiddenMesh = -2;
                    break;
            }

            EmitDustBurstOnce(level);
        }

        private static bool IsDustRock(int type) =>
            type is >= 6 and <= 12;

        private void EmitDustBurstOnce(byte level)
        {
            if (_burstEmitted || _dustEmitter == null || HiddenMesh == -2)
                return;

            // SourceMain: while HiddenMesh != -2 emit one burst then hide the mesh forever.
            _burstEmitted = true;

            int count = Type switch
            {
                6 or 7 or 8 => 10,
                9 or 10 or 11 => 5,
                12 => 7,
                _ => 0
            };
            if (count == 0)
                return;

            Vector3 light = Type == 12
                ? new Vector3(0.3f, 0.3f, 0.3f)
                : new Vector3(0.05f, 0.05f, 0.1f);

            Vector3 position = WorldPosition.Translation;
            var bones = GetBoneTransforms();
            if (bones != null && bones.Length > 0)
                position = Vector3.Transform(bones[0].Translation, WorldPosition);

            int variant = Type switch
            {
                6 or 7 or 8 => MuGame.Random.Next(3),
                9 or 10 or 11 => 3 + MuGame.Random.Next(3),
                _ => 7
            };

            for (int i = 0; i < count; i++)
                _dustEmitter.CreateParticle(variant, position, Angle, light, variant, Scale, this);

            HiddenMesh = -2;
        }
    }

    /// <summary>
    /// BITMAP_CLOUD dust puffs (CreateParticle(BITMAP_CLOUD, ..., o-&gt;Scale)) rendered additively.
    /// </summary>
    internal sealed class ChaosCastleDustEmitter : SourceParticleSystem
    {
        private const string CloudTexturePath = "Effect/clouds.jpg";
        private Texture2D? _texture;
        private readonly ChaosCastleObject _owner;

        public ChaosCastleDustEmitter(ChaosCastleObject owner)
            : base(capacity: 32)
        {
            _owner = owner;
            MaxDistance = 2000f;
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

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 1.4f;
            particle.MaxLifeTime = 1.4f;
            particle.Scale = MathF.Max(0.6f, particle.Scale * (0.8f + (float)MuGame.Random.NextDouble() * 0.4f));
            particle.Velocity = new Vector3(
                MuGame.Random.Next(33) - 16f,
                MuGame.Random.Next(33) - 16f,
                25f + MuGame.Random.Next(30));
            particle.Gravity = 0f;
            particle.Light *= 0.5f + (float)MuGame.Random.NextDouble() * 0.5f;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Velocity *= 1f - 0.8f * dt;
            particle.Scale += 1.1f * dt;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            Vector3 light = Vector3.Clamp(particle.Light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, particle.Alpha * lifeRatio);
        }
    }
}
