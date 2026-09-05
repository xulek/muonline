#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Exact ports of the SourceMain5.2 angle helpers used by the joint system
    /// (ZzzAI.cpp CreateAngle / TurnAngle2 / MoveHumming, Math/ZzzMathLib.cpp AngleMatrix).
    /// All angles are degrees, matching the C++ conventions.
    /// </summary>
    public static class SourceJointMath
    {
        public static float FrameFactor => FPSCounter.Instance.FPS_ANIMATION_FACTOR;

        public static float CreateAngle(float x1, float y1, float x2, float y2)
        {
            float nx2 = x2 - x1, ny2 = y2 - y1;
            float r;
            if (MathF.Abs(nx2) < 0.0001f)
            {
                if (ny2 < 0f) return 0f;
                return 180f;
            }
            if (MathF.Abs(ny2) < 0.0001f)
            {
                if (nx2 < 0f) return 270f;
                return 90f;
            }

            float angle = MathF.Atan(ny2 / nx2) / MathHelper.Pi * 180f + 90f;
            if (nx2 < 0f && ny2 >= 0f) r = angle + 180f;
            else if (nx2 < 0f && ny2 < 0f) r = angle + 180f;
            else r = angle;
            return r;
        }

        public static float CreateAngle2D(Vector3 from, Vector3 to)
            => CreateAngle(from.X, from.Y, to.X, to.Y);

        public static float TurnAngle2(float angle, float a, float d)
        {
            if (angle < 0f) angle += 360f;
            if (a < 0f) a += 360f;
            float aa;
            if (angle < 180f)
            {
                aa = angle - d;
                if (a >= angle + d && a < angle + 180f) angle += d;
                else if (aa >= 0f && (a >= angle + 180f || a < aa)) angle -= d;
                else if (aa < 0f && a >= angle + 180f && a < aa + 360f) angle = angle - d + 360f;
                else angle = a;
            }
            else
            {
                aa = angle + d;
                if (a < angle - d && a >= angle - 180f) angle -= d;
                else if (aa < 360f && (a < angle - 180f || a >= aa)) angle += d;
                else if (aa >= 360f && a < angle - 180f && a >= aa - 360f) angle = angle + d - 360f;
                else angle = a;
            }
            return angle;
        }

        /// <summary>Port of MoveHumming: steers Angle.Z (yaw) and Angle.X (pitch) toward
        /// the target and returns the remaining XY distance.</summary>
        public static float MoveHumming(ref Vector3 position, ref Vector3 angle, Vector3 targetPosition, float turn, float frameFactor)
        {
            float scaledTurn = turn * frameFactor;
            float targetYaw = CreateAngle2D(position, targetPosition);
            angle.Z = TurnAngle2(angle.Z, targetYaw, scaledTurn);

            Vector3 range = position - targetPosition;
            float distance = MathF.Sqrt(range.X * range.X + range.Y * range.Y);
            float targetPitch = 360f - CreateAngle(position.Z, distance, targetPosition.Z, 0f);
            angle.X = TurnAngle2(angle.X, targetPitch, scaledTurn);
            return distance;
        }

        /// <summary>Basis vectors of the C++ AngleMatrix ((Z*Y)*X, degrees).</summary>
        public static void AngleBasis(Vector3 angleDeg, out Vector3 right, out Vector3 forward, out Vector3 up)
        {
            float sy = MathF.Sin(angleDeg.Z * MathHelper.Pi / 180f);
            float cy = MathF.Cos(angleDeg.Z * MathHelper.Pi / 180f);
            float sp = MathF.Sin(angleDeg.Y * MathHelper.Pi / 180f);
            float cp = MathF.Cos(angleDeg.Y * MathHelper.Pi / 180f);
            float sr = MathF.Sin(angleDeg.X * MathHelper.Pi / 180f);
            float cr = MathF.Cos(angleDeg.X * MathHelper.Pi / 180f);

            // Columns of the C++ matrix (VectorRotate contracts against rows m[j][i]).
            right = new Vector3(cp * cy, sr * sp * cy + cr * -sy, cr * sp * cy + -sr * -sy);
            forward = new Vector3(cp * sy, sr * sp * sy + cr * cy, cr * sp * sy + -sr * cy);
            up = new Vector3(-sp, sr * cp, cr * cp);
        }

        /// <summary>Rotates a local vector by the C++ AngleMatrix.</summary>
        public static Vector3 Rotate(Vector3 local, Vector3 angleDeg)
        {
            AngleBasis(angleDeg, out var right, out var forward, out var up);
            return local.X * right + local.Y * forward + local.Z * up;
        }
    }
}
