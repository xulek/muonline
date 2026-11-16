#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Fixed-capacity port of the SourceMain5.2 JOINT tail history (ZzzEffectJoint.cpp
    /// CreateTail without blur). Each tail stores the four cross points the C++ code writes:
    /// [0]/[1] = Position -/+ local X * Scale/2 (RENDER_FACE_TWO plane) and
    /// [2]/[3] = Position -/+ local Z * Scale/2 (RENDER_FACE_ONE plane).
    /// Index 0 is always the newest tail, exactly like <c>o-&gt;Tails[0]</c>.
    /// </summary>
    internal sealed class SourceTrail
    {
        private readonly Vector3[] _points;
        private int _head = -1;

        public SourceTrail(int maxTails)
        {
            MaxTails = maxTails < 2 ? 2 : maxTails;
            _points = new Vector3[MaxTails * 4];
        }

        /// <summary>C++ o-&gt;MaxTails: the history keeps at most this many tails.</summary>
        public int MaxTails { get; private set; }

        /// <summary>Number of stored tails (C++ NumTails + 1).</summary>
        public int Count { get; private set; }

        /// <summary>Rendered segment count (C++ o-&gt;NumTails).</summary>
        public int SegmentCount => Count > 1 ? Count - 1 : 0;

        public void Clear()
        {
            Count = 0;
            _head = -1;
        }

        /// <summary>Reuse a preallocated trail with the MaxTails chosen at joint creation.</summary>
        public void Reset(int maxTails)
        {
            MaxTails = System.Math.Clamp(maxTails, 2, _points.Length / 4);
            Clear();
        }

        /// <summary>CreateTail(o, AngleMatrix(angleDeg)) at <paramref name="position"/>.</summary>
        public void Push(Vector3 position, Vector3 angleDeg, float scale)
        {
            SourceJointMath.AngleBasis(angleDeg, out Vector3 right, out _, out Vector3 up);
            Push(position, right, up, scale);
        }

        /// <summary>CreateTailAxis(o, AngleMatrix(angleDeg), 1): corners 2 / 3 along local Y instead of Z.</summary>
        public void PushAxisY(Vector3 position, Vector3 angleDeg, float scale)
        {
            SourceJointMath.AngleBasis(angleDeg, out Vector3 right, out Vector3 forward, out _);
            Push(position, right, forward, scale);
        }

        /// <summary>CreateTail with an explicit (already rotated) local X / Z basis.</summary>
        public void Push(Vector3 position, Vector3 right, Vector3 up, float scale)
        {
            _head = (_head + 1) % MaxTails;
            if (Count < MaxTails)
                Count++;

            float half = scale * 0.5f;
            int baseIndex = _head * 4;
            _points[baseIndex] = position - right * half;
            _points[baseIndex + 1] = position + right * half;
            _points[baseIndex + 2] = position - up * half;
            _points[baseIndex + 3] = position + up * half;
        }

        /// <summary>Moves every stored tail by <paramref name="delta"/> (owner-relative joints).</summary>
        public void Translate(Vector3 delta)
        {
            if (delta == Vector3.Zero)
                return;
            for (int i = 0; i < _points.Length; i++)
                _points[i] += delta;
        }

        /// <summary>Tail corner <paramref name="corner"/> (0..3) of tail <paramref name="tail"/> (0 = newest).</summary>
        public Vector3 Get(int tail, int corner)
        {
            int index = _head - tail;
            if (index < 0)
                index += MaxTails;
            return _points[index * 4 + corner];
        }

        /// <summary>Centre of tail <paramref name="tail"/>.</summary>
        public Vector3 GetCenter(int tail) => (Get(tail, 0) + Get(tail, 1)) * 0.5f;
    }
}
