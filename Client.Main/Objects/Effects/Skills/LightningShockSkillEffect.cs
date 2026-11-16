#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Lightning Shock (AT_SKILL_LIGHTNING_SHOCK) (skill 230). A red lightning orb rises over the caster and slams into the ground ahead.
    /// </summary>
    [SkillVisualEffect(230)]
    public sealed class LightningShockSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new LightningShockEffect(context.Caster);
        }
    }
}
