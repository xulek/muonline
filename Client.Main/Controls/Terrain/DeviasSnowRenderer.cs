using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.Terrain
{
    // CPU displacement keeps colour and shadow geometry identical on DX and GL.
    // Only resident, changed 4x4-tile patches upload vertices; tracks live separately.
    internal sealed class DeviasSnowRenderer : IDisposable
    {
        private const int Cells = DeviasSnowField.Cells;
        private const int Row = Cells + 1;
        private const int Pad = DeviasSnowField.Pad;
        private const int Apron = DeviasSnowField.PatchSamples;
        private const float Step = DeviasSnowField.Spacing;
        // 3x3 Sobel over one cell: sum of weights (4) * distance between taps (2 cells).
        private const float NormalScale = 1f / (8f * Step);
        private const int MaxResidentPatches = 96;
        private const int MaxNewPatchesPerFrame = 2;
        private const double MaxPatchBuildMilliseconds = 2.0;
        // A footprint stamped this frame must show up at once, so freshly stamped
        // patches get a generous budget. Slow refreshes (decay, ambient) get a small
        // one so they can never pile up into a single-frame spike.
        private const int MaxStampRebuildsPerFrame = 8;
        private const int MaxRefreshRebuildsPerFrame = 2;
        // Below one 8-bit step the baked terrain light cannot change.
        private const float AmbientTolerance = 1f / 255f;

        private readonly GraphicsDevice _device;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly TerrainVisibilityManager _visibility;
        private readonly DeviasSnowField _field;
        private readonly float _depth;
        // Decay is linear, so a patch is refreshed once its tracks have moved by about a
        // quarter of a world unit: invisible steps, and far fewer uploads than a fixed 0.5 s.
        private readonly float _refreshInterval;
        private readonly Dictionary<(int X, int Y), Patch> _patches = new();
        private readonly Dictionary<(int X, int Y), Task<Patch>> _pendingPatches = new();
        private readonly List<(int X, int Y)> _completedPatches = new();
        private readonly Dictionary<WalkerObject, Track> _tracks = new();
        private readonly List<WalkerObject> _staleActors = new();
        private readonly List<TerrainBlock> _drawBlocks = new();
        private readonly List<Missing> _missing = new();
        private Vector2 _drawFocus;
        private readonly float[] _cuts = new float[Apron * Apron];
        private readonly float[] _delta = new float[Apron * Apron];
        private readonly SnowVertex[] _vertices = new SnowVertex[Row * Row];
        private readonly ushort[] _adaptiveIndices = new ushort[Cells * Cells * 6];
        private readonly bool[] _detailedBlocks = new bool[16 * 16];
        private BasicEffect _fallback;
        private Texture2D _materialTexture;
        private Task<Color[][]> _materialLevels;
        private bool _materialTextureReady;
        private Bindings _bindings;
        private int _boundBase, _boundOverlay;
        private double _now;
        private double _lastPrune;
        // Per-frame budgets are reset in Update, so they do not depend on who calls ResetMetrics.
        private int _frameNewPatches, _frameStampRebuilds, _frameRefreshRebuilds;
        private double _framePatchMilliseconds;
        public int DrawCalls { get; private set; }
        public int DrawnTriangles { get; private set; }
        public int VertexUploads { get; private set; }
        public int UploadedVertices { get; private set; }
        public int NewPatchBuilds { get; private set; }
        public double PatchBuildMilliseconds { get; private set; }
        public float AmbientLight { get; set; } = 0.25f;
        public long SurfaceVersion { get; private set; }

        private struct SnowVertex : IVertexType
        {
            public Vector3 Position;
            public Color Color;
            public Vector3 Normal;
            public Vector2 TextureCoordinate;
            public Color TerrainLight;
            public static readonly VertexDeclaration Declaration = new(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
                new VertexElement(16, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(36, VertexElementFormat.Color, VertexElementUsage.Color, 1));
            VertexDeclaration IVertexType.VertexDeclaration => Declaration;
        }

        private readonly record struct MaterialBatch(int Base, int Overlay, int StartIndex, int Triangles, bool Opaque);
        private readonly record struct Missing(int X, int Y, float DistanceSquared);

        // Effect parameters and techniques resolved once per effect instance instead of
        // by string on every patch and material batch.
        private sealed class Bindings
        {
            public readonly Effect Owner;
            public readonly EffectTechnique Snow, SnowBase, SnowOpaque, SnowOpaqueBase, Shadow;
            public readonly EffectParameter ProceduralUv, IsWater, Diffuse, Overlay, BaseScale,
                OverlayScale, OverlayEnabled, Ambient, OpaqueCaster, Material;

            public Bindings(Effect effect)
            {
                Owner = effect;
                Snow = effect.Techniques["DynamicLighting_Snow"];
                SnowBase = effect.Techniques["DynamicLighting_Snow_Base"];
                SnowOpaque = effect.Techniques["DynamicLighting_Snow_Opaque"];
                SnowOpaqueBase = effect.Techniques["DynamicLighting_Snow_Opaque_Base"];
                Shadow = effect.Techniques["ShadowCaster"];
                var p = effect.Parameters;
                ProceduralUv = p["UseProceduralTerrainUV"];
                IsWater = p["IsWaterTexture"];
                Diffuse = p["DiffuseTexture"];
                Overlay = p["SnowOverlayTexture"];
                BaseScale = p["SnowBaseUvScale"];
                OverlayScale = p["SnowOverlayUvScale"];
                OverlayEnabled = p["SnowOverlayEnabled"];
                Ambient = p["SnowAmbientLight"];
                OpaqueCaster = p["ShadowOpaqueCaster"]; // optional: only in the optimised shader
                Material = p["SnowMaterialTexture"];
            }
        }

        private sealed class Track
        {
            public Vector3 Position;
            public DeviasSnowFootsteps Footsteps;
            public double Seen;
        }

        private sealed class Patch : IDisposable
        {
            public float[] Ground = new float[Apron * Apron];
            public float[] Coverage = new float[Apron * Apron];
            public float[] Thickness = new float[Apron * Apron];
            public Color[] Light = new Color[Row * Row];
            public Vector2[] GroundSlope = new Vector2[Row * Row];
            public Color[] TerrainLight = new Color[Row * Row];
            public float[] SurfaceHeights = new float[Row * Row];
            public MaterialBatch[] Materials;
            public int[][] MaterialBlocks;
            public bool[] VisibleCells = new bool[Cells * Cells];
            public bool[] StaticDetailedBlocks = new bool[16 * 16];
            public bool[] DetailedBlocks = new bool[16 * 16];
            public bool HasDeformation;
            public int OpaqueDrawnFrame = -1;
            public ushort[] IndexData;
            public DynamicVertexBuffer Vertices;
            public IndexBuffer Indices;
            public int Triangles;
            public long Revision = -1;
            public double Built = -1;
            public double Seen;
            public float AmbientLight;
            public void Dispose() { Vertices?.Dispose(); Indices?.Dispose(); }
        }

        public DeviasSnowRenderer(GraphicsDevice device, TerrainData data, TerrainPhysics physics,
            TerrainVisibilityManager visibility, DeviasGroundSnowSettings settings)
        {
            _device = device;
            _data = data;
            _physics = physics;
            _visibility = visibility;
            _depth = float.IsFinite(settings.Depth) ? Math.Clamp(settings.Depth, 8f, 40f) : 24f;
            _field = new DeviasSnowField(float.IsFinite(settings.TrackLifetime) ? settings.TrackLifetime : 90f);
            _refreshInterval = Math.Clamp(0.25f * _field.Lifetime / _depth, 0.5f, 3f);
            _materialLevels = Task.Run(BuildMaterialLevels);
        }

        public void Update(GameTime time, WalkableWorldControl world)
        {
            _now = time.TotalGameTime.TotalSeconds;
            _frameNewPatches = _frameStampRebuilds = _frameRefreshRebuilds = 0;
            _framePatchMilliseconds = 0;
            float dt = (float)time.ElapsedGameTime.TotalSeconds;
            foreach (var obj in world.VisibleObjects)
            {
                if (obj is not WalkerObject actor || actor.Status != GameControlStatus.Ready ||
                    (actor is not PlayerObject && actor is not MonsterObject)) continue;
                RecordActor(actor, world, dt);
            }
            // The local walker can be outside the visible list during a camera transition.
            if (world.Walker != null && (!_tracks.TryGetValue(world.Walker, out var hero) || hero.Seen != _now))
                RecordActor(world.Walker, world, dt);

            if (_now - _lastPrune >= 1)
            {
                _lastPrune = _now;
                _field.Prune(_now);
                _staleActors.Clear();
                foreach (var pair in _tracks)
                    if (_now - pair.Value.Seen > 1) _staleActors.Add(pair.Key);
                foreach (var actor in _staleActors) _tracks.Remove(actor);
                // Keep meshes across camera turns. Evict only under the residency cap;
                // two-second expiry repeatedly rebuilt the same expensive patches.
            }
        }

        private void RecordActor(WalkerObject actor, WalkableWorldControl world, float dt)
        {
            Vector3 position = actor.Position;
            if (!float.IsFinite(position.X + position.Y + position.Z)) return;
            if (!_tracks.TryGetValue(actor, out var track))
            {
                _tracks.Add(actor, new Track { Position = position, Seen = _now,
                    Footsteps = new DeviasSnowFootsteps(actor.GetHashCode()) });
                return;
            }
            Vector3 old = track.Position;
            bool continuous = _now - track.Seen <= 0.25;
            track.Position = position;
            track.Seen = _now;
            bool grounded = actor.ExtraHeight <= 5 &&
                MathF.Abs(position.Z - _physics.RequestTerrainHeight(position.X, position.Y) - world.ExtraHeight) < 18;
            if (actor is PlayerObject player)
            {
                var flags = _physics.RequestTerrainFlag((int)(position.X / 100), (int)(position.Y / 100));
                grounded &= !player.IsDead && !(player.HasEquippedWings && (flags & TWFlags.SafeZone) == 0);
            }
            if (actor is MonsterObject monster) grounded &= !monster.IsDead;
            float dx = position.X - old.X, dy = position.Y - old.Y;
            if (!continuous || !DeviasSnowField.CanStampMovement(dx, dy, dt, grounded))
            {
                // A pause preserves the unfinished step; teleports/flight restart it.
                if (!continuous || !grounded || dx * dx + dy * dy > 0.01f)
                    track.Footsteps.Reset();
                return;
            }
            float scale = Math.Clamp(actor.Scale / 0.85f, 0.75f, 2.5f);
            track.Footsteps.StampMovement(_field, old.X, old.Y, position.X, position.Y, scale, _depth, _now);
        }

        private float CoverageAt(float x, float y)
        {
            if (x < 0 || y < 0 || x >= 25500 || y >= 25500) return 0;
            int tx = (int)(x / 100), ty = (int)(y / 100);
            // Never bridge a missing ground tile, water, a wall or a raised roof.
            if (IsExcluded(tx, ty))
                return 0;
            float u = x / 100 - tx, v = y / 100 - ty;
            float a = NodeCoverage(tx, ty), b = NodeCoverage(tx + 1, ty);
            float c = NodeCoverage(tx + 1, ty + 1), d = NodeCoverage(tx, ty + 1);
            float mask = u >= v ? (1 - u) * a + (u - v) * b + v * c : (1 - v) * a + u * c + (v - u) * d;
            float coverage = MathHelper.SmoothStep(0, 1, Math.Clamp((mask - 0.15f) / 0.85f, 0, 1));
            // Retreat into valid ground near blocked tiles, rather than dropping a
            // full-height sheet vertically at an attribute-grid boundary.
            float edgeDistance = 35;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if ((ox == 0 && oy == 0) || !IsExcluded(tx + ox, ty + oy)) continue;
                    float ex = ox < 0 ? u * 100 : ox > 0 ? (1 - u) * 100 : 0;
                    float ey = oy < 0 ? v * 100 : oy > 0 ? (1 - v) * 100 : 0;
                    edgeDistance = MathF.Min(edgeDistance, MathF.Sqrt(ex * ex + ey * ey));
                }
            return coverage * MathHelper.SmoothStep(0, 1, edgeDistance / 35);
        }

        private bool IsExcluded(int x, int y)
        {
            return (uint)x >= 255 || (uint)y >= 255 ||
                (_physics.RequestTerrainFlag(x, y) & (TWFlags.NoGround | TWFlags.Water | TWFlags.NoMove | TWFlags.Height)) != 0;
        }

        private float NodeCoverage(int x, int y)
        {
            int index = x + y * 256;
            float alpha = _data.Mapping.Alpha[index] / 255f;
            float baseSnow = _data.Mapping.Layer1[index] <= 1 ? 1 : 0;
            float upperSnow = _data.Mapping.Layer2[index] <= 1 ? 1 : 0;
            return MathHelper.Lerp(baseSnow, upperSnow, alpha);
        }

        // Runs on a worker thread: reads immutable terrain data only.
        private Patch CreatePatch(int cx, int cy)
        {
            var patch = new Patch();
            float ambient = AmbientLight;
            patch.AmbientLight = ambient;
            for (int y = -Pad; y <= Cells + Pad; y++)
                for (int x = -Pad; x <= Cells + Pad; x++)
                {
                    float wx = (cx * Cells + x) * Step, wy = (cy * Cells + y) * Step;
                    int i = x + Pad + (y + Pad) * Apron;
                    patch.Ground[i] = _physics.RequestTerrainHeight(wx, wy);
                    patch.Coverage[i] = CoverageAt(wx, wy);
                    patch.Thickness[i] = _depth + 2f * MathF.Sin((cx * Cells + x) * 0.075f) * MathF.Cos((cy * Cells + y) * 0.061f);
                    if (x >= 0 && x <= Cells && y >= 0 && y <= Cells)
                    {
                        patch.Light[x + y * Row] = SampleLight(wx, wy);
                        patch.TerrainLight[x + y * Row] = SampleTerrainLight(wx, wy, ambient);
                        // The original height map has one planar face per half tile.
                        // Snow scatters light across those facets instead of exposing the grid.
                        const float normalRadius = 100f;
                        patch.GroundSlope[x + y * Row] = new Vector2(
                            _physics.RequestTerrainHeight(wx - normalRadius, wy) - _physics.RequestTerrainHeight(wx + normalRadius, wy),
                            _physics.RequestTerrainHeight(wx, wy - normalRadius) - _physics.RequestTerrainHeight(wx, wy + normalRadius)) / (normalRadius * 2);
                    }
                }
            for (int y = 0; y < Cells; y++)
                for (int x = 0; x < Cells; x++)
                {
                    int mask = x + Pad + (y + Pad) * Apron;
                    patch.VisibleCells[x + y * Cells] = patch.Coverage[mask] + patch.Coverage[mask + 1] +
                        patch.Coverage[mask + Apron] + patch.Coverage[mask + Apron + 1] > 0;
                }
            // A roof or material edge only keeps its own 25-unit blocks detailed.
            // Opaque interiors and tapered edges are batched separately for both passes.
            var blockGroups = new Dictionary<(int Base, int Overlay, bool Opaque), List<int>>();
            for (int y = 0; y < Cells; y += 4)
                for (int x = 0; x < Cells; x += 4)
                {
                    bool fullCoverage = true;
                    for (int sy = y; sy <= y + 4 && fullCoverage; sy++)
                        for (int sx = x; sx <= x + 4; sx++)
                            if (patch.Coverage[sx + Pad + (sy + Pad) * Apron] < 0.999f)
                            { fullCoverage = false; break; }
                    int block = x / 4 + y / 4 * 16;
                    patch.StaticDetailedBlocks[block] = patch.DetailedBlocks[block] = !fullCoverage;
                    bool visible = false;
                    for (int sy = y; sy < y + 4 && !visible; sy++)
                        for (int sx = x; sx < x + 4; sx++)
                            if (patch.VisibleCells[sx + sy * Cells]) { visible = true; break; }
                    if (!visible) continue;
                    var material = GetMaterial(cx * 4 + x / 16, cy * 4 + y / 16);
                    var key = (material.Base, material.Overlay, fullCoverage);
                    if (!blockGroups.TryGetValue(key, out var blocks))
                        blockGroups.Add(key, blocks = new List<int>());
                    blocks.Add(block);
                }
            var materials = new List<MaterialBatch>(blockGroups.Count);
            patch.MaterialBlocks = new int[blockGroups.Count][];
            var indexData = new ushort[Cells * Cells * 6];
            int count = 0, groupIndex = 0;
            foreach (var group in blockGroups)
            {
                int[] blocks = group.Value.ToArray();
                patch.MaterialBlocks[groupIndex++] = blocks;
                int next = WriteAdaptiveIndices(indexData, count, blocks, patch.DetailedBlocks, patch.VisibleCells);
                materials.Add(new MaterialBatch(group.Key.Base, group.Key.Overlay, count, (next - count) / 3, group.Key.Opaque));
                count = next;
            }
            patch.Materials = materials.ToArray();
            patch.IndexData = new ushort[count];
            Array.Copy(indexData, patch.IndexData, count);
            patch.Triangles = count / 3;
            return patch;
        }

        private static bool IsDetailed(bool[] blocks, int x, int y)
        {
            return (uint)x >= 16 || (uint)y >= 16 || blocks[x + y * 16];
        }

        private static int WriteAdaptiveIndices(ushort[] indices, int count, int[] blocks, bool[] detailed, bool[] visibleCells = null)
        {
            foreach (int block in blocks)
            {
                int bx = block % 16, by = block / 16;
                int x = bx * 4, y = by * 4;
                if (detailed[block])
                {
                    for (int sy = y; sy < y + 4; sy++)
                        for (int sx = x; sx < x + 4; sx++)
                        {
                            if (visibleCells != null && !visibleCells[sx + sy * Cells]) continue;
                            ushort a = (ushort)(sx + sy * Row), b = (ushort)(a + 1);
                            ushort d = (ushort)(a + Row), c = (ushort)(d + 1);
                            indices[count++] = a; indices[count++] = b; indices[count++] = c;
                            indices[count++] = c; indices[count++] = d; indices[count++] = a;
                        }
                    continue;
                }
                ushort center = (ushort)(x + 2 + (y + 2) * Row);
                void Edge(int ax, int ay, int ex, int ey, bool dense)
                {
                    int segments = dense ? 4 : 1;
                    for (int s = 0; s < segments; s++)
                    {
                        indices[count++] = center;
                        indices[count++] = (ushort)(ax + (ex - ax) * s / segments + (ay + (ey - ay) * s / segments) * Row);
                        indices[count++] = (ushort)(ax + (ex - ax) * (s + 1) / segments + (ay + (ey - ay) * (s + 1) / segments) * Row);
                    }
                }
                // Match both fine neighbours and every sample along patch boundaries.
                Edge(x, y, x + 4, y, IsDetailed(detailed, bx, by - 1));
                Edge(x + 4, y, x + 4, y + 4, IsDetailed(detailed, bx + 1, by));
                Edge(x + 4, y + 4, x, y + 4, IsDetailed(detailed, bx, by + 1));
                Edge(x, y + 4, x, y, IsDetailed(detailed, bx - 1, by));
            }
            return count;
        }

        private void RefreshAdaptiveTopology(Patch patch)
        {
            if (!FillDetailedBlocks(_cuts, patch.DetailedBlocks, _detailedBlocks, patch.StaticDetailedBlocks)) return;
            Array.Copy(_detailedBlocks, patch.DetailedBlocks, _detailedBlocks.Length);
            int count = 0;
            for (int group = 0; group < patch.Materials.Length; group++)
            {
                int next = WriteAdaptiveIndices(_adaptiveIndices, count, patch.MaterialBlocks[group], patch.DetailedBlocks, patch.VisibleCells);
                patch.Materials[group] = patch.Materials[group] with { StartIndex = count, Triangles = (next - count) / 3 };
                count = next;
            }
            patch.Indices.SetData(_adaptiveIndices, 0, count);
            patch.Triangles = count / 3;
        }

        private static bool FillDetailedBlocks(float[] cuts, bool[] current, bool[] next, bool[] staticDetailed = null)
        {
            bool changed = false;
            for (int by = 0; by < 16; by++)
                for (int bx = 0; bx < 16; bx++)
                {
                    bool detailed = staticDetailed != null && staticDetailed[bx + by * 16];
                    // Include the Sobel ring, so a bank just outside the block retains
                    // the fine normals too. Read actual samples, not neighbouring chunks.
                    for (int y = by * 4 - Pad; y <= by * 4 + 4 + Pad && !detailed; y++)
                        for (int x = bx * 4 - Pad; x <= bx * 4 + 4 + Pad; x++)
                            if (cuts[x + Pad + (y + Pad) * Apron] > 0)
                            { detailed = true; break; }
                    int block = bx + by * 16;
                    next[block] = detailed;
                    changed |= detailed != current[block];
                }
            return changed;
        }

        private (int Base, int Overlay) GetMaterial(int x, int y)
        {
            int i = x + y * 256;
            byte a = _data.Mapping.Alpha[i], b = _data.Mapping.Alpha[i + 1];
            byte c = _data.Mapping.Alpha[i + 257], d = _data.Mapping.Alpha[i + 256];
            // Match TerrainRenderer's opaque-layer shortcut, including texture alpha.
            if ((a & b & c & d) == 255) return (_data.Mapping.Layer2[i], -1);
            return (_data.Mapping.Layer1[i], (a | b | c | d) != 0 ? _data.Mapping.Layer2[i] : -1);
        }

        private Color SampleTerrainLight(float x, float y, float ambientLight)
        {
            float tx = Math.Clamp(x / 100, 0, 254.999f), ty = Math.Clamp(y / 100, 0, 254.999f);
            int ix = (int)tx, iy = (int)ty, i = ix + iy * 256;
            float u = tx - ix, v = ty - iy;
            Vector4 Node(int index)
            {
                Color source = _data.FinalLightMap[index];
                float ambient = ambientLight * 255;
                return new Vector4((byte)MathF.Min(source.R + ambient, 255),
                    (byte)MathF.Min(source.G + ambient, 255), (byte)MathF.Min(source.B + ambient, 255),
                    _data.Mapping.Alpha[index]) / 255f;
            }
            Vector4 a = Node(i), b = Node(i + 1), c = Node(i + 257), d = Node(i + 256);
            return new Color(u >= v ? (1 - u) * a + (u - v) * b + v * c : (1 - v) * a + u * c + (v - u) * d);
        }

        private Color SampleLight(float x, float y)
        {
            float tx = Math.Clamp(x / 100, 0, 254.999f), ty = Math.Clamp(y / 100, 0, 254.999f);
            int ix = (int)tx, iy = (int)ty, i = ix + iy * 256;
            float u = MathHelper.SmoothStep(0, 1, tx - ix), v = MathHelper.SmoothStep(0, 1, ty - iy);
            // FinalLightMap already contains the coarse terrain-face normals. Using it
            // would shade the old tile grid a second time on top of our snow normals.
            var light = _data.LightData ?? _data.FinalLightMap;
            Vector3 a = light[i].ToVector3(), b = light[i + 1].ToVector3();
            Vector3 c = light[i + 257].ToVector3(), d = light[i + 256].ToVector3();
            return new Color(Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v));
        }

        private void Rebuild(Patch patch, int cx, int cy, long revision)
        {
            // One pass over the chunks instead of a dictionary lookup per sample.
            bool hadDeformation = patch.HasDeformation;
            _field.FillDepth(cx, cy, _cuts, _now);
            patch.HasDeformation = false;
            for (int i = 0; i < _delta.Length; i++)
            {
                patch.HasDeformation |= _cuts[i] > 0;
                float thickness = MathF.Max(1f, patch.Thickness[i] - _cuts[i]);
                _delta[i] = patch.Coverage[i] * (thickness + 0.2f) - 0.1f;
            }
            RefreshAdaptiveTopology(patch);

            float ambient = AmbientLight;
            bool relight = MathF.Abs(patch.AmbientLight - ambient) >= AmbientTolerance;
            float invDepth = 1f / _depth;
            for (int y = 0; y <= Cells; y++)
                for (int x = 0; x <= Cells; x++)
                {
                    int i = x + Pad + (y + Pad) * Apron;
                    int vertex = x + y * Row;
                    // Sobel over the deformation of the 3x3 neighbourhood. Unlike the old
                    // two-tap difference across four cells (25 units, wider than a footprint),
                    // it keeps the banks of a print sharp and has no grid-axis bias. The wide
                    // terrain slope stays in GroundSlope, so the original tile facets are hidden.
                    float a00 = _delta[i - Apron - 1], a10 = _delta[i - Apron], a20 = _delta[i - Apron + 1];
                    float a01 = _delta[i - 1], a21 = _delta[i + 1];
                    float a02 = _delta[i + Apron - 1], a12 = _delta[i + Apron], a22 = _delta[i + Apron + 1];
                    Vector2 slope = patch.GroundSlope[vertex];
                    slope.X -= ((a20 + 2f * a21 + a22) - (a00 + 2f * a01 + a02)) * NormalScale;
                    slope.Y -= ((a02 + 2f * a12 + a22) - (a00 + 2f * a10 + a20)) * NormalScale;
                    Vector3 normal = Vector3.Normalize(new Vector3(slope, 1));
                    Color light = patch.Light[vertex];
                    light.A = (byte)(patch.Coverage[i] * 255);
                    Vector3 position = new Vector3((cx * Cells + x) * Step, (cy * Cells + y) * Step,
                        patch.Ground[i] + _delta[i]);
                    patch.SurfaceHeights[vertex] = position.Z;
                    if (relight)
                        patch.TerrainLight[vertex] = SampleTerrainLight(position.X, position.Y, ambient);
                    _vertices[vertex] = new SnowVertex { Position = position, Color = light, Normal = normal,
                        TextureCoordinate = new Vector2(_cuts[i] * invDepth, patch.Coverage[i]),
                        TerrainLight = patch.TerrainLight[vertex] };
                }
            patch.Vertices.SetData(_vertices, 0, _vertices.Length, SetDataOptions.Discard);
            VertexUploads++;
            UploadedVertices += _vertices.Length;
            if (patch.Revision != revision || patch.Built < 0 || hadDeformation || patch.HasDeformation)
                SurfaceVersion++;
            patch.Revision = revision;
            patch.Built = _now;
            if (relight) patch.AmbientLight = ambient;
        }

        public bool TryGetSurfaceHeight(float x, float y, out float height)
        {
            height = 0;
            if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 || x >= 25500 || y >= 25500)
                return false;
            float gx = x / Step, gy = y / Step;
            int ix = (int)gx, iy = (int)gy;
            if (!_patches.TryGetValue((ix / Cells, iy / Cells), out var patch) || patch.Built < 0)
                return false;

            height = SampleSurfaceHeight(patch.SurfaceHeights,
                patch.DetailedBlocks,
                gx - ix / Cells * Cells, gy - iy / Cells * Cells);
            return true;
        }

        private static float SampleSurfaceHeight(float[] h, bool[] detailed, float x, float y)
        {
            int ix = (int)x, iy = (int)y;
            int bx = ix / 4, by = iy / 4;
            if (detailed == null || detailed[bx + by * 16])
            {
                int i = ix + iy * Row;
                float tx = x - ix, ty = y - iy;
                return tx >= ty
                    ? (1 - tx) * h[i] + (tx - ty) * h[i + 1] + ty * h[i + Row + 1]
                    : (1 - ty) * h[i] + tx * h[i + Row + 1] + (ty - tx) * h[i + Row];
            }

            float u = x - bx * 4, v = y - by * 4;
            float dx = u - 2, dy = v - 2;
            int center = bx * 4 + 2 + (by * 4 + 2) * Row;
            if (MathF.Abs(dx) + MathF.Abs(dy) < 1e-6f) return h[center];
            int ax, ay, ex, ey;
            bool dense;
            float along;
            if (MathF.Abs(dx) > MathF.Abs(dy))
            {
                float edgeY = 2 + dy * (2 / MathF.Abs(dx));
                if (dx > 0)
                { ax = 4; ay = 0; ex = 4; ey = 4; along = edgeY; dense = IsDetailed(detailed, bx + 1, by); }
                else
                { ax = 0; ay = 4; ex = 0; ey = 0; along = 4 - edgeY; dense = IsDetailed(detailed, bx - 1, by); }
            }
            else
            {
                float edgeX = 2 + dx * (2 / MathF.Abs(dy));
                if (dy > 0)
                { ax = 4; ay = 4; ex = 0; ey = 4; along = 4 - edgeX; dense = IsDetailed(detailed, bx, by + 1); }
                else
                { ax = 0; ay = 0; ex = 4; ey = 0; along = edgeX; dense = IsDetailed(detailed, bx, by - 1); }
            }
            if (dense)
            {
                int segment = Math.Clamp((int)MathF.Floor(along), 0, 3);
                int sx = (ex - ax) / 4, sy = (ey - ay) / 4;
                ax += sx * segment; ay += sy * segment;
                ex = ax + sx; ey = ay + sy;
            }
            float aX = ax - 2, aY = ay - 2, bX = ex - 2, bY = ey - 2;
            float inverseArea = 1f / (aX * bY - aY * bX);
            float aWeight = (dx * bY - dy * bX) * inverseArea;
            float bWeight = (aX * dy - aY * dx) * inverseArea;
            int a = bx * 4 + ax + (by * 4 + ay) * Row;
            int b = bx * 4 + ex + (by * 4 + ey) * Row;
            return (1 - aWeight - bWeight) * h[center] + aWeight * h[a] + bWeight * h[b];
        }

        public void ResetMetrics()
        {
            DrawCalls = DrawnTriangles = VertexUploads = UploadedVertices = 0;
            NewPatchBuilds = 0;
            PatchBuildMilliseconds = 0;
        }

        private static Color[][] BuildMaterialLevels()
        {
            const int hashSize = 256;
            var values = new float[hashSize * hashSize];
            for (int y = 0; y < hashSize; y++)
                for (int x = 0; x < hashSize; x++)
                {
                    float qx = x * 0.1031f, qy = y * 0.1031f;
                    qx -= MathF.Floor(qx);
                    qy -= MathF.Floor(qy);
                    float dot = qx * (qy + 33.33f) + qy * (qx + 33.33f) + qx * (qx + 33.33f);
                    qx += dot;
                    qy += dot;
                    float hash = (qx + qy) * qx;
                    hash -= MathF.Floor(hash);
                    values[x + y * hashSize] = hash;
                }

            Vector3 Noise(float x, float y, int period)
            {
                int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
                float fx = x - ix, fy = y - iy;
                int x0 = ix % period, y0 = iy % period;
                int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
                float a = values[x0 + y0 * hashSize], b = values[x1 + y0 * hashSize];
                float c = values[x0 + y1 * hashSize], d = values[x1 + y1 * hashSize];
                float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
                float du = 6 * fx * (1 - fx), dv = 6 * fy * (1 - fy);
                return new Vector3(MathHelper.Lerp(MathHelper.Lerp(a, b, u), MathHelper.Lerp(c, d, u), v),
                    du * MathHelper.Lerp(b - a, d - c, v), dv * MathHelper.Lerp(c - a, d - b, u));
            }

            // Integer octave periods make the whole material seamless over 1024 world
            // units. One texel per world unit retains the original 4-unit grain detail.
            const int size = 1024;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    Vector3 powder = Noise(u * 75, v * 75, 75);
                    Vector3 grain = Noise(u * 236 + 17.3f, v * 236 + 9.1f, 236);
                    float drift = Noise(u * 14 + 3.7f, v * 14 + 21.5f, 14).X;
                    float slopeX = powder.Y * 0.22f + grain.Y * 0.19f;
                    float slopeY = powder.Z * 0.22f + grain.Z * 0.19f;
                    float albedo = (powder.X - 0.5f) * 0.10f + (drift - 0.5f) * 0.04f + (grain.X - 0.5f) * 0.05f;
                    pixels[x + y * size] = new Color(new Vector4(slopeX / 1.25f + 0.5f,
                        slopeY / 1.25f + 0.5f, powder.X, albedo * 5f + 0.5f));
                }
            var levels = new Color[11][];
            levels[0] = pixels;
            for (int level = 1, width = size; level < levels.Length; level++, width /= 2)
            {
                int nextWidth = width / 2;
                var previous = levels[level - 1];
                var next = new Color[nextWidth * nextWidth];
                for (int y = 0; y < nextWidth; y++)
                    for (int x = 0; x < nextWidth; x++)
                    {
                        int i = x * 2 + y * 2 * width;
                        Color a = previous[i], b = previous[i + 1], c = previous[i + width], d = previous[i + width + 1];
                        next[x + y * nextWidth] = new Color((byte)((a.R + b.R + c.R + d.R + 2) / 4),
                            (byte)((a.G + b.G + c.G + d.G + 2) / 4), (byte)((a.B + b.B + c.B + d.B + 2) / 4),
                            (byte)((a.A + b.A + c.A + d.A + 2) / 4));
                    }
                levels[level] = next;
            }
            return levels;
        }

        private Texture2D GetMaterialTexture()
        {
            if (_materialTextureReady) return _materialTexture;
            if (!_materialLevels.IsCompleted)
            {
                // Preparing the CPU texture must never stall the first render frame.
                if (_materialTexture == null)
                {
                    _materialTexture = new Texture2D(_device, 1, 1, false, SurfaceFormat.Color);
                    _materialTexture.SetData(new[] { new Color(128, 128, 128, 128) });
                }
                return _materialTexture;
            }
            var levels = _materialLevels.GetAwaiter().GetResult();
            var texture = new Texture2D(_device, 1024, 1024, true, SurfaceFormat.Color);
            for (int level = 0; level < levels.Length; level++)
                texture.SetData(level, null, levels[level], 0, levels[level].Length);
            _materialTexture?.Dispose();
            _materialTexture = texture;
            _materialTextureReady = true;
            _materialLevels = null;
            return texture;
        }

        public void Draw(Effect effect, bool shadow = false, bool opaqueOnly = false)
        {
            var blend = _device.BlendState;
            var depth = _device.DepthStencilState;
            var raster = _device.RasterizerState;
            EffectTechnique previousTechnique = effect?.CurrentTechnique;
            Bindings bind = null;
            float proceduralUv = 0, opaqueCaster = 0;
            if (effect != null)
            {
                bind = _bindings != null && _bindings.Owner == effect ? _bindings : (_bindings = new Bindings(effect));
                proceduralUv = bind.ProceduralUv?.GetValueSingle() ?? 0;
                opaqueCaster = bind.OpaqueCaster?.GetValueSingle() ?? 0;
            }
            try
            {
                _device.BlendState = shadow || opaqueOnly ? BlendState.Opaque : BlendState.NonPremultiplied;
                _device.DepthStencilState = DepthStencilState.Default;
                _device.RasterizerState = RasterizerState.CullNone;
                _boundBase = _boundOverlay = int.MinValue;
                if (bind != null)
                {
                    effect.CurrentTechnique = shadow ? bind.Shadow : bind.Snow;
                    bind.ProceduralUv?.SetValue(0f);
                    bind.IsWater?.SetValue(0f);
                    bind.Diffuse?.SetValue(GraphicsManager.Instance.Pixel);
                    bind.Ambient?.SetValue(AmbientLight);
                    if (!shadow && bind.Material != null) bind.Material.SetValue(GetMaterialTexture());
                    // Snow has no alpha cut-outs in the caster pass: skip the texture fetch.
                    if (shadow) bind.OpaqueCaster?.SetValue(1f);
                }
                else
                {
                    _fallback ??= new BasicEffect(_device) { VertexColorEnabled = true, LightingEnabled = true };
                    _fallback.World = Matrix.Identity;
                    _fallback.View = Camera.Instance.View;
                    _fallback.Projection = Camera.Instance.Projection;
                    _fallback.AmbientLightColor = new Vector3(0.6f);
                    _fallback.DirectionalLight0.Enabled = true;
                    _fallback.DirectionalLight0.Direction = Vector3.Normalize(Constants.SUN_DIRECTION);
                    _fallback.DirectionalLight0.DiffuseColor = new Vector3(0.6f);
                    effect = _fallback;
                }

                _drawBlocks.Clear();
                _drawBlocks.AddRange(_visibility.VisibleBlocks);
                _drawFocus = new Vector2(Camera.Instance.Target.X, Camera.Instance.Target.Y);
                CompletePatchBuilds();
                _missing.Clear();
                // Draw order does not matter (patches do not overlap), so there is no
                // per-frame sort: only the nearest missing patches are picked below.
                foreach (var block in _drawBlocks)
                {
                    if (!block.IsVisible) continue;
                    var key = (block.Xi / 4, block.Yi / 4);
                    if (!_patches.TryGetValue(key, out var patch))
                    {
                        if (!_pendingPatches.ContainsKey(key))
                            _missing.Add(new Missing(key.Item1, key.Item2, DistanceSquared(block)));
                        continue;
                    }
                    patch.Seen = _now;
                    if (patch.Triangles == 0) continue;
                    RefreshPatch(patch, key.Item1, key.Item2);
                    _device.SetVertexBuffer(patch.Vertices);
                    _device.Indices = patch.Indices;
                    if (!shadow)
                    {
                        foreach (var batch in patch.Materials)
                        {
                            if (opaqueOnly && !batch.Opaque) continue;
                            if (!opaqueOnly && batch.Opaque && patch.OpaqueDrawnFrame == MuGame.FrameIndex) continue;
                            _device.BlendState = batch.Opaque ? BlendState.Opaque : BlendState.NonPremultiplied;
                            if (bind != null) BindMaterial(bind, batch, batch.Opaque);
                            foreach (var pass in effect.CurrentTechnique.Passes)
                            {
                                pass.Apply();
                                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, batch.StartIndex, batch.Triangles);
                                DrawCalls++; DrawnTriangles += batch.Triangles;
                            }
                        }
                    }
                    else
                    {
                        foreach (var pass in effect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, patch.Triangles);
                        }
                    }
                    if (opaqueOnly) patch.OpaqueDrawnFrame = MuGame.FrameIndex;
                }
                SchedulePatchBuilds();
            }
            finally
            {
                if (previousTechnique != null)
                {
                    effect.CurrentTechnique = previousTechnique;
                    bind.ProceduralUv?.SetValue(proceduralUv);
                    if (shadow) bind.OpaqueCaster?.SetValue(opaqueCaster);
                }
                _device.SetVertexBuffer(null);
                _device.Indices = null;
                _device.BlendState = blend;
                _device.DepthStencilState = depth;
                _device.RasterizerState = raster;
            }
        }

        // Stamped patches are rebuilt promptly; decay and ambient refreshes are throttled.
        private void RefreshPatch(Patch patch, int px, int py)
        {
            long revision = _field.GetRevision(px, py);
            if (patch.Revision != revision)
            {
                if (_frameStampRebuilds >= MaxStampRebuildsPerFrame) return; // retried next frame
                _frameStampRebuilds++;
                Rebuild(patch, px, py, revision);
                return;
            }
            bool relight = MathF.Abs(patch.AmbientLight - AmbientLight) >= AmbientTolerance;
            bool aged = _now - patch.Built >= _refreshInterval && patch.HasDeformation;
            if ((relight || aged) && _frameRefreshRebuilds < MaxRefreshRebuildsPerFrame)
            {
                _frameRefreshRebuilds++;
                Rebuild(patch, px, py, revision);
            }
        }

        private float DistanceSquared(TerrainBlock block)
        {
            float x = block.Xi * 100 + 200 - _drawFocus.X, y = block.Yi * 100 + 200 - _drawFocus.Y;
            return x * x + y * y;
        }

        // Terrain inputs are immutable after world loading. All height, mask, normal and
        // index preparation runs off the render thread; graphics resources and the mutable
        // footprint field stay here. Nearest missing patches go first.
        private void SchedulePatchBuilds()
        {
            while (_missing.Count > 0 && _pendingPatches.Count < MaxNewPatchesPerFrame)
            {
                int nearest = 0;
                for (int i = 1; i < _missing.Count; i++)
                    if (_missing[i].DistanceSquared < _missing[nearest].DistanceSquared) nearest = i;
                var missing = _missing[nearest];
                _missing[nearest] = _missing[_missing.Count - 1];
                _missing.RemoveAt(_missing.Count - 1);
                var key = (missing.X, missing.Y);
                if (_pendingPatches.ContainsKey(key)) continue;
                // At the residency cap with everything on screen there is nowhere to put a new
                // patch. Previously the job was built, thrown away and restarted every frame.
                if (_patches.Count + _pendingPatches.Count >= MaxResidentPatches && !EvictUnusedPatch()) break;
                _pendingPatches.Add(key, Task.Run(() => CreatePatch(key.Item1, key.Item2)));
            }
        }

        private void CompletePatchBuilds()
        {
            _completedPatches.Clear();
            foreach (var pair in _pendingPatches)
                if (pair.Value.IsCompleted) _completedPatches.Add(pair.Key);
            foreach (var key in _completedPatches)
            {
                if (_frameNewPatches >= MaxNewPatchesPerFrame || _framePatchMilliseconds >= MaxPatchBuildMilliseconds) break;
                Task<Patch> task = _pendingPatches[key];
                _pendingPatches.Remove(key);
                Patch patch = task.GetAwaiter().GetResult(); // completed only; surface preparation failures
                if (_patches.Count >= MaxResidentPatches && !EvictUnusedPatch()) continue;
                long started = Stopwatch.GetTimestamp();
                if (patch.Triangles > 0)
                {
                    patch.Indices = new IndexBuffer(_device, IndexElementSize.SixteenBits, Cells * Cells * 6, BufferUsage.WriteOnly);
                    patch.Indices.SetData(patch.IndexData);
                    patch.Vertices = new DynamicVertexBuffer(_device, SnowVertex.Declaration, Row * Row, BufferUsage.WriteOnly);
                    Rebuild(patch, key.X, key.Y, _field.GetRevision(key.X, key.Y));
                }
                patch.IndexData = null;
                patch.Seen = _now;
                _patches.Add(key, patch);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _frameNewPatches++;
                _framePatchMilliseconds += elapsed;
                NewPatchBuilds++;
                PatchBuildMilliseconds += elapsed;
            }
        }

        private void BindMaterial(Bindings bind, MaterialBatch batch, bool opaque)
        {
            EffectTechnique technique = opaque
                ? (batch.Overlay >= 0 ? bind.SnowOpaque : bind.SnowOpaqueBase)
                : (batch.Overlay >= 0 ? bind.Snow : bind.SnowBase);
            if (bind.Owner.CurrentTechnique != technique) bind.Owner.CurrentTechnique = technique;
            Texture2D Texture(int index) => _data.Textures != null && (uint)index < _data.Textures.Length &&
                _data.Textures[index] != null ? _data.Textures[index] : GraphicsManager.Instance.Pixel;
            // Only touch parameters that actually change between consecutive batches.
            if (batch.Base != _boundBase)
            {
                var primary = Texture(batch.Base);
                bind.Diffuse?.SetValue(primary);
                bind.BaseScale?.SetValue(new Vector2(0.64f / primary.Width, 0.64f / primary.Height));
                _boundBase = batch.Base;
            }
            if (batch.Overlay != _boundOverlay)
            {
                var overlay = Texture(batch.Overlay);
                bind.Overlay?.SetValue(overlay);
                bind.OverlayScale?.SetValue(new Vector2(0.64f / overlay.Width, 0.64f / overlay.Height));
                bind.OverlayEnabled?.SetValue(batch.Overlay >= 0 ? 1f : 0f);
                _boundOverlay = batch.Overlay;
            }
        }

        private bool IsInView((int X, int Y) key)
        {
            foreach (var block in _drawBlocks)
                if (block.Xi / 4 == key.X && block.Yi / 4 == key.Y) return true;
            return false;
        }

        // Frees one slot by dropping the least recently drawn patch that is not on screen.
        private bool EvictUnusedPatch()
        {
            bool found = false;
            (int X, int Y) oldest = default;
            double oldestSeen = double.MaxValue;
            foreach (var pair in _patches)
            {
                if (pair.Value.Seen >= oldestSeen || IsInView(pair.Key)) continue;
                oldest = pair.Key;
                oldestSeen = pair.Value.Seen;
                found = true;
            }
            if (!found) return false;
            _patches[oldest].Dispose();
            _patches.Remove(oldest);
            SurfaceVersion++;
            return true;
        }

        public void Dispose()
        {
            foreach (var patch in _patches.Values) patch.Dispose();
            _patches.Clear();
            _pendingPatches.Clear(); // pending jobs own CPU arrays only; no GPU cleanup is required
            _tracks.Clear();
            _fallback?.Dispose();
            _materialTexture?.Dispose();
        }
    }
}
