using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(309, "Hell Maine")]
    public class HellMaine : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/hm_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/hm_idle1.wav" : "Sound/w34/hm_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            1 => "Sound/w34/hm_bloodywind.wav",
            2 => "Sound/w34/hm_firelay.wav",
            _ => "Sound/w34/hm_attack1.wav"
        };

        public HellMaine()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster106.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 33 || !progress.CrossedTick(10))
                return;

            if (progress.Action == MonsterActionType.Attack1)
                MonsterAttackModelEffect.Spawn(World, "Skill/Storm01.bmd", Position, Angle,
                    new Vector3(1f, 0.4f, 0.25f), 1f, 1.2f, speed: 310f, animated: true);
            else if (progress.SkillId == 3 && TryGetAttackTargetPosition(out Vector3 target))
            {
                var flame = new FlameEffect(this, target, area: false);
                flame.ReleaseImmediately();
                World.Objects.Add(flame);
                _ = flame.Load();
            }
        }
    }
}
