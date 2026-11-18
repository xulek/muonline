using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(505, "Jerint")]
    public class Jerint : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId == 72;
        protected override bool VaryBasicAttack => !IsImperialGuardian;
        private static readonly Vector3 SteelTint = new(0.55f, 0.7f, 1f);
        private static readonly int[] LightningBones = { 61 };
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/Jelint_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav" : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType switch
        {
            1 => "Sound/w69w70w71w72/Jelint_attack1.wav",
            2 => "Sound/eBloodAttack.wav",
            3 => "Sound/w69w70w71w72/Jelint_attack3.wav",
            _ => null,
        };

        public Jerint()
        {
            Scale = 1.45f;
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!IsImperialGuardian)
                return;

            switch (progress.Action)
            {
                case MonsterActionType.Attack1 when progress.CrossedFrame(7.5):
                    MonsterAttackTrailEffect.Spawn(this, progress.Action, 7.5f, 10.6f,
                        61, 51, new Vector3(1f, 0.5f, 0.2f));
                    break;
                case MonsterActionType.Attack2:
                    if (progress.CrossedFrame(3.0))
                        MonsterAttackModelEffect.Spawn(World, "Skill/Circle01.bmd", Position, Angle,
                            new Vector3(1f, 0.28f, 0.16f), 0.55f, 0.9f);
                    if (progress.CrossedFrame(6.0))
                        MonsterAttackTrailEffect.Spawn(this, progress.Action, 6f, 9.8f, 61, 51, SteelTint);
                    if (progress.CrossedFrame(8.0) && TryGetAttackTarget(out _))
                        SpawnMagicAttackEffect(LightningBones, progress.Action);
                    break;
                case MonsterActionType.Attack3:
                    if (progress.CrossedFrame(0.2))
                        PlayAttackSound("Sound/BLOW_OF_DESTRUCTION.wav");
                    if (progress.CrossedFrame(9.8))
                        MonsterAttackTrailEffect.Spawn(this, progress.Action, 9.8f, 12.5f, 52, 58, SteelTint);
                    if (progress.CrossedTick(18) && TryGetAttackTargetPosition(out Vector3 target))
                    {
                        MonsterAttackModelEffect.Spawn(World, "Effect/knight_plancrack_grand.bmd",
                            target, Angle, SteelTint, 1.1f, 1f, animated: true);
                        MonsterAttackModelEffect.Spawn(World, "Effect/nightwater01.bmd",
                            target, Angle, SteelTint, 0.8f, 1.6f, animated: true);
                    }
                    break;
            }
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Jelint_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster166.bmd");
            await base.Load();
        }
    }
}
