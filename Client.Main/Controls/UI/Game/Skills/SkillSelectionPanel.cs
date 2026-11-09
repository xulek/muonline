#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Ornate skill picker shared by both HUDs: the character's skills in a grid of HUD slot frames and a detail panel
    /// (costs, range, cooldown, requirements with unmet ones in red, hotbar key). A click picks the skill; Esc or a
    /// click outside closes it.
    /// </summary>
    public class SkillSelectionPanel : UIControl, IUiTexturePreloadable
    {
        private const int Columns = 6;
        private const int Padding = 22;
        private const int HeaderHeight = 58;
        private const int FooterHeight = 30;
        private const int SlotGap = 8;
        private const int MaxSlotSize = 52, MinSlotSize = 40;
        private const int GridInset = 12;
        private const int DetailWidth = 300;
        private const int ContentGap = 14;
        private const int MinContentHeight = 300;
        private const float OpenDuration = 0.18f;
        private const int OpenOffsetY = 18;

        private static readonly Color ManaColor = new(130, 180, 255);
        private static readonly Color AgColor = new(196, 150, 245);
        private static readonly Color UnmetColor = new(240, 110, 100);

        private readonly List<SkillEntryState> _skills = new();
        private readonly List<Rectangle> _slotRects = new();
        private CharacterState? _state;
        private ushort? _selectedSkillId;
        private int _hovered = -1;
        private bool _closeHovered;
        private int _slotSize = MaxSlotSize;
        private Rectangle _gridLocal, _detailLocal, _footerLocal;
        private bool _opening;
        private float _openElapsed;

        /// <summary>Fired when a skill is picked.</summary>
        public event Action<SkillEntryState>? SkillSelected;

        /// <summary>Key label of the hotbar slot a skill is on (null when it is not), shown on its slot.</summary>
        public Func<ushort, string?>? HotbarKeyOf { get; set; }

        /// <summary>Key label of the slot being assigned, shown in the footer.</summary>
        public string? TargetSlotLabel { get; set; }

        public SkillSelectionPanel()
        {
            Interactive = true;
            AutoViewSize = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;
            Visible = false;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        /// <summary>Opens the panel with the character's skills.</summary>
        public void Open(CharacterState characterState)
        {
            if (characterState == null)
                return;

            _state = characterState;
            _skills.Clear();
            _skills.AddRange(characterState.GetSkills()
                .OrderBy(s => SkillDatabase.GetSkillName(s.SkillId))
                .ThenBy(s => s.SkillId));

            int rows = Math.Max(1, (int)Math.Ceiling(_skills.Count / (float)Columns));
            int maxGridHeight = Math.Max(200, UiScaler.VirtualSize.Y - 200 - HeaderHeight - FooterHeight);
            _slotSize = MaxSlotSize;
            while (_slotSize > MinSlotSize && rows * (_slotSize + SlotGap) - SlotGap + GridInset * 2 > maxGridHeight)
                _slotSize -= 2;

            int gridWidth = Columns * (_slotSize + SlotGap) - SlotGap + GridInset * 2;
            int gridHeight = rows * (_slotSize + SlotGap) - SlotGap + GridInset * 2;
            int contentHeight = Math.Max(MinContentHeight, gridHeight);
            int width = Padding * 2 + gridWidth + ContentGap + DetailWidth;
            int height = HeaderHeight + contentHeight + FooterHeight + Padding / 2;

            ControlSize = ViewSize = new Point(width, height);
            _gridLocal = new Rectangle(Padding, HeaderHeight, gridWidth, contentHeight);
            _detailLocal = new Rectangle(_gridLocal.Right + ContentGap, HeaderHeight, DetailWidth, contentHeight);
            _footerLocal = new Rectangle(Padding, HeaderHeight + contentHeight + 4, width - Padding * 2, FooterHeight - 4);

            _slotRects.Clear();
            for (int i = 0; i < _skills.Count; i++)
            {
                _slotRects.Add(new Rectangle(
                    _gridLocal.X + GridInset + i % Columns * (_slotSize + SlotGap),
                    _gridLocal.Y + GridInset + i / Columns * (_slotSize + SlotGap),
                    _slotSize, _slotSize));
            }

            _hovered = -1;
            _ = OrnateAssets.PreloadAsync(new[] { "Hud/slot", "Hud/slot_active" });
            Visible = true;
            BringToFront();
            _opening = true;
            _openElapsed = 0f;
            Alpha = 0f;
            Offset = new Point(0, OpenOffsetY);
        }

        /// <summary>Closes the panel.</summary>
        public void Close()
        {
            _opening = false;
            Alpha = 1f;
            Offset = Point.Zero;
            Visible = false;
            TargetSlotLabel = null;
        }

        public void HighlightSkill(ushort skillId) => _selectedSkillId = skillId;

        public IEnumerable<string> GetPreloadTexturePaths() => SkillIconAtlas.TexturePaths;

        private Rectangle At(Rectangle local) => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private Rectangle CloseRect => At(new Rectangle(ViewSize.X - 52, 14, 32, 32));

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible)
                return;

            if (_opening)
            {
                _openElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
                float t = MathHelper.Clamp(_openElapsed / OpenDuration, 0f, 1f);
                float eased = 1f - MathF.Pow(1f - t, 3f);
                Alpha = eased;
                Offset = new Point(0, (int)MathF.Round((1f - eased) * OpenOffsetY));
                _opening = t < 1f;
            }

            Point mouse = MuGame.Instance.UiMouseState.Position;
            Point hit = PointerForHitTest(mouse);
            _closeHovered = CloseRect.Contains(hit);
            _hovered = -1;
            for (int i = 0; i < _slotRects.Count; i++)
            {
                if (At(_slotRects[i]).Contains(hit))
                {
                    _hovered = i;
                    break;
                }
            }

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Close();
                Scene?.ConsumeKeyboardEscape();
                return;
            }

            bool clicked = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed &&
                           MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released;
            if (!clicked)
                return;

            if (_closeHovered || !DisplayRectangle.Contains(mouse))
            {
                Close();
                Scene?.SetMouseInputConsumed();
                return;
            }

            if (_hovered >= 0)
            {
                SkillEntryState skill = _skills[_hovered];
                _selectedSkillId = skill.SkillId;
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                SkillSelected?.Invoke(skill);
                Close();
                Scene?.SetMouseInputConsumed();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            SpriteBatch sb = GraphicsManager.Instance.Sprite;
            if (sb == null)
                return;

            float alpha = Alpha;
            Rectangle frame = DisplayRectangle;
            OrnateDraw.Window(sb, frame, alpha);

            // Grid of skills.
            Rectangle grid = At(_gridLocal);
            OrnateDraw.Inset(sb, grid, alpha, 0.5f);
            UiDrawHelper.DrawBorder(sb, new Rectangle(grid.X - 1, grid.Y - 1, grid.Width + 2, grid.Height + 2), OrnateItemUi.Rim * (0.9f * alpha));
            for (int i = 0; i < _skills.Count; i++)
                DrawSlot(sb, At(_slotRects[i]), _skills[i], i == _hovered, alpha);
            if (_skills.Count == 0)
                OrnateDraw.TextCentered(sb, "No skills learned yet", grid, 14f, OrnateStyle.TextMuted, alpha);

            DrawDetail(sb, At(_detailLocal), _hovered >= 0 ? _skills[_hovered] : null, alpha);
            DrawFooter(sb, At(_footerLocal), alpha);

            OrnateWidgets.CloseButton(sb, CloseRect, _closeHovered, alpha);
            OrnateDraw.TitlePlaque(sb, "Skills", frame.Center.X, frame.Y + 2, 38, alpha);
            string count = _skills.Count == 1 ? "1 skill" : $"{_skills.Count} skills";
            OrnateDraw.TextCentered(sb, count, new Rectangle(frame.X, frame.Y + 30, frame.Width, 20), 12f, OrnateStyle.TextMuted, alpha);
        }

        private void DrawSlot(SpriteBatch sb, Rectangle rect, SkillEntryState skill, bool hover, float alpha)
        {
            bool selected = _selectedSkillId == skill.SkillId;
            var inner = new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
            UiDrawHelper.DrawVerticalGradient(sb, inner, new Color(34, 29, 24) * alpha, new Color(12, 10, 9) * alpha, 12);
            DrawSkillIcon(sb, inner, skill.SkillId, alpha);
            if (hover)
                sb.Draw(GraphicsManager.Instance.Pixel, inner, new Color(255, 214, 128) * (0.12f * alpha));

            Texture2D? frameTexture = OrnateAssets.Get(selected || hover ? "Hud/slot_active" : "Hud/slot");
            if (frameTexture != null)
            {
                Color tint = selected || hover ? Color.White : new Color(205, 195, 180);
                sb.Draw(frameTexture, new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), tint * alpha);
            }
            else
            {
                UiDrawHelper.DrawBorder(sb, rect, (selected || hover ? OrnateStyle.Gold : OrnateStyle.Bronze) * alpha);
            }

            // Hotbar key badge, bottom-right.
            string? key = HotbarKeyOf?.Invoke(skill.SkillId);
            if (!string.IsNullOrEmpty(key))
            {
                Vector2 size = OrnateDraw.Measure(key, 11f);
                var badge = new Rectangle(rect.Right - (int)size.X - 9, rect.Bottom - (int)size.Y - 5, (int)size.X + 6, (int)size.Y + 1);
                sb.Draw(GraphicsManager.Instance.Pixel, badge, Color.Black * (0.75f * alpha));
                UiDrawHelper.DrawBorder(sb, badge, OrnateStyle.Bronze * alpha);
                OrnateDraw.TextCentered(sb, key, badge, 11f, OrnateStyle.Gold, alpha, bold: true);
            }
        }

        private static void DrawSkillIcon(SpriteBatch sb, Rectangle dest, ushort skillId, float alpha)
        {
            var definition = SkillDatabase.GetSkillDefinition(skillId);
            if (!SkillIconAtlas.TryResolve(skillId, definition, out var frame))
                return;

            Texture2D? texture = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            if (texture == null)
                return;

            const int pad = 3;
            float scale = MathF.Min((dest.Width - pad * 2) / (float)SkillIconAtlas.IconWidth, (dest.Height - pad * 2) / (float)SkillIconAtlas.IconHeight);
            int w = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconWidth * scale));
            int h = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconHeight * scale));
            sb.Draw(texture, new Rectangle(dest.Center.X - w / 2, dest.Center.Y - h / 2, w, h), frame.SourceRectangle, Color.White * alpha);
        }

        private void DrawDetail(SpriteBatch sb, Rectangle area, SkillEntryState? skill, float alpha)
        {
            OrnateDraw.Inset(sb, area, alpha, 0.45f);
            UiDrawHelper.DrawBorder(sb, new Rectangle(area.X - 1, area.Y - 1, area.Width + 2, area.Height + 2), OrnateItemUi.Rim * (0.9f * alpha));

            if (skill == null)
            {
                OrnateDraw.TextCentered(sb, "Hover a skill to see its details", area, 13f, OrnateStyle.TextMuted, alpha);
                return;
            }

            // Header: big icon in a gold frame, name and type.
            var icon = new Rectangle(area.X + 14, area.Y + 14, 58, 58);
            UiDrawHelper.DrawVerticalGradient(sb, icon, new Color(40, 34, 27) * alpha, new Color(12, 10, 9) * alpha, 12);
            DrawSkillIcon(sb, new Rectangle(icon.X + 3, icon.Y + 3, icon.Width - 6, icon.Height - 6), skill.SkillId, alpha);
            Texture2D? frame = OrnateAssets.Get("Hud/slot_active");
            if (frame != null)
                sb.Draw(frame, new Rectangle(icon.X - 3, icon.Y - 3, icon.Width + 6, icon.Height + 6), Color.White * alpha);

            var definition = SkillDatabase.GetSkillDefinition(skill.SkillId);
            string type = SkillDatabase.GetSkillType(skill.SkillId) switch
            {
                SkillType.Area => "Area skill",
                SkillType.Self => "Self skill",
                _ => "Target skill",
            };
            int textX = icon.Right + 12;
            int textWidth = area.Right - textX - 10;
            string name = Ellipsize(SkillDatabase.GetSkillName(skill.SkillId), 17f, textWidth);
            OrnateDraw.Text(sb, name, new Vector2(textX, icon.Y + 4), 17f, OrnateStyle.Gold, alpha, bold: true);
            string subtitle = skill.SkillLevel > 0 ? $"{type}  ·  Level {skill.SkillLevel}" : type;
            OrnateDraw.Text(sb, subtitle, new Vector2(textX, icon.Y + 30), 12f, OrnateStyle.TextMuted, alpha);
            string? key = HotbarKeyOf?.Invoke(skill.SkillId);
            if (!string.IsNullOrEmpty(key))
                OrnateDraw.Text(sb, $"On hotbar: {key}", new Vector2(textX, icon.Y + 46), 12f, OrnateStyle.GoldDim, alpha);

            OrnateDraw.Divider(sb, new Rectangle(area.X + 12, icon.Bottom + 8, area.Width - 24, 12), alpha);

            int y = icon.Bottom + 28;
            void Row(string label, string value, Color color)
            {
                var row = new Rectangle(area.X + 16, y, area.Width - 32, 20);
                OrnateDraw.TextLeft(sb, label, row, 13f, OrnateStyle.TextMuted, alpha);
                OrnateDraw.TextRight(sb, value, row, 13f, color, alpha, bold: true);
                y += 22;
            }

            if (definition == null)
            {
                Row("Details", "not available", OrnateStyle.TextMuted);
                return;
            }

            if (definition.ManaCost > 0)
                Row("Mana cost", definition.ManaCost.ToString(CultureInfo.InvariantCulture), ManaColor);
            if (definition.AbilityGaugeCost > 0)
                Row("AG cost", definition.AbilityGaugeCost.ToString(CultureInfo.InvariantCulture), AgColor);
            if (definition.Damage > 0)
                Row("Base damage", definition.Damage.ToString(CultureInfo.InvariantCulture), OrnateStyle.TextLight);
            if (definition.Distance > 0)
                Row("Range", definition.Distance.ToString(CultureInfo.InvariantCulture), OrnateStyle.TextLight);
            if (definition.Delay > 0)
                Row("Cooldown", (definition.Delay / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + " s", OrnateStyle.TextLight);

            bool anyRequirement = definition.RequiredLevel > 0 || definition.RequiredStrength > 0 || definition.RequiredDexterity > 0 ||
                                  definition.RequiredEnergy > 0 || definition.RequiredLeadership > 0;
            if (!anyRequirement)
                return;

            y += 10;
            OrnateWidgets.SectionHeader(sb, new Rectangle(area.X + 10, y, area.Width - 20, 24), "Requirements", alpha);
            y += 30;
            void Requirement(string label, int required, int have)
            {
                if (required > 0)
                    Row(label, required.ToString(CultureInfo.InvariantCulture), _state != null && have < required ? UnmetColor : OrnateStyle.TextLight);
            }

            Requirement("Level", definition.RequiredLevel, _state?.Level ?? 0);
            Requirement("Strength", definition.RequiredStrength, _state?.TotalStrength ?? 0);
            Requirement("Agility", definition.RequiredDexterity, _state?.TotalAgility ?? 0);
            Requirement("Energy", definition.RequiredEnergy, _state?.TotalEnergy ?? 0);
            Requirement("Command", definition.RequiredLeadership, _state?.TotalLeadership ?? 0);
        }

        private void DrawFooter(SpriteBatch sb, Rectangle area, float alpha)
        {
            string text = string.IsNullOrEmpty(TargetSlotLabel)
                ? "Click a skill to choose it   ·   Esc closes"
                : $"Click a skill to put it on slot {TargetSlotLabel}   ·   Esc closes";
            OrnateDraw.TextCentered(sb, text, area, 12f, OrnateStyle.TextMuted, alpha);
        }

        private static string Ellipsize(string text, float size, float maxWidth)
        {
            if (OrnateDraw.Measure(text, size).X <= maxWidth)
                return text;
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text[..length].TrimEnd() + "...";
                if (OrnateDraw.Measure(candidate, size).X <= maxWidth)
                    return candidate;
            }
            return "...";
        }
    }
}
