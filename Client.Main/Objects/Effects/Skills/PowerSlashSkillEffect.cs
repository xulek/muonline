#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Magic Gladiator Power Slash visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(56)] // Power Slash (AT_SKILL_ICE_BLADE)
    public sealed class PowerSlashSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new PowerSlashEffect(context.Caster);
        }
    }
}
