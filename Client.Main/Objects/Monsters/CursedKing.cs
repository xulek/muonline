using Client.Main.Content;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(66, "Cursed King")]
    public class CursedKing : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/mCursedKingDie1.wav";

        protected override void OnIdle()
        {
            base.OnIdle();
            PlayMonsterSound(MuGame.Random.Next(2) == 0 ? "Sound/mCursedKing1.wav" : "Sound/mCursedKing2.wav");
        }

        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;

        public CursedKing()
        {
            Scale = 1.7f;
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 32
            };
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 43
            };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster49.bmd");
            await base.Load();
        }
    }
}
