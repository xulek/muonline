#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Summoner's Decay (skill 38 / AT_SKILL_BLAST_POISON). The original
    /// drops two hidden MODEL_FIRE objects onto the skill's target tile, so the effect is
    /// spawned at the area target position and falls from the sky.
    /// </summary>
    [SkillVisualEffect(38)]
    public sealed class DecaySkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.World == null)
                return null;

            Vector3 target = context.TargetPosition
                ?? context.Caster?.WorldPosition.Translation
                ?? Vector3.Zero;

            return new DecayEffect(context.Caster, target);
        }
    }
}
