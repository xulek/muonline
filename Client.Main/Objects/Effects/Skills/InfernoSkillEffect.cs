#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Inferno (AT_SKILL_INFERNO = 14): CreateInferno ring + MODEL_SKILL_INFERNO at the caster.
    /// </summary>
    [SkillVisualEffect(14)] // Inferno
    public sealed class InfernoSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new InfernoEffect(context.Caster);
        }
    }
}
