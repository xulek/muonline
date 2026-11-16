#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Lightning Orb (AT_SKILL_ALICE_LIGHTNINGORB = 216): cast hand thunders, the orb itself only with a target.
    /// </summary>
    [SkillVisualEffect(216)]
    public sealed class LightningOrbSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new LightningOrbEffect(context.Caster, target);
        }
    }
}
