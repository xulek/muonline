#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Meteorite (AT_SKILL_METEO = 2): MODEL_FIRE sub 0 falling onto the target.
    /// </summary>
    [SkillVisualEffect(2)] // Meteorite
    public sealed class MeteoriteSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            return new FireBallEffect(context.Caster, target, meteor: true);
        }
    }
}
