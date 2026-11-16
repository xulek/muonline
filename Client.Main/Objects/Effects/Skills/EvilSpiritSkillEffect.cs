#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Evil Spirit (AT_SKILL_EVIL = 9): eight BITMAP_JOINT_SPIRIT homing around the caster.
    /// </summary>
    [SkillVisualEffect(9)] // Evil Spirit
    public sealed class EvilSpiritSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new EvilSpiritEffect(context.Caster);
        }
    }
}
