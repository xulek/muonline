using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>
    /// Base for immediate-mode ornate windows. Provides the framed window with title plaque and optional close
    /// button, fade-in, and pointer handling: subclasses map positions to integer hit ids in <see cref="HitTest"/>,
    /// react to clicks in <see cref="OnActivate"/> and draw in <see cref="DrawContent"/> using the same layout
    /// helpers, so visuals and click areas cannot drift apart. Ids below <see cref="FirstHitId"/> are reserved.
    /// </summary>
    public abstract class OrnateWindowControl : UIControl
    {
        protected const int HitNone = ClickTracker.None;
        protected const int HitClose = 1;
        protected const int FirstHitId = 10;

        protected OrnateWindowControl(Point size)
        {
            Interactive = true;
            AutoViewSize = false;
            ControlSize = size;
            ViewSize = size;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        public string Title { get; set; }
        public int TitlePlateHeight { get; set; } = 40;
        public bool ShowCloseButton { get; set; }

        /// <summary>0..1 fade set by the host; while below 1 the window also slides down by up to <see cref="SlideDistance"/>.</summary>
        public float Opacity { get; set; } = 1f;
        public int SlideDistance { get; set; }

        /// <summary>When above 0, the whole screen behind the window is darkened (modal dialogs).</summary>
        public float ModalDim { get; set; }

        /// <summary>Raised by the close button.</summary>
        public event EventHandler CloseRequested;

        protected ClickTracker Clicks { get; } = new();

        /// <summary>Extra kit paths (<c>"Icons/x"</c>, <c>"Emblems/x"</c>) preloaded with the frame textures.</summary>
        protected virtual IEnumerable<string> PreloadPaths => null;

        /// <summary>When false the window ignores the pointer (e.g. while an operation is in progress).</summary>
        protected virtual bool InputEnabled => true;

        protected Rectangle Frame
        {
            get
            {
                Rectangle rect = DisplayRectangle;
                rect.Y += (int)MathF.Round((1f - Opacity) * SlideDistance);
                return rect;
            }
        }

        /// <summary>Rectangle relative to the window's top-left corner.</summary>
        protected Rectangle At(int x, int y, int width, int height) => new(Frame.X + x, Frame.Y + y, width, height);

        protected Rectangle CloseButtonBounds => At(Frame.Width - 56, 16, 34, 34);

        public override async Task Load()
        {
            await base.Load();
            await OrnateAssets.PreloadAsync(PreloadPaths);
        }

        // --- Input --------------------------------------------------------------------------------------

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            if (!InputEnabled)
            {
                Clicks.Reset();
                return;
            }

            OrnatePointer pointer = OrnatePointer.Capture();
            if (UpdateDrag(pointer))
                return;

            int hover = ShowCloseButton && CloseButtonBounds.Contains(pointer.Position) ? HitClose : HitTest(pointer.Position);
            if (pointer.Pressed && TryBeginDrag(hover, pointer))
            {
                Clicks.Reset();
                return;
            }

            int clicked = Clicks.Update(hover, pointer);
            if (clicked == HitClose)
            {
                PlayClickSound();
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (clicked != HitNone)
            {
                OnActivate(clicked);
            }
        }

        /// <summary>Maps a pointer position to a hit id (&gt;= <see cref="FirstHitId"/>) or <see cref="HitNone"/>.</summary>
        protected abstract int HitTest(Point position);

        /// <summary>Called when an element was pressed and released.</summary>
        protected abstract void OnActivate(int hit);

        /// <summary>Return true to start dragging the element under the pointer instead of clicking it.</summary>
        protected virtual bool TryBeginDrag(int hit, in OrnatePointer pointer) => false;

        /// <summary>Called every frame before hit-testing; return true while a drag owns the pointer.</summary>
        protected virtual bool UpdateDrag(in OrnatePointer pointer) => false;

        protected static void PlayClickSound() => SoundController.Instance?.PlayBuffer("Sound/iButtonClick.wav");

        // --- Drawing ------------------------------------------------------------------------------------

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            SpriteBatch sb = GraphicsManager.Instance.Sprite;
            float alpha = Opacity;
            Rectangle frame = Frame;

            if (ModalDim > 0f)
            {
                Point screen = UiScaler.VirtualSize;
                sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(0, 0, screen.X, screen.Y), Color.Black * (ModalDim * alpha));
            }

            OrnateDraw.Window(sb, frame, alpha);
            DrawContent(sb, alpha);
            if (ShowCloseButton)
                OrnateWidgets.CloseButton(sb, CloseButtonBounds, Clicks.IsHover(HitClose), alpha);
            if (!string.IsNullOrEmpty(Title))
                OrnateDraw.TitlePlaque(sb, Title, frame.Center.X, frame.Y + 2, TitlePlateHeight, alpha);

            base.Draw(gameTime);
        }

        protected abstract void DrawContent(SpriteBatch sb, float alpha);
    }
}
