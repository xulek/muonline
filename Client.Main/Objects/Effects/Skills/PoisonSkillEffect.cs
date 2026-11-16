#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Poison (AT_SKILL_POISON = 1): MODEL_POISON + BITMAP_SMOKE sub 1 at the target.
    /// </summary>
    [SkillVisualEffect(1)] // Poison
    public sealed class PoisonSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new PoisonEffect(context.Caster, target);
        }
    }
}
