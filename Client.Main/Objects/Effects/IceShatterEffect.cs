using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateBlood special case for MODEL_ICE_MONSTER: the monster despawns and ten
    /// MODEL_ICE_SMALL (Skill/Ice02.bmd) shards fly out of it. Per shard (ZzzEffect.cpp, SubType 0): random
    /// heading, outward speed 6.4-32, upward speed 8-23, gravity 3/frame, direction damped by 0.9/frame,
    /// bouncing on the terrain (gravity halved and reversed, life -4) while tumbling around X.
    /// </summary>
    public sealed class IceShatterEffect : EffectObject
    {
        private const int ShardCount = 10;
        private const float FramesPerSecond = 25f;

        private readonly IceShardPiece[] _shards = new IceShardPiece[ShardCount];
        private Client.Data.BMD.BMD _iceModel;
        private bool _built;

        public IceShatterEffect(Vector3 position, Vector3 angle)
        {
            // Shards are children placed relative to this effect; keep it axis aligned.
            Position = position;
            Angle = Vector3.Zero;
            IsTransparent = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-400f, -400f, -100f), new Vector3(400f, 400f, 500f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            _iceModel = await BMDLoader.Instance.Prepare("Skill/Ice02.bmd");
            if (_iceModel == null)
                return;

            for (int i = 0; i < _shards.Length; i++)
            {
                var shard = new IceShardPiece(_iceModel);
                _shards[i] = shard;
                Children.Add(shard);
                await shard.Load();
                shard.Launch();
            }
            _built = true;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || !_built)
                return;

            float frames = (float)gameTime.ElapsedGameTime.TotalSeconds * FramesPerSecond;
            bool anyAlive = false;

            for (int i = 0; i < _shards.Length; i++)
            {
                var shard = _shards[i];
                if (shard == null)
                    continue;

                if (!shard.Step(frames, World?.Terrain, Position))
                {
                    shard.Hidden = true;
                    continue;
                }
                anyAlive = true;
            }

            if (!anyAlive)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private sealed class IceShardPiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;
            private Vector3 _direction;
            private float _gravity;
            private float _life;
            private float _tumble;

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

            /// <summary>CreateEffect(MODEL_ICE_SMALL, ..., SubType 0) initial state.</summary>
            public void Launch()
            {
                float yaw = (float)MuGame.Random.NextDouble() * MathHelper.TwoPi;
                float speed = (MuGame.Random.Next(256) + 64) * 0.1f;
                _direction = new Vector3(-MathF.Sin(yaw) * speed, MathF.Cos(yaw) * speed, 0f);
                _gravity = MuGame.Random.Next(16) + 8;
                _life = MuGame.Random.Next(16) + 32;
                Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                _tumble = 0f;
                Angle = new Vector3(0f, 0f, yaw);
                Position = new Vector3(0f, 0f, 50f);
            }

            /// <summary>One simulation step in 25 fps frames; returns false when the shard has expired.</summary>
            public bool Step(float frames, Client.Main.Controls.TerrainControl terrain, Vector3 origin)
            {
                Vector3 pos = Position + _direction * frames;
                _direction *= MathF.Pow(0.9f, frames);
                pos.Z += _gravity * frames;
                _gravity -= 3f * frames;

                float height = terrain != null
                    ? terrain.RequestTerrainHeight(origin.X + pos.X, origin.Y + pos.Y) - origin.Z
                    : float.MinValue;
                if (pos.Z < height)
                {
                    pos.Z = height;
                    _gravity = -_gravity * 0.5f;
                    _life -= 4f * frames;
                    _tumble -= Scale * 128f * frames;
                }
                else
                {
                    _tumble -= Scale * 32f * frames;
                }

                Position = pos;
                Angle = new Vector3(MathHelper.ToRadians(_tumble), 0f, Angle.Z);

                _life -= frames;
                return _life > 0f;
            }
        }
    }
}
