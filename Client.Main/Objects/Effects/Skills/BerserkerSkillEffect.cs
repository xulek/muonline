#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Berserker (AT_SKILL_ALICE_BERSERKER), skill 218: caster ground circle plus the
    /// elshildring buff rings on the target (see <see cref="AliceBuffEffect"/>).
    /// </summary>
    [SkillVisualEffect(218)]
    public sealed class BerserkerSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new AliceBuffEffect(context.Caster, SummonerSkillContext.ResolveTarget(context), AliceBuffEffect.Kind.Berserker);
        }
    }
}
