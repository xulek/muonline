#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Combo (59): ReceiveMagic CreateEffect(MODEL_COMBO) on the caster.</summary>
    [SkillVisualEffect(59)] // Combo
    public sealed class ComboSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            return new ComboEffect(context.Caster);
        }
    }
}
