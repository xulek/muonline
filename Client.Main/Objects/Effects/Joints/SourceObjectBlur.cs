#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    // ZzzEffectBlurSpark.cpp:19-25, 207-225, 227-350: one OBJECT_BLUR record.
    internal sealed class SourceObjectBlur
    {
        private const int Capacity = 600;
        private const int MaxCount = Capacity - 1;
        private const int InitialLife = 30;
        private readonly Vector3[] _first = new Vector3[Capacity];
        private readonly Vector3[] _second = new Vector3[Capacity];
        private int _head;
        private int _count;
        private int _life;

        public bool IsLive => _life > 0;

        public void Add(Vector3 first, Vector3 second)
        {
            if (_life <= 0)
            {
                _head = 0;
                _count = 0;
                _life = InitialLife;
            }
            _head = (_head + Capacity - 1) % Capacity;
            _first[_head] = first;
            _second[_head] = second;
            if (_count < MaxCount)
                _count++;
        }

        public void Move()
        {
            if (_life <= 0)
                return;
            _life--;
            if (_life <= 0)
            {
                _count = 0;
                return;
            }
            if (_count > 0)
                _count--;
            if (_count > 0)
            {
                int previousHead = _head;
                _head = (_head + Capacity - 1) % Capacity;
                _first[_head] = _first[previousHead];
                _second[_head] = _second[previousHead];
            }
        }

        /// <summary>Appends white additive quads to a batch begun with this blur's texture.</summary>
        public void Append(SourceQuadBatch batch) => Append(batch, Vector3.One);

        /// <summary>Appends additive quads coloured b-&gt;Light x (Number - j) / Number.</summary>
        public void Append(SourceQuadBatch batch, Vector3 light)
        {
            for (int j = 0; j < _count - 1; j++)
            {
                int current = (_head + j) % Capacity;
                int next = (_head + j + 1) % Capacity;
                float u0 = j / (float)_count;
                float u1 = (j + 1) / (float)_count;
                float light0 = (_count - j) / (float)_count;
                float light1 = (_count - (j + 1)) / (float)_count;
                batch.AddQuad(_first[current], new Vector2(u0, 1f),
                    _second[current], new Vector2(u0, 0f),
                    _second[next], new Vector2(u1, 0f),
                    _first[next], new Vector2(u1, 1f),
                    SourceQuadBatch.ToColor(light * light0),
                    SourceQuadBatch.ToColor(light * light1));
            }
        }
    }
}
