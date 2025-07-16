using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System;
using Client.Main.Objects;
using Client.Main.Models;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Simple ray-based occlusion culling as a fallback/test
    /// </summary>
    public class SimpleOcclusionCulling
    {
        private readonly ILogger _logger;
        private float _lastCullTime;
        private const float CULL_INTERVAL = 0.1f; // 10 FPS
        
        public SimpleOcclusionCulling()
        {
            _logger = MuGame.AppLoggerFactory?.CreateLogger<SimpleOcclusionCulling>();
        }

        public void Update(GameTime gameTime, IEnumerable<WorldObject> worldObjects)
        {
            if (!Constants.ENABLE_OCCLUSION_CULLING)
            {
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

            // Get objects that are in view frustum but ignore current occlusion state
            var inViewObjects = worldObjects.Where(obj => obj.Status == GameControlStatus.Ready && !obj.OutOfView && !obj.Hidden).ToList();
            var camera = Camera.Instance;
            int culledCount = 0;

            // Reset all occlusion flags first
            foreach (var obj in inViewObjects)
            {
                obj.OcclusionCulled = false;
            }

            // Then check occlusion for each object
            foreach (var obj in inViewObjects)
            {
                // NEVER occlude players, NPCs, or monsters - they are the most important objects
                if (IsLivingEntity(obj))
                {
                    obj.OcclusionCulled = false;
                    continue;
                }

                bool isOccluded = IsSimpleOccluded(obj, inViewObjects, camera);
                obj.OcclusionCulled = isOccluded;
                if (isOccluded) culledCount++;
            }

            if (Constants.DEBUG_OCCLUSION_CULLING)
            {
                _logger?.LogInformation($"SimpleOcclusion: {culledCount}/{inViewObjects.Count} objects culled ({(culledCount / (float)inViewObjects.Count * 100):F1}% reduction)");
            }
        }

        private bool IsSimpleOccluded(WorldObject obj, List<WorldObject> allObjects, Camera camera)
        {
            var objCenter = (obj.BoundingBoxWorld.Min + obj.BoundingBoxWorld.Max) * 0.5f;
            var cameraPos = camera.Position;
            var direction = objCenter - cameraPos;
            var distance = direction.Length();
            
            if (distance < 100f) // Don't occlude very close objects
                return false;
                
            direction.Normalize();
            
            var objSize = obj.BoundingBoxWorld.Max - obj.BoundingBoxWorld.Min;
            int potentialOccluders = 0;

            // Check if any other object is between camera and this object
            foreach (var other in allObjects)
            {
                if (other == obj || other.OutOfView || other.Hidden)
                    continue;

                // CRITICAL: Skip transparent objects that shouldn't occlude
                if (IsTransparentObject(other))
                    continue;

                // CRITICAL: Skip living entities - they can move and shouldn't occlude
                if (IsLivingEntity(other))
                    continue;

                var otherCenter = (other.BoundingBoxWorld.Min + other.BoundingBoxWorld.Max) * 0.5f;
                var otherDistance = Vector3.Distance(cameraPos, otherCenter);
                
                // Skip objects that are further away or too close to target
                if (otherDistance >= distance * 0.9f || otherDistance < 50f)
                    continue;

                // Check if the other object is large enough to occlude
                var otherSize = other.BoundingBoxWorld.Max - other.BoundingBoxWorld.Min;
                var minOccluderSize = GetMinOccluderSize(other);
                
                if (otherSize.X < minOccluderSize || otherSize.Y < minOccluderSize)
                    continue;
                
                potentialOccluders++;

                // Simple ray-box intersection test
                var ray = new Ray(cameraPos, direction);
                var intersection = ray.Intersects(other.BoundingBoxWorld);
                
                if (intersection.HasValue && intersection.Value < distance * 0.85f) // Must be closer
                {
                    // Check if the occluder is large enough relative to the occludee
                    var occluderSize = otherSize;
                    
                    // Balanced size comparison - occluder should be similar or larger
                    if (occluderSize.X >= objSize.X * 0.9f && occluderSize.Y >= objSize.Y * 0.9f)
                    {
                        // Additional check: make sure objects are roughly aligned
                        var lateralDistance = Vector3.Distance(
                            new Vector3(objCenter.X, objCenter.Y, 0),
                            new Vector3(otherCenter.X, otherCenter.Y, 0)
                        );
                        
                        if (lateralDistance < MathF.Max(occluderSize.X, occluderSize.Y) * 0.8f)
                        {
                            return true;
                        }
                    }
                }
            }

            // Remove spam - only log actual occlusions, not failed attempts
            
            return false;
        }

        private bool IsLivingEntity(WorldObject obj)
        {
            string typeName = obj.GetType().Name;
            
            // Player objects - always visible
            if (typeName.Contains("Player"))
                return true;
            
            // NPC objects - always visible  
            if (typeName.Contains("NPC") || typeName.Contains("Npc"))
                return true;
            
            // Monster objects - always visible
            if (typeName.Contains("Monster"))
                return true;
            
            // Specific NPC/Monster types from MuOnline
            var livingEntityTypes = new HashSet<string>
            {
                "PlayerObject",
                "MonsterObject",
                "NPCObject",
                "NpcObject",
                "WalkerObject",          // Base class for walking entities
                "Trainer",               // Specific NPCs
                "Pasi",
                "Leo", 
                "Marlon",
                "Hanzo",
                "ElfSoldier",
                "CrossbowGuard",
                "KnightGuard",
                "MerchantAnimalObject",
                "Guard",
                "Merchant",
                "Goblin",
                "Orc",
                "Skeleton",
                "Spider",
                "Budge",
                "Mummy",
                "Larva",
                "Beetle",
                "Golem",
                "Assassin",
                "EliteOrc",
                "Cyclops",
                "Yeti",
                "Troll",
                "Mammoth",
                "IceMonster",
                "Hellhound",
                "Balrog",
                "Poison",
                "Meteorite",
                "Gargoyle"
            };
            
            return livingEntityTypes.Contains(typeName);
        }

        private float GetMinOccluderSize(WorldObject obj)
        {
            string typeName = obj.GetType().Name;
            
            // Solid building objects - can be effective occluders even if smaller
            if (typeName.Contains("Wall") || typeName.Contains("House") || typeName.Contains("Building"))
                return 80f;
            
            // Trees and large natural objects - good occluders
            if (typeName.Contains("Tree") || typeName.Contains("Rock") || typeName.Contains("Stone"))
                return 120f;
            
            // Furniture and decorative objects - need to be larger to occlude
            if (typeName.Contains("Furniture") || typeName.Contains("Decoration") || typeName.Contains("Ornament"))
                return 150f;
            
            // Fence and barrier objects - can be effective occluders
            if (typeName.Contains("Fence") || typeName.Contains("Barrier") || typeName.Contains("Gate"))
                return 100f;
            
            // Default minimum size for unknown objects
            return 120f;
        }

        private bool IsTransparentObject(WorldObject obj)
        {
            // Check explicit transparency properties
            if (obj.IsTransparent)
                return true;

            // Check alpha transparency
            if (obj.TotalAlpha < 0.95f)
                return true;

            // Check blend state for transparency
            if (obj.BlendState == BlendState.AlphaBlend || 
                obj.BlendState == BlendState.Additive || 
                obj.BlendState == BlendState.NonPremultiplied)
                return true;

            // Check object type name for common transparent object patterns
            string typeName = obj.GetType().Name;
            
            // Effect objects - all effects should be transparent
            if (typeName.Contains("Effect"))
                return true;

            // Particle systems
            if (typeName.Contains("Particle"))
                return true;

            // Specific transparent object types
            var transparentTypes = new HashSet<string>
            {
                "GrassObject",           // Grass uses alpha cutout
                "WaterObject",           // Water is transparent
                "WaterSplashObject",     // Water effects
                "WaterMistParticleSystem",
                "WaterPortalObject",
                "WaterPlantObject",
                "WaterFallObject",
                "WaterSpoutObject",
                "ShipWaterPathObject",
                "FlowersObject",         // Flowers are often transparent
                "FlowersObject2",
                "LightBeamObject",       // Light beams
                "LightObject",
                "FireLightObject",
                "PortalObject",          // Portals
                "Warp01NPCObject",
                "Warp02NPCObject", 
                "Warp03NPCObject",
                "BubblesObject",         // Atmospheric effects
                "AuroraObject",
                "CloudsEffect",
                "CloudLightEffect",
                "WingObject",            // Wings
                "Wing403",
                "ChatBubbleObject",      // UI elements
                "DamageTextObject",
                "LogoSunObject",
                "MuGameObject",
                "WaveByShipObject",
                "ClimberObject",
                "CardObject",
                "EoTheCraftsmanPlaceObject"
            };

            if (transparentTypes.Contains(typeName))
                return true;

            // Check if it's a sprite object (2D objects are typically transparent)
            if (typeName.Contains("Sprite"))
                return true;

            return false;
        }
    }
}