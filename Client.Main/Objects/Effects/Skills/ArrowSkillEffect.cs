#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;

namespace Client.Main.Objects.Effects.Skills
{
    [SkillVisualEffect(24)]  // Triple Shot
    [SkillVisualEffect(25)]  // Basic bow skill
    [SkillVisualEffect(46)]  // Deep Impact
    [SkillVisualEffect(51)]  // Ice Arrow
    [SkillVisualEffect(52)]  // Penetration
    [SkillVisualEffect(235)] // Multi-Shot
    public sealed class ArrowSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster is not PlayerObject shooter || context.World == null)
                return null;

            // AT_SKILL_PIERCING: AttackStage CheckAttackTime(3) gathering + SOUND_PIERCING (ZzzCharacter.cpp:2573).
            if (context.SkillId == 52)
            {
                var gather = new PenetrationGatherEffect(shooter);
                context.World.Objects.Add(gather);
                _ = gather.Load();
            }

            return new ArrowProjectileEffect(
                shooter,
                context.World,
                context.TargetId,
                context.TargetPosition,
                ArrowProjectileSpawner.GetVolleyKind(context.SkillId));
        }
    }
}
