#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Lightning (AT_SKILL_THUNDER = 3): homing BITMAP_JOINT_THUNDER bolts while the cast runs.
    /// </summary>
    [SkillVisualEffect(3)] // Lightning
    public sealed class LightningSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new LightningEffect(context.Caster, target);
        }
    }
}
