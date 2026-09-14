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
        private const int Apron = Cells + 5;
        private const float Step = DeviasSnowField.Spacing;
        private const int MaxResidentPatches = 96;
        private const int MaxNewPatchesPerFrame = 2;
        private readonly GraphicsDevice _device;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly TerrainVisibilityManager _visibility;
        private readonly DeviasSnowField _field;
        private readonly float _depth;
        private readonly Dictionary<(int X, int Y), Patch> _patches = new();
        private readonly Dictionary<(int X, int Y), Task<Patch>> _pendingPatches = new();
        private readonly List<(int X, int Y)> _completedPatches = new();
        private readonly Dictionary<WalkerObject, Track> _tracks = new();
        private readonly List<WalkerObject> _staleActors = new();
        private readonly List<(int X, int Y)> _stalePatches = new();
        private readonly List<TerrainBlock> _drawBlocks = new();
        private Vector2 _drawFocus;
        private readonly float[] _heights = new float[Apron * Apron];
        private readonly SnowVertex[] _vertices = new SnowVertex[Row * Row];
        private BasicEffect _fallback;
        private double _now;
        private double _lastPrune;
        public int DrawCalls { get; private set; }
        public int DrawnTriangles { get; private set; }
        public int VertexUploads { get; private set; }
        public int UploadedVertices { get; private set; }
        public int NewPatchBuilds { get; private set; }
        public double PatchBuildMilliseconds { get; private set; }
        public float AmbientLight { get; set; } = 0.25f;

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

        private readonly record struct MaterialBatch(int Base, int Overlay, int StartIndex, int Triangles);

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
            public MaterialBatch[] Materials;
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
        }

        public void Update(GameTime time, WalkableWorldControl world)
        {
            _now = time.TotalGameTime.TotalSeconds;
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

        private Patch CreatePatch(int cx, int cy)
        {
            var patch = new Patch();
            float ambient = AmbientLight;
            patch.AmbientLight = ambient;
            for (int y = -2; y <= Cells + 2; y++)
                for (int x = -2; x <= Cells + 2; x++)
                {
                    float wx = (cx * Cells + x) * Step, wy = (cy * Cells + y) * Step;
                    int i = x + 2 + (y + 2) * Apron;
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
            var groups = new Dictionary<(int Base, int Overlay), List<ushort>>();
            for (int y = 0; y < Cells; y++)
                for (int x = 0; x < Cells; x++)
                {
                    int mask = x + 2 + (y + 2) * Apron;
                    if (patch.Coverage[mask] + patch.Coverage[mask + 1] +
                        patch.Coverage[mask + Apron] + patch.Coverage[mask + Apron + 1] <= 0) continue;
                    ushort a = (ushort)(x + y * Row), b = (ushort)(a + 1), d = (ushort)(a + Row), c = (ushort)(d + 1);
                    var material = GetMaterial(cx * 4 + x / 16, cy * 4 + y / 16);
                    if (!groups.TryGetValue(material, out var indices))
                        groups.Add(material, indices = new List<ushort>());
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    indices.Add(c); indices.Add(d); indices.Add(a);
                }
            var allIndices = new List<ushort>(Cells * Cells * 6);
            var materials = new List<MaterialBatch>(groups.Count);
            foreach (var group in groups)
            {
                materials.Add(new MaterialBatch(group.Key.Base, group.Key.Overlay, allIndices.Count, group.Value.Count / 3));
                allIndices.AddRange(group.Value);
            }
            patch.Materials = materials.ToArray();
            patch.Triangles = allIndices.Count / 3;
            patch.IndexData = allIndices.ToArray();
            return patch;
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
            for (int y = -2; y <= Cells + 2; y++)
                for (int x = -2; x <= Cells + 2; x++)
                {
                    int gx = cx * Cells + x, gy = cy * Cells + y;
                    int i = x + 2 + (y + 2) * Apron;
                    float cut = _field.SampleDepth(gx, gy, _now);
                    float thickness = MathF.Max(1f, patch.Thickness[i] - cut);
                    _heights[i] = patch.Ground[i] + patch.Coverage[i] * (thickness + 0.2f) - 0.1f;
                }
            for (int y = 0; y <= Cells; y++)
                for (int x = 0; x <= Cells; x++)
                {
                    int i = x + 2 + (y + 2) * Apron;
                    // Smooth the terrain at tile scale, retaining the actual small-scale
                    // snow deformation gradient so footprints keep their steep banks.
                    Vector2 slope = patch.GroundSlope[x + y * Row];
                    slope.X += ((_heights[i - 2] - patch.Ground[i - 2]) -
                        (_heights[i + 2] - patch.Ground[i + 2])) / (Step * 4);
                    slope.Y += ((_heights[i - Apron * 2] - patch.Ground[i - Apron * 2]) -
                        (_heights[i + Apron * 2] - patch.Ground[i + Apron * 2])) / (Step * 4);
                    Vector3 normal = Vector3.Normalize(new Vector3(slope, 1));
                    Color light = patch.Light[x + y * Row];
                    light.A = (byte)(patch.Coverage[i] * 255);
                    float cut = _field.SampleDepth(cx * Cells + x, cy * Cells + y, _now) / _depth;
                    int vertex = x + y * Row;
                    Vector3 position = new Vector3((cx * Cells + x) * Step, (cy * Cells + y) * Step, _heights[i]);
                    if (patch.AmbientLight != AmbientLight)
                        patch.TerrainLight[vertex] = SampleTerrainLight(position.X, position.Y, AmbientLight);
                    _vertices[vertex] = new SnowVertex { Position = position, Color = light, Normal = normal,
                        TextureCoordinate = new Vector2(cut, patch.Coverage[i]), TerrainLight = patch.TerrainLight[vertex] };
                }
            patch.Vertices.SetData(_vertices, 0, _vertices.Length, SetDataOptions.Discard);
            VertexUploads++;
            UploadedVertices += _vertices.Length;
            patch.Revision = revision;
            patch.Built = _now;
            patch.AmbientLight = AmbientLight;
        }

        public void ResetMetrics()
        {
            DrawCalls = DrawnTriangles = VertexUploads = UploadedVertices = 0;
            NewPatchBuilds = 0;
            PatchBuildMilliseconds = 0;
        }

        public void Draw(Effect effect, bool shadow = false)
        {
            var blend = _device.BlendState;
            var depth = _device.DepthStencilState;
            var raster = _device.RasterizerState;
            EffectTechnique previousTechnique = effect?.CurrentTechnique;
            float proceduralUv = effect?.Parameters["UseProceduralTerrainUV"]?.GetValueSingle() ?? 0;
            try
            {
                _device.BlendState = shadow ? BlendState.Opaque : BlendState.NonPremultiplied;
                _device.DepthStencilState = DepthStencilState.Default;
                _device.RasterizerState = RasterizerState.CullNone;
                if (effect != null)
                {
                    effect.CurrentTechnique = effect.Techniques[shadow ? "ShadowCaster" : "DynamicLighting_Snow"];
                    effect.Parameters["UseProceduralTerrainUV"]?.SetValue(0f);
                    effect.Parameters["IsWaterTexture"]?.SetValue(0f);
                    effect.Parameters["DiffuseTexture"]?.SetValue(GraphicsManager.Instance.Pixel);
                    effect.Parameters["SnowAmbientLight"]?.SetValue(AmbientLight);
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
                _drawBlocks.Sort(CompareDistance);
                CompletePatchBuilds();
                foreach (var block in _drawBlocks)
                {
                    if (!block.IsVisible) continue;
                    var key = (block.Xi / 4, block.Yi / 4);
                    if (!_patches.TryGetValue(key, out var patch))
                    {
                        // Terrain inputs are immutable after world loading. All height,
                        // mask, normal and index preparation runs off the render thread;
                        // graphics resources and the mutable footprint field stay here.
                        if (_pendingPatches.Count < MaxNewPatchesPerFrame && !_pendingPatches.ContainsKey(key))
                            _pendingPatches.Add(key, Task.Run(() => CreatePatch(key.Item1, key.Item2)));
                        continue;
                    }
                    patch.Seen = _now;
                    if (patch.Triangles == 0) continue;
                    long revision = _field.GetRevision(key.Item1, key.Item2);
                    if (patch.Revision != revision || patch.AmbientLight != AmbientLight || (_field.HasTracks(key.Item1, key.Item2) && _now - patch.Built >= 0.5))
                        Rebuild(patch, key.Item1, key.Item2, revision);
                    _device.SetVertexBuffer(patch.Vertices);
                    _device.Indices = patch.Indices;
                    if (!shadow && effect != _fallback)
                    {
                        foreach (var batch in patch.Materials)
                        {
                            BindMaterial(effect, batch);
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
                            if (!shadow) { DrawCalls++; DrawnTriangles += patch.Triangles; }
                        }
                    }
                }
            }
            finally
            {
                if (previousTechnique != null)
                {
                    effect.CurrentTechnique = previousTechnique;
                    effect.Parameters["UseProceduralTerrainUV"]?.SetValue(proceduralUv);
                }
                _device.SetVertexBuffer(null);
                _device.Indices = null;
                _device.BlendState = blend;
                _device.DepthStencilState = depth;
                _device.RasterizerState = raster;
            }
        }

        private void CompletePatchBuilds()
        {
            _completedPatches.Clear();
            foreach (var pair in _pendingPatches)
                if (pair.Value.IsCompleted) _completedPatches.Add(pair.Key);
            foreach (var key in _completedPatches)
            {
                if (NewPatchBuilds >= MaxNewPatchesPerFrame || PatchBuildMilliseconds >= 2.0) break;
                Task<Patch> task = _pendingPatches[key];
                _pendingPatches.Remove(key);
                Patch patch = task.GetAwaiter().GetResult(); // completed only; surface preparation failures
                EvictUnusedPatch();
                if (_patches.Count >= MaxResidentPatches) continue;
                long started = Stopwatch.GetTimestamp();
                if (patch.Triangles > 0)
                {
                    patch.Indices = new IndexBuffer(_device, IndexElementSize.SixteenBits, patch.IndexData.Length, BufferUsage.WriteOnly);
                    patch.Indices.SetData(patch.IndexData);
                    patch.Vertices = new DynamicVertexBuffer(_device, SnowVertex.Declaration, Row * Row, BufferUsage.WriteOnly);
                    Rebuild(patch, key.X, key.Y, _field.GetRevision(key.X, key.Y));
                }
                patch.IndexData = null;
                patch.Seen = _now;
                _patches.Add(key, patch);
                NewPatchBuilds++;
                PatchBuildMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
        }

        private int CompareDistance(TerrainBlock a, TerrainBlock b)
        {
            float ax = a.Xi * 100 + 200 - _drawFocus.X, ay = a.Yi * 100 + 200 - _drawFocus.Y;
            float bx = b.Xi * 100 + 200 - _drawFocus.X, by = b.Yi * 100 + 200 - _drawFocus.Y;
            return (ax * ax + ay * ay).CompareTo(bx * bx + by * by);
        }

        private void BindMaterial(Effect effect, MaterialBatch batch)
        {
            Texture2D Texture(int index) => _data.Textures != null && (uint)index < _data.Textures.Length &&
                _data.Textures[index] != null ? _data.Textures[index] : GraphicsManager.Instance.Pixel;
            var primary = Texture(batch.Base);
            var overlay = Texture(batch.Overlay);
            effect.Parameters["DiffuseTexture"]?.SetValue(primary);
            effect.Parameters["SnowOverlayTexture"]?.SetValue(overlay);
            effect.Parameters["SnowBaseUvScale"]?.SetValue(new Vector2(0.64f / primary.Width, 0.64f / primary.Height));
            effect.Parameters["SnowOverlayUvScale"]?.SetValue(new Vector2(0.64f / overlay.Width, 0.64f / overlay.Height));
            effect.Parameters["SnowOverlayEnabled"]?.SetValue(batch.Overlay >= 0 ? 1f : 0f);
        }

        private void EvictUnusedPatch()
        {
            if (_patches.Count < MaxResidentPatches) return;
            _stalePatches.Clear();
            foreach (var pair in _patches) _stalePatches.Add(pair.Key);
            foreach (var block in _visibility.VisibleBlocks) _stalePatches.Remove((block.Xi / 4, block.Yi / 4));
            if (_stalePatches.Count == 0) return;
            var oldest = _stalePatches[0];
            foreach (var key in _stalePatches)
                if (_patches[key].Seen < _patches[oldest].Seen) oldest = key;
            _patches[oldest].Dispose();
            _patches.Remove(oldest);
        }

        public void Dispose()
        {
            foreach (var patch in _patches.Values) patch.Dispose();
            _patches.Clear();
            _pendingPatches.Clear(); // pending jobs own CPU arrays only; no GPU cleanup is required
            _tracks.Clear();
            _fallback?.Dispose();
        }
    }
}
