#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Content;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.Trade;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Renders equipment durability warnings in a compact, click-through overlay.
    /// </summary>
    public sealed class EquipmentDurabilityHud : UIControl
    {
        private const int EquipmentSlotCount = 12;
        private const byte HelperSlot = 8;
        private const int BaseTopOffset = 4;
        private const int BaseIconSize = 40;
        private const int BaseIconGap = 3;
        private const int IconPreviewCacheSize = 96;

        private readonly CharacterState _characterState;
        private readonly List<DurabilityWarningEntry> _entries = new();
        private readonly Dictionary<string, Texture2D> _iconTextureCache = new(StringComparer.OrdinalIgnoreCase);

        private Point _lastVirtualSize = Point.Zero;
        private SpriteFont? _font;
        private bool _isDirty = true;

        private int _iconSize = BaseIconSize;
        private int _iconGap = BaseIconGap;
        private int _startY;
        private float _uiScale = 1f;
        private double _timeSeconds;

        private DurabilityWarningEntry? _hoveredEntry;

        private enum WarningSeverity
        {
            Warning,
            Serious,
            Critical
        }

        private sealed class DurabilityWarningEntry
        {
            public byte Slot { get; init; }
            public ItemDefinition Definition { get; init; } = null!;
            public int Durability { get; init; }
            public int MaxDurability { get; init; }
            public WarningSeverity Severity { get; init; }
            public Rectangle Rect { get; set; }
        }

        public EquipmentDurabilityHud(CharacterState characterState)
        {
            _characterState = characterState;

            AutoViewSize = false;
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            _characterState.InventoryChanged += OnInventoryStateChanged;
            _characterState.EquipmentChanged += OnInventoryStateChanged;

            RefreshLayout();
        }

        public override void Dispose()
        {
            _characterState.InventoryChanged -= OnInventoryStateChanged;
            _characterState.EquipmentChanged -= OnInventoryStateChanged;
            base.Dispose();
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _lastVirtualSize = Point.Zero;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
            {
                return;
            }

            _timeSeconds = gameTime.TotalGameTime.TotalSeconds;

            RefreshLayout();

            if (_isDirty)
            {
                if (!IsTradeVisible())
                {
                    RebuildWarnings();
                }
            }
            else
            {
                LayoutWarningRects();
            }

            EnsureWarningIconsCached();
            UpdateHoveredEntry();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || _entries.Count == 0 || IsTradeVisible())
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (spriteBatch == null || pixel == null)
            {
                return;
            }

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                _font ??= GraphicsManager.Instance.Font;

                DrawPanelBackground(spriteBatch, pixel);

                for (int i = 0; i < _entries.Count; i++)
                {
                    DrawWarningIcon(spriteBatch, pixel, _entries[i]);
                }

                DrawTooltip(spriteBatch, pixel);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void OnInventoryStateChanged()
        {
            MuGame.ScheduleOnMainThread(() => _isDirty = true);
        }

        private void RefreshLayout()
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (virtualSize == _lastVirtualSize)
            {
                return;
            }

            _lastVirtualSize = virtualSize;

            float scaleX = virtualSize.X / 1024f;
            float scaleY = virtualSize.Y / 768f;
            _uiScale = Math.Clamp(MathF.Min(scaleX, scaleY), 0.82f, 1.28f);

            _iconSize = ScaleValue(BaseIconSize, _uiScale);
            _iconGap = Math.Max(1, ScaleValue(BaseIconGap, _uiScale));
            _startY = ScaleValue(BaseTopOffset, _uiScale);

            X = 0;
            Y = 0;
            // The control renders in absolute UI coordinates; it does not need a large hit area.
            ControlSize = new Point(1, 1);
            ViewSize = ControlSize;
        }

        private void RebuildWarnings()
        {
            _entries.Clear();

            if (IsTradeVisible())
            {
                return;
            }

            _isDirty = false;

            var items = _characterState.GetInventoryItems();
            for (byte slot = 0; slot < EquipmentSlotCount; slot++)
            {
                if (slot == HelperSlot || !items.TryGetValue(slot, out byte[]? itemData) || itemData == null || itemData.Length == 0)
                {
                    continue;
                }

                if (TryCreateEntry(slot, itemData, out var entry))
                {
                    _entries.Add(entry);
                }
            }

            LayoutWarningRects();
        }

        private bool TryCreateEntry(byte slot, byte[] itemData, out DurabilityWarningEntry entry)
        {
            entry = null!;

            var definition = ItemDatabase.GetItemDefinition(itemData);
            if (definition == null)
            {
                return false;
            }

            var details = ItemDatabase.ParseItemDetails(itemData);
            if (IsArrowOrBolt(definition) || IsIgnoredWizardRing(definition, details.Level))
            {
                return false;
            }

            bool isStaff = definition.Group == 5;
            int maxDurability = ItemUiHelper.CalculateMaxDurability(definition, details, isStaff);
            if (maxDurability <= 0)
            {
                return false;
            }

            int durability = ItemDatabase.GetItemDurability(itemData);
            if (durability > maxDurability * 0.5f)
            {
                return false;
            }

            entry = new DurabilityWarningEntry
            {
                Slot = slot,
                Definition = definition,
                Durability = durability,
                MaxDurability = maxDurability,
                Severity = ResolveSeverity(durability, maxDurability)
            };

            return true;
        }

        private void LayoutWarningRects()
        {
            int totalWidth = (_entries.Count * _iconSize) + Math.Max(0, _entries.Count - 1) * _iconGap;
            int left = Math.Max(ScaleValue(8, _uiScale), (UiScaler.VirtualSize.X - totalWidth) / 2);

            for (int i = 0; i < _entries.Count; i++)
            {
                int x = left + i * (_iconSize + _iconGap);
                int y = _startY;
                _entries[i].Rect = new Rectangle(x, y, _iconSize, _iconSize);
            }
        }

        private void EnsureWarningIconsCached()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var def = _entries[i].Definition;
                if (string.IsNullOrWhiteSpace(def.TexturePath) ||
                    !def.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (BmdPreviewRenderer.TryGetCachedPreview(def, IconPreviewCacheSize, IconPreviewCacheSize) == null)
                {
                    BmdPreviewRenderer.GetPreview(def, IconPreviewCacheSize, IconPreviewCacheSize);
                }
            }
        }

        private void UpdateHoveredEntry()
        {
            _hoveredEntry = null;

            var mouse = MuGame.Instance.UiMouseState.Position;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Rect.Contains(mouse))
                {
                    _hoveredEntry = _entries[i];
                    return;
                }
            }
        }

        private void DrawPanelBackground(SpriteBatch spriteBatch, Texture2D pixel)
        {
            int pad = Math.Max(2, ScaleValue(2, _uiScale));
            for (int i = 0; i < _entries.Count; i++)
            {
                Rectangle rect = _entries[i].Rect;
                if (rect == Rectangle.Empty)
                {
                    continue;
                }

                var panel = new Rectangle(
                    rect.X - pad,
                    rect.Y - pad,
                    rect.Width + (pad * 2),
                    rect.Height + (pad * 2));

                spriteBatch.Draw(pixel, panel, ModernHudTheme.BorderOuter * Alpha);

                var inner = new Rectangle(
                    panel.X + 1,
                    panel.Y + 1,
                    Math.Max(1, panel.Width - 2),
                    Math.Max(1, panel.Height - 2));
                UiDrawHelper.DrawVerticalGradient(spriteBatch, inner,
                    ModernHudTheme.BgDark * Alpha,
                    ModernHudTheme.BgDarkest * Alpha);

                spriteBatch.Draw(pixel,
                    new Rectangle(inner.X + 1, inner.Y, Math.Max(1, inner.Width - 2), 1),
                    ModernHudTheme.Accent * 0.45f * Alpha);
            }
        }

        private void DrawWarningIcon(SpriteBatch spriteBatch, Texture2D pixel, DurabilityWarningEntry entry)
        {
            Rectangle rect = entry.Rect;
            if (rect == Rectangle.Empty)
            {
                return;
            }

            Color severityColor = GetSeverityColor(entry.Severity);
            float pulse = entry.Severity == WarningSeverity.Critical
                ? 0.72f + (0.28f * (float)Math.Sin(_timeSeconds * 8.0))
                : 1f;

            spriteBatch.Draw(pixel, rect, severityColor * 0.78f * Alpha);

            var inner = new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner,
                ModernHudTheme.SlotBg * Alpha,
                ModernHudTheme.BgDarkest * Alpha);

            int pad = Math.Max(2, ScaleValue(3, _uiScale));
            var iconRect = new Rectangle(inner.X + pad, inner.Y + pad, Math.Max(1, inner.Width - (pad * 2)), Math.Max(1, inner.Height - (pad * 2)));
            Texture2D? iconTexture = ResolveItemIcon(entry.Definition);
            if (iconTexture != null)
            {
                spriteBatch.Draw(iconTexture, iconRect, Color.White * Alpha);
            }
            else
            {
                spriteBatch.Draw(pixel, iconRect, ModernHudTheme.SecondaryDim * 0.35f * Alpha);
            }

            Color overlayColor = severityColor * (entry.Severity == WarningSeverity.Warning ? 0.24f : 0.34f) * pulse * Alpha;
            spriteBatch.Draw(pixel, iconRect, overlayColor);

            spriteBatch.Draw(pixel,
                new Rectangle(inner.X, inner.Bottom - 2, inner.Width, 1),
                severityColor * 0.85f * Alpha);

            if (_font != null)
            {
                string durabilityText = entry.Durability.ToString();
                float scale = Math.Clamp(0.34f * _uiScale, 0.30f, 0.44f);
                Vector2 textSize = _font.MeasureString(durabilityText) * scale;

                int badgeWidth = (int)MathF.Ceiling(textSize.X) + 4;
                int badgeHeight = (int)MathF.Ceiling(textSize.Y) + 1;
                var badge = new Rectangle(inner.Right - badgeWidth - 1, inner.Bottom - badgeHeight - 1, badgeWidth, badgeHeight);
                spriteBatch.Draw(pixel, badge, Color.Black * 0.66f * Alpha);

                Vector2 textPos = new(badge.X + 2, badge.Y);
                spriteBatch.DrawString(_font, durabilityText, textPos + Vector2.One, Color.Black * 0.8f * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, durabilityText, textPos, ModernHudTheme.TextWhite * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private void DrawTooltip(SpriteBatch spriteBatch, Texture2D pixel)
        {
            if (_hoveredEntry == null || _font == null)
            {
                return;
            }

            string name = _hoveredEntry.Definition.Name ?? $"Item {_hoveredEntry.Definition.Group}/{_hoveredEntry.Definition.Id}";
            string durabilityLine = $"Durability: {_hoveredEntry.Durability}/{_hoveredEntry.MaxDurability}";
            string stateLine = _hoveredEntry.Severity switch
            {
                WarningSeverity.Warning => "State: Worn",
                WarningSeverity.Serious => "State: Damaged",
                _ => "State: Critical"
            };

            string[] lines = { name, durabilityLine, stateLine };
            float scale = Math.Clamp(0.50f * _uiScale, 0.45f, 0.60f);
            int pad = Math.Max(5, ScaleValue(7, _uiScale));
            int lineHeight = (int)MathF.Ceiling(_font.LineSpacing * scale);

            int maxTextWidth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int width = (int)MathF.Ceiling(_font.MeasureString(lines[i]).X * scale);
                if (width > maxTextWidth)
                {
                    maxTextWidth = width;
                }
            }

            int tooltipWidth = maxTextWidth + (pad * 2);
            int tooltipHeight = (lineHeight * lines.Length) + (pad * 2);
            Point virtualSize = UiScaler.VirtualSize;
            Point mouse = MuGame.Instance.UiMouseState.Position;

            int x = Math.Clamp(mouse.X + ScaleValue(12, _uiScale), 2, Math.Max(2, virtualSize.X - tooltipWidth - 2));
            int y = Math.Clamp(mouse.Y + ScaleValue(12, _uiScale), 2, Math.Max(2, virtualSize.Y - tooltipHeight - 2));
            var tooltipRect = new Rectangle(x, y, tooltipWidth, tooltipHeight);

            spriteBatch.Draw(pixel, tooltipRect, ModernHudTheme.BorderOuter * Alpha);

            var inner = new Rectangle(
                tooltipRect.X + 1,
                tooltipRect.Y + 1,
                Math.Max(1, tooltipRect.Width - 2),
                Math.Max(1, tooltipRect.Height - 2));

            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner,
                new Color(20, 24, 32, 252) * Alpha,
                new Color(11, 13, 18, 255) * Alpha);

            spriteBatch.Draw(pixel,
                new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                GetSeverityColor(_hoveredEntry.Severity) * 0.75f * Alpha);

            Color[] lineColors =
            {
                ModernHudTheme.TextGold,
                ModernHudTheme.TextWhite,
                GetSeverityColor(_hoveredEntry.Severity)
            };

            for (int i = 0; i < lines.Length; i++)
            {
                Vector2 textPos = new(inner.X + pad, inner.Y + pad + (i * lineHeight));
                spriteBatch.DrawString(_font, lines[i], textPos + Vector2.One, Color.Black * 0.75f * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, lines[i], textPos, lineColors[i] * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private Texture2D? ResolveItemIcon(ItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.TexturePath))
            {
                return null;
            }

            if (definition.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
            {
                return BmdPreviewRenderer.TryGetCachedPreview(definition, IconPreviewCacheSize, IconPreviewCacheSize);
            }

            if (_iconTextureCache.TryGetValue(definition.TexturePath, out var cached))
            {
                return cached;
            }

            var texture = TextureLoader.Instance.GetTexture2D(definition.TexturePath);
            if (texture != null)
            {
                _iconTextureCache[definition.TexturePath] = texture;
            }

            return texture;
        }

        private static WarningSeverity ResolveSeverity(int durability, int maxDurability)
        {
            if (durability <= 0 || durability <= maxDurability * 0.2f)
            {
                return WarningSeverity.Critical;
            }

            if (durability <= maxDurability * 0.3f)
            {
                return WarningSeverity.Serious;
            }

            return WarningSeverity.Warning;
        }

        private static Color GetSeverityColor(WarningSeverity severity)
        {
            return severity switch
            {
                WarningSeverity.Warning => ModernHudTheme.Warning,
                WarningSeverity.Serious => new Color(255, 150, 60),
                _ => ModernHudTheme.Danger
            };
        }

        private static bool IsArrowOrBolt(ItemDefinition definition)
        {
            return definition.Group == 4 && (definition.Id == 7 || definition.Id == 15);
        }

        private static bool IsIgnoredWizardRing(ItemDefinition definition, int level)
        {
            return definition.Group == 13 && definition.Id == 20 && (level == 1 || level == 2);
        }

        private static int ScaleValue(int value, float scale)
        {
            return Math.Max(1, (int)MathF.Round(value * scale));
        }

        private static bool IsTradeVisible()
        {
            return TradeControl.Instance?.Visible == true;
        }
    }
}
