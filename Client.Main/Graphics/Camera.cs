using Microsoft.Xna.Framework;
using System;
using System.Diagnostics;
using System.Threading;

namespace Client.Main.Graphics
{
    public readonly struct CameraState
    {
        public CameraState(
            float aspectRatio,
            float fieldOfView,
            float viewNear,
            float viewFar,
            Vector3 position,
            Vector3 target)
        {
            AspectRatio = aspectRatio;
            FieldOfView = fieldOfView;
            ViewNear = viewNear;
            ViewFar = viewFar;
            Position = position;
            Target = target;
        }

        public float AspectRatio { get; }
        public float FieldOfView { get; }
        public float ViewNear { get; }
        public float ViewFar { get; }
        public Vector3 Position { get; }
        public Vector3 Target { get; }

        public CameraState With(
            float? aspectRatio = null,
            float? fieldOfView = null,
            float? viewNear = null,
            float? viewFar = null,
            Vector3? position = null,
            Vector3? target = null)
        {
            return new CameraState(
                aspectRatio ?? AspectRatio,
                fieldOfView ?? FieldOfView,
                viewNear ?? ViewNear,
                viewFar ?? ViewFar,
                position ?? Position,
                target ?? Target);
        }
    }

    public class Camera
    {
        // Static Properties
        public static Camera Instance { get; } = new Camera();

        // Fields
        private float _aspectRatio = 1.4f;
        private float _fov = 35f;
        private float _viewNear = 10f;  // Increased from 1f to improve depth precision
        private float _viewFar = 1800f;
        private Vector3 _position = Vector3.Zero;
        private Vector3 _target = Vector3.Zero;
        private readonly BoundingFrustum _frustum = new(Matrix.Identity);
        private Matrix _cullingView = Matrix.Identity;

        // Throttle CameraMoved events to avoid flooding subscribers during rapid camera updates
        private long _lastCameraMovedTimeMs = 0;
        private const int CAMERA_MOVED_COOLDOWN_MS = 300; // milliseconds

        // Screen shake state
        private float _shakeIntensity;
        private float _shakeTimeLeft;
        private float _shakeFrequency = 25f;
        private float _shakeElapsed;
        private readonly Random _shakeRng = new();

        // Public Properties
        public float AspectRatio
        {
            get => _aspectRatio;
            set { if (_aspectRatio != value) { _aspectRatio = value; UpdateProjection(); } }
        }

        public float FOV
        {
            get => _fov;
            set { if (_fov != value) { _fov = value; UpdateProjection(); } }
        }

        public float ViewNear
        {
            get => _viewNear;
            set { if (_viewNear != value) { _viewNear = value; UpdateProjection(); } }
        }

        public float ViewFar
        {
            get => _viewFar;
            set { if (_viewFar != value) { _viewFar = value; UpdateProjection(); } }
        }

        private float? _renderDistanceLimit;

        /// <summary>
        /// Optional hard limit of the rendered distance from the camera (distance fog end). It caps the far
        /// plane and the terrain / object culling ranges; null renders as far as <see cref="ViewFar"/> allows.
        /// </summary>
        public float? RenderDistanceLimit
        {
            get => _renderDistanceLimit;
            set { if (_renderDistanceLimit != value) { _renderDistanceLimit = value; UpdateProjection(); } }
        }

        /// <summary>Horizontal distance from the camera up to which terrain blocks are considered.</summary>
        public float TerrainRenderDistance => Limit(_viewFar * 1.7f);

        /// <summary>Horizontal distance from the camera up to which world objects are considered.</summary>
        public float ObjectCullDistance => _renderDistanceLimit.HasValue
            ? MathF.Min(_viewFar + Constants.MAX_CAMERA_DISTANCE, _renderDistanceLimit.Value) + 250f
            : _viewFar + Constants.MAX_CAMERA_DISTANCE + 250f;

        private float Limit(float distance) =>
            _renderDistanceLimit.HasValue ? MathF.Min(distance, _renderDistanceLimit.Value) : distance;

        public Vector3 Position
        {
            get => _position;
            set { if (_position != value) { _position = value; UpdateView(); } }
        }

        public Vector3 Target
        {
            get => _target;
            set { if (_target != value) { _target = value; UpdateView(); } }
        }

        public Matrix View { get; private set; }
        /// <summary>World-space camera basis shared by billboard and camera-facing effects.</summary>
        public Vector3 Right { get; private set; } = Vector3.UnitX;
        public Vector3 Up { get; private set; } = Vector3.UnitZ;
        public Matrix Projection { get; private set; }
        /// <summary>Cached render View * Projection matrix. Updated only when either source matrix changes.</summary>
        public Matrix ViewProjection { get; private set; }
        public BoundingFrustum Frustum => _frustum;
        /// <summary>Changes whenever the rendered view/projection changes, including screen shake.</summary>
        public ulong StateVersion { get; private set; }
        /// <summary>
        /// Changes only when the stable camera used for visibility tests changes.
        /// Screen shake intentionally does not invalidate world culling.
        /// </summary>
        public ulong CullingStateVersion { get; private set; }

        // Events
        public event EventHandler CameraMoved;

        // Constructors
        private Camera()
        {
            UpdateProjection();
            UpdateView();
        }

        // Public Methods
        public void ForceUpdate()
        {
            UpdateView(updateCulling: true);
            UpdateProjection();
        }

        public CameraState CaptureState()
        {
            return new CameraState(
                _aspectRatio,
                _fov,
                _viewNear,
                _viewFar,
                _position,
                _target);
        }

