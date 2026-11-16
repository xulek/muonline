#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Support
{
    /// <summary>
    /// Exact SourceMain5.2 AngleMatrix + VectorRotate (Math/ZzzMathLib.cpp).
    /// VectorRotate computes out[i] = dot(in, m[i]), i.e. out = M * in, so a local axis maps
    /// to a matrix <em>column</em>. With this convention yaw 0 flies toward -Y and yaw 90
    /// toward +X, matching CreateAngle / MoveHumming.
    /// </summary>
    public static class SourceMath
    {
        public static void Basis(Vector3 angleDeg, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ)
        {
            float yaw = angleDeg.Z * MathF.PI / 180f;
            float pitch = angleDeg.Y * MathF.PI / 180f;
            float roll = angleDeg.X * MathF.PI / 180f;
            float sy = MathF.Sin(yaw), cy = MathF.Cos(yaw);
            float sp = MathF.Sin(pitch), cp = MathF.Cos(pitch);
            float sr = MathF.Sin(roll), cr = MathF.Cos(roll);

            // Columns of matrix[3][4].
            axisX = new Vector3(cp * cy, cp * sy, -sp);
            axisY = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
            axisZ = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        }

        public static Vector3 Rotate(Vector3 local, Vector3 angleDeg)
        {
            Basis(angleDeg, out var x, out var y, out var z);
            return local.X * x + local.Y * y + local.Z * z;
        }

        /// <summary>Forward flight of a JOINT/effect: VectorRotate((0, -velocity, 0)).</summary>
        public static Vector3 Forward(float velocity, Vector3 angleDeg) =>
            Rotate(new Vector3(0f, -velocity, 0f), angleDeg);
    }
}
