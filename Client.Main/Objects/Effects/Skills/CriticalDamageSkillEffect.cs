#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord Increase Critical Damage weapon flashes visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(64)] // Increase Critical Damage (AT_SKILL_ADD_CRITICAL)
    public sealed class CriticalDamageSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new DarkLordCriticalDamageEffect(context.Caster);
        }
    }
}
