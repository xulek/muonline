#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Hud
{
    public sealed class ModernBottomHud : UIControl
    {
        // ──────────────── Bar-specific colors ────────────────
        private static readonly Color HpColor = new(200, 45, 45);
        private static readonly Color HpColorBright = new(255, 80, 80);
        private static readonly Color HpColorDark = new(100, 18, 18);
        private static readonly Color HpGlow = new(255, 60, 60, 50);

        private static readonly Color MpColor = new(55, 120, 210);
        private static readonly Color MpColorBright = new(100, 170, 255);
        private static readonly Color MpColorDark = new(25, 55, 110);
        private static readonly Color MpGlow = new(80, 150, 255, 50);

        private static readonly Color SdColor = new(210, 185, 50);
        private static readonly Color SdColorBright = new(255, 230, 90);
        private static readonly Color SdColorDark = new(110, 90, 20);
        private static readonly Color SdGlow = new(255, 220, 60, 45);

        private static readonly Color AgColor = new(150, 70, 200);
        private static readonly Color AgColorBright = new(200, 120, 255);
        private static readonly Color AgColorDark = new(70, 30, 100);
        private static readonly Color AgGlow = new(180, 100, 255, 45);

        private static readonly Color ExpColor = new(212, 175, 85);
        private static readonly Color ExpColorBright = new(255, 220, 130);
        private static readonly Color ExpColorDark = new(110, 88, 35);
        private static readonly Color ExpGlow = new(255, 210, 100, 35);

        // ──────────────── State ────────────────
        private readonly CharacterState _state;
        private readonly SkillSelectionPanel _skillPanel;

        private SpriteFont? _font;
        private Point _lastVirtualSize = Point.Zero;
        private double _totalTime;

        // Resource bar display values (lerped for animation)
        private float _displayHpPct, _displayMpPct, _displaySdPct, _displayAgPct;
        private float _targetHpPct, _targetMpPct, _targetSdPct, _targetAgPct;
        private const float LerpSpeed = 6f;

        // Layout rects (recomputed on resize)
        private Rectangle _panelRect;
        private Rectangle _hpBarRect, _sdBarRect, _mpBarRect, _agBarRect;
        private Rectangle _expBarRect;
        private Rectangle[] _slotRects = Array.Empty<Rectangle>();
        private Rectangle[] _btnRects = Array.Empty<Rectangle>();
        private float _barFontScale;
        private float _slotFontScale;
        private float _btnFontScale;
        private float _expFontScale;

        // Skill slots: 0-2 = potion (Q/W/E), 3-12 = skills (1-0)
        private const int SlotCount = 13;
        private const int PotionSlotCount = 3;
        private readonly SkillEntryState?[] _slotSkills = new SkillEntryState?[SlotCount];
        private int _activeSkillSlot = 3;
        private int _pendingAssignSlot = -1;

        // Potion slot assignments (Q=0, W=1, E=2) — stores item type
        private readonly (byte Group, int Id)?[] _potionAssignments = new (byte, int)?[PotionSlotCount];
        private readonly Dictionary<string, Texture2D> _potionTextureCache = new();
        private const int PotionIconCacheSize = 48; // fixed size for BMD preview caching

        // Potion picker popup
        private bool _potionPickerOpen;
        private int _potionPickerSlot = -1;
        private readonly List<PotionCandidate> _potionCandidates = new();
        private int _hoveredPotionCandidate = -1;
        private Rectangle _potionPickerRect;
        private Rectangle[] _potionPickerItemRects = Array.Empty<Rectangle>();

        // Interface buttons
        private static readonly string[] ButtonLabels = { "MENU", "CHAR", "INV", "PARTY", "GUILD", "QUEST" };
        private int _hoveredButton = -1;
        private int _hoveredSlot = -1;

        // Keyboard
        private static readonly Keys[] SlotKeys =
        {
            Keys.Q, Keys.W, Keys.E,
            Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
            Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0
        };
        private static readonly string[] SlotKeyLabels =
        {
            "Q", "W", "E",
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0"
        };

        public SkillEntryState? SelectedSkill => _slotSkills[_activeSkillSlot];

        public ModernBottomHud(CharacterState state, SkillSelectionPanel skillPanel)
        {
            _state = state;
            _skillPanel = skillPanel;

            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            _skillPanel.SkillSelected += OnSkillSelectedFromPanel;

            var defaultSkill = _state.GetSkills().FirstOrDefault();
            if (defaultSkill != null)
            {
                _slotSkills[3] = defaultSkill;
            }

            RefreshLayout();
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _lastVirtualSize = Point.Zero;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            RefreshLayout();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _totalTime = gameTime.TotalGameTime.TotalSeconds;

            _targetHpPct = _state.MaximumHealth > 0 ? _state.CurrentHealth / (float)_state.MaximumHealth : 0f;
            _targetMpPct = _state.MaximumMana > 0 ? _state.CurrentMana / (float)_state.MaximumMana : 0f;
            _targetSdPct = _state.MaximumShield > 0 ? _state.CurrentShield / (float)_state.MaximumShield : 0f;
            _targetAgPct = _state.MaximumAbility > 0 ? _state.CurrentAbility / (float)_state.MaximumAbility : 0f;

            _displayHpPct = MathHelper.Lerp(_displayHpPct, _targetHpPct, LerpSpeed * dt);
            _displayMpPct = MathHelper.Lerp(_displayMpPct, _targetMpPct, LerpSpeed * dt);
            _displaySdPct = MathHelper.Lerp(_displaySdPct, _targetSdPct, LerpSpeed * dt);
            _displayAgPct = MathHelper.Lerp(_displayAgPct, _targetAgPct, LerpSpeed * dt);

            HandleKeyboard();
            HandleMouseHover();
            HandlePotionPickerClick();
            EnsurePotionIconsCached();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
                return;

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
                if (_font == null)
                    return;

                var pixel = GraphicsManager.Instance.Pixel;
                if (pixel == null)
                    return;

                DrawPanelBackground(spriteBatch, pixel);

                // Left bars: HP + SD (next to quick slots)
                DrawResourceBar(spriteBatch, pixel, _hpBarRect, _displayHpPct,
                    HpColorDark, HpColor, HpColorBright, HpGlow,
                    $"{_state.CurrentHealth}/{_state.MaximumHealth}", "HP", critical: _targetHpPct < 0.25f);
                DrawResourceBar(spriteBatch, pixel, _sdBarRect, _displaySdPct,
                    SdColorDark, SdColor, SdColorBright, SdGlow,
                    $"{_state.CurrentShield}/{_state.MaximumShield}", "SD", critical: false);

                // Right bars: MP + AG (next to quick slots)
                DrawResourceBar(spriteBatch, pixel, _mpBarRect, _displayMpPct,
                    MpColorDark, MpColor, MpColorBright, MpGlow,
                    $"{_state.CurrentMana}/{_state.MaximumMana}", "MP", critical: _targetMpPct < 0.15f);
                DrawResourceBar(spriteBatch, pixel, _agBarRect, _displayAgPct,
                    AgColorDark, AgColor, AgColorBright, AgGlow,
                    $"{_state.CurrentAbility}/{_state.MaximumAbility}", "AG", critical: false);

                DrawQuickSlots(spriteBatch, pixel);
                DrawInterfaceButtons(spriteBatch, pixel);
                DrawExpBar(spriteBatch, pixel);

                if (_potionPickerOpen)
                    DrawPotionPicker(spriteBatch, pixel);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override bool OnClick()
        {
            base.OnClick();

            var mousePos = MuGame.Instance.UiMouseState;

            // If picker is open, clicks are handled in HandlePotionPickerClick (Update)
            if (_potionPickerOpen)
                return true;

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    if (i < PotionSlotCount)
                    {
                        // Potion slot → open picker
                        OpenPotionPicker(i);
                    }
                    else
                    {
                        // Skill slot → open skill selection panel
                        _pendingAssignSlot = i;
                        _skillPanel.Open(_state);
                    }
                    return true;
                }
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    OnButtonClicked(i);
                    return true;
                }
            }

            if (_panelRect.Contains(mousePos.X, mousePos.Y) || _expBarRect.Contains(mousePos.X, mousePos.Y))
                return true;

            return false;
        }

        private void HandleKeyboard()
        {
            var kb = MuGame.Instance.Keyboard;
            var prev = MuGame.Instance.PrevKeyboard;

            // Q/W/E → consume assigned potion
            for (int i = 0; i < PotionSlotCount; i++)
            {
                if (kb.IsKeyDown(SlotKeys[i]) && !prev.IsKeyDown(SlotKeys[i]))
                {
                    ConsumePotionInSlot(i);
                }
            }

            // 1-0 → select skill slot
            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                if (kb.IsKeyDown(SlotKeys[i]) && !prev.IsKeyDown(SlotKeys[i]))
                {
                    _activeSkillSlot = i;
                }
            }

            // Escape → close potion picker
            if (_potionPickerOpen && kb.IsKeyDown(Keys.Escape) && !prev.IsKeyDown(Keys.Escape))
            {
                _potionPickerOpen = false;
            }
        }

        private void HandleMouseHover()
        {
            var mousePos = MuGame.Instance.UiMouseState;
            _hoveredButton = -1;
            _hoveredSlot = -1;
            _hoveredPotionCandidate = -1;

            // Check potion picker first (it's on top)
            if (_potionPickerOpen)
            {
                for (int i = 0; i < _potionPickerItemRects.Length; i++)
                {
                    if (_potionPickerItemRects[i].Contains(mousePos.X, mousePos.Y))
                    {
                        _hoveredPotionCandidate = i;
                        return;
                    }
                }
            }

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    _hoveredSlot = i;
                    break;
                }
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    _hoveredButton = i;
                    break;
                }
            }
        }

        private void HandlePotionPickerClick()
        {
            if (!_potionPickerOpen)
                return;

            var mouse = MuGame.Instance.UiMouseState;
            var prevMouse = MuGame.Instance.PrevUiMouseState;

            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed
                && prevMouse.LeftButton == ButtonState.Released;

            if (!leftJustPressed)
                return;

            // Check if clicked on a picker item
            for (int i = 0; i < _potionPickerItemRects.Length; i++)
            {
                if (_potionPickerItemRects[i].Contains(mouse.X, mouse.Y))
                {
                    if (i < _potionCandidates.Count && _potionPickerSlot >= 0 && _potionPickerSlot < PotionSlotCount)
                    {
                        var candidate = _potionCandidates[i];
                        _potionAssignments[_potionPickerSlot] = (candidate.Group, candidate.Id);
                        SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                    }
                    _potionPickerOpen = false;
                    return;
                }
            }

            // Click outside picker → close
            if (!_potionPickerRect.Contains(mouse.X, mouse.Y))
            {
                _potionPickerOpen = false;
            }
        }

        private void EnsurePotionIconsCached()
        {
            // Pre-generate BMD previews (outside SpriteBatch scope) using fixed cache size
            for (int i = 0; i < PotionSlotCount; i++)
            {
                var assignment = _potionAssignments[i];
                if (assignment == null) continue;
                var def = ItemDatabase.GetItemDefinition(assignment.Value.Group, (short)assignment.Value.Id);
                if (def?.TexturePath != null && def.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize) == null)
                        BmdPreviewRenderer.GetPreview(def, PotionIconCacheSize, PotionIconCacheSize);
                }
            }

            if (_potionPickerOpen)
            {
                foreach (var candidate in _potionCandidates)
                {
                    var def = ItemDatabase.GetItemDefinition(candidate.Group, (short)candidate.Id);
                    if (def?.TexturePath != null && def.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                    {
                        if (BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize) == null)
                            BmdPreviewRenderer.GetPreview(def, PotionIconCacheSize, PotionIconCacheSize);
                    }
                }
            }
        }

        private void OnSkillSelectedFromPanel(SkillEntryState skill)
        {
            int targetSlot = _pendingAssignSlot >= PotionSlotCount ? _pendingAssignSlot : _activeSkillSlot;
            if (targetSlot < PotionSlotCount)
                targetSlot = 3;

            _slotSkills[targetSlot] = skill;
            _activeSkillSlot = targetSlot;
            _pendingAssignSlot = -1;
        }

        private void OnButtonClicked(int index)
        {
            SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");

            if (MuGame.Instance?.ActiveScene is not Scenes.GameScene gs)
                return;

            switch (index)
            {
                case 0: gs.PauseMenu.Visible = !gs.PauseMenu.Visible; break;
                case 1: ToggleWindow<Character.CharacterInfoWindowControl>(gs); break;
                case 2:
                    if (gs.InventoryControl != null)
                    {
                        if (gs.InventoryControl.Visible)
                        {
                            gs.InventoryControl.Hide();
                        }
                        else
                        {
                            gs.InventoryControl.Show();
                        }
                    }
                    break;
                case 3: ToggleWindow<Party.PartyPanelControl>(gs); break;
            }
        }

        private static void ToggleWindow<T>(Scenes.GameScene gs) where T : GameControl
        {
            for (int i = 0; i < gs.Controls.Count; i++)
            {
                if (gs.Controls[i] is T ctrl)
                {
                    ctrl.Visible = !ctrl.Visible;
                    return;
                }
            }
        }

        // ════════════════════════════ Layout ════════════════════════════
        //
        // Layout (left → right):
        //   [PARTY][GUILD][QUEST] | HP SD | [Q][W][E]  [1][2]...[0] | MP AG | [MENU][CHAR][INV]

        private void RefreshLayout()
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (virtualSize == _lastVirtualSize)
                return;

            _lastVirtualSize = virtualSize;

            int vw = virtualSize.X;
            int vh = virtualSize.Y;

            int panelH = 92;
            int expH = 12;
            int panelY = vh - panelH - expH;

            _panelRect = new Rectangle(0, panelY, vw, panelH);
            _expBarRect = new Rectangle(0, vh - expH, vw, expH);

            // Font scales
            _barFontScale = 0.45f;
            _slotFontScale = 0.36f;
            _btnFontScale = 0.40f;
            _expFontScale = 0.42f;

            int pad = 6;
            int innerTop = panelY + pad;
            int innerH = panelH - pad * 2;

            // ── Buttons (edges, tall, stacked vertically) ──
            int btnW = 56;
            int btnGap = 3;
            int btnCount = 3;
            int btnH = (innerH - btnGap * (btnCount - 1)) / btnCount;

            _btnRects = new Rectangle[ButtonLabels.Length];

            // Left side buttons: PARTY(3), GUILD(4), QUEST(5)
            int leftBtnX = pad;
            for (int i = 0; i < 3; i++)
            {
                _btnRects[3 + i] = new Rectangle(
                    leftBtnX, innerTop + i * (btnH + btnGap),
                    btnW, btnH);
            }

            // Right side buttons: MENU(0), CHAR(1), INV(2)
            int rightBtnX = vw - pad - btnW;
            for (int i = 0; i < 3; i++)
            {
                _btnRects[i] = new Rectangle(
                    rightBtnX, innerTop + i * (btnH + btnGap),
                    btnW, btnH);
            }

            // ── Available center space ──
            int contentLeft = leftBtnX + btnW + 6;
            int contentRight = rightBtnX - 6;
            int contentW = contentRight - contentLeft;

            // ── Quick slots first — compute how big they can be ──
            int slotGap = 3;
            int potionGap = 10;
            int fixedGaps = (SlotCount - 1) * slotGap + potionGap;

            // Slots take ~45% of center, bars take rest
            int barW = (int)(contentW * 0.19f);
            int barSlotGap = 6;
            int slotsAreaW = contentW - 2 * barW - 2 * barSlotGap;
            int slotSize = Math.Min(
                (slotsAreaW - fixedGaps) / SlotCount,
                innerH); // don't exceed panel height
            slotSize = Math.Max(slotSize, 30); // minimum

            int totalSlotW = SlotCount * slotSize + fixedGaps;
            int slotsAreaLeft = contentLeft + barW + barSlotGap;
            int slotsAreaRight = contentRight - barW - barSlotGap;
            int actualSlotsW = slotsAreaRight - slotsAreaLeft;
            int slotStartX = slotsAreaLeft + (actualSlotsW - totalSlotW) / 2;
            int slotY = panelY + (panelH - slotSize) / 2;

            _slotRects = new Rectangle[SlotCount];
            int sx = slotStartX;
            for (int i = 0; i < SlotCount; i++)
            {
                _slotRects[i] = new Rectangle(sx, slotY, slotSize, slotSize);
                sx += slotSize + slotGap;
                if (i == PotionSlotCount - 1) sx += potionGap;
            }

            // ── Resource bars (between buttons and slots, vertically centered) ──
            int barH = 24;
            int barGapV = 4;
            int barsBlockH = barH * 2 + barGapV;
            int barsTopY = panelY + (panelH - barsBlockH) / 2;

            // Left bars: HP + SD
            _hpBarRect = new Rectangle(contentLeft, barsTopY, barW, barH);
            _sdBarRect = new Rectangle(contentLeft, barsTopY + barH + barGapV, barW, barH);

            // Right bars: MP + AG
            int rightBarX = contentRight - barW;
            _mpBarRect = new Rectangle(rightBarX, barsTopY, barW, barH);
            _agBarRect = new Rectangle(rightBarX, barsTopY + barH + barGapV, barW, barH);

            X = 0;
            Y = panelY;
            ControlSize = new Point(vw, panelH + expH);
            ViewSize = ControlSize;
        }

        // ════════════════════════════ Drawing ════════════════════════════

        private void DrawPanelBackground(SpriteBatch sb, Texture2D pixel)
        {
            // Top shadow fade above the panel
            var shadowRect = new Rectangle(_panelRect.X, _panelRect.Y - 8, _panelRect.Width, 8);
            UiDrawHelper.DrawVerticalGradient(sb, shadowRect,
                Color.Transparent, new Color(0, 0, 0, 100));

            // Outer border frame
            sb.Draw(pixel, _panelRect, ModernHudTheme.BorderOuter);

            // Inner gradient background
            var inner = new Rectangle(_panelRect.X + 1, _panelRect.Y + 1,
                Math.Max(1, _panelRect.Width - 2), Math.Max(1, _panelRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, inner,
                new Color(20, 24, 32, 252), new Color(8, 10, 14, 255));

            // Top accent line (gold)
            sb.Draw(pixel,
                new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.Accent * 0.55f);

            // Second subtle highlight line
            sb.Draw(pixel,
                new Rectangle(inner.X + 2, inner.Y + 1, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.BorderInner * 0.25f);

            // Vertical separators between buttons and bars
            DrawVerticalSeparator(sb, pixel,
                _btnRects[0].Right + 3, _panelRect.Y + 4, _panelRect.Height - 8);
            DrawVerticalSeparator(sb, pixel,
                _btnRects[3].X - 4, _panelRect.Y + 4, _panelRect.Height - 8);
        }

        private static void DrawVerticalSeparator(SpriteBatch sb, Texture2D pixel, int x, int y, int height)
        {
            sb.Draw(pixel, new Rectangle(x, y, 1, height), ModernHudTheme.BorderOuter * 0.9f);
            sb.Draw(pixel, new Rectangle(x + 1, y, 1, height), ModernHudTheme.BorderInner * 0.3f);
            sb.Draw(pixel, new Rectangle(x - 1, y, 3, 2), ModernHudTheme.Accent * 0.45f);
        }

        private void DrawResourceBar(SpriteBatch sb, Texture2D pixel, Rectangle rect,
            float pct, Color darkColor, Color mainColor, Color brightColor, Color glowColor,
            string valueText, string label, bool critical)
        {
            float clampedPct = MathHelper.Clamp(pct, 0f, 1f);

            // Pulsing alpha for critical state
            float critAlpha = 1f;
            if (critical && clampedPct > 0f)
            {
                critAlpha = 0.65f + 0.35f * (float)Math.Sin(_totalTime * 4.0);
            }

            // Outer frame with rounded-look bevel
            sb.Draw(pixel, rect, ModernHudTheme.BorderOuter);

            // Inner track
            var track = new Rectangle(rect.X + 1, rect.Y + 1,
                Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));

            // Track background with subtle gradient
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(18, 20, 28, 240), new Color(8, 10, 14, 250));

            // Fill bar
            int fillW = Math.Max(0, (int)(track.Width * clampedPct));
            if (fillW > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillW, track.Height);

                // Main gradient fill (dark → bright)
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect, darkColor * critAlpha, mainColor * critAlpha);

                // Top shine line (bright, 1px)
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1),
                    brightColor * 0.6f * critAlpha);

                // Second shine line (softer)
                if (fillRect.Height > 4)
                {
                    sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y + 1, fillRect.Width, 1),
                        brightColor * 0.2f * critAlpha);
                }

                // Bottom shadow line
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Bottom - 1, fillRect.Width, 1),
                    Color.Black * 0.3f);

                // Right edge glow at fill boundary
                if (fillW > 2 && glowColor.A > 0)
                {
                    int glowW = Math.Min(6, fillW);
                    sb.Draw(pixel, new Rectangle(fillRect.Right - glowW, fillRect.Y, glowW, fillRect.Height),
                        glowColor * critAlpha);
                }

                // Segment tick marks every 25%
                for (int seg = 1; seg < 4; seg++)
                {
                    int tickX = track.X + (int)(track.Width * (seg / 4f));
                    if (tickX < fillRect.Right && tickX > track.X)
                    {
                        sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height),
                            Color.Black * 0.25f);
                    }
                }
            }

            // Segment tick marks (unfilled region too, very subtle)
            for (int seg = 1; seg < 4; seg++)
            {
                int tickX = track.X + (int)(track.Width * (seg / 4f));
                if (tickX >= track.X + fillW)
                {
                    sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height),
                        ModernHudTheme.BorderInner * 0.15f);
                }
            }

            // Inner border highlight (top-left bevel)
            sb.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), 1),
                ModernHudTheme.BorderHighlight * 0.12f);

            // Text
            if (_font != null)
            {
                float textScale = _barFontScale;

                // Label (left-aligned)
                var labelSize = _font.MeasureString(label) * textScale;
                float labelX = rect.X + 5;
                float labelY = rect.Y + (rect.Height - labelSize.Y) / 2f;
                DrawTextWithShadow(sb, label, new Vector2(labelX, labelY), mainColor * 0.9f, textScale);

                // Value (right-aligned)
                var valSize = _font.MeasureString(valueText) * textScale;
                float valX = rect.Right - valSize.X - 5;
                float valY = rect.Y + (rect.Height - valSize.Y) / 2f;
                DrawTextWithShadow(sb, valueText, new Vector2(valX, valY), ModernHudTheme.TextWhite, textScale);
            }
        }

        private void DrawQuickSlots(SpriteBatch sb, Texture2D pixel)
        {
            for (int i = 0; i < _slotRects.Length; i++)
            {
                var rect = _slotRects[i];
                bool isActive = i == _activeSkillSlot;
                bool isHovered = i == _hoveredSlot;
                bool isSkillSlot = i >= PotionSlotCount;
                bool isPotionSlot = i < PotionSlotCount;

                // Active slot: outer glow aura
                if (isActive)
                {
                    float glowPulse = 0.35f + 0.15f * (float)Math.Sin(_totalTime * 3.0);
                    var glowRect = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                    sb.Draw(pixel, glowRect, ModernHudTheme.AccentGlow * glowPulse);
                }

                // Slot outer border
                Color borderColor = isActive ? ModernHudTheme.Accent
                    : isHovered ? ModernHudTheme.SlotHover
                    : isPotionSlot ? new Color(55, 45, 65, 180) // slightly purple tint for potions
                    : ModernHudTheme.SlotBorder;

                sb.Draw(pixel, rect, borderColor);

                // Slot inner background with gradient
                var inner = new Rectangle(rect.X + 1, rect.Y + 1,
                    Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
                UiDrawHelper.DrawVerticalGradient(sb, inner,
                    new Color(16, 18, 24, 245), new Color(8, 10, 14, 250));

                // Inner top highlight
                sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1),
                    ModernHudTheme.BorderHighlight * 0.15f);

                // Hover highlight overlay
                if (isHovered && !isActive)
                {
                    sb.Draw(pixel, inner, ModernHudTheme.SlotHover * 0.15f);
                }

                // Draw skill icon if assigned
                if (isSkillSlot && _slotSkills[i] != null)
                {
                    DrawSkillIcon(sb, inner, _slotSkills[i]!);
                }

                // Potion slot: draw assigned item icon or empty indicator
                if (isPotionSlot)
                {
                    DrawPotionSlotContent(sb, pixel, inner, i);
                }

                // Key label badge (top-left)
                if (_font != null)
                {
                    string keyLabel = SlotKeyLabels[i];
                    float keyScale = _slotFontScale;
                    var keySize = _font.MeasureString(keyLabel) * keyScale;

                    // Badge background
                    int badgeW = (int)keySize.X + 5;
                    int badgeH = (int)keySize.Y + 2;
                    var badgeRect = new Rectangle(rect.X, rect.Y, badgeW, badgeH);
                    sb.Draw(pixel, badgeRect, Color.Black * 0.55f);

                    float kx = rect.X + 2;
                    float ky = rect.Y + 1;
                    Color keyColor = isActive ? ModernHudTheme.AccentBright
                        : isHovered ? ModernHudTheme.TextWhite
                        : ModernHudTheme.TextGray;
                    sb.DrawString(_font, keyLabel, new Vector2(kx, ky), keyColor,
                        0f, Vector2.Zero, keyScale, SpriteEffects.None, 0f);
                }

                // Active slot bottom indicator bar
                if (isActive)
                {
                    sb.Draw(pixel, new Rectangle(rect.X + 2, rect.Bottom - 2, rect.Width - 4, 2),
                        ModernHudTheme.Accent * 0.9f);
                }
            }
        }

        private void DrawSkillIcon(SpriteBatch sb, Rectangle dest, SkillEntryState skill)
        {
            var definition = SkillDatabase.GetSkillDefinition(skill.SkillId);
            if (!SkillIconAtlas.TryResolve(skill.SkillId, definition, out var frame))
                return;

            var tex = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            if (tex == null)
                return;

            int pad = 3;
            var iconDest = new Rectangle(dest.X + pad, dest.Y + pad,
                Math.Max(1, dest.Width - pad * 2), Math.Max(1, dest.Height - pad * 2));
            sb.Draw(tex, iconDest, frame.SourceRectangle, Color.White);
        }

        private void DrawInterfaceButtons(SpriteBatch sb, Texture2D pixel)
        {
            for (int i = 0; i < _btnRects.Length; i++)
            {
                var rect = _btnRects[i];
                bool isHovered = i == _hoveredButton;

                // Button border
                sb.Draw(pixel, rect, isHovered ? ModernHudTheme.BorderInner : ModernHudTheme.BorderOuter);

                // Button background with gradient
                var inner = new Rectangle(rect.X + 1, rect.Y + 1,
                    Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));

                if (isHovered)
                {
                    UiDrawHelper.DrawVerticalGradient(sb, inner,
                        ModernHudTheme.BgLighter, ModernHudTheme.BgMid);
                    // Hover glow underline
                    sb.Draw(pixel, new Rectangle(rect.X + 2, rect.Bottom - 1, rect.Width - 4, 1),
                        ModernHudTheme.Accent * 0.5f);
                }
                else
                {
                    UiDrawHelper.DrawVerticalGradient(sb, inner,
                        ModernHudTheme.BgMid, ModernHudTheme.BgDark);
                }

                // Top highlight
                sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1),
                    ModernHudTheme.BorderHighlight * (isHovered ? 0.3f : 0.12f));

                // Button text
                if (_font != null)
                {
                    string label = ButtonLabels[i];
                    float btnScale = _btnFontScale;
                    var textSize = _font.MeasureString(label) * btnScale;
                    float tx = rect.X + (rect.Width - textSize.X) / 2f;
                    float ty = rect.Y + (rect.Height - textSize.Y) / 2f;

                    Color textColor = isHovered ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
                    DrawTextWithShadow(sb, label, new Vector2(tx, ty), textColor, btnScale);
                }
            }
        }

        private void DrawExpBar(SpriteBatch sb, Texture2D pixel)
        {
            // Frame
            sb.Draw(pixel, _expBarRect, ModernHudTheme.BorderOuter);

            // Track with gradient
            var track = new Rectangle(_expBarRect.X + 1, _expBarRect.Y + 1,
                Math.Max(1, _expBarRect.Width - 2), Math.Max(1, _expBarRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(12, 14, 20, 245), new Color(6, 8, 12, 250));

            // Calculate EXP percentage
            double expPercent = 0;
            if (_state.ExperienceForNextLevel > 0)
            {
                ushort currentLevel = _state.Level;
                ulong prevLevelExp = currentLevel > 1
                    ? (ulong)((currentLevel - 1 + 9) * (currentLevel - 1) * (currentLevel - 1) * 10)
                    : 0;
                ulong expInCurrentLevel = _state.Experience >= prevLevelExp ? _state.Experience - prevLevelExp : 0;
                ulong expNeededForLevel = _state.ExperienceForNextLevel >= prevLevelExp
                    ? _state.ExperienceForNextLevel - prevLevelExp : 1;
                expPercent = expNeededForLevel > 0 ? (expInCurrentLevel / (double)expNeededForLevel) * 100.0 : 0.0;
            }

            float pct = MathHelper.Clamp((float)(expPercent / 100.0), 0f, 1f);
            int fillW = (int)(track.Width * pct);

            if (fillW > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillW, track.Height);

                // Main gradient fill
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect, ExpColorDark, ExpColor);

                // Top shine
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1),
                    ExpColorBright * 0.5f);

                // Bottom shadow
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Bottom - 1, fillRect.Width, 1),
                    Color.Black * 0.3f);

                // Glow at the fill edge
                if (fillW > 3 && ExpGlow.A > 0)
                {
                    int glowW = Math.Min(8, fillW);
                    sb.Draw(pixel, new Rectangle(fillRect.Right - glowW, fillRect.Y, glowW, fillRect.Height),
                        ExpGlow);
                }

                // Animated shimmer moving across the bar
                float shimmerPhase = (float)(_totalTime * 0.3 % 1.0);
                int shimmerX = track.X + (int)(track.Width * shimmerPhase);
                int shimmerW = 20;
                if (shimmerX < fillRect.Right && shimmerX + shimmerW > fillRect.X)
                {
                    int clippedX = Math.Max(shimmerX, fillRect.X);
                    int clippedR = Math.Min(shimmerX + shimmerW, fillRect.Right);
                    int clippedW = clippedR - clippedX;
                    if (clippedW > 0)
                    {
                        sb.Draw(pixel, new Rectangle(clippedX, fillRect.Y, clippedW, fillRect.Height),
                            ExpColorBright * 0.15f);
                    }
                }
            }

            // 10% segment tick marks
            for (int seg = 1; seg < 10; seg++)
            {
                int tickX = track.X + (int)(track.Width * (seg / 10f));
                Color tickColor = tickX < track.X + fillW
                    ? Color.Black * 0.2f
                    : ModernHudTheme.BorderInner * 0.12f;
                sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height), tickColor);
            }

            // EXP text
            if (_font != null)
            {
                string expText = $"EXP {expPercent:F1}%";
                float textScale = _expFontScale;
                var textSize = _font.MeasureString(expText) * textScale;
                float tx = _expBarRect.X + (_expBarRect.Width - textSize.X) / 2f;
                float ty = _expBarRect.Y + (_expBarRect.Height - textSize.Y) / 2f;

                // Text shadow
                sb.DrawString(_font, expText, new Vector2(tx + 1, ty + 1),
                    Color.Black * 0.8f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_font, expText, new Vector2(tx, ty),
                    ExpColorBright, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            }
        }

        // ════════════════════════════ Potions ════════════════════════════

        private record struct PotionCandidate(byte Group, int Id, string Name, string? TexturePath, int Count, byte FirstSlot);

        private void OpenPotionPicker(int slotIndex)
        {
            _potionPickerSlot = slotIndex;
            BuildPotionCandidates();

            if (_potionCandidates.Count == 0)
            {
                _potionPickerOpen = false;
                return;
            }

            _potionPickerOpen = true;
            LayoutPotionPicker();
        }

        private void BuildPotionCandidates()
        {
            _potionCandidates.Clear();

            var items = _state.GetInventoryItems();
            var grouped = new Dictionary<(byte, int), (string Name, string? TexturePath, int Count, byte FirstSlot)>();

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue; // skip equipment slots

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def == null || !def.IsQuickSlotConsumable() || def.IsJewel() || def.IsUpgradeJewel())
                    continue;

                byte durability = ItemDatabase.GetItemDurability(kvp.Value);
                int stack = Math.Max(1, (int)durability);

                var key = ((byte)def.Group, def.Id);
                if (grouped.TryGetValue(key, out var existing))
                {
                    grouped[key] = (existing.Name, existing.TexturePath, existing.Count + stack, existing.FirstSlot);
                }
                else
                {
                    grouped[key] = (def.Name ?? $"Item {def.Group}/{def.Id}", def.TexturePath, stack, kvp.Key);
                }
            }

            foreach (var kvp in grouped.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
            {
                _potionCandidates.Add(new PotionCandidate(
                    kvp.Key.Item1, kvp.Key.Item2,
                    kvp.Value.Name, kvp.Value.TexturePath,
                    kvp.Value.Count, kvp.Value.FirstSlot));
            }
        }

        private void LayoutPotionPicker()
        {
            if (_potionPickerSlot < 0 || _potionPickerSlot >= _slotRects.Length || _potionCandidates.Count == 0)
                return;

            int itemH = 28;
            int padX = 6;
            int padY = 4;
            int pickerW = 180;
            int pickerH = padY * 2 + _potionCandidates.Count * itemH;

            var slotRect = _slotRects[_potionPickerSlot];
            int pickerX = slotRect.X + (slotRect.Width - pickerW) / 2;
            int pickerY = slotRect.Y - pickerH - 4;

            // Clamp to screen
            pickerX = Math.Clamp(pickerX, 2, _lastVirtualSize.X - pickerW - 2);
            pickerY = Math.Max(2, pickerY);

            _potionPickerRect = new Rectangle(pickerX, pickerY, pickerW, pickerH);

            _potionPickerItemRects = new Rectangle[_potionCandidates.Count];
            for (int i = 0; i < _potionCandidates.Count; i++)
            {
                _potionPickerItemRects[i] = new Rectangle(
                    pickerX + padX, pickerY + padY + i * itemH,
                    pickerW - padX * 2, itemH);
            }
        }

        private void DrawPotionPicker(SpriteBatch sb, Texture2D pixel)
        {
            if (_potionCandidates.Count == 0)
                return;

            // Background
            sb.Draw(pixel, _potionPickerRect, ModernHudTheme.BorderOuter);
            var inner = new Rectangle(_potionPickerRect.X + 1, _potionPickerRect.Y + 1,
                Math.Max(1, _potionPickerRect.Width - 2), Math.Max(1, _potionPickerRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, inner,
                new Color(22, 26, 35, 250), new Color(12, 14, 20, 255));

            // Top accent
            sb.Draw(pixel, new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.Accent * 0.5f);

            for (int i = 0; i < _potionCandidates.Count; i++)
            {
                var candidate = _potionCandidates[i];
                var rect = _potionPickerItemRects[i];
                bool hovered = i == _hoveredPotionCandidate;

                if (hovered)
                {
                    sb.Draw(pixel, rect, ModernHudTheme.SlotHover * 0.25f);
                }

                // Icon area (left side)
                int iconSize = Math.Min(rect.Height - 4, 22);
                var iconRect = new Rectangle(rect.X + 2, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);

                // Draw item icon
                var candidateDef = ItemDatabase.GetItemDefinition(candidate.Group, (short)candidate.Id);
                Texture2D? iconTex = ResolveItemIcon(candidateDef);
                if (iconTex != null)
                {
                    sb.Draw(iconTex, iconRect, Color.White);
                }
                else
                {
                    // Fallback colored square
                    sb.Draw(pixel, iconRect, new Color(60, 50, 80) * 0.5f);
                }

                // Name text
                if (_font != null)
                {
                    float nameScale = 0.36f;
                    string displayName = candidate.Name;
                    float nameX = iconRect.Right + 5;
                    float nameY = rect.Y + (rect.Height - _font.MeasureString(displayName).Y * nameScale) / 2f;

                    Color nameColor = hovered ? ModernHudTheme.TextGold : ModernHudTheme.TextWhite;
                    DrawTextWithShadow(sb, displayName, new Vector2(nameX, nameY), nameColor, nameScale);

                    // Count (right-aligned)
                    string countText = $"x{candidate.Count}";
                    var countSize = _font.MeasureString(countText) * nameScale;
                    float countX = rect.Right - countSize.X - 2;
                    float countY = nameY;
                    DrawTextWithShadow(sb, countText, new Vector2(countX, countY), ModernHudTheme.TextGray, nameScale);
                }

                // Separator line
                if (i < _potionCandidates.Count - 1)
                {
                    sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom, rect.Width, 1),
                        ModernHudTheme.BorderInner * 0.15f);
                }
            }

            UiDrawHelper.DrawCornerAccents(sb, _potionPickerRect,
                ModernHudTheme.Accent * 0.3f, size: 5, thickness: 1);
        }

        private void DrawPotionSlotContent(SpriteBatch sb, Texture2D pixel, Rectangle inner, int slotIndex)
        {
            var assignment = _potionAssignments[slotIndex];
            if (assignment == null)
            {
                // Empty potion slot indicator
                if (_font != null)
                {
                    int dSize = 4;
                    int cx = inner.X + inner.Width / 2;
                    int cy = inner.Y + inner.Height / 2 + 2;
                    sb.Draw(pixel, new Rectangle(cx - dSize / 2, cy - dSize / 2, dSize, dSize),
                        new Color(100, 80, 130) * 0.35f);
                }
                return;
            }

            var (group, id) = assignment.Value;
            var def = ItemDatabase.GetItemDefinition(group, (short)id);
            if (def == null) return;

            // Draw item icon
            Texture2D? tex = ResolveItemIcon(def);
            if (tex != null)
            {
                int pad = 3;
                var iconDest = new Rectangle(inner.X + pad, inner.Y + pad,
                    Math.Max(1, inner.Width - pad * 2), Math.Max(1, inner.Height - pad * 2));
                sb.Draw(tex, iconDest, Color.White);
            }

            // Count badge (bottom-right)
            if (_font != null)
            {
                int count = CountPotionInInventory(group, id);
                if (count > 0)
                {
                    string countText = count.ToString();
                    float countScale = _slotFontScale * 0.9f;
                    var countSize = _font.MeasureString(countText) * countScale;
                    float cx = inner.Right - countSize.X - 1;
                    float cy = inner.Bottom - countSize.Y - 1;

                    // Badge background
                    sb.Draw(pixel, new Rectangle((int)cx - 1, (int)cy, (int)countSize.X + 3, (int)countSize.Y + 1),
                        Color.Black * 0.65f);
                    sb.DrawString(_font, countText, new Vector2(cx, cy),
                        ModernHudTheme.TextWhite, 0f, Vector2.Zero, countScale, SpriteEffects.None, 0f);
                }
                else
                {
                    // No stock — dim the icon
                    sb.Draw(pixel, inner, Color.Black * 0.5f);
                }
            }
        }

        private void ConsumePotionInSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= PotionSlotCount)
                return;

            var assignment = _potionAssignments[slotIndex];
            if (assignment == null) return;

            var (group, id) = assignment.Value;

            // Find first matching item in inventory
            var items = _state.GetInventoryItems();
            byte? foundSlot = null;

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue;

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def != null && def.Group == group && def.Id == id)
                {
                    foundSlot = kvp.Key;
                    break;
                }
            }

            if (foundSlot == null) return;

            // Play consumption sound
            var itemDef = ItemDatabase.GetItemDefinition(group, (short)id);
            string itemName = itemDef?.Name?.ToLowerInvariant() ?? string.Empty;
            if (itemName.Contains("apple"))
                SoundController.Instance.PlayBuffer("Sound/pEatApple.wav");
            else
                SoundController.Instance.PlayBuffer("Sound/pDrink.wav");

            byte slot = foundSlot.Value;
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = Task.Run(async () =>
                {
                    await svc.SendConsumeItemRequestAsync(slot);
                    await Task.Delay(300);
                    MuGame.ScheduleOnMainThread(() => _state.RaiseInventoryChanged());
                });
            }
        }

        private int CountPotionInInventory(byte group, int id)
        {
            int total = 0;
            var items = _state.GetInventoryItems();

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue;

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def != null && def.Group == group && def.Id == id)
                {
                    byte durability = ItemDatabase.GetItemDurability(kvp.Value);
                    total += Math.Max(1, (int)durability);
                }
            }

            return total;
        }

        private Texture2D? ResolveItemIcon(ItemDefinition? def)
        {
            if (def?.TexturePath == null)
                return null;

            string texturePath = def.TexturePath;

            // BMD models: use pre-cached preview at fixed size (generated in Update, scaled on draw)
            if (texturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                return BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize);

            // Non-BMD textures: load directly
            if (_potionTextureCache.TryGetValue(texturePath, out var cached))
                return cached;

            var tex = TextureLoader.Instance.GetTexture2D(texturePath);
            if (tex != null)
                _potionTextureCache[texturePath] = tex;

            return tex;
        }

        // ════════════════════════════ Helpers ════════════════════════════

        private void DrawTextWithShadow(SpriteBatch sb, string text, Vector2 pos, Color color, float scale)
        {
            sb.DrawString(_font!, text, pos + new Vector2(1, 1),
                Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font!, text, pos, color,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
