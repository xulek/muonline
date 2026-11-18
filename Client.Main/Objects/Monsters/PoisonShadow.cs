using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(39, "Poison Shadow")]
    public class PoisonShadow : ShadowMonster // Inherits from ShadowMonster
    {
        public PoisonShadow() : base(true)
        {
            Scale = 1.2f; // Inherited scale
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            SpawnMagicAttackEffect(
                new[] { 33 },
                (MonsterActionType)CurrentAction,
                new Vector3(0f, -130f, 0f));
        }

        // Load() and sound methods inherited from ShadowMonster
        // Sounds are inherited from ShadowMonster
    }
}
