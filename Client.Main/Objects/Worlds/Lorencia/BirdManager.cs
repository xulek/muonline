using Client.Main.Controllers;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Worlds.Lorencia
{
    /// <summary>
    /// Manages bird spawning, movement, and despawning in Lorencia world.
    /// Birds fly in loose flocks with less cohesion than fish.
    /// </summary>
    public partial class BirdManager
    {
        private const int MAX_BIRDS = 20; // More birds than butterflies
        private const float NEIGHBOR_RADIUS = 200f; // Distance to consider as neighbor
        private const float SEPARATION_DISTANCE = 180f; // Minimum distance between birds (increased to prevent mesh overlap)
        private const float COHESION_STRENGTH = 0.08f; // Weak attraction to group
        private const float ALIGNMENT_STRENGTH = 0.15f; // Weak alignment (more than butterflies)
        private const float SEPARATION_STRENGTH = 3.0f; // Strong repulsion to prevent overlap
        private const float SPAWN_DISTANCE = 2500f; // Spawn radius around player
        private const int MAX_SPAWN_PER_FRAME = 3; // Spawn 3 birds per frame
        private const float NORMAL_SPAWN_COOLDOWN = 0.6f; // Spawn every 0.6 seconds
        private const float SPAWN_OBSTACLE_CLEARANCE = 100f; // Spawn this far above a building or tree
        private const float MAX_SPAWN_ALTITUDE = 700f; // Above terrain; no spawn over taller obstacles

        private List<BirdObject> _birds = new List<BirdObject>();
        private WalkableWorldControl _world;
        private Random _random = new Random();
        private float _spawnCooldown = 0f;
        private bool _isSpawning = false;

        // Buildings, walls and trees load in after the world, so the obstacle map is rebuilt soon after
        // the start, then at growing intervals up to every 15 seconds.
        private readonly BirdObstacleMap _obstacles;
        private float _obstacleRebuildTimer;
        private float _obstacleRebuildInterval = 0.5f;

        public BirdManager(WalkableWorldControl world)
        {
            _world = world;
            _obstacles = new BirdObstacleMap(world);
        }

        public void Update(GameTime gameTime)
        {
            if (_world?.Walker == null)
                return;

            Vector3 heroPosition = _world.Walker.Position;
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Update spawn cooldown
            _spawnCooldown -= deltaTime;

            _obstacleRebuildTimer -= deltaTime;
            if (_obstacleRebuildTimer <= 0f)
            {
                _obstacles.Rebuild();
                _obstacleRebuildInterval = MathF.Min(_obstacleRebuildInterval * 2f, 15f);
                _obstacleRebuildTimer = _obstacleRebuildInterval;
            }

            // Spawn birds if needed
            if (_birds.Count < MAX_BIRDS && _spawnCooldown <= 0f && !_isSpawning)
            {
                SpawnBirds(heroPosition);
                _spawnCooldown = NORMAL_SPAWN_COOLDOWN;
            }

            // Update all birds
            for (int i = _birds.Count - 1; i >= 0; i--)
            {
                var bird = _birds[i];

                // Check if bird is too far from hero - despawn
                float distanceFromHero = bird.GetDistanceFromHero(heroPosition);
                if (distanceFromHero > 2000f)
                {
                    _world.Objects.Remove(bird);
                    _birds.RemoveAt(i);
                    bird.Dispose();
                    continue;
                }

                // Update movement behavior
                bird.UpdateMovement(gameTime, heroPosition);

                // Apply weak boid behavior (only when flying, not when landing/taking off)
                if (bird.AIState == BirdObject.BirdAIState.Flying && !bird.IsAvoiding)
                {
                    ApplyBoidBehavior(bird, i, gameTime);
                }

                // Check boundaries (walls, safe zones)
                CheckBoundaries(bird);

                // Apply final movement
                bird.ApplyMovement(gameTime);

                // GOBoid.cpp: within 600 of hero, rfc512 -> aBird1 / aBird2
                UpdateBirdCalls(bird, gameTime);
            }
        }

        private void UpdateBirdCalls(BirdObject bird, GameTime gameTime)
        {
            var walker = _world.Walker;
            if (walker == null)
                return;

            float dx = bird.Position.X - walker.Position.X;
            float dy = bird.Position.Y - walker.Position.Y;
            if (dx * dx + dy * dy >= 600f * 600f)
                return;

            float probability = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f / 512f;
            if ((float)MuGame.Random.NextDouble() < probability)
                SoundController.Instance.PlayBufferWithAttenuation("Sound/aBird1.wav", bird.Position, walker.Position);
            if ((float)MuGame.Random.NextDouble() < probability)
                SoundController.Instance.PlayBufferWithAttenuation("Sound/aBird2.wav", bird.Position, walker.Position);
        }

        /// <summary>
        /// Spawns birds in circular pattern around player at flying height
        /// </summary>
        private void SpawnBirds(Vector3 heroPosition)
        {
            _isSpawning = true;

            int spawnCount = 0;
            int maxAttempts = MAX_SPAWN_PER_FRAME * 5;

            for (int attempt = 0; attempt < maxAttempts && spawnCount < MAX_SPAWN_PER_FRAME; attempt++)
            {
                if (_birds.Count >= MAX_BIRDS)
                    break;

                // Circular spawn distribution around hero
                float angle = (float)(_random.NextDouble() * Math.PI * 2);
                float distance = (float)(_random.NextDouble() * SPAWN_DISTANCE * 0.5f) + SPAWN_DISTANCE * 0.5f;
                float offsetX = MathF.Cos(angle) * distance;
                float offsetY = MathF.Sin(angle) * distance;

                Vector3 spawnPos = new Vector3(
                    heroPosition.X + offsetX,
                    heroPosition.Y + offsetY,
                    0f
                );

                // Validate spawn position is within terrain bounds
                const int TERRAIN_SIZE = 256;
                float terrainScale = Constants.TERRAIN_SCALE;
                int spawnX = (int)(spawnPos.X / terrainScale);
                int spawnY = (int)(spawnPos.Y / terrainScale);

                // Skip if out of bounds
                if (spawnX < 0 || spawnX >= TERRAIN_SIZE ||
                    spawnY < 0 || spawnY >= TERRAIN_SIZE)
                {
                    continue;
                }

                // Get terrain height
                float terrainHeight = _world.Terrain.RequestTerrainHeight(spawnPos.X, spawnPos.Y);

                // Spawn at flying height (200-600 above terrain)
                float spawnHeight = terrainHeight + 200f + (float)(_random.NextDouble() * 400f);

                // Never inside a building or tree: spawn above what stands there, or elsewhere when it is too tall
                float obstacleTop = _obstacles.TopAt(spawnPos.X, spawnPos.Y);
                if (obstacleTop != BirdObstacleMap.None)
                {
                    float clear = obstacleTop + SPAWN_OBSTACLE_CLEARANCE;
                    if (clear - terrainHeight > MAX_SPAWN_ALTITUDE)
                        continue;
                    spawnHeight = MathF.Max(spawnHeight, clear);
                }

                spawnPos = new Vector3(spawnPos.X, spawnPos.Y, spawnHeight);

                // Random initial angle
                float randomAngle = (float)(_random.NextDouble() * MathF.PI * 2f);
                Vector3 initialAngle = new Vector3(0f, 0f, randomAngle);

                // Calculate initial direction from angle
                float angleRad = initialAngle.Z;
                Vector3 initialDirection = new Vector3(MathF.Cos(angleRad), MathF.Sin(angleRad), 0f);

                // Random scale variation: 0.75 to 1.0 (smaller birds)
                float randomScale = 0.75f + (float)_random.NextDouble() * 0.25f;

                var bird = new BirdObject
                {
                    Obstacles = _obstacles,
                    Position = spawnPos,
                    Scale = randomScale,
                    Angle = initialAngle,
                    Direction = initialDirection
                };

                _birds.Add(bird);
                _world.Objects.Add(bird);

                spawnCount++;
            }

            _isSpawning = false;
        }

        /// <summary>
        /// Applies weak boid behavior - birds fly in loose flocks
        /// </summary>
        private void ApplyBoidBehavior(BirdObject bird, int birdIndex, GameTime gameTime)
        {
            Vector3 separation = Vector3.Zero;
            Vector3 alignment = Vector3.Zero;
            Vector3 cohesion = Vector3.Zero;
            int neighborCount = 0;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Find neighbors (only other flying birds)
            for (int i = 0; i < _birds.Count; i++)
            {
                if (i == birdIndex)
                    continue;

                var other = _birds[i];

                // Only flock with other flying birds
                if (other.AIState != BirdObject.BirdAIState.Flying)
                    continue;

                float distance = Vector3.Distance(bird.Position, other.Position);

                if (distance < NEIGHBOR_RADIUS && distance > 0.1f)
                {
                    neighborCount++;

                    // Separation: avoid collisions
                    if (distance < SEPARATION_DISTANCE)
                    {
                        Vector3 away = bird.Position - other.Position;
                        if (away.LengthSquared() > 0.001f)
                        {
                            away.Normalize();
                            separation += away / distance;
                        }
                    }

                    // Alignment
                    alignment += other.Direction;

                    // Cohesion
                    cohesion += other.Position;
                }
            }

            if (neighborCount > 0)
            {
                alignment /= neighborCount;
                cohesion /= neighborCount;

                cohesion = cohesion - bird.Position;
                if (cohesion.LengthSquared() > 0.001f)
                {
                    cohesion.Normalize();
                }

                // Apply weak forces (birds have weak flocking)
                Vector3 boidInfluence =
                    separation * SEPARATION_STRENGTH +
                    alignment * ALIGNMENT_STRENGTH +
                    cohesion * COHESION_STRENGTH;

                boidInfluence *= deltaTime;

                Vector3 currentDir = bird.Direction;
                if (currentDir.LengthSquared() < 0.001f)
                    currentDir = Vector3.UnitX;

                bird.Direction = currentDir + boidInfluence;
            }
        }

        /// <summary>
        /// Hides birds that leave the terrain. Walls and towns do not stop birds: GOBoid.cpp MoveBird checks no terrain
        /// flags, so birds fly over the SafeZone town and over NoMove walls as well.
        /// </summary>
        private void CheckBoundaries(BirdObject bird)
        {
            const int TERRAIN_SIZE = 256;
            float terrainScale = Constants.TERRAIN_SCALE;
            int terrainX = (int)(bird.Position.X / terrainScale);
            int terrainY = (int)(bird.Position.Y / terrainScale);

            if (terrainX < 0 || terrainX >= TERRAIN_SIZE ||
                terrainY < 0 || terrainY >= TERRAIN_SIZE)
            {
                bird.Hidden = true;
            }
        }

        /// <summary>
        /// Clears all birds from the world
        /// </summary>
        public void Clear()
        {
            foreach (var bird in _birds)
            {
                _world.Objects.Remove(bird);
                bird.Dispose();
            }
            _birds.Clear();
        }
    }
}
