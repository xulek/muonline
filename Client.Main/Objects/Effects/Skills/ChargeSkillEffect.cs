#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Charge" skill (ID 269, AT_SKILL_OCCUPY). See <see cref="SwordForceRushEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(269)]
    public sealed class ChargeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SwordForceRushEffect(context.Caster, occupy: true);
        }
    }
}
