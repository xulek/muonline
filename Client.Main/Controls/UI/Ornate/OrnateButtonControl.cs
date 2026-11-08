using System;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Ornate
{
    public enum OrnateButtonStyle
    {
        /// <summary>Bronze plate with a gold label.</summary>
        Standard,
        /// <summary>Crimson winged plate for the main action of a screen.</summary>
        Primary
    }

    /// <summary>Stand-alone ornate button for screens that are not built as an <see cref="OrnateWindowControl"/>.</summary>
    public sealed class OrnateButtonControl : UIControl
    {
        private readonly ClickTracker _clicks = new();

        public OrnateButtonControl(string label, OrnateButtonStyle style, Point size)
        {
            Label = label;
            Style = style;
            Interactive = true;
            AutoViewSize = false;
            ControlSize = size;
            ViewSize = size;
        }

        public string Label { get; set; }
        public OrnateButtonStyle Style { get; set; }
        public bool Enabled { get; set; } = true;
        public float Opacity { get; set; } = 1f;

        public event EventHandler Clicked;

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            OrnatePointer pointer = OrnatePointer.Capture();
            int hover = Enabled && DisplayRectangle.Contains(pointer.Position) ? 1 : ClickTracker.None;
            if (_clicks.Update(hover, pointer) != ClickTracker.None)
            {
                SoundController.Instance?.PlayBuffer("Sound/iButtonClick.wav");
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var state = new WidgetState(_clicks.IsHover(1), _clicks.IsPressed(1), Disabled: !Enabled);
            var sb = GraphicsManager.Instance.Sprite;
            if (Style == OrnateButtonStyle.Primary)
                OrnateWidgets.PrimaryButton(sb, DisplayRectangle, Label, state, Opacity);
            else
                OrnateWidgets.Button(sb, DisplayRectangle, Label, state, Opacity, overhang: 1f);
        }
    }
}
