#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Abolish Magic / Remove Buff (72): MODEL_SPEARSKILL sub5-7 spirals around the caster.</summary>
    [SkillVisualEffect(72)] // Remove Buff
    public sealed class RemoveBuffSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            // AttackStage releases at animation frame 3.5 (ZzzCharacter.cpp:2763).
            return new RemoveBuffEffect(context.Caster);
        }
    }
}
