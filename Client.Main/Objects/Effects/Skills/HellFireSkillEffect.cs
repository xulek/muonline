#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Hellfire (AT_SKILL_HELL = 10): MODEL_CIRCLE + MODEL_CIRCLE_LIGHT at the caster.
    /// </summary>
    [SkillVisualEffect(10)] // Hellfire
    public sealed class HellFireSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new HellFireEffect(context.Caster);
        }
    }
}
