using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(75, "Great Drakan")]
    public class GreatDrakan : MonsterObject
    {
        public GreatDrakan()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            // SourceMain5.2 RenderCharacter: GREAT_DRAKAN gets the RENDER_CHROME |
            // RENDER_BRIGHT | RENDER_EXTRA body pass tinted (1.0, 0.1, 0.1)
            // (extra part not reproduced).
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(1f, 0.1f, 0.1f);
            // SourceMain5.2 RenderCharacter(MONSTER_GREAT_DRAKAN): every frame one
            // CreateParticle(BITMAP_FIRE, scale 0.3) from bone 18 (~25/s).
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBone = 18,
                EmissionRate = 25f,
                ParticleScaleMin = 0.28f,
                ParticleScaleMax = 0.32f
            });
            // Set meshes that should NOT use blending (equivalent to NoneBlendMesh = true)
            NoneBlendMeshes.Add(0); // Mesh 0: no blending
            NoneBlendMeshes.Add(3); // Mesh 3: no blending
            NoneBlendMeshes.Add(4); // Mesh 4: no blending
            // Mesh 1 and 2 will use blending (not in NoneBlendMeshes set)
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster55.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakan1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition);

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            if (attackType == 1)
            {
                // SourceMain5.2 AttackEffect MONSTER_GREAT_DRAKAN Attack1: inferno
                // burst at self + 5 falling MODEL_PIERCING+1 meteors.
                var inferno = new Effects.ScrollOfInfernoEffect(this, Position);
                World.Objects.Add(inferno);
                _ = inferno.Load();

                var meteors = new Effects.MonsterMeteorFallEffect(this, 5);
                World.Objects.Add(meteors);
                _ = meteors.Load();
            }
            else
            {
                // Attack2 (CheckAttackTime(13)): MODEL_PIERCING+1 bolt from bone 11
                // (offset -50,100,0, pitch +45) with a JOINT_THUNDER sub2 link
                // to the packet target.
                var bolt = new Effects.MonsterArrowProjectileEffect(this, 11, LastAttackTargetId)
                {
                    ProjectileModelPath = "Skill/Piercing.bmd",
                    SourceOffset = new Vector3(-50f, 100f, 0f)
                };
                World.Objects.Add(bolt);
                _ = bolt.Load();

                SpawnMagicAttackEffect(new[] { 11 }, attackType);
            }
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanDie.wav", Position, listenerPosition);
        }
    }
}
