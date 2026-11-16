#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Cometfall (skill 13): two falling Blast01 comets at the target tile.
    /// </summary>
    [SkillVisualEffect(13)] // Cometfall (AT_SKILL_BLAST)
    public sealed class CometfallSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            Vector3 target = context.TargetPosition ?? context.Caster.WorldPosition.Translation;
            if (context.TargetPosition == null && context.TargetId != 0 &&
                context.World.TryGetWalkerById(context.TargetId, out var walker))
                target = walker.WorldPosition.Translation;

            return new CometfallEffect(context.Caster, target);
        }
    }
}
