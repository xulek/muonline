using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(463, "Fire Flame Ghost")]
    public class FireFlameGhost : SoundMonsterObject
    {
        protected override string DamageSoundPath => "Sound/UmbMon_Damage01.wav";
        protected override string DeathSoundPath => "Sound/UmbMon_Dead.wav";
        private double _previousWalkFrame = -1;
        private bool _alternateStep;

        public FireFlameGhost()
        {
            Scale = 0.8f;
            HiddenMesh = 2;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!IsMoving || CurrentAction != (int)MonsterActionType.Walk || Model?.Actions == null ||
                CurrentAction >= Model.Actions.Length || Model.Actions[CurrentAction] == null)
            {
                _previousWalkFrame = -1;
                return;
            }

            var action = Model.Actions[CurrentAction];
            int frames = Math.Max(action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys, 1);
            double frame = GetAnimationTime() % frames;
            bool crossed = _previousWalkFrame < 0 ? frame >= 1 && frame < 2
                : frame < _previousWalkFrame ? _previousWalkFrame < 1 || frame >= 1
                : _previousWalkFrame < 1 && frame >= 1;
            _previousWalkFrame = frame;
            if (crossed)
            {
                PlayMonsterSound(_alternateStep ? "Sound/UmbMon_Walk02.wav" : "Sound/UmbMon_Walk01.wav");
                _alternateStep = !_alternateStep;
            }
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster155.bmd");
            await base.Load();
        }
    }
}
