#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Dark Lord Chaotic Diseier visual (AT_SKILL_GAOTIC 238). CreateBomb is only spawned for the
    /// hero's own cast on the selected target (WSclient.cpp: sc == Hero &amp;&amp; SelectedCharacter != -1).
    /// </summary>
    [SkillVisualEffect(238)]
    public sealed class ChaoticDiseierSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? bombTarget = null;
            if (context.Caster is PlayerObject { IsMainWalker: true } && context.TargetId != 0 &&
                context.World.TryGetWalkerById(context.TargetId, out WalkerObject target))
                bombTarget = target;

            return new ChaoticDiseierEffect(context.Caster, bombTarget);
        }
    }
}
