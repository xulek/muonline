#nullable enable
using Client.Main.Core.Utilities;
namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord Fire Burst visual used by the base, strength and mastery skills.
    /// </summary>
    [SkillVisualEffect(61)]
    [SkillVisualEffect(508)]
    [SkillVisualEffect(514)]
    public sealed class FireBurstSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new FireBurstEffect(context.Caster, context.TargetId);
        }
    }
}
