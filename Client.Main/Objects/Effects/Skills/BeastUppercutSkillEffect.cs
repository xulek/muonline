#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Beast Uppercut" skill (ID 261, AT_SKILL_STAMP). See <see cref="BeastUppercutEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(261)]
    public sealed class BeastUppercutSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new BeastUppercutEffect(context.Caster, context.TargetId);
        }
    }
}
