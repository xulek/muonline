using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Client.Main.Scenes
{
    public abstract class BaseScene : GameControl
    {
        public new WorldControl World { get; protected set; }

        /// <summary>
        /// Indicates that the scene owns a complete loading presentation and can be made active
        /// before its full initialization finishes. Scenes which return false stay off-screen
        /// until they are ready, so the previous scene remains visible during the transition.
        /// </summary>
        public virtual bool CanRenderWhileInitializing => false;

        /// <summary>
        /// Prepares the minimum resources required for the first frame shown during initialization.
        /// The default implementation has no special presentation requirements.
        /// </summary>
        public virtual Task PrepareForFirstPresentedFrameAsync() => Task.CompletedTask;

        /// <summary>
        /// Called immediately after this scene becomes the globally active scene and before the
        /// previous scene is disposed. Menu worlds use it to atomically publish their deferred
        /// camera, preventing camera settings from leaking into the scene still being displayed.
        /// </summary>
        public virtual void OnActivated()
        {
            World?.ActivateDeferredCamera();
        }

        public CursorControl Cursor { get; }
        public GameControl MouseControl { get; set; }
        public GameControl MouseHoverControl { get; set; }
        public GameControl FocusControl { get; set; }
        public DebugPanel DebugPanel { get; }

        public WorldObject MouseHoverObject { get; set; }
        public bool IsMouseHandledByUI { get; set; }
        public bool IsMouseInputConsumedThisFrame { get; private set; }
        public bool IsKeyboardEnterConsumedThisFrame { get; private set; }
        public bool IsKeyboardEscapeConsumedThisFrame { get; private set; }

        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<BaseScene>();
        private bool _progressiveInitializationReserved;
        private bool _leftMouseCapturedByUi;
        private bool _rightMouseCapturedByUi;

        public BaseScene()
        {
            AutoViewSize = false;
            ViewSize = new(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);

            Controls.Add(DebugPanel = new DebugPanel { Visible = Constants.SHOW_DEBUG_PANEL });
            Controls.Add(Cursor = new CursorControl());
        }

        public void ChangeWorld<T>() where T : WorldControl, new()
        {
            Task.Run(() => ChangeWorldAsync<T>()).ConfigureAwait(false);
        }

        public async Task ChangeWorldAsync<T>(Action<string, float> progressCallback = null) where T : WorldControl, new()
        {
            progressCallback?.Invoke("Disposing previous world...", 0.0f * (progressCallback == null ? 1.0f : 1.0f)); // Progress for this step
            World?.Dispose();

            progressCallback?.Invoke($"Creating World {typeof(T).Name}...", 0.1f * (progressCallback == null ? 1.0f : 1.0f));
            var newWorld = new T();
            Controls.Add(newWorld);

            progressCallback?.Invoke($"Initializing World {typeof(T).Name}...", 0.2f * (progressCallback == null ? 1.0f : 1.0f));
            // If WorldControl needs fine-grained progress, it would need InitializeWithProgressReporting too.
            // For now, its Initialize() is treated as one block.
            await newWorld.Initialize();

            World = newWorld;
            progressCallback?.Invoke($"World {typeof(T).Name} Ready.", 1.0f * (progressCallback == null ? 1.0f : 1.0f));

            DebugPanel.Dispose();
        }


        internal void ReserveProgressiveInitialization()
        {
            // If already fully initialized (e.g., pre-initialized by a loading scene
            // with its own progress UI), there is nothing to reserve — GameControl.Update
            // will not auto-init a Ready scene, and the subsequent
            // InitializeWithProgressReporting call will see it is already done.
            if (Status == GameControlStatus.Ready)
                return;

            if (Status != GameControlStatus.NonInitialized)
                throw new InvalidOperationException($"Cannot reserve initialization for {GetType().Name} in state {Status}.");

            _progressiveInitializationReserved = true;
            Status = GameControlStatus.Initializing;
        }

        public virtual async Task InitializeWithProgressReporting(Action<string, float> progressCallback)
        {
            bool usesReservedInitialization =
                _progressiveInitializationReserved && Status == GameControlStatus.Initializing;
            if (!usesReservedInitialization && Status != GameControlStatus.NonInitialized)
                return;

            _progressiveInitializationReserved = false;

            void Report(string message, float progress) => progressCallback?.Invoke(message, progress);

            try
            {
                if (!usesReservedInitialization)
                    Status = GameControlStatus.Initializing;

                Report($"Initializing {GetType().Name}...", 0.05f);

                var controlsToInitialize = Controls
                    .Where(control => control.Status == GameControlStatus.NonInitialized)
                    .ToArray();
                if (controlsToInitialize.Length > 0)
                {
                    Report($"Initializing UI Controls for {GetType().Name}...", 0.10f);
                    await InitializeControlsInBatchesAsync(controlsToInitialize, Report);
                    Report($"UI Controls Initialized for {GetType().Name}.", 0.15f);
                }
                else
                {
                    Report($"No new UI Controls to initialize for {GetType().Name}.", 0.15f);
                }

                await MuGame.YieldToNextFrameAsync(
                    $"SceneInit.{GetType().Name}.Content",
                    MainThreadDispatcher.WorkPriority.Critical);
                await LoadSceneContentWithProgress(progressCallback);

                // A completed child Task can resume parent async methods inline inside the
                // dispatcher action which finished the child phase. Break that continuation
                // chain before scheduling the final scene work back on the game thread.
                await Task.Yield();
                await MuGame.YieldToNextFrameAsync(
                    $"SceneInit.{GetType().Name}.Finalize",
                    MainThreadDispatcher.WorkPriority.Critical);

                Report($"Finalizing {GetType().Name}...", 0.95f);
                AfterLoad();

                Status = GameControlStatus.Ready;
                Report($"{GetType().Name} Ready.", 1.0f);

                // Complete this workflow outside the current dispatcher action so the parent
                // scene-change state machine cannot inherit the complete finalization cost.
                await Task.Yield();
            }
            catch (Exception e)
            {
                _logger?.LogDebug($"Error during InitializeWithProgressReporting for {GetType().Name}: {e.Message}{Environment.NewLine}{e.StackTrace}");
                Status = GameControlStatus.Error;
                Report($"Error initializing {GetType().Name}: {e.Message}", 1.0f);
                throw;
            }
        }


        private async Task InitializeControlsInBatchesAsync(
            GameControl[] controls,
            Action<string, float> report)
        {
            int initialized = 0;
            for (int i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                if (control == null || control.Status != GameControlStatus.NonInitialized)
                    continue;

                // Initialize only one root control per frame. Many controls perform a sizeable
                // synchronous prefix before their first await, so Task.WhenAll over a batch still
                // produced 20-60 ms scene-transition actions.
                await control.Initialize();
                initialized++;

                // Initialize() may complete on a worker thread. Resume through the dispatcher
                // before touching scene progress state or starting the next GPU/UI control.
                await MuGame.YieldToNextFrameAsync(
                    $"SceneInit.{GetType().Name}.Control.{control.GetType().Name}[{initialized}/{controls.Length}]",
                    MainThreadDispatcher.WorkPriority.High);

                report?.Invoke(
                    $"Initializing UI Controls for {GetType().Name} ({initialized}/{controls.Length})...",
                    0.10f + 0.05f * initialized / Math.Max(1, controls.Length));
            }
        }

        protected virtual async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            // Base implementation calls the old Load method and reports basic progress
            progressCallback?.Invoke($"Loading content for {GetType().Name}...", 0.2f); // Generic start
            await Load(); // Calls existing Load method of the derived scene
            progressCallback?.Invoke($"Content loaded for {GetType().Name}.", 0.9f); // Generic end
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
            {
                long initializingControlTreeStarted = UpdatePassProfiler.Start();
                base.Update(gameTime);
                UpdatePassProfiler.AddSceneControlTree(initializingControlTreeStarted);
                return;
            }

            long sceneInputStarted = UpdatePassProfiler.Start();
            var currentFocusControl = FocusControl;
            var currentMouseControl = MouseControl;

            MouseControl = null;
            MouseHoverObject = null;
            IsMouseInputConsumedThisFrame = false;
            IsKeyboardEnterConsumedThisFrame = false;
            IsKeyboardEscapeConsumedThisFrame = false;

            // Simulate mouse click by touch input
            var touchState = MuGame.Instance.Touch;
            if (touchState.Count > 0)
            {
                var touch = touchState[0];
                int x = (int)touch.Position.X;
                int y = (int)touch.Position.Y;
                bool isTouchDown = touch.State == TouchLocationState.Pressed || touch.State == TouchLocationState.Moved;

                Mouse.SetPosition(x, y);

                MuGame.Instance.Mouse = new MouseState(
                    x,
                    y,
                    0,
                    isTouchDown ? ButtonState.Pressed : ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released
                );
            }

            var uiMouse = MuGame.Instance.UiMouseState;
            var prevUiMouse = MuGame.Instance.PrevUiMouseState;
            Point uiMousePosition = uiMouse.Position;

            FindTopmostUiControlsAtPoint(
                uiMousePosition,
                out GameControl topmostUiControl,
                out GameControl topmostInteractiveForScroll);

            MouseHoverControl = topmostUiControl; // general hover (tooltips, visual effects)
            MouseControl = topmostInteractiveForScroll; // target for scroll dispatch


            // no UI control captured the mouse for scroll interaction, check the World itself
            if (MouseControl == null && World != null && World.Visible && World.Interactive && World.IsMouseOver)
            {
                MouseControl = World; // world can handle scroll (camera zoom)
                if (MouseHoverControl == null)  // no UI element was hovered for tooltips
                {
                    MouseHoverControl = World;
                }
            }

            bool isPointerOverUi = topmostUiControl != null;
            bool leftPressed = uiMouse.LeftButton == ButtonState.Pressed;
            bool rightPressed = uiMouse.RightButton == ButtonState.Pressed;
            bool leftJustPressed = leftPressed && prevUiMouse.LeftButton == ButtonState.Released;
            bool rightJustPressed = rightPressed && prevUiMouse.RightButton == ButtonState.Released;
            bool leftJustReleased = !leftPressed && prevUiMouse.LeftButton == ButtonState.Pressed;
            bool rightJustReleased = !rightPressed && prevUiMouse.RightButton == ButtonState.Pressed;

            if (leftJustPressed && isPointerOverUi)
                _leftMouseCapturedByUi = true;

            if (rightJustPressed && isPointerOverUi)
                _rightMouseCapturedByUi = true;

            if ((isPointerOverUi && (leftPressed || rightPressed || leftJustReleased || rightJustReleased))
                || _leftMouseCapturedByUi
                || _rightMouseCapturedByUi)
            {
                IsMouseInputConsumedThisFrame = true;
            }

            if (leftJustReleased)
                _leftMouseCapturedByUi = false;

            if (rightJustReleased)
                _rightMouseCapturedByUi = false;

            // Consume scroll for UI before the world processes input
            int preScrollChange = MuGame.Instance.UiMouseState.ScrollWheelValue - MuGame.Instance.PrevUiMouseState.ScrollWheelValue;
            if (preScrollChange != 0 && MouseControl != null && MouseControl.Interactive && MouseControl != World)
            {
                IsMouseInputConsumedThisFrame = true;
            }

            if (World is WalkableWorldControl { Walker: not null } walkableWorld)
            {
                walkableWorld.Walker.MouseScrollToZoom = World == MouseHoverControl;
            }

            UpdatePassProfiler.AddSceneInput(sceneInputStarted);
            long controlTreeStarted = UpdatePassProfiler.Start();
            base.Update(gameTime);
            UpdatePassProfiler.AddSceneControlTree(controlTreeStarted);
            long scenePostStarted = UpdatePassProfiler.Start();

            if (Status != GameControlStatus.Ready)
            {
                UpdatePassProfiler.AddScenePost(scenePostStarted);
                return;
            }

            if (World == null)
            {
                UpdatePassProfiler.AddScenePost(scenePostStarted);
                return;
            }

            // Clear MouseHoverObject if no object is currently being hovered
            if (MouseHoverObject != null && !MouseHoverObject.IsMouseHover)
            {
                MouseHoverObject = null;
            }

            // Update cursor after world objects have been updated
            Cursor.Update(gameTime);

            // focus management (driven by GameControl.OnClick via FocusControlIfInteractive)
            if (FocusControl != currentFocusControl)
            {
                currentFocusControl?.OnBlur();
                FocusControl?.OnFocus();
            }

            // scroll handling - using the MouseControl determined above
            if (MouseControl != null && MouseControl.Interactive) // MouseControl here is the target for scroll
            {
                int scrollWheelChange = MuGame.Instance.UiMouseState.ScrollWheelValue - MuGame.Instance.PrevUiMouseState.ScrollWheelValue;
                if (scrollWheelChange != 0)
                {
                    int normalizedScrollDelta = scrollWheelChange; // positive for up, negative for down
                    if (MouseControl.ProcessMouseScroll(normalizedScrollDelta)) // UI handled it
                    {
                        if (MouseControl != World) // world can scroll (zoom) without "consuming" in this sense for other UI
                        {
                            IsMouseInputConsumedThisFrame = true;
                        }
                    }
                }

                if (MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed && !MouseControl.IsMousePressed)
                {
                    MouseControl.IsMousePressed = true;
                }
                else if (MouseControl == currentMouseControl && MuGame.Instance.UiMouseState.LeftButton == ButtonState.Released && MouseControl.IsMousePressed)
                {
                    MouseControl.IsMousePressed = false;
                    MouseControl.OnClick();
                    if (MouseControl != World) // a UI element (not the world) handled the click
                    {
                        IsMouseInputConsumedThisFrame = true;
                    }
                    MouseHoverControl = MouseControl;
                }
                else if (currentMouseControl != null && currentMouseControl.IsMousePressed && MouseControl != currentMouseControl)
                {
                    currentMouseControl.IsMousePressed = false;
                }
            }

            // handle 3D world object clicks if UI didn't consume input
            if (!IsMouseInputConsumedThisFrame && MouseHoverObject != null &&
                MuGame.Instance.PrevMouseState.LeftButton == ButtonState.Pressed &&
                MuGame.Instance.UiMouseState.LeftButton == ButtonState.Released)
            {
                MouseHoverObject.OnClick();
            }

            DebugPanel.BringToFront();
            Cursor.BringToFront();
            UpdatePassProfiler.AddScenePost(scenePostStarted);
        }

        public void FocusControlIfInteractive(GameControl control)
        {
            if (control != null && control.Interactive && FocusControl != control)
            {
                FocusControl = control;
                // Focus/OnBlur will be called in the main update loop check
            }
        }

        public void SetMouseInputConsumed()
        {
            IsMouseInputConsumedThisFrame = true;
        }

        public void ConsumeKeyboardEnter()
        {
            IsKeyboardEnterConsumedThisFrame = true;
        }

        public void ConsumeKeyboardEscape()
        {
            IsKeyboardEscapeConsumedThisFrame = true;
        }

        private void FindTopmostUiControlsAtPoint(
            Point mousePosition,
            out GameControl topmostControl,
            out GameControl topmostInteractiveControl)
        {
            topmostControl = null;
            topmostInteractiveControl = null;

            var controls = Controls.GetSnapshotArray();
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                FindTopmostUiControlsAtPointRecursive(
                    controls[i],
                    mousePosition,
                    ref topmostControl,
                    ref topmostInteractiveControl);

                if (topmostControl != null && topmostInteractiveControl != null)
                    return;
            }
        }

        private void FindTopmostUiControlsAtPointRecursive(
            GameControl control,
            Point mousePosition,
            ref GameControl topmostControl,
            ref GameControl topmostInteractiveControl)
        {
            if (control == null || !control.Visible || ReferenceEquals(control, World) || ReferenceEquals(control, Cursor))
                return;

            var children = control.Controls.GetSnapshotArray();
            for (int i = children.Length - 1; i >= 0; i--)
            {
                FindTopmostUiControlsAtPointRecursive(
                    children[i],
                    mousePosition,
                    ref topmostControl,
                    ref topmostInteractiveControl);

                if (topmostControl != null && topmostInteractiveControl != null)
                    return;
            }

            if (control is not UIControl || !IsPointInsideControl(control, mousePosition))
                return;

            if (topmostControl == null && ShouldUiControlCapturePointer(control, interactiveOnly: false))
                topmostControl = control;

            if (topmostInteractiveControl == null && control.Interactive)
                topmostInteractiveControl = control;
        }

        private static bool IsPointInsideControl(GameControl control, Point point)
        {
            var rect = control.DisplayRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return false;
            }

            return point.X >= rect.X
                && point.X < rect.Right
                && point.Y >= rect.Y
                && point.Y < rect.Bottom;
        }

        private static bool ShouldUiControlCapturePointer(GameControl control, bool interactiveOnly)
        {
            if (interactiveOnly)
            {
                return control.Interactive;
            }

            if (control.Interactive)
            {
                return true;
            }

            // Non-interactive controls are click-through by default.
            // Controls which must still block pointer (rare overlays) can opt-in explicitly.
            return control.CapturePointerWhenNonInteractive;
        }

        public override void Draw(GameTime gameTime)
        {
            if (World == null)
                return;

            // --- Pass 1: Render all 3D world geometry ---
            // This pass populates the depth buffer.
            World.Draw(gameTime);
            World.DrawAfter(gameTime);

            // Iterate only the post-cull visible set for overlay UI (nameplates / bboxes).
            // Avoids touching every WorldObject on the map for off-screen entities.
            var worldObjects = World.VisibleObjects;
            var droppedItems = World.DroppedItems;

            // --- Pass 2a: Render batched dropped-item shine sprites ---
            // Drawn as a single SpriteBatch to avoid per-item Begin/End overhead.
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.BackToFront,
                       BlendState.Additive,
                       GraphicsManager.GetQualityLinearSamplerState(),
                       DepthStencilState.DepthRead))
            {
                for (int i = 0; i < droppedItems.Count; i++)
                {
                    var item = droppedItems[i];
                    if (item == null) continue;
                    item.DrawShineEffect(gameTime);
                }
            }

            // --- Pass 2: Render 3D-aware UI (Nameplates, 2D BBoxes) ---
            // This batch respects the depth buffer populated by the 3D world.
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.BackToFront,       // Sort sprites by depth
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.DepthRead))     // Read depth buffer but don't write to it
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    var worldObject = worldObjects[i];
                    if (worldObject == null)
                        continue;

                    // Call the public methods to draw depth-aware UI elements
                    worldObject.DrawBoundingBox2D();

                    // Dropped items draw their labels in a separate pass (Depth=None) to avoid depth occlusion
                    // while still batching all labels in one SpriteBatch.
                    if (worldObject is DroppedItemObject)
                        continue;

                    worldObject.DrawHoverName();
                }
            }

            // --- Pass 2b: Render batched dropped-item labels (always visible) ---
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None))
            {
                for (int i = 0; i < droppedItems.Count; i++)
                {
                    var item = droppedItems[i];
                    if (item == null) continue;
                    item.DrawHoverName();
                }
            }

            // --- Pass 3: Render standard 2D UI (HUD overlays) ---
            // This batch ignores the depth buffer and draws on top of everything.
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       null,
                       null,
                       UiScaler.SpriteTransform))
            {
                var controls = Controls.GetSnapshot();
                for (int i = 0; i < controls.Count; i++)
                {
                    var ctrl = controls[i];
                    if (ctrl == null || ctrl == World || !ctrl.Visible)
                    {
                        continue;
                    }

                    ctrl.Draw(gameTime);
                }
            }
        }
    }
}
