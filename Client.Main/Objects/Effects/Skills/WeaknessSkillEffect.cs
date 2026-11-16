#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Weakness (AT_SKILL_ALICE_WEAKNESS) (skill 221). Red curse circles around the caster.
    /// </summary>
    [SkillVisualEffect(221)]
    public sealed class WeaknessSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new SummonerCurseEffect(context.Caster, weakness: true);
        }
    }
}
