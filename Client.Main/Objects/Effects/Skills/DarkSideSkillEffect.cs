#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Dark Side" skill (ID 263, AT_SKILL_DARKSIDE). See <see cref="DarkSideEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(263)]
    public sealed class DarkSideSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new DarkSideEffect(context.Caster, context.TargetId);
        }
    }
}
