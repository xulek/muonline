using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Objects;
using Microsoft.Extensions.Logging;

namespace Client.Main.Controllers
{
    public class OcclusionCullingManager
    {
        private const int DEPTH_BUFFER_SIZE = 128;
        private const int HIERARCHICAL_LEVELS = 4;
        private const int MAX_OCCLUDERS = 512;
        private const float OCCLUSION_BIAS = 0.001f;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly List<WorldObject> _occluders = new(MAX_OCCLUDERS);
        private readonly List<WorldObject> _occludees = new(1024);
        private readonly float[,] _depthBuffer = new float[DEPTH_BUFFER_SIZE, DEPTH_BUFFER_SIZE];
        private readonly float[][][] _hierarchicalDepth = new float[HIERARCHICAL_LEVELS][][];
        private readonly Queue<WorldObject> _visibilityQueue = new(512);
        
        private float _lastCullTime;
        private const float CULL_INTERVAL = 0.05f; // 20 FPS for occlusion culling
        private readonly ILogger _logger;

        public static OcclusionCullingManager Instance { get; private set; }

        public OcclusionCullingManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            Instance = this;
            _logger = MuGame.AppLoggerFactory?.CreateLogger<OcclusionCullingManager>();
            InitializeHierarchicalDepth();
        }

        private void InitializeHierarchicalDepth()
        {
            for (int level = 0; level < HIERARCHICAL_LEVELS; level++)
            {
                int size = DEPTH_BUFFER_SIZE >> level;
                _hierarchicalDepth[level] = new float[size][];
                for (int i = 0; i < size; i++)
                {
                    _hierarchicalDepth[level][i] = new float[size];
                }
            }
        }

        public void Update(GameTime gameTime, IEnumerable<WorldObject> worldObjects)
        {
            if (!Constants.ENABLE_OCCLUSION_CULLING)
            {
                // Reset occlusion culling flags when disabled
                foreach (var obj in worldObjects)
                {
                    obj.OcclusionCulled = false;
                }
                return;
            }

            float currentTime = (float)gameTime.TotalGameTime.TotalSeconds;
            if (currentTime - _lastCullTime < CULL_INTERVAL)
                return;

            _lastCullTime = currentTime;

            CategorizeObjects(worldObjects);
            
            if (Constants.DEBUG_OCCLUSION_CULLING)
                _logger?.LogDebug($"OcclusionCulling: {_occluders.Count} occluders, {_occludees.Count} occludees");
            
            if (_occluders.Count > 0)
            {
                RenderDepthBuffer();
                BuildHierarchicalDepth();
                PerformOcclusionCulling();
            }
            else
            {
                // Reset occlusion flags if no occluders
                foreach (var obj in _occludees)
                {
                    obj.OcclusionCulled = false;
                }
            }
        }

        private void CategorizeObjects(IEnumerable<WorldObject> worldObjects)
        {
            _occluders.Clear();
            _occludees.Clear();

            var camera = Camera.Instance;
            var cameraPos = camera.Position;
            int totalObjects = 0;

            foreach (var obj in worldObjects)
            {
                totalObjects++;
                
                if (!obj.Visible || obj.OutOfView)
                    continue;

                var bounds = obj.BoundingBoxWorld;
                var center = (bounds.Min + bounds.Max) * 0.5f;
                var size = bounds.Max - bounds.Min;
                var distance = Vector3.Distance(cameraPos, center);

                // Objects are potential occluders if they're large enough and close enough
                var minSide = Math.Min(size.X, Math.Min(size.Y, size.Z));
                var maxSide = Math.Max(size.X, Math.Max(size.Y, size.Z));
                
                // More lenient criteria for MuOnline objects
                bool isLargeEnough = minSide > 10f && maxSide > 20f;
                bool isCloseEnough = distance < 600f;
                
                if (isLargeEnough && isCloseEnough && _occluders.Count < MAX_OCCLUDERS)
                {
                    _occluders.Add(obj);
                }
                else
                {
                    _occludees.Add(obj);
                }
            }
            
            if (Constants.DEBUG_OCCLUSION_CULLING)
                _logger?.LogDebug($"OcclusionCulling: Processed {totalObjects} total objects, {_occluders.Count} potential occluders, {_occludees.Count} occludees");

            // Sort occluders by distance (closer objects first)
            _occluders.Sort((a, b) => 
            {
                var distA = Vector3.DistanceSquared(cameraPos, (a.BoundingBoxWorld.Min + a.BoundingBoxWorld.Max) * 0.5f);
                var distB = Vector3.DistanceSquared(cameraPos, (b.BoundingBoxWorld.Min + b.BoundingBoxWorld.Max) * 0.5f);
                return distA.CompareTo(distB);
            });
        }

