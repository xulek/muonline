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
    [NpcInfo(570, "Bone Scorpion")]
    public class BoneScorpion : MonsterObject
    {
        public BoneScorpion()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster211.bmd");
            await base.Load();
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlaySound("Sound/Karutan/BoneScorpion_attack.wav");
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            PlaySound("Sound/Karutan/BoneScorpion_hit.wav");
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            PlaySound("Sound/Karutan/BoneScorpion_death.wav");
        }

        private void PlaySound(string path)
        {
            if (World is WalkableWorldControl world && world.Walker != null)
                SoundController.Instance.PlayBufferWithAttenuation(path, Position, world.Walker.Position, 600f);
        }
    }
}
