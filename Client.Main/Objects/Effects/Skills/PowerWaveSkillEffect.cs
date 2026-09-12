#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Power Wave (skill 11 / AT_SKILL_POWERWAVE). The original spawns
    /// MODEL_MAGIC2 at the caster and lets it fly forward along the caster's facing
    /// direction, so the skill's target point is only used to turn the caster around.
    /// </summary>
    [SkillVisualEffect(11)]
    public sealed class PowerWaveSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new PowerWaveEffect(context.Caster);
        }
    }
}
