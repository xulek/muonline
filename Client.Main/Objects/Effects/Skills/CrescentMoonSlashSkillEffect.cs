#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Crescent Moon Slash (skill 44): sparks while charging, then the SwordForce lunge.
    /// </summary>
    [SkillVisualEffect(44)] // Crescent Moon Slash (AT_SKILL_RUSH)
    public sealed class CrescentMoonSlashSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SwordForceRushEffect(context.Caster, occupy: false);
        }
    }
}
