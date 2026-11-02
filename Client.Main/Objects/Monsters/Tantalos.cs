using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(58, "Tantalos")]
    public class Tantalos : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private GlowingEyesEffect _eyeGlow;

        public Tantalos()
        {
            AnimationSpeed = 12.5f; // SetActionSpeed doubles the source speed; 2 * 12.5 matches 25 FPS.
            Scale = 1.8f;
            BlendMesh = 2; // Normal blending, not full transparency like Zaikan
            BlendMeshLight = 1.0f;
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 43
            };
            Children.Add(_rightHandWeapon);
            Children.Add(new SourceMonsterSandSmokeEffect());

            // Eyes: bones 24 (Right), 25 (Left) — original MoveEye(o, b, 24, 25)
            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 25,
                RightEyeBone = 24,
                GlowColor = new Color(30, 120, 255),
                GlowScale = 1.1f,
                GlowAlpha = 0.9f,
                TrailWidth = 5f,
                TrailDuration = 0.6f
            };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster43.bmd");
            var weapon = ItemDatabase.GetItemDefinition(0, 16); // Sword of Destruction
            if (weapon != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.4f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f * 0.4f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.4f);
            SetActionSpeed(MonsterActionType.Attack1, 0.35f);
            SetActionSpeed(MonsterActionType.Attack2, 0.35f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 0.4f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 20;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 145, 146, 147, 148, 149);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/jaikan1.wav" : "Sound/jaikan2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/jaikan_attack1.wav" : "Sound/jaikan_attack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/jaikan_attack1.wav" : "Sound/jaikan_attack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/jaikan_die.wav", Position, listenerPosition); // Sound 149
        }
    }
}
