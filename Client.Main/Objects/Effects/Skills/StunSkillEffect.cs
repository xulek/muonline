#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Stun (67): BITMAP_FLASH sub7 bolts from the caster at the AttackTime limit.</summary>
    [SkillVisualEffect(67)] // Stun
    public sealed class StunSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            return new StunCastEffect(context.Caster);
        }
    }
}
