#nullable enable
using System;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Pollution (AT_SKILL_SUMMON_POLLUTION) (225): CastSummonSkill uses the target tile corner (iTargetPos * TERRAIN_SCALE), not its centre.
    /// </summary>
    [SkillVisualEffect(225)]
    public sealed class SummonPollutionSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            Vector3 point = context.TargetPosition ?? context.Caster.WorldPosition.Translation;
            var corner = new Vector3(MathF.Floor(point.X / Constants.TERRAIN_SCALE) * Constants.TERRAIN_SCALE,
                MathF.Floor(point.Y / Constants.TERRAIN_SCALE) * Constants.TERRAIN_SCALE, point.Z);
            return new LagulEffect(context.Caster, corner);
        }
    }
}
