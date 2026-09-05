#nullable enable
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of SourceMain5.2 MODEL_PIERCING+1 sub1 (AttackEffect MONSTER_DRAKAN):
    /// meteors lock onto the caster's position every frame, gain Gravity (+90/frame),
    /// drill into the ground and die at terrain height.
    /// </summary>
    public sealed class MonsterMeteorFallEffect : EffectObject
    {
        private const float LifetimeSeconds = 1.4f;

        private readonly ModelObject _owner;
        private readonly MeteorPiece[] _meteors;
        private float _life = LifetimeSeconds;

        public MonsterMeteorFallEffect(ModelObject owner, int meteorCount)
        {
            _owner = owner;
            Position = owner.Position;
            _meteors = new MeteorPiece[meteorCount];
            IsTransparent = true;
            AffectedByTransparency = true;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-800f, -800f, -200f),
                new Vector3(800f, 800f, 900f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var piercingModel = await BMDLoader.Instance.Prepare("Skill/Piercing.bmd");
            if (piercingModel == null)
                return;

            for (int i = 0; i < _meteors.Length; i++)
            {
                var meteor = new MeteorPiece(piercingModel);
                _meteors[i] = meteor;
                Children.Add(meteor);
                await meteor.Load();

                // Initial scatter: +-500 XY, +500 Z (CreateEffect call site).
                // Yaw copies the caster angle (VectorCopy(o->Angle)); only pitch +45.
                double a = MuGame.Random.NextDouble() * MathHelper.TwoPi;
                float r = (float)MuGame.Random.NextDouble() * 500f;
                meteor.Scatter = new Vector3(MathF.Cos((float)a) * r, MathF.Sin((float)a) * r, 500f);
                meteor.Angle = new Vector3(MathHelper.ToRadians(45f), 0f, _owner.Angle.Z);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _life -= dt;

            bool anyAlive = false;
            foreach (var meteor in _meteors)
            {
                if (meteor == null || meteor.Dead)
                    continue;

                // C++ MoveJoint/MODEL_PIERCING: Position copies Owner->Position each
                // frame; Gravity accumulates +90/frame-factor driving the fall.
                meteor.Gravity += 90f * dt * 25f * 0.04f; // ~90 units/s^2 scaled
                meteor.Position = new Vector3(
                    _owner.Position.X + meteor.Scatter.X,
                    _owner.Position.Y + meteor.Scatter.Y,
                    meteor.Position.Z == 0f
                        ? _owner.Position.Z + meteor.Scatter.Z
                        : meteor.Position.Z - meteor.Gravity * dt);
                meteor.Angle = new Vector3(meteor.Angle.X - meteor.Gravity * dt * 0.2f, 0f, meteor.Angle.Z);

                if (World?.Terrain != null &&
                    meteor.Position.Z <= World.Terrain.RequestTerrainHeight(meteor.Position.X, meteor.Position.Y))
                {
                    meteor.Dead = true;
                    meteor.Hidden = true;
                    continue;
                }

                anyAlive = true;
            }

            if (_life <= 0f || !anyAlive)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private sealed class MeteorPiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;

            public Vector3 Scatter { get; set; }
            public float Gravity { get; set; } = 40f;
            public bool Dead { get; set; }

            public MeteorPiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 6f;
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
