#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Magic Gladiator Gigantic Storm visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(237)] // Gigantic Storm (AT_SKILL_GIGANTIC_STORM)
    public sealed class GiganticStormSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new GiganticStormEffect(context.Caster);
        }
    }
}
