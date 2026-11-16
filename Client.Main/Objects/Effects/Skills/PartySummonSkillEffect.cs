#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord party summon circle visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(63)] // Summon party (AT_SKILL_PARTY_TELEPORT)
    public sealed class PartySummonSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new PartySummonEffect(context.Caster);
        }
    }
}
