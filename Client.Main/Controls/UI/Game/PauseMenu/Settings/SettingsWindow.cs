using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controls.UI.Ornate;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.PauseMenu.Settings
{
    /// <summary>
    /// MMO-style settings window: category tabs on the left, option rows (switches, sliders, selectors) on the
    /// right, a description line for the hovered option and Back / Resume buttons. Options come from
    /// <see cref="SettingsCatalog"/>.
    /// </summary>
    internal sealed class SettingsWindow : OrnateWindowControl
    {
        private const int WindowWidth = 880;
        private const int WindowHeight = 600;
        private const int TabX = 30, TabY = 76, TabWidth = 212, TabHeight = 44, TabStep = 52;
        private const int ContentX = 272, ContentRight = WindowWidth - 26;
        private const int RowsTop = 146, RowHeight = 34, VisibleRows = 10;
        private const int ControlWidth = 250;

        // Hit ids: buttons, tabs (HitTab + index), rows (HitRow + index * 4 + part).
        private const int HitBack = FirstHitId, HitResume = FirstHitId + 1, HitTab = 100, HitRow = 1000;
        private const int PartRow = 0, PartLeft = 1, PartRight = 2, PartCenter = 3;

        private readonly List<SettingsCategory> _categories;
        private List<SettingOption> _options = new();
        private int _category;
        private int _scroll;
        private bool _rebuildRequested;
        private SettingSlider _dragSlider;
        private float _dragValue;

        public SettingsWindow() : base(new Point(WindowWidth, WindowHeight))
        {
            Title = "SETTINGS";
            ShowCloseButton = true;
            _categories = SettingsCatalog.Create(() => _rebuildRequested = true);
            SelectCategory(0);
        }

        /// <summary>Back button / ESC: return to the previous menu.</summary>
        public event EventHandler BackRequested;

        /// <summary>Resume button: close the whole menu. The close button raises <see cref="OrnateWindowControl.CloseRequested"/>.</summary>
        public event EventHandler ResumeRequested;

        public int CategoryCount => _categories.Count;

        protected override IEnumerable<string> PreloadPaths => _categories.Select(c => "Icons/" + c.Icon);

        public void SelectCategory(int index)
        {
            _category = Math.Clamp(index, 0, _categories.Count - 1);
            _options = _categories[_category].Build();
            _scroll = 0;
            _dragSlider = null;
        }

        /// <summary>Called whenever the window is opened so lists such as resolutions reflect the current state.</summary>
        public void Reopen()
        {
            SelectCategory(_category);
            Clicks.Reset();
        }

        private int MaxScroll => Math.Max(0, _options.Count - VisibleRows);

        public override bool ProcessMouseScroll(int scrollDelta)
        {
            if (!Visible)
                return false;

            _scroll = Math.Clamp(_scroll + (scrollDelta > 0 ? -1 : 1), 0, MaxScroll);
            return true;
        }

        // --- Layout -------------------------------------------------------------------------------------

        private Rectangle ResumeBounds => At(ContentRight - 190, WindowHeight - 58, 190, 40);
        private Rectangle BackBounds => At(ContentRight - 190 - 18 - 150, WindowHeight - 58, 150, 40);
        private Rectangle TabBounds(int i) => At(TabX, TabY + i * TabStep, TabWidth, TabHeight);
        private Rectangle InfoBounds => At(ContentX, WindowHeight - 110, ContentRight - ContentX, 40);
        private int RowsWidth => ContentRight - ContentX - (MaxScroll > 0 ? 14 : 0);
        private Rectangle RowBounds(int visibleIndex) => At(ContentX, RowsTop + visibleIndex * RowHeight, RowsWidth, RowHeight - 2);
        private static Rectangle ControlBounds(Rectangle row) => new(row.Right - ControlWidth - 10, row.Y, ControlWidth, row.Height);

        // --- Input --------------------------------------------------------------------------------------

        public override void Update(GameTime gameTime)
        {
            if (_rebuildRequested && Visible)
            {
                _rebuildRequested = false;
                int scroll = _scroll;
                _options = _categories[_category].Build();
                _scroll = Math.Clamp(scroll, 0, MaxScroll);
            }

            base.Update(gameTime);
        }

        protected override int HitTest(Point p)
        {
            if (ResumeBounds.Contains(p)) return HitResume;
            if (BackBounds.Contains(p)) return HitBack;

            for (int i = 0; i < _categories.Count; i++)
                if (TabBounds(i).Contains(p))
                    return HitTab + i;

            for (int v = 0; v < VisibleRows && _scroll + v < _options.Count; v++)
            {
                int index = _scroll + v;
                Rectangle row = RowBounds(v);
                if (!row.Contains(p) || _options[index] is SettingHeader)
                    continue;

                int part = PartRow;
                if (_options[index] is SettingChoice)
                {
                    Rectangle box = OrnateWidgets.ChoiceBox(ControlBounds(row));
                    part = OrnateWidgets.ChoiceLeft(box).Contains(p) ? PartLeft
                        : OrnateWidgets.ChoiceRight(box).Contains(p) ? PartRight
                        : box.Contains(p) ? PartCenter
                        : PartRow;
                }
                else if (_options[index] is SettingSlider)
                {
                    Rectangle track = OrnateWidgets.SliderTrack(ControlBounds(row));
                    track.Inflate(10, 10);
                    part = track.Contains(p) ? PartCenter : PartRow;
                }

                return HitRow + index * 4 + part;
            }

            return HitNone;
        }

        private static bool IsRow(int hit) => hit >= HitRow;
        private static int RowIndex(int hit) => (hit - HitRow) / 4;
        private static int RowPart(int hit) => (hit - HitRow) % 4;

        protected override bool TryBeginDrag(int hit, in OrnatePointer pointer)
        {
            if (!IsRow(hit) || RowPart(hit) != PartCenter || _options[RowIndex(hit)] is not SettingSlider slider || !slider.Available)
                return false;

            _dragSlider = slider;
            _dragValue = slider.Get();
            DragTo(pointer.Position.X);
            return true;
        }

        protected override bool UpdateDrag(in OrnatePointer pointer)
        {
            if (_dragSlider == null)
                return false;

            DragTo(pointer.Position.X);
            if (!pointer.Down)
            {
                if (_dragSlider.ApplyOnRelease)
                    _dragSlider.Set(_dragValue);
                if (_dragSlider.Persist)
                    SettingsPersistence.Save();
                _dragSlider = null;
            }
            return true;
        }

        private void DragTo(int mouseX)
        {
            int index = _options.IndexOf(_dragSlider);
            if (index < _scroll || index >= _scroll + VisibleRows)
                return;

            Rectangle track = OrnateWidgets.SliderTrack(ControlBounds(RowBounds(index - _scroll)));
            float t = MathHelper.Clamp((mouseX - track.X) / (float)Math.Max(1, track.Width), 0f, 1f);
            float value = _dragSlider.Quantize(_dragSlider.Min + t * (_dragSlider.Max - _dragSlider.Min));
            if (MathF.Abs(value - _dragValue) < 0.0001f)
                return;

            _dragValue = value;
            if (!_dragSlider.ApplyOnRelease)
                _dragSlider.Set(value);
        }

        protected override void OnActivate(int hit)
        {
            if (hit == HitResume || hit == HitBack)
            {
                PlayClickSound();
                (hit == HitResume ? ResumeRequested : BackRequested)?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (hit >= HitTab && hit < HitRow)
            {
                if (hit - HitTab != _category)
                {
                    PlayClickSound();
                    SelectCategory(hit - HitTab);
                }
                return;
            }

            if (!IsRow(hit) || !_options[RowIndex(hit)].Available)
                return;

            switch (_options[RowIndex(hit)])
            {
                case SettingToggle toggle:
                    toggle.Set(!toggle.Get());
                    PlayClickSound();
                    if (toggle.Persist)
                        SettingsPersistence.Save();
                    break;
                case SettingChoice choice when choice.Choices.Count > 0:
                    int current = choice.GetIndex();
                    int count = choice.Choices.Count;
                    int next = RowPart(hit) switch
                    {
                        PartLeft => current < 0 ? 0 : Math.Max(0, current - 1),
                        PartRight => current < 0 ? 0 : Math.Min(count - 1, current + 1),
                        _ => (current + 1) % count
                    };
                    if (next != current)
                    {
                        choice.SetIndex(next);
                        PlayClickSound();
                        // A preset changes many other options: their new values are saved too.
                        SettingsPersistence.Save();
                    }
                    break;
            }
        }

        // --- Drawing ------------------------------------------------------------------------------------

        protected override void DrawContent(SpriteBatch sb, float a)
        {
            // Sidebar
            OrnateDraw.Inset(sb, At(18, 62, TabWidth + 24, WindowHeight - 62 - 22), a, 0.5f);
            for (int i = 0; i < _categories.Count; i++)
            {
                var state = new WidgetState(Hover: Clicks.IsHover(HitTab + i), Selected: i == _category);
                OrnateWidgets.Tab(sb, TabBounds(i), _categories[i].Name, _categories[i].Icon, state, a);
            }
            OrnateDraw.TextCentered(sb, "ESC  -  Back", At(18, WindowHeight - 60, TabWidth + 24, 30), 12f, OrnateStyle.TextMuted, a);

            // Header
            SettingsCategory category = _categories[_category];
            OrnateDraw.Text(sb, category.Name.ToUpperInvariant(), new Vector2(Frame.X + ContentX + 4, Frame.Y + 64), 22f, OrnateStyle.Gold, a, bold: true);
            OrnateDraw.Text(sb, category.Subtitle, new Vector2(Frame.X + ContentX + 4, Frame.Y + 97), 12.5f, OrnateStyle.TextMuted, a);
            OrnateDraw.Divider(sb, At(ContentX - 6, 116, ContentRight - ContentX + 12, 26), a);

            // Rows
            for (int v = 0; v < VisibleRows && _scroll + v < _options.Count; v++)
                DrawRow(sb, _scroll + v, RowBounds(v), a);

            OrnateWidgets.Scrollbar(sb, At(ContentRight - 6, RowsTop, 5, VisibleRows * RowHeight - 2), _scroll, VisibleRows, _options.Count, a);

            // Description of the hovered option
            Rectangle info = InfoBounds;
            OrnateDraw.Inset(sb, info, a, 0.4f);
            SettingOption hovered = HoveredOption();
            OrnateDraw.TextLeft(sb, hovered?.Description ?? "Hover over an option to see what it does.",
                new Rectangle(info.X + 14, info.Y, info.Width - 28, info.Height), 12.5f,
                hovered != null ? OrnateStyle.TextLight : OrnateStyle.TextMuted, a);

            OrnateWidgets.Button(sb, BackBounds, "Back", new WidgetState(Clicks.IsHover(HitBack), Clicks.IsPressed(HitBack)), a, overhang: 0.3f);
            OrnateWidgets.Button(sb, ResumeBounds, "Resume Game", new WidgetState(Clicks.IsHover(HitResume), Clicks.IsPressed(HitResume)), a, overhang: 0.3f);
        }

        private SettingOption HoveredOption()
        {
            if (_dragSlider != null)
                return _dragSlider;
            return IsRow(Clicks.Hover) && RowIndex(Clicks.Hover) < _options.Count ? _options[RowIndex(Clicks.Hover)] : null;
        }

        private void DrawRow(SpriteBatch sb, int index, Rectangle row, float a)
        {
            SettingOption option = _options[index];
            if (option is SettingHeader)
            {
                OrnateWidgets.SectionHeader(sb, row, option.Label, a);
                return;
            }

            bool available = option.Available;
            bool hovered = _dragSlider == option || available && IsRow(Clicks.Hover) && RowIndex(Clicks.Hover) == index;
            OrnateWidgets.RowBackground(sb, row, index, hovered, a);

            Color labelColor = !available ? OrnateStyle.TextDisabled : hovered ? Color.White : OrnateStyle.TextLight;
            OrnateDraw.TextLeft(sb, option.Label, new Rectangle(row.X + 16, row.Y, row.Width - ControlWidth - 30, row.Height), 14f, labelColor, a);

            float controlAlpha = (available ? 1f : 0.4f) * a;
            Rectangle control = ControlBounds(row);
            switch (option)
            {
                case SettingToggle toggle:
                    OrnateWidgets.Toggle(sb, control, toggle.Get(), controlAlpha);
                    break;
                case SettingSlider slider:
                    float value = _dragSlider == slider ? _dragValue : slider.Get();
                    OrnateWidgets.Slider(sb, control, slider.Fraction(value), slider.Format(value), controlAlpha);
                    break;
                case SettingChoice choice:
                    int current = choice.GetIndex();
                    string text = current >= 0 && current < choice.Choices.Count ? choice.Choices[current] : "Custom";
                    OrnateWidgets.Choice(sb, control, text,
                        canLeft: current != 0 && choice.Choices.Count > 0,
                        canRight: current < choice.Choices.Count - 1,
                        hoverLeft: Clicks.Hover == HitRow + index * 4 + PartLeft,
                        hoverRight: Clicks.Hover == HitRow + index * 4 + PartRight,
                        controlAlpha);
                    break;
            }
        }
    }
}
