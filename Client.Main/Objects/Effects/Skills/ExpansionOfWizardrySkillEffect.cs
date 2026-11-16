#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Expansion of Wizardry (skill 233): magic_powerup model, shrinking rings and bone glow.
    /// </summary>
    [SkillVisualEffect(233)] // Expansion of Wizardry (AT_SKILL_SWELL_OF_MAGICPOWER)
    public sealed class ExpansionOfWizardrySkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new ExpansionOfWizardryEffect(context.Caster);
        }
    }
}
