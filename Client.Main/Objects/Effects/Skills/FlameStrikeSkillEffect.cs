#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Magic Gladiator Flame Strike visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(236)] // Flame Strike (AT_SKILL_FLAME_STRIKE)
    public sealed class FlameStrikeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new FlameStrikeEffect(context.Caster);
        }
    }
}