        public void ApplyState(in CameraState state)
        {
            _aspectRatio = state.AspectRatio;
            _fov = state.FieldOfView;
            _viewNear = state.ViewNear;
            _viewFar = state.ViewFar;
            _position = state.Position;
            _target = state.Target;

            UpdateView(updateCulling: true);
            UpdateProjection();
        }

        /// <summary>
        /// Starts a screen shake. Multiple calls pick the stronger intensity.
        /// </summary>
        public void Shake(float intensity, float duration, float frequency = 25f)
        {
            if (intensity > _shakeIntensity || _shakeTimeLeft <= 0f)
            {
                _shakeIntensity = intensity;
                _shakeFrequency = frequency;
            }
            if (duration > _shakeTimeLeft)
                _shakeTimeLeft = duration;
        }

        /// <summary>
        /// Call once per frame from the main update loop to advance shake timer.
        /// </summary>
        public void UpdateShake(float dt)
        {
            if (_shakeTimeLeft <= 0f)
                return;

            _shakeElapsed += dt;
            _shakeTimeLeft -= dt;

            if (_shakeTimeLeft <= 0f)
            {
                _shakeIntensity = 0f;
                _shakeTimeLeft = 0f;
            }

            UpdateView(updateCulling: false);
        }

        // Private Methods
        private void UpdateProjection()
        {
            Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(_fov),
                _aspectRatio,
                _viewNear,
                Limit(_viewFar + Constants.MAX_CAMERA_DISTANCE)
            );
            ViewProjection = View * Projection;

            UpdateFrustum();
            unchecked
            {
                StateVersion++;
                CullingStateVersion++;
            }
            RaiseCameraMovedThrottled();
        }

        private void UpdateView(bool updateCulling = true)
        {
            Vector3 basePosition = _position;
            Vector3 baseTarget = _target;
            Matrix stableView = CreateViewMatrix(basePosition, baseTarget, out Vector3 cameraRight, out Vector3 cameraUp);

            Vector3 renderPosition = basePosition;
            Vector3 renderTarget = baseTarget;
            if (_shakeTimeLeft > 0f && _shakeIntensity > 0f)
            {
                float decay = MathHelper.Clamp(_shakeTimeLeft / 0.5f, 0f, 1f);
                float strength = _shakeIntensity * decay;
                float t = _shakeElapsed * _shakeFrequency;
                float ox = MathF.Sin(t) * strength;
                float oy = MathF.Sin(t * 1.3f + 1.7f) * strength;
                float oz = MathF.Sin(t * 0.9f + 3.1f) * strength * 0.5f;
                var offset = new Vector3(ox, oy, oz);
                renderPosition += offset;
                renderTarget += offset;
            }

            View = renderPosition == basePosition
                ? stableView
                : CreateViewMatrix(renderPosition, renderTarget, out cameraRight, out cameraUp);
            Right = cameraRight;
            Up = cameraUp;
            ViewProjection = View * Projection;

            if (updateCulling)
            {
                _cullingView = stableView;
                UpdateFrustum();
                unchecked { CullingStateVersion++; }
                RaiseCameraMovedThrottled();
            }

            unchecked { StateVersion++; }
        }

        private static Matrix CreateViewMatrix(
            Vector3 position,
            Vector3 target,
            out Vector3 right,
            out Vector3 up)
        {
            Vector3 direction = target - position;
            if (direction.LengthSquared() < 1e-8f)
                direction = Vector3.UnitY;
            else
                direction.Normalize();

            right = Vector3.Cross(direction, Vector3.UnitZ);
            if (right.LengthSquared() < 1e-8f)
                right = Vector3.UnitX;
            else
                right.Normalize();

            up = Vector3.Cross(right, direction);
            return Matrix.CreateLookAt(position, target, up);
        }

        /// <summary>
        /// Extra half-angle (degrees) of the culling frustum on every side. Visibility is rebuilt only after the
        /// camera has turned / moved by a threshold (WorldControl, TerrainVisibilityManager), so without an angular
        /// margin objects at the screen edges appear a little late while turning, more so the farther they are
        /// (a constant world-space guard band cannot cover that at large distances).
        /// </summary>
        private const float CullingAngleMarginDegrees = 3f;

        private void UpdateFrustum()
        {
            float halfFovY = MathHelper.ToRadians(_fov) * 0.5f;
            float halfFovX = MathF.Atan(MathF.Tan(halfFovY) * _aspectRatio);
            float margin = MathHelper.ToRadians(CullingAngleMarginDegrees);
            float cullHalfFovY = MathF.Min(halfFovY + margin, MathHelper.ToRadians(85f));
            float cullHalfFovX = MathF.Min(halfFovX + margin, MathHelper.ToRadians(85f));

            Matrix cullingProjection = Matrix.CreatePerspectiveFieldOfView(
                cullHalfFovY * 2f,
                MathF.Tan(cullHalfFovX) / MathF.Tan(cullHalfFovY),
                _viewNear,
                Limit(_viewFar + Constants.MAX_CAMERA_DISTANCE));
            _frustum.Matrix = _cullingView * cullingProjection;
        }

        /// <summary>
        /// Raises the CameraMoved event but limits firing to once every CAMERA_MOVED_COOLDOWN_MS milliseconds.
        /// </summary>
        private void RaiseCameraMovedThrottled()
        {
            var handler = CameraMoved;
            if (handler == null)
                return;

            long nowMs = Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
            long previous = Volatile.Read(ref _lastCameraMovedTimeMs);
            if (nowMs - previous < CAMERA_MOVED_COOLDOWN_MS)
                return;

            if (Interlocked.CompareExchange(ref _lastCameraMovedTimeMs, nowMs, previous) == previous)
                handler(this, EventArgs.Empty);
        }
    }
}
