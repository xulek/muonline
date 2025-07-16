using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Objects;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Simple test class to verify occlusion culling setup
    /// </summary>
    public static class OcclusionCullingTest
    {
        private static readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger("OcclusionCullingTest");

        public static void TestObjectCategorization(IEnumerable<WorldObject> worldObjects)
        {
            if (!Constants.DEBUG_OCCLUSION_CULLING)
                return;

            var visibleObjects = worldObjects.Where(obj => obj.Visible && !obj.OutOfView).ToList();
            var occludedObjects = worldObjects.Where(obj => obj.OcclusionCulled).ToList();

            _logger?.LogInformation($"=== Occlusion Culling Test ===");
            _logger?.LogInformation($"Total objects: {worldObjects.Count()}");
            _logger?.LogInformation($"Visible objects: {visibleObjects.Count}");
            _logger?.LogInformation($"Occluded objects: {occludedObjects.Count}");
            _logger?.LogInformation($"Effective rendering reduction: {(occludedObjects.Count / (double)visibleObjects.Count * 100):F1}%");

            // Log object types
            var objectTypes = visibleObjects.GroupBy(obj => obj.GetType().Name)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count);

            _logger?.LogInformation($"Object types in scene:");
            foreach (var type in objectTypes)
            {
                _logger?.LogInformation($"  {type.Type}: {type.Count}");
            }

            // Log bounding box sizes for debugging
            var camera = Camera.Instance;
            var cameraPos = camera.Position;
            int sampleCount = 0;
            
            foreach (var obj in visibleObjects.Take(5)) // Sample first 5 objects
            {
                var bounds = obj.BoundingBoxWorld;
                var center = (bounds.Min + bounds.Max) * 0.5f;
                var size = bounds.Max - bounds.Min;
                var distance = Vector3.Distance(cameraPos, center);
                
                _logger?.LogInformation($"  {obj.GetType().Name}: Size({size.X:F1}, {size.Y:F1}, {size.Z:F1}), Distance: {distance:F1}");
                
                if (++sampleCount >= 5) break;
            }
        }
    }
}