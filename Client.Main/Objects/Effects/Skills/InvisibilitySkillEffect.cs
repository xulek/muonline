#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Invisibility (70): BITMAP_MAGIC+1 sub6 on the target.</summary>
    [SkillVisualEffect(70)] // Invisibility
    public sealed class InvisibilitySkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            var target = SupportSkillTargets.ResolveTarget(context);
            return target == null ? null : new InvisibilityCastEffect(target);
        }
    }
}
