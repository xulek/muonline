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

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(160, "Schriker")]
    public class Schriker : MonsterObject
    {
        private readonly WeaponObject _rightHandWeapon;
        private readonly WeaponObject _leftHandWeapon;

        protected virtual string IdleSound1 => "Sound/mSvIdle1.wav";
        protected virtual string IdleSound2 => "Sound/mSvIdle2.wav";
        protected virtual string AttackSound1 => "Sound/mSvAttack1.wav";
        protected virtual string AttackSound2 => "Sound/mSvAttack2.wav";
        protected virtual string DeathSound => "Sound/mSvDeath.wav";

        public Schriker()
        {
            Scale = 1.2f;
            MoveSpeed = 250f;

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 41
            };
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 51
            };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 33, 34 },
                PrimaryTexturePath = "Effect/energy.jpg",
                PrimaryScale = 0.1f,
                LightColor = new Color(0, 230, 255),
                PulseSpeed = 0.003f,
                PulseBase = 0.8f,
                PulseAmplitude = 0.2f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 33, 34 },
                PrimaryTexturePath = "Effect/shiny02.jpg",
                PrimaryScale = 0.7f,
                LightColor = new Color(26, 77, 255),
                PulseSpeed = 0.003f,
                PulseBase = 0.8f,
                PulseAmplitude = 0.2f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBones = new[] { 41, 51 },
                SourceOffsets = new[]
                {
                    new Vector3(0f, -40f, 0f),
                    new Vector3(0f, -64f, 0f),
                    new Vector3(0f, -88f, 0f),
                    new Vector3(0f, -112f, 0f),
                    new Vector3(0f, -136f, 0f),
                    new Vector3(0f, -160f, 0f)
                },
                EmitAllSourceBones = true,
                EmissionRate = 150f,
                TexturePath = "Effect/Fire05.jpg",
                TextureColumns = 4,
                SourceParticleSubType = 12,
                ParticleLight = new Vector3(0.9f, 0.9f, 1f),
                ParticleScaleMin = 0.45f,
                ParticleScaleMax = 0.60f,
                StopOnDeath = true
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster70.bmd");
            var doubleBlade = ItemDatabase.GetItemDefinition(0, 13); // Double Blade
            if (doubleBlade != null)
            {
                var model = await BMDLoader.Instance.Prepare(doubleBlade.TexturePath);
                _rightHandWeapon.Model = model;
                _leftHandWeapon.Model = model;
            }
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.10f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? IdleSound1 : IdleSound2;
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? AttackSound1 : AttackSound2;
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation(DeathSound, Position, listenerPosition);
        }
    }
}
