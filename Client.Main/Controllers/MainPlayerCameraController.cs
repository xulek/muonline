using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controllers;

internal sealed class MainPlayerCameraController
{
    private const int MiddleMouseDragThresholdPixels = 3;

    private float _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private float _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private int _previousScrollValue;
    private bool _mouseScrollToZoom = true;
    private float _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
    private float _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
    private int _middleMouseTravel;

    // Showcase view while the inventory is open in a safe zone: the camera comes down to half the hero's height, level
    // with them, far enough to frame them from head to toe, with the hero moved to the left of the screen so the window
    // on the right does not cover them. Blends in and out.
    private const float ShowcaseDistance = 560f;
    private static readonly float ShowcasePitch = MathHelper.ToRadians(176f); // almost level with the hero
    private const float ShowcaseHeight = 95f;   // half of the character
    private const float ShowcaseSideOffset = 125f;
    private const float ShowcaseBlendSpeed = 3.5f;
    private float _showcase;

    /// <summary>When true the camera blends into the showcase view; the player's own zoom and angle come back after.</summary>
    public bool Showcase { get; set; }

    /// <summary>How far the showcase view is blended in (0..1).</summary>
    public float ShowcaseBlend => _showcase;

    /// <summary>Horizontal direction from the hero towards the camera (unit length).</summary>
    public Vector2 TowardCamera
    {
        get
        {
            float pitch = MathHelper.Lerp(_cameraPitch, ShowcasePitch, _showcase);
            var v = new Vector2(MathF.Cos(pitch) * MathF.Sin(_cameraYaw), MathF.Cos(pitch) * MathF.Cos(_cameraYaw));
            return v.LengthSquared() > 1e-6f ? Vector2.Normalize(v) : Vector2.UnitY;
        }
    }

    public bool MouseScrollToZoom
    {
        get => _mouseScrollToZoom;
        set => _mouseScrollToZoom = value;
    }

    public void Initialize()
    {
        _previousScrollValue = MuGame.Instance.Mouse.ScrollWheelValue;
        ResetToDefault(immediateDistance: true);
        _middleMouseTravel = 0;
    }

    public void Update(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _currentCameraDistance = MathHelper.Lerp(
            _currentCameraDistance,
            _targetCameraDistance,
            Constants.ZOOM_SPEED * deltaTime);

        float blendTarget = Showcase ? 1f : 0f;
        _showcase = _showcase < blendTarget
            ? Math.Min(blendTarget, _showcase + ShowcaseBlendSpeed * deltaTime)
            : Math.Max(blendTarget, _showcase - ShowcaseBlendSpeed * deltaTime);

        var mouse = MuGame.Instance.Mouse;
        var previousMouse = MuGame.Instance.PrevMouseState;

        if (MouseScrollToZoom && _showcase <= 0f)
        {
            int scrollDelta = mouse.ScrollWheelValue - _previousScrollValue;
            if (scrollDelta != 0)
            {
                float zoomChange = -scrollDelta * Constants.ZOOM_SPEED;
                _targetCameraDistance = MathHelper.Clamp(
                    _targetCameraDistance + zoomChange,
                    Constants.MIN_CAMERA_DISTANCE,
                    Constants.MAX_CAMERA_DISTANCE);
            }
        }

        _previousScrollValue = mouse.ScrollWheelValue;

        bool middlePressed = mouse.MiddleButton == ButtonState.Pressed;
        bool middleWasPressed = previousMouse.MiddleButton == ButtonState.Pressed;

        if (middlePressed && !middleWasPressed)
        {
            _middleMouseTravel = 0;
        }

        if (middlePressed)
        {
            int deltaX = mouse.X - previousMouse.X;
            int deltaY = mouse.Y - previousMouse.Y;

            if (deltaX != 0 || deltaY != 0)
            {
                _middleMouseTravel += Math.Abs(deltaX) + Math.Abs(deltaY);
                _cameraYaw = MathHelper.WrapAngle(
                    _cameraYaw - deltaX * Constants.ROTATION_SENSITIVITY);
                _cameraPitch = MathHelper.Clamp(
                    _cameraPitch - deltaY * Constants.ROTATION_SENSITIVITY,
                    Constants.MIN_PITCH,
                    Constants.MAX_PITCH);
            }
        }

        if (!middlePressed && middleWasPressed)
        {
            // A click resets the complete view. A drag keeps the new yaw, pitch and zoom.
            if (_middleMouseTravel < MiddleMouseDragThresholdPixels)
                ResetToDefault(immediateDistance: true);

            _middleMouseTravel = 0;
        }
    }

    public void Apply(Vector3 target)
    {
        // Smoothstep so the move starts and ends gently.
        float blend = _showcase * _showcase * (3f - 2f * _showcase);
        float distance = MathHelper.Lerp(_currentCameraDistance, ShowcaseDistance, blend);
        float pitch = MathHelper.Lerp(_cameraPitch, ShowcasePitch, blend);

        float x = distance * MathF.Cos(pitch) * MathF.Sin(_cameraYaw);
        float y = distance * MathF.Cos(pitch) * MathF.Cos(_cameraYaw);
        float z = distance * MathF.Sin(pitch);

        if (blend > 0f)
        {
            // Screen-right in the ground plane: perpendicular to the horizontal view direction.
            var toCamera = new Vector2(x, y);
            var right = toCamera.LengthSquared() > 1f ? Vector2.Normalize(new Vector2(-toCamera.Y, toCamera.X)) : Vector2.Zero;
            target += new Vector3(right * (ShowcaseSideOffset * blend), ShowcaseHeight * blend);
        }

        var cameraPosition = target + new Vector3(x, y, z);

        Camera.Instance.FOV = 35 * Constants.FOV_SCALE;
        Camera.Instance.Position = cameraPosition;
        Camera.Instance.Target = target;
    }

    private void ResetToDefault(bool immediateDistance)
    {
        _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
        _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
        _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;

        if (immediateDistance)
            _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    }
}
