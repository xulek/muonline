#nullable enable
using System.Threading.Tasks;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Scenes;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Soul Barrier (AT_SKILL_WIZARDDEFENSE = 16). ReceiveMagic: SetPlayerMagic; AttackStage default
    /// (ZzzCharacter.cpp:2876) releases at AnimationFrame >= 5; release switch ZzzCharacter.cpp:4835-4855.
    /// The orbiting joints themselves belong to eBuff_WizDefense (BuffEffectController).
    /// </summary>
    [SkillVisualEffect(16)]
    public sealed class SoulBarrierSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            return new ReleaseEffect(context.Caster, SupportSkillTargets.ResolveTargetOrCaster(context));
        }

        private sealed class ReleaseEffect : SourceSkillEffectBase
        {
            private readonly WalkerObject _target;

            public ReleaseEffect(WalkerObject caster, WalkerObject target)
                : base(caster, 5f, SupportSkillTargets.AttackStageDelayFrames * 0.04f, 1)
            {
                _target = target;
            }

            protected override Task LoadEffectContent() => Task.CompletedTask;

            protected override void OnStart()
            {
                if (MuGame.Instance?.ActiveScene is GameScene scene)
                    scene.BuffEffects?.ReleaseSoulBarrier(_target.NetworkId);
            }

            protected override void OnTick(float factor)
            {
            }

            protected override bool HasActiveContent() => false;

            protected override void OnDraw()
            {
            }
        }
    }
}
