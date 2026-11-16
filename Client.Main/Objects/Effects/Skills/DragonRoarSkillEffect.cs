#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Dragon Roar" skill (ID 264, AT_SKILL_DRAGON_LOWER). See <see cref="DragonRoarEffect"/>.
    /// Works for any caster (hero or remote walker).
    /// </summary>
    [SkillVisualEffect(264)]
    public sealed class DragonRoarSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            // 0x1E AreaSkillAnimation (no target) is the cast, every 0x19 SkillAnimation with a
            // target is one hit (WSclient.cpp ReceiveMagicContinue / ReceiveMagic).
            ushort casterId = context.Caster.NetworkId;
            if (context.TargetId == 0)
            {
                DragonRoarHitEffect.ResetHitCounter(casterId);
                return new DragonRoarEffect(context.Caster, context.TargetPosition);
            }

            return new DragonRoarHitEffect(context.Caster, context.TargetId, DragonRoarHitEffect.NextVolcanoSubType(casterId));
        }
    }
}
