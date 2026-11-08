using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>One frame of UI pointer input (virtual UI coordinates) for immediate-mode ornate windows.</summary>
    public readonly struct OrnatePointer
    {
        public Point Position { get; }
        public bool Down { get; }
        /// <summary>Left button went down this frame.</summary>
        public bool Pressed { get; }
        /// <summary>Left button went up this frame.</summary>
        public bool Released { get; }

        private OrnatePointer(Point position, bool down, bool wasDown)
        {
            Position = position;
            Down = down;
            Pressed = down && !wasDown;
            Released = !down && wasDown;
        }

        /// <summary>Diagnostics only: replaces the mouse position, e.g. for hover screenshots.</summary>
        public static Point? DiagnosticPosition { get; set; }

        public static OrnatePointer Capture()
        {
            MouseState mouse = MuGame.Instance.UiMouseState;
            MouseState previous = MuGame.Instance.PrevUiMouseState;
            return new OrnatePointer(
                DiagnosticPosition ?? mouse.Position,
                mouse.LeftButton == ButtonState.Pressed,
                previous.LeftButton == ButtonState.Pressed);
        }
    }

    /// <summary>
    /// Press/release matching for hit ids: an element is clicked when the button is pressed and released over the
    /// same id. Id 0 means "nothing".
    /// </summary>
    public sealed class ClickTracker
    {
        public const int None = 0;

        public int Hover { get; private set; }
        public int Pressed { get; private set; }

        public bool IsHover(int id) => Hover == id && id != None;
        public bool IsPressed(int id) => Pressed == id && Hover == id && id != None;

        /// <summary>Updates hover/press state and returns the clicked id, or <see cref="None"/>.</summary>
        public int Update(int hover, in OrnatePointer pointer)
        {
            Hover = hover;
            if (pointer.Pressed)
            {
                Pressed = hover;
                return None;
            }

            if (!pointer.Released)
                return None;

            int clicked = Pressed != None && Pressed == hover ? hover : None;
            Pressed = None;
            return clicked;
        }

        /// <summary>Forgets the pending press, e.g. when a drag took over the pointer.</summary>
        public void CancelPress() => Pressed = None;

        public void Reset() => Hover = Pressed = None;
    }
}
