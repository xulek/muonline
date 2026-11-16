#nullable enable
using System;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Tail ribbon of a SourceMain5.2 "tail" joint (ZzzEffectJoint.cpp, m_bCreateTails):
    /// BITMAP_SWORDEFF, BITMAP_FORCEPILLAR, BITMAP_GROUND_WIND and BITMAP_LAVA all follow
    /// one bone of their target and leave a camera-facing ribbon of width <c>Scale</c>
    /// behind it. The original spawns one short-lived joint per frame; here every frame
    /// pushes one sample whose brightness fades over the joint lifetime.
    /// </summary>
    internal sealed class RageFxTrail
    {
        private readonly Vector3[] _points;
        private readonly float[] _ages;
        private int _head;
        private int _count;

        public RageFxTrail(int capacity, int textureSlot, float sampleLifeFrames, float width, Vector3 light)
        {
            _points = new Vector3[Math.Max(2, capacity)];
            _ages = new float[_points.Length];
            TextureSlot = textureSlot;
            SampleLife = sampleLifeFrames;
            Width = width;
            Light = light;
        }

        public int TextureSlot { get; }
        public float SampleLife { get; set; }
        public float Width { get; set; }
        public Vector3 Light { get; set; }

        /// <summary>World units every sample drifts up per frame (BITMAP_GROUND_WIND rises 20).</summary>
        public float RisePerFrame { get; set; }

        public bool HasLiveSamples
        {
            get
            {
                for (int i = 0; i < _count; i++)
                {
                    if (_ages[Index(i)] < SampleLife)
                        return true;
                }

                return false;
            }
        }

        public void Push(Vector3 point)
        {
            _points[_head] = point;
            _ages[_head] = 0f;
            _head = (_head + 1) % _points.Length;
            if (_count < _points.Length)
                _count++;
        }

        public void Reset() => _count = 0;

        public void Update(float factor)
        {
            for (int i = 0; i < _count; i++)
            {
                int index = Index(i);
                _ages[index] += factor;
                if (RisePerFrame != 0f)
                    _points[index].Z += RisePerFrame * factor;
            }
        }

        private int Index(int ordinal)
        {
            int start = _count < _points.Length ? 0 : _head;
            return (start + ordinal) % _points.Length;
        }

        public void Write(RageFxBatch batch)
        {
            if (_count < 2)
                return;

            Vector3 cameraPosition = Camera.Instance.Position;
            float halfWidth = Width * 0.5f;

            for (int i = 0; i < _count - 1 && !batch.IsFull; i++)
            {
                int a = Index(i);
                int b = Index(i + 1);
                if (_ages[a] >= SampleLife && _ages[b] >= SampleLife)
                    continue;

                Vector3 p0 = _points[a];
                Vector3 p1 = _points[b];
                Vector3 direction = p1 - p0;
                if (direction.LengthSquared() < 0.01f)
                    continue;

                Vector3 toCamera = cameraPosition - (p0 + p1) * 0.5f;
                Vector3 side = Vector3.Cross(direction, toCamera);
                if (side.LengthSquared() < 0.0001f)
                    continue;

                side.Normalize();
                side *= halfWidth;

                Color c0 = RageFxBatch.ToColor(Light * Fade(_ages[a]));
                Color c1 = RageFxBatch.ToColor(Light * Fade(_ages[b]));
                float u0 = (float)i / (_count - 1);
                float u1 = (float)(i + 1) / (_count - 1);
                batch.Quad(p0 - side, p0 + side, p1 + side, p1 - side, c0, c1, u0, u1);
            }
        }

        private float Fade(float age) => MathHelper.Clamp(1f - age / MathF.Max(SampleLife, 0.001f), 0f, 1f);
    }
}
