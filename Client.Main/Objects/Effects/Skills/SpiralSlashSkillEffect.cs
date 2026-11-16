#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Magic Gladiator Spiral Slash visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(57)] // Spiral Slash (AT_SKILL_ONEFLASH)
    public sealed class SpiralSlashSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SpiralSlashEffect(context.Caster);
        }
    }
}
