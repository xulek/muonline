#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Chain Lightning (AT_SKILL_ALICE_CHAINLIGHTNING = 215) on the 0x1E cast: only the release sound. The bolts come
    /// from the chain packet (0xBF 0x0A, ReceiveChainMagic) as <see cref="ChainLightningEffect"/>.
    /// </summary>
    [SkillVisualEffect(215)]
    public sealed class ChainLightningSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new ChainLightningCastEffect(context.Caster);
        }
    }
}
