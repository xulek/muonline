using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Client.Main.Objects.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(150, "Bali")]
    public class Bali : MonsterObject
    {
        public Bali()
        {
            Scale = 0.12f; // SourceMain5.2 MONSTER_BALI
            MoveSpeed = 250f;
            Children.Add(new MonsterBoneFireEffect
            {
                EmissionRate = 6.25f,
                ParticleScaleMin = 0.10f,
                ParticleScaleMax = 0.14f,
                ActionBoneMap = new System.Collections.Generic.Dictionary<int, int>
                {
                    [(int)MonsterActionType.Attack1] = 33,
                    [(int)MonsterActionType.Attack2] = 20,
                    [(int)MonsterActionType.Attack3] = 41,
                    [(int)MonsterActionType.Attack4] = 49
                }
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster33.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);

            // Specific PlaySpeed adjustments from C++ OpenMonsterModel
            if (Model?.Actions != null)
            {
                const int ATTACK3_INDEX = (int)MonsterActionType.Attack3;
                const int ATTACK4_INDEX = (int)MonsterActionType.Attack4;
                const int APPEAR_INDEX = (int)MonsterActionType.Appear;
                const int RUN_INDEX = (int)MonsterActionType.Run;

                if (ATTACK3_INDEX < Model.Actions.Length && Model.Actions[ATTACK3_INDEX] != null)
                    Model.Actions[ATTACK3_INDEX].PlaySpeed = 0.4f;
                if (ATTACK4_INDEX < Model.Actions.Length && Model.Actions[ATTACK4_INDEX] != null)
                    Model.Actions[ATTACK4_INDEX].PlaySpeed = 0.4f;
                if (APPEAR_INDEX < Model.Actions.Length && Model.Actions[APPEAR_INDEX] != null)
                    Model.Actions[APPEAR_INDEX].PlaySpeed = 0.4f;
                if (RUN_INDEX < Model.Actions.Length && Model.Actions[RUN_INDEX] != null)
                    Model.Actions[RUN_INDEX].PlaySpeed = 0.4f;
            }
            // C++: b->BoneHead = 6;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 126, 127, 128, 129, 127);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mBali1.wav" : "Sound/mBali2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            if (attackType == 1)
                SoundController.Instance.PlayBufferWithAttenuation("Sound/mBaliAttack1.wav", Position, listenerPosition); // Sound 128
            else
                SoundController.Instance.PlayBufferWithAttenuation("Sound/mBaliAttack2.wav", Position, listenerPosition); // Sound 129
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mBaliAttack1.wav" : "Sound/mBaliAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBali2.wav", Position, listenerPosition); // Sound 127
        }
    }
}
