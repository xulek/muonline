#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Support
{
    /// <summary>How a <see cref="SourceFxTrail"/> maps U along its tails (RenderJoints Light1/Light2).</summary>
    public enum TrailUvMode
    {
        /// <summary>(NumTails - j) / (MaxTails - 1).</summary>
        Normal,
        /// <summary>m_byReverseUV == 3: 1 - j / (MaxTails - 1).</summary>
        Reverse,
        /// <summary>bTileMapping: (NumTails - j) / 16 * 2 - scroll * 2 (wrapped texture).</summary>
        Tiled,
        /// <summary>BITMAP_FLARE_FORCE 0-4 / 11-13: (NumTails - j) / ((MaxTails - 1) / 2) - scroll.</summary>
        FlareForce
    }

    /// <summary>
    /// One SourceMain5.2 JOINT tail ribbon (ZzzEffectJoint.cpp CreateTail / RenderJoints):
    /// a shifting history of four-point crosses rendered as up to two quad strips.
    /// Instances are preallocated by <see cref="SourceFxEffect"/> and reused.
    /// </summary>
    public sealed class SourceFxTrail
    {
        public const int MaxCapacity = 51;

        private readonly Vector3[] _tails = new Vector3[MaxCapacity * 4];

        public bool Active;
        public int Slot;
        public int MaxTails = 2;
        public int NumTails;
        public float Scale;
        public Vector3 Light = Vector3.One;
        public bool FaceOne = true;
        public bool FaceTwo = true;
        public TrailUvMode UvMode;
        /// <summary>FLARE_FORCE colour ramp: Light * ((NumTails - 1 - j) / MaxTails * 2).</summary>
        public bool FlareForceRamp;

        // Free C++ JOINT working fields.
        public Vector3 Position;
        public Vector3 Angle;
        public Vector3 Direction;
        public Vector3 StartPosition;
        public Vector3 TargetPosition;
        public float Velocity;
        public float LifeTime;
        public float Weapon;
        public float MultiUse;
        public int SubType;
        public bool Collision;
        public WalkerObject? Target;

        public void Begin(int slot, int maxTails, float scale)
        {
            Active = true;
            Slot = slot;
            MaxTails = Math.Clamp(maxTails, 2, MaxCapacity);
            NumTails = 0;
            Scale = scale;
            Light = Vector3.One;
            FaceOne = true;
            FaceTwo = true;
            UvMode = TrailUvMode.Normal;
            FlareForceRamp = false;
            Direction = Vector3.Zero;
            StartPosition = Vector3.Zero;
            TargetPosition = Vector3.Zero;
            Velocity = 0f;
            LifeTime = 0f;
            Weapon = 0f;
            MultiUse = 0f;
            SubType = 0;
            Collision = false;
            Target = null;
        }

        /// <summary>CreateJoint start tail: writes slot 0 without shifting.</summary>
        public void SetStartTail(Vector3 position, Vector3 angleDeg, bool axisY = false)
            => Write(0, position, angleDeg, axisY);

        /// <summary>CreateTail / CreateTailAxis(axis 1 when <paramref name="axisY"/>).</summary>
        public void Push(Vector3 position, Vector3 angleDeg, bool axisY = false)
        {
            NumTails++;
            if (NumTails > MaxTails - 1)
                NumTails = MaxTails - 1;

            for (int j = NumTails - 1; j >= 0; j--)
            {
                int src = j * 4;
                int dst = src + 4;
                _tails[dst] = _tails[src];
                _tails[dst + 1] = _tails[src + 1];
                _tails[dst + 2] = _tails[src + 2];
                _tails[dst + 3] = _tails[src + 3];
            }

            Write(0, position, angleDeg, axisY);
        }

        /// <summary>Moves every stored tail point (used when a ribbon follows a moving owner).</summary>
        public void Translate(Vector3 delta)
        {
            int count = (NumTails + 1) * 4;
            for (int i = 0; i < count; i++)
                _tails[i] += delta;
        }

        private void Write(int index, Vector3 p, Vector3 angleDeg, bool axisY)
        {
            SourceMath.Basis(angleDeg, out var right, out var forward, out var up);
            float h = Scale * 0.5f;
            int i = index * 4;
            _tails[i] = p - right * h;
            _tails[i + 1] = p + right * h;
            Vector3 second = axisY ? forward : up;
            _tails[i + 2] = p - second * h;
            _tails[i + 3] = p + second * h;
        }

        internal void Emit(float scroll)
        {
            if (NumTails <= 0)
                return;

            float denom = Math.Max(MaxTails - 1, 1);
            float flareDenom = Math.Max((MaxTails - 1) / 2, 1);

            for (int j = 0; j < NumTails; j++)
            {
                float l1, l2;
                switch (UvMode)
                {
                    case TrailUvMode.Reverse:
                        l1 = 1f - j / denom;
                        l2 = 1f - (j + 1) / denom;
                        break;
                    case TrailUvMode.Tiled:
                        l1 = (NumTails - j) / 16f * 2f - scroll * 2f;
                        l2 = (NumTails - j - 1) / 16f * 2f - scroll * 2f;
                        break;
                    case TrailUvMode.FlareForce:
                        l1 = (NumTails - j) / flareDenom - scroll;
                        l2 = (NumTails - j - 1) / flareDenom - scroll;
                        break;
                    default:
                        l1 = (NumTails - j) / denom;
                        l2 = (NumTails - j - 1) / denom;
                        break;
                }

                Vector3 light = Light;
                if (FlareForceRamp)
                    light *= (NumTails - 1 - j) / (float)MaxTails * 2f;
                var color = new Color(light);

                int c = j * 4;
                int n = c + 4;
                if (FaceOne)
                {
                    SourceFxBatch.Quad(
                        _tails[c + 2], new Vector2(l1, 1f),
                        _tails[c + 3], new Vector2(l1, 0f),
                        _tails[n + 3], new Vector2(l2, 0f),
                        _tails[n + 2], new Vector2(l2, 1f),
                        color);
                }

                if (FaceTwo)
                {
                    SourceFxBatch.Quad(
                        _tails[c], new Vector2(l1, 0f),
                        _tails[c + 1], new Vector2(l1, 1f),
                        _tails[n + 1], new Vector2(l2, 1f),
                        _tails[n], new Vector2(l2, 0f),
                        color);
                }
            }
        }
    }
}
