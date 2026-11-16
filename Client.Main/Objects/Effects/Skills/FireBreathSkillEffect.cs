#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Fire Breath (skill 49): BITMAP_SHOTGUN flame stream ending in a spark bomb.
    /// </summary>
    [SkillVisualEffect(49)] // Fire Breath (AT_SKILL_RIDER)
    public sealed class FireBreathSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new FireBreathEffect(context.Caster);
        }
    }
}
