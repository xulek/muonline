#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Thorns / Damage Reflection (AT_SKILL_ALICE_THORNS), skill 217: caster ground circle plus the
    /// elshildring buff rings on the target (see <see cref="AliceBuffEffect"/>).
    /// </summary>
    [SkillVisualEffect(217)]
    public sealed class ThornsSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new AliceBuffEffect(context.Caster, SummonerSkillContext.ResolveTarget(context), AliceBuffEffect.Kind.Thorns);
        }
    }
}
