using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;

using Joints = Client.Main.Objects.Effects.Joints;

namespace Client.Main.Objects.Monsters
{
    public class Kundun : MonsterObject
    {
        private readonly WeaponObject _staff;

        public Kundun()
        {
            Scale = 2f;
            MoveSpeed = 250f;

            _staff = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 49
            };
            Children.Add(_staff);
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 8, 9 },
                PrimaryTexturePath = "Effect/energy.jpg",
                PrimaryScale = 0.4f,
                LightColor = new Color(255, 0, 0),
                PulseSpeed = 0.003f,
                PulseBase = 0.8f,
                PulseAmplitude = 0.2f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 100 },
                PrimaryTexturePath = "Effect/flareBlue.jpg",
                PrimaryScale = 1.2f,
                LightColor = Color.White,
                PulseSpeed = 0.001f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.25f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 100 },
                PrimaryTexturePath = "Effect/flareRed.jpg",
                PrimaryScale = 1.2f,
                LightColor = Color.White,
                RequiredAction = (int)MonsterActionType.Shock,
                PulseSpeed = 0.001f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.25f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBreathEffect
            {
                SourceBone = 100,
                SourceOffset = Vector3.Zero,
                EmitWhenNoTriggers = true,
                EmissionRate = 12.5f,
                MinScale = 0.9f,
                MaxScale = 1.1f,
                BreathColor = Color.White
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster65.bmd");
            var staff = ItemDatabase.GetItemDefinition(5, 11); // Staff of Kundun
            if (staff != null)
                _staff.Model = await BMDLoader.Instance.Prepare(staff.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.30f);
            SetActionSpeed(MonsterActionType.Attack2, 0.25f);
            SetActionSpeed(MonsterActionType.Shock, 0.15f);
            SetActionSpeed(MonsterActionType.Die, 0.25f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mKundunIdle.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = attackType == 1 ? "Sound/mKundunAttack1.wav" : "Sound/mKundunAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            if (attackType == 1)
            {
                // SourceMain5.2 RenderHellasMonsterVisual MODEL_ILLUSION_OF_KUNDUN
                // ATTACK1 (GMHellas.cpp:1684-1698): MODEL_CUNDUN_SKILL sub0 spawned
                // from bone 49. No CUNDUN model exists in the port, so the inferno
                // burst approximates the ground slam visual.
                var inferno = new Effects.ScrollOfInfernoEffect(this, Position);
                World.Objects.Add(inferno);
                _ = inferno.Load();

                SpawnMagicAttackEffect(new[] { 49 }, attackType);
            }
            else
            {
                // ATTACK2 (GMHellas.cpp:1700-1731): MODEL_CUNDUN_SKILL sub1 above
                // bone 49 (Z forced to 400) + 24x JOINT_SPIRIT2 sub14, scale 100,
                // yaw stepped by 30 degrees. sub14 joints are stationary
                // (Velocity 0); SpiritBurst reproduces the radial fan.
                Vector3 origin = GetBoneWorldPosition(49);
                for (int i = 0; i < 24; i++)
                {
                    var spirit = Joints.SourceJointEffect.SpiritBurst(
                        origin,
                        i * 30f,
                        20f,
                        scale: 100f);
                    World.Objects.Add(spirit);
                    _ = spirit.Load();
                }
            }
        }

        private Vector3 GetBoneWorldPosition(int boneIndex)
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || boneIndex < 0 || boneIndex >= bones.Length)
                return WorldPosition.Translation;

            return (bones[boneIndex] * WorldPosition).Translation;
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            _staff.Hidden = true;
        }
    }
}
