using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(457, "Coolutin")]
    public class Coolutin : RaklionMonsterObject
    {
        protected virtual string ModelPath => "Monster/Monster149.bmd";
        private readonly MonsterBreathEffect _breath = new() { SourceBone = 19, MinScale = 0.36f, MaxScale = 0.65f };
        protected override string AttackSoundPath => "Sound/w58w59/Coolertin_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/Coolertin_move.wav";
        protected override string DeathSoundPath => "Sound/mHellSpiderDie.wav";

        public Coolutin()
        {
            Children.Add(_breath);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World == null || !progress.CrossedFrame(1.7) ||
                !TryComputeBoneAt((int)progress.Action, 1.7, 19, out Matrix pose))
                return;

            var tint = progress.Action == MonsterActionType.Attack2
                ? new Color(70, 230, 85) : new Color(100, 155, 255);
            _breath.EmitBurst(tint, count: 10, speed: 530f);
            MonsterAttackModelEffect.Spawn(World, "Effect/chysukmoon.bmd", (pose * WorldPosition).Translation,
                Angle, tint.ToVector3(), 0.7f, 0.3f, speed: 670f);
        }
    }
}
