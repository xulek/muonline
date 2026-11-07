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
    [NpcInfo(145, "Death Centurion")]
    public class LordCenturion : MonsterObject
    {
        private readonly WeaponObject _spear;

        public LordCenturion()
        {
            Scale = 1.5f;
            MoveSpeed = 250f;
            BlendMesh = 0;

            _spear = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 56,
                ItemLevel = 7
            };
            Children.Add(_spear);
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 0 },
                BoneOffsets = new[] { new Vector3(0f, 0f, 30f) },
                PrimaryTexturePath = "Effect/flareBlue.jpg",
                PrimaryScale = 2f,
                LightColor = new Color(255, 0, 0),
                PulseSpeed = 0.001f,
                PulseBase = 1f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.15f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 28, 29 },
                BoneOffsets = new[] { new Vector3(5f, 0f, 0f), new Vector3(5f, 0f, 0f) },
                PrimaryTexturePath = "Effect/flare01.jpg",
                PrimaryScale = 0.3f,
                LightColor = Color.White,
                PulseSpeed = 0.001f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.667f,
                HideDuringDeath = true
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster68.bmd");
            var spear = ItemDatabase.GetItemDefinition(3, 11); // Dragon Spear
            if (spear != null)
                _spear.Model = await BMDLoader.Instance.Prepare(spear.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.20f);
            SetActionSpeed(MonsterActionType.Attack2, 0.30f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mLsIdle1.wav" : "Sound/mLsIdle2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mLsAttack1.wav" : "Sound/mLsAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            _spear.Hidden = true;
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mLsDeath.wav", Position, listenerPosition);
        }
    }
}
