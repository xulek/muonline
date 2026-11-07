using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(571, "Orcus")]
    public class Orcus : MonsterObject
    {
        public Orcus()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster212.bmd");
            await base.Load();
        }

        protected override void OnStartWalk()
        {
            base.OnStartWalk();
            _walkSoundToggle = !_walkSoundToggle;
            PlaySound(_walkSoundToggle ? "Sound/Karutan/Orcus_move1.wav" : "Sound/Karutan/Orcus_move2.wav");
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlaySound(attackType == 2 ? "Sound/Karutan/Orcus_attack_2.wav" : "Sound/Karutan/Orcus_attack_1.wav");
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            PlaySound("Sound/Karutan/Orcus_death.wav");
        }

        private bool _walkSoundToggle;

        private void PlaySound(string path)
        {
            if (World is WalkableWorldControl world && world.Walker != null)
                SoundController.Instance.PlayBufferWithAttenuation(path, Position, world.Walker.Position, 600f);
        }
    }
}
