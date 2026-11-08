using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System;

namespace Client.Main.Objects.Player
{
    /// <summary>
    /// Where an item rides on the back bone: weapons and shields holstered in the safe zone, and the arrows and bolts
    /// that are always carried there. Angles and offsets follow SourceMain5.2 RenderLinkObject (Link branch) as called
    /// by RenderCharacterBackItem; they are relative to bone 47, the same anchor the wings use.
    /// </summary>
    internal static class BackItemPlacement
    {
        private const int Bows = 4, Staffs = 5, Shields = 6;
        private const int Bolt = 7, Arrows = 15, ArrowViperBow = 20;

        /// <summary>Arrows and bolts are never held in a hand, safe zone or not.</summary>
        public static bool IsAmmo(int group, int index) => group == Bows && (index == Bolt || index == Arrows);

        /// <summary>
        /// Local position and rotation (radians, X/Y/Z as in <see cref="MathUtils.AngleQuaternion"/>) of an item relative
        /// to the back bone. <paramref name="rightHandItem"/> is the first equipment slot (SourceMain's Weapon[0]); the
        /// item of the other slot is mirrored to the other side, except shields and the Arrow Viper Bow.
        /// </summary>
        public static void Get(int group, int index, bool rightHandItem, out Vector3 position, out Vector3 angle)
        {
            Matrix placement = Placement(group, index, out bool mirrorable);
            if (!rightHandItem && mirrorable)
            {
                // The second item sits on the other side of the back: the first one's placement reflected across X.
                // (SourceMain's own composite for it, 145/0/275 + 0/10/-30, does not map onto this engine's axes.)
                Matrix flip = Matrix.CreateScale(-1f, 1f, 1f);
                placement = flip * placement * flip;
            }

            position = placement.Translation;
            angle = ToAngles(placement);
        }

        private static Matrix Placement(int group, int index, out bool mirrorable)
        {
            mirrorable = group != Shields && !(group == Bows && index == ArrowViperBow);

            if (group == Shields)
            {
                return index switch
                {
                    16 => Rigid(new Vector3(30f, 0f, 90f), new Vector3(-20f, 0f, -20f)),  // Elemental Shield
                    14 or 15 => Rigid(new Vector3(50f, 0f, 90f), new Vector3(-28f, 0f, -25f)), // Legendary, Grand Soul
                    6 => Rigid(new Vector3(30f, 0f, 90f), new Vector3(-15f, 0f, -25f)),   // Skull Shield
                    _ => Rigid(new Vector3(70f, 0f, 90f), new Vector3(-10f, 0f, 0f)),
                };
            }

            if (group == Bows)
            {
                bool crossbow = (index >= 8 && index < Arrows) || index == 16 || (index >= 18 && index < ArrowViperBow);
                if (crossbow)
                    return Rigid(new Vector3(0f, 20f, 180f), new Vector3(-10f, 8f, 40f));

                return index switch
                {
                    ArrowViperBow => Rigid(new Vector3(-60f, 0f, -80f), new Vector3(-5f, 20f, 0f)),
                    23 => Rigid(new Vector3(-60f, 0f, -80f), new Vector3(-5f, 20f, -5f)), // Stinger Bow
                    24 => Rigid(new Vector3(90f, 0f, -80f), new Vector3(10f, 20f, -5f)),  // Air Lyn Bow
                    _ => Rigid(new Vector3(70f, 0f, 90f), new Vector3(-10f, 5f, 10f)),    // bows, bolts, arrows
                };
            }

            if (group == Staffs && index == 9)  // Dragon Soul Staff
                return Rigid(new Vector3(110f, 180f, 90f), new Vector3(-10f, 5f, -10f));
            if (group == Staffs && index == 8)  // Staff of Destruction
                return Rigid(new Vector3(70f, 0f, 90f), new Vector3(-10f, 5f, 10f));

            return Rigid(new Vector3(70f, 0f, 90f), new Vector3(-20f, 5f, 40f));
        }

        /// <summary>Rotation by MU Euler angles in degrees, then translation: p' = R p + t.</summary>
        private static Matrix Rigid(Vector3 degrees, Vector3 translation) =>
            Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(new Vector3(
                MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.Z)))) *
            Matrix.CreateTranslation(translation);

        /// <summary>Inverse of <see cref="MathUtils.AngleQuaternion"/> for the rotation part of a matrix.</summary>
        private static Vector3 ToAngles(Matrix m)
        {
            // Column-convention rotation R[i][j] = m(j, i): pitch = asin(-R20), yaw = atan2(R10, R00), roll = atan2(R21, R22)
            float sinPitch = -m.M13;
            float pitch = MathF.Asin(Math.Clamp(sinPitch, -1f, 1f));
            if (MathF.Abs(sinPitch) < 0.99999f)
                return new Vector3(MathF.Atan2(m.M23, m.M33), pitch, MathF.Atan2(m.M12, m.M11));

            return new Vector3(0f, pitch, MathF.Atan2(-m.M21, m.M22)); // looking straight up or down: roll and yaw are one angle
        }

        /// <summary>Diagnostics (StressHarness): largest error of position and angles turned back into a matrix.</summary>
        internal static float SelfCheck()
        {
            float worst = 0f;
            (int Group, int Index)[] items =
            {
                (0, 0), (0, 5), (2, 8), (4, 0), (4, 7), (4, 8), (4, 15), (4, 16), (4, 20), (4, 23), (4, 24),
                (5, 0), (5, 8), (5, 9), (6, 0), (6, 6), (6, 14), (6, 16),
            };
            foreach (var (group, index) in items)
            {
                foreach (bool rightHand in new[] { true, false })
                {
                    Matrix expected = Placement(group, index, out bool mirrorable);
                    if (!rightHand && mirrorable)
                        expected = Matrix.CreateScale(-1f, 1f, 1f) * expected * Matrix.CreateScale(-1f, 1f, 1f);

                    Get(group, index, rightHand, out Vector3 position, out Vector3 angle);
                    Matrix rebuilt = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(angle)) * Matrix.CreateTranslation(position);
                    worst = MathF.Max(worst, MaxDifference(expected, rebuilt));
                }
            }
            return worst;
        }

        private static float MaxDifference(Matrix a, Matrix b)
        {
            float worst = 0f;
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    worst = MathF.Max(worst, MathF.Abs(a[row, col] - b[row, col]));
            return worst;
        }
    }
}
