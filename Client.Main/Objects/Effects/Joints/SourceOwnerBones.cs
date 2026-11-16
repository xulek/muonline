#nullable enable
using Client.Data.BMD;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Owner bone lookups of the SourceMain5.2 buff emitters:
    /// <c>pModel-&gt;TransformPosition(Owner-&gt;BoneTransform[bone], relative, p, false); VectorScale(p, pModel-&gt;BodyScale, p);
    /// VectorAdd(p, Owner-&gt;Position, p)</c> - TransformPosition already applies BodyScale, so the offset from the owner origin
    /// is scaled twice.
    /// </summary>
    internal static class SourceOwnerBones
    {
        public static int Count(WalkerObject owner) => owner.GetBoneTransforms()?.Length ?? 0;

        /// <summary>pModel-&gt;Bones[bone].Dummy.</summary>
        public static bool IsDummy(WalkerObject owner, int bone)
        {
            BMDTextureBone[]? bones = owner.Model?.Bones;
            return bones == null || bone < 0 || bone >= bones.Length || ReferenceEquals(bones[bone], BMDTextureBone.Dummy);
        }

        public static bool TryGet(WalkerObject owner, int bone, Vector3 relative, out Vector3 position)
        {
            Matrix[]? bones = owner.GetBoneTransforms();
            Matrix world = owner.WorldPosition;
            Vector3 origin = world.Translation;
            if (bones == null || bone < 0 || bone >= bones.Length)
            {
                position = origin;
                return false;
            }

            Vector3 once = Vector3.Transform(Vector3.Transform(relative, bones[bone]), world);
            position = origin + (once - origin) * owner.TotalScale;
            return true;
        }
    }
}
