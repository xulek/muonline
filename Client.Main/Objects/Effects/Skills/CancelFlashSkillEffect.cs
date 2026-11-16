#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Cancel Stun (68) / Cancel Invisibility (71): BITMAP_FLASH sub0/1 striking the target (only when target != caster).</summary>
    [SkillVisualEffect(68)] // Cancel Stun
    [SkillVisualEffect(71)] // Cancel Invisibility
    public sealed class CancelFlashSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            var target = SupportSkillTargets.ResolveTarget(context);
            if (target == null || ReferenceEquals(target, context.Caster))
                return null;

            // AttackStage jumps AttackTime straight to the limit for these skills.
            return new FlashStrikeEffect(context.Caster!, target);
        }
    }
}
