#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Factory for Lance (ID 45, AT_SKILL_JAVELIN). See <see cref="LanceEffect"/>.</summary>
    [SkillVisualEffect(45)]
    public sealed class LanceSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            return new LanceEffect(context.Caster, SupportSkillTargets.ResolveTarget(context));
        }
    }
}
