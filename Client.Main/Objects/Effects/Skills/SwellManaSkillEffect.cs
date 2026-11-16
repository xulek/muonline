#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Swell Mana (69): mana rune at ReceiveMagic, spirit ring at the AttackTime limit.</summary>
    [SkillVisualEffect(69)] // Swell Mana
    public sealed class SwellManaSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            return new SwellManaEffect(context.Caster);
        }
    }
}
