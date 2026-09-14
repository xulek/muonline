using System;
using System.Collections.Generic;

namespace Client.Main.Controls.Terrain
{
    // World-space samples survive GPU mesh eviction. Each sample belongs to exactly
    // one chunk; neighbouring meshes read the same sample along their shared edge.
    internal sealed class DeviasSnowField
    {
        public const float Spacing = 6.25f;
        public const int Cells = 64;
        public const int MaxChunks = 128;
        private readonly float _lifetime;
        private readonly Dictionary<(int X, int Y), Chunk> _chunks = new();
        private readonly List<(int X, int Y)> _expired = new();
        private long _revision;
        private readonly long[] _meshRevisions = new long[64 * 64];

        private sealed class Chunk
        {
            public readonly float[] Depth = new float[Cells * Cells];
            public readonly double[] Time = new double[Cells * Cells];
            public double LastTouched;
        }

        public DeviasSnowField(float lifetime) => _lifetime = Math.Clamp(lifetime, 5f, 600f);
        public int ChunkCount => _chunks.Count;

        public static bool CanStampMovement(float dx, float dy, float dt, bool grounded)
        {
            float distanceSquared = dx * dx + dy * dy;
            float limit = MathF.Min(100f, 12f + MathF.Max(0, dt) * 900f);
            return grounded && float.IsFinite(distanceSquared) && dt > 0 && dt <= 0.25f &&
                   distanceSquared > 0.01f && distanceSquared <= limit * limit;
        }

        public float SampleDepth(int x, int y, double now)
        {
            if (x < 0 || y < 0 || !_chunks.TryGetValue((x / Cells, y / Cells), out var chunk))
                return 0;
            int index = x % Cells + y % Cells * Cells;
            return chunk.Depth[index] * (float)Math.Clamp(1 - (now - chunk.Time[index]) / _lifetime, 0, 1);
        }

        // Include the normal-calculation apron on all four sides.
        public long GetRevision(int x, int y)
        {
            return (uint)x < 64 && (uint)y < 64 ? _meshRevisions[x + y * 64] : 0;
        }

        public bool HasTracks(int x, int y)
        {
            for (int j = y - 1; j <= y + 1; j++)
                for (int i = x - 1; i <= x + 1; i++)
                    if (_chunks.ContainsKey((i, j))) return true;
            return false;
        }

        private void MarkMeshes(int minX, int minY, int maxX, int maxY)
        {
            long revision = ++_revision;
            for (int y = Math.Max(0, minY); y <= Math.Min(63, maxY); y++)
                for (int x = Math.Max(0, minX); x <= Math.Min(63, maxX); x++)
                    _meshRevisions[x + y * 64] = revision;
        }

        public void StampSegment(float ax, float ay, float bx, float by, float radius, float depth, double now)
        {
            if (!float.IsFinite(ax + ay + bx + by + radius + depth) || radius <= 0 || depth <= 0)
                return;
            radius = MathF.Min(radius, 40f);
            int minX = Math.Max(0, (int)MathF.Floor((MathF.Min(ax, bx) - radius) / Spacing));
            int minY = Math.Max(0, (int)MathF.Floor((MathF.Min(ay, by) - radius) / Spacing));
            int maxX = Math.Min(4095, (int)MathF.Ceiling((MathF.Max(ax, bx) + radius) / Spacing));
            int maxY = Math.Min(4095, (int)MathF.Ceiling((MathF.Max(ay, by) + radius) / Spacing));
            float dx = bx - ax, dy = by - ay;
            float lengthSquared = MathF.Max(dx * dx + dy * dy, 0.0001f);
            // Only meshes whose samples/normal apron overlap the stamp need an upload.
            MarkMeshes((int)MathF.Floor((minX - 3f) / Cells), (int)MathF.Floor((minY - 3f) / Cells),
                (maxX + 2) / Cells, (maxY + 2) / Cells);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x * Spacing - ax, py = y * Spacing - ay;
                    float t = Math.Clamp((px * dx + py * dy) / lengthSquared, 0, 1);
                    px -= t * dx;
                    py -= t * dy;
                    float r = MathF.Sqrt(px * px + py * py) / radius;
                    if (r >= 1) continue;
                    // Flat compacted sole, steep but smooth banks around it.
                    float edge = Math.Clamp((1 - r) / 0.55f, 0, 1);
                    float cut = depth * edge * edge * (3 - 2 * edge);
                    var key = (x / Cells, y / Cells);
                    if (!_chunks.TryGetValue(key, out var chunk))
                    {
                        if (_chunks.Count >= MaxChunks) EvictOldest();
                        _chunks.Add(key, chunk = new Chunk());
                    }
                    int index = x % Cells + y % Cells * Cells;
                    float existing = SampleDepth(x, y, now);
                    chunk.Depth[index] = MathF.Max(existing, cut);
                    chunk.Time[index] = now;
                    chunk.LastTouched = now;
                }
            }
        }

        private void EvictOldest()
        {
            (int X, int Y) oldest = default;
            double time = double.MaxValue;
            foreach (var pair in _chunks)
                if (pair.Value.LastTouched < time)
                {
                    oldest = pair.Key;
                    time = pair.Value.LastTouched;
                }
            _chunks.Remove(oldest);
            MarkMeshes(oldest.X - 1, oldest.Y - 1, oldest.X + 1, oldest.Y + 1);
        }

        public void Prune(double now)
        {
            _expired.Clear();
            foreach (var pair in _chunks)
                if (now - pair.Value.LastTouched >= _lifetime) _expired.Add(pair.Key);
            foreach (var key in _expired)
            {
                _chunks.Remove(key);
                MarkMeshes(key.X - 1, key.Y - 1, key.X + 1, key.Y + 1);
            }
        }
    }
}
