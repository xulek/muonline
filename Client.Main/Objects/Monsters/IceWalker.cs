using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(454, "Ice Walker")]
    public class IceWalker : RaklionMonsterObject
    {
        private readonly MonsterBreathEffect _breath = new() { SourceBone = 8, MinScale = 0.38f, MaxScale = 0.6f };
        protected override string AttackSoundPath => "Sound/w58w59/IceWalker_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/IceWalker_move.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_wolfhuman_death01.wav";

        public IceWalker()
        {
            Children.Add(_breath);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster146.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            // OpenMU sends Ice (0x19) immediately after its generic attack animation.
            if ((progress.Action == MonsterActionType.Attack2 || progress.SkillId == 7) && progress.CrossedFrame(4.4))
                _breath.EmitBurst(new Color(125, 175, 255), count: 14, speed: 640f);
        }
    }
}
