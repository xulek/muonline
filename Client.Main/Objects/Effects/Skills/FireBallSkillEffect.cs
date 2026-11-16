#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Fire Ball (AT_SKILL_FIREBALL = 4): MODEL_FIRE sub 1 from the caster toward the target.
    /// </summary>
    [SkillVisualEffect(4)] // Fire Ball
    public sealed class FireBallSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new FireBallEffect(context.Caster, target);
        }
    }
}
