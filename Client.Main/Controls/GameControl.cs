using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Content;

namespace Client.Main.Controls
{
    public abstract class GameControl : IChildItem<GameControl>, IDisposable
    {
        public virtual bool NonDisposable => false;

        // Fields
        private Point _controlSize, _viewSize;
        private GameControl _parent;
        private ControlAlign _align;
        private bool _autoViewSize = true;
        private int _x, _y;
        private float _scale = 1f;
        private Margin _margin = Margin.Empty;
        private Margin _padding = new Margin();
        private Point _offset;
        private bool _visible = true;
        private bool _layoutDirty = true;
        private bool _isCurrentlyPressedByMouse = false;
        private Point _cachedDisplayPosition;
        private Point _cachedDisplaySize;
        private Rectangle _cachedDisplayRectangle;
        private int _displayRevision = 1;
        private int _cachedDisplayRevision;
        private int _cachedParentDisplayGeneration = -1;
        private int _displayGeneration;
        private static readonly ILogger _logger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { }).CreateLogger<GameControl>();

        // Properties
        public GraphicsDevice GraphicsDevice => MuGame.Instance.GraphicsDevice;
        public GameControl Root => Parent?.Root ?? this;
        public GameControl Parent
        {
            get => _parent;
            set
            {
                if (ReferenceEquals(_parent, value))
                    return;

                _parent = value;
                MarkLayoutDirty();
            }
        }
        public BaseScene Scene => this is BaseScene scene ? scene : Parent?.Scene;
        public virtual WorldControl World => Scene?.World;

        public ChildrenCollection<GameControl> Controls { get; private set; }
        public GameControlStatus Status { get; protected set; } = GameControlStatus.NonInitialized;
        public ControlAlign Align
        {
            get => _align;
            set
            {
                if (_align == value)
                    return;

                _align = value;
                MarkLayoutDirty();
            }
        }
        public bool AutoViewSize
        {
            get => _autoViewSize;
            set
            {
                if (_autoViewSize == value)
                    return;

                _autoViewSize = value;
                MarkLayoutDirty();
            }
        }
        public bool Interactive { get; set; }
        /// <summary>
        /// Allows non-interactive controls to still capture pointer hit-testing in scene input routing.
        /// Defaults to false to keep click-through behavior for visual-only overlays.
        /// </summary>
        public bool CapturePointerWhenNonInteractive { get; set; }
        public int X
        {
            get => _x;
            set
            {
                if (_x == value)
                    return;

                _x = value;
                MarkLayoutDirty();
            }
        }
        public int Y
        {
            get => _y;
            set
            {
                if (_y == value)
                    return;

                _y = value;
                MarkLayoutDirty();
            }
        }
        public Point ControlSize
        {
            get => _controlSize;
            set
            {
                if (_controlSize == value)
                    return;

                _controlSize = value;
                MarkLayoutDirty();
            }
        }
        public Point ViewSize
        {
            get => _viewSize;
            set
            {
                if (_viewSize == value)
                    return;

                _viewSize = value;
                MarkLayoutDirty();
                OnScreenSizeChanged();
            }
        }
        public float Scale
        {
            get => _scale;
            set
            {
                if (Math.Abs(_scale - value) < 0.0001f)
                    return;

                _scale = value;
                MarkLayoutDirty();
            }
        }
        public Margin Margin
        {
            get => _margin;
            set
            {
                if (EqualityComparer<Margin>.Default.Equals(_margin, value))
                    return;

                _margin = value;
                MarkLayoutDirty();
            }
        }
        public Margin Padding
        {
            get => _padding;
            set
            {
                if (EqualityComparer<Margin>.Default.Equals(_padding, value))
                    return;

                _padding = value;
                MarkLayoutDirty();
            }
        }
        public Color BorderColor { get; set; }
        public int BorderThickness { get; set; } = 0;
        public Color BackgroundColor { get; set; } = Color.Transparent;
        public Point Offset
        {
            get => _offset;
            set
            {
                if (_offset == value)
                    return;

                _offset = value;
                MarkLayoutDirty();
            }
        }
        public virtual Point DisplayPosition
        {
            get
            {
                EnsureDisplayCache();
                return _cachedDisplayPosition;
            }
        }

        public virtual Point DisplaySize
        {
            get
            {
                EnsureDisplayCache();
                return _cachedDisplaySize;
            }
        }

