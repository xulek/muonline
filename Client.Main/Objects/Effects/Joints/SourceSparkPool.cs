#nullable enable
using System;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Fixed pools for the two spark children many melee skills spawn together:
    /// BITMAP_JOINT_SPARK sub 0 / 1 (ZzzEffectJoint.cpp:902 init, prologue flight, 4153 move, Spark01.jpg) and
    /// BITMAP_SPARK sub 0 / 2 / 12 (ZzzEffectParticle.cpp:2027 init, 6589 move, 9239 render, Spark02.jpg).
    /// </summary>
    internal sealed class SourceSparkPool
    {
        public const string JointTexturePath = "Effect/Spark01.jpg";
        public const string ParticleTexturePath = "Effect/Spark02.jpg";
        public const string Spark1TexturePath = "Effect/Spark03.jpg"; // BITMAP_SPARK + 1 (sub 13)

        private struct Joint
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public int SubType;
            public float Light;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Spark
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public float Light;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Joint[] _joints;
        private readonly Spark[] _sparks;

        public SourceSparkPool(int maxJoints, int maxSparks)
        {
            _joints = new Joint[maxJoints];
            _sparks = new Spark[maxSparks];
        }

        public bool HasLive
        {
            get
            {
                for (int i = 0; i < _joints.Length; i++)
                    if (_joints[i].Live) return true;
                for (int i = 0; i < _sparks.Length; i++)
                    if (_sparks[i].Live) return true;
                return false;
            }
        }

        /// <summary>CreateJoint(BITMAP_JOINT_SPARK, Position, Position, Angle, subType) - sub 0, 1 or 3 (same init as 0), Scale 2.</summary>
        public void SpawnJoint(Vector3 position, Vector3 angleDeg, int subType = 0)
        {
            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint j = ref _joints[i];
                if (j.Live)
                    continue;

                SourceJointMath.AngleBasis(angleDeg, out j.Right, out j.Forward, out j.Up);
                j.Live = true;
                j.Position = position;
                j.SubType = subType;
                j.Light = 1f;
                if (subType == 1)
                {
                    j.Velocity = MuGame.Random.Next(20) + 16;
                    j.LifeTime = MuGame.Random.Next(4) + 4;
                }
                else
                {
                    j.Velocity = MuGame.Random.Next(20) + 6;
                    j.LifeTime = MuGame.Random.Next(8) + 8;
                }
                int maxTails = SourceJointMath.ScaledMaxTails(2);
                if (j.Trail == null || j.Trail.MaxTails != maxTails)
                    j.Trail = new SourceTrail(maxTails);
                else
                    j.Trail.Clear();
                j.Trail.Push(position, j.Right, j.Up, 2f);
                return;
            }
        }

        /// <summary>
        /// CreateParticle(BITMAP_SPARK, Position, Angle, Light, subType, scale) - sub 0, 2 or 12 (sub 12 adds the Scale
        /// argument) with white light.
        /// </summary>
        public void SpawnSpark(Vector3 position, Vector3 angleDeg, int subType = 0, float scale = 0f)
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark p = ref _sparks[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.Light = 1f;
                p.Scale = (MuGame.Random.Next(4) + 4) * 0.1f;
                p.LifeTime = MuGame.Random.Next(16) + 24;
                p.Angle = new Vector3(angleDeg.X, angleDeg.Y, MuGame.Random.Next(360));
                p.Gravity = MuGame.Random.Next(16) + 6;
                var local = new Vector3(0f, (MuGame.Random.Next(20) + 20) * 0.1f, 0f);
                if (subType == 2)
                {
                    p.Scale *= 2f;
                    local *= 3f;
                }
                else if (subType == 12)
                {
                    p.Scale += scale;
                }
                else if (subType == 13)
                {
                    // TexType BITMAP_SPARK + 1: draw this pool with Spark03.jpg
                    p.Scale *= 1.6f;
                    local *= 1.5f;
                }
                p.Velocity = SourceJointMath.Rotate(local, p.Angle);
                return;
            }
        }

        public void Move(float factor, TerrainControl? terrain)
        {
            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint j = ref _joints[i];
                if (!j.Live)
                    continue;

                j.Position += -j.Forward * (j.Velocity * factor);
                if (j.SubType == 1)
                {
                    j.Velocity += factor * 0.3f;
                    j.Light *= MathF.Pow(1f / 1.4f, factor);
                }
                else if (j.SubType == 3)
                {
                    // MoveJoint ZzzEffectJoint.cpp:4161: Velocity += 0.1 F, Light /= 1.1^F
                    j.Velocity += factor * 0.1f;
                    j.Light *= MathF.Pow(1f / 1.1f, factor);
                }
                j.Trail.Push(j.Position, j.Right, j.Up, 2f);

                j.LifeTime -= factor;
                if (j.LifeTime < 0f)
                    j.Live = false;
            }

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark p = ref _sparks[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                // MovePosition rotates the (already rotated) velocity again; the case then adds it once more.
                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;

                p.Light = p.LifeTime / 16f;
                p.Position.Z += p.Gravity * factor;
                p.Gravity -= 2f * factor;

                float height = terrain?.RequestTerrainHeight(p.Position.X, p.Position.Y) ?? p.Position.Z;
                if (p.Position.Z < height)
                {
                    p.Position.Z = height;
                    p.Gravity = -p.Gravity * 0.6f;
                    p.LifeTime -= 4f * factor;
                }

                p.Position += p.Velocity * factor;
            }
        }

        public void DrawJoints(SourceQuadBatch batch, Texture2D? texture)
        {
            if (!batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint j = ref _joints[i];
                if (j.Live)
                    batch.AddTrail(j.Trail, SourceQuadBatch.TrailFaces.Both, new Vector3(j.Light),
                        SourceQuadBatch.TrailShade.Flat, j.Trail.MaxTails - 1);
            }
            batch.End();
        }

        public void DrawSparks(SourceQuadBatch batch, Texture2D? texture)
        {
            if (!batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark p = ref _sparks[i];
                if (p.Live)
                    batch.AddSprite(p.Position, p.Scale, new Vector3(p.Light));
            }
            batch.End();
        }
    }
}
