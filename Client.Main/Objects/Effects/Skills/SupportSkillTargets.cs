#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>Shared caster/target resolution for buff and utility skill factories.</summary>
    internal static class SupportSkillTargets
    {
        /// <summary>
        /// 25 Hz frames between ReceiveMagic (AttackTime = 1) and the AttackTime >=
        /// g_iLimitAttackTime (15) switch in MoveCharacter that creates most cast visuals.
        /// </summary>
        public const float AttackStageDelayFrames = 14f;

        /// <summary>Resolves the skill target walker, or null when not present.</summary>
        public static WalkerObject? ResolveTarget(in SkillEffectContext context)
        {
            ushort id = (ushort)(context.TargetId & 0x7FFF);
            if (id == 0 || context.World == null)
                return null;

            if (context.Caster != null && (context.Caster.NetworkId & 0x7FFF) == id)
                return context.Caster;

            return context.World.TryGetWalkerById(id, out var walker) ? walker : null;
        }

        /// <summary>The target when known, otherwise the caster (self buffs).</summary>
        public static WalkerObject ResolveTargetOrCaster(in SkillEffectContext context)
            => ResolveTarget(context) ?? context.Caster;

        public static bool IsUsable(in SkillEffectContext context) =>
            context.Caster != null && context.World != null;

        public static Vector3 Feet(WalkerObject walker) => walker.WorldPosition.Translation;
    }
}
