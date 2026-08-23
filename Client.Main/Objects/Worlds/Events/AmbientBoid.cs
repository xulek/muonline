#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Events
{
    public enum BoidFlightStyle
    {
        /// <summary>Birds/crows/eagles: altitude band 200-600 + jitter (MoveBird).</summary>
        FlyingHigh,
        /// <summary>Bats: absolute hover terrain + (-|sin(T)|*150+350)*factor (MoveBat).</summary>
        FlyingLow,
        /// <summary>Rats: ground scurry stuck to terrain height.</summary>
        GroundScurry
    }

    /// <summary>
    /// GOBoid.cpp ambient fauna boid, verbatim rules:
    /// CreateBoid: Velocity=1, Light=(0.5), Alpha 0->1 fade-in, Gravity=13 deg/frame,
    /// AI=BOID_FLY, LifeTime=0 (no expiry), CurrentAction=0.
    /// MoveBoidGroup: advance local (+Velocity*25, 0, Dir2) per reference frame
    /// (REFERENCE_FPS=25 -> Velocity*625 u/s). MoveBoid: steer toward flock target
    /// at Gravity deg/frame. Despawn only Range>=1500 or rand_fps_check(512).
    /// </summary>
    public sealed class AmbientBoid : ModelObject
    {
        private const float ReferenceFps = 25f;

        private readonly BoidFlightStyle _style;
        private readonly float _speedUnitsPerSecond;
        private float _gravityDegrees = 13f;
        private float _timer;

        private Vector2 _wanderTarget;
        private float _wanderRetargetTimer = 1f;

        public bool IsFadedOut { get; private set; }
        public bool Live { get; set; } = true;

        public AmbientBoid(string modelPath, float scale, BoidFlightStyle style)
        {
            ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
            Scale = scale;
            _style = style;
            // Ground scurry critters (rats/scorpions) come from the separate fish-pool
            // crawl rules in the source (MoveFishs, Velocity ~0.6/scale) -> scaled down.
            _speedUnitsPerSecond = 1f * 25f * ReferenceFps *
                                   (_style == BoidFlightStyle.GroundScurry ? 0.12f : 1f);

            Alpha = 0f;
            LightEnabled = true;
            Light = new Vector3(0.5f);

            // Source renders boids at PlaySpeed 1.0 (~25 keys/s); client AnimationSpeed 15
            // matches the fast flutter precedent set by ButterflyObject.
            AnimationSpeed = style == BoidFlightStyle.GroundScurry ? 4f : 15f;
        }

        private string ModelPath { get; }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare(ModelPath);
            if (Model != null)
                CurrentAction = 0;

            await base.Load();
        }

        public void BeginLife(Vector3 position)
        {
            // Spawn at the camera perimeter (650-1100 units) so nothing pops into view;
            // flyers start at terrain+150..350.
            double spawnAngle = MuGame.Random.NextDouble() * Math.PI * 2.0;
            float dist = 650f + (float)MuGame.Random.NextDouble() * 450f;
            float sx = position.X + MathF.Cos((float)spawnAngle) * dist;
            float sy = position.Y + MathF.Sin((float)spawnAngle) * dist;
            float baseZ = World?.Terrain?.RequestTerrainHeight(sx, sy) ?? position.Z;

            Position = new Vector3(
                sx,
                sy,
                _style == BoidFlightStyle.GroundScurry ? baseZ : baseZ + 150f + MuGame.Random.Next(200));

            Angle = new Vector3(0f, 0f, (float)(MuGame.Random.NextDouble() * Math.PI * 2.0));
            Live = true;
            IsFadedOut = false;
            Alpha = 0f;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || World?.Terrain == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            float legacyFrames = dt * ReferenceFps;
            float animFactor = MathF.Min(legacyFrames, 1f);
            _timer += 0.2f * legacyFrames;

            SteerTowardWanderTarget(dt);

            Vector3 forward = new(MathF.Sin(Angle.Z), -MathF.Cos(Angle.Z), 0f);
            Position += forward * _speedUnitsPerSecond * dt;

            float terrainHeight = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);

            switch (_style)
            {
                case BoidFlightStyle.FlyingHigh:
                {
                    // MoveBird: climb below 200, descend above 600, +-8 jitter/frame
                    float z = Position.Z;
                    if (z < terrainHeight + 200f) z += 10f * legacyFrames;
                    else if (z > terrainHeight + 600f) z -= 10f * legacyFrames;
                    z += (MuGame.Random.Next(16) - 8) * animFactor;
                    Position = new Vector3(Position.X, Position.Y, z);
                    break;
                }

                case BoidFlightStyle.FlyingLow:
                {
                    // MoveBat verbatim: Z reset every frame to
                    // terrain + (-abs(sin(Timer))*150 + 350) * FPS_ANIMATION_FACTOR
                    float lift = (-MathF.Abs(MathF.Sin(_timer)) * 150f + 350f) * animFactor;
                    Position = new Vector3(Position.X, Position.Y, terrainHeight + lift);
                    break;
                }

                case BoidFlightStyle.GroundScurry:
                    Position = new Vector3(Position.X, Position.Y, terrainHeight);
                    break;
            }

            if (Alpha < 1f)
                Alpha = MathF.Min(1f, Alpha + dt * 1.25f);
        }

        private void SteerTowardWanderTarget(float dt)
        {
            _wanderRetargetTimer -= dt;
            var walker = (World as WalkableWorldControl)?.Walker;
            if (_wanderRetargetTimer <= 0f && walker != null)
            {
                _wanderRetargetTimer = 3f + (float)MuGame.Random.NextDouble() * 3f;
                _wanderTarget = new Vector2(
                    walker.Position.X + MuGame.Random.Next(1024) - 512f,
                    walker.Position.Y + MuGame.Random.Next(1024) - 512f);
            }

            float dx = _wanderTarget.X - Position.X;
            float dy = _wanderTarget.Y - Position.Y;
            if (dx * dx + dy * dy < 100f)
                return;

            // TurnAngle: shortest-arc step of Gravity degrees per reference frame
            float targetDeg = (float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
            float currentDeg = MathHelper.ToDegrees(Angle.Z);
            float delta = (targetDeg - currentDeg) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;

            float legacyFrames = dt * ReferenceFps;
            float step = MathHelper.Clamp(delta, -_gravityDegrees * legacyFrames, _gravityDegrees * legacyFrames);
            Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + MathHelper.ToRadians(step));
        }

        /// <summary>rand_fps_check(512): natural random despawn from the source loop.</summary>
        public bool ShouldDespawnRandomly(float dt) =>
            (float)MuGame.Random.NextDouble() < dt * ReferenceFps / 512f;
    }
}
