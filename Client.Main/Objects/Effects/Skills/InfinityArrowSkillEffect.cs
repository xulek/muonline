#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Infinity Arrow (77): ReceiveMagic CreateEffect(MODEL_INFINITY_ARROW) on the caster.</summary>
    [SkillVisualEffect(77)] // Infinity Arrow
    public sealed class InfinityArrowSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            return new InfinityArrowEffect(context.Caster);
        }
    }
}
