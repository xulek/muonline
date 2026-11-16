#nullable enable
using System;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>Texture atlas layout of a SourceMain particle bitmap.</summary>
    public enum SkillFxSheet : byte
    {
        Single,
        /// <summary>4 horizontal frames (BITMAP_FIRE family: Frame % 4 * 0.25, width * 0.25).</summary>
        Strip4,
        /// <summary>4x4 frames (BITMAP_EXPLOTION).</summary>
        Grid4x4
    }

    /// <summary>One CreateParticle instance (25 Hz tick semantics).</summary>
    public struct SkillFxParticle
    {
        public bool Active;
        public byte Texture;
        public SkillFxSheet Sheet;
        public bool FadeWithLife;
        public bool GroundBounce;
        public Vector3 Position;
        public Vector3 Velocity;
        public float VelocityDamping;
        public float Gravity;
        public float GravityAccel;
        public float GravityToPosition;
        public float GravityToScale;
        public float Scale;
        public float ScaleAdd;
        public float ScaleMul;
        public float Rotation;
        public float RotationSpeed;
        public Vector3 Light;
        public float LightMul;
        public float Life;
        public float MaxLife;
        public float FrameSpeed;
    }

    /// <summary>Fixed-capacity particle pool with the generic SourceMain particle integrator.</summary>
    public sealed class SkillFxParticlePool
    {
        private readonly SkillFxParticle[] _items;

        public SkillFxParticlePool(int capacity)
        {
            _items = new SkillFxParticle[capacity];
        }

        public bool HasActive
        {
            get
            {
                for (int i = 0; i < _items.Length; i++)
                    if (_items[i].Active)
                        return true;
                return false;
            }
        }

        public static SkillFxParticle Create(byte texture, Vector3 position, float life, float scale, Vector3 light)
        {
            return new SkillFxParticle
            {
                Active = true,
                Texture = texture,
                Position = position,
                Life = life,
                MaxLife = life,
                Scale = scale,
                ScaleMul = 1f,
                Light = light,
                LightMul = 1f,
                VelocityDamping = 1f,
                FrameSpeed = 1f
            };
        }

        public void Spawn(in SkillFxParticle particle)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (!_items[i].Active)
                {
                    _items[i] = particle;
                    _items[i].Active = true;
                    return;
                }
            }
        }

        public void Tick(TerrainControl? terrain)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                ref SkillFxParticle p = ref _items[i];
                if (!p.Active)
                    continue;

                p.Life -= 1f;
                if (p.Life <= 0f)
                {
                    p.Active = false;
                    continue;
                }

                p.Gravity += p.GravityAccel;
                p.Position.Z += p.Gravity * p.GravityToPosition;
                p.Scale = p.Scale * p.ScaleMul + p.ScaleAdd + p.Gravity * p.GravityToScale;
                p.Position += p.Velocity;
                p.Velocity *= p.VelocityDamping;
                p.Rotation += p.RotationSpeed;
                p.Light *= p.LightMul;

                if (p.GroundBounce && terrain != null)
                {
                    float h = terrain.RequestTerrainHeight(p.Position.X, p.Position.Y);
                    if (p.Position.Z < h)
                    {
                        p.Position.Z = h;
                        p.Gravity = -p.Gravity * 0.6f;
                        p.Life -= 4f;
                    }
                }

                if (p.Scale <= 0.01f)
                    p.Active = false;
            }
        }

        /// <summary>Draws all particles using <paramref name="texture"/> slot. Size = texture width * Scale.</summary>
        public void Draw(byte textureSlot, Texture2D? texture, BlendState? blend = null)
        {
            if (texture == null)
                return;

            bool begun = false;
            for (int i = 0; i < _items.Length; i++)
            {
                ref SkillFxParticle p = ref _items[i];
                if (!p.Active || p.Texture != textureSlot)
                    continue;

                if (!begun)
                {
                    if (!SkillFxQuadBatch.Begin(texture, blend))
                        return;
                    begun = true;
                }

                float lum = p.FadeWithLife ? p.Life / MathF.Max(p.MaxLife, 1f) : 1f;
                Color color = SkillFxQuadBatch.Light(p.Light, lum);
                float halfW = texture.Width * p.Scale * 0.5f;
                float halfH = texture.Height * p.Scale * 0.5f;
                float age = (p.MaxLife - p.Life) * p.FrameSpeed;
                switch (p.Sheet)
                {
                    case SkillFxSheet.Strip4:
                    {
                        int frame = Math.Clamp((int)(age * 4f / MathF.Max(p.MaxLife, 1f)), 0, 3);
                        float u = frame * 0.25f;
                        SkillFxQuadBatch.Billboard(p.Position, halfW * 0.25f, halfH, p.Rotation, color, u, 0f, u + 0.25f, 1f);
                        break;
                    }
                    case SkillFxSheet.Grid4x4:
                    {
                        int frame = Math.Clamp((int)(age / 2f), 0, 15);
                        float u = frame % 4 * 0.25f + 0.005f;
                        float v = frame / 4 * 0.25f + 0.005f;
                        SkillFxQuadBatch.Billboard(p.Position, halfW, halfH, p.Rotation, color, u, v, u + 0.24f, v + 0.24f);
                        break;
                    }
                    default:
                        SkillFxQuadBatch.Billboard(p.Position, halfW, halfH, p.Rotation, color);
                        break;
                }
            }

            if (begun)
                SkillFxQuadBatch.End();
        }
    }

    /// <summary>One CreateJoint streak: flies along its Angle (SourceMain degrees) leaving a short tail.</summary>
    public struct SkillFxStreak
    {
        public const int MaxTails = 8;

        public bool Active;
        public Vector3 Position;
        public Vector3 AngleDeg;
        public float Velocity;
        public float VelocityAdd;
        public Vector3 Light;
        public float LightMul;
        public float Life;
        public float MaxLife;
        public float Width;
        public int TailCount;
        public int TailLimit;
        public bool HasTarget;
        public Vector3 Target;
        public Tails Tail;

        [System.Runtime.CompilerServices.InlineArray(MaxTails)]
        public struct Tails
        {
            private Vector3 _element;
        }
    }

    /// <summary>Fixed-capacity pool of joint streaks (BITMAP_JOINT_SPARK, JOINT_SPIRIT, JOINT_HEALING...).</summary>
    public sealed class SkillFxStreakPool
    {
        private readonly SkillFxStreak[] _items;

        public SkillFxStreakPool(int capacity)
        {
            _items = new SkillFxStreak[capacity];
        }

        public bool HasActive
        {
            get
            {
                for (int i = 0; i < _items.Length; i++)
                    if (_items[i].Active)
                        return true;
                return false;
            }
        }

        public int Spawn(Vector3 position, Vector3 angleDeg, float velocity, float life, float width, int tails,
            Vector3 light, float velocityAdd = 0f, float lightMul = 1f)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Active)
                    continue;

                ref SkillFxStreak s = ref _items[i];
                s = default;
                s.Active = true;
                s.Position = position;
                s.AngleDeg = angleDeg;
                s.Velocity = velocity;
                s.VelocityAdd = velocityAdd;
                s.Light = light;
                s.LightMul = lightMul;
                s.Life = life;
                s.MaxLife = life;
                s.Width = width;
                s.TailLimit = Math.Clamp(tails, 2, SkillFxStreak.MaxTails);
                s.Tail[0] = position;
                s.TailCount = 1;
                return i;
            }

            return -1;
        }

        /// <summary>Streak that travels in a straight line to <paramref name="target"/> over its life.</summary>
        public void SpawnToward(Vector3 position, Vector3 target, float life, float width, int tails, Vector3 light)
        {
            int index = Spawn(position, Vector3.Zero, 0f, life, width, tails, light);
            if (index < 0)
                return;
            _items[index].HasTarget = true;
            _items[index].Target = target;
        }

        public void Tick()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                ref SkillFxStreak s = ref _items[i];
                if (!s.Active)
                    continue;

                s.Life -= 1f;
                if (s.Life <= 0f)
                {
                    s.Active = false;
                    continue;
                }

                if (s.HasTarget)
                    s.Position += (s.Target - s.Position) / MathF.Max(s.Life + 1f, 1f);
                else
                    s.Position += SkillFxMath.Rotate(new Vector3(0f, -s.Velocity, 0f), s.AngleDeg);

                s.Velocity += s.VelocityAdd;
                s.Light *= s.LightMul;

                int count = Math.Min(s.TailCount + 1, s.TailLimit);
                for (int t = count - 1; t > 0; t--)
                    s.Tail[t] = s.Tail[t - 1];
                s.Tail[0] = s.Position;
                s.TailCount = count;
            }
        }

        public void Draw(Texture2D? texture, BlendState? blend = null)
        {
            if (texture == null)
                return;

            bool begun = false;
            for (int i = 0; i < _items.Length; i++)
            {
                ref SkillFxStreak s = ref _items[i];
                if (!s.Active || s.TailCount < 2)
                    continue;

                if (!begun)
                {
                    if (!SkillFxQuadBatch.Begin(texture, blend))
                        return;
                    begun = true;
                }

                float denom = MathF.Max(s.TailLimit - 1, 1);
                Color color = SkillFxQuadBatch.Light(s.Light);
                float half = s.Width * 0.5f;
                for (int t = 0; t < s.TailCount - 1; t++)
                {
                    SkillFxQuadBatch.Segment(s.Tail[t], s.Tail[t + 1], half, half, color, color,
                        t / denom, (t + 1) / denom);
                }
            }

            if (begun)
                SkillFxQuadBatch.End();
        }
    }
}
