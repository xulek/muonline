using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(515, "Grand Wizard")]
    public class GrandWizard : SoundMonsterObject
    {
        protected override bool VaryBasicAttack => World?.MapId != 70;
        protected override string MoveSoundPath => World?.MapId == 70 ? "Sound/w69w70w71w72/GaionKalein_move.wav" : null;
        protected override string DeathSoundPath => World?.MapId == 70 ? "Sound/w69w70w71w72/GrandWizard_death.wav" : null;
        private GlowingEyesEffect _eyeGlow;

        public GrandWizard()
        {
            Scale = 1.3f;
            // Eyes: bones 79,33 or 80,34 (two MoveEye calls in original)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 33, RightEyeBone = 79, GlowColor = new Color(60, 140, 255) };
            Children.Add(_eyeGlow);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 70)
                return;

            if (progress.Action == MonsterActionType.Attack1 && progress.CrossedTick(1) &&
                TryGetAttackTargetPosition(out Vector3 meteorTarget))
            {
                MonsterFireRainEffect.SpawnForAttack(this, meteorTarget, fireCount: 1);
                PlayAttackSound("Sound/eExplosion.wav", meteorTarget);
            }
            else if (progress.Action == MonsterActionType.Attack2 && progress.SkillStarted && progress.SkillId == 51 &&
                     TryGetAttackTargetPosition(out Vector3 flameTarget))
            {
                PlayAttackSound("Sound/w42/firepillar.wav", flameTarget);
                var flame = new FlameEffect(this, flameTarget, area: false);
                flame.ReleaseImmediately();
                World.Objects.Add(flame);
                _ = flame.Load();
            }
            else if (progress.Action == MonsterActionType.Attack2 && progress.SkillStarted && progress.SkillId == 52)
            {
                MonsterFireRainEffect.SpawnForAttack(this, fireCount: 5);
            }
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster176.bmd");
            await base.Load();
        }
    }
}
