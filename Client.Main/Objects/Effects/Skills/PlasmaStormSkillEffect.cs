#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Plasma Storm (76, Fenrir): homing lightning to the target and nearby monsters.</summary>
    [SkillVisualEffect(76)] // Plasma Storm
    public sealed class PlasmaStormSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;
            // ReceiveMagicContinue sets the Fenrir skill target to its caster; 0x19 carries a target key.
            WalkerObject? target = context.TargetId == 0 && context.TargetPosition.HasValue
                ? context.Caster : SupportSkillTargets.ResolveTarget(context);
            int fenrirType = context.Caster is PlayerObject player ? player.FenrirSkillVariant : 1;
            return new PlasmaStormEffect(context.Caster, target, fenrirType);
        }
    }
}
