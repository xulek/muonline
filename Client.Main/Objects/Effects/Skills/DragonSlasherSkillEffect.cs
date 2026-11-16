#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Dragon Slasher" skill (ID 265, AT_SKILL_DRAGON_KICK). See <see cref="DragonSlasherEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(265)]
    public sealed class DragonSlasherSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new DragonSlasherEffect(context.Caster, context.TargetId);
        }
    }
}
