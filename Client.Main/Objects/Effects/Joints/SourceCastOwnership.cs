#nullable enable
using System.Collections.Generic;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// The original draws cast-hand visuals from the character (MoveCharacterVisual), once per frame however many casts are
    /// queued. C# casts each own an effect object, so the newest one claims the caster and older ones stop emitting.
    /// </summary>
    internal static class SourceCastOwnership
    {
        private static readonly Dictionary<WalkerObject, object> Owners = new();

        public static void Claim(WalkerObject caster, object effect) => Owners[caster] = effect;

        public static bool IsOwner(WalkerObject caster, object effect) =>
            Owners.TryGetValue(caster, out object? owner) && ReferenceEquals(owner, effect);

        public static void Release(WalkerObject caster, object effect)
        {
            if (IsOwner(caster, effect))
                Owners.Remove(caster);
        }
    }
}
