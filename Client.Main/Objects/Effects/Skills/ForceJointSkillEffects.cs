#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Factory for Mana Rays (ID 73, AT_SKILL_DEATH_CANNON). See <see cref="ForceJointSkillEffect"/>.</summary>
    [SkillVisualEffect(73)]
    public sealed class ManaRaysSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            return new ForceJointSkillEffect(context.Caster, SupportSkillTargets.ResolveTarget(context), fireBlast: false);
        }
    }

    /// <summary>Factory for Fire Blast (ID 74, AT_SKILL_SPACE_SPLIT). See <see cref="ForceJointSkillEffect"/>.</summary>
    [SkillVisualEffect(74)]
    public sealed class FireBlastSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            return new ForceJointSkillEffect(context.Caster, SupportSkillTargets.ResolveTarget(context), fireBlast: true);
        }
    }
}
