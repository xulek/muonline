#nullable enable
using Client.Main.Objects.Effects.Skills;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>Small helpers shared by the Summoner skill factories.</summary>
    internal static class SummonerSkillContext
    {
        /// <summary>Resolves the skill target (TargetId masked like the original FindCharacterIndex).</summary>
        public static WalkerObject? ResolveTarget(in SkillEffectContext context)
        {
            ushort id = (ushort)(context.TargetId & 0x7FFF);
            if (id == 0 || context.World == null)
                return null;

            return context.World.TryGetWalkerById(id, out var target) ? target : null;
        }

        /// <summary>The area target point, falling back to the target object and then the caster.</summary>
        public static Vector3 ResolvePoint(in SkillEffectContext context, WalkerObject? target)
        {
            if (context.TargetPosition.HasValue)
                return context.TargetPosition.Value;
            if (target != null)
                return target.WorldPosition.Translation;
            return context.Caster.WorldPosition.Translation;
        }
    }
}
