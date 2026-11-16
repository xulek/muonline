#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Scenes;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Teleport (skill 6). The arrival flash (CreateTeleportEnd) must be spawned by the teleport
    /// completion code via <see cref="TeleportSparkEffect.Spawn"/>.
    /// </summary>
    [SkillVisualEffect(6)] // Teleport (AT_SKILL_TELEPORT) - CreateTeleportBegin at the caster
    public sealed class TeleportSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            // GameSceneSkillController already spawned the hero's start flash when sending the request.
            if (MuGame.Instance?.ActiveScene is GameScene scene && scene.Hero == context.Caster)
                return null;

            // CreateTeleportBegin(so): SOUND_MAGIC comes from the packet sound map.
            return new TeleportSparkEffect(context.Caster, context.Caster.WorldPosition.Translation, playSound: false);
        }
    }
}