        public virtual Rectangle DisplayRectangle
        {
            get
            {
                EnsureDisplayCache();
                return _cachedDisplayRectangle;
            }
        }
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value)
                    return;

                _visible = value;
                MarkLayoutDirty();
            }
        }

        // Added property for storing additional data (e.g., design info)
        public object Tag { get; set; }
        // Added Name property to identify controls
        public string Name { get; set; }
        // Added Alpha property for controlling transparency (1 = fully opaque)
        public float Alpha { get; set; } = 1f;

        public bool IsMouseOver { get; set; }
        public bool IsMousePressed { get; set; }
        public bool HasFocus => Scene?.FocusControl == this;

        // Events
        public event EventHandler Click;
        public event EventHandler SizeChanged;
        public event EventHandler Focus;
        public event EventHandler Blur;

        // Constructors
        protected GameControl()
        {
            Controls = new ChildrenCollection<GameControl>(this);
            Controls.ControlAdded += OnChildCollectionChanged;
            Controls.ControlRemoved += OnChildCollectionChanged;
        }

        protected virtual MouseState CurrentMouseState => MuGame.Instance.Mouse;
        protected virtual MouseState PreviousMouseState => MuGame.Instance.PrevMouseState;

        // Public Methods
        public virtual bool OnClick()
        {
            // Focus this control when clicked, if it's interactive.
            // Scene.FocusControl is now set in BaseScene's Update.
            // Here, we just invoke the event.
            Click?.Invoke(this, EventArgs.Empty);
            return Click != null; // consumes if there's a subscriber
        }

        public virtual void OnFocus()
        {
            Focus?.Invoke(this, EventArgs.Empty);
        }

        public virtual void OnBlur()
        {
            Blur?.Invoke(this, EventArgs.Empty);
        }

        public virtual async Task Initialize()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            try
            {
                Status = GameControlStatus.Initializing;

                var controls = Controls.GetSnapshotArray();
                var taskList = new List<Task>(controls.Length);

                for (int i = 0; i < controls.Length; i++)
                {
                    var control = controls[i];
                    if (control.Status == GameControlStatus.NonInitialized)
                        taskList.Add(control.Initialize());
                }

                await Task.WhenAll(taskList);

                await Load();
                AfterLoad();

                Status = GameControlStatus.Ready;
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to initialize control {ControlType}", GetType().FullName);
                Status = GameControlStatus.Error;
            }
        }

        public virtual Task Load()
        {
            return Task.CompletedTask;
        }

        public virtual void AfterLoad()
        {
            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
                controls[i].AfterLoad();
        }

        public virtual void Update(GameTime gameTime)
        {
            if (Status == GameControlStatus.NonInitialized && (Parent == null || Parent.Status == GameControlStatus.Ready))
            {
                // Fire and forget initialization
                _ = Initialize();
            }

            if (Status != GameControlStatus.Ready || !Visible) return;

            // Non-interactive visual controls make up most of the UI tree. Avoid fetching
            // mouse state and recursively calculating DisplayRectangle unless the control can
            // consume or explicitly capture pointer input.
            bool needsPointerHitTest = Interactive || CapturePointerWhenNonInteractive;
            if (!needsPointerHitTest)
            {
                IsMouseOver = false;
                IsMousePressed = false;
                _isCurrentlyPressedByMouse = false;
            }
            else
            {
                var mouse = CurrentMouseState;
                Rectangle rectangle = DisplayRectangle;
                Point mousePosition = mouse.Position;
                IsMouseOver = mousePosition.X >= rectangle.Left && mousePosition.X <= rectangle.Right &&
                              mousePosition.Y >= rectangle.Top && mousePosition.Y <= rectangle.Bottom;

                if (!Interactive)
                {
                    IsMousePressed = false;
                    _isCurrentlyPressedByMouse = false;
                }
                else
                {
                    var previousMouse = PreviousMouseState;
                    if (IsMouseOver)
                    {
                        if (mouse.LeftButton == ButtonState.Pressed)
                        {
                            IsMousePressed = true;
                            if (previousMouse.LeftButton == ButtonState.Released)
                            {
                                _isCurrentlyPressedByMouse = true;
                                Scene?.FocusControlIfInteractive(this);
                            }
                        }
                        else
                        {
                            if (_isCurrentlyPressedByMouse && previousMouse.LeftButton == ButtonState.Pressed && OnClick())
                            {
                                if (Scene is BaseScene baseScene && this != baseScene.World)
                                    baseScene.SetMouseInputConsumed();
                            }

                            _isCurrentlyPressedByMouse = false;
                            IsMousePressed = false;
                        }
                    }
                    else
                    {
                        IsMousePressed = false;
                        if (mouse.LeftButton == ButtonState.Released)
                            _isCurrentlyPressedByMouse = false;
                    }
                }
            }

            // Iterate over the cached immutable snapshot. It is rebuilt only when the
            // collection changes, avoiding one lock for Count and another for every index.
            var controls = Controls.GetSnapshotArray();
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                var control = controls[i];
                // A child removed by a previous sibling remains in this frame's snapshot,
                // so skip it when disposal already completed.
                if (control.Status != GameControlStatus.Disposed &&
                    ReferenceEquals(control.Parent, this))
                {
                    control.Update(gameTime);
                }
            }

            if (_layoutDirty)
                RecalculateLayout();
        }

        public virtual bool ProcessMouseScroll(int scrollDelta)
        {
            // Base implementation: does not handle scroll, bubbles to children if needed.
            // Most specific controls (like scrollbars) will override this.
            // For container controls, they might iterate their children.
            return false;
        }

        public virtual async Task PreloadAssetsAsync()
        {
            if (this is TextureControl tc && !string.IsNullOrEmpty(tc.TexturePath))
            {
                await TextureLoader.Instance.PrepareAndGetTexture(tc.TexturePath);
            }

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
                await controls[i].PreloadAssetsAsync();
        }

        public virtual void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            DrawBackground();
            DrawBorder();

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                var child = controls[i];
                if (ReferenceEquals(child.Parent, this))
                    child.Draw(gameTime);
            }
        }

        public virtual void DrawAfter(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                var child = controls[i];
                if (ReferenceEquals(child.Parent, this))
                    child.DrawAfter(gameTime);
            }
        }

        public virtual void Dispose()
        {
            if (NonDisposable) return;

            Controls.ControlAdded -= OnChildCollectionChanged;
            Controls.ControlRemoved -= OnChildCollectionChanged;

            var controls = Controls.GetSnapshotArray();
            for (int i = controls.Length - 1; i >= 0; i--)
                controls[i].Dispose();

            Controls.Clear();

            Parent?.Controls.Remove(this);

            Status = GameControlStatus.Disposed;
            // Make sure to unhook any event handlers to prevent memory leaks
            Click = null;
            SizeChanged = null;
            Focus = null;
            Blur = null;
        }

        public void BringToFront()
        {
            if (Status == GameControlStatus.Disposed) return;
            if (Parent == null) return;
            var siblings = Parent.Controls.GetSnapshotArray();
            if (siblings.Length == 0 || siblings[^1] == this) return;
            Parent.Controls.MoveToEnd(this);
        }

        public void SendToBack()
        {
            if (Status == GameControlStatus.Disposed) return;
            if (Parent == null) return;
            var siblings = Parent.Controls.GetSnapshotArray();
            if (siblings.Length == 0 || siblings[0] == this) return;
            Parent.Controls.MoveToStart(this);
        }

        // Protected Methods
        private void OnChildCollectionChanged(GameControl child)
        {
            MarkLayoutDirty();
        }

        private void RecalculateLayout()
        {
            if (AutoViewSize)
            {
                int maxWidth = 0;
                int maxHeight = 0;

                var controls = Controls.GetSnapshotArray();
                for (int i = 0; i < controls.Length; i++)
                {
                    var control = controls[i];
                    if (control.Status == GameControlStatus.Disposed)
                        continue;

                    int controlWidth = control.DisplaySize.X + control.Margin.Left;
                    int controlHeight = control.DisplaySize.Y + control.Margin.Top;

                    if (!Align.HasFlag(ControlAlign.Left))
                        controlWidth += control.X;
                    if (!Align.HasFlag(ControlAlign.Bottom))
                        controlHeight += control.Y;

                    maxWidth = Math.Max(maxWidth, controlWidth);
                    maxHeight = Math.Max(maxHeight, controlHeight);
                }

                Point requiredSize = new(
                    Math.Max(ControlSize.X, maxWidth),
                    Math.Max(ControlSize.Y, maxHeight));

                if (_viewSize != requiredSize)
                {
                    _viewSize = requiredSize;
                    InvalidateDisplayCache();
                    OnScreenSizeChanged();
                }
            }

            if (Align != ControlAlign.None)
                AlignControl();

            _layoutDirty = false;
        }

        protected void MarkLayoutDirty()
        {
            _layoutDirty = true;
            InvalidateDisplayCache();

            if (Parent != null)
            {
                Parent._layoutDirty = true;
                Parent.InvalidateDisplayCache();
            }

            if (Controls == null)
                return;

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i]._layoutDirty = true;
                controls[i].InvalidateDisplayCache();
            }
        }

        private void InvalidateDisplayCache()
        {
            unchecked
            {
                _displayRevision++;
                if (_displayRevision == 0)
                    _displayRevision = 1;
            }
        }

        private void EnsureDisplayCache()
        {
            Rectangle parentRectangle = default;
            int parentGeneration = 0;
            if (Parent != null)
            {
                Parent.EnsureDisplayCache();
                parentRectangle = Parent._cachedDisplayRectangle;
                parentGeneration = Parent._displayGeneration;
            }

            if (_cachedDisplayRevision == _displayRevision &&
                _cachedParentDisplayGeneration == parentGeneration)
            {
                return;
            }

            _cachedDisplayPosition = new Point(
                parentRectangle.X + _x + _margin.Left - _margin.Right + _offset.X,
                parentRectangle.Y + _y + _margin.Top - _margin.Bottom + _offset.Y);
            _cachedDisplaySize = new Point(
                (int)(_viewSize.X * _scale),
                (int)(_viewSize.Y * _scale));
            _cachedDisplayRectangle = new Rectangle(_cachedDisplayPosition, _cachedDisplaySize);
            _cachedDisplayRevision = _displayRevision;
            _cachedParentDisplayGeneration = parentGeneration;

            unchecked
            {
                _displayGeneration++;
                if (_displayGeneration == 0)
                    _displayGeneration = 1;
            }
        }

        protected virtual void AlignControl()
        {
            if (Parent == null)
            {
                Y = 0;
                X = 0;
                return;
            }

            if (Align == ControlAlign.None)
                return;

            if (Align.HasFlag(ControlAlign.Top))
                Y = 0;
            else if (Align.HasFlag(ControlAlign.Bottom))
                Y = Parent.DisplaySize.Y - DisplaySize.Y;
            else if (Align.HasFlag(ControlAlign.VerticalCenter))
                Y = (Parent.DisplaySize.Y / 2) - (DisplaySize.Y / 2);

            if (Align.HasFlag(ControlAlign.Left))
                X = 0;
            else if (Align.HasFlag(ControlAlign.Right))
                X = Parent.DisplaySize.X - DisplaySize.X;
            else if (Align.HasFlag(ControlAlign.HorizontalCenter))
                X = (Parent.DisplaySize.X / 2) - (DisplaySize.X / 2);
        }

        protected void DrawBackground()
        {
            if (BackgroundColor.A == 0) // Check alpha for transparency
                return;

            GraphicsManager.Instance.Sprite.Draw(
                GraphicsManager.Instance.Pixel,
                DisplayRectangle,
                BackgroundColor * Alpha // Apply control's alpha
            );
        }

        protected void DrawBorder()
        {
            if (BorderThickness <= 0 || BorderColor.A == 0) // Check alpha for transparency
                return;

            Color finalBorderColor = BorderColor * Alpha;
            Rectangle rectangle = DisplayRectangle;
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            SpriteBatch sprite = GraphicsManager.Instance.Sprite;

            sprite.Draw(pixel, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, BorderThickness), finalBorderColor);
            sprite.Draw(pixel, new Rectangle(rectangle.X, rectangle.Bottom - BorderThickness, rectangle.Width, BorderThickness), finalBorderColor);
            sprite.Draw(pixel, new Rectangle(rectangle.X, rectangle.Y, BorderThickness, rectangle.Height), finalBorderColor);
            sprite.Draw(pixel, new Rectangle(rectangle.Right - BorderThickness, rectangle.Y, BorderThickness, rectangle.Height), finalBorderColor);
        }

        protected virtual void OnScreenSizeChanged()
        {
            if (Parent != null)
                Parent._layoutDirty = true;

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i]._layoutDirty = true;
                controls[i].InvalidateDisplayCache();
            }

            SizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
