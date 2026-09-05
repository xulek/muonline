using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateBlood special case for MODEL_ICE_MONSTER:
    /// monster despawns instantly and ten MODEL_ICE_SMALL (Skill/Ice02.bmd)
    /// shards replace the corpse, spinning in place until they expire.
    /// </summary>
    public sealed class IceShatterEffect : EffectObject
    {
        private const float LifetimeSeconds = 1.6f;
        private const int ShardCount = 10;
        private const float SpawnJitterRadius = 12f;

        private readonly IceShardPiece[] _shards = new IceShardPiece[ShardCount];
        private float _life = LifetimeSeconds;

        public IceShatterEffect(Vector3 position, Vector3 angle)
        {
            Position = position;
            Angle = angle;
            IsTransparent = true;
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var iceModel = await BMDLoader.Instance.Prepare("Skill/Ice02.bmd");
            if (iceModel == null)
                return;

            for (int i = 0; i < _shards.Length; i++)
            {
                var shard = new IceShardPiece(iceModel);
                _shards[i] = shard;
                Children.Add(shard);
                await shard.Load();

                // SourceMain5.2 spawns every shard at the monster origin with a one-shot
                // +50 Z hop; jitter only separates the overlapping meshes for depth sorting.
                double a = MuGame.Random.NextDouble() * MathHelper.TwoPi;
                float r = (float)MuGame.Random.NextDouble() * SpawnJitterRadius;
                shard.Position = new Vector3(
                    Position.X + MathF.Cos((float)a) * r,
                    Position.Y + MathF.Sin((float)a) * r,
                    Position.Z + 50f);
                shard.RotationSpeed = (0.7f + (float)MuGame.Random.NextDouble() * 0.6f) *
                                      (MuGame.Random.Next(2) == 0 ? -1f : 1f);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _life -= (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Fade shards out during the final third, then clean up (original effects
            // simply expire after their frame budget).
            float fadeStart = LifetimeSeconds * 0.66f;
            float alpha = _life < fadeStart ? MathHelper.Max(_life / fadeStart, 0f) : 1f;

            for (int i = 0; i < _shards.Length; i++)
            {
                var shard = _shards[i];
                if (shard == null)
                    continue;
                shard.Angle = new Vector3(
                    shard.Angle.X - shard.RotationSpeed,
                    shard.Angle.Y,
                    shard.Angle.Z);
                shard.Alpha = alpha;
            }

            if (_life <= 0f)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private sealed class IceShardPiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;

            public float RotationSpeed { get; set; }

            public IceShardPiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = false;
                AnimationSpeed = 6f;
                // SourceMain5.2: BlendMesh = 0, BlendMeshLight = 0.3f (translucent glowing ice).
                BlendMesh = 0;
                BlendMeshLight = 0.3f;
                IsTransparent = true;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }
        }
    }
}
