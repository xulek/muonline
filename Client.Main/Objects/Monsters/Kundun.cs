using Client.Main.Content;
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
    [NpcInfo(275, "Illusion of Kundun")]
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
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mKundunAttack1.wav" : "Sound/mKundunAttack2.wav";
            PlayAttackSound(sound);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World == null)
                return;

            if (progress.Action == MonsterActionType.Attack1 && progress.CrossedFrame(3.0))
            {
                var origin = Position;
                origin.Z = GetStaffPosition(progress.Action, 3.0).Z;
                MonsterAttackModelEffect.Spawn(World, "Skill/phoenix.bmd", origin,
                    Angle, new Vector3(1f, 0.62f, 0.32f), 0.82f, 0.7f,
                    speed: 700f, rays: 7, yawStep: MathHelper.ToRadians(12f), animated: true);
            }
            else if (progress.Action == MonsterActionType.Attack2)
            {
                if (progress.CrossedFrame(5.0))
                {
                    Vector3 origin = GetStaffPosition(progress.Action, 5.0);
                    origin.Z = Position.Z + 400f;
                    MonsterAttackModelEffect.Spawn(World, "Skill/dragonhead.bmd", origin,
                        Angle, new Vector3(0.62f, 0.72f, 1f), 1.1f, 1f, endScale: 1.35f, animated: true);
                }

                if (progress.CrossedFrame(6.0))
                {
                    Vector3 origin = GetStaffPosition(progress.Action, 6.0);
                    origin.Z = Position.Z + 400f;
                    // The reference makes two passes over the same twelve angles.
                    for (int i = 0; i < 12; i++)
                    {
                        var spirit = Joints.SourceJointEffect.SpiritBurst(origin, i * 30f, 20f, scale: 100f);
                        World.Objects.Add(spirit);
                        _ = spirit.Load();
                    }
                }
            }
        }

        private Vector3 GetStaffPosition(MonsterActionType action, double frame) =>
            TryComputeBoneAt((int)action, frame, 49, out Matrix pose)
                ? (pose * WorldPosition).Translation
                : Position + Vector3.UnitZ * 130f;

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mKundunAttack1.wav" : "Sound/mKundunAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            _staff.Hidden = true;
        }
    }
}
