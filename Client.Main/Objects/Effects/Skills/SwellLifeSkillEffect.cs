#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Swell Life (skill 48): radial spirit burst and orange ground circles.
    /// </summary>
    [SkillVisualEffect(48)] // Swell Life (AT_SKILL_VITALITY)
    public sealed class SwellLifeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SwellLifeCastEffect(context.Caster);
        }
    }
}
