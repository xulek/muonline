using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(504, "Gaion Kharein")]
    public class GaionKharein : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId == 72;
        protected override bool VaryBasicAttack => !IsImperialGuardian;
        private static readonly (int Bone, string Model)[] Swords =
        {
            (2, "Monster/Boss_Karane_sword_left02.bmd"),
            (10, "Monster/Boss_Karane_sword_right02.bmd"),
            (4, "Monster/Boss_Karane_sword_left01.bmd"),
            (8, "Monster/Boss_Karane_sword_right01.bmd"),
            (6, "Monster/Boss_Karane_sword_main01.bmd"),
        };
        protected override string MoveSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/GaionKalein_move.wav" : null;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/GrandWizard_death.wav" : null;

        public GaionKharein()
        {
            Scale = 1.4f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/GaionKalein_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster165.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!IsImperialGuardian || !progress.CrossedFrame(0.0) ||
                !TryGetAttackTargetPosition(out Vector3 target))
                return;

            (int start, int count) = progress.Action switch
            {
                MonsterActionType.Attack1 => (0, 2),
                MonsterActionType.Attack2 => (2, 2),
                MonsterActionType.Attack3 => (0, Swords.Length),
                MonsterActionType.Attack4 => (4, 1),
                _ => (0, 0),
            };

            target.Z += 55f;
            Matrix transform = WorldPosition;
            for (int i = start; i < start + count; i++)
            {
                var (bone, path) = Swords[i];
                if (!TryComputeBoneAt((int)progress.Action, 0, bone, out Matrix pose))
                    continue;

                var sword = new MonsterArrowProjectileEffect((pose * transform).Translation, target, path, Scale);
                World.Objects.Add(sword);
                _ = sword.Load();
            }
        }
    }
}
