#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Projectile fired by monsters: SourceMain5.2 archers call CreateArrows
    /// (MODEL_HUNTER / MODEL_VALKYRIE / MODEL_SOLDIER / MODEL_ORC_ARCHER) and map
    /// bosses spawn model projectiles (e.g. Selupan's MODEL_RAKLION_BOSS_MAGIC).
    /// Flies from the source bone to the packet target.
    /// </summary>
    public sealed class MonsterArrowProjectileEffect : EffectObject
    {
        private const float FlightSpeed = 1400f;
        private const float MinFlightSeconds = 0.15f;
        private const float MaxFlightSeconds = 0.6f;

        private readonly ModelObject _shooter;
        private readonly int _sourceBone;
        private readonly ushort _targetId;

        /// <summary>BMD model used for the projectile (arrow by default).</summary>
        public string ProjectileModelPath { get; set; } = "Skill/Arrow01.bmd";
        public float ProjectileScale { get; set; } = 1f;

        /// <summary>
        /// Local offset added to the source bone position (e.g. Selupan's
        /// MODEL_RAKLION_BOSS_MAGIC spawns at Position + 30Y, not on a bone).
        /// </summary>
        public Vector3 SourceOffset { get; set; }

        private Vector3 _start;
        private Vector3 _end;
        private float _flightSeconds = MinFlightSeconds;
        private float _elapsed;
        private bool _initialized;

        public MonsterArrowProjectileEffect(ModelObject shooter, int sourceBone, ushort targetId)
        {
            _shooter = shooter ?? throw new ArgumentNullException(nameof(shooter));
            _sourceBone = sourceBone;
            _targetId = targetId;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-2000f, -2000f, -500f),
                new Vector3(2000f, 2000f, 800f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var arrowModel = await BMDLoader.Instance.Prepare(ProjectileModelPath);
            if (arrowModel == null)
                return;

            var arrow = new ArrowPiece(arrowModel)
            {
                Scale = ProjectileScale
            };
            Children.Add(arrow);
            await arrow.Load();

            // Start at the bow bone; end at the packet target chest height.
            _start = GetBoneWorldPosition(_sourceBone) + SourceOffset;
            if (_shooter.World is WalkableWorldControl world &&
                world.TryGetWalkerById(_targetId, out var target))
                _end = target.WorldPosition.Translation + Vector3.UnitZ * 60f;
            else
                _end = _start - _shooter.WorldPosition.Forward * 300f + Vector3.UnitZ * 40f;

            float distance = Vector3.Distance(_start, _end);
            _flightSeconds = MathHelper.Clamp(distance / FlightSpeed, MinFlightSeconds, MaxFlightSeconds);

            Position = _start;
            _initialized = true;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!_initialized)
                return;

            _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
            float progress = MathHelper.Clamp(_elapsed / _flightSeconds, 0f, 1f);

            Position = Vector3.Lerp(_start, _end, progress);
            OrientAlongPath(_start, _end);

            if (progress >= 1f)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private void OrientAlongPath(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            if (direction.LengthSquared() < 0.001f)
                return;

            direction.Normalize();
            // Mu arrow models fly nose-first along their local forward axis.
            Angle = new Vector3(
                MathF.Atan2(direction.Z, new Vector2(direction.X, direction.Y).Length()),
                0f,
                MathF.Atan2(direction.Y, direction.X));
        }

        private Vector3 GetBoneWorldPosition(int boneIndex)
        {
            Matrix[] bones = _shooter.GetBoneTransforms();
            if (bones == null || boneIndex < 0 || boneIndex >= bones.Length)
                return _shooter.WorldPosition.Translation;

            return (bones[boneIndex] * _shooter.WorldPosition).Translation;
        }

        private sealed class ArrowPiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;

            public ArrowPiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = false;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }
        }
    }
}