        private void RenderDepthBuffer()
        {
            // Clear depth buffer
            for (int y = 0; y < DEPTH_BUFFER_SIZE; y++)
            {
                for (int x = 0; x < DEPTH_BUFFER_SIZE; x++)
                {
                    _depthBuffer[x, y] = 1.0f;
                }
            }

            var camera = Camera.Instance;
            var viewProjection = camera.View * camera.Projection;

            // Render each occluder to depth buffer
            foreach (var occluder in _occluders)
            {
                RenderObjectToDepthBuffer(occluder, viewProjection);
            }
        }

        private void RenderObjectToDepthBuffer(WorldObject obj, Matrix viewProjection)
        {
            var bounds = obj.BoundingBoxWorld;
            var corners = bounds.GetCorners();

            // Project all corners to screen space
            var screenCorners = new Vector3[8];
            bool allBehind = true;
            
            for (int i = 0; i < 8; i++)
            {
                screenCorners[i] = _graphicsDevice.Viewport.Project(corners[i], Matrix.Identity, Camera.Instance.View, Camera.Instance.Projection);
                if (screenCorners[i].Z >= 0 && screenCorners[i].Z <= 1)
                    allBehind = false;
            }

            if (allBehind) return;

            // Find 2D bounding box in screen space
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float minZ = float.MaxValue;

            foreach (var corner in screenCorners)
            {
                if (corner.Z >= 0 && corner.Z <= 1)
                {
                    minX = Math.Min(minX, corner.X);
                    minY = Math.Min(minY, corner.Y);
                    maxX = Math.Max(maxX, corner.X);
                    maxY = Math.Max(maxY, corner.Y);
                    minZ = Math.Min(minZ, corner.Z);
                }
            }

            // Convert to depth buffer coordinates
            int x1 = Math.Max(0, (int)((minX / _graphicsDevice.Viewport.Width) * DEPTH_BUFFER_SIZE));
            int y1 = Math.Max(0, (int)((minY / _graphicsDevice.Viewport.Height) * DEPTH_BUFFER_SIZE));
            int x2 = Math.Min(DEPTH_BUFFER_SIZE - 1, (int)((maxX / _graphicsDevice.Viewport.Width) * DEPTH_BUFFER_SIZE));
            int y2 = Math.Min(DEPTH_BUFFER_SIZE - 1, (int)((maxY / _graphicsDevice.Viewport.Height) * DEPTH_BUFFER_SIZE));

            // Render to depth buffer
            for (int y = y1; y <= y2; y++)
            {
                for (int x = x1; x <= x2; x++)
                {
                    _depthBuffer[x, y] = Math.Min(_depthBuffer[x, y], minZ);
                }
            }
        }

        private void BuildHierarchicalDepth()
        {
            // Level 0 is the full resolution depth buffer
            for (int y = 0; y < DEPTH_BUFFER_SIZE; y++)
            {
                for (int x = 0; x < DEPTH_BUFFER_SIZE; x++)
                {
                    _hierarchicalDepth[0][x][y] = _depthBuffer[x, y];
                }
            }

            // Build hierarchical levels
            for (int level = 1; level < HIERARCHICAL_LEVELS; level++)
            {
                int prevSize = DEPTH_BUFFER_SIZE >> (level - 1);
                int currentSize = DEPTH_BUFFER_SIZE >> level;
                
                for (int y = 0; y < currentSize; y++)
                {
                    for (int x = 0; x < currentSize; x++)
                    {
                        // Sample 2x2 block from previous level and take minimum (closest depth)
                        int px = x * 2;
                        int py = y * 2;
                        
                        float minDepth = 1.0f;
                        for (int dy = 0; dy < 2 && py + dy < prevSize; dy++)
                        {
                            for (int dx = 0; dx < 2 && px + dx < prevSize; dx++)
                            {
                                minDepth = Math.Min(minDepth, _hierarchicalDepth[level - 1][px + dx][py + dy]);
                            }
                        }
                        
                        _hierarchicalDepth[level][x][y] = minDepth;
                    }
                }
            }
        }

