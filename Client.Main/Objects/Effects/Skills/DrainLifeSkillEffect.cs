#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Drain Life (AT_SKILL_ALICE_DRAINLIFE) (skill 214). Red ghosts fly from the caster to the target and life energy flows back.
    /// </summary>
    [SkillVisualEffect(214)]
    public sealed class DrainLifeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            var target = SummonerSkillContext.ResolveTarget(context);
            return target == null ? null : new DrainLifeEffect(context.Caster, target);
        }
    }
}
