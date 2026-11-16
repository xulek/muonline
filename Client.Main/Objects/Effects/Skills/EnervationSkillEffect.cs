#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Enervation / Innovation (AT_SKILL_ALICE_ENERVATION) (skill 222). Green curse circles around the caster.
    /// </summary>
    [SkillVisualEffect(222)]
    public sealed class EnervationSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SummonerCurseEffect(context.Caster, weakness: false);
        }
    }
}
