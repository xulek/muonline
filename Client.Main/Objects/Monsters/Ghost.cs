using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // Needed for BlendState
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(11, "Ghost")]
    public class Ghost : MonsterObject // Renamed
    {
        public Ghost()
        {
            RenderShadow = false; // Ghosts typically don't cast shadows
            // SourceMain5.2 CreateMonster presets c->Blood = true (ZzzCharacter.cpp:13845),
            // which blocks the one-shot death blood in EtcStopAnimationSetting — equivalent
            // to no death-blood decals here. (MODEL_GHOST_MONSTER is NOT exempt from the
            // hit-blood burst at ZzzCharacter.cpp:4712, which only skips MODEL_GHOST.)
            Blood = false;
            Alpha = 0.4f; // SourceMain5.2: c->Object.AlphaTarget = 0.4f
            MoveSpeed = 375f; // SourceMain5.2: c->MoveSpeed = 15 (15 * 25 FPS)
            BlendState = Blendings.Alpha; // Use Alpha blending
        }

        public override async Task Load()
        {
            // Model Loading Type: 7 -> File Number: 7 + 1 = 8
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster08.bmd");
            await base.Load();

            // SourceMain5.2 ZzzOpenData.cpp: base monster action speeds.
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 35, 36, 37, 38, 39);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGhost1.wav"
                : "Sound/mGhost2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGhostAttack1.wav"
                : "Sound/mGhostAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGhostAttack1.wav"
                : "Sound/mGhostAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGhostDie.wav", Position, listenerPosition);
        }
    }
}
