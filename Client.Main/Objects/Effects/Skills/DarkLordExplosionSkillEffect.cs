#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Explosion (AT_SKILL_EXPLODE 79): bombs on the target character.
    /// </summary>
    [SkillVisualEffect(79)]
    public sealed class DarkLordExplosionSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            if (context.TargetId != 0 && context.World.TryGetWalkerById(context.TargetId, out WalkerObject target))
                return new DarkLordExplosionEffect(context.Caster, target.WorldPosition.Translation);

            // Sent as an area skill: the server's target tile stands in for the target character.
            return context.TargetPosition is { } position ? new DarkLordExplosionEffect(context.Caster, position) : null;
        }
    }
}
