using Client.Data.ATT;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Worlds.Atlans
{
    /// <summary>
    /// Manages fish boid behavior in Atlans world.
    /// Based on original client's MoveBoid and MoveBoidGroup algorithms.
    /// </summary>
    public class BoidManager
    {
        private const int MAX_BOIDS = 20; // Maximum number of fish in the area
        private const float NEIGHBOR_RADIUS = 200f; // Distance to consider as neighbor
        private const float SEPARATION_DISTANCE = 80f; // Minimum distance between fish
        private const float COHESION_STRENGTH = 0.15f; // Attraction to group center
        private const float ALIGNMENT_STRENGTH = 0.3f; // Alignment with neighbors
        private const float SEPARATION_STRENGTH = 3.0f; // Repulsion from neighbors
        private const float MIN_SPAWN_DISTANCE = 450f;
        private const float MAX_SPAWN_DISTANCE = 1400f;
        private const float SPAWN_VIEW_MARGIN = 60f;
        private const float DESPAWN_DISTANCE = 1500f;
        private const int MAX_SPAWN_PER_FRAME = 3; // Spawn 3 fish per frame for faster population
        private const float NORMAL_SPAWN_COOLDOWN = 0.5f; // Spawn every 0.5 seconds

        private List<FishObject> _fishes = new List<FishObject>();
        private Random _random = new Random();
        private WorldControl _world;
        private WalkableWorldControl _walkableWorld;
        private bool _isSpawning = false;
        private float _spawnCooldown = 0f;

        public BoidManager(WorldControl world)
        {
            _world = world;
            _walkableWorld = world as WalkableWorldControl;
        }

        public void Update(GameTime gameTime)
        {
            if (_walkableWorld?.Walker == null)
                return;

            if (_world.Status != GameControlStatus.Ready)
                return;

            Vector3 heroPosition = _walkableWorld.Walker.Position;
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Update spawn cooldown
            if (_spawnCooldown > 0f)
            {
                _spawnCooldown -= deltaTime;
            }

            // Spawn new fish if needed
            if (_spawnCooldown <= 0f && !_isSpawning)
            {
                SpawnFish(heroPosition);
                _spawnCooldown = NORMAL_SPAWN_COOLDOWN;
            }

            for (int i = _fishes.Count - 1; i >= 0; i--)
            {
                var fish = _fishes[i];

                // The camera may have moved while the fish model was loading.
                if (fish.Status is GameControlStatus.Disposed or GameControlStatus.Error ||
                    fish.GetDistanceFromHero(heroPosition) > DESPAWN_DISTANCE || !CanSwimAt(fish.Position) ||
                    (fish.Visible && fish.Alpha == 0f && !IsOutsideCamera(fish.Position)))
                {
                    _world.Objects.Remove(fish);
                    _fishes.RemoveAt(i);
                    fish.Dispose();
                    continue;
                }

                // The world hides fish while it prepares their models for the first frame.
                if (!fish.Visible)
                    continue;

                // Get terrain height at fish position (using world coordinates)
                float terrainHeight = _world.Terrain.RequestTerrainHeight(fish.Position.X, fish.Position.Y);

                // Update fish movement and environment interaction
                fish.UpdateMovement(gameTime, heroPosition, terrainHeight);

                // Apply boid algorithm
                ApplyBoidBehavior(fish, i, gameTime);

                Vector3 previousPosition = fish.Position;
                fish.ApplyMovement(gameTime);
                if (!CanSwimAt(fish.Position))
                {
                    fish.Position = previousPosition;
                    fish.TurnAround();
                }
            }
        }

        /// <summary>
        /// Starts fish out of view on swimmable tiles beyond the safe zone.
        /// </summary>
        private void SpawnFish(Vector3 heroPosition)
        {
            if (_fishes.Count >= MAX_BOIDS)
                return;

            // Already spawning, skip this frame
            if (_isSpawning)
                return;

            _isSpawning = true;

            // Spawn only a few fish per frame to avoid blocking
            int spawnCount = 0;
            int targetSpawnPerFrame = MAX_SPAWN_PER_FRAME;
            int maxAttempts = targetSpawnPerFrame * 8;
            int attempts = 0;

            while (spawnCount < targetSpawnPerFrame && _fishes.Count < MAX_BOIDS && attempts < maxAttempts)
            {
                attempts++;

                float bearing = (float)_random.NextDouble() * MathHelper.TwoPi;
                float distance = MathHelper.Lerp(MIN_SPAWN_DISTANCE, MAX_SPAWN_DISTANCE, (float)_random.NextDouble());
                var spawnPos = new Vector3(heroPosition.X + MathF.Cos(bearing) * distance,
                    heroPosition.Y + MathF.Sin(bearing) * distance, 0f);

                if (!CanSwimAt(spawnPos, TWFlags.SafeZone))
                    continue;

                spawnPos.Z = _world.Terrain.RequestTerrainHeight(spawnPos.X, spawnPos.Y) + FishObject.SwimHeight;
                if (!IsOutsideCamera(spawnPos))
                    continue;

                float heading = bearing + MathHelper.Pi + ((float)_random.NextDouble() - 0.5f) * MathHelper.PiOver4;

                // Random scale variation: 1.7 to 2.3 (base is 2.0)
                float randomScale = 1.7f + (float)_random.NextDouble() * 0.6f;

                var fish = new FishObject
                {
                    Type = (short)(_random.Next(0, 2) + 1), // Fish type 1 or 2 (MODEL_FISH01+2 or +3)
                    Position = spawnPos,
                    Scale = randomScale, // Random size variation
                    Angle = new Vector3(0f, 0f, MathHelper.WrapAngle(heading + MathHelper.PiOver2)),
                    Direction = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f)
                };

                _fishes.Add(fish);

                // Add to world - this will automatically set fish.World and Load() will be called in Update()
                _world.Objects.Add(fish);

                spawnCount++;
            }

            _isSpawning = false;
        }

        /// <summary>
        /// Applies Boid algorithm (separation, alignment, cohesion) to a fish.
        /// NEW: Works with velocity-based system - modifies desired direction only.
        /// </summary>
         private void ApplyBoidBehavior(FishObject fish, int fishIndex, GameTime gameTime)
        {
            Vector3 separation = Vector3.Zero;
            Vector3 alignment = Vector3.Zero;
            Vector3 cohesion = Vector3.Zero;
            int neighborCount = 0;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Find neighbors and calculate boid forces
            for (int i = 0; i < _fishes.Count; i++)
            {
                if (i == fishIndex)
                    continue;

                var other = _fishes[i];
                float distance = Vector3.Distance(fish.Position, other.Position);

                // Only consider nearby fish as neighbors
                if (distance < NEIGHBOR_RADIUS && distance > 0.1f)
                {
                    neighborCount++;

                    // Separation: steer away from nearby fish
                    if (distance < SEPARATION_DISTANCE)
                    {
                        Vector3 away = fish.Position - other.Position;
                        if (away.LengthSquared() > 0.001f)
                        {
                            away.Normalize();
                            separation += away / distance; // Stronger when closer
                        }
                    }

                    // Alignment: match direction of neighbors
                    alignment += other.Direction;

                    // Cohesion: move towards center of neighbors
                    cohesion += other.Position;
                }
            }

            if (neighborCount > 0)
            {
                // Average the forces
                alignment /= neighborCount;
                cohesion /= neighborCount;

                // Cohesion: steer towards center of mass
                cohesion = cohesion - fish.Position;
                if (cohesion.LengthSquared() > 0.001f)
                {
                    cohesion.Normalize();
                }

                // Apply forces to desired direction
                // fish.Direction setter automatically normalizes, so we can just add influences
                Vector3 currentDir = fish.Direction;
                Vector3 boidInfluence =
                    separation * SEPARATION_STRENGTH * deltaTime +
                    alignment * ALIGNMENT_STRENGTH * deltaTime +
                    cohesion * COHESION_STRENGTH * deltaTime;

                fish.Direction = currentDir + boidInfluence; // Setter will normalize
            }
        }

        private static bool IsOutsideCamera(Vector3 position) =>
            Camera.Instance.Frustum.Contains(new BoundingSphere(position, SPAWN_VIEW_MARGIN)) == ContainmentType.Disjoint;

        private bool CanSwimAt(Vector3 position, TWFlags additionalBlocked = TWFlags.None)
        {
            int terrainX = (int)MathF.Floor(position.X / Constants.TERRAIN_SCALE);
            int terrainY = (int)MathF.Floor(position.Y / Constants.TERRAIN_SCALE);
            if ((uint)terrainX >= Constants.TERRAIN_SIZE || (uint)terrainY >= Constants.TERRAIN_SIZE)
                return false;

            const TWFlags blocked = TWFlags.NoMove | TWFlags.NoGround | TWFlags.Height;
            return (_world.Terrain.RequestTerrainFlag(terrainX, terrainY) & (blocked | additionalBlocked)) == 0;
        }

        /// <summary>
        /// Clears all fish from the world
        /// </summary>
        public void Clear()
        {
            foreach (var fish in _fishes)
            {
                _world.Objects.Remove(fish);
                fish.Dispose();
            }
            _fishes.Clear();
        }

        public int FishCount => _fishes.Count;
    }
}
