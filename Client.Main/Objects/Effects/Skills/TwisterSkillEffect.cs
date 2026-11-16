#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Twister (AT_SKILL_STORM = 8): MODEL_STORM moving forward from the caster.
    /// </summary>
    [SkillVisualEffect(8)] // Twister
    public sealed class TwisterSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new TwisterEffect(context.Caster);
        }
    }
}
