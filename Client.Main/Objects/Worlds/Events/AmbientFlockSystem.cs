#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Events
{
    /// <summary>
    /// Self-contained flock for the shared GOBoid.cpp pool rules:
    /// spawns boids around the hero (source box Hero±512), recycles slots on
    /// Range >= 1500 or rand_fps_check(512), never popping in view.
    /// </summary>
    public sealed class AmbientFlockSystem : EffectObject
    {
        private const float DespawnRange = 1500f;

        private readonly WalkableWorldControl _world;
        private readonly string _modelPath;
        private readonly float _scale;
        private readonly BoidFlightStyle _style;
        private readonly int _maxBoids;
        private readonly List<AmbientBoid> _boids = new();
        private float _spawnCooldown;

        public AmbientFlockSystem(
            WalkableWorldControl world,
            string modelPath,
            float scale,
            BoidFlightStyle style,
            int maxBoids)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _modelPath = modelPath;
            _scale = scale;
            _style = style;
            _maxBoids = Math.Max(1, maxBoids);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (_world.Status != GameControlStatus.Ready || walker == null)
                return;

            Vector3 heroPosition = walker.Position;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            for (int i = _boids.Count - 1; i >= 0; i--)
            {
                var boid = _boids[i];
                float distSq = Vector2.DistanceSquared(
                    new Vector2(boid.Position.X, boid.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));

                if (boid.Status == GameControlStatus.Disposed ||
                    distSq > DespawnRange * DespawnRange ||
                    boid.ShouldDespawnRandomly(dt))
                {
                    Children.Remove(boid);
                    boid.Dispose();
                    _boids.RemoveAt(i);
                }
            }

            // Slot refill like the source loop (dead slot -> CreateBoid next pass)
            if (_boids.Count >= _maxBoids)
                return;

            _spawnCooldown -= dt;
            if (_spawnCooldown > 0f)
                return;

            _spawnCooldown = 1f;

            var spawned = new AmbientBoid(_modelPath, _scale, _style);
            spawned.BeginLife(heroPosition);

            const int terrainSize = Constants.TERRAIN_SIZE;
            int tileX = (int)(spawned.Position.X / Constants.TERRAIN_SCALE);
            int tileY = (int)(spawned.Position.Y / Constants.TERRAIN_SCALE);
            bool tileValid =
                tileX >= 0 && tileX < terrainSize &&
                tileY >= 0 && tileY < terrainSize &&
                !_world.Terrain.RequestTerrainFlag(tileX, tileY).HasFlag(Client.Data.ATT.TWFlags.NoGround);

            if (!tileValid)
            {
                spawned.Dispose();
                return;
            }

            _boids.Add(spawned);
            Children.Add(spawned);
            _ = spawned.Load();
        }

        public override void Dispose()
        {
            foreach (var boid in _boids)
                boid.Dispose();
            _boids.Clear();
            base.Dispose();
        }
    }
}
