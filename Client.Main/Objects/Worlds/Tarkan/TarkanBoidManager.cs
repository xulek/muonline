using System;
using System.Collections.Generic;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Manages desert crawler bug flocking behavior in Tarkan world.
    /// Spawns bugs at camera perimeter with smooth alpha fade-in/fade-out to eliminate sudden pop-in and pop-out.
    /// </summary>
    public class TarkanBoidManager
    {
        private const int MAX_BOIDS = 10;
        private readonly List<TarkanBugObject> _bugs = new();
        private readonly Random _random = new();
        private readonly WorldControl _world;
        private readonly WalkableWorldControl _walkableWorld;
        private float _spawnTimer = 0f;

        public TarkanBoidManager(WorldControl world)
        {
            _world = world;
            _walkableWorld = world as WalkableWorldControl;
        }

        public void Update(GameTime gameTime)
        {
            if (_walkableWorld?.Walker == null || _world.Status != GameControlStatus.Ready)
                return;

            Vector3 heroPosition = _walkableWorld.Walker.Position;
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (deltaTime <= 0f) return;

            const int TERRAIN_SIZE = 256;
            float terrainScale = Constants.TERRAIN_SCALE;

            // 1. Maintain active pool of bugs - spawn at camera perimeter
            _spawnTimer -= deltaTime;
            if (_spawnTimer <= 0f && _bugs.Count < MAX_BOIDS)
            {
                _spawnTimer = 0.5f;

                // Spawn in a ring around the hero (600 to 1000 units away, entering screen naturally)
                float spawnAngle = (float)(_random.NextDouble() * Math.PI * 2.0);
                float spawnDistance = 650f + (float)_random.NextDouble() * 350f;
                float spawnOffsetX = MathF.Cos(spawnAngle) * spawnDistance;
                float spawnOffsetY = MathF.Sin(spawnAngle) * spawnDistance;
                Vector3 spawnPos = new Vector3(heroPosition.X + spawnOffsetX, heroPosition.Y + spawnOffsetY, heroPosition.Z);

                int spawnTileX = (int)(spawnPos.X / terrainScale);
                int spawnTileY = (int)(spawnPos.Y / terrainScale);

                if (spawnTileX >= 0 && spawnTileX < TERRAIN_SIZE && spawnTileY >= 0 && spawnTileY < TERRAIN_SIZE)
                {
                    var spawnFlag = _world.Terrain.RequestTerrainFlag(spawnTileX, spawnTileY);
                    if (!spawnFlag.HasFlag(Client.Data.ATT.TWFlags.NoGround))
                    {
                        float terrainHeight = _world.Terrain.RequestTerrainHeight(spawnPos.X, spawnPos.Y);
                        spawnPos.Z = terrainHeight;

                        // Point initial heading roughly towards the player's general vicinity with random variation
                        float toHeroDx = heroPosition.X - spawnPos.X + (float)(_random.Next(400) - 200);
                        float toHeroDy = heroPosition.Y - spawnPos.Y + (float)(_random.Next(400) - 200);
                        float initialAngleDeg = AngleUtils.CreateAngleDegrees(0f, 0f, toHeroDx, toHeroDy);
                        float initialAngleRad = MathHelper.ToRadians(initialAngleDeg);

                        Vector3 initialForward = new Vector3(MathF.Sin(initialAngleRad), -MathF.Cos(initialAngleRad), 0f);

                        // Scale: 0.85f .. 1.15f
                        float scale = 0.85f + (float)_random.NextDouble() * 0.3f;
                        float velocity = 2.5f / scale;

                        var bug = new TarkanBugObject
                        {
                            Position = spawnPos,
                            Scale = scale,
                            Velocity = velocity,
                            Gravity = 9.0f,
                            LifeTime = 30f + (float)_random.NextDouble() * 25f, // 30-55s life
                            Angle = new Vector3(0f, 0f, initialAngleRad),
                            Direction = spawnPos + initialForward * 3.0f,
                            Live = true,
                            SubType = 0
                        };

                        _bugs.Add(bug);
                        _world.Objects.Add(bug);
                        _ = bug.Load();
                    }
                }
            }

            // 2. Update boid behaviors, movement and smooth lifecycle
            for (int i = _bugs.Count - 1; i >= 0; i--)
            {
                var bug = _bugs[i];

                // Only remove completely from world once smoothly faded out to 0 alpha
                if (bug.IsFadedOut || bug.Status == GameControlStatus.Disposed)
                {
                    _world.Objects.Remove(bug);
                    _bugs.RemoveAt(i);
                    bug.Dispose();
                    continue;
                }

                // Range check to Hero: if too far away, trigger smooth fade-out
                float distToHero = Vector2.Distance(
                    new Vector2(bug.Position.X, bug.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));

                if (distToHero >= 1350f)
                {
                    bug.Live = false;
                }

                if (bug.Live)
                {
                    // Lifetime countdown: when expired, trigger smooth fade-out
                    bug.LifeTime -= deltaTime;
                    if (bug.LifeTime <= 0f)
                    {
                        bug.Live = false;
                    }

                    // Boid steering flocking algorithm (ZzzAI.cpp:200 MoveBoid)
                    int numNeighbors = 0;
                    float targetX = 0f;
                    float targetY = 0f;

                    for (int j = 0; j < _bugs.Count; j++)
                    {
                        if (j == i || !_bugs[j].Live)
                            continue;

                        var other = _bugs[j];
                        float dist = Vector2.Distance(
                            new Vector2(bug.Position.X, bug.Position.Y),
                            new Vector2(other.Position.X, other.Position.Y));

                        if (dist < 400f)
                        {
                            float xdist = other.Direction.X - other.Position.X;
                            float ydist = other.Direction.Y - other.Position.Y;

                            if (dist < 80f)
                            {
                                xdist -= (other.Direction.X - bug.Position.X);
                                ydist -= (other.Direction.Y - bug.Position.Y);
                            }
                            else
                            {
                                xdist += (other.Direction.X - bug.Position.X);
                                ydist += (other.Direction.Y - bug.Position.Y);
                            }

                            float pdist = MathF.Sqrt(xdist * xdist + ydist * ydist);
                            if (pdist > 0.001f)
                            {
                                targetX += xdist / pdist;
                                targetY += ydist / pdist;
                                numNeighbors++;
                            }
                        }
                    }

                    if (numNeighbors > 0)
                    {
                        targetX = bug.Position.X + targetX / numNeighbors;
                        targetY = bug.Position.Y + targetY / numNeighbors;
                        float dx = targetX - bug.Position.X;
                        float dy = targetY - bug.Position.Y;

                        float targetAngleDeg = AngleUtils.CreateAngleDegrees(0f, 0f, dx, dy);
                        float targetAngleRad = MathHelper.ToRadians(targetAngleDeg);
                        bug.TurnAngle(targetAngleRad, bug.Gravity, deltaTime);
                    }
                }

                // Forward movement in direction the model is facing
                float forwardSpeed = bug.Velocity * 50.0f;
                Vector3 forwardDir = new Vector3(MathF.Sin(bug.Angle.Z), -MathF.Cos(bug.Angle.Z), 0f);
                Vector3 newPos = bug.Position + forwardDir * forwardSpeed * deltaTime;

                int tileX = (int)(newPos.X / terrainScale);
                int tileY = (int)(newPos.Y / terrainScale);

                if (tileX < 0 || tileX >= TERRAIN_SIZE || tileY < 0 || tileY >= TERRAIN_SIZE)
                {
                    bug.Live = false;
                    continue;
                }

                // Terrain boundary collision: turn 180 degrees smoothly back
                var tileFlag = _world.Terrain.RequestTerrainFlag(tileX, tileY);
                if (tileFlag.HasFlag(Client.Data.ATT.TWFlags.NoGround) || tileFlag.HasFlag(Client.Data.ATT.TWFlags.NoMove))
                {
                    bug.Angle = new Vector3(bug.Angle.X, bug.Angle.Y, MathHelper.WrapAngle(bug.Angle.Z + MathHelper.Pi));
                    bug.SubType++;
                    if (bug.SubType >= 3)
                    {
                        bug.Live = false;
                    }
                }
                else if (bug.SubType > 0)
                {
                    bug.SubType--;
                }

                float height = _world.Terrain.RequestTerrainHeight(newPos.X, newPos.Y);
                newPos.Z = height;

                bug.Position = newPos;
                bug.Direction = newPos + forwardDir * 3.0f;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _bugs.Count; i++)
            {
                var bug = _bugs[i];
                _world.Objects.Remove(bug);
                bug.Dispose();
            }
            _bugs.Clear();
        }
    }
}
