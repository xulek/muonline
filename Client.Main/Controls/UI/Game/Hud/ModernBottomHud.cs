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
    public sealed partial class ModernBottomHud : UIControl
    {
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
        private uint _lastCurrentMana = uint.MaxValue;
        private uint _lastMaximumMana = uint.MaxValue;
        private string _healthText = string.Empty, _healthMaxText = string.Empty;
        private string _manaText = string.Empty, _manaMaxText = string.Empty;
        private const float LerpSpeed = 6f;

        // Layout rects (recomputed on resize)
        private Rectangle _panelRect;
        private Rectangle _hpBarRect, _sdBarRect, _mpBarRect, _agBarRect;
        private Rectangle _expBarRect;
        private Rectangle[] _slotRects = Array.Empty<Rectangle>();
        private Rectangle[] _btnRects = Array.Empty<Rectangle>();
        private float _slotFontScale;
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
        private static readonly string[] ButtonLabels = { "MENU", "CHAR", "INV", "PARTY", "GUILD", "FRIENDS" };
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
            _skillPanel.HotbarKeyOf = HotbarKeyOf;
            _state.InventoryChanged += OnInventoryChanged;
            _state.KeyConfigurationReceived += OnServerKeyConfigurationReceived;

            RefreshLayout();
        }

        private void OnInventoryChanged()
        {
            _consumableCandidatesDirty = true;
        }

        public override void Dispose()
        {
            _state.InventoryChanged -= OnInventoryChanged;
            _state.KeyConfigurationReceived -= OnServerKeyConfigurationReceived;
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
            UpdateOrbTrails(dt);

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

                DrawFrame(spriteBatch);
                DrawCompanionLifeBars(spriteBatch, pixel);

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

            if (IsOverFrame(new Point(mousePos.X, mousePos.Y)))
                return true;

            return false;
        }

        /// <summary>Orb captions, rebuilt only when the values change.</summary>
        private void RefreshResourceTexts()
        {
            if (_lastCurrentHealth != _state.CurrentHealth || _lastMaximumHealth != _state.MaximumHealth)
            {
                _lastCurrentHealth = _state.CurrentHealth;
                _lastMaximumHealth = _state.MaximumHealth;
                _healthText = _lastCurrentHealth.ToString();
                _healthMaxText = "/ " + _lastMaximumHealth;
            }

            if (_lastCurrentMana != _state.CurrentMana || _lastMaximumMana != _state.MaximumMana)
            {
                _lastCurrentMana = _state.CurrentMana;
                _lastMaximumMana = _state.MaximumMana;
                _manaText = _lastCurrentMana.ToString();
                _manaMaxText = "/ " + _lastMaximumMana;
            }
        }

        private void HandleKeyboard()
        {
            var kb = MuGame.Instance.Keyboard;
            var prev = MuGame.Instance.PrevKeyboard;

            // Typing in a text field (chat, shop price, trade amount...): the keys are text, not hotbar keys.
            if (Scene?.IsTextInputActive == true)
                return;

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
            // Hovers only where no window lies on top of the HUD.
            var mousePos = PointerForHitTest(MuGame.Instance.UiMouseState.Position);
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

        /// <summary>Key label of the skill slot holding <paramref name="skillId"/>, or null.</summary>
        private string? HotbarKeyOf(ushort skillId)
        {
            for (int i = PotionSlotCount; i < _slotSkills.Length; i++)
                if (_slotSkills[i]?.SkillId == skillId)
                    return SlotKeyLabels[i];
            return null;
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
            _skillPanel.TargetSlotLabel = SlotKeyLabels[_pendingAssignSlot];
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

            _restoredFromLocal = MuGame.TryLoadQuickSlotAssignments(characterName, out int activeSkillSlot, out ushort?[] savedSkillSlots, out (byte Group, int Id)?[] savedPotionSlots);
            if (_restoredFromLocal)
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

            else if (_state.ServerKeyConfiguration is { } serverConfig && serverConfig.HasAnyAssignment())
            {
                ApplyServerKeyConfiguration(serverConfig, learnedSkills);
            }

            for (int i = 0; i < _imprintSets[1].Length && i < _hotbarSkills.Length; i++)
                _imprintSets[1][i] = _slotSkills[PotionSlotCount + i]?.SkillId;
            SyncHotbarSkills();

            EnsureActiveSkillSelection(learnedSkills.Values.FirstOrDefault());
            _quickSlotsRestored = true;
        }

        private bool _restoredFromLocal;

        /// <summary>
        /// Uses the key configuration stored on the server (0xF3/0x30, original ReceiveOption) when this machine
        /// has no saved quick slots for the character: skill hotkeys 1..0 and the Q/W/E/R potions.
        /// </summary>
        private void ApplyServerKeyConfiguration(KeyConfiguration config, Dictionary<ushort, SkillEntryState> learnedSkills)
        {
            for (int i = 0; i < KeyConfiguration.SkillHotkeyCount && PotionSlotCount + i < SlotCount; i++)
            {
                ushort skillId = config.SkillHotkeys[i];
                if (skillId != KeyConfiguration.EmptySkill && learnedSkills.TryGetValue(skillId, out var skill))
                    _slotSkills[PotionSlotCount + i] = skill;
            }

            for (int i = 0; i < 4 && i < ItemHotbarSlotCount; i++)
            {
                byte potion = config.PotionHotkeys[i];
                if (potion != 0xFF)
                    _potionAssignments[i] = (14, potion);
            }
        }

        private void OnServerKeyConfigurationReceived(KeyConfiguration config)
        {
            // Arrived after the local restore: only apply when nothing was saved on this machine.
            if (!_quickSlotsRestored || _restoredFromLocal || !config.HasAnyAssignment())
                return;

            var learnedSkills = _state.GetSkills().ToDictionary(skill => skill.SkillId);
            ApplyServerKeyConfiguration(config, learnedSkills);
            for (int i = 0; i < _imprintSets[1].Length && i < _hotbarSkills.Length; i++)
                _imprintSets[1][i] = _slotSkills[PotionSlotCount + i]?.SkillId;
            SyncHotbarSkills();
            EnsureActiveSkillSelection(learnedSkills.Values.FirstOrDefault());
        }

        /// <summary>Saves the hotkeys on the server too, like SaveOptions in the original client.</summary>
        private void SaveKeyConfigurationOnServer()
        {
            var config = new KeyConfiguration();
            if (_state.ServerKeyConfiguration is { } previous)
            {
                config.GameOptions = previous.GameOptions;
                config.ChatLogBox = previous.ChatLogBox;
            }

            for (int i = 0; i < KeyConfiguration.SkillHotkeyCount && PotionSlotCount + i < SlotCount; i++)
                config.SkillHotkeys[i] = _slotSkills[PotionSlotCount + i]?.SkillId ?? KeyConfiguration.EmptySkill;
            for (int i = 0; i < 4 && i < ItemHotbarSlotCount; i++)
                config.PotionHotkeys[i] = _potionAssignments[i] is { Group: 14 } potion && potion.Id is >= 0 and < 0xFF ? (byte)potion.Id : (byte)0xFF;

            var bytes = config.ToBytes();
            if (_state.ServerKeyConfiguration != null && bytes.AsSpan().SequenceEqual(_state.ServerKeyConfiguration.ToBytes()))
                return;
            _state.SetServerKeyConfiguration(config);
            _ = MuGame.Network?.GetCharacterService()?.SendSaveKeyConfigurationAsync(bytes);
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
            _restoredFromLocal = true;
            SaveKeyConfigurationOnServer();
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
                case 4: gs.GuildInfo?.Toggle(); break;
                case 5: gs.FriendList?.Toggle(); break;
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
            int y = _panelRect.Y - barHeight - 34; // above the bar's top ornaments
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
