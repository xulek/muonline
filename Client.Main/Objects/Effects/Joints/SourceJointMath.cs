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

            // Columns of the C++ matrix: VectorRotate computes out[i] = dot(in, m[i]),
            // so a local axis maps to a column (yaw 90 sends local -Y to +X).
            right = new Vector3(cp * cy, cp * sy, -sp);
            forward = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
            up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        }

        /// <summary>CreateJoint epilogue: MaxTails / FPS_ANIMATION_FACTOR, capped at MAX_TAILS (200).</summary>
        public static int ScaledMaxTails(int maxTails)
        {
            float factor = MathF.Max(FrameFactor, 0.001f);
            return Math.Min(200, (int)(maxTails / factor));
        }

        /// <summary>Port of GetMagicScrew (ZzzEffect.cpp): unit vector wobbling with WorldTime.</summary>
        public static Vector3 MagicScrew(int param, float speedRate = 1f)
        {
            param += (int)(FPSCounter.Instance.WorldTime / 40.0);

            float rate = MathF.Pow(speedRate, FrameFactor);
            float speed0 = 0.048f * rate;
            float speed1 = 0.0613f * rate;
            float speed2 = 0.1113f * rate;
            float s0 = MathF.Sin((param + 55555) * speed0);
            float dirX = s0 * MathF.Cos(param * speed1);
            float dirY = s0 * MathF.Sin(param * speed1);
            float dirZ = MathF.Cos((param + 55555) * speed0);
            float sinAdd = MathF.Sin((param + 11111) * speed2);
            float cosAdd = MathF.Cos((param + 11111) * speed2);
            return new Vector3(cosAdd * dirY - sinAdd * dirZ, sinAdd * dirY + cosAdd * dirZ, dirX);
        }

        /// <summary>Rotates a local vector by the C++ AngleMatrix.</summary>
        public static Vector3 Rotate(Vector3 local, Vector3 angleDeg)
        {
            AngleBasis(angleDeg, out var right, out var forward, out var up);
            return local.X * right + local.Y * forward + local.Z * up;
        }
    }
}
