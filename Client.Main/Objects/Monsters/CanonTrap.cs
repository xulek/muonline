using Client.Main.Content;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(105, "Canon Trap")]
    public class CanonTrap : MonsterObject
    {
        private static readonly int[] CannonOrigin = { 0 };

        public CanonTrap()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"NPC/c_mon.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId == 38 && progress.CrossedTick(1) && TryGetAttackTarget(out _))
                SpawnMagicAttackEffect(CannonOrigin, progress.Action, Vector3.UnitZ * 85f);
        }
    }
}
