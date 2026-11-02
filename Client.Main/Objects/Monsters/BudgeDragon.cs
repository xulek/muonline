using System;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(2, "Budge Dragon")]
    public class BudgeDragon : MonsterObject
    {
        private new ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<MonsterObject>();

        // Flight bob — matches original (-abs(sin(timer))*70+70)*FPS_ANIMATION_FACTOR
        private float _bobTimer;
        private float _fireLegacyAccumulator;

        // Ground dust/smoke emitter — matches original BITMAP_SMOKE+1 from fall-through case
        private BudgeDragonDustEffect _dustEffect;

        // Fire breath emitter — matches original BITMAP_FIRE from bone 7 during attack
        private BudgeDragonFireAttackEffect _fireAttackEffect;

        public BudgeDragon()
        {
            RenderShadow = true;
            Scale = 0.5f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)

            _dustEffect = new BudgeDragonDustEffect();
            Children.Add(_dustEffect);

            _fireAttackEffect = new BudgeDragonFireAttackEffect();
            Children.Add(_fireAttackEffect);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster03.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.7f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Model == null)
            {
                base.Update(gameTime);
                return;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            bool isDead = IsDead || CurrentAction == (int)MonsterActionType.Die;

            // --- Flight bob only during movement ---
            bool isMoving = IsMoving || CurrentAction == (int)MonsterActionType.Walk;
            if (!isDead && isMoving)
            {
                _bobTimer += 3.75f * dt;
                ExtraHeight = -MathF.Abs(MathF.Sin(_bobTimer)) * 70f + 70f;
            }
            else
            {
                ExtraHeight = 0;
            }

            base.Update(gameTime);

            // --- Fire breath from mouth (bone 7) during attack ---
            if (!isDead
                && CurrentAction == (int)MonsterActionType.Attack1
                && _animTime <= 4.0
                && BoneTransform != null
                && BoneTransform.Length > 7)
            {
                _fireLegacyAccumulator += dt * 25f;
                int legacyTicks = (int)_fireLegacyAccumulator;
                _fireLegacyAccumulator -= legacyTicks;
                if (legacyTicks > 0)
                {
                    // Source rand_fps_check(1): emit once per reference frame.
                    Vector3 boneOffset = new Vector3(0f, 32f + MuGame.Random.Next(32), 0f);
                    Vector3 boneLocal = Vector3.Transform(boneOffset, BoneTransform[7]);
                    _fireAttackEffect.SpawnWorldPosition = Vector3.Transform(boneLocal, WorldPosition);
                    _fireAttackEffect.EmitThisFrame = true;
                }
            }
            else
                _fireLegacyAccumulator = 0f;
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mBudge1.wav" : "Sound/mBudgeAttack1.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        protected override void OnStartWalk()
        {
            base.OnStartWalk();
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            // SoundController.Instance.PlayBufferWithAttenuation("Sound/mBudge1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBudgeAttack1.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBudgeAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBudgeDie.wav", Position, listenerPosition);
        }
    }
}
