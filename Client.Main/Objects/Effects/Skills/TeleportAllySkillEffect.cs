#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Teleport Ally (skill 15): CreateTeleportBegin on the target and CreateTeleportEnd on the caster.
    /// </summary>
    [SkillVisualEffect(15)] // Teleport Ally (AT_SKILL_TELEPORT_B)
    public sealed class TeleportAllySkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            if (context.TargetId != 0 && context.World.TryGetWalkerById(context.TargetId, out var target) &&
                !ReferenceEquals(target, context.Caster))
            {
                // CreateTeleportBegin(to)
                TeleportSparkEffect.Spawn(context.World, target.WorldPosition.Translation, target);
            }

            // CreateTeleportEnd(so)
            return new TeleportSparkEffect(context.Caster, context.Caster.WorldPosition.Translation, playSound: true);
        }
    }
}
