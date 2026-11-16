#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Recovery (234): ReceiveMagic AT_SKILL_RECOVER visuals on the target.</summary>
    [SkillVisualEffect(234)] // Recovery
    public sealed class RecoverySkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            // BITMAP_IMPACT is created only when the caster is not the local hero (sc != Hero).
            bool remote = !(MuGame.Instance?.ActiveScene is Client.Main.Scenes.GameScene scene && ReferenceEquals(scene.Hero, context.Caster));
            return new RecoveryEffect(context.Caster, SupportSkillTargets.ResolveTargetOrCaster(context), remote);
        }
    }
}
