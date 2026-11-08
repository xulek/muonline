using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    /// <summary>
    /// Represents a flying bird in Lorencia world.
    /// Birds have 4 AI states: Flying (200-600 height), Descending, OnGround, Ascending
    /// Based on original client's MoveBird algorithm with BOID_FLY/DOWN/GROUND/UP states.
    /// </summary>
    public class BirdObject : ModelObject
    {
        private Random _random = new Random();
        private float _timer;
        private float _baseSpeed;
        private float _directionChangeTimer;

        // 3D VELOCITY SYSTEM
        private Vector3 _currentVelocity;
        private Vector3 _desiredDirection;
        private float _currentSpeed;
        private float _targetSpeed;
        private float _targetHeight;

        // AI STATE SYSTEM
        private BirdAIState _aiState;
        private float _stateTimer; // Time spent in current state

        private const float DirectionChangeInterval = 1.0f; // Change direction frequently for chaotic flight
        private const float SteeringSpeed = 2.5f; // Fast steering for erratic movement
        private const float RotationSpeed = 4.0f;
        private const float MinSpeedMultiplier = 1.0f;
        private const float MaxSpeedMultiplier = 1.3f;
        private const float MinFlightHeight = 350f; // Birds fly high (original: 200-600)
        private const float MaxFlightHeight = 450f;
        private const float LandCheckDistance = 200f; // Start checking for landing at 200 units
        private const float LandCheckMaxDistance = 400f; // Stop checking at 400 units
        private const float VerticalLerpSpeed = 0.3f; // Moderate vertical movement
        private const float DescendSpeed = 300f; // Units per second while landing

        // OBSTACLE AVOIDANCE (BirdObstacleMap): climb over what is ahead, turn away from what is too tall to climb
        private const float ClimbSpeed = 500f;
        private const float ObstacleClearance = 80f; // Margin above an obstacle's top
        private const float MaxClimbAltitude = 700f; // Above terrain; taller obstacles are flown around
        private const float MinLookAhead = 300f;
        private const float LookAheadSeconds = 0.9f;
        private const float LookAheadStep = 100f;
        private const float TurnLookAheadFactor = 1.5f;
        private static readonly float[] TurnAngles = { 0.7f, 1.4f, 2.1f, MathF.PI };

        private float _requiredZ = BirdObstacleMap.None; // Altitude needed to clear what is ahead this frame
        private float _avoidTimer; // While > 0 the bird keeps the heading it turned to
        private float _footHeight; // Origin height above the lowest point of the mesh (the feet)

        public enum BirdAIState
        {
            Flying,      // Normal flight at 200-600 height
            Descending,  // Flying down to ground
            OnGround,    // Resting on terrain
            Ascending    // Taking off from ground
        }

        // Legacy properties
        public Vector3 Direction
        {
            get => _desiredDirection;
            set => _desiredDirection = value.LengthSquared() > 0.001f ? Vector3.Normalize(value) : value;
        }
        public float Velocity { get; set; }
        public float Timer
        {
            get => _timer;
            set => _timer = value;
        }

        public int SubType { get; set; }
        public BirdAIState AIState => _aiState;

        /// <summary>Solid map objects to avoid; set by <see cref="BirdManager"/>.</summary>
        internal BirdObstacleMap Obstacles { get; set; }

        /// <summary>Seconds since the bird spawned.</summary>
        internal float Age { get; private set; }

        /// <summary>True while the bird holds the heading it just turned to, away from an obstacle.</summary>
        internal bool IsAvoiding => _avoidTimer > 0f;

        public BirdObject()
        {
            _timer = (float)(_random.NextDouble() * MathF.PI * 2);
            _baseSpeed = 600f + (float)_random.NextDouble() * 50f; // 100-150 units/sec (much faster!)
            _currentSpeed = _baseSpeed;
            _targetSpeed = _baseSpeed;
            // Scale is set by BirdManager during spawn
            Alpha = 1f;
            LightEnabled = true; // Enable lighting for shadows
            BlendState = BlendState.AlphaBlend;
            IsTransparent = true;
            RenderShadow = true;
            DepthState = DepthStencilState.Default; // Use default depth state for proper rendering
            _directionChangeTimer = 0f;

            // Initialize velocity system
            _currentVelocity = Vector3.Zero;
            _desiredDirection = Vector3.Zero;
            _targetHeight = MinFlightHeight + (float)_random.NextDouble() * (MaxFlightHeight - MinFlightHeight);

            // Start in flying state
            _aiState = BirdAIState.Flying;
            _stateTimer = 0f;

            SubType = 0;
        }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            var modelPath = "Object1/Bird01.bmd";
            Model = await BMDLoader.Instance.Prepare(modelPath);

            if (Model != null)
            {
                CurrentAction = 0;
                AnimationSpeed = 12.0f; // Wing flapping speed
            }

            await base.Load();

            // Initialize velocity to match initial angle
            float angleRad = Angle.Z;
            _desiredDirection = new Vector3(MathF.Cos(angleRad), MathF.Sin(angleRad), 0f);
            _currentVelocity = _desiredDirection * _currentSpeed;
        }

        public override void Update(GameTime gameTime)
        {
            if (Status == GameControlStatus.NonInitialized)
            {
                _ = Load();
                return;
            }

            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Fade in alpha
            if (Alpha < 1f)
            {
                Alpha += deltaTime * 2f;
                if (Alpha > 1f) Alpha = 1f;
            }
        }

        /// <summary>
        /// Updates bird behavior based on AI state and hero position.
        /// Birds periodically land when hero is at distance 200-400.
        /// </summary>
        public void UpdateMovement(GameTime gameTime, Vector3 heroPosition)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _timer += deltaTime;
            _stateTimer += deltaTime;
            _directionChangeTimer += deltaTime;
            Age += deltaTime;
            if (_avoidTimer > 0f)
                _avoidTimer -= deltaTime;

            float distanceFromHero = Vector2.Distance(
                new Vector2(Position.X, Position.Y),
                new Vector2(heroPosition.X, heroPosition.Y));

            // STATE MACHINE
            switch (_aiState)
            {
                case BirdAIState.Flying:
                    UpdateFlyingState(deltaTime, distanceFromHero);
                    break;

                case BirdAIState.Descending:
                    UpdateDescendingState(deltaTime);
                    break;

                case BirdAIState.OnGround:
                    UpdateGroundState(deltaTime, heroPosition);
                    break;

                case BirdAIState.Ascending:
                    UpdateAscendingState(deltaTime);
                    break;
            }
        }

        private void UpdateFlyingState(float deltaTime, float distanceFromHero)
        {
            // Occasional chirping while flying
            if (_random.NextDouble() < deltaTime * 0.05f) // 5% chance per second
            {
                PlayBirdSound();
            }

            // Speed variation
            if (_random.NextDouble() < deltaTime * 0.5f)
            {
                float speedMultiplier = MinSpeedMultiplier + (float)_random.NextDouble() * (MaxSpeedMultiplier - MinSpeedMultiplier);
                _targetSpeed = _baseSpeed * speedMultiplier;
            }

            _currentSpeed = MathHelper.Lerp(_currentSpeed, _targetSpeed, MathHelper.Clamp(deltaTime * 0.8f, 0f, 1f));

            // Direction changes (less chaotic than butterflies)
            // The random heading changes below are paused while the bird holds a heading it turned to
            // to get away from an obstacle.
            if (_directionChangeTimer >= DirectionChangeInterval && _avoidTimer <= 0f)
            {
                _directionChangeTimer = 0f;
                float randomAngle = (float)(_random.NextDouble() * MathF.PI * 2f);
                Vector3 newDirection = new Vector3(MathF.Cos(randomAngle), MathF.Sin(randomAngle), 0f);
                _desiredDirection = Vector3.Lerp(_desiredDirection, newDirection, 0.5f);
                if (_desiredDirection.LengthSquared() > 0.001f)
                {
                    _desiredDirection = Vector3.Normalize(_desiredDirection);
                }

                // Change target height within flight range
                _targetHeight = MinFlightHeight + (float)_random.NextDouble() * (MaxFlightHeight - MinFlightHeight);
            }

            // CHAOTIC MOVEMENT - frequent erratic direction changes like butterflies
            if (_avoidTimer <= 0f && _random.NextDouble() < 0.4f) // 40% chance per frame for chaotic flight
            {
                float nudgeAngle = (float)(_random.NextDouble() * MathF.PI * 2f);
                Vector3 nudge = new Vector3(MathF.Cos(nudgeAngle), MathF.Sin(nudgeAngle), 0f);
                _desiredDirection = Vector3.Lerp(_desiredDirection, nudge, 0.2f); // Stronger nudge
                if (_desiredDirection.LengthSquared() > 0.001f)
                {
                    _desiredDirection = Vector3.Normalize(_desiredDirection);
                }
            }

            // Additional erratic movement
            if (_avoidTimer <= 0f && _random.NextDouble() < deltaTime * 8f) // Frequent small adjustments
            {
                float erraticAngle = (float)(_random.NextDouble() * MathF.PI * 2f);
                Vector3 erratic = new Vector3(MathF.Cos(erraticAngle), MathF.Sin(erraticAngle), 0f);
                _desiredDirection = Vector3.Lerp(_desiredDirection, erratic, 0.12f);
                if (_desiredDirection.LengthSquared() > 0.001f)
                {
                    _desiredDirection = Vector3.Normalize(_desiredDirection);
                }
            }

            // CHECK FOR LANDING - if hero is at distance 200-400, occasionally land
            // Original: if ((int)WorldTime % 8192 < 2048) - roughly 25% of time
            if (_stateTimer > 5f && // Don't check immediately after state change
                distanceFromHero >= LandCheckDistance &&
                distanceFromHero <= LandCheckMaxDistance)
            {
                if (_random.NextDouble() < deltaTime * 0.3f) // 30% chance per second
                {
                    _aiState = BirdAIState.Descending;
                    _stateTimer = 0f;
                }
            }
        }

        private void UpdateDescendingState(float deltaTime)
        {
            // Descend rapidly - o->Direction[2] = -20.f. The bird slows down on the way so it lands near where it
            // started the descent instead of far ahead of it.
            _currentSpeed = MathHelper.Lerp(_currentSpeed, _baseSpeed * 0.35f, MathHelper.Clamp(deltaTime * 2f, 0f, 1f));
            // No horizontal direction changes while descending
        }

        private void UpdateGroundState(float deltaTime, Vector3 heroPosition)
        {
            // On ground - wait for hero to approach or random takeoff
            _currentSpeed = 0f; // Stopped

            // Check if hero is moving (walking) - would need hero state, simplified to distance check
            float distanceFromHero = Vector2.Distance(
                new Vector2(Position.X, Position.Y),
                new Vector2(heroPosition.X, heroPosition.Y));

            // Take off if hero gets close OR random chance
            if (distanceFromHero < 150f || _random.NextDouble() < deltaTime * 0.1f) // 10% chance per second
            {
                _aiState = BirdAIState.Ascending;
                _stateTimer = 0f;
                _currentSpeed = _baseSpeed * 1.1f; // Boost speed for takeoff
                CurrentAction = 0; // Reset animation
            }
        }

        private void UpdateAscendingState(float deltaTime)
        {
            // Ascending - velocity slowly decreases
            _currentSpeed -= 0.005f * 60f * deltaTime; // Original: 0.005f * FPS_ANIMATION_FACTOR

            // Random height fluctuations during ascent
            if (_random.NextDouble() < deltaTime * 10f)
            {
                _targetHeight += (float)(_random.NextDouble() * 16f - 8f);
            }

            // When velocity reaches normal, switch to flying
            if (_currentSpeed <= _baseSpeed)
            {
                _currentSpeed = _baseSpeed;
                _aiState = BirdAIState.Flying;
                _stateTimer = 0f;
            }
        }

        /// <summary>Lowest world-space Z of the mesh in the current pose, over all vertices.</summary>
        internal float MeasureLowestWorldZ()
        {
            var bones = BoneTransform;
            var meshes = Model?.Meshes;
            if (bones == null || meshes == null)
                return Position.Z;

            Matrix world = WorldPosition;
            float lowest = float.MaxValue;
            foreach (var mesh in meshes)
            {
                if (mesh.Vertices == null)
                    continue;
                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.Node < 0 || vertex.Node >= bones.Length)
                        continue;
                    float z = Vector3.Transform(Vector3.Transform(vertex.Position, bones[vertex.Node]), world).Z;
                    if (z < lowest)
                        lowest = z;
                }
            }
            return lowest == float.MaxValue ? Position.Z : lowest;
        }

        /// <summary>Diagnostics (StressHarness): drop straight down from the current position.</summary>
        internal void DiagnosticsDescend()
        {
            _aiState = BirdAIState.Descending;
            _stateTimer = 0f;
            _desiredDirection = Vector3.Zero;
            _currentVelocity = Vector3.Zero;
        }

        /// <summary>
        /// Returns distance from hero (used by BirdManager for despawning)
        /// </summary>
        public float GetDistanceFromHero(Vector3 heroPosition)
        {
            return Vector2.Distance(
                new Vector2(Position.X, Position.Y),
                new Vector2(heroPosition.X, heroPosition.Y));
        }

        /// <summary>
        /// Applies velocity-based movement with AI state-specific behavior.
        /// Uses World.Terrain.RequestTerrainHeight for accurate terrain elevation.
        /// </summary>
        public void ApplyMovement(GameTime gameTime)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Get terrain height at bird's current position using World.Terrain.RequestTerrainHeight
            var walkableWorld = World as Controls.WalkableWorldControl;
            if (walkableWorld?.Terrain == null)
                return;

            float terrainHeight = walkableWorld.Terrain.RequestTerrainHeight(Position.X, Position.Y);

            AvoidObstacles(terrainHeight);

            // Calculate target velocity (horizontal movement)
            Vector3 targetVelocity = _desiredDirection * _currentSpeed;

            // Smoothly steer current velocity towards target velocity
            _currentVelocity = Vector3.Lerp(_currentVelocity, targetVelocity,
                MathHelper.Clamp(deltaTime * SteeringSpeed, 0f, 1f));

            // Dynamic animation speed based on state
            float actualSpeed = _currentVelocity.Length();
            if (_aiState == BirdAIState.OnGround)
            {
                AnimationSpeed = 1.0f; // Slow idle animation
            }
            else
            {
                float speedRatio = actualSpeed / _baseSpeed;
                AnimationSpeed = MathHelper.Clamp(5.0f + speedRatio * 4.0f, 3.0f, 10.0f);
            }

            // Apply horizontal movement
            Position = new Vector3(
                Position.X + _currentVelocity.X * deltaTime,
                Position.Y + _currentVelocity.Y * deltaTime,
                Position.Z
            );

            // VERTICAL MOVEMENT based on AI state
            float desiredZ = 0f;

            switch (_aiState)
            {
                case BirdAIState.Flying:
                    // Random height fluctuations: o->Position[2] += (float)(rand()%16-8)
                    if (_random.NextDouble() < deltaTime * 5f)
                    {
                        _targetHeight += (float)(_random.NextDouble() * 16f - 8f);
                    }

                    // Clamp to flight range
                    _targetHeight = MathHelper.Clamp(_targetHeight, MinFlightHeight, MaxFlightHeight);
                    desiredZ = terrainHeight + _targetHeight;

                    // SLOW smooth lerp to target height for natural flight
                    Position = new Vector3(Position.X, Position.Y,
                        MathHelper.Lerp(Position.Z, desiredZ, MathHelper.Clamp(deltaTime * VerticalLerpSpeed, 0f, 1f)));
                    break;

                case BirdAIState.Descending:
                    float newDescendZ = Position.Z - DescendSpeed * deltaTime;
                    float groundLevel = terrainHeight + FootHeight();

                    if (_requiredZ > newDescendZ)
                    {
                        // Something solid is in the way, or the ground below is taken: give up landing.
                        _aiState = BirdAIState.Ascending;
                        _stateTimer = 0f;
                        _currentSpeed = _baseSpeed * 1.1f;
                    }
                    else if (newDescendZ <= groundLevel)
                    {
                        // The feet touch the terrain
                        Position = new Vector3(Position.X, Position.Y, groundLevel);
                        _aiState = BirdAIState.OnGround;
                        _stateTimer = 0f;

                        // Play landing sound
                        PlayBirdSound();
                    }
                    else
                    {
                        Position = new Vector3(Position.X, Position.Y, newDescendZ);
                    }
                    break;

                case BirdAIState.OnGround:
                    // Stand on the terrain: the lowest point of the mesh (feet) sits on it in every animation pose
                    Position = new Vector3(Position.X, Position.Y, terrainHeight + FootHeight());
                    break;

                case BirdAIState.Ascending:
                    // Slow ascent - 3-4x slower (was 20, now 5)
                    Position = new Vector3(Position.X, Position.Y, Position.Z + 5f * deltaTime * 60f);

                    // Play takeoff sound
                    if (_stateTimer < deltaTime * 2f) // Only once at start
                    {
                        PlayBirdSound();
                    }
                    break;
            }

            // Climb over whatever is ahead, faster than the relaxed height changes above
            if ((_aiState == BirdAIState.Flying || _aiState == BirdAIState.Ascending) && _requiredZ > Position.Z)
            {
                Position = new Vector3(Position.X, Position.Y,
                    MathF.Min(_requiredZ, Position.Z + ClimbSpeed * deltaTime));
            }

            // Update visual orientation
            if (_currentVelocity.LengthSquared() > 1.0f && _aiState != BirdAIState.OnGround)
            {
                float targetAngleRaw = MathF.Atan2(_currentVelocity.Y, _currentVelocity.X);
                float targetAngleIsometric = targetAngleRaw + MathHelper.PiOver2;

                float currentAngle = Angle.Z;
                float angleDiff = MathHelper.WrapAngle(targetAngleIsometric - currentAngle);
                float maxRotationStep = RotationSpeed * deltaTime;
                float rotationStep = MathHelper.Clamp(angleDiff, -maxRotationStep, maxRotationStep);

                Angle = new Vector3(Angle.X, Angle.Y, currentAngle + rotationStep);
            }
        }

        /// <summary>
        /// Looks along the heading for solid map objects. Sets <see cref="_requiredZ"/> to the altitude that clears
        /// them and, when they are too tall to climb over, turns the bird to a clear heading.
        /// </summary>
        private void AvoidObstacles(float terrainHeight)
        {
            _requiredZ = BirdObstacleMap.None;
            if (Obstacles == null || _aiState == BirdAIState.OnGround)
                return;

            float maxZ = terrainHeight + MaxClimbAltitude;
            var velocity = new Vector2(_currentVelocity.X, _currentVelocity.Y);
            float speed = velocity.Length();

            // Hardly moving (just taking off or landing): only what is right under the bird counts
            if (speed < 1f)
            {
                _requiredZ = MathF.Min(RequiredAltitude(Position.X, Position.Y), maxZ);
                return;
            }

            Vector2 heading = velocity / speed;
            float lookAhead = MathF.Max(MinLookAhead, speed * LookAheadSeconds);
            float required = RequiredAltitudeAlong(heading, lookAhead);

            // Turning takes time, so what is too tall to climb over is looked for further ahead than what is climbed
            float turnLookAhead = lookAhead * TurnLookAheadFactor;
            if (RequiredAltitudeAlong(heading, turnLookAhead) > maxZ)
                TurnAway(heading, turnLookAhead, maxZ);

            _requiredZ = MathF.Min(required, maxZ);
        }

        private float RequiredAltitude(float x, float y)
        {
            float top = Obstacles.TopAt(x, y);
            return top == BirdObstacleMap.None ? BirdObstacleMap.None : top + ObstacleClearance;
        }

        /// <summary>Highest altitude needed to clear the obstacles on the way, sampled once per tile.</summary>
        private float RequiredAltitudeAlong(Vector2 direction, float distance)
        {
            float required = BirdObstacleMap.None;
            for (float d = 0f; d <= distance; d += LookAheadStep)
            {
                float z = RequiredAltitude(Position.X + direction.X * d, Position.Y + direction.Y * d);
                if (z > required)
                    required = z;
            }
            return required;
        }

        /// <summary>Picks the nearest heading (left or right of the current one, random side first) with nothing
        /// ahead that is too tall to climb over, and holds it for a second.</summary>
        private void TurnAway(Vector2 heading, float lookAhead, float maxZ)
        {
            if (_avoidTimer > 0f)
                return;

            float baseAngle = MathF.Atan2(heading.Y, heading.X);
            float side = _random.Next(2) == 0 ? 1f : -1f;
            foreach (float turn in TurnAngles)
            {
                for (int i = 0; i < 2; i++)
                {
                    float angle = baseAngle + (i == 0 ? side : -side) * turn;
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    if (RequiredAltitudeAlong(direction, lookAhead) <= maxZ)
                    {
                        _desiredDirection = new Vector3(direction, 0f);
                        _avoidTimer = 1f;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Height of the origin above the lowest point of the mesh in its current pose (the feet), scaled. The bird
        /// stands on the terrain when its origin is this high.
        /// </summary>
        private float FootHeight()
        {
            var bones = BoneTransform;
            var meshes = Model?.Meshes;
            if (bones == null || meshes == null)
                return _footHeight;

            float lowest = float.MaxValue;
            foreach (var mesh in meshes)
            {
                if (mesh.Vertices == null)
                    continue;
                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.Node < 0 || vertex.Node >= bones.Length)
                        continue;
                    float z = Vector3.Transform(vertex.Position, bones[vertex.Node]).Z;
                    if (z < lowest)
                        lowest = z;
                }
            }

            if (lowest != float.MaxValue)
                _footHeight = -lowest * TotalScale;
            return _footHeight;
        }

        /// <summary>
        /// Plays random bird chirp sound with 3D positional audio
        /// Original: LoadWaveFile(SOUND_BIRD01/02, L"Data\\Sound\\aBird1.wav/aBird2.wav")
        /// </summary>
        private void PlayBirdSound()
        {
            try
            {
                var walkableWorld = World as Controls.WalkableWorldControl;
                if (walkableWorld?.Walker == null)
                    return;

                // Random choice between two bird sounds
                string soundPath = _random.Next(0, 2) == 0
                    ? "Sound/aBird1.wav"
                    : "Sound/aBird2.wav";

                // Play with 3D attenuation - sound gets quieter with distance
                Controllers.SoundController.Instance.PlayBufferWithAttenuation(
                    soundPath,
                    Position,
                    walkableWorld.Walker.Position,
                    maxDistance: 1500f, // Hear birds from far away
                    loop: false
                );
            }
            catch
            {
                // Ignore if sound files don't exist
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Model == null)
                return;

            base.Draw(gameTime);
        }
    }
}
