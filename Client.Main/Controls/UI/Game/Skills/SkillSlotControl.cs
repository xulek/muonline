#nullable enable
using System;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Single skill slot with modern framed rendering and contextual tooltip.
    /// </summary>
    public class SkillSlotControl : UIControl
    {
        private SkillEntryState? _skill;
        private Texture2D? _iconTexture;
        private string? _iconPath;
        private Rectangle _iconSource;
        private bool _isSelected;
        private bool _wasHovered;
        private string _tooltipText = string.Empty;
        private bool _showTooltip;

        public const int SLOT_WIDTH = 28;
        public const int SLOT_HEIGHT = 48;

        public SkillEntryState? Skill
        {
            get => _skill;
            set
            {
                _skill = value;
                UpdateDisplay();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                if (!_isSelected)
                {
                    _showTooltip = false;
                }
            }
        }

        public bool IsTooltipEnabled { get; set; } = true;
        public bool ShowFooter { get; set; } = true;

        public event Action<SkillEntryState?>? HoverChanged;

        public SkillSlotControl()
        {
            AutoViewSize = false;
            ControlSize = new Point(SLOT_WIDTH, SLOT_HEIGHT);
            ViewSize = ControlSize;
            Interactive = true;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;
        }

        public override async Task Load()
        {
            foreach (var texturePath in SkillIconAtlas.AllTexturePaths)
            {
                await TextureLoader.Instance.Prepare(texturePath);
            }

            await base.Load();
            RefreshSkillIconTexture();
        }

        private void UpdateDisplay()
        {
            _showTooltip = false;
            _tooltipText = string.Empty;
            RefreshSkillIconTexture();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (spriteBatch == null || pixel == null)
            {
                return;
            }

            Rectangle rect = DisplayRectangle;
            DrawSlotFrame(spriteBatch, pixel, rect, gameTime);
            DrawIconArea(spriteBatch, pixel, rect);
            DrawSkillIcon(spriteBatch, rect);
            if (ShowFooter)
            {
                DrawFooter(spriteBatch, pixel, rect);
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || !_showTooltip || string.IsNullOrWhiteSpace(_tooltipText))
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (spriteBatch == null || pixel == null || font == null)
            {
                return;
            }

            string[] lines = _tooltipText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return;
            }

            float scale = 0.56f;
            int padding = 7;
            int lineHeight = (int)MathF.Ceiling(font.LineSpacing * scale);
            int maxTextWidth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int width = (int)MathF.Ceiling(font.MeasureString(lines[i]).X * scale);
                if (width > maxTextWidth)
                {
                    maxTextWidth = width;
                }
            }

            int tooltipWidth = Math.Max(178, maxTextWidth + (padding * 2));
            int tooltipHeight = (padding * 2) + (lineHeight * lines.Length);

            Point mouse = MuGame.Instance.UiMouseState.Position;
            Point virtualSize = UiScaler.VirtualSize;

            int x = Math.Clamp(mouse.X + 14, 2, Math.Max(2, virtualSize.X - tooltipWidth - 2));
            int y = Math.Clamp(mouse.Y + 14, 2, Math.Max(2, virtualSize.Y - tooltipHeight - 2));

            var tooltipRect = new Rectangle(x, y, tooltipWidth, tooltipHeight);

            spriteBatch.Draw(pixel, tooltipRect, ModernHudTheme.BorderOuter);

            var inner = new Rectangle(
                tooltipRect.X + 1,
                tooltipRect.Y + 1,
                Math.Max(1, tooltipRect.Width - 2),
                Math.Max(1, tooltipRect.Height - 2));

            UiDrawHelper.DrawVerticalGradient(
                spriteBatch,
                inner,
                new Color(20, 24, 32, 252),
                new Color(11, 13, 18, 255));

            spriteBatch.Draw(
                pixel,
                new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.Accent * 0.45f);

            for (int i = 0; i < lines.Length; i++)
            {
                Color color = i switch
                {
                    0 => ModernHudTheme.TextGold,
                    1 => ModernHudTheme.TextGray,
                    _ => ModernHudTheme.TextWhite
                };

                float tx = inner.X + padding;
                float ty = inner.Y + padding + (i * lineHeight);

                DrawTextWithShadow(
                    spriteBatch,
                    font,
                    lines[i],
                    new Vector2(tx, ty),
                    color,
                    scale);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            bool isHovered = IsMouseOver;
            _showTooltip = IsTooltipEnabled && isHovered && _skill != null && !_isSelected;
            if (_showTooltip)
            {
                BuildTooltip();
            }

            if (_wasHovered != isHovered)
            {
                _wasHovered = isHovered;
                HoverChanged?.Invoke(isHovered ? _skill : null);
            }
        }

        private void DrawSlotFrame(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rect, GameTime gameTime)
        {
            bool hovered = IsMouseOver;
            float pulse = 0.6f + (0.4f * (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 8.0));

            if (_isSelected)
            {
                var glowRect = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                spriteBatch.Draw(pixel, glowRect, ModernHudTheme.AccentGlow * pulse * Alpha);
            }
            else if (hovered)
            {
                var glowRect = new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
                spriteBatch.Draw(pixel, glowRect, ModernHudTheme.Secondary * 0.2f * Alpha);
            }

            Color outerBorder = _isSelected
                ? ModernHudTheme.AccentDim
                : hovered ? ModernHudTheme.BorderInner : ModernHudTheme.BorderOuter;

            spriteBatch.Draw(pixel, rect, outerBorder * Alpha);

            Rectangle inner = new(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            Color topColor = _isSelected
                ? new Color(44, 36, 24, 248)
                : hovered ? ModernHudTheme.BgLight : ModernHudTheme.BgMid;
            Color bottomColor = _isSelected
                ? new Color(18, 14, 10, 255)
                : ModernHudTheme.BgDarkest;

            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner, topColor * Alpha, bottomColor * Alpha);

            spriteBatch.Draw(
                pixel,
                new Rectangle(inner.X + 1, inner.Y, Math.Max(1, inner.Width - 2), 1),
                (_isSelected ? ModernHudTheme.Accent : ModernHudTheme.BorderHighlight) * 0.35f * Alpha);

            if (_isSelected)
            {
                UiDrawHelper.DrawCornerAccents(spriteBatch, rect, ModernHudTheme.Accent * 0.55f * Alpha, size: 4, thickness: 1);
            }
        }

        private void DrawIconArea(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rect)
        {
            Rectangle iconArea = GetIconAreaRect(rect, ShowFooter);

            UiDrawHelper.DrawVerticalGradient(
                spriteBatch,
                iconArea,
                ModernHudTheme.SlotBg * Alpha,
                ModernHudTheme.BgDarkest * Alpha);

            spriteBatch.Draw(pixel, new Rectangle(iconArea.X, iconArea.Y, iconArea.Width, 1), ModernHudTheme.BorderInner * 0.25f * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(iconArea.X, iconArea.Bottom - 1, iconArea.Width, 1), ModernHudTheme.BorderOuter * 0.95f * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(iconArea.X, iconArea.Y, 1, iconArea.Height), ModernHudTheme.BorderOuter * 0.95f * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(iconArea.Right - 1, iconArea.Y, 1, iconArea.Height), ModernHudTheme.BorderInner * 0.25f * Alpha);
        }

        private void DrawSkillIcon(SpriteBatch spriteBatch, Rectangle rect)
        {
            Rectangle iconArea = GetIconAreaRect(rect, ShowFooter);
            var font = GraphicsManager.Instance.Font;
            if (_skill == null)
            {
                if (font != null && ShowFooter)
                {
                    float baseScale = rect.Height / (float)SLOT_HEIGHT;
                    float textScale = 0.34f * baseScale;
                    DrawCenteredText(
                        spriteBatch,
                        font,
                        "EMPTY",
                        iconArea,
                        ModernHudTheme.TextDark * Alpha,
                        textScale);
                }
                return;
            }

            if (_iconTexture == null || _iconTexture.IsDisposed)
            {
                RefreshSkillIconTexture();
            }

            if (_iconTexture == null || _iconTexture.IsDisposed)
            {
                return;
            }

            float fitScale = MathF.Min(
                iconArea.Width / (float)SkillIconAtlas.IconWidth,
                iconArea.Height / (float)SkillIconAtlas.IconHeight);

            int drawW = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconWidth * fitScale));
            int drawH = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconHeight * fitScale));

            var iconRect = new Rectangle(
                iconArea.X + (iconArea.Width - drawW) / 2,
                iconArea.Y + (iconArea.Height - drawH) / 2,
                drawW,
                drawH);

            bool unusable = !SkillManager.MeetsDemandConditions(_skill.SkillId);
            Texture2D iconTexture = _iconTexture;
            Color iconTint = Color.White;
            if (unusable && _iconPath != null)
                iconTexture = SkillIconRenderer.GetIconTexture(_iconPath, true, out iconTint) ?? _iconTexture;
            spriteBatch.Draw(iconTexture, iconRect, _iconSource, iconTint * Alpha);

            // Cooldown overlay (dark fill from top to bottom proportional to remaining time)
            DrawCooldownOverlay(spriteBatch, iconRect);

            // Cooldown timer text (centered, large)
            DrawCooldownTimer(spriteBatch, font, iconRect);
        }

        private void DrawFooter(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rect)
        {
            Rectangle footer = GetFooterRect(rect);
            UiDrawHelper.DrawHorizontalGradient(
                spriteBatch,
                footer,
                (_isSelected ? ModernHudTheme.AccentDim : ModernHudTheme.BgDark) * Alpha,
                ModernHudTheme.BgDarkest * Alpha);

            spriteBatch.Draw(pixel, new Rectangle(footer.X, footer.Y, footer.Width, 1), ModernHudTheme.BorderInner * 0.2f * Alpha);

            var font = GraphicsManager.Instance.Font;
            if (font == null)
            {
                return;
            }

            float baseScale = rect.Height / (float)SLOT_HEIGHT;
            float smallScale = 0.33f * baseScale;

            if (_skill != null)
            {
                string idText = $"#{_skill.SkillId}";
                DrawTextWithShadow(
                    spriteBatch,
                    font,
                    idText,
                    new Vector2(footer.X + 2, footer.Y + 1),
                    ModernHudTheme.TextGray * Alpha,
                    smallScale);

                string typeGlyph = GetSkillTypeGlyph(_skill.SkillId);
                Vector2 typeSize = font.MeasureString(typeGlyph) * smallScale;
                DrawTextWithShadow(
                    spriteBatch,
                    font,
                    typeGlyph,
                    new Vector2(footer.Right - typeSize.X - 2, footer.Y + 1),
                    (_isSelected ? ModernHudTheme.AccentBright : ModernHudTheme.SecondaryBright) * Alpha,
                    smallScale);

                if (_skill.SkillLevel > 0)
                {
                    DrawLevelBadge(spriteBatch, pixel, font, rect, _skill.SkillLevel, baseScale);
                }
            }
        }

        private void DrawLevelBadge(SpriteBatch spriteBatch, Texture2D pixel, SpriteFont font, Rectangle rect, byte level, float baseScale)
        {
            string text = $"L{level}";
            float textScale = 0.33f * baseScale;

            int badgeWidth = Math.Max(12, (int)MathF.Ceiling(font.MeasureString(text).X * textScale) + 5);
            int badgeHeight = Math.Max(8, (int)MathF.Ceiling(font.LineSpacing * textScale) + 2);

            var badgeRect = new Rectangle(
                rect.Right - badgeWidth - 2,
                rect.Bottom - badgeHeight - 2,
                badgeWidth,
                badgeHeight);

            spriteBatch.Draw(pixel, badgeRect, ModernHudTheme.BorderOuter * Alpha);
            var inner = new Rectangle(
                badgeRect.X + 1,
                badgeRect.Y + 1,
                Math.Max(1, badgeRect.Width - 2),
                Math.Max(1, badgeRect.Height - 2));

            UiDrawHelper.DrawVerticalGradient(
                spriteBatch,
                inner,
                (_isSelected ? ModernHudTheme.Accent : ModernHudTheme.Secondary) * 0.85f * Alpha,
                (_isSelected ? ModernHudTheme.AccentDim : ModernHudTheme.SecondaryDim) * Alpha);

            DrawCenteredText(spriteBatch, font, text, inner, ModernHudTheme.TextWhite * Alpha, textScale);
        }

        private static Rectangle GetIconAreaRect(Rectangle rect, bool showFooter)
        {
            int xPad = Math.Max(2, rect.Width / 10);
            int yPad = Math.Max(2, rect.Height / 16);
            int footerHeight = showFooter ? Math.Max(8, rect.Height / 5) : 0;

            return new Rectangle(
                rect.X + xPad,
                rect.Y + yPad,
                Math.Max(1, rect.Width - (xPad * 2)),
                Math.Max(1, rect.Height - yPad - footerHeight - (showFooter ? 1 : yPad)));
        }

        private static Rectangle GetFooterRect(Rectangle rect)
        {
            int xPad = Math.Max(2, rect.Width / 10);
            int footerHeight = Math.Max(8, rect.Height / 5);
            return new Rectangle(
                rect.X + xPad,
                rect.Bottom - footerHeight - 1,
                Math.Max(1, rect.Width - (xPad * 2)),
                footerHeight);
        }

        private static void DrawCenteredText(SpriteBatch spriteBatch, SpriteFont font, string text, Rectangle rect, Color color, float scale)
        {
            Vector2 size = font.MeasureString(text) * scale;
            float x = rect.X + (rect.Width - size.X) * 0.5f;
            float y = rect.Y + (rect.Height - size.Y) * 0.5f;
            DrawTextWithShadow(spriteBatch, font, text, new Vector2(x, y), color, scale);
        }

        private static void DrawTextWithShadow(SpriteBatch spriteBatch, SpriteFont font, string text, Vector2 position, Color color, float scale)
        {
            spriteBatch.DrawString(font, text, position + new Vector2(1f, 1f), Color.Black * 0.75f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private string GetSkillTypeGlyph(ushort skillId)
        {
            return SkillDatabase.GetSkillType(skillId) switch
            {
                SkillType.Area => "A",
                SkillType.Self => "S",
                _ => "T"
            };
        }

        private void BuildTooltip()
        {
            if (_skill == null)
            {
                _tooltipText = string.Empty;
                return;
            }

            var skillDef = SkillDatabase.GetSkillDefinition(_skill.SkillId);
            string skillName = SkillDatabase.GetSkillName(_skill.SkillId);
            ushort manaCost = SkillDatabase.GetSkillManaCost(_skill.SkillId);
            ushort agCost = SkillDatabase.GetSkillAGCost(_skill.SkillId);
            uint range = SkillDatabase.GetSkillRange(_skill.SkillId);
            var skillType = SkillDatabase.GetSkillType(_skill.SkillId);

            string typeText = skillType switch
            {
                SkillType.Area => "Area",
                SkillType.Target => "Target",
                SkillType.Self => "Self",
                _ => "Unknown"
            };

            var tooltip = $"{skillName}\nType: {typeText}  •  Lv {_skill.SkillLevel}";

            if (manaCost > 0 || agCost > 0)
            {
                tooltip += $"\nMana: {manaCost}";
                if (agCost > 0)
                    tooltip += $"   AG: {agCost}";
            }

            if (skillDef != null)
            {
                if (skillDef.Damage > 0)
                    tooltip += $"\nDamage: {skillDef.Damage}";

                if (range > 0)
                    tooltip += $"\nRange: {range}";

                if (skillDef.RequiredLevel > 0)
                    tooltip += $"\nRequired Lv: {skillDef.RequiredLevel}";

                // Stat requirements (SourceMain: DemendConditionCheckSkill display)
                bool hasStats = skillDef.RequiredStrength > 0 || skillDef.RequiredDexterity > 0
                             || skillDef.RequiredEnergy > 0 || skillDef.RequiredLeadership > 0;
                if (hasStats)
                {
                    tooltip += "\n---";
                    if (skillDef.RequiredStrength > 0)
                        tooltip += $"\nStr: {skillDef.RequiredStrength}";
                    if (skillDef.RequiredDexterity > 0)
                        tooltip += $"\nDex: {skillDef.RequiredDexterity}";
                    if (skillDef.RequiredEnergy > 0)
                        tooltip += $"\nEne: {skillDef.RequiredEnergy}"; // checked as is (SkillManager.GetUnmetDemand)
                    if (skillDef.RequiredLeadership > 0)
                        tooltip += $"\nCmd: {skillDef.RequiredLeadership}";
                }

                if (skillDef.Delay > 0)
                    tooltip += $"\nCooldown: {skillDef.Delay / 1000f:F1}s";

                if (skillDef.MasteryType > 0)
                    tooltip += $"\nMastery: Tier {skillDef.MasteryType}";
            }

            _tooltipText = tooltip;
        }

        private void RefreshSkillIconTexture()
        {
            _iconTexture = null;

            if (_skill == null)
            {
                return;
            }

            var definition = SkillDatabase.GetSkillDefinition(_skill.SkillId);
            if (!SkillIconAtlas.TryResolve(_skill.SkillId, definition, out var frame))
            {
                return;
            }

            _iconTexture = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            _iconPath = frame.TexturePath;
            _iconSource = frame.SourceRectangle;
        }

        /// <summary>
        /// Draws a dark semi-transparent overlay over the icon proportional to remaining cooldown.
        /// The overlay covers from the top of the icon down to a position proportional to cooldown ratio.
        /// </summary>
        private void DrawCooldownOverlay(SpriteBatch spriteBatch, Rectangle iconRect)
        {
            if (_skill == null)
                return;

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var gameTime = MuGame.Instance?.GameTime;
            double now = gameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            float ratio = Core.Client.SkillCooldownTracker.GetCooldownRatio(_skill.SkillId, now);
            if (ratio <= 0f)
                return;

            int overlayHeight = Math.Max(1, (int)(iconRect.Height * ratio));

            var overlayRect = new Rectangle(
                iconRect.X,
                iconRect.Y,
                iconRect.Width,
                overlayHeight);

            // Dark overlay
            spriteBatch.Draw(pixel, overlayRect, new Color(0, 0, 0, 160) * Alpha);

            // Bright edge at the bottom of the overlay (the cooldown "sweep" line)
            spriteBatch.Draw(
                pixel,
                new Rectangle(overlayRect.X, overlayRect.Y + overlayHeight - 1, overlayRect.Width, 1),
                ModernHudTheme.Accent * 0.5f * Alpha);
        }

        /// <summary>
        /// Draws remaining cooldown time in seconds over the center of the icon.
        /// </summary>
        private void DrawCooldownTimer(SpriteBatch spriteBatch, SpriteFont? font, Rectangle iconRect)
        {
            if (_skill == null || font == null)
                return;

            var gameTime = MuGame.Instance?.GameTime;
            double now = gameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            int remainingMs = Core.Client.SkillCooldownTracker.GetRemainingMs(_skill.SkillId, now);
            if (remainingMs <= 0)
                return;

            string timerText = remainingMs >= 1000
                ? $"{(remainingMs + 999) / 1000}"   // whole seconds
                : $"{(remainingMs + 99) / 100f:F1}"; // one decimal

            float textScale = 0.6f;
            Vector2 textSize = font.MeasureString(timerText) * textScale;
            float tx = iconRect.X + (iconRect.Width - textSize.X) * 0.5f;
            float ty = iconRect.Y + (iconRect.Height - textSize.Y) * 0.5f;

            spriteBatch.DrawString(font, timerText, new Vector2(tx + 1f, ty + 1f),
                Color.Black * 0.85f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(font, timerText, new Vector2(tx, ty),
                ModernHudTheme.TextWhite, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }
}
