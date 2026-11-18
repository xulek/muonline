using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(308, "Death Tree")]
    public class DeathTree : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/dt_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/dt_idle1.wav" : "Sound/w34/dt_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/dt_attack2.wav" : "Sound/w34/dt_attack1.wav";

        public DeathTree()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster105.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId == 33 && progress.Action == MonsterActionType.Attack2 && progress.CrossedTick(10))
                MonsterAttackModelEffect.Spawn(World, "Object34/tree_eff.bmd", Position,
                    Angle, new Vector3(0.55f, 0.9f, 0.48f), 0.9f, Scale, speed: 270f, animated: true);
        }
    }
}
