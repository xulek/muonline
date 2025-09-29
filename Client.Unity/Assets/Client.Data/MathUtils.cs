using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Client.Data
{
    public static class MathUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion AngleQuaternion(Vector3 eulerAngles)
        {
            float halfX = eulerAngles.x * 0.5f;
            float halfY = eulerAngles.y * 0.5f;
            float halfZ = eulerAngles.z * 0.5f;

            float sinX = MathF.Sin(halfX);
            float cosX = MathF.Cos(halfX);
            float sinY = MathF.Sin(halfY);
            float cosY = MathF.Cos(halfY);
            float sinZ = MathF.Sin(halfZ);
            float cosZ = MathF.Cos(halfZ);

            float w = cosX * cosY * cosZ + sinX * sinY * sinZ;
            float x = sinX * cosY * cosZ - cosX * sinY * sinZ;
            float y = cosX * sinY * cosZ + sinX * cosY * sinZ;
            float z = cosX * cosY * sinZ - sinX * sinY * cosZ;

            Quaternion quaternion = new Quaternion(x, y, z, w);
            quaternion = Quaternion.Normalize(quaternion);

            return quaternion;
        }
    }
}
