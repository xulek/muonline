#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Flame (AT_SKILL_FLAME = 5): BITMAP_FLAME sub 5 at the target (to->Position, terrain height).
    /// </summary>
    [SkillVisualEffect(5)] // Flame
    public sealed class FlameSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            // ReceiveMagic (target): sub 5 at to->Position; ReceiveMagicContinue (area): sub 0 at the tile centre.
            if (context.TargetPosition.HasValue)
                return new FlameEffect(context.Caster, context.TargetPosition.Value, area: true);
            if (context.TargetId != 0 && context.World.TryGetWalkerById(context.TargetId, out var target))
                return new FlameEffect(context.Caster, target.WorldPosition.Translation, area: false);
            return null;
        }
    }
}
