#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Magic Gladiator Fire Slash visual (SourceMain5.2 port). Works for any caster.
    /// </summary>
    [SkillVisualEffect(55)] // Fire Slash (AT_SKILL_REDUCEDEFENSE)
    public sealed class FireSlashSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new FireSlashEffect(context.Caster, context.SkillId);
        }
    }
}
