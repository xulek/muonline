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

        // Samples read outside a patch when building normals (one Sobel ring).
        public const int Pad = 1;
        // Samples per row/column of the padded block one patch reads: -Pad .. Cells + Pad.
        public const int PatchSamples = Cells + 1 + 2 * Pad;

        private const int Grid = 64; // chunks / meshes per axis (25600 units / 400)

        private readonly float _lifetime;
        // Stamping evaluates the print profile on a supersample x supersample grid inside
        // every sample cell and averages it. Point sampling aliases: the same print changes
        // volume by ~27% with its sub-cell position. 1 reproduces the original exactly.
        private readonly int _supersample;
        private readonly float[] _tapOffsets;
        private readonly Dictionary<(int X, int Y), Chunk> _chunks = new();
        private readonly List<(int X, int Y)> _expired = new();
        private long _revision;
        private readonly long[] _meshRevisions = new long[Grid * Grid];
        // Live chunks in the 3x3 neighbourhood of every mesh, so HasTracks is O(1).
        private readonly short[] _nearbyChunks = new short[Grid * Grid];

        private sealed class Chunk
        {
            public readonly float[] Depth = new float[Cells * Cells];
            public readonly double[] Time = new double[Cells * Cells];
            public double LastTouched;
        }

        public DeviasSnowField(float lifetime, int supersample = 2)
        {
            _lifetime = Math.Clamp(lifetime, 5f, 600f);
            _supersample = Math.Clamp(supersample, 1, 4);
            _tapOffsets = new float[_supersample];
            for (int i = 0; i < _supersample; i++)
                _tapOffsets[i] = ((i + 0.5f) / _supersample - 0.5f) * Spacing; // 0 for a single tap
        }
        public int ChunkCount => _chunks.Count;
        public float Lifetime => _lifetime;

        public static bool CanStampMovement(float dx, float dy, float dt, bool grounded)
        {
            float distanceSquared = dx * dx + dy * dy;
            float limit = MathF.Min(100f, 12f + MathF.Max(0, dt) * 900f);
            return grounded && float.IsFinite(distanceSquared) && dt > 0 && dt <= 0.25f &&
                   distanceSquared > 0.01f && distanceSquared <= limit * limit;
        }

        private float Decayed(Chunk chunk, int index, double now)
        {
            return chunk.Depth[index] * (float)Math.Clamp(1 - (now - chunk.Time[index]) / _lifetime, 0, 1);
        }

        public float SampleDepth(int x, int y, double now)
        {
            if (x < 0 || y < 0 || !_chunks.TryGetValue((x / Cells, y / Cells), out var chunk))
                return 0;
            return Decayed(chunk, x % Cells + y % Cells * Cells, now);
        }

        // Writes the decayed depth of every sample a patch reads (-Pad .. Cells + Pad on
        // both axes) into dst, row-major with stride PatchSamples. One dictionary lookup
        // per neighbouring chunk instead of one per sample. Returns false when no chunk
        // touches the patch (dst is then all zeroes).
        public bool FillDepth(int cx, int cy, float[] dst, double now)
        {
            Array.Clear(dst, 0, PatchSamples * PatchSamples);
            bool any = false;
            for (int ky = cy - 1; ky <= cy + 1; ky++)
            {
                if (ky < 0) continue;
                for (int kx = cx - 1; kx <= cx + 1; kx++)
                {
                    if (kx < 0 || !_chunks.TryGetValue((kx, ky), out var chunk)) continue;
                    // Chunks past their lifetime decay to exactly zero; Prune removes them later.
                    if (now - chunk.LastTouched >= _lifetime) continue;
                    int offsetX = (kx - cx) * Cells, offsetY = (ky - cy) * Cells;
                    int x0 = Math.Max(-Pad, offsetX), x1 = Math.Min(Cells + Pad, offsetX + Cells - 1);
                    int y0 = Math.Max(-Pad, offsetY), y1 = Math.Min(Cells + Pad, offsetY + Cells - 1);
                    if (x0 > x1 || y0 > y1) continue;
                    any = true;
                    for (int y = y0; y <= y1; y++)
                    {
                        int source = x0 - offsetX + (y - offsetY) * Cells;
                        int target = x0 + Pad + (y + Pad) * PatchSamples;
                        for (int x = x0; x <= x1; x++)
                            dst[target++] = Decayed(chunk, source++, now);
                    }
                }
            }
            return any;
        }

        public long GetRevision(int x, int y)
        {
            return (uint)x < Grid && (uint)y < Grid ? _meshRevisions[x + y * Grid] : 0;
        }

        // True when any chunk lives in the 3x3 neighbourhood: those samples can still decay.
        public bool HasTracks(int x, int y)
        {
            return (uint)x < Grid && (uint)y < Grid && _nearbyChunks[x + y * Grid] > 0;
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
        }

        private void MarkMeshes(int minX, int minY, int maxX, int maxY)
        {
            long revision = ++_revision;
            for (int y = Math.Max(0, minY); y <= Math.Min(Grid - 1, maxY); y++)
                for (int x = Math.Max(0, minX); x <= Math.Min(Grid - 1, maxX); x++)
                    _meshRevisions[x + y * Grid] = revision;
        }

        private void AdjustNeighbours((int X, int Y) key, int delta)
        {
            for (int y = Math.Max(0, key.Y - 1); y <= Math.Min(Grid - 1, key.Y + 1); y++)
                for (int x = Math.Max(0, key.X - 1); x <= Math.Min(Grid - 1, key.X + 1); x++)
                    _nearbyChunks[x + y * Grid] += (short)delta;
        }

        private Chunk AddChunk((int X, int Y) key)
        {
            if (_chunks.Count >= MaxChunks) EvictOldest();
            var chunk = new Chunk();
            _chunks.Add(key, chunk);
            AdjustNeighbours(key, 1);
            return chunk;
        }

        private void RemoveChunk((int X, int Y) key)
        {
            if (!_chunks.Remove(key)) return;
            AdjustNeighbours(key, -1);
            MarkMeshes(key.X - 1, key.Y - 1, key.X + 1, key.Y + 1);
        }

        public void StampSegment(float ax, float ay, float bx, float by, float radius, float depth, double now)
        {
            if (!float.IsFinite(ax + ay + bx + by + radius + depth) || radius <= 0 || depth <= 0)
                return;
            radius = MathF.Min(radius, 40f);
            float reach = radius + (_supersample > 1 ? Spacing * 0.5f : 0f); // taps sit up to half a cell off-centre
            int minX = Math.Max(0, (int)MathF.Floor((MathF.Min(ax, bx) - reach) / Spacing));
            int minY = Math.Max(0, (int)MathF.Floor((MathF.Min(ay, by) - reach) / Spacing));
            int maxX = Math.Min(4095, (int)MathF.Ceiling((MathF.Max(ax, bx) + reach) / Spacing));
            int maxY = Math.Min(4095, (int)MathF.Ceiling((MathF.Max(ay, by) + reach) / Spacing));
            float dx = bx - ax, dy = by - ay;
            float lengthSquared = MathF.Max(dx * dx + dy * dy, 0.0001f);

            // Neighbouring samples of one row almost always share a chunk.
            (int X, int Y) cachedKey = (-1, -1);
            Chunk cached = null;
            bool changed = false;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float cut = 0;
                    bool hit = false;
                    for (int ty = 0; ty < _supersample; ty++)
                        for (int tx = 0; tx < _supersample; tx++)
                        {
                            float px = x * Spacing + _tapOffsets[tx] - ax, py = y * Spacing + _tapOffsets[ty] - ay;
                            float t = Math.Clamp((px * dx + py * dy) / lengthSquared, 0, 1);
                            px -= t * dx;
                            py -= t * dy;
                            float r = MathF.Sqrt(px * px + py * py) / radius;
                            if (r >= 1) continue;
                            // Flat compacted sole, steep but smooth banks around it.
                            float edge = Math.Clamp((1 - r) / 0.55f, 0, 1);
                            cut += depth * edge * edge * (3 - 2 * edge);
                            hit = true;
                        }
                    if (!hit) continue;
                    if (_supersample > 1) cut /= _supersample * _supersample;
                    var key = (x / Cells, y / Cells);
                    Chunk chunk;
                    if (cached != null && key == cachedKey)
                        chunk = cached;
                    else
                    {
                        if (!_chunks.TryGetValue(key, out chunk)) chunk = AddChunk(key);
                        cached = chunk;
                        cachedKey = key;
                    }
                    int index = x % Cells + y % Cells * Cells;
                    chunk.Depth[index] = MathF.Max(Decayed(chunk, index, now), cut);
                    chunk.Time[index] = now;
                    chunk.LastTouched = now;
                    changed = true;
                }
            }
            // Only meshes whose samples/normal apron overlap the stamp need an upload,
            // and only when a sample was actually written.
            if (changed)
                MarkMeshes(FloorDiv(minX - Pad - 1, Cells), FloorDiv(minY - Pad - 1, Cells),
                    FloorDiv(maxX + Pad, Cells), FloorDiv(maxY + Pad, Cells));
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
            RemoveChunk(oldest);
        }

        public void Prune(double now)
        {
            _expired.Clear();
            foreach (var pair in _chunks)
                if (now - pair.Value.LastTouched >= _lifetime) _expired.Add(pair.Key);
            foreach (var key in _expired) RemoveChunk(key);
        }
    }
}
