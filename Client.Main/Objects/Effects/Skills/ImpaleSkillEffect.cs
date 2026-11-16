#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Impale (skill 47): weapon charge, spear models and riding-spear thrusts.
    /// </summary>
    [SkillVisualEffect(47)] // Impale (AT_SKILL_SPEAR)
    public sealed class ImpaleSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new ImpaleEffect(context.Caster);
        }
    }
}
