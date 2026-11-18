using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(69, "Alquamos")]
    public class Alquamos : MonsterObject
    {
        public Alquamos()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            BlendMesh = 0; // SourceMain5.2 CreateCharacter: BlendMesh = 0
            BlendMeshLight = 1.0f;

            // SourceMain5.2 RenderCharacter(MONSTER_ALQUAMOS) (ZzzCharacter.cpp:8602-8623): BITMAP_LIGHT sprites on the
            // g_chStar bones + rand_fps_check(1) BITMAP_SPARK+1 particles. Anchors and port sheet: MonsterAlquamosAuraEffect.
            Children.Add(new MonsterAlquamosAuraEffect());
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster51.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 175, 175, 175, 175, 176);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Note: C++ had commented out idle sound, but we'll use attack sound for idle as per mapping
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition); // Sound 175
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlayAttackSound("Sound/mAlquamosAttack1.wav"); // Sound 175

            // NOTE: a plain Alquamos attack has no visual in SourceMain5.2 (AttackEffect case MONSTER_ALQUAMOS is empty,
            // ZzzCharacter.cpp:1518), so only the attack sound plays here. The flares come from AT_SKILL_ENERGYBALL
            // (ZzzCharacter.cpp:1979-1990): MonsterAlquamosFlareEffect.
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.SkillStarted || progress.SkillId != EnergyBallAttackSkill ||
                World is not WalkableWorldControl world || !TryGetAttackTarget(out var target))
                return;

            // AttackEffect MONSTER_ALQUAMOS, AT_SKILL_ENERGYBALL branch (ZzzCharacter.cpp:1979-1990).
            var flares = new Effects.MonsterAlquamosFlareEffect(Position, target);
            world.Objects.Add(flares);
            MuGame.TaskScheduler?.QueueTask(
                async () => await flares.Load(),
                Client.Main.Controllers.TaskScheduler.Priority.High,
                "MonsterAlquamosFlareEffect.Load");
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosDie.wav", Position, listenerPosition); // Sound 176
        }
    }
}
