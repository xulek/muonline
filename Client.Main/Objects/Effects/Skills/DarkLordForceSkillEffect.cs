#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord Force / Force Wave visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(60)] // Force (AT_SKILL_FORCE)
    [SkillVisualEffect(66)] // Force Wave (AT_SKILL_FORCE_WAVE)
    public sealed class DarkLordForceSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new DarkLordForceEffect(context.Caster);
        }
    }
}
