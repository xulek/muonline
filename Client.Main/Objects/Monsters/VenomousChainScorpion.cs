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
    [NpcInfo(569, "Venomous Chain Scorpion")]
    public class VenomousChainScorpion : MonsterObject
    {
        public VenomousChainScorpion()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster210.bmd");
            await base.Load();
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlaySound("Sound/Karutan/ToxyChainScorpion_attack.wav");
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            PlaySound("Sound/Karutan/ToxyChainScorpion_hit.wav");
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            PlaySound("Sound/Karutan/ToxyChainScorpion_death.wav");
        }

        private void PlaySound(string path)
        {
            if (World is WalkableWorldControl world && world.Walker != null)
                SoundController.Instance.PlayBufferWithAttenuation(path, Position, world.Walker.Position, 600f);
        }
    }
}
