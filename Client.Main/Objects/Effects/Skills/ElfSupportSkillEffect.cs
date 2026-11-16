#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Fairy Elf support casts (ZzzCharacter.cpp MoveCharacter, AttackTime >= limit):
    /// Heal → BITMAP_MAGIC+1 sub1 on the target, Greater Defense → sub2 on the target,
    /// Greater Damage → sub3 on the target, Summon monsters → sub3 on the caster.
    /// ReceiveMagic plays SOUND_SKILL_DEFENSE (sKnightDefense.wav) for all of them; the
    /// packet handler already plays it for Heal (26), so the effect plays it for the rest.
    /// The persistent buff aura (MODEL_SPEARSKILL sub4 for defense) is a buff-state visual
    /// and is not part of this cast effect.
    /// </summary>
    [SkillVisualEffect(26)] // Heal
    [SkillVisualEffect(27)] // Greater Defense
    [SkillVisualEffect(28)] // Greater Damage
    [SkillVisualEffect(30)] // Summon Goblin
    [SkillVisualEffect(31)] // Summon Stone Golem
    [SkillVisualEffect(32)] // Summon Assassin
    [SkillVisualEffect(33)] // Summon Elite Yeti
    [SkillVisualEffect(34)] // Summon Dark Knight
    [SkillVisualEffect(35)] // Summon Bali
    [SkillVisualEffect(36)] // Summon Soldier
    public sealed class ElfSupportSkillEffect : ISkillVisualEffect
    {
        private const string DefenseSound = "Sound/sKnightDefense.wav";

        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            switch (context.SkillId)
            {
                case 26:
                    return new HealingCircleEffect(context.Caster, SupportSkillTargets.ResolveTargetOrCaster(context), 1, sound: null);
                case 27:
                    return new HealingCircleEffect(context.Caster, SupportSkillTargets.ResolveTargetOrCaster(context), 2, DefenseSound);
                case 28:
                    return new HealingCircleEffect(context.Caster, SupportSkillTargets.ResolveTargetOrCaster(context), 3, DefenseSound);
                default:
                    return new HealingCircleEffect(context.Caster, context.Caster, 3, DefenseSound);
            }
        }
    }
}
