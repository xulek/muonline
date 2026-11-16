#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Strike of Destruction (skill 232): crack line toward the target tile and blue water impact.
    /// </summary>
    [SkillVisualEffect(232)] // Strike of Destruction (AT_SKILL_BLOW_OF_DESTRUCTION)
    public sealed class StrikeOfDestructionSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            Vector3? target = context.TargetPosition;
            if (target == null && context.TargetId != 0 &&
                context.World.TryGetWalkerById(context.TargetId, out var walker))
                target = walker.WorldPosition.Translation;

            return new StrikeOfDestructionEffect(context.Caster, target);
        }
    }
}
