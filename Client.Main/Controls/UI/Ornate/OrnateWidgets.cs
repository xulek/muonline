using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>Visual state of a widget for one frame.</summary>
    public readonly record struct WidgetState(bool Hover = false, bool Pressed = false, bool Selected = false, bool Disabled = false);

    /// <summary>
    /// Ready-made ornate widgets built from <see cref="OrnateDraw"/>. Each one draws into a rectangle the caller
    /// also uses for hit-testing; helpers such as <see cref="ToggleBounds"/> return the exact sub-rectangles.
    /// </summary>
    public static class OrnateWidgets
    {
        private static Texture2D Pixel => GraphicsManager.Instance.Pixel;

        // --- Buttons ------------------------------------------------------------------------------------

        /// <summary>Large menu button: plate, medallion icon on the left, bold label.</summary>
        public static void MenuButton(SpriteBatch sb, Rectangle rect, string label, string icon, WidgetState state,
            float alpha, bool danger = false)
        {
            if (state.Pressed)
                rect.Y += 1;

            float a = (state.Disabled ? 0.55f : 1f) * alpha;
            bool hover = state.Hover && !state.Disabled;
            OrnateDraw.Plate(sb, rect, hover, (hover ? Color.White : new Color(225, 218, 205)) * a, overhang: 0.35f);

            int iconSize = hover ? 42 : 38;
            OrnateDraw.Icon(sb, icon, new Rectangle(rect.X + 58 - iconSize / 2, rect.Center.Y - iconSize / 2, iconSize, iconSize), a);

            Color text = danger
                ? (hover ? OrnateStyle.DangerHover : OrnateStyle.Danger)
                : hover ? Color.White : OrnateStyle.Gold;
            OrnateDraw.TextLeft(sb, label, new Rectangle(rect.X + 92, rect.Y, rect.Width - 92, rect.Height), 17f, text, a, bold: true);
        }

        /// <summary>
        /// Compact button with a centred label (Back, Accept, ...). Keep <paramref name="overhang"/> at 1 wherever
        /// there is room; lower values crop the ornaments above/below the plate (only for tightly stacked rows).
        /// </summary>
        public static void Button(SpriteBatch sb, Rectangle rect, string label, WidgetState state, float alpha, float overhang = 1f)
        {
            if (state.Pressed)
                rect.Y += 1;

            float a = (state.Disabled ? 0.55f : 1f) * alpha;
            bool hover = state.Hover && !state.Disabled;
            OrnateDraw.Plate(sb, rect, hover, Color.White * a, overhang);
            Rectangle inner = OrnateDraw.PlateInner(hover ? OrnateAssets.ButtonHover : OrnateAssets.Button,
                hover ? OrnateStyle.ButtonHover : OrnateStyle.Button, rect);
            OrnateDraw.TextCentered(sb, label, rect, OrnateDraw.FitSize(label, 15f, inner.Width), hover ? Color.White : OrnateStyle.Gold, a, bold: true);
        }

        /// <summary>Main call to action (Enter Game, Accept): crimson plate with winged caps.</summary>
        public static void PrimaryButton(SpriteBatch sb, Rectangle rect, string label, WidgetState state, float alpha)
        {
            if (state.Pressed)
                rect.Y += 1;

            float a = (state.Disabled ? 0.45f : 1f) * alpha;
            bool hover = state.Hover && !state.Disabled;
            Texture2D texture = hover ? OrnateAssets.ButtonPrimaryHover : OrnateAssets.ButtonPrimary;
            PlateLayout layout = hover ? OrnateStyle.ButtonPrimaryHover : OrnateStyle.ButtonPrimary;
            OrnateDraw.Plate(sb, texture, layout, rect, hover, Color.White * a, overhang: 1f);
            Rectangle inner = OrnateDraw.PlateInner(texture, layout, rect);
            OrnateDraw.TextCentered(sb, label, rect, OrnateDraw.FitSize(label, 19f, inner.Width), hover ? Color.White : OrnateStyle.Gold, a, bold: true);
        }

        /// <summary>
        /// List card with an emblem socket on the left (character slots, saved items, ...). Returns the socket
        /// rectangle (inner opening of the ring); draw an <see cref="OrnateAssets.Emblem"/> into it with
        /// <see cref="SocketEmblem"/>. Ornaments above/below the card are drawn in full, so space cards by at least
        /// <c>height * 1.85</c>.
        /// </summary>
        public static Rectangle Card(SpriteBatch sb, Rectangle rect, WidgetState state, float alpha)
        {
            bool selected = state.Selected;
            Color tint = selected || state.Hover ? Color.White : new Color(172, 166, 156);
            OrnateDraw.Plate(sb, selected ? OrnateAssets.CardSelected : OrnateAssets.Card,
                selected ? OrnateStyle.CardSelected : OrnateStyle.Card, rect, selected, tint * alpha, overhang: 1f);
            return CardSocket(rect, selected);
        }

        public static Rectangle CardSocket(Rectangle rect, bool selected)
        {
            Texture2D texture = selected ? OrnateAssets.CardSelected : OrnateAssets.Card;
            if (texture == null)
            {
                int size = rect.Height - 8;
                return new Rectangle(rect.X + 6, rect.Center.Y - size / 2, size, size);
            }

            PlateLayout layout = selected ? OrnateStyle.CardSelected : OrnateStyle.Card;
            Vector3 socket = selected ? OrnateStyle.CardSelectedSocket : OrnateStyle.CardSocket;
            float scale = OrnateDraw.Measure(texture, layout, rect, 0.3f).Scale;
            float centerX = rect.X + socket.X * texture.Width * scale;
            float centerY = rect.Y + (socket.Y - layout.BandTop) * texture.Height * scale;
            int diameter = (int)MathF.Round(socket.Z * texture.Width * scale);
            return new Rectangle((int)MathF.Round(centerX - diameter / 2f), (int)MathF.Round(centerY - diameter / 2f), diameter, diameter);
        }

        /// <summary>Emblem disc filling a socket opening (1px larger so its soft edge tucks under the ring).</summary>
        public static void SocketEmblem(SpriteBatch sb, string emblem, Rectangle socket, float alpha)
        {
            Texture2D texture = OrnateAssets.Emblem(emblem);
            if (texture == null)
                return;
            socket.Inflate(1, 1);
            sb.Draw(texture, socket, Color.White * alpha);
        }

        /// <summary>Sidebar tab with icon; <see cref="WidgetState.Selected"/> marks the active tab.</summary>
        public static void Tab(SpriteBatch sb, Rectangle rect, string label, string icon, WidgetState state, float alpha)
        {
            bool active = state.Selected;
            bool hover = state.Hover;

            Color tint = active ? Color.White : hover ? new Color(235, 225, 205) : new Color(170, 165, 158);
            OrnateDraw.Plate(sb, rect, active, tint * alpha, overhang: 0.12f);

            var iconRect = new Rectangle(rect.X + 26, rect.Center.Y - 17, 34, 34);
            OrnateDraw.Icon(sb, icon, iconRect, (active || hover ? 1f : 0.8f) * alpha);

            Color text = active ? OrnateStyle.Gold : hover ? OrnateStyle.TextLight : OrnateStyle.TextMuted;
            OrnateDraw.TextLeft(sb, label, new Rectangle(iconRect.Right + 10, rect.Y, rect.Right - iconRect.Right - 10, rect.Height),
                15f, text, alpha, bold: active);
        }

        public static void CloseButton(SpriteBatch sb, Rectangle rect, bool hover, float alpha)
        {
            Texture2D texture = OrnateAssets.Close;
            if (hover)
                rect.Inflate(2, 2);

            if (texture != null)
                sb.Draw(texture, rect, (hover ? Color.White : new Color(215, 205, 190)) * alpha);
            else
                OrnateDraw.TextCentered(sb, "X", rect, 16f, OrnateStyle.Gold, alpha, bold: true);
        }

        // --- Value controls -----------------------------------------------------------------------------

        /// <summary>Switch rectangle inside a control area (left aligned, vertically centred).</summary>
        public static Rectangle ToggleBounds(Rectangle area)
        {
            Texture2D texture = OrnateAssets.ToggleOn;
            const int h = 24;
            int w = texture != null ? h * texture.Width / texture.Height : 80;
            return new Rectangle(area.X, area.Center.Y - h / 2, w, h);
        }

        /// <summary>On/off switch followed by an "On"/"Off" caption.</summary>
        public static void Toggle(SpriteBatch sb, Rectangle area, bool on, float alpha)
        {
            Rectangle rect = ToggleBounds(area);
            Texture2D texture = on ? OrnateAssets.ToggleOn : OrnateAssets.ToggleOff;
            if (texture != null)
            {
                sb.Draw(texture, rect, Color.White * alpha);
            }
            else
            {
                sb.Draw(Pixel, rect, (on ? new Color(150, 110, 40) : new Color(30, 28, 26)) * alpha);
                UiDrawHelper.DrawBorder(sb, rect, OrnateStyle.Bronze * alpha);
            }

            OrnateDraw.TextLeft(sb, on ? "On" : "Off", new Rectangle(rect.Right + 10, area.Y, 60, area.Height), 13f,
                on ? OrnateStyle.Gold : OrnateStyle.TextMuted, alpha, bold: on);
        }

        /// <summary>Slider track inside a control area; the value caption uses the remaining space on the right.</summary>
        public static Rectangle SliderTrack(Rectangle area) =>
            new(area.X + 10, area.Center.Y - 4, area.Width - 84, 8);

        /// <summary>Slider with gold fill, gem knob and right-aligned value caption. <paramref name="t"/> is 0..1.</summary>
        public static void Slider(SpriteBatch sb, Rectangle area, float t, string valueText, float alpha)
        {
            Rectangle track = SliderTrack(area);
            t = MathHelper.Clamp(t, 0f, 1f);
            int fillWidth = (int)MathF.Round(track.Width * t);

            sb.Draw(Pixel, new Rectangle(track.X - 1, track.Y - 1, track.Width + 2, track.Height + 2), OrnateStyle.BronzeDark * alpha);
            sb.Draw(Pixel, track, new Color(10, 9, 8) * alpha);
            sb.Draw(Pixel, new Rectangle(track.X, track.Y, track.Width, 2), Color.Black * alpha);
            if (fillWidth > 0)
            {
                var fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                UiDrawHelper.DrawVerticalGradient(sb, fill, new Color(255, 214, 120) * alpha, new Color(170, 104, 32) * alpha, 8);
                sb.Draw(Pixel, new Rectangle(fill.X, fill.Y, fill.Width, 1), new Color(255, 245, 200) * (0.7f * alpha));
            }

            const int knobSize = 24;
            var knob = new Rectangle(track.X + fillWidth - knobSize / 2, track.Center.Y - knobSize / 2, knobSize, knobSize);
            Texture2D knobTexture = OrnateAssets.SliderKnob;
            if (knobTexture != null)
                sb.Draw(knobTexture, knob, Color.White * alpha);
            else
                sb.Draw(Pixel, new Rectangle(knob.X + 6, knob.Y + 4, 12, 16), OrnateStyle.Gold * alpha);

            OrnateDraw.TextRight(sb, valueText, new Rectangle(area.X, area.Y, area.Width - 4, area.Height), 13f, OrnateStyle.Gold, alpha);
        }

        /// <summary>Selector box (◄ value ►) inside a control area.</summary>
        public static Rectangle ChoiceBox(Rectangle area) => new(area.X, area.Center.Y - 13, area.Width, 26);
        public static Rectangle ChoiceLeft(Rectangle box) => new(box.X + 3, box.Y + 1, 26, box.Height - 2);
        public static Rectangle ChoiceRight(Rectangle box) => new(box.Right - 29, box.Y + 1, 26, box.Height - 2);

        public static void Choice(SpriteBatch sb, Rectangle area, string value, bool canLeft, bool canRight,
            bool hoverLeft, bool hoverRight, float alpha)
        {
            Rectangle box = ChoiceBox(area);
            OrnateDraw.Inset(sb, box, alpha, 0.55f);
            UiDrawHelper.DrawBorder(sb, box, OrnateStyle.BronzeDark * (0.8f * alpha));
            OrnateDraw.TextCentered(sb, value, box, 13.5f, OrnateStyle.Gold, alpha, bold: true);
            Arrow(sb, ChoiceLeft(box), false, canLeft, hoverLeft, alpha);
            Arrow(sb, ChoiceRight(box), true, canRight, hoverRight, alpha);
        }

        public static void Arrow(SpriteBatch sb, Rectangle rect, bool right, bool enabled, bool hover, float alpha)
        {
            Texture2D texture = OrnateAssets.ArrowLeft;
            float a = (enabled ? (hover ? 1f : 0.85f) : 0.25f) * alpha;
            if (texture == null)
            {
                OrnateDraw.TextCentered(sb, right ? ">" : "<", rect, 14f, OrnateStyle.Gold, a);
                return;
            }

            int h = rect.Height - (hover && enabled ? 0 : 4);
            int w = h * texture.Width / texture.Height;
            var dest = new Rectangle(rect.Center.X - w / 2, rect.Center.Y - h / 2, w, h);
            sb.Draw(texture, dest, null, Color.White * a, 0f, Vector2.Zero, right ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        // --- Text input --------------------------------------------------------------------------------

        /// <summary>
        /// Makes a <see cref="TextFieldControl"/> transparent so it can sit inside <see cref="InputFrame"/>
        /// (the window draws the frame in its content pass, the field draws its text as a child on top).
        /// </summary>
        public static void StyleTextField(TextFieldControl field, float fontSize = 13f)
        {
            field.Skin = TextFieldSkin.Flat;
            field.BackgroundColor = Color.Transparent;
            field.BorderColor = Color.Transparent;
            field.BorderThickness = 0;
            field.TextColor = OrnateStyle.TextLight;
            field.FontSize = fontSize;
        }

        /// <summary>Recessed input box with a bronze rim that turns gold while focused (or red on error).</summary>
        public static void InputFrame(SpriteBatch sb, Rectangle rect, bool focused, bool error, float alpha)
        {
            OrnateDraw.Inset(sb, rect, alpha, 0.6f);
            Color rim = error ? OrnateStyle.Danger : focused ? OrnateStyle.Gold : OrnateStyle.BronzeDark;
            UiDrawHelper.DrawBorder(sb, rect, rim * ((focused || error ? 0.9f : 0.75f) * alpha));
            if (focused)
                UiDrawHelper.DrawBorder(sb, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), OrnateStyle.Gold * (0.25f * alpha));
        }

        // --- Lists --------------------------------------------------------------------------------------

        /// <summary>Gold section caption with a fading rule after it, aligned to the bottom of <paramref name="row"/>.</summary>
        public static void SectionHeader(SpriteBatch sb, Rectangle row, string label, float alpha)
        {
            string caption = label.ToUpperInvariant();
            Vector2 size = OrnateDraw.Measure(caption, 14f);
            float textY = row.Bottom - size.Y - 5;
            OrnateDraw.Text(sb, caption, new Vector2(row.X + 6, textY), 14f, OrnateStyle.GoldDim, alpha, bold: true);
            float lineX = row.X + 16 + size.X;
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle((int)lineX, (int)(textY + size.Y * 0.55f), (int)(row.Right - lineX), 1),
                OrnateStyle.Bronze * (0.9f * alpha), Color.Transparent, 24);
        }

        /// <summary>Striped list row with a gold hover band and accent bar.</summary>
        public static void RowBackground(SpriteBatch sb, Rectangle row, int index, bool hover, float alpha)
        {
            sb.Draw(Pixel, row, Color.Black * ((index % 2 == 0 ? 0.22f : 0.12f) * alpha));
            if (!hover)
                return;

            Color glow = new(200, 150, 70);
            UiDrawHelper.DrawHorizontalGradient(sb, row, glow * (0.2f * alpha), glow * (0.02f * alpha), 24);
            sb.Draw(Pixel, new Rectangle(row.X, row.Y, 3, row.Height), OrnateStyle.Gold * alpha);
        }

        public static void Scrollbar(SpriteBatch sb, Rectangle track, int first, int visible, int total, float alpha)
        {
            if (total <= visible)
                return;

            sb.Draw(Pixel, track, Color.Black * (0.5f * alpha));
            int thumbHeight = Math.Max(24, track.Height * visible / total);
            int thumbY = track.Y + (track.Height - thumbHeight) * first / Math.Max(1, total - visible);
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(track.X, thumbY, track.Width, thumbHeight),
                OrnateStyle.Gold * alpha, OrnateStyle.Bronze * alpha, 4);
        }
    }
}
