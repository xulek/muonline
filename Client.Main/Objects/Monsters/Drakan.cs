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
    [NpcInfo(73, "Drakan")]
    public class Drakan : MonsterObject
    {
        public Drakan()
        {
            Scale = 0.8f;
            MoveSpeed = 250f;
            // SourceMain5.2 RenderCharacter: DRAKAN gets the RENDER_CHROME |
            // RENDER_BRIGHT pass tinted (0.2, 0.2, 0.8) and then RENDER_CHROME2 |
            // RENDER_LIGHTMAP tinted white (lightmap part not reproduced).
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(0.2f, 0.2f, 0.8f);
            BrightOverlayTexturePath2 = "Effect/Chrome02.jpg";
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[]
                {
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
                    52, 53, 54, 55, 56, 57, 58
                },
                PrimaryTexturePath = "Effect/light.jpg",
                PrimaryScale = 0.8f,
                LightColor = new Color(26, 26, 255),
                HideDuringDeath = true
            });
            // SourceMain5.2 RenderCharacter(MONSTER_DRAKAN): JOINT_THUNDER chains
            // between consecutive bones (13->14->15->16 and 22->23), sub7 scale 20.
            Children.Add(new MonsterBoneLightningEffect
            {
                LineScale = 0.3f,
                BonePairs = new[] { 13, 14, 14, 15, 15, 16, 22, 23 }
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

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 165, 165, 166, 166, 167);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakan1.wav", Position, listenerPosition); // Sound 165
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition); // Sound 166

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            if (attackType == 1)
            {
                // SourceMain5.2 AttackEffect MONSTER_DRAKAN Attack1 (CheckAttackTime(11)):
                // CreateInferno + MODEL_SKILL_INFERNO at self, plus 5 falling
                // MODEL_PIERCING+1 meteors scattered ±500 around the caster.
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
                // (offset -50,100,0, pitch +45, light 1.0/0.5/0.0) with a JOINT_THUNDER
                // sub2 link to the packet target.
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
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanDie.wav", Position, listenerPosition); // Sound 167
        }
    }
}
