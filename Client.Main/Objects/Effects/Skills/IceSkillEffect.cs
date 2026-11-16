#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Ice (AT_SKILL_SLOW = 7): MODEL_ICE + 5 MODEL_ICE_SMALL at the target.
    /// </summary>
    [SkillVisualEffect(7)] // Ice
    public sealed class IceSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new IceEffect(context.Caster, target);
        }
    }
}
