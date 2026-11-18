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
        private Vector3 _direction = Vector3.UnitX;
        private float _currentSpeed = FlightSpeed;
        private float _verticalSpeed;
        private float _verticalVelocity;
        private float _heightJitterTimer;
        private float _heightJitterSpeed;
        private BirdAIState _aiState;

        // MoveBird and MoveBoidGroup express movement in units per frame at 25 FPS.
        internal const float ReferenceTicksPerSecond = 25f;
        private const float ReferenceTick = 1f / ReferenceTicksPerSecond;
        private const float FlightSpeed = 25f * ReferenceTicksPerSecond;
        private const float TurnSpeed = 13f * (MathHelper.Pi / 180f) * ReferenceTicksPerSecond;
        private const float FlockTurnSpeed = MathHelper.Pi;
        private const float WanderTurnSpeed = 0.25f;
        private const float FadeInSeconds = 0.75f;
        private const float FlightVerticalSpeed = 10f * ReferenceTicksPerSecond;
        private const float DiveAndAscentSpeed = 20f * ReferenceTicksPerSecond;
        private const float TakeoffDeceleration = 0.005f * FlightSpeed * ReferenceTicksPerSecond;
        private const float MinFlightHeight = 200f;
        private const float MaxFlightHeight = 600f;
        private const float LandCheckDistance = 200f;
        private const float LandCheckMaxDistance = 400f;

        // OBSTACLE AVOIDANCE (BirdObstacleMap): climb over what is ahead, turn away from what is too tall to climb
        private const float ClimbSpeed = 280f;
        private const float VerticalAcceleration = 520f;
        private const float ClearanceResponse = 1.8f;
        private const float ObstacleClearance = 80f; // Margin above an obstacle's top
        private const float MaxClimbAltitude = 700f; // Above terrain; taller obstacles are flown around
        private const float MinLookAhead = 300f;
        private const float LookAheadSeconds = 1.85f;
        private const float LandingLookAheadSeconds = 0.9f;
        private const float LookAheadStep = Constants.TERRAIN_SCALE / 2f;
        private const float TurnLookAheadSeconds = 1.35f;
        private static readonly float[] TurnAngles = { 0.7f, 1.4f, 2.1f, MathF.PI };

        private float _requiredZ = BirdObstacleMap.None; // Altitude needed to clear what is ahead this frame
        private Vector2 _avoidDirection;
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
            get => _direction;
            set
            {
                var heading = new Vector3(value.X, value.Y, 0f);
                _direction = heading.LengthSquared() > 0.001f ? Vector3.Normalize(heading) : Vector3.UnitX;
            }
        }
        public float Velocity { get; set; }
        public float Timer
        {
            get => _timer;
            set => _timer = value;
        }

        public int SubType { get; set; }
        public BirdAIState AIState => _aiState;
        internal Vector2 FlightVelocity => new Vector2(_direction.X, _direction.Y) * _currentSpeed;

        /// <summary>Solid map objects to avoid; set by <see cref="BirdManager"/>.</summary>
        internal BirdObstacleMap Obstacles { get; set; }

        /// <summary>Seconds since the bird spawned.</summary>
        internal float Age { get; private set; }

        /// <summary>True while the bird holds the heading it just turned to, away from an obstacle.</summary>
        internal bool IsAvoiding => _avoidTimer > 0f;

        public BirdObject()
        {
            _timer = (float)(_random.NextDouble() * MathF.PI * 2);
            // Scale is set by BirdManager during spawn
            Alpha = 0f;
            LightEnabled = true; // Enable lighting for shadows
            BlendState = BlendState.AlphaBlend;
            IsTransparent = true;
            RenderShadow = true;
            ShadowOpacity = 0f;
            DepthState = DepthStencilState.Default; // Use default depth state for proper rendering

            _aiState = BirdAIState.Flying;

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

            if (Alpha < 1f)
            {
                Alpha = MathF.Min(1f, Alpha + deltaTime / FadeInSeconds);
                ShadowOpacity = Alpha;
            }
        }

        /// <summary>
        /// Updates the flight state before steering and moving the bird.
        /// </summary>
        public void UpdateMovement(GameTime gameTime, Vector3 heroPosition)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _timer += deltaTime;
            Age += deltaTime;
            if (_avoidTimer > 0f)
                _avoidTimer -= deltaTime;

            switch (_aiState)
            {
                case BirdAIState.Flying:
                    if (gameTime.TotalGameTime.TotalMilliseconds % 8192 < 2048)
                    {
                        float distanceFromHero = GetDistanceFromHero(heroPosition);
                        if (distanceFromHero >= LandCheckDistance && distanceFromHero <= LandCheckMaxDistance)
                            _aiState = BirdAIState.Descending;
                    }

                    if (_random.NextDouble() < deltaTime * 0.05f)
                        PlayBirdSound();
                    break;

                case BirdAIState.OnGround:
                    if (World is Controls.WalkableWorldControl { Walker: { IsMoving: true } } ||
                        _random.NextDouble() < deltaTime * ReferenceTicksPerSecond / 256f)
                    {
                        StartTakeoff();
                    }
                    break;

                case BirdAIState.Ascending:
                    _currentSpeed -= TakeoffDeceleration * deltaTime;
                    if (_currentSpeed <= FlightSpeed)
                    {
                        _currentSpeed = FlightSpeed;
                        _aiState = BirdAIState.Flying;
                    }
                    break;
            }
        }

        private void StartTakeoff(float ascentSpeed = DiveAndAscentSpeed)
        {
            _verticalVelocity = _aiState == BirdAIState.OnGround ? ascentSpeed : 0f;
            _aiState = BirdAIState.Ascending;
            _currentSpeed = FlightSpeed * 1.1f;
            _verticalSpeed = ascentSpeed;
            CurrentAction = 0;
            PlayBirdSound();
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
            _currentSpeed = 0f;
            _avoidTimer = 0f;
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
        /// Turns toward the flock or around obstacles, then advances along the flight heading.
        /// </summary>
        public void ApplyMovement(GameTime gameTime, Vector2 flockDirection = default)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var walkableWorld = World as Controls.WalkableWorldControl;
            if (walkableWorld?.Terrain == null || deltaTime <= 0f)
                return;

            float terrainHeight = walkableWorld.Terrain.RequestTerrainHeight(Position.X, Position.Y);

            float heading = MathF.Atan2(_direction.Y, _direction.X);
            float turn = 0f;
            float steeringBlend = 1f - MathF.Exp(-6f * deltaTime);
            if (_currentSpeed > 0f)
            {
                bool avoiding = IsAvoiding;
                Vector2 target = avoiding ? _avoidDirection : flockDirection;
                if (target.LengthSquared() > 0.001f)
                {
                    float difference = MathHelper.WrapAngle(MathF.Atan2(target.Y, target.X) - heading);
                    float maxTurn = (avoiding ? TurnSpeed : FlockTurnSpeed) * deltaTime;
                    turn = MathHelper.Clamp(difference * (avoiding ? 1f : steeringBlend), -maxTurn, maxTurn);
                }
                else if (_aiState == BirdAIState.Flying)
                {
                    turn = MathF.Sin(_timer) * WanderTurnSpeed * deltaTime;
                }

                heading += turn;
                _direction = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f);
            }

            // Check clearance along the heading selected for this step.
            AvoidObstacles(terrainHeight);

            Vector2 velocity = FlightVelocity;
            AnimationSpeed = _aiState == BirdAIState.OnGround
                ? 1f
                : MathHelper.Clamp(5f + velocity.Length() / FlightSpeed * 4f, 3f, 10f);

            Vector3 position = Position + new Vector3(velocity * deltaTime, 0f);
            if (_currentSpeed > 0f)
                terrainHeight = walkableWorld.Terrain.RequestTerrainHeight(position.X, position.Y);

            float bank = _aiState == BirdAIState.Flying
                ? MathHelper.Lerp(Angle.Y, MathHelper.Clamp(turn / deltaTime * 0.08f, -0.20f, 0.20f), steeringBlend)
                : 0f;
            Angle = new Vector3(Angle.X, bank, MathHelper.WrapAngle(heading + MathHelper.PiOver2));

            switch (_aiState)
            {
                case BirdAIState.Flying:
                    float height = position.Z - terrainHeight;
                    if (height < MinFlightHeight)
                        _verticalSpeed = FlightVerticalSpeed;
                    else if (height > MaxFlightHeight)
                        _verticalSpeed = -FlightVerticalSpeed;
                    break;

                case BirdAIState.Descending:
                    float newDescendZ = position.Z - DiveAndAscentSpeed * deltaTime;
                    float groundLevel = terrainHeight + FootHeight();

                    if (_requiredZ > newDescendZ)
                    {
                        StartTakeoff(ClimbSpeed);
                    }
                    else if (newDescendZ <= groundLevel)
                    {
                        position.Z = groundLevel;
                        _currentSpeed = 0f;
                        _aiState = BirdAIState.OnGround;
                        PlayBirdSound();
                    }
                    else
                    {
                        position.Z = newDescendZ;
                    }
                    break;

                case BirdAIState.OnGround:
                    position.Z = terrainHeight + FootHeight();
                    break;

            }

            if (_aiState is BirdAIState.Flying or BirdAIState.Ascending)
            {
                _heightJitterTimer += deltaTime;
                if (_heightJitterTimer >= ReferenceTick)
                {
                    _heightJitterTimer %= ReferenceTick;
                    _heightJitterSpeed = _random.Next(-8, 8) * ReferenceTicksPerSecond;
                }

                float speedLimit = _aiState == BirdAIState.Ascending ? _verticalSpeed : ClimbSpeed;
                float flightSpeed = MathHelper.Clamp(_verticalSpeed + _heightJitterSpeed, -speedLimit, speedLimit);
                float clearanceSpeed = MathHelper.Clamp((_requiredZ - position.Z) * ClearanceResponse, -ClimbSpeed, ClimbSpeed);
                float targetSpeed = MathF.Max(flightSpeed, clearanceSpeed);
                float speedChange = VerticalAcceleration * deltaTime;
                _verticalVelocity += MathHelper.Clamp(targetSpeed - _verticalVelocity, -speedChange, speedChange);
                position.Z += _verticalVelocity * deltaTime;
            }

            Position = position;
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
            Vector2 velocity = FlightVelocity;
            float speed = velocity.Length();

            // Hardly moving (just taking off or landing): only what is right under the bird counts
            if (speed < 1f)
            {
                _requiredZ = MathF.Min(RequiredAltitude(Position.X, Position.Y), maxZ);
                return;
            }

            Vector2 heading = velocity / speed;
            float lookAheadSeconds = _aiState == BirdAIState.Descending ? LandingLookAheadSeconds : LookAheadSeconds;
            float lookAhead = MathF.Max(MinLookAhead, speed * lookAheadSeconds);
            float required = RequiredAltitudeAlong(heading, lookAhead);

            float turnLookAhead = MathF.Max(MinLookAhead, speed * TurnLookAheadSeconds);
            if (RequiredAltitudeAlong(heading, turnLookAhead) > maxZ)
                TurnAway(heading, turnLookAhead, maxZ);

            _requiredZ = MathF.Min(required, maxZ);
        }

        private float RequiredAltitude(float x, float y)
        {
            float top = Obstacles.TopAt(x, y);
            return top == BirdObstacleMap.None ? BirdObstacleMap.None : top + ObstacleClearance;
        }

        /// <summary>Highest altitude needed to clear the obstacles along the flight path.</summary>
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
                        _avoidDirection = direction;
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
