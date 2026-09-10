#nullable enable
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of SourceMain5.2 MODEL_FIRE rain (AttackEffect MONSTER_BALROG boss
    /// branch): flames appear at random ground points +-512 around the caster and
    /// burn out. The original spawns one per frame for the whole boss attack; the
    /// port spawns a one-shot burst per attack (server packets throttle visuals).
    /// </summary>
    public sealed class MonsterFireRainEffect : EffectObject
    {
        private readonly FirePiece?[] _fires;

        public MonsterFireRainEffect(Vector3 center, int fireCount = 8)
        {
            Position = center;
            _fires = new FirePiece?[fireCount];
            IsTransparent = true;
            AffectedByTransparency = true;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-700f, -700f, -100f),
                new Vector3(700f, 700f, 300f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var fireModel = await BMDLoader.Instance.Prepare("Skill/Fire.bmd");
            if (fireModel == null)
                return;

            for (int i = 0; i < _fires.Length; i++)
            {
                float x = Position.X + MuGame.Random.Next(1024) - 512f;
                float y = Position.Y + MuGame.Random.Next(1024) - 512f;
                float groundZ = World?.Terrain != null
                    ? World.Terrain.RequestTerrainHeight(x, y)
                    : Position.Z;

                var piece = new FirePiece(fireModel)
                {
                    Position = new Vector3(x, y, groundZ)
                };
                _fires[i] = piece;
                Children.Add(piece);
                await piece.Load();
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            bool anyAlive = false;
            foreach (var fire in _fires)
            {
                if (fire == null || fire.Finished)
                    continue;

                anyAlive = true;
            }

            if (!anyAlive)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private sealed class FirePiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;
            private float _life;

            public bool Finished => _life <= 0f;

            public FirePiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                _life = 1.2f + (float)MuGame.Random.NextDouble() * 0.6f;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 6f;
                IsTransparent = true;
                BlendState = Microsoft.Xna.Framework.Graphics.BlendState.Additive;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready || Finished)
                    return;

                _life -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                Alpha = MathHelper.Clamp(_life / 0.5f, 0f, 1f);
            }
        }
    }
}
