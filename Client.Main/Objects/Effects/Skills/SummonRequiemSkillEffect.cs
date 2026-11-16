#nullable enable
using System;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.Summoner;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Requiem (AT_SKILL_SUMMON_REQUIEM) (224): CastSummonSkill uses the target tile corner (iTargetPos * TERRAIN_SCALE), not its centre.
    /// </summary>
    [SkillVisualEffect(224)]
    public sealed class SummonRequiemSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            Vector3 point = context.TargetPosition ?? context.Caster.WorldPosition.Translation;
            var corner = new Vector3(MathF.Floor(point.X / Constants.TERRAIN_SCALE) * Constants.TERRAIN_SCALE,
                MathF.Floor(point.Y / Constants.TERRAIN_SCALE) * Constants.TERRAIN_SCALE, point.Z);
            return new NeilEffect(context.Caster, corner);
        }
    }
}
