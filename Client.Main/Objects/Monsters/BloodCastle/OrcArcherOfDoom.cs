using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(137, "Orc Archer of Doom")]
    public class OrcArcherOfDoom : MonsterObject
    {
        private WeaponObject _leftHandWeapon;

        public OrcArcherOfDoom()
        {
            // SourceMain5.2 CreateMonster MONSTER_ORC_ARCHER_OF_DOOM (ZzzCharacter.cpp:13399):
            // Scale = 1.2f, HiddenMesh = 1, Weapon[1] = Battle Bow +5 (LinkBone 39)
            Scale = 1.2f;
            HiddenMesh = 1;
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 39,
                ItemLevel = 5
            };
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            var bow = ItemDatabase.GetItemDefinition(4, 3); // Battle Bow
            if (bow != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(bow.TexturePath);

            Model = await BMDLoader.Instance.Prepare($"Monster/Monster47.bmd");
            await base.Load();
        }
    }
}
