using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(147, "Aegis")]
    public class Aegis : MonsterObject
    {
        public Aegis()
        {
            Scale = 1.4f;
            MoveSpeed = 250f;
            BlendMesh = 1;

            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 9, 10 },
                BoneOffsets = new[] { new Vector3(4f, 0f, 5f), new Vector3(4f, 0f, 5f) },
                PrimaryTexturePath = "Effect/energy.jpg",
                PrimaryScale = 0.1f,
                SecondaryTexturePath = "Effect/shiny02.jpg",
                SecondaryScale = 1f,
                LightColor = Color.White,
                PulseSpeed = 0.005f,
                PulseBase = 0.85f,
                PulseAmplitude = 0.15f,
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 3, 4 },
                PrimaryTexturePath = "Effect/flare01.jpg",
                PrimaryScale = 1.7f,
                SecondaryTexturePath = "Effect/shiny02.jpg",
                SecondaryScale = 1f,
                LightColor = new Color(179, 153, 255),
                HideDuringDeath = true
            });
            Children.Add(new MonsterBoneLightningEffect
            {
                RequiredAction = (int)MonsterActionType.Attack1,
                BonePairs = new[]
                {
                    10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16,
                    31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37
                },
                LineScale = 0.30f
            });
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBone = 14,
                SourceOffset = new Vector3(0f, -20f, 0f),
                EmissionRate = 125f,
                EmitOnlyDuringAttack = true,
                TexturePath = "Effect/Fire05.jpg",
                TextureColumns = 4,
                SourceParticleSubType = 12,
                ParticleScaleMin = 1.50f,
                ParticleScaleMax = 1.65f,
                StopOnDeath = true
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster67.bmd");
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
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mEsisIdle.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mEsisAttack1.wav" : "Sound/mEsisAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mEsisDeath.wav", Position, listenerPosition);
        }
    }
}
