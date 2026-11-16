#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// BITMAP_JOINT_THUNDER joints homing on a fixed TargetPosition (ZzzEffectJoint.cpp:1053 init, 4688 move,
    /// 6921 render): prologue flight, MaxTails x (MoveHumming 50, jitter, CreateTail, step), epilogue tail.
    /// Sub 3: Direction (rand%1024-512)/Scale, LifeTime 0 when Distance &gt; 150. Sub 16 / 0: break when Distance &lt; Velocity * 1.5.
    /// Sub 4 (4697 / 4740 / 4865): Position reset to the target every move + BITMAP_SHINY+1 sprite at +30F
    /// ((rand%8+8)*0.2), MoveHumming toward target - 300 Z with turn -10, Direction x = rand%64-32 (z stays 0).
    /// Render: thunder U ((NumTails - j) / (MaxTails - 1) * 2^F - Scroll, face TWO + 2 Scroll), GL_REPEAT.
    /// </summary>
    internal sealed class SourceThunderPool
    {
        public const string TexturePath = "Effect/JointThunder01.jpg";

        private struct Thunder
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 TargetPosition;
            public Vector3 Light;
            public float Velocity;
            public float Scale;
            public float LifeTime;
            public SourceTrail Trail;
            public bool HasSprite;
            public Vector3 SpritePosition;
            public float SpriteScale;
            public float SpriteRotation;
        }

        public const string ShinyTexturePath = "Effect/Shiny02.jpg"; // BITMAP_SHINY + 1 (sub 4 sprite)

        private readonly Thunder[] _thunders;

        public SourceThunderPool(int capacity)
        {
            _thunders = new Thunder[capacity];
        }

        public bool HasLive
        {
            get
            {
                for (int i = 0; i < _thunders.Length; i++) if (_thunders[i].Live) return true;
                return false;
            }
        }

        /// <summary>CreateJoint(BITMAP_JOINT_THUNDER, from, target, angle, subType, NULL, scale, PKKey, SkillIndex).</summary>
        public void Spawn(Vector3 from, Vector3 target, Vector3 angleDeg, int subType, float scale,
            float lifeTime, float velocity, int maxTails, Vector3 light)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live)
                    continue;

                t.Live = true;
                t.SubType = subType;
                t.Position = from;
                t.TargetPosition = target;
                t.Angle = angleDeg;
                t.Light = light;
                t.Velocity = velocity;
                t.Scale = scale;
                t.LifeTime = lifeTime;
                int tails = SourceJointMath.ScaledMaxTails(maxTails);
                if (t.Trail == null || t.Trail.MaxTails != tails)
                    t.Trail = new SourceTrail(tails);
                else
                    t.Trail.Clear();
                t.Trail.Push(from, angleDeg, scale);
                return;
            }
        }

        public void Move(float factor)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (!t.Live)
                    continue;

                SourceJointMath.AngleBasis(t.Angle, out Vector3 endRight, out Vector3 endForward, out Vector3 endUp);
                t.Position += -endForward * (t.Velocity * factor);

                t.HasSprite = false;
                if (t.SubType == 4)
                {
                    t.Position = t.TargetPosition;
                    t.HasSprite = true;
                    t.SpritePosition = t.TargetPosition + Vector3.UnitZ * (30f * factor);
                    t.SpriteScale = (MuGame.Random.Next(8) + 8) * 0.2f;
                    t.SpriteRotation = MuGame.Random.Next(360);
                }

                int steps = t.Trail.MaxTails;
                for (int j = 0; j < steps; j++)
                {
                    float distance;
                    Vector3 angle;
                    if (t.SubType == 4)
                    {
                        Vector3 below = new(t.TargetPosition.X, t.TargetPosition.Y, t.TargetPosition.Z - 300f);
                        distance = SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, below, -10f, factor);
                        angle = new Vector3(t.Angle.X + (MuGame.Random.Next(64) - 32) * factor, t.Angle.Y, t.Angle.Z);
                    }
                    else
                    {
                        distance = SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, t.TargetPosition, 50f, factor);
                        angle = new Vector3(
                            t.Angle.X + (MuGame.Random.Next(1024) - 512) / t.Scale * factor,
                            t.Angle.Y,
                            t.Angle.Z + (MuGame.Random.Next(1024) - 512) / t.Scale * factor);
                    }
                    SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                    t.Trail.Push(t.Position, right, up, t.Scale);

                    if (t.SubType == 3)
                    {
                        if (distance > 150f)
                            t.LifeTime = 0f;
                    }
                    else if (distance < t.Velocity * 1.5f)
                    {
                        break;
                    }

                    t.Position += -forward * (t.Velocity * factor * factor);
                }

                t.Trail.Push(t.Position, endRight, endUp, t.Scale);
                t.LifeTime -= factor;
                if (t.LifeTime < 0f)
                    t.Live = false;
            }
        }

        /// <summary>The sub 4 BITMAP_SHINY+1 sprites created during the last <see cref="Move"/>.</summary>
        public void DrawSprites(SourceQuadBatch batch, Texture2D? shinyTexture)
        {
            if (!batch.Begin(shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live && t.HasSprite)
                    batch.AddSprite(t.SpritePosition, t.SpriteScale, t.Light, t.SpriteRotation);
            }
            batch.End();
        }

        public void Draw(SourceQuadBatch batch, Texture2D? texture)
        {
            if (!batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
                return;

            float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
            float pow2 = MathF.Pow(2f, SourceJointMath.FrameFactor);
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live)
                    batch.AddTrail(t.Trail, SourceQuadBatch.TrailFaces.Both, t.Light, SourceQuadBatch.TrailShade.Flat,
                        (t.Trail.MaxTails - 1) / pow2, scroll, false, 2f * scroll);
            }
            batch.End();
        }
    }
}
