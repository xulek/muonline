using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>One button of an <see cref="OrnateMenuWindow"/>.</summary>
    public sealed record OrnateMenuEntry(string Label, string Icon, Action OnClick, bool Danger = false);

    /// <summary>
    /// Vertical menu of large icon buttons with an optional info line under the title and a footer hint.
    /// The window height follows the number of entries.
    /// </summary>
    public sealed class OrnateMenuWindow : OrnateWindowControl
    {
        private const int Width = 380;
        private const int ButtonWidth = 310;
        private const int ButtonHeight = 50;
        private const int ButtonTop = 80;
        private const int ButtonStep = 64;
        private const int FooterHeight = 70;

        private readonly IReadOnlyList<OrnateMenuEntry> _entries;

        public OrnateMenuWindow(string title, IReadOnlyList<OrnateMenuEntry> entries)
            : base(new Point(Width, ButtonTop + entries.Count * ButtonStep + FooterHeight))
        {
            Title = title;
            _entries = entries;
            SlideDistance = 14;
        }

        /// <summary>Line under the title (e.g. character name); null or empty hides it.</summary>
        public Func<string> InfoLine { get; set; }

        public string Footer { get; set; }

        /// <summary>While busy the buttons are dimmed, ignore input and the footer shows <see cref="BusyFooter"/>.</summary>
        public bool Busy { get; set; }
        public string BusyFooter { get; set; } = "Please wait...";

        protected override IEnumerable<string> PreloadPaths => _entries.Select(e => "Icons/" + e.Icon);
        protected override bool InputEnabled => !Busy;

        private Rectangle ButtonBounds(int index) =>
            At((Width - ButtonWidth) / 2, ButtonTop + index * ButtonStep, ButtonWidth, ButtonHeight);

        protected override int HitTest(Point position)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (ButtonBounds(i).Contains(position))
                    return FirstHitId + i;
            return HitNone;
        }

        protected override void OnActivate(int hit)
        {
            PlayClickSound();
            _entries[hit - FirstHitId].OnClick?.Invoke();
        }

        protected override void DrawContent(SpriteBatch sb, float alpha)
        {
            Rectangle frame = Frame;

            string info = InfoLine?.Invoke();
            if (!string.IsNullOrEmpty(info))
                OrnateDraw.TextCentered(sb, info, At(0, 42, frame.Width, 24), 13f, OrnateStyle.TextMuted, alpha);

            // Hovered button last so its ornaments sit on top of its neighbours.
            int hovered = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!Busy && Clicks.IsHover(FirstHitId + i))
                    hovered = i;
                else
                    DrawEntry(sb, i, alpha);
            }
            if (hovered >= 0)
                DrawEntry(sb, hovered, alpha);

            int footerTop = ButtonTop + _entries.Count * ButtonStep;
            OrnateDraw.Divider(sb, At(24, footerTop - 4, frame.Width - 48, 26), alpha);
            OrnateDraw.TextCentered(sb, Busy ? BusyFooter : Footer, At(0, footerTop + 26, frame.Width, 24), 12f, OrnateStyle.TextMuted, alpha);
        }

        private void DrawEntry(SpriteBatch sb, int index, float alpha)
        {
            OrnateMenuEntry entry = _entries[index];
            int id = FirstHitId + index;
            var state = new WidgetState(Hover: Clicks.IsHover(id), Pressed: Clicks.IsPressed(id), Disabled: Busy);
            OrnateWidgets.MenuButton(sb, ButtonBounds(index), entry.Label, entry.Icon, state, alpha, entry.Danger);
        }
    }
}
