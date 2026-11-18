using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Worlds.Lorencia
{
    /// <summary>
    /// Manages bird spawning, movement, and despawning in Lorencia world.
    /// Birds steer toward the projected path of nearby flockmates.
    /// </summary>
    public partial class BirdManager
    {
        private const int MAX_BIRDS = 5;
        private const float NEIGHBOR_RADIUS = 400f;
        private const float SEPARATION_DISTANCE = 80f;
        private const float NEIGHBOR_LOOK_AHEAD_SECONDS = 6f / BirdObject.ReferenceTicksPerSecond;
        private const float MIN_SPAWN_DISTANCE = 650f;
        private const float MAX_SPAWN_DISTANCE = 1100f;
        private const float SPAWN_VIEW_MARGIN = 90f;
        private const float DESPAWN_DISTANCE = 1500f;
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

                if (bird.Status is GameControlStatus.Disposed or GameControlStatus.Error ||
                    bird.GetDistanceFromHero(heroPosition) >= DESPAWN_DISTANCE || !IsWithinTerrain(bird.Position))
                {
                    _world.Objects.Remove(bird);
                    _birds.RemoveAt(i);
                    bird.Dispose();
                    continue;
                }

                // Keep the bird at its launch point until the world publishes its model.
                if (!bird.Visible)
                    continue;

                bird.UpdateMovement(gameTime, heroPosition);

                Vector2 flockDirection = bird.AIState != BirdObject.BirdAIState.OnGround && !bird.IsAvoiding
                    ? GetFlockDirection(bird, i)
                    : Vector2.Zero;

                bird.ApplyMovement(gameTime, flockDirection);

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
        /// Gives birds room to fly into the camera view.
        /// </summary>
        private void SpawnBirds(Vector3 heroPosition)
        {
            _isSpawning = true;

            int spawnCount = 0;
            int maxAttempts = MAX_SPAWN_PER_FRAME * 5;
            BoundingFrustum frustum = Camera.Instance.Frustum;

            for (int attempt = 0; attempt < maxAttempts && spawnCount < MAX_SPAWN_PER_FRAME; attempt++)
            {
                if (_birds.Count >= MAX_BIRDS)
                    break;

                float bearing = (float)_random.NextDouble() * MathHelper.TwoPi;
                float distance = MathHelper.Lerp(MIN_SPAWN_DISTANCE, MAX_SPAWN_DISTANCE, (float)_random.NextDouble());
                var spawnPos = new Vector3(
                    heroPosition.X + MathF.Cos(bearing) * distance,
                    heroPosition.Y + MathF.Sin(bearing) * distance,
                    0f);

                if (!IsWithinTerrain(spawnPos))
                    continue;

                // Get terrain height
                float terrainHeight = _world.Terrain.RequestTerrainHeight(spawnPos.X, spawnPos.Y);

                float spawnHeight = terrainHeight + _random.Next(150, 350);

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

                // The last attempts may use a visible position when the camera is zoomed out.
                if (attempt < maxAttempts - MAX_SPAWN_PER_FRAME &&
                    frustum.Contains(new BoundingSphere(spawnPos, SPAWN_VIEW_MARGIN)) != ContainmentType.Disjoint)
                {
                    continue;
                }

                // Aim back toward the area around the player with room for different flight paths.
                float heading = bearing + MathHelper.Pi + ((float)_random.NextDouble() - 0.5f) * MathHelper.PiOver2;

                // Random scale variation: 0.75 to 1.0 (smaller birds)
                float randomScale = 0.75f + (float)_random.NextDouble() * 0.25f;

                var bird = new BirdObject
                {
                    Obstacles = _obstacles,
                    Position = spawnPos,
                    Scale = randomScale,
                    Direction = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f),
                    Angle = new Vector3(0f, 0f, MathHelper.WrapAngle(heading + MathHelper.PiOver2))
                };

                _birds.Add(bird);
                _world.Objects.Add(bird);

                spawnCount++;
            }

            _isSpawning = false;
        }

        /// <summary>
        /// Averages headings toward where neighbors are flying, with separation at close range.
        /// </summary>
        private Vector2 GetFlockDirection(BirdObject bird, int birdIndex)
        {
            Vector2 direction = Vector2.Zero;

            for (int i = 0; i < _birds.Count; i++)
            {
                if (i == birdIndex)
                    continue;

                var other = _birds[i];
                if (!other.Visible || other.AIState == BirdObject.BirdAIState.OnGround)
                    continue;

                Vector3 offset = other.Position - bird.Position;
                float distanceSquared = offset.LengthSquared();
                if (distanceSquared >= NEIGHBOR_RADIUS * NEIGHBOR_RADIUS)
                    continue;

                Vector2 target = new Vector2(offset.X, offset.Y);
                target = distanceSquared < SEPARATION_DISTANCE * SEPARATION_DISTANCE
                    ? -target
                    : target + other.FlightVelocity * NEIGHBOR_LOOK_AHEAD_SECONDS;

                if (target.LengthSquared() > 0.001f)
                    direction += Vector2.Normalize(target);
            }

            return direction;
        }

        private static bool IsWithinTerrain(Vector3 position)
        {
            const float mapSize = Constants.TERRAIN_SIZE * Constants.TERRAIN_SCALE;
            return position.X >= 0f && position.X < mapSize && position.Y >= 0f && position.Y < mapSize;
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
