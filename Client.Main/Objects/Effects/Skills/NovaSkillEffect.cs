#nullable enable
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Nova release/explosion visual (Skill ID 40).
    /// </summary>
    [Core.Utilities.SkillVisualEffect(40)]
    public sealed class NovaSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            byte stage = ScrollOfNovaChargeEffect.ConsumeStageAndStop(context.World, context.Caster.NetworkId);

            // The local hero already spawned its release explosion optimistically in
            // GameSceneSkillController.SpawnNovaExplosion; the server echo would
            // otherwise spawn a second (stage 0) explosion here.
            if (stage == 0 && context.Caster is PlayerObject player &&
                MuGame.Instance?.ActiveScene is GameScene scene &&
                scene.Hero == player)
                return null;

            Vector3 center = context.Caster.WorldPosition.Translation;
            return new ScrollOfNovaExplosionEffect(context.Caster, center, stage);
        }
    }
}
