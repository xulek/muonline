using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Monsters;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Objects
{
    public abstract class WalkerObject : ModelObject
    {
        protected override bool RequiresPerFrameAnimation => true;

        // Fields: rotation and movement
        private Vector3 _targetAngle;
        private Direction _direction;
        private Vector2 _location;
        private Vector3 _cachedTargetPosition;
        private bool _targetPositionDirty = true;
        private bool _pendingTerrainSnap;
        protected Queue<Vector2> _currentPath;   // FIFO – cheaper removal than List.RemoveAt(0)
        private uint _moveRequestVersion;
        private CancellationTokenSource _pathRequestCancellation;

        public static int LastPathLength { get; private set; }
        public static double LastPathApplyMs { get; private set; }
        public static double LastPathQueueMs { get; private set; }
        public static double LastPathFacingMs { get; private set; }
        public static double LastPathBuildDirectionsMs { get; private set; }
        public static double LastPathSendScheduleMs { get; private set; }

    private readonly MainPlayerCameraController _mainPlayerCameraController = new();
    /// <summary>Showcase camera (inventory open in a safe zone), see <see cref="MainPlayerCameraController.Showcase"/>.</summary>
    public bool CameraShowcase
    {
        get => _mainPlayerCameraController.Showcase;
        set => _mainPlayerCameraController.Showcase = value;
    }

    public bool MouseScrollToZoom
    {
        get => _mainPlayerCameraController.MouseScrollToZoom;
        set => _mainPlayerCameraController.MouseScrollToZoom = value;
    }


        private const float RotationSpeed = 10f;
        private int _previousActionForSound = -1;
        private bool _serverControlledAnimation = false;
        private const byte DebuffPoison = 55;
        private const byte DebuffFreeze = 56;
        private static readonly Color DebuffTintNone = Color.White;
        private static readonly Color DebuffTintIce = new Color(150, 175, 220, 255);
        private static readonly Color DebuffTintPoison = new Color(145, 205, 145, 255);
        private Color _activeDebuffTint = DebuffTintNone;
        private Color _temporaryDebuffTint = DebuffTintNone;
        private double _temporaryDebuffTintUntilMs;
        private double _nextDebuffTintCheckMs;
        private const double DebuffTintCheckIntervalMs = 100d;

        // Properties
        public bool IsMainWalker => World is WalkableWorldControl walkableWorld && walkableWorld.Walker == this;

        public Vector2 Location
        {
            get => _location;
            set => OnLocationChanged(_location, value);
        }

        public float ExtraHeight { get; set; }

        public Direction Direction
        {
            get => _direction;
            set
            {
                if (_direction != value)
                {
                    _direction = value;
                    OnDirectionChanged();
                }
            }
        }

        public Vector3 TargetPosition
        {
            get
            {
                if (!_targetPositionDirty)
                    return _cachedTargetPosition;

                var x = Location.X * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                var y = Location.Y * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                var z = World is null ? 0 : World.Terrain.RequestTerrainHeight(x, y);
                _cachedTargetPosition = new Vector3(x, y, z);
                _targetPositionDirty = false;
                return _cachedTargetPosition;
            }
        }

        public Vector3 MoveTargetPosition { get; set; }

        /// <summary>
        /// Invalidates the cached world-space target. The cache depends on the current
        /// terrain and must not survive a world transition even when the tile coordinates
        /// stay unchanged.
        /// </summary>
        internal void InvalidateTerrainTarget()
        {
            _targetPositionDirty = true;
            _pendingTerrainSnap = true;
        }

        /// <summary>
        /// Snaps the walker to the current world's terrain immediately. Teleports and map
        /// changes must not use the interpolation path because the previous world's Z value
        /// can otherwise remain visible until the first movement update.
        /// </summary>
        internal void SnapToTerrainHeight(bool updateCamera = true)
        {
            if (World == null)
            {
                _targetPositionDirty = true;
                _pendingTerrainSnap = true;
                return;
            }

            // Preview/cinematic worlds place walkers directly in world space. Applying the
            // tile-based terrain position there moves actors (for example character selection)
            // away from their authored display position on the first update.
            if (World is not WalkableWorldControl walkableWorld)
            {
                _pendingTerrainSnap = false;
                return;
            }

            if (walkableWorld.Terrain?.Status != GameControlStatus.Ready)
            {
                _targetPositionDirty = true;
                _pendingTerrainSnap = true;
                return;
            }

            _targetPositionDirty = true;
            _pendingTerrainSnap = false;
            Vector3 terrainTarget = TargetPosition;
            float heightOffset = ExtraHeight + walkableWorld.ExtraHeight;

            MoveTargetPosition = terrainTarget;
            Position = new Vector3(terrainTarget.X, terrainTarget.Y, terrainTarget.Z + heightOffset);

            if (updateCamera && IsMainWalker)
                _mainPlayerCameraController.Apply(MoveTargetPosition);
        }

        public float MoveSpeed { get; set; } = Constants.MOVE_SPEED;
        public bool IsMoving => Vector3.DistanceSquared(MoveTargetPosition, TargetPosition) > 0.0001f;
        public new ushort NetworkId { get; set; }

        public ushort idanim = 0;

        // Public Methods
        protected readonly AnimationController _animationController;
        private double _lastAnimControllerTotalSeconds; // TotalGameTime at last AnimationController update

        public bool IsOneShotPlaying => _animationController?.IsOneShotPlaying ?? false;

        public bool IsAttackOrSkillAnimationPlaying()
        {
            if (!IsOneShotPlaying)
                return false;

            var type = _animationController.GetAnimationType((ushort)CurrentAction);
            return type == AnimationType.Attack || type == AnimationType.Skill;
        }

        internal void NotifyOneShotAnimationCompleted()
        {
            _animationController.NotifyAnimationCompleted();
        }

        protected WalkerObject()
        {
            _animationController = new AnimationController(this);
            UseSunLight = false; // Characters (players/NPCs/monsters) rely on dynamic lights only, no sun-tint
            RenderShadow = true; // All walkers (players, NPCs, monsters) should cast shadows
            BoundingBoxLocal = new BoundingBox(new Vector3(-40, -40, 0), new Vector3(40, 40, 180));
        }

        public new virtual async Task Load()
        {
            MoveTargetPosition = Vector3.Zero;
            _targetPositionDirty = true;
            _mainPlayerCameraController.Initialize();

            await base.Load();
        }

        public void Reset()
        {
            _currentPath = null;
            MoveTargetPosition = Vector3.Zero;
            _movementIntent = false;
            _targetPositionDirty = true;
            _nextDebuffTintCheckMs = 0d;

            // Reset animation state to clear any stuck death animations
            _animationController?.Reset();
        }

        /// <summary>
        /// Immediately stops any ongoing movement for this walker.
        /// Clears the current path and locks the movement target to the
        /// current world position so the object stays exactly where it is.
        /// The logical <see cref="Location"/> is also synchronized without
        /// triggering direction changes.
        /// </summary>
        public void StopMovement()
        {
            _currentPath?.Clear();
            _currentPath = null;

            // Freeze the object at its current rendered position
            MoveTargetPosition = Position;

            // Update the logical tile position without invoking OnLocationChanged
            _location = new Vector2(
                (int)(Position.X / Constants.TERRAIN_SCALE),
                (int)(Position.Y / Constants.TERRAIN_SCALE));
            _targetPositionDirty = true;

            // Align target angle with current rotation to prevent snapping
            _targetAngle = Angle;
        }

        public void OnDirectionChanged()
        {
            if (World is WalkableWorldControl)
                _targetAngle = _direction.ToAngle();
            else
                Angle = _direction.ToAngle();
        }

        public override void Update(GameTime gameTime)
        {
            if (_pendingTerrainSnap && World?.Terrain?.Status == GameControlStatus.Ready)
                SnapToTerrainHeight(updateCamera: false);

            if (Status != GameControlStatus.Ready || World == null || !Visible)
            {
                base.Update(gameTime);
                return;
            }

            UpdateDebuffTint(gameTime.TotalGameTime.TotalMilliseconds);
            base.Update(gameTime);

            float controllerDelta;
            double totalSeconds = gameTime.TotalGameTime.TotalSeconds;
            if (_lastAnimControllerTotalSeconds > 0)
                controllerDelta = (float)(totalSeconds - _lastAnimControllerTotalSeconds);
            else
                controllerDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _lastAnimControllerTotalSeconds = totalSeconds;

            _animationController?.Update(controllerDelta);

            // --- animation test ---
            // if (IsMainWalker)
            // {
            //     KeyboardState currentKeyboardState = Keyboard.GetState();
            //     if (currentKeyboardState.IsKeyDown(Keys.A) && _previousKeyboardState_WalkerTest.IsKeyUp(Keys.A))
            //     {
            //         idanim += 1;
            //         Debug.WriteLine($"[WALKER_ANIM_TEST] Key 'A' pressed. Changing animation to ID: {idanim}");
            //         PlayAction((ushort)idanim);
            //     }

            //     if (currentKeyboardState.IsKeyDown(Keys.B) && _previousKeyboardState_WalkerTest.IsKeyUp(Keys.B)) 
            //     {
            //         idanim -= 1;
            //         Debug.WriteLine($"[WALKER_ANIM_TEST] Key 'A' pressed. Changing animation to ID: {idanim}");
            //         PlayAction((ushort)idanim);
            //     }
            //     _previousKeyboardState_WalkerTest = currentKeyboardState;
            // }
            // -----------------------------------------


            if (IsMainWalker)
                HandleMouseInput();

            BeforeUpdatePosition(gameTime);
            UpdatePosition(gameTime);

            // Animation handled centrally to preserve cross-action blending

            if (_currentPath != null && _currentPath.Count > 0 && !IsMoving)
            {
                var next = _currentPath.Dequeue();
                MoveTowards(next, gameTime);
            }

            if (CurrentAction != _previousActionForSound)
            {
                if (this is MonsterObject)
                {
                    // Monster-specific sound methods are called in PlayAction
                }
                _previousActionForSound = CurrentAction;
            }
        }

        /// <summary>
        /// Plays the specified action using the centralized animation controller.
        /// </summary>
        public void PlayAction(ushort actionIndex, bool fromServer = false)
        {
            // If we re-trigger a one-shot while already in the same action, restart it.
            // This avoids "stuck" or jittery attacks/skills at low FPS / packet bursts.
            if (_priorActionIndex == actionIndex)
            {
                var kind = _animationController.GetAnimationType(actionIndex);
                if (kind is AnimationType.Attack or AnimationType.Skill or AnimationType.Emote or AnimationType.Appear)
                {
                    _animTime = 0.0;
                }
            }

            _serverControlledAnimation = fromServer;
            _animationController?.PlayAnimation(actionIndex, fromServer);
        }

        // Overload for backward compatibility
        public void PlayAction(ushort actionIndex)
        {
            PlayAction(actionIndex, false);
        }

        public virtual void MoveTo(Vector2 targetLocation, bool sendToServer = true, bool usePathfinding = true)
        {
            if (World == null || targetLocation == Location || !this.IsAlive())
                return;

            if (IsMainWalker && MuGame.Network?.GetCharacterState()?.IsTeleporting == true)
                return;

            _movementIntent = true;
            UpdateFacingFromVector(targetLocation - Location);

            if (this is PlayerObject player)
                player.OnPlayerMoved();

            Vector2 startPos = new((int)Location.X, (int)Location.Y);
            WorldControl currentWorld = World;
            uint requestVersion = unchecked(++_moveRequestVersion);

            CancelPendingPathRequest();

            if (!usePathfinding)
            {
                var path = Pathfinding.BuildDirectPath(startPos, targetLocation);
                ApplyPathOnMainThread(path, sendToServer, currentWorld, startPos, requestVersion);
                return;
            }

            var cancellation = new CancellationTokenSource();
            _pathRequestCancellation = cancellation;
            _ = ComputePathAsync(
                startPos,
                targetLocation,
                sendToServer,
                currentWorld,
                requestVersion,
                cancellation);
        }

        private async Task ComputePathAsync(
            Vector2 startPos,
            Vector2 targetLocation,
            bool sendToServer,
            WorldControl expectedWorld,
            uint requestVersion,
            CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                List<Vector2> path = await Pathfinding.FindPathAsync(
                    startPos,
                    targetLocation,
                    expectedWorld,
                    token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                    return;

                if ((path == null || path.Count == 0) && !sendToServer)
                    path = Pathfinding.BuildDirectPath(startPos, targetLocation);

                byte[] preparedDirections = sendToServer
                    ? BuildServerDirections(path, startPos)
                    : null;

                MuGame.ScheduleOnMainThread(
                    () =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ApplyPathOnMainThread(
                                path,
                                sendToServer,
                                expectedWorld,
                                startPos,
                                requestVersion,
                                preparedDirections);
                        }
                    },
                    MainThreadDispatcher.WorkPriority.High,
                    $"ComputePathAsync.ApplyPath[{path?.Count ?? 0}]");
            }
            catch (OperationCanceledException)
            {
                // A newer movement request superseded this path.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WalkerObject] Pathfinding failed: {ex}");
            }
            finally
            {
                if (ReferenceEquals(Interlocked.CompareExchange(
                        ref _pathRequestCancellation,
                        null,
                        cancellation),
                    cancellation))
                {
                    cancellation.Dispose();
                }
            }
        }

        private void CancelPendingPathRequest()
        {
            var previous = Interlocked.Exchange(ref _pathRequestCancellation, null);
            if (previous == null)
                return;

            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }

        protected void ApplyPathOnMainThread(
            List<Vector2> path,
            bool sendToServer,
            WorldControl expectedWorld,
            Vector2? pathStart = null,
            uint requestVersion = 0,
            byte[] preparedDirections = null)
        {
            if (MuGame.Instance.ActiveScene?.World != expectedWorld || Status == GameControlStatus.Disposed)
                return;

            if (requestVersion != 0 && requestVersion != _moveRequestVersion)
            {
                // Ignore stale async path results from older movement requests.
                return;
            }

            if (path == null || path.Count == 0)
            {
                _currentPath?.Clear();
                _movementIntent = false;
                return;
            }

            long applyStarted = Stopwatch.GetTimestamp();
            long queueStarted = applyStarted;
            _currentPath ??= new Queue<Vector2>(Math.Max(16, path.Count));
            _currentPath.Clear();
            _currentPath.EnsureCapacity(path.Count);
            for (int i = 0; i < path.Count; i++)
                _currentPath.Enqueue(path[i]);
            LastPathQueueMs = Stopwatch.GetElapsedTime(queueStarted).TotalMilliseconds;

            long facingStarted = Stopwatch.GetTimestamp();
            UpdateFacingFromVector(path[0] - Location);
            LastPathFacingMs = Stopwatch.GetElapsedTime(facingStarted).TotalMilliseconds;

            LastPathBuildDirectionsMs = 0d;
            LastPathSendScheduleMs = 0d;
            if (sendToServer && IsMainWalker)
            {
                var start = pathStart ?? new Vector2((int)Location.X, (int)Location.Y);
                long directionsStarted = Stopwatch.GetTimestamp();
                preparedDirections ??= BuildServerDirections(path, start);
                LastPathBuildDirectionsMs = Stopwatch.GetElapsedTime(directionsStarted).TotalMilliseconds;

                if (preparedDirections.Length > 0)
                {
                    // Packet construction and socket synchronization may run before the first
                    // await. Defer the complete send so path publication remains a small mutation.
                    long sendScheduleStarted = Stopwatch.GetTimestamp();
                    _ = Task.Run(() => SendWalkDirectionsToServerSafelyAsync(preparedDirections, start));
                    LastPathSendScheduleMs = Stopwatch.GetElapsedTime(sendScheduleStarted).TotalMilliseconds;
                }
            }

            LastPathLength = path.Count;
            LastPathApplyMs = Stopwatch.GetElapsedTime(applyStarted).TotalMilliseconds;
        }

        public void FaceTowards(Vector2 targetLocation, bool immediate = false)
        {
            UpdateFacingFromVector(targetLocation - Location, immediate);
        }

        private static byte[] BuildServerDirections(List<Vector2> path, Vector2 startPos)
        {
            if (path == null || path.Count == 0)
                return Array.Empty<byte>();

            static byte GetClientDirectionCode(Vector2 from, Vector2 to)
            {
                int dx = (int)(to.X - from.X);
                int dy = (int)(to.Y - from.Y);
                return (dx, dy) switch
                {
                    (-1, 0) => 0,
                    (-1, 1) => 1,
                    (0, 1) => 2,
                    (1, 1) => 3,
                    (1, 0) => 4,
                    (1, -1) => 5,
                    (0, -1) => 6,
                    (-1, -1) => 7,
                    _ => 0xFF
                };
            }

            int count = Math.Min(path.Count, 15);
            var result = new byte[count];
            var directionMap = MuGame.Network?.GetDirectionMap();
            Vector2 current = startPos;
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 step = path[i];
                byte clientDirection = GetClientDirectionCode(current, step);
                if (clientDirection > 7)
                    break;

                result[written++] = directionMap != null &&
                    directionMap.TryGetValue(clientDirection, out byte serverDirection)
                        ? serverDirection
                        : clientDirection;
                current = step;
            }

            if (written == result.Length)
                return result;
            if (written == 0)
                return Array.Empty<byte>();

            Array.Resize(ref result, written);
            return result;
        }

        private async Task SendWalkDirectionsToServerSafelyAsync(byte[] directions, Vector2 startPos)
        {
            try
            {
                var network = MuGame.Network;
                if (network == null || directions == null || directions.Length == 0)
                    return;

                await network.SendWalkRequestAsync(
                    (byte)startPos.X,
                    (byte)startPos.Y,
                    directions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WalkerObject] Failed to send walk path: {ex}");
            }
        }

        // Private Methods
        protected virtual void OnLocationChanged(Vector2 oldLocation, Vector2 newLocation)
        {
            if (oldLocation == newLocation) return;
            _location = new Vector2((int)newLocation.X, (int)newLocation.Y);
            _targetPositionDirty = true;

            if (oldLocation == Vector2.Zero)
                return;

            // Use helper that already maps delta → Direction enum
            Direction = DirectionExtensions.GetDirectionFromMovementDelta(
                            (int)(newLocation.X - oldLocation.X),
                            (int)(newLocation.Y - oldLocation.Y));
        }

        public void UpdatePosition(GameTime gameTime)
        {
            if (World is not WalkableWorldControl walkableWorld)
                return;

            UpdateMoveTargetPosition(gameTime);

            float worldExtraHeight = walkableWorld.ExtraHeight;
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_targetAngle != Angle)
            {
                float localDeltaTime = deltaTime;
                Vector3 angleDifference = _targetAngle - Angle;
                angleDifference.Z = MathHelper.WrapAngle(angleDifference.Z);

                float maxStep = RotationSpeed * localDeltaTime;
                float stepZ = MathHelper.Clamp(angleDifference.Z, -maxStep, maxStep);

                Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + stepZ);

                if (Math.Abs(angleDifference.Z) <= maxStep)
                    Angle = new Vector3(Angle.X, Angle.Y, _targetAngle.Z);
            }

            float targetHeight =
                MoveTargetPosition.Z +
                worldExtraHeight +
                ExtraHeight;
            float interpolationFactor = 15f * deltaTime;
            float newZ = MathHelper.Lerp(Position.Z, targetHeight, interpolationFactor);

            Position = new Vector3(MoveTargetPosition.X, MoveTargetPosition.Y, newZ);
        }


        private void UpdateMoveTargetPosition(GameTime time)
        {
            Vector3 targetPosition = TargetPosition;

            if (MoveTargetPosition == Vector3.Zero)
            {
                MoveTargetPosition = targetPosition;
                UpdateCameraPosition(MoveTargetPosition);
                return;
            }

            Vector3 remaining = targetPosition - MoveTargetPosition;
            float remainingDistanceSq = remaining.LengthSquared();
            if (remainingDistanceSq <= 0.0001f)
            {
                MoveTargetPosition = targetPosition;
                UpdateCameraPosition(MoveTargetPosition);
                return;
            }

            UpdateFacingFromVector(new Vector2(remaining.X, remaining.Y));

            float deltaTime = (float)time.ElapsedGameTime.TotalSeconds;
            float maxStep = MoveSpeed * deltaTime;

            if (maxStep * maxStep >= remainingDistanceSq)
            {
                UpdateCameraPosition(targetPosition);
                if (_currentPath == null || _currentPath.Count == 0)
                    _movementIntent = false;
            }
            else
            {
                float inverseDistance = 1f / MathF.Sqrt(remainingDistanceSq);
                Vector3 moveVector = remaining * (inverseDistance * maxStep);
                UpdateCameraPosition(MoveTargetPosition + moveVector);
            }
        }

        private void UpdateCameraPosition(Vector3 position)
        {
            MoveTargetPosition = position;
            if (!IsMainWalker)
                return;

            _mainPlayerCameraController.Apply(position);
        }

        private void UpdateDebuffTint(double nowMs)
        {
            if (nowMs < _nextDebuffTintCheckMs)
                return;

            // Stagger network-state lookups so a crowd of walkers does not query buffs
            // on the same frame.
            _nextDebuffTintCheckMs = nowMs + DebuffTintCheckIntervalMs + (NetworkId & 0x0F);

            Color tint = ResolveDebuffTint(nowMs);
            if (tint.PackedValue == _activeDebuffTint.PackedValue)
                return;

            _activeDebuffTint = tint;
            ApplyTintRecursive(this, tint);
            InvalidateBuffers(BufferFlagLighting);
        }

        private Color ResolveDebuffTint(double nowMs)
        {
            if (_temporaryDebuffTint.PackedValue != DebuffTintNone.PackedValue)
            {
                if (nowMs <= _temporaryDebuffTintUntilMs)
                    return _temporaryDebuffTint;

                _temporaryDebuffTint = DebuffTintNone;
                _temporaryDebuffTintUntilMs = 0d;
            }

            var charState = MuGame.Network?.GetCharacterState();
            if (charState == null)
                return DebuffTintNone;

            ushort objectId = IsMainWalker ? charState.Id : NetworkId;
            if (objectId == 0)
                return DebuffTintNone;

            bool hasFreeze = charState.HasActiveBuff(DebuffFreeze, objectId);
            bool hasPoison = charState.HasActiveBuff(DebuffPoison, objectId);

            if (hasFreeze)
                return DebuffTintIce;

            if (hasPoison)
                return DebuffTintPoison;

            return DebuffTintNone;
        }

        public void ApplyTemporaryDebuffTint(byte effectId, float durationSeconds)
        {
            Color tint = effectId switch
            {
                DebuffFreeze => DebuffTintIce,
                DebuffPoison => DebuffTintPoison,
                _ => DebuffTintNone
            };

            if (tint.PackedValue == DebuffTintNone.PackedValue)
                return;

            double nowMs = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            double untilMs = nowMs + Math.Max(0.1f, durationSeconds) * 1000d;

            _temporaryDebuffTint = tint;
            if (untilMs > _temporaryDebuffTintUntilMs)
                _temporaryDebuffTintUntilMs = untilMs;
            _nextDebuffTintCheckMs = 0d;
        }

        private static void ApplyTintRecursive(ModelObject model, Color tint)
        {
            model.Color = tint;

            var children = model.Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is ModelObject childModel)
                    ApplyTintRecursive(childModel, tint);
            }
        }

        private void MoveTowards(Vector2 target, GameTime gameTime)
        {
            Location = target;

            if (this is MonsterObject monster)
            {
                // Check if a one-shot animation is currently playing
                if (_animationController?.IsOneShotPlaying == true)
                {
                    // Don't override the animation if a one-shot is playing
                    // Debug.WriteLine($"[WalkerObject] Monster one-shot animation is playing, not overriding with walk animation");
                    return;
                }

                const int MONSTER_ACTION_WALK = (int)MonsterActionType.Walk;
                int moveAction = MONSTER_ACTION_WALK;
                if (Model?.Actions?.Length > (int)MonsterActionType.Run &&
                    Model.Actions[(int)MonsterActionType.Run]?.NumAnimationKeys > 0)
                {
                    // Optionally choose run instead of walk here
                }

                if (CurrentAction != moveAction)
                {
                    // Debug.WriteLine($"[WalkerObject] Monster starting walk animation: {moveAction}");
                    PlayAction((byte)moveAction);
                }
            }
        }

        private void HandleMouseInput()
        {
            _mainPlayerCameraController.Update(MuGame.Instance.GameTime);

            // Showcase camera: a hero standing still turns to face it.
            if (_mainPlayerCameraController.Showcase)
            {
                var standing = new Vector2(MoveTargetPosition.X - Position.X, MoveTargetPosition.Y - Position.Y).LengthSquared() < 25f;
                if (standing)
                {
                    Vector2 toCamera = _mainPlayerCameraController.TowardCamera;
                    SetFacingAngleZ(MathF.Atan2(toCamera.X, -toCamera.Y));
                }
            }
        }

        /// <summary>
        /// Hook for derived walkers to run logic after input but before position/rotation updates.
        /// </summary>
        protected virtual void BeforeUpdatePosition(GameTime gameTime)
        {
        }

        /// <summary>
        /// Sets a continuous facing angle (Z, in radians) without changing <see cref="Direction"/>.
        /// Useful for MU-like stand rotation behaviors which are not limited to 8 directions.
        /// </summary>
        protected void SetFacingAngleZ(float angleZ, bool immediate = false)
        {
            angleZ = MathHelper.WrapAngle(angleZ);
            _targetAngle = new Vector3(_targetAngle.X, _targetAngle.Y, angleZ);

            if (immediate)
            {
                Angle = new Vector3(Angle.X, Angle.Y, angleZ);
            }
        }

        private void UpdateFacingFromVector(Vector2 direction, bool immediate = false)
        {
            if (direction.LengthSquared() <= 0.0001f) return;

            float angleDeg = AngleUtils.CreateAngleDegrees(0f, 0f, direction.X, direction.Y);
            SetFacingAngleZ(MathHelper.ToRadians(angleDeg), immediate);
        }

        protected bool _movementIntent;
        public bool MovementIntent => _movementIntent;

        public override void Dispose()
        {
            CancelPendingPathRequest();
            base.Dispose();
        }

        // protected override void Dispose(bool disposing)
        // {
        //     if (disposing)
        //     {
        //         _animationController?.Dispose();
        //     }
        //     base.Dispose(disposing);
        // }

        protected override void OnWorldChanged(WorldControl newWorld, WorldControl prevWorld)
        {
            if (!ReferenceEquals(newWorld, prevWorld))
            {
                CancelPendingPathRequest();
                _targetPositionDirty = true;
                _pendingTerrainSnap = newWorld is WalkableWorldControl;
                MoveTargetPosition = Vector3.Zero;
            }

            base.OnWorldChanged(newWorld, prevWorld);

            // During initial world construction the terrain may not be ready yet. The scene
            // and map controllers call SnapToTerrainHeight again after initialization.
            if (newWorld is WalkableWorldControl &&
                newWorld.Status == GameControlStatus.Ready &&
                newWorld.Terrain?.Status == GameControlStatus.Ready)
            {
                SnapToTerrainHeight();
            }
            else
            {
                UpdateCameraPosition(Position);
            }
        }
    }
}