        private void PerformOcclusionCulling()
        {
            var camera = Camera.Instance;
            int occludedCount = 0;
            
            foreach (var obj in _occludees)
            {
                bool isOccluded = IsObjectOccluded(obj, camera);
                obj.OcclusionCulled = isOccluded;
                if (isOccluded) occludedCount++;
            }
            
            if (Constants.DEBUG_OCCLUSION_CULLING)
                _logger?.LogDebug($"OcclusionCulling: {occludedCount}/{_occludees.Count} objects culled");
        }

        private bool IsObjectOccluded(WorldObject obj, Camera camera)
        {
            var bounds = obj.BoundingBoxWorld;
            var corners = bounds.GetCorners();

            // Project bounding box to screen space
            var screenCorners = new Vector3[8];
            bool allBehind = true;
            
            for (int i = 0; i < 8; i++)
            {
                screenCorners[i] = _graphicsDevice.Viewport.Project(corners[i], Matrix.Identity, camera.View, camera.Projection);
                if (screenCorners[i].Z >= 0 && screenCorners[i].Z <= 1)
                    allBehind = false;
            }

            if (allBehind) return false; // Behind camera, not occluded

            // Find screen space bounding box
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var corner in screenCorners)
            {
                if (corner.Z >= 0 && corner.Z <= 1)
                {
                    minX = Math.Min(minX, corner.X);
                    minY = Math.Min(minY, corner.Y);
                    maxX = Math.Max(maxX, corner.X);
                    maxY = Math.Max(maxY, corner.Y);
                    minZ = Math.Min(minZ, corner.Z);
                    maxZ = Math.Max(maxZ, corner.Z);
                }
            }

            // Test against hierarchical depth buffer
            return TestHierarchicalOcclusion(minX, minY, maxX, maxY, minZ + OCCLUSION_BIAS);
        }

        private bool TestHierarchicalOcclusion(float minX, float minY, float maxX, float maxY, float objectDepth)
        {
            // Convert to normalized coordinates (0-1)
            float normMinX = Math.Max(0, minX / _graphicsDevice.Viewport.Width);
            float normMinY = Math.Max(0, minY / _graphicsDevice.Viewport.Height);
            float normMaxX = Math.Min(1, maxX / _graphicsDevice.Viewport.Width);
            float normMaxY = Math.Min(1, maxY / _graphicsDevice.Viewport.Height);

            // Start with the highest level (lowest resolution)
            for (int level = HIERARCHICAL_LEVELS - 1; level >= 0; level--)
            {
                int size = DEPTH_BUFFER_SIZE >> level;
                
                int x1 = (int)(normMinX * size);
                int y1 = (int)(normMinY * size);
                int x2 = (int)(normMaxX * size);
                int y2 = (int)(normMaxY * size);

                x1 = Math.Max(0, Math.Min(size - 1, x1));
                y1 = Math.Max(0, Math.Min(size - 1, y1));
                x2 = Math.Max(0, Math.Min(size - 1, x2));
                y2 = Math.Max(0, Math.Min(size - 1, y2));

                // Check if any pixel in the region is closer than the object
                bool hasVisiblePixel = false;
                for (int y = y1; y <= y2; y++)
                {
                    for (int x = x1; x <= x2; x++)
                    {
                        if (_hierarchicalDepth[level][x][y] > objectDepth)
                        {
                            hasVisiblePixel = true;
                            break;
                        }
                    }
                    if (hasVisiblePixel) break;
                }

                if (hasVisiblePixel)
                {
                    // If we're at the finest level, the object is visible
                    if (level == 0)
                        return false;
                    
                    // Otherwise, go to finer level
                    continue;
                }
                else
                {
                    // All pixels are closer, object is occluded
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            // Clean up resources if needed
        }
    }
}