#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord "Fire Scream" skill (ID 78, AT_SKILL_DARK_SCREAM).
    /// SourceMain5.2 spawns the three dark/fire pillar pairs at the caster, so the effect
    /// ignores the skill's area target point and follows the caster instead.
    /// </summary>
    [SkillVisualEffect(78)]
    public sealed class FireScreamSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new FireScreamEffect(context.Caster);
        }
    }
}
