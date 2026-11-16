#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Ignore Defense / Increase Health / Increase Block" skill (ID 266-268, AT_SKILL_ATT/HP/DEF_UP_OURFORCES). See <see cref="RageBuffEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(266)]
    [SkillVisualEffect(267)]
    [SkillVisualEffect(268)]
    public sealed class RageBuffSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new RageBuffEffect(context.Caster, context.SkillId);
        }
    }
}
