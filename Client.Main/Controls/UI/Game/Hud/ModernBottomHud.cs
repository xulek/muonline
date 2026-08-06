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

        private static readonly Color CompanionColorGood = new(92, 188, 122);
        private static readonly Color CompanionColorWarn = new(234, 186, 78);
        private static readonly Color CompanionColorDanger = new(220, 88, 88);
        private static readonly HashSet<int> HelperLifeIds = new() { 0, 1, 2, 3, 4 };

        // ──────────────── State ────────────────
        private readonly CharacterState _state;
        private readonly SkillSelectionPanel _skillPanel;

        private SpriteFont? _font;
        private Point _lastVirtualSize = Point.Zero;
        private double _totalTime;

        // Resource bar display values (lerped for animation)
        private float _displayHpPct, _displayMpPct, _displaySdPct, _displayAgPct;
        private float _targetHpPct, _targetMpPct, _targetSdPct, _targetAgPct;
        private uint _lastCurrentHealth = uint.MaxValue;
        private uint _lastMaximumHealth = uint.MaxValue;
        private uint _lastCurrentShield = uint.MaxValue;
        private uint _lastMaximumShield = uint.MaxValue;
        private uint _lastCurrentMana = uint.MaxValue;
        private uint _lastMaximumMana = uint.MaxValue;
        private uint _lastCurrentAbility = uint.MaxValue;
        private uint _lastMaximumAbility = uint.MaxValue;
        private string _healthText = string.Empty;
        private string _shieldText = string.Empty;
        private string _manaText = string.Empty;
        private string _abilityText = string.Empty;
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
        private readonly CompanionLifeInfo?[] _companionInfos = new CompanionLifeInfo?[2];

        // Skill slots: 0-2 = potion (Q/W/E), 3-12 = skills (1-0)
        private const int SlotCount = 13;
        private const int PotionSlotCount = 3;
        private readonly SkillEntryState?[] _slotSkills = new SkillEntryState?[SlotCount];
        private int _activeSkillSlot = 3;
        private int _pendingAssignSlot = -1;
        private bool _quickSlotsRestored;
        private bool _lastDarkRavenEquipped;

        // Potion slot assignments (Q=0, W=1, E=2) — stores item type
        public const int ItemHotbarSlotCount = 5;
        private readonly (byte Group, int Id)?[] _potionAssignments = new (byte, int)?[ItemHotbarSlotCount];
        private readonly Dictionary<string, Texture2D> _potionTextureCache = new();
        private const int PotionIconCacheSize = 48; // fixed size for BMD preview caching

        // Classic uses its own five-slot item bar and two skill sets. These fields hold
        // interaction state only; Modern still renders and handles its original 13-slot HUD.
        public static readonly int[] SetSlotCounts = { 4, 7 };
        private readonly ushort?[][] _imprintSets =
        {
            new ushort?[4],
            new ushort?[7]
        };
        private readonly SkillEntryState?[] _hotbarSkills = new SkillEntryState?[SlotCount - PotionSlotCount];
        private int _activeSet = 1;
        private bool _consumableCandidatesDirty = true;

        // Potion picker popup
        private bool _potionPickerOpen;
        private int _potionPickerSlot = -1;
        private readonly List<PotionCandidate> _potionCandidates = new();
        private readonly List<(byte Group, int Id, string Name, string? TexturePath, int Count)> _consumableCandidateView = new();
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

        private readonly record struct CompanionLifeInfo(string Name, int Current, int Maximum, Color FillColor);

        public SkillEntryState? SelectedSkill => _slotSkills[_activeSkillSlot];

        public int ActiveSet => _activeSet;
        public int VisibleSkillCount => SetSlotCounts[Math.Clamp(_activeSet, 0, SetSlotCounts.Length - 1)];
        public IReadOnlyList<SkillEntryState?> HotbarSkills => _hotbarSkills;
        public bool IsModernRenderer => UiThemeManager.CurrentId == UiThemeId.Modern;

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
            _state.InventoryChanged += OnInventoryChanged;

            RefreshLayout();
        }

        private void OnInventoryChanged()
        {
            _consumableCandidatesDirty = true;
        }

        public override void Dispose()
        {
            _state.InventoryChanged -= OnInventoryChanged;
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
            RefreshLayout();

            if (_lastDarkRavenEquipped != _state.IsDarkRavenEquipped)
            {
                _lastDarkRavenEquipped = _state.IsDarkRavenEquipped;
                _quickSlotsRestored = false;
            }

            RestoreQuickSlotsIfNeeded();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _totalTime = gameTime.TotalGameTime.TotalSeconds;

            _targetHpPct = _state.MaximumHealth > 0 ? _state.CurrentHealth / (float)_state.MaximumHealth : 0f;
            _targetMpPct = _state.MaximumMana > 0 ? _state.CurrentMana / (float)_state.MaximumMana : 0f;
            _targetSdPct = _state.MaximumShield > 0 ? _state.CurrentShield / (float)_state.MaximumShield : 0f;
            _targetAgPct = _state.MaximumAbility > 0 ? _state.CurrentAbility / (float)_state.MaximumAbility : 0f;
            RefreshResourceTexts();

            _displayHpPct = MathHelper.Lerp(_displayHpPct, _targetHpPct, LerpSpeed * dt);
            _displayMpPct = MathHelper.Lerp(_displayMpPct, _targetMpPct, LerpSpeed * dt);
            _displaySdPct = MathHelper.Lerp(_displaySdPct, _targetSdPct, LerpSpeed * dt);
            _displayAgPct = MathHelper.Lerp(_displayAgPct, _targetAgPct, LerpSpeed * dt);

            RefreshCompanionLifeInfos();
            if (IsModernRenderer)
            {
                HandleKeyboard();
                HandleMouseHover();
                HandlePotionPickerClick();
                EnsurePotionIconsCached();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || !IsModernRenderer)
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
                DrawCompanionLifeBars(spriteBatch, pixel);

                // Left bars: HP + SD (next to quick slots)
                DrawResourceBar(spriteBatch, pixel, _hpBarRect, _displayHpPct,
                    HpColorDark, HpColor, HpColorBright, HpGlow,
                    _healthText, "HP", critical: _targetHpPct < 0.25f);
                DrawResourceBar(spriteBatch, pixel, _sdBarRect, _displaySdPct,
                    SdColorDark, SdColor, SdColorBright, SdGlow,
                    _shieldText, "SD", critical: false);

                // Right bars: MP + AG (next to quick slots)
                DrawResourceBar(spriteBatch, pixel, _mpBarRect, _displayMpPct,
                    MpColorDark, MpColor, MpColorBright, MpGlow,
                    _manaText, "MP", critical: _targetMpPct < 0.15f);
                DrawResourceBar(spriteBatch, pixel, _agBarRect, _displayAgPct,
                    AgColorDark, AgColor, AgColorBright, AgGlow,
                    _abilityText, "AG", critical: false);

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
                        BeginSkillAssignment(i - PotionSlotCount);
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

        private void RefreshResourceTexts()
        {
            if (_lastCurrentHealth != _state.CurrentHealth || _lastMaximumHealth != _state.MaximumHealth)
            {
                _lastCurrentHealth = _state.CurrentHealth;
                _lastMaximumHealth = _state.MaximumHealth;
                _healthText = $"{_lastCurrentHealth}/{_lastMaximumHealth}";
            }

            if (_lastCurrentShield != _state.CurrentShield || _lastMaximumShield != _state.MaximumShield)
            {
                _lastCurrentShield = _state.CurrentShield;
                _lastMaximumShield = _state.MaximumShield;
                _shieldText = $"{_lastCurrentShield}/{_lastMaximumShield}";
            }

            if (_lastCurrentMana != _state.CurrentMana || _lastMaximumMana != _state.MaximumMana)
            {
                _lastCurrentMana = _state.CurrentMana;
                _lastMaximumMana = _state.MaximumMana;
                _manaText = $"{_lastCurrentMana}/{_lastMaximumMana}";
            }

            if (_lastCurrentAbility != _state.CurrentAbility || _lastMaximumAbility != _state.MaximumAbility)
            {
                _lastCurrentAbility = _state.CurrentAbility;
                _lastMaximumAbility = _state.MaximumAbility;
                _abilityText = $"{_lastCurrentAbility}/{_lastMaximumAbility}";
            }
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
                    PersistQuickSlots();
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
                        PersistQuickSlots();
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
            SyncHotbarSkills();
            PersistQuickSlots();
        }

        /// <summary>
        /// Opens the shared skill picker for a zero-based combat hotbar slot.
        /// Classic uses this path as well, so assignment does not depend on the HUD renderer.
        /// </summary>
        public void BeginSkillAssignment(int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= _hotbarSkills.Length || _skillPanel == null)
                return;

            _pendingAssignSlot = PotionSlotCount + hotbarIndex;
            _skillPanel.Interactive = true;
            _skillPanel.Open(_state);
            _skillPanel.BringToFront();
            if (Scene != null)
                Scene.FocusControl = _skillPanel;
        }

        /// <summary>
        /// Clears the active Classic skill set and immediately applies the empty hotbar.
        /// </summary>
        public void ResetActiveSkillSet()
        {
            ClearSet(_activeSet);
            ActivateSet(_activeSet);
            _pendingAssignSlot = -1;
        }

        private void SyncHotbarSkills()
        {
            for (int i = 0; i < _hotbarSkills.Length; i++)
                _hotbarSkills[i] = _slotSkills[PotionSlotCount + i];
        }

        public SkillEntryState? GetHotbarSkillAt(int hotbarIndex)
            => hotbarIndex >= 0 && hotbarIndex < _hotbarSkills.Length ? _hotbarSkills[hotbarIndex] : null;

        public void SetHotbarSkillAt(int hotbarIndex, SkillEntryState? skill)
        {
            if (hotbarIndex < 0 || hotbarIndex >= _hotbarSkills.Length)
                return;

            _slotSkills[PotionSlotCount + hotbarIndex] = skill;
            _hotbarSkills[hotbarIndex] = skill;
            if (_activeSet >= 0 && _activeSet < _imprintSets.Length && hotbarIndex < _imprintSets[_activeSet].Length)
                _imprintSets[_activeSet][hotbarIndex] = skill?.SkillId;
            PersistQuickSlots();
        }

        public void ClearHotbarSkillAt(int hotbarIndex) => SetHotbarSkillAt(hotbarIndex, null);

        public void AssignSkillToHotbar(SkillEntryState skill)
        {
            if (skill == null)
                return;

            for (int i = 0; i < _hotbarSkills.Length; i++)
            {
                if (_hotbarSkills[i] == null)
                {
                    SetHotbarSkillAt(i, skill);
                    _activeSkillSlot = PotionSlotCount + i;
                    return;
                }
            }

            SetHotbarSkillAt(0, skill);
            _activeSkillSlot = PotionSlotCount;
        }

        public bool IsSkillOnHotbar(ushort skillId)
        {
            for (int i = 0; i < _hotbarSkills.Length; i++)
                if (_hotbarSkills[i]?.SkillId == skillId)
                    return true;
            return false;
        }

        public int GetSetSlotCount(int set)
            => set >= 0 && set < SetSlotCounts.Length ? SetSlotCounts[set] : 0;

        public SkillEntryState? GetSetSkillAt(int set, int index)
        {
            if (set < 0 || set >= _imprintSets.Length || index < 0 || index >= _imprintSets[set].Length)
                return null;

            ushort? id = _imprintSets[set][index];
            if (!id.HasValue)
                return null;

            foreach (SkillEntryState skill in _state.GetSkills())
                if (skill.SkillId == id.Value)
                    return skill;
            return null;
        }

        public void SetSetSkillAt(int set, int index, SkillEntryState? skill)
        {
            if (set < 0 || set >= _imprintSets.Length || index < 0 || index >= _imprintSets[set].Length)
                return;
            _imprintSets[set][index] = skill?.SkillId;
        }

        public void ClearSetSkillAt(int set, int index) => SetSetSkillAt(set, index, null);

        public void ClearSet(int set)
        {
            if (set < 0 || set >= _imprintSets.Length)
                return;
            Array.Clear(_imprintSets[set]);
        }

        public void ActivateSet(int set)
        {
            if (set < 0 || set >= _imprintSets.Length)
                return;

            _activeSet = set;
            for (int i = 0; i < _hotbarSkills.Length; i++)
            {
                SkillEntryState? skill = i < _imprintSets[set].Length ? GetSetSkillAt(set, i) : null;
                _slotSkills[PotionSlotCount + i] = skill;
            }
            SyncHotbarSkills();
            EnsureActiveSkillSelection(_hotbarSkills.FirstOrDefault(skill => skill != null));
            PersistQuickSlots();
        }

        public (byte Group, int Id)? GetItemAssignmentAt(int index)
            => index >= 0 && index < _potionAssignments.Length ? _potionAssignments[index] : null;

        public void SetItemAssignmentAt(int index, (byte Group, int Id)? assignment)
        {
            if (index < 0 || index >= _potionAssignments.Length)
                return;
            _potionAssignments[index] = assignment;
            PersistQuickSlots();
        }

        public IReadOnlyList<(byte Group, int Id, string Name, string? TexturePath, int Count)> GetConsumableCandidates()
        {
            EnsureConsumableCandidates();
            return _consumableCandidateView;
        }

        public void EnsureConsumableIconsCached()
        {
            EnsureConsumableCandidates();
            for (int i = 0; i < _potionAssignments.Length; i++)
            {
                var assignment = _potionAssignments[i];
                if (!assignment.HasValue)
                    continue;

                ItemDefinition? definition = ItemDatabase.GetItemDefinition(assignment.Value.Group, (short)assignment.Value.Id);
                if (definition?.TexturePath?.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (BmdPreviewRenderer.TryGetCachedPreview(definition, PotionIconCacheSize, PotionIconCacheSize) == null)
                        BmdPreviewRenderer.GetPreview(definition, PotionIconCacheSize, PotionIconCacheSize);
                }
            }
        }

        public Texture2D? GetItemIconByKey(byte group, int id)
            => ResolveItemIcon(ItemDatabase.GetItemDefinition(group, (short)id));

        public int CountItemInInventory(byte group, int id)
            => CountPotionInInventory(group, id);

        private void EnsureConsumableCandidates()
        {
            if (!_consumableCandidatesDirty)
                return;
            BuildPotionCandidates();
            _consumableCandidatesDirty = false;
        }

        private void RestoreQuickSlotsIfNeeded()
        {
            if (_quickSlotsRestored)
                return;

            string? characterName = GetPersistentCharacterName();
            if (string.IsNullOrWhiteSpace(characterName))
                return;

            var learnedSkills = _state.GetSkills().ToDictionary(skill => skill.SkillId);
            if (learnedSkills.Count == 0)
                return;

            if (MuGame.TryLoadQuickSlotAssignments(characterName, out int activeSkillSlot, out ushort?[] savedSkillSlots, out (byte Group, int Id)?[] savedPotionSlots))
            {
                for (int i = PotionSlotCount; i < Math.Min(SlotCount, savedSkillSlots.Length); i++)
                {
                    ushort? skillId = savedSkillSlots[i];
                    if (skillId.HasValue && learnedSkills.TryGetValue(skillId.Value, out var skill))
                    {
                        _slotSkills[i] = skill;
                    }
                }

                for (int i = 0; i < Math.Min(ItemHotbarSlotCount, savedPotionSlots.Length); i++)
                {
                    _potionAssignments[i] = savedPotionSlots[i];
                }

                if (activeSkillSlot >= PotionSlotCount && activeSkillSlot < SlotCount)
                {
                    _activeSkillSlot = activeSkillSlot;
                }
            }

            for (int i = 0; i < _imprintSets[1].Length && i < _hotbarSkills.Length; i++)
                _imprintSets[1][i] = _slotSkills[PotionSlotCount + i]?.SkillId;
            SyncHotbarSkills();

            EnsureActiveSkillSelection(learnedSkills.Values.FirstOrDefault());
            _quickSlotsRestored = true;
        }

        private void EnsureActiveSkillSelection(SkillEntryState? fallbackSkill)
        {
            if (_slotSkills[3] == null && fallbackSkill != null)
            {
                _slotSkills[3] = fallbackSkill;
            }

            if (_activeSkillSlot >= PotionSlotCount &&
                _activeSkillSlot < SlotCount &&
                _slotSkills[_activeSkillSlot] != null)
            {
                return;
            }

            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                if (_slotSkills[i] != null)
                {
                    _activeSkillSlot = i;
                    return;
                }
            }

            _activeSkillSlot = 3;
        }

        private void PersistQuickSlots()
        {
            if (!_quickSlotsRestored)
                return;

            string? characterName = GetPersistentCharacterName();
            if (string.IsNullOrWhiteSpace(characterName))
                return;

            ushort?[] skillIds = new ushort?[SlotCount];
            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                skillIds[i] = _slotSkills[i]?.SkillId;
            }

            MuGame.PersistQuickSlotAssignments(characterName, _activeSkillSlot, skillIds, _potionAssignments);
        }

        private string? GetPersistentCharacterName()
        {
            string? name = _state.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == "???")
                return null;

            return name;
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
            var controls = gs.Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] is T ctrl)
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
            int slotWidth = Math.Max(28, slotSize - 4);
            int slotHeight = Math.Min(innerH, slotSize + 4);

            int totalSlotW = SlotCount * slotWidth + fixedGaps;
            int slotsAreaLeft = contentLeft + barW + barSlotGap;
            int slotsAreaRight = contentRight - barW - barSlotGap;
            int actualSlotsW = slotsAreaRight - slotsAreaLeft;
            int slotStartX = slotsAreaLeft + (actualSlotsW - totalSlotW) / 2;
            int slotY = panelY + (panelH - slotHeight) / 2;

            _slotRects = new Rectangle[SlotCount];
            int sx = slotStartX;
            for (int i = 0; i < SlotCount; i++)
            {
                _slotRects[i] = new Rectangle(sx, slotY, slotWidth, slotHeight);
                sx += slotWidth + slotGap;
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

        private void RefreshCompanionLifeInfos()
        {
            _companionInfos[0] = null;
            _companionInfos[1] = null;

            var items = _state.GetInventoryItems();
            int writeIndex = 0;

            if (TryGetHelperLifeInfo(items, out var helper))
            {
                _companionInfos[writeIndex++] = helper;
            }

            if (writeIndex < _companionInfos.Length && TryGetDarkRavenLifeInfo(items, out var raven))
            {
                _companionInfos[writeIndex] = raven;
            }
        }

        private static bool TryGetHelperLifeInfo(IReadOnlyDictionary<byte, byte[]> items, out CompanionLifeInfo info)
        {
            info = default;

            const byte helperSlot = 8;
            if (!items.TryGetValue(helperSlot, out var helperData) || helperData == null || helperData.Length == 0)
            {
                return false;
            }

            var definition = ItemDatabase.GetItemDefinition(helperData);
            if (definition == null || definition.Group != 13 || !HelperLifeIds.Contains(definition.Id))
            {
                return false;
            }

            int currentLife = ItemDatabase.GetItemDurability(helperData);
            const int maxLife = 255;
            info = new CompanionLifeInfo(
                GetCompanionName(definition.Id, definition.Name),
                currentLife,
                maxLife,
                ResolveCompanionFillColor(currentLife, maxLife));
            return true;
        }

        private static bool TryGetDarkRavenLifeInfo(IReadOnlyDictionary<byte, byte[]> items, out CompanionLifeInfo info)
        {
            info = default;

            // Reference client reads Dark Raven life from weapon-left slot.
            // Keep a fallback check on weapon-right for server slot layout variations.
            Span<byte> candidateSlots = stackalloc byte[] { 1, 0 };

            for (int i = 0; i < candidateSlots.Length; i++)
            {
                byte slot = candidateSlots[i];
                if (!items.TryGetValue(slot, out var itemData) || itemData == null || itemData.Length == 0)
                {
                    continue;
                }

                var definition = ItemDatabase.GetItemDefinition(itemData);
                if (definition == null || definition.Group != 13 || definition.Id != 5)
                {
                    continue;
                }

                int currentLife = ItemDatabase.GetItemDurability(itemData);
                const int maxLife = 255;
                info = new CompanionLifeInfo(
                    GetCompanionName(definition.Id, definition.Name),
                    currentLife,
                    maxLife,
                    ResolveCompanionFillColor(currentLife, maxLife));
                return true;
            }

            return false;
        }

        private static string GetCompanionName(int itemId, string? defaultName)
        {
            return itemId switch
            {
                0 => "Guardian Angel",
                1 => "Imp",
                2 => "Uniria",
                3 => "Dinorant",
                4 => "Dark Horse",
                5 => "Dark Raven",
                _ => string.IsNullOrWhiteSpace(defaultName) ? "Companion" : defaultName
            };
        }

        private static Color ResolveCompanionFillColor(int current, int maximum)
        {
            if (maximum <= 0)
            {
                return CompanionColorDanger;
            }

            float ratio = MathHelper.Clamp(current / (float)maximum, 0f, 1f);
            if (ratio <= 0.2f)
            {
                return CompanionColorDanger;
            }

            if (ratio <= 0.5f)
            {
                return CompanionColorWarn;
            }

            return CompanionColorGood;
        }

        private void DrawCompanionLifeBars(SpriteBatch sb, Texture2D pixel)
        {
            if (_font == null)
            {
                return;
            }

            int count = 0;
            for (int i = 0; i < _companionInfos.Length; i++)
            {
                if (_companionInfos[i].HasValue)
                    count++;
            }

            if (count == 0)
                return;

            int barHeight = 13;
            int barGap = 6;
            int barWidth = Math.Clamp((int)(_panelRect.Width * 0.12f), 120, 156);
            int totalWidth = (count * barWidth) + ((count - 1) * barGap);
            int startX = _panelRect.Center.X - (totalWidth / 2);
            int y = _panelRect.Y + 4;
            int drawn = 0;

            for (int i = 0; i < _companionInfos.Length; i++)
            {
                if (!_companionInfos[i].HasValue)
                    continue;

                var rect = new Rectangle(startX + drawn * (barWidth + barGap), y, barWidth, barHeight);
                DrawCompanionLifeBar(sb, pixel, rect, _companionInfos[i]!.Value);
                drawn++;
            }
        }

        private void DrawCompanionLifeBar(SpriteBatch sb, Texture2D pixel, Rectangle rect, CompanionLifeInfo info)
        {
            sb.Draw(pixel, rect, ModernHudTheme.BorderOuter);

            var track = new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(18, 20, 28, 242),
                new Color(8, 10, 14, 252));

            float lifeRatio = info.Maximum > 0
                ? MathHelper.Clamp(info.Current / (float)info.Maximum, 0f, 1f)
                : 0f;
            int fillWidth = (int)(track.Width * lifeRatio);
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect,
                    Color.Lerp(info.FillColor * 0.55f, ModernHudTheme.BgDark, 0.45f),
                    info.FillColor);
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1), info.FillColor * 0.65f);
            }

            string text = $"{info.Name} {info.Current}/{info.Maximum}";
            float scale = 0.32f;
            Vector2 size = _font!.MeasureString(text) * scale;
            Vector2 textPos = new(
                rect.X + (rect.Width - size.X) * 0.5f,
                rect.Y + (rect.Height - size.Y) * 0.5f);

            sb.DrawString(_font, text, textPos + Vector2.One,
                Color.Black * 0.8f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, text, textPos,
                ModernHudTheme.TextWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
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
            var iconBounds = new Rectangle(dest.X + pad, dest.Y + pad,
                Math.Max(1, dest.Width - pad * 2), Math.Max(1, dest.Height - pad * 2));

            float fitScale = MathF.Min(
                iconBounds.Width / (float)SkillIconAtlas.IconWidth,
                iconBounds.Height / (float)SkillIconAtlas.IconHeight);

            int drawW = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconWidth * fitScale));
            int drawH = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconHeight * fitScale));

            var iconDest = new Rectangle(
                iconBounds.X + (iconBounds.Width - drawW) / 2,
                iconBounds.Y + (iconBounds.Height - drawH) / 2,
                drawW,
                drawH);
            sb.Draw(tex, iconDest, frame.SourceRectangle, Color.White);

            DrawSkillCooldownOverlay(sb, iconDest, skill);
            DrawSkillCooldownTimer(sb, iconDest, skill);
        }

        private void DrawSkillCooldownOverlay(SpriteBatch spriteBatch, Rectangle iconRect, SkillEntryState skill)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
                return;

            double now = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            float ratio = SkillCooldownTracker.GetCooldownRatio(skill.SkillId, now);
            if (ratio <= 0f)
                return;

            int overlayHeight = Math.Max(1, (int)(iconRect.Height * ratio));
            var overlayRect = new Rectangle(iconRect.X, iconRect.Y, iconRect.Width, overlayHeight);

            spriteBatch.Draw(pixel, overlayRect, new Color(0, 0, 0, 160) * Alpha);
            spriteBatch.Draw(
                pixel,
                new Rectangle(overlayRect.X, overlayRect.Y + overlayHeight - 1, overlayRect.Width, 1),
                ModernHudTheme.Accent * 0.5f * Alpha);
        }

        private void DrawSkillCooldownTimer(SpriteBatch spriteBatch, Rectangle iconRect, SkillEntryState skill)
        {
            if (_font == null)
                return;

            double now = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            int remainingMs = SkillCooldownTracker.GetRemainingMs(skill.SkillId, now);
            if (remainingMs <= 0)
                return;

            string timerText = remainingMs >= 1000
                ? $"{(remainingMs + 999) / 1000}"
                : $"{(remainingMs + 99) / 100f:F1}";

            const float textScale = 0.6f;
            Vector2 textSize = _font.MeasureString(timerText) * textScale;
            float tx = iconRect.X + (iconRect.Width - textSize.X) * 0.5f;
            float ty = iconRect.Y + (iconRect.Height - textSize.Y) * 0.5f;

            spriteBatch.DrawString(_font, timerText, new Vector2(tx + 1f, ty + 1f),
                Color.Black * 0.85f * Alpha, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, timerText, new Vector2(tx, ty),
                ModernHudTheme.TextWhite * Alpha, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
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
            _consumableCandidateView.Clear();

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
                var candidate = new PotionCandidate(
                    kvp.Key.Item1, kvp.Key.Item2,
                    kvp.Value.Name, kvp.Value.TexturePath,
                    kvp.Value.Count, kvp.Value.FirstSlot);
                _potionCandidates.Add(candidate);
                _consumableCandidateView.Add((candidate.Group, candidate.Id, candidate.Name, candidate.TexturePath, candidate.Count));
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
