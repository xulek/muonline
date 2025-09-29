using UnityEngine;
using System;

namespace Client.Main
{
    public static class MathUtils
    {
        public static Quaternion AngleQuaternion(Vector3 euler)
        {
            // Assuming euler is in degrees:
            return Quaternion.Euler(euler);
        }

        public static Matrix4x4 AngleMatrix(Vector3 angles)
        {
            // Convert angles from degrees to radians
            float pitch = angles.x * Mathf.Deg2Rad;
            float yaw = angles.y * Mathf.Deg2Rad;
            float roll = angles.z * Mathf.Deg2Rad;

            // Create quaternion from yaw(pitch), pitch(pitch), roll(roll)
            // Note: MonoGame uses YawPitchRoll(yaw, pitch, roll) order
            Quaternion rotation = Quaternion.Euler(angles.x, angles.y, angles.z);

            // Convert quaternion to matrix
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(rotation);

            // MonoGame code returns transposed matrix; Unity uses column-major matrices, so transpose if needed
            return rotationMatrix.transpose;
        }

        public static float DotProduct(Vector3 x, Vector3 y)
        {
            return Vector3.Dot(x, y);
        }

        public static Vector3 FaceNormalize(Vector3 v1, Vector3 v2, Vector3 v3)
        {
            // Cross product of edges
            float nx = (v2.y - v1.y) * (v3.z - v1.z) - (v3.y - v1.y) * (v2.z - v1.z);
            float ny = (v2.z - v1.z) * (v3.x - v1.x) - (v3.z - v1.z) * (v2.x - v1.x);
            float nz = (v2.x - v1.x) * (v3.y - v1.y) - (v3.x - v1.x) * (v2.y - v1.y);

            float lengthSquared = nx * nx + ny * ny + nz * nz;
            if (lengthSquared == 0) return Vector3.zero;

            float invLength = 1.0f / Mathf.Sqrt(lengthSquared);
            return new Vector3(nx * invLength, ny * invLength, nz * invLength);
        }

        public static Vector3 VectorRotate(Vector3 in1, Matrix4x4 in2)
        {
            // Multiply vector by rotation matrix (ignore translation)
            return new Vector3(
                in1.x * in2.m00 + in1.y * in2.m10 + in1.z * in2.m20,
                in1.x * in2.m01 + in1.y * in2.m11 + in1.z * in2.m21,
                in1.x * in2.m02 + in1.y * in2.m12 + in1.z * in2.m22
            );
        }

        public static Vector3 VectorIRotate(Vector3 in1, Matrix4x4 in2)
        {
            // Assuming inverse rotation (transpose of rotation matrix)
            return new Vector3(
                in1.x * in2.m00 + in1.y * in2.m10 + in1.z * in2.m20,
                in1.x * in2.m01 + in1.y * in2.m11 + in1.z * in2.m21,
                in1.x * in2.m02 + in1.y * in2.m12 + in1.z * in2.m22
            );
        }

        //public static Quaternion AngleQuaternion(Vector3 angles)
        //{
        //    // Convert degrees to radians and halve for quaternion formula
        //    float halfPitch = angles.x * Mathf.Deg2Rad * 0.5f;
        //    float halfYaw = angles.y * Mathf.Deg2Rad * 0.5f;
        //    float halfRoll = angles.z * Mathf.Deg2Rad * 0.5f;

        //    float sr = Mathf.Sin(halfPitch);
        //    float cr = Mathf.Cos(halfPitch);
        //    float sp = Mathf.Sin(halfYaw);
        //    float cp = Mathf.Cos(halfYaw);
        //    float sy = Mathf.Sin(halfRoll);
        //    float cy = Mathf.Cos(halfRoll);

        //    float x = sr * cp * cy - cr * sp * sy;
        //    float y = cr * sp * cy + sr * cp * sy;
        //    float z = cr * cp * sy - sr * sp * cy;
        //    float w = cr * cp * cy + sr * sp * sy;

        //    return new Quaternion(x, y, z, w);
        //}
    }
}
