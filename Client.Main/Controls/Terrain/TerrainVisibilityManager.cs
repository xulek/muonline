using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Controls.Terrain
{
    public class TerrainBlock
    {
        public BoundingBox Bounds;
        public BoundingBox PaddedBounds;
        public float MinZ, MaxZ;
        public int LODLevel;
        public Vector2 Center;
        public bool IsVisible;
        public int Xi, Yi;

        // 16 tiles fit in a single mask, avoiding one heap allocation per terrain block.
        public ushort TileVisibilityMask;
        public int VisibleTileCount;
        public bool FullyVisible; // All tiles in block are visible (skip individual tile tests)

        public bool IsTileVisible(int tileIndex)
            => (uint)tileIndex < 16u && (((uint)TileVisibilityMask) & (1u << tileIndex)) != 0;
    }

    /// <summary>
    /// Manages terrain culling, LOD selection, and determines visible blocks.
    /// </summary>
    public class TerrainVisibilityManager
    {
        private const int BlockSize = 4;
        private const int MaxLodLevels = 3;
        private const float LodDistanceMultiplier = 3000f;
        private const float CameraMoveThreshold = 32f;
        // Slight conservative padding (world units) to avoid edge popping
        private const float CullingPaddingXY = 64f; // ~0.64 tile with TERRAIN_SCALE=100
        private const float CullingPaddingZ = 32f;  // Small vertical slack

        private readonly TerrainData _data;
        private readonly TerrainBlockCache _blockCache;
        private readonly List<TerrainBlock> _visibleBlocks = new(256);
        // Scratch list reused each frame to avoid per-update allocations
        private readonly List<TerrainBlock> _visibleScratch = new(256);
        private TerrainBlock[] _classificationScratch = Array.Empty<TerrainBlock>();
        private readonly BoundingBox[] _tilePaddedBounds = new BoundingBox[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];
        private Vector2 _lastCameraPosition;
        private Vector3 _lastCameraDirection;
        private float _lastViewFar;
        private float _lastFov;
        private float _lastAspectRatio;
        private bool _hasVisibilitySnapshot;
        private readonly int[] _lodSteps = { 1, 2, 4 };
        private bool _cullingDataReady;
        private const int ParallelBlockCullingThreshold = 1024;
        private static readonly ParallelOptions CullingParallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2)
        };

        public IReadOnlyList<TerrainBlock> VisibleBlocks => _visibleBlocks;
        public int[] LodSteps => _lodSteps;
        // Bumped each time the visible-block set is rebuilt. Consumers can cache derived data
        // (e.g. per-block LOD grid) keyed on this version to avoid recomputing within the same frame.
        public int Version { get; private set; }

        public TerrainVisibilityManager(TerrainData data)
        {
            _data = data;
            _blockCache = new TerrainBlockCache(BlockSize, Constants.TERRAIN_SIZE);
            PrecomputeCullingData();
        }

        private void PrecomputeCullingData()
        {
            if (_data.HeightMap == null) return;
            int blocksPerSide = Constants.TERRAIN_SIZE / BlockSize;

            // Precompute padded bounds for every tile once.
            for (int y = 0; y < Constants.TERRAIN_SIZE; y++)
            {
                for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
                {
                    int i1 = GetTerrainIndexRepeat(x, y);
                    int i2 = GetTerrainIndexRepeat(x + 1, y);
                    int i3 = GetTerrainIndexRepeat(x + 1, y + 1);
                    int i4 = GetTerrainIndexRepeat(x, y + 1);

                    float hmin = MathF.Min(MathF.Min(_data.HeightMap[i1].R, _data.HeightMap[i2].R),
                                           MathF.Min(_data.HeightMap[i3].R, _data.HeightMap[i4].R)) * 1.5f;
                    float hmax = MathF.Max(MathF.Max(_data.HeightMap[i1].R, _data.HeightMap[i2].R),
                                           MathF.Max(_data.HeightMap[i3].R, _data.HeightMap[i4].R)) * 1.5f;

                    float sx = x * Constants.TERRAIN_SCALE;
                    float sy = y * Constants.TERRAIN_SCALE;
                    float ex = (x + 1) * Constants.TERRAIN_SCALE;
                    float ey = (y + 1) * Constants.TERRAIN_SCALE;

                    _tilePaddedBounds[y * Constants.TERRAIN_SIZE + x] = new BoundingBox(
                        new Vector3(sx - CullingPaddingXY, sy - CullingPaddingXY, hmin - CullingPaddingZ),
                        new Vector3(ex + CullingPaddingXY, ey + CullingPaddingXY, hmax + CullingPaddingZ));
                }
            }

            for (int by = 0; by < blocksPerSide; by++)
            {
                for (int bx = 0; bx < blocksPerSide; bx++)
                {
                    var block = _blockCache.GetBlock(bx, by);
                    block.MinZ = float.MaxValue;
                    block.MaxZ = float.MinValue;

                    // A block of BlockSize tiles spans BlockSize + 1 vertices per axis; the far border row and
                    // column belong to it as well, otherwise steep edges get a too low / too high box.
                    for (int y = 0; y <= BlockSize; y++)
                    {
                        for (int x = 0; x <= BlockSize; x++)
                        {
                            int idx = GetTerrainIndexRepeat(block.Xi + x, block.Yi + y);
                            float h = _data.HeightMap[idx].R * 1.5f;
                            if (h < block.MinZ) block.MinZ = h;
                            if (h > block.MaxZ) block.MaxZ = h;
                        }
                    }

                    float sx = block.Xi * Constants.TERRAIN_SCALE;
                    float sy = block.Yi * Constants.TERRAIN_SCALE;
                    float ex = (block.Xi + BlockSize) * Constants.TERRAIN_SCALE;
                    float ey = (block.Yi + BlockSize) * Constants.TERRAIN_SCALE;

                    block.Bounds = new BoundingBox(
                        new Vector3(sx, sy, block.MinZ),
                        new Vector3(ex, ey, block.MaxZ));
                    block.PaddedBounds = Inflate(block.Bounds, CullingPaddingXY, CullingPaddingZ);
                }
            }

            _cullingDataReady = true;
        }

        public void Update(Vector2 cameraPosition)
        {
            if (!_cullingDataReady)
                PrecomputeCullingData();

            if (!_cullingDataReady)
                return;

            var camera = Camera.Instance;
            Vector3 cameraDirection = camera.Target - camera.Position;
            if (cameraDirection.LengthSquared() > 1e-8f)
                cameraDirection.Normalize();
            else
                cameraDirection = Vector3.UnitY;

            const float thrSq = CameraMoveThreshold * CameraMoveThreshold;
            bool positionChanged = Vector2.DistanceSquared(_lastCameraPosition, cameraPosition) >= thrSq;
            bool directionChanged = !_hasVisibilitySnapshot ||
                                    Vector3.Dot(_lastCameraDirection, cameraDirection) < 0.9995f;
            bool projectionChanged = !_hasVisibilitySnapshot ||
                                     MathF.Abs(_lastViewFar - camera.TerrainRenderDistance) > 0.01f ||
                                     MathF.Abs(_lastFov - camera.FOV) > 0.001f ||
                                     MathF.Abs(_lastAspectRatio - camera.AspectRatio) > 0.0001f;

            if (_hasVisibilitySnapshot && !positionChanged && !directionChanged && !projectionChanged)
                return;

            _lastCameraPosition = cameraPosition;
            _lastCameraDirection = cameraDirection;
            _lastViewFar = camera.TerrainRenderDistance;
            _lastFov = camera.FOV;
            _lastAspectRatio = camera.AspectRatio;
            _hasVisibilitySnapshot = true;
            _visibleBlocks.Clear();

            float renderDist = camera.TerrainRenderDistance;
            float renderDistSq = renderDist * renderDist;
            int cellWorld = (int)(BlockSize * Constants.TERRAIN_SCALE);

            const int Extra = 4;
            int tilesPerAxis = Constants.TERRAIN_SIZE / BlockSize;

            int startX = Math.Max(0, (int)((cameraPosition.X - renderDist) / cellWorld) - Extra);
            int startY = Math.Max(0, (int)((cameraPosition.Y - renderDist) / cellWorld) - Extra);
            int endX = Math.Min(tilesPerAxis - 1, (int)((cameraPosition.X + renderDist) / cellWorld) + Extra);
            int endY = Math.Min(tilesPerAxis - 1, (int)((cameraPosition.Y + renderDist) / cellWorld) + Extra);

            var frustum = Camera.Instance.Frustum;
            int expected = (endX - startX + 1) * (endY - startY + 1);
            var visible = _visibleScratch;
            visible.Clear();
            if (visible.Capacity < expected)
                visible.Capacity = expected;

            bool useParallelCulling = Environment.ProcessorCount > 1 &&
                                      expected >= ParallelBlockCullingThreshold;

            if (useParallelCulling)
            {
                EnsureClassificationScratchCapacity(expected);
                int width = endX - startX + 1;
                Parallel.For(startY, endY + 1, CullingParallelOptions, gy =>
                {
                    int resultIndex = (gy - startY) * width;
                    for (int gx = startX; gx <= endX; gx++, resultIndex++)
                    {
                        _classificationScratch[resultIndex] =
                            TryClassifyVisibleBlock(gx, gy, cameraPosition, renderDistSq, frustum, out var block)
                                ? block
                                : null;
                    }
                });

                for (int i = 0; i < expected; i++)
                {
                    TerrainBlock block = _classificationScratch[i];
                    if (block != null)
                        visible.Add(block);
                }
            }
            else
            {
                for (int gy = startY; gy <= endY; gy++)
                {
                    for (int gx = startX; gx <= endX; gx++)
                    {
                        if (TryClassifyVisibleBlock(gx, gy, cameraPosition, renderDistSq, frustum, out var block))
                            visible.Add(block);
                    }
                }
            }

            _visibleBlocks.AddRange(visible);

            unchecked { Version++; }
        }

        private void EnsureClassificationScratchCapacity(int required)
        {
            if (_classificationScratch.Length >= required)
                return;

            int capacity = 1024;
            while (capacity < required)
                capacity <<= 1;

            _classificationScratch = new TerrainBlock[capacity];
        }

        private bool TryClassifyVisibleBlock(
            int gx,
            int gy,
            Vector2 cameraPosition,
            float renderDistSq,
            BoundingFrustum frustum,
            out TerrainBlock block)
        {
            block = _blockCache.GetBlock(gx, gy);

            float distSq = Vector2.DistanceSquared(block.Center, cameraPosition);
            if (distSq > renderDistSq)
            {
                block.IsVisible = false;
                return false;
            }

            block.LODLevel = GetLodLevelFromDistanceSquared(distSq);

            var containment = frustum.Contains(block.PaddedBounds);
            block.IsVisible = containment != ContainmentType.Disjoint;
            if (!block.IsVisible)
                return false;

            if (containment == ContainmentType.Contains)
            {
                block.FullyVisible = true;
                block.VisibleTileCount = 16;
                block.TileVisibilityMask = ushort.MaxValue;
            }
            else
            {
                block.FullyVisible = false;
                PerformTileCulling(block, frustum);
            }

            return true;
        }

        private static int GetLodLevelFromDistanceSquared(float distanceSquared)
        {
            float level1 = LodDistanceMultiplier;
            float level2 = LodDistanceMultiplier * 2f;

            if (distanceSquared < level1 * level1)
                return 0;
            if (distanceSquared < level2 * level2)
                return 1;
            return MaxLodLevels - 1;
        }

        private void PerformTileCulling(TerrainBlock block, BoundingFrustum frustum)
        {
            int visibleCount = 0;
            ushort visibilityMask = 0;

            for (int tileY = 0; tileY < BlockSize; tileY++)
            {
                for (int tileX = 0; tileX < BlockSize; tileX++)
                {
                    int x = block.Xi + tileX;
                    int y = block.Yi + tileY;
                    int tileIndex = y * Constants.TERRAIN_SIZE + x;
                    if (frustum.Contains(_tilePaddedBounds[tileIndex]) == ContainmentType.Disjoint)
                        continue;

                    int indexInBlock = tileY * BlockSize + tileX;
                    visibilityMask |= (ushort)(1 << indexInBlock);
                    visibleCount++;
                }
            }

            block.TileVisibilityMask = visibilityMask;
            block.VisibleTileCount = visibleCount;
            block.FullyVisible = visibleCount == 16;
        }

        private static BoundingBox Inflate(BoundingBox bounds, float padXY, float padZ)
        {
            var pad = new Vector3(padXY, padXY, padZ);
            return new BoundingBox(bounds.Min - pad, bounds.Max + pad);
        }

        private static int GetTerrainIndexRepeat(int x, int y)
            => ((y & Constants.TERRAIN_SIZE_MASK) * Constants.TERRAIN_SIZE)
             + (x & Constants.TERRAIN_SIZE_MASK);

        private class TerrainBlockCache
        {
            private readonly TerrainBlock[,] _blocks;
            private readonly int _gridSize;

            public TerrainBlockCache(int blockSize, int terrainSize)
            {
                _gridSize = terrainSize / blockSize;
                _blocks = new TerrainBlock[_gridSize, _gridSize];

                for (int y = 0; y < _gridSize; y++)
                {
                    for (int x = 0; x < _gridSize; x++)
                    {
                        int xi = x * blockSize;
                        int yi = y * blockSize;
                        _blocks[y, x] = new TerrainBlock
                        {
                            Xi = xi,
                            Yi = yi,
                            Center = new Vector2(
                                (xi + blockSize * 0.5f) * Constants.TERRAIN_SCALE,
                                (yi + blockSize * 0.5f) * Constants.TERRAIN_SCALE)
                        };
                    }
                }
            }

            public TerrainBlock GetBlock(int x, int y) => _blocks[y, x];
        }
    }
}
