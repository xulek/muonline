using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(149, "Necron")]
    public class Necron : MonsterObject
    {
        public Necron()
        {
            Scale = 1.2f;
            MoveSpeed = 250f;
            BlendMesh = 3;

            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 9, 10 },
                BoneOffsets = new[] { new Vector3(5f, 0f, 0f), new Vector3(5f, 0f, 0f) },
                PrimaryTexturePath = "Effect/flare01.jpg",
                PrimaryScale = 0.3f,
                LightColor = Color.White,
                PulseSpeed = 0.001f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.667f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 60 },
                PrimaryTexturePath = "Effect/energy.jpg",
                PrimaryScale = 0.7f,
                SecondaryTexturePath = "Effect/energy.jpg",
                SecondaryScale = 0.7f,
                TertiaryTexturePath = "Effect/flare01.jpg",
                TertiaryScale = 1.1f,
                LightColor = Color.White,
                PulseSpeed = 0.002f,
                PulseBase = 0.6f,
                PulseAmplitude = 0.3f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.14f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 63, 64, 65, 66, 67 },
                PrimaryTexturePath = "Effect/flare01.jpg",
                PrimaryScale = 0.5f,
                LightColor = new Color(26, 77, 255),
                PulseSpeed = 0.001f,
                PulseScaleBase = 1f,
                PulseScaleAmplitude = 0.4f,
                HideDuringDeath = true
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster69.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mNecronIdle1.wav" : "Sound/mNecronIdle2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mNecronAttack1.wav" : "Sound/mNecronAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mNecronDeath.wav", Position, listenerPosition);
        }
    }
}
