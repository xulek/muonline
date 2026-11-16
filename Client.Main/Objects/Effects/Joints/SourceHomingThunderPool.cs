#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// BITMAP_JOINT_THUNDER sub 0 / 28 homing on a target object, with the arrival children of Scale 50 joints.
    /// Shared by every caster (the original Joints / Particles arrays are global): slots carry their owner.
    /// init    ZzzEffectJoint.cpp:1053 (Scale, MaxTails 50, Velocity 50; sub 0 LifeTime 2), 1314 (sub 28: LifeTime 2, vPriorColor)
    /// move    ZzzEffectJoint.cpp:2945 (prologue flight), 4688 (sub 0/28: TargetPosition = Target + 80 Z, MoveHumming 50;
    ///         Direction sub 28 rand%256-128, else (rand%1024-512)/Scale; CreateTail; Distance &lt; Velocity*1.5 with Scale 50:
    ///         BITMAP_ENERGY FpsChecked, rand_fps_check(8) BITMAP_SMOKE, sub 0 WorldTime%1000&lt;500 &amp;&amp; rand_fps_check(16):
    ///         sub 28 joint at TargetPosition + (rand%100-50, rand%100-50, rand%120-60), Scale 6 + rand%8)
    /// render  ZzzEffectJoint.cpp:7042/7265 (thunder U x 2^F - Scroll, face TWO + 2 Scroll, GL_REPEAT)
    /// energy  ZzzEffectParticle.cpp:734 / 4970 (Scale (rand%8+6)*0.1, Rotation rand%360, Gravity 20, LifeTime 2; Rotation += Gravity*F)
    /// smoke   ZzzEffectParticle.cpp:1158 / 5304 (sub 0: LifeTime 16, Scale (rand%32+48)*0.01, Rotation WorldTime%360;
    ///         Light = LifeTime/8, Gravity += 0.2F, Z += Gravity*F, Scale += 0.05F)
    /// Not ported: AddTerrainLight per tail step (sub 0 Scale &gt;= 50).
    /// </summary>
    internal sealed class SourceHomingThunderPool
    {
        public const string ThunderTexturePath = "Effect/JointThunder01.jpg"; // BITMAP_JOINT_THUNDER (GL_REPEAT)
        public const string EnergyTexturePath = "Effect/Thunder01.jpg";      // BITMAP_ENERGY
        public const string SmokeTexturePath = "Effect/smoke01.jpg";         // BITMAP_SMOKE

        private const float Velocity = 50f;
        private const int MaxThunders = 256;
        private const int MaxParticles = 512;

        private struct Thunder
        {
            public object? Owner;
            public int SubType;
            public WalkerObject? Target;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 TargetPosition;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public SourceTrail? Trail;
        }

        private struct Particle
        {
            public object? Owner;
            public bool IsSmoke;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        public static readonly SourceHomingThunderPool Shared = new();

        private readonly Thunder[] _thunders = new Thunder[MaxThunders];
        private readonly Particle[] _particles = new Particle[MaxParticles];

        private SourceHomingThunderPool()
        {
        }

        /// <summary>CreateJoint(BITMAP_JOINT_THUNDER, from, ..., angle, subType (0 / 28), target, scale, ..., light).</summary>
        public void SpawnThunder(object owner, int subType, Vector3 from, Vector3 angleDeg, Vector3 light, float scale,
            WalkerObject? target, Vector3 targetPosition)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Owner != null)
                    continue;

                // MAX_TAILS capacity once per slot; Reset picks MaxTails / F for this joint.
                t.Trail ??= new SourceTrail(200);
                t.Trail.Reset(SourceJointMath.ScaledMaxTails(50));

                t.Owner = owner;
                t.SubType = subType;
                t.Target = target;
                t.Position = from;
                t.Angle = angleDeg;
                t.TargetPosition = targetPosition;
                t.Light = light;
                t.Scale = scale;
                t.LifeTime = 2f;
                t.Trail.Push(from, angleDeg, scale);
                return;
            }
        }

        /// <summary>CreateParticleFpsChecked(BITMAP_ENERGY, position, angle, light) without the FPS check.</summary>
        public void SpawnEnergy(object owner, Vector3 position, Vector3 light)
        {
            int i = FindFreeParticle();
            if (i < 0)
                return;

            ref Particle p = ref _particles[i];
            p.Owner = owner;
            p.IsSmoke = false;
            p.Position = position;
            p.Light = light;
            p.Scale = (MuGame.Random.Next(8) + 6) * 0.1f;
            p.Rotation = MuGame.Random.Next(360);
            p.Gravity = 20f;
            p.LifeTime = 2f;
        }

        private void SpawnSmoke(object owner, Vector3 position, Vector3 light)
        {
            int i = FindFreeParticle();
            if (i < 0)
                return;

            ref Particle p = ref _particles[i];
            p.Owner = owner;
            p.IsSmoke = true;
            p.Position = position;
            p.Light = light;
            p.LifeTime = 16f;
            p.Scale = (MuGame.Random.Next(32) + 48) * 0.01f;
            p.Rotation = (long)FPSCounter.Instance.WorldTime % 360;
            p.Gravity = 0f;
        }

        private int FindFreeParticle()
        {
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i].Owner == null)
                    return i;
            return -1;
        }

        public bool HasLive(object owner)
        {
            for (int i = 0; i < _thunders.Length; i++) if (_thunders[i].Owner == owner) return true;
            for (int i = 0; i < _particles.Length; i++) if (_particles[i].Owner == owner) return true;
            return false;
        }

        public void Release(object owner)
        {
            for (int i = 0; i < _thunders.Length; i++)
                if (_thunders[i].Owner == owner) { _thunders[i].Owner = null; _thunders[i].Target = null; }
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i].Owner == owner) _particles[i].Owner = null;
        }

        public void Move(object owner, float factor)
        {
            MoveThunders(owner, factor);
            MoveParticles(owner, factor);
        }

        private void MoveThunders(object owner, float factor)
        {
            bool lowWorldTime = (long)FPSCounter.Instance.WorldTime % 1000 < 500;
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Owner != owner)
                    continue;

                // Target pointer stays valid in the original; a removed C# object keeps its last position.
                if (IsPresent(t.Target))
                    t.TargetPosition = t.Target!.WorldPosition.Translation + new Vector3(0f, 0f, 80f);

                SourceJointMath.AngleBasis(t.Angle, out Vector3 endRight, out Vector3 endForward, out Vector3 endUp);
                t.Position += -endForward * (Velocity * factor);

                SourceTrail trail = t.Trail!;
                int steps = trail.MaxTails;
                for (int j = 0; j < steps; j++)
                {
                    float distance = SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, t.TargetPosition, 50f, factor);

                    float dx, dz;
                    if (t.SubType == 28)
                    {
                        dx = MuGame.Random.Next(256) - 128;
                        dz = MuGame.Random.Next(256) - 128;
                    }
                    else
                    {
                        dx = (MuGame.Random.Next(1024) - 512) / t.Scale;
                        dz = (MuGame.Random.Next(1024) - 512) / t.Scale;
                    }

                    var angle = new Vector3(t.Angle.X + dx * factor, t.Angle.Y, t.Angle.Z + dz * factor);
                    SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                    trail.Push(t.Position, right, up, t.Scale);

                    if (distance < Velocity * 1.5f)
                    {
                        if (t.Scale == 50f)
                        {
                            if (FPSCounter.Instance.RandFPSCheck(1))
                                SpawnEnergy(owner, t.Position, t.Light);
                            if (FPSCounter.Instance.RandFPSCheck(8))
                            {
                                MuGame.Random.Next(64); MuGame.Random.Next(64); MuGame.Random.Next(64); // unused jitter
                                SpawnSmoke(owner, t.Position, t.Light);
                            }
                            if (t.SubType == 0 && lowWorldTime && FPSCounter.Instance.RandFPSCheck(16))
                            {
                                var offset = new Vector3(MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50, MuGame.Random.Next(120) - 60);
                                Vector3 childAngle = t.Angle;
                                Vector3 childLight = t.Light;
                                WalkerObject? childTarget = t.Target;
                                Vector3 childTargetPosition = t.TargetPosition;
                                float childScale = 6f + MuGame.Random.Next(8);
                                SpawnThunder(owner, 28, childTargetPosition + offset, childAngle, childLight, childScale, childTarget, childTargetPosition);
                            }
                        }
                        break;
                    }

                    t.Position += -forward * (Velocity * factor * factor);
                }

                trail.Push(t.Position, endRight, endUp, t.Scale);

                t.LifeTime -= factor;
                if (t.LifeTime < 0f)
                {
                    t.Owner = null;
                    t.Target = null;
                }
            }
        }

        private void MoveParticles(object owner, float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Owner != owner)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Owner = null;
                    continue;
                }

                if (p.IsSmoke)
                {
                    p.Light = new Vector3(p.LifeTime / 8f);
                    p.Gravity += 0.2f * factor;
                    p.Position.Z += p.Gravity * factor;
                    p.Scale += factor * 0.05f;
                }
                else
                {
                    p.Rotation += p.Gravity * factor;
                }
            }
        }

        public void Draw(object owner, SourceQuadBatch batch, Texture2D? thunderTexture, Texture2D? smokeTexture, Texture2D? energyTexture)
        {
            if (batch.Begin(thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
                float pow2 = MathF.Pow(2f, SourceJointMath.FrameFactor);
                for (int i = 0; i < _thunders.Length; i++)
                {
                    ref Thunder t = ref _thunders[i];
                    if (t.Owner == owner)
                        batch.AddTrail(t.Trail!, SourceQuadBatch.TrailFaces.Both, t.Light, SourceQuadBatch.TrailShade.Flat,
                            (t.Trail!.MaxTails - 1) / pow2, scroll, false, 2f * scroll);
                }
                batch.End();
            }

            DrawParticles(owner, batch, smokeTexture, true);
            DrawParticles(owner, batch, energyTexture, false);
        }

        private void DrawParticles(object owner, SourceQuadBatch batch, Texture2D? texture, bool smoke)
        {
            if (!batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Owner == owner && p.IsSmoke == smoke)
                    batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
            batch.End();
        }

        private static bool IsPresent(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;
    }
}
