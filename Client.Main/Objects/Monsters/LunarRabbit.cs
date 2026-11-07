using Client.Main.Content;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(413, "Lunar Rabbit")]
    public class LunarRabbit : SoundMonsterObject
    {
        protected override string MoveSoundPath => "Sound/SE_Ev_rabbit_walk.wav";
        protected override string DamageSoundPath => "Sound/SE_Ev_rabbit_damage.wav";
        protected override string DeathSoundPath => "Sound/SE_Ev_rabbit_death.wav";
        private double _walkSoundElapsed;

        public LunarRabbit()
        {
            Scale = 0.8f;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!IsMoving)
            {
                _walkSoundElapsed = 0;
                return;
            }

            _walkSoundElapsed += gameTime.ElapsedGameTime.TotalSeconds;
            if (_walkSoundElapsed >= 0.8)
            {
                _walkSoundElapsed = 0;
                PlayMonsterSound(MoveSoundPath);
            }
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster128.bmd");
            await base.Load();
        }
    }
}
