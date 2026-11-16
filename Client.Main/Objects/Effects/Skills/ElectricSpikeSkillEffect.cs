#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord Electric Spike beam visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(65)] // Electric Spike (AT_SKILL_THUNDER_STRIKE)
    public sealed class ElectricSpikeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new ElectricSpikeEffect(context.Caster);
        }
    }
}
