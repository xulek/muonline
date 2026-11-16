#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for classic Ice Storm and its five Ice Up master variants.
    /// </summary>
    [SkillVisualEffect(39)]
    [SkillVisualEffect(302)]
    [SkillVisualEffect(303)]
    [SkillVisualEffect(304)]
    [SkillVisualEffect(305)]
    [SkillVisualEffect(306)]
    public sealed class IceStormSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            Vector3 center = context.TargetPosition ?? context.Caster.WorldPosition.Translation;
            return new IceStormEffect(context.Caster, center);
        }
    }
}
