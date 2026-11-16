#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>SourceMain5.2 vector helpers (degrees, AngleMatrix/VectorRotate conventions).</summary>
    public static class SkillFxMath
    {
        /// <summary>
        /// Exact VectorRotate(local, AngleMatrix(angleDeg)) of ZzzMathLib: angle X = roll, Y = pitch,
        /// Z = yaw, out[i] = row[i] . local. Forward (0,-1,0) at yaw a becomes (sin a, -cos a, 0).
        /// </summary>
        public static Vector3 Rotate(Vector3 local, Vector3 angleDeg)
        {
            float sy = MathF.Sin(MathHelper.ToRadians(angleDeg.Z));
            float cy = MathF.Cos(MathHelper.ToRadians(angleDeg.Z));
            float sp = MathF.Sin(MathHelper.ToRadians(angleDeg.Y));
            float cp = MathF.Cos(MathHelper.ToRadians(angleDeg.Y));
            float sr = MathF.Sin(MathHelper.ToRadians(angleDeg.X));
            float cr = MathF.Cos(MathHelper.ToRadians(angleDeg.X));

            return new Vector3(
                local.X * (cp * cy) + local.Y * (sr * sp * cy - cr * sy) + local.Z * (cr * sp * cy + sr * sy),
                local.X * (cp * sy) + local.Y * (sr * sp * sy + cr * cy) + local.Z * (cr * sp * sy - sr * cy),
                local.X * -sp + local.Y * (sr * cp) + local.Z * (cr * cp));
        }

        /// <summary>Rotates around Z only by a yaw in degrees.</summary>
        public static Vector3 RotateYaw(Vector3 local, float yawDeg) => Rotate(local, new Vector3(0f, 0f, yawDeg));

        /// <summary>o->Angle[2] of a walker (degrees).</summary>
        public static float YawDeg(WorldObject owner) => MathHelper.ToDegrees(owner.Angle.Z);

        /// <summary>rand() % range.</summary>
        public static int Rand(int range) => range <= 0 ? 0 : MuGame.Random.Next(range);

        public static float RandRange(float min, float max) => min + (float)MuGame.Random.NextDouble() * (max - min);
    }
}
