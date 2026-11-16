#nullable enable
using Client.Main.Core.Utilities;
namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Energy Ball (AT_SKILL_ENERGYBALL = 17): BITMAP_ENERGY from the caster toward the target.
    /// </summary>
    [SkillVisualEffect(17)] // Energy Ball
    public sealed class EnergyBallSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new EnergyBallEffect(context.Caster, target);
        }
    }
}
