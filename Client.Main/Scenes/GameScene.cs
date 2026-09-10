// GameScene.cs
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Worlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Effects.Skills;
using Client.Main.Core.Utilities;
using Client.Main.Networking.PacketHandling.Handlers; // For CharacterClassNumber
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.Map;
using Client.Main.Controls.UI.Game.Party;
using Client.Main.Controls.UI.Game.PauseMenu;
using Client.Main.Controls.UI.Game.Character;
using Client.Main.Controls.UI.Game.Trade;
using Client.Main.Controls.UI.Game.Quest;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Networking;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Client.Main.Controls.UI.Game.Buffs;
using Client.Main.Controls.UI.Game.Hud;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Controllers;
using Client.Main.Helpers;

namespace Client.Main.Scenes
{
    public class GameScene : BaseScene
    {
        // ──────────────────────────── Fields ────────────────────────────
        private readonly HeroObject _hero;
        private ModernBottomHud _modernHud;
        private BottomBarControl _classicBottomBar;
        private TouchActionButtonsControl _classicTouchActions;
        private TouchMenuControl _classicTouchMenu;
        private VirtualJoystickControl _classicJoystick;
        private SkillImprintControl _classicSkillImprint;
        private PotionImprintControl _classicPotionImprint;
        private Controls.UI.Game.Skills.MasterSkillTreeControl _masteryTree;
        private MasteryTreeControl _classicMasteryTree;
        private EquipmentDurabilityHud _equipmentDurabilityHud;
        private GameSceneMapController _mapController;
        private MapListControl _mapListControl;
        private ChatLogWindow _chatLog;
        private MoveCommandWindow _moveCommandWindow;
        private ChatInputBoxControl _chatInput;
        private InventoryControl _inventoryControl;
        private Controls.UI.NotificationManager _notificationManager;
        private PartyPanelControl _partyPanel;
        private readonly (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) _characterInfo;
        private CharacterInfoWindowControl _characterInfoWindow;
        private MiniMapControl _miniMap;
        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<GameScene>() ?? NullLogger<GameScene>.Instance;
        private LabelControl _pingLabel; // Displays current ping
        private LabelControl _fpsLabel; // Displays current FPS independently of DebugPanel
        private double _pingTimer = 0;
        private double _fpsTimer = 0;
        private int? _lastPingValue = null;
        private int _lastFpsValue = -1;
        private PauseMenuControl _pauseMenu; // ESC menu
        // (SkillQuickSlot removed — replaced by ModernBottomHud)
        private Controls.UI.Game.Skills.SkillSelectionPanel _skillSelectionPanel; // Skill selection panel (independent)
        private bool _modernSkillSelectionWasVisible;
        private bool _modernSkillSelectionWasInteractive;
        private bool _modernMasteryWasVisible;
        private bool _modernMasteryWasInteractive;
        private bool _uiThemeVisibilityInitialized;
        private UiThemeId _lastAppliedUiTheme;
        private CurrentLocationControl _currentLocationControl; // Current map + coordinates (top-left)
        private ActiveBuffsPanel _activeBuffsPanel; // Active buffs display (top-left corner)
        private Texture2D _backgroundTexture;
        private ProgressBarControl _progressBar;
        private GameSceneSkillController _skillController;
        private GameSceneNotificationController _notificationController;
        private GameScenePlayerMenuController _playerMenuController;
        private GameSceneHotkeys _hotkeys;
        private GameSceneScopeImportController _scopeImportController;
        private GameSceneObjectEditorController _objectEditorController;
        private GameSceneDuelController _duelController;
        private GameSceneChatController _chatController;
        private GameSceneUiPreloadController _uiPreloadController;
        private GameSceneWindowCloseController _windowCloseController;
        private Task _sceneShellInitializationTask;
        private Task _firstPresentedFramePreparationTask;
        private LoadingScreenControl _initialLoadingScreen;
        private bool _sceneShellInitialized;
        private Action _pendingWorldActivation;
        private TaskCompletionSource<bool> _pendingWorldActivationCompletion;
        private bool _pendingWorldActivationScheduled;
        private bool _pendingWorldActivationCleansLoadingUi;
        private string _pendingWorldActivationName;
        private bool _initialWorldActivationCooldown;
        private bool _initialWorldLoadInProgress = true;

        // Performance optimization fields - track object IDs for O(1) lookups
        // ───────────────────────── Properties ─────────────────────────
        public HeroObject Hero => _hero;
        public ChatLogWindow ChatLog => _chatLog;
        public InventoryControl InventoryControl => _inventoryControl;
        public TradeControl TradeControl => TradeControl.Instance;
        public PauseMenuControl PauseMenu => _pauseMenu;
        internal ModernBottomHud ModernHud => _modernHud;
        internal GameSceneSkillController SkillController => _skillController;
        internal Controls.UI.Game.Skills.MasterSkillTreeControl MasteryTree => _masteryTree;
        internal MasteryTreeControl ClassicMasteryTree => _classicMasteryTree;

        public override bool CanRenderWhileInitializing => true;

        public static readonly IReadOnlyDictionary<byte, Type> MapWorldRegistry = DiscoverWorlds();

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "World registry uses reflection; trimming is not supported for scene discovery.")]
        private static IReadOnlyDictionary<byte, Type> DiscoverWorlds()
        {
            var registry = new Dictionary<byte, Type>();
            var worldTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(WalkableWorldControl).IsAssignableFrom(t));

            foreach (var type in worldTypes)
            {
                var attr = type.GetCustomAttribute<WorldInfoAttribute>();
                if (attr != null)
                {
                    if (!registry.TryAdd((byte)attr.MapId, type))
                    {
                        // Optionally log a warning about duplicate MapId
                    }
                }
            }
            return registry;
        }

        // ──────────────────────── Constructors ────────────────────────
        public GameScene((string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) characterInfo)
        {
            _characterInfo = characterInfo;
            _logger?.LogDebug(
                "GameScene shell created for Character: {Name} ({Class})",
                _characterInfo.Name,
                _characterInfo.Class);

            // Keep the constructor intentionally small. Scene construction happens inside a
            // main-thread dispatcher action, so building the full HUD here previously made
            // HandleEnteredGame block the game for roughly 150 ms.
            _hero = new HeroObject(new AppearanceData(characterInfo.Appearance));
        }

        public override Task PrepareForFirstPresentedFrameAsync()
        {
            _firstPresentedFramePreparationTask ??= PrepareFirstPresentedFrameCoreAsync();
            return _firstPresentedFramePreparationTask;
        }

        private async Task PrepareFirstPresentedFrameCoreAsync()
        {
            // Build only scene-owned loading resources here. Shared/singleton game controls are
            // attached after the previous GameScene has been disposed, so a GameScene-to-GameScene
            // fallback cannot detach or reset controls which already belong to the new scene.
            if (_backgroundTexture == null)
            {
                try
                {
                    _backgroundTexture = MuGame.Instance.Content.Load<Texture2D>("Background");
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("[GameScene] Background load failed: {Message}", ex.Message);
                }
            }

            if (_progressBar == null)
            {
                _progressBar = new ProgressBarControl
                {
                    Progress = 0.01f,
                    StatusText = "Preparing game interface...",
                    Visible = true
                };
                Controls.Add(_progressBar);
            }

            if (_initialLoadingScreen == null)
            {
                _initialLoadingScreen = new LoadingScreenControl
                {
                    Visible = true,
                    Message = "Preparing game interface...",
                    Progress = 0.01f
                };
                Controls.Add(_initialLoadingScreen);
            }

            if (_progressBar.Status == GameControlStatus.NonInitialized)
                await _progressBar.Initialize();

            if (_initialLoadingScreen.Status == GameControlStatus.NonInitialized)
                await _initialLoadingScreen.Initialize();
        }

        public override async Task InitializeWithProgressReporting(Action<string, float> progressCallback)
        {
            await PrepareForFirstPresentedFrameAsync();

            Action<string, float> effectiveProgressCallback = progressCallback ?? UpdateLoadProgress;
            _sceneShellInitializationTask ??= InitializeSceneShellAsync(effectiveProgressCallback);
            await _sceneShellInitializationTask;

            await MuGame.YieldToNextFrameAsync(
                "GameScene.InitializeControls",
                MainThreadDispatcher.WorkPriority.High);
            await base.InitializeWithProgressReporting(effectiveProgressCallback);
        }

        private async Task InitializeSceneShellAsync(Action<string, float> progressCallback)
        {
            void Report(string message, float progress)
            {
                progressCallback?.Invoke(message, progress);

                var loading = _initialLoadingScreen ?? _mapController?.LoadingScreen;
                if (loading != null)
                {
                    loading.Message = message;
                    loading.Progress = progress;
                }

                if (_progressBar != null)
                {
                    _progressBar.StatusText = message;
                    _progressBar.Progress = progress;
                }
            }

            Report("Preparing game interface...", 0.01f);

            // Phase 1: controls required by the loading and messaging paths.
            Controls.Add(NpcShopControl.Instance);
            Controls.Add(VaultControl.Instance);
            Controls.Add(ChaosMixControl.Instance);
            Controls.Add(TradeControl.Instance);
            Controls.Add(QuestDialogControl.Instance);
            Controls.Add(DevilSquareEnterControl.Instance);
            Controls.Add(BloodCastleEnterControl.Instance);
            Controls.Add(BloodCastleTimeControl.Instance);
            Controls.Add(BloodCastleResultControl.Instance);

            _mapListControl = new MapListControl { Visible = false };
            _chatLog = new ChatLogWindow
            {
                X = 5
            };
            Controls.Add(_chatLog);

            _chatInput = new ChatInputBoxControl(_chatLog, MuGame.AppLoggerFactory)
            {
                X = 5
            };
            Controls.Add(_chatInput);
            ApplyChatThemeLayout();
            _duelController = new GameSceneDuelController(this, _chatLog, _logger);

            _notificationManager = new Controls.UI.NotificationManager();
            Controls.Add(_notificationManager);
            _notificationManager.BringToFront();
            _notificationController = new GameSceneNotificationController(_notificationManager, _chatLog);
            _notificationController.AddPending(ChatMessageHandler.TakePendingServerMessages());
            _scopeImportController = new GameSceneScopeImportController(this, _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Inventory",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 2: inventory and common windows.
            _inventoryControl = new InventoryControl(MuGame.Network, MuGame.AppLoggerFactory);
            Controls.Add(_inventoryControl);
            _inventoryControl.HookEvents();
            _windowCloseController = new GameSceneWindowCloseController(_inventoryControl, _logger);

            _moveCommandWindow = new MoveCommandWindow(MuGame.AppLoggerFactory, MuGame.Network);
            Controls.Add(_moveCommandWindow);
            _moveCommandWindow.MapWarpRequested += OnMapWarpRequested;

            _characterInfoWindow = new CharacterInfoWindowControl { X = 20, Y = 50, Visible = false };
            Controls.Add(_characterInfoWindow);
            _miniMap = new MiniMapControl(this);
            Controls.Add(_miniMap);
            _partyPanel = new PartyPanelControl();
            Controls.Add(_partyPanel);

            _fpsLabel = new LabelControl
            {
                Text = "FPS: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = 5, Right = 5 },
                FontSize = 10,
                TextColor = Color.LightGreen
            };
            Controls.Add(_fpsLabel);

            _pingLabel = new LabelControl
            {
                Text = "Ping: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = 22, Right = 5 },
                FontSize = 10,
                TextColor = Color.White
            };
            Controls.Add(_pingLabel);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Hud",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 3: HUD and interaction controllers.
            var characterState = MuGame.Network.GetCharacterState();
            _pauseMenu = new PauseMenuControl();
            Controls.Add(_pauseMenu);

            _skillSelectionPanel = new Controls.UI.Game.Skills.SkillSelectionPanel();
            Controls.Add(_skillSelectionPanel);

            _modernHud = new ModernBottomHud(characterState, _skillSelectionPanel);
            Controls.Add(_modernHud);

            // Classic owns a separate HUD tree. The controls are created once and only their
            // visibility changes, so switching themes cannot leave either HUD underneath.
            _classicBottomBar = new BottomBarControl(characterState, _modernHud);
            _classicTouchActions = new TouchActionButtonsControl();
            _classicJoystick = new VirtualJoystickControl();
            _classicSkillImprint = new SkillImprintControl(characterState, _modernHud);
            _classicPotionImprint = new PotionImprintControl(characterState, _modernHud);
            _classicTouchMenu = new TouchMenuControl
            {
                HotbarToHide = _classicTouchActions,
                ImprintPanel = _classicSkillImprint,
                PotionPanel = _classicPotionImprint
            };
            Controls.Add(_classicBottomBar);
            Controls.Add(_classicTouchActions);
            Controls.Add(_classicJoystick);
            Controls.Add(_classicSkillImprint);
            Controls.Add(_classicPotionImprint);
            Controls.Add(_classicTouchMenu);

            _masteryTree = new Controls.UI.Game.Skills.MasterSkillTreeControl();
            _classicMasteryTree = new MasteryTreeControl(characterState);
            Controls.Add(_masteryTree);
            Controls.Add(_classicMasteryTree);
            _equipmentDurabilityHud = new EquipmentDurabilityHud(characterState);
            Controls.Add(_equipmentDurabilityHud);
            _skillController = new GameSceneSkillController(
                this,
                _modernHud,
                _logger,
                _duelController.IsDuelAttackTarget);
            ApplyUiThemeVisibility();

            _currentLocationControl = new CurrentLocationControl(characterState);
            Controls.Add(_currentLocationControl);
            _activeBuffsPanel = new ActiveBuffsPanel(characterState, _currentLocationControl);
            Controls.Add(_activeBuffsPanel);

            var duelHud = new DuelHudControl(characterState);
            Controls.Add(duelHud);
            Controls.Add(DevilSquareCountdownControl.Instance);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Controllers",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 4: interaction controllers.
            _playerMenuController = new GameScenePlayerMenuController(
                this,
                StartWhisperToPlayer,
                _duelController.OnDuelRequestedFromContextMenu);
            _playerMenuController.Initialize();
            _objectEditorController = new GameSceneObjectEditorController(this, _logger);
            _objectEditorController.Initialize();
            _hotkeys = new GameSceneHotkeys(
                this,
                _pauseMenu,
                _playerMenuController,
                _moveCommandWindow,
                _inventoryControl,
                _characterInfoWindow,
                _miniMap,
                _chatInput,
                _chatLog,
                _objectEditorController,
                _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.LoadingInfrastructure",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 5: complete the loading infrastructure prepared before scene activation.
            // The background and progress controls already exist, so no active frame can expose
            // only the cleared render target while this heavier shell is being assembled.
            _mapController = new GameSceneMapController(
                this,
                _modernHud,
                _progressBar,
                _chatLog,
                _chatInput,
                _mapListControl,
                DebugPanel,
                Cursor,
                _scopeImportController,
                _logger,
                _initialLoadingScreen);
            _initialLoadingScreen = null;
            _mapController.EnsureLoadingScreen();
            _chatController = new GameSceneChatController(_mapController, _duelController, _chatLog, _logger);
            _chatInput.MessageSendRequested += _chatController.OnChatMessageSendRequested;
            _uiPreloadController = new GameSceneUiPreloadController(this, _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Ordering",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 6: z-order changes are separated because BringToFront mutates the controls
            // collection and repeatedly recalculates ordering.
            _fpsLabel.BringToFront();
            _pingLabel.BringToFront();
            _chatInput.BringToFront();
            _pauseMenu.BringToFront();
            _modernHud.BringToFront();
            _classicBottomBar.BringToFront();
            _classicTouchActions.BringToFront();
            _classicJoystick.BringToFront();
            _classicSkillImprint.BringToFront();
            _classicPotionImprint.BringToFront();
            _classicMasteryTree.BringToFront();
            _classicTouchMenu.BringToFront();
            _equipmentDurabilityHud.BringToFront();
            _currentLocationControl.BringToFront();
            _activeBuffsPanel.BringToFront();
            duelHud.BringToFront();
            DevilSquareCountdownControl.Instance.BringToFront();
            DebugPanel.BringToFront();
            Cursor.BringToFront();

            // Subscribe after the complete UI tree exists. ThemeChanged is synchronous, so
            // this makes control layout updates finish before the scene repositions dependent
            // controls such as the chat log and input box.
            UiThemeManager.ThemeChanged += HandleUiThemeChanged;
            _sceneShellInitialized = true;
            Report("Game interface prepared.", 0.04f);

            // Optional assets are deliberately not awaited by the scene transition.
            _ = _uiPreloadController.StartPreloadAsync();
        }

        public GameScene() : this(GetCharacterInfoFromState())
        {
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
            ApplyChatThemeLayout();
        }

        public GameScene((string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) characterInfo, NetworkManager networkManager)
            : this(characterInfo)
        {
            // Optionally store networkManager if needed in the future
        }

        private static (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) GetCharacterInfoFromState()
        {
            var state = MuGame.Network?.GetCharacterState();
            if (state != null)
            {
                return (state.Name ?? "Unknown", state.Class, state.Level, Array.Empty<byte>());
            }
            return ("Unknown", CharacterClassNumber.DarkKnight, 1, Array.Empty<byte>());
        }

        // ───────────────────── Content Loading (Progressive) ─────────────────────
        private void UpdateLoadProgress(string message, float progress)
        {
            if (MuGame.IsMainThread)
            {
                _mapController?.UpdateLoadProgress(message, progress);
                return;
            }

            MuGame.ScheduleOnMainThread(
                () => _mapController?.UpdateLoadProgress(message, progress),
                MainThreadDispatcher.WorkPriority.High,
                "GameScene.UpdateLoadProgress");
        }

        protected override async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            WorldControl worldInstance = null;
            try
            {
                UpdateLoadProgress("Initializing Game Scene...", 0.0f);

                var charState = MuGame.Network?.GetCharacterState();
                if (charState == null)
                {
                    UpdateLoadProgress("Error: CharacterState is null.", 1.0f);
                    _logger?.LogDebug("CharacterState is null in GameScene.Load, cannot proceed.");
                    _modernHud.Visible = false;
                    return;
                }

                // Phase 1: apply the small, data-only hero state.
                UpdateLoadProgress("Setting up hero info...", 0.05f);
                _hero.CharacterClass = _characterInfo.Class;
                _hero.Name = _characterInfo.Name;
                charState.UpdateCoreCharacterInfo(
                    charState.Id,
                    _characterInfo.Name,
                    _characterInfo.Class,
                    _characterInfo.Level,
                    charState.PositionX,
                    charState.PositionY,
                    charState.MapId);
                _hero.NetworkId = charState.Id;
                _hero.Location = new Vector2(charState.PositionX, charState.PositionY);
                if (_windowCloseController != null)
                {
                    _hero.PlayerMoved += _windowCloseController.OnHeroMoved;
                    _hero.PlayerTookDamage += _windowCloseController.OnHeroTookDamage;
                }

                Type initialWorldType = typeof(LorenciaWorld);
                if (MapWorldRegistry.TryGetValue((byte)charState.MapId, out Type mappedType))
                    initialWorldType = mappedType;
                else
                    _logger?.LogDebug("Unknown MapId {MapId}. Defaulting to Lorencia.", charState.MapId);

                await MuGame.YieldToNextFrameAsync(
                    $"GameScene.Load.CreateWorld.{initialWorldType.Name}",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 2: create a hidden world shell. Keeping it hidden prevents the renderer
                // from cold-starting terrain, culling and model buffers before loading completes.
                UpdateLoadProgress($"Creating world: {initialWorldType.Name}...", 0.20f);
                if (World != null)
                {
                    Controls.Remove(World);
                    World.Dispose();
                    World = null;
                }

                worldInstance = (WorldControl)Activator.CreateInstance(initialWorldType);
                worldInstance.Visible = false;
                Controls.Add(worldInstance);
                World = worldInstance;

                if (worldInstance is WalkableWorldControl walkable)
                {
                    walkable.Walker = _hero;
                    _scopeImportController?.EnsureWalkerNetworkId(walkable, charState.Id, "initial world shell");
                }

                _hero.World = worldInstance;

                await MuGame.YieldToNextFrameAsync(
                    $"GameScene.Load.InitializeWorld.{initialWorldType.Name}",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 3: initialize the hidden world. Any unavoidable cold I/O is now isolated
                // to a named transition phase and cannot be combined with hero publication.
                UpdateLoadProgress($"Loading world: {initialWorldType.Name}...", 0.30f);
                await worldInstance.Initialize();
                UpdateLoadProgress($"World {initialWorldType.Name} initialized.", 0.60f);

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.HeroAssets",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 4: load the hero before adding it to the live object collection.
                UpdateLoadProgress("Loading hero assets...", 0.65f);
                if (_hero.Status == GameControlStatus.NonInitialized ||
                    _hero.Status == GameControlStatus.Initializing)
                {
                    await _hero.Load();
                }

                // Asset loading may complete on the thread pool. Marshal back before touching
                // model buffers and split prewarm from publication into separate frames.
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PrepareHero",
                    MainThreadDispatcher.WorkPriority.Critical);
                _scopeImportController?.EnsureHeroNetworkId(charState.Id, "after hero Load()");
                _hero.SnapToTerrainHeight(updateCamera: false);
                await _hero.PrepareGpuTexturesForFirstFrameAsync();
                _hero.PrepareRenderResourcesForFirstFrame();
                await CharacterSpawnEffect.PreloadAsync();

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PublishHero",
                    MainThreadDispatcher.WorkPriority.Critical);

                if (!worldInstance.Objects.Contains(_hero))
                {
                    worldInstance.Objects.Add(_hero);
                    CharacterSpawnEffect.Start(_hero);
                }
                if (worldInstance is WalkableWorldControl initializedWalkable)
                    _scopeImportController?.EnsureWalkerNetworkId(initializedWalkable, charState.Id, "after hero publication");

                // Phase 5: queue each scope category in a separate frame. Remote objects load
                // asynchronously and are published only after their own assets are ready.
                UpdateLoadProgress("Importing nearby players...", 0.80f);
                await (_scopeImportController?.ImportPendingRemotePlayersAsync() ?? Task.CompletedTask);
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.ImportNpcsMonsters",
                    MainThreadDispatcher.WorkPriority.High);

                UpdateLoadProgress("Importing nearby NPCs and monsters...", 0.86f);
                await (_scopeImportController?.ImportPendingNpcsMonstersAsync() ?? Task.CompletedTask);
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.ImportDroppedItems",
                    MainThreadDispatcher.WorkPriority.High);

                UpdateLoadProgress("Importing dropped items...", 0.90f);
                await (_scopeImportController?.ImportPendingDroppedItemsAsync() ?? Task.CompletedTask);

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PrepareVisibility",
                    MainThreadDispatcher.WorkPriority.High);

                // Build the first visibility snapshot while the loading screen is still active.
                // This moves the initial spatial/culling rebuild out of the first gameplay frame.
                await worldInstance.PrepareInitialRenderResourcesAsync(
                    "GameScene.Load.PrewarmModel");

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PreloadSounds",
                    MainThreadDispatcher.WorkPriority.Low);
                UpdateLoadProgress("Preloading sounds...", 0.96f);
                await PreloadSoundsAsync();

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.Finalize",
                    MainThreadDispatcher.WorkPriority.Critical);

                if (worldInstance is WalkableWorldControl finalWalkable)
                    _scopeImportController?.EnsureWalkerNetworkId(finalWalkable, charState.Id, "final verification");

                _mapController?.UpdateLoadProgress("Preparing first frame...", 0.99f);
                _ = QueueWorldActivationAfterLoadingFrame(() =>
                {
                    _hero.SnapToTerrainHeight();
                    worldInstance.Visible = true;
                    _modernHud.Visible = true;
                    _mapController?.UpdateLoadProgress("Game ready!", 1.0f);
                    ScheduleMapNameUpdateNextFrame("GameScene.UpdateInitialMapName");
                    _ = RefreshMiniMapAsync();
                }, "GameScene.ActivateInitialWorld");

                // Complete this nested async workflow outside the dispatcher action which ran
                // GameScene.Load.Finalize. This prevents parent scene-initialization continuations
                // from being charged to (and executed inside) the same frame-budgeted action.
                await Task.Yield();
            }
            finally
            {
                // Activation owns loading-screen cleanup. On failures there is no queued
                // activation, so release the loading UI immediately.
                if (_pendingWorldActivation == null)
                {
                    _initialWorldLoadInProgress = false;
                    _mapController?.DisposeLoadingScreen();
                    if (_progressBar != null)
                        _progressBar.Visible = false;
                }
            }
        }

        public override async Task Load()
        {
            // This method is called by BaseScene.Initialize() if LoadSceneContentWithProgress is not overridden,
            // OR if the overridden method calls base.Load().
            // For GameScene, we want the progressive loading, so we'll call it from here if this Load is hit.
            // However, with the new structure, InitializeWithProgressReporting should call LoadSceneContentWithProgress directly.
            // This is a fallback / ensures old paths might still work or for clarity.
            if (Status == GameControlStatus.Initializing) // Check if we are already in the new init flow
            {
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
            else
            {
                // Fallback to old behavior or log a warning
                _logger?.LogDebug("GameScene.Load() called outside of InitializeWithProgressReporting flow. Consider refactoring.");
                await base.Load(); // Which is empty in BaseScene, then calls derived GameScene's old Load logic
            }
        }

        private async void OnMapWarpRequested(int mapIndex, string mapDisplayName)
        {
            _logger?.LogDebug($"Player requested warp to map index: {mapIndex}");
            var mapName = mapDisplayName;
            _chatLog.AddMessage("System", $"Warping to {mapName} (ID {mapIndex})...", MessageType.System);

            try
            {
                await MuGame.Network.SendWarpRequestAsync((ushort)mapIndex);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, $"Error sending warp request for map index {mapIndex}.");
                _chatLog.AddMessage("System", $"Error warping: {ex.Message}", MessageType.Error);
            }
        }

        // ─────────────────── Map Change Logic (Remains largely the same) ───────────────────
        public async Task ChangeMap([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type worldType)
        {
            if (_mapController != null)
            {
                await _mapController.ChangeMap(worldType);
            }
        }

        public async Task ChangeMap<T>() where T : WalkableWorldControl, new()
        {
            await ChangeMap(typeof(T));
        }

        // ─────────────────── Notification Handling ───────────────────
        public void ShowNotificationMessage(ServerMessage.MessageType messageType, string message)
        {
            _notificationController?.Enqueue(messageType, message);
        }

        // ─────────────────────────── Update Loop ───────────────────────────
        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
            {
                _mapController?.UpdateLoading(gameTime);
                return;
            }

            if (_initialWorldLoadInProgress ||
                _mapController?.IsChangingWorld == true ||
                _pendingWorldActivation != null ||
                _initialWorldActivationCooldown ||
                World == null ||
                !World.Visible ||
                World.Status != GameControlStatus.Ready)
            {
                _mapController?.UpdateLoading(gameTime);
                return;
            }

            var currentKeyboardState = MuGame.Instance.Keyboard;
            var previousKeyboardState = MuGame.Instance.PrevKeyboard;

            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            long buffsStarted = UpdatePassProfiler.Start();
            MuGame.Network?.UpdateBuffs();
            MuGame.Network?.GetCharacterState()?.ExpireActiveBuffs();
            _hotkeys?.HandleGlobal(currentKeyboardState, previousKeyboardState);
            UpdatePassProfiler.AddGameBuffs(buffsStarted);

            long notificationsStarted = UpdatePassProfiler.Start();
            _notificationManager?.Update(gameTime);
            _notificationController?.ProcessPending();
            UpdatePassProfiler.AddGameNotifications(notificationsStarted);

            long scopeStarted = UpdatePassProfiler.Start();
            if (World is WalkableWorldControl walkableWorld)
                ScopeHandler.PumpNpcSpawnQueue(walkableWorld);
            UpdatePassProfiler.AddGameScopePump(scopeStarted);

            if (World == null || World.Status != GameControlStatus.Ready)
            {
                _playerMenuController?.ResetOnWorldUnavailable();
                _skillController?.ClearPending();
                return;
            }

            long interactionStarted = UpdatePassProfiler.Start();
            var uiMouse = MuGame.Instance.UiMouseState;
            var prevUiMouse = MuGame.Instance.PrevUiMouseState;

            long playerMenuStarted = UpdatePassProfiler.Start();
            _playerMenuController?.Update(gameTime, currentKeyboardState, uiMouse, prevUiMouse);
            UpdatePassProfiler.AddGamePlayerMenu(playerMenuStarted);

            long skillUpdateStarted = UpdatePassProfiler.Start();
            _skillController?.Update();
            UpdatePassProfiler.AddGameSkillUpdate(skillUpdateStarted);

            long attackInputStarted = UpdatePassProfiler.Start();
            // Handle attack clicks on monsters with proper validation
            if (!IsMouseInputConsumedThisFrame &&
                !WorldHoverSystem.IsAltPressed() &&
                MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed &&
                MuGame.Instance.PrevMouseState.LeftButton == ButtonState.Released) // Fresh press
            {
                MonsterObject hoveredAttackMonster = WorldHoverSystem.FindBestLiveMonster(
                    World.VisibleObjects,
                    MuGame.Instance.MouseRay,
                    World);

                if (hoveredAttackMonster != null &&
                    Hero != null &&
                    !Hero.IsDead && // Don't attack if player is dead
                    Vector2.Distance(Hero.Location, hoveredAttackMonster.Location) <= Hero.GetAttackRangeTiles()) // Check range
                {
                    Hero.Attack(hoveredAttackMonster);
                    SetMouseInputConsumed(); // Consume the click
                }
            }

            // Handle attack clicks on duel opponent players (treat as monster during duel)
            if (!IsMouseInputConsumedThisFrame &&
                MouseHoverObject is PlayerObject targetPlayer &&
                targetPlayer != _hero &&
                (_duelController?.IsDuelAttackTarget(targetPlayer) == true) &&
                MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed &&
                MuGame.Instance.PrevMouseState.LeftButton == ButtonState.Released) // Fresh press
            {
                if (Hero != null &&
                    !Hero.IsDead &&
                    !targetPlayer.IsDead &&
                    targetPlayer.World == World)
                {
                    Hero.Attack(targetPlayer);
                    SetMouseInputConsumed();
                }
            }

            UpdatePassProfiler.AddGameAttackInput(attackInputStarted);

            // Handle skill usage with right-click. These paths are measured independently so
            // the next runtime trace can identify packet/effect cold starts precisely.
            long rightClickSkillStarted = UpdatePassProfiler.Start();
            _skillController?.HandleRightClickSkillUsage();
            UpdatePassProfiler.AddGameRightClickSkill(rightClickSkillStarted);

            long hotkeysStarted = UpdatePassProfiler.Start();
            _hotkeys?.HandleInWorld(currentKeyboardState, previousKeyboardState);
            UpdatePassProfiler.AddGameHotkeys(hotkeysStarted);
            UpdatePassProfiler.AddGameInteraction(interactionStarted);

            long housekeepingStarted = UpdatePassProfiler.Start();
            // Update ping every 5 seconds to reduce network overhead
            _pingTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_pingTimer >= 5.0)
            {
                _pingTimer = 0;
                _ = UpdatePingAsync();
            }

            // Keep this separate from DebugPanel so it is always available.
            _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_fpsTimer >= 0.25)
            {
                _fpsTimer = 0;
                UpdateFpsLabel();
            }
            UpdatePassProfiler.AddGameHousekeeping(housekeepingStarted);
        }

        internal void ScheduleMapNameUpdateNextFrame(string actionName)
        {
            _ = UpdateMapNameNextFrameAsync(actionName);
        }

        internal Task RefreshMiniMapAsync()
        {
            return _miniMap != null && World != null
                ? _miniMap.LoadContentForWorld(World.WorldIndex)
                : Task.CompletedTask;
        }

        private async Task UpdateMapNameNextFrameAsync(string actionName)
        {
            await MuGame.YieldToNextFrameAsync(
                string.IsNullOrWhiteSpace(actionName) ? "GameScene.UpdateMapName" : actionName,
                MainThreadDispatcher.WorkPriority.High);
            _mapController?.UpdateMapName();
        }

        internal Task QueueWorldActivationAfterLoadingFrame(
            Action activation,
            string actionName,
            bool cleanupLoadingUi = true)
        {
            ArgumentNullException.ThrowIfNull(activation);
            if (_pendingWorldActivation != null)
                throw new InvalidOperationException("A world activation is already pending.");

            _pendingWorldActivation = activation;
            _pendingWorldActivationScheduled = false;
            _pendingWorldActivationCleansLoadingUi = cleanupLoadingUi;
            _pendingWorldActivationName = string.IsNullOrWhiteSpace(actionName)
                ? "GameScene.ActivateWorldAfterLoadingFrame"
                : actionName;
            _pendingWorldActivationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _pendingWorldActivationCompletion.Task;
        }

        private void SchedulePendingWorldActivation()
        {
            if (_pendingWorldActivation == null || _pendingWorldActivationScheduled)
                return;

            _pendingWorldActivationScheduled = true;
            MuGame.ScheduleOnMainThread(() =>
            {
                Action activation = _pendingWorldActivation;
                TaskCompletionSource<bool> completion = _pendingWorldActivationCompletion;
                try
                {
                    activation?.Invoke();
                    if (_pendingWorldActivationCleansLoadingUi)
                        _initialWorldActivationCooldown = true;
                    completion?.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion?.TrySetException(ex);
                    throw;
                }
                finally
                {
                    _pendingWorldActivation = null;
                    _pendingWorldActivationCompletion = null;
                    _pendingWorldActivationScheduled = false;
                    _pendingWorldActivationCleansLoadingUi = false;
                    _pendingWorldActivationName = null;
                }
            }, MainThreadDispatcher.WorkPriority.Critical, _pendingWorldActivationName);
        }

        // ─────────────────────────── Draw Loop ───────────────────────────
        public override void Draw(GameTime gameTime)
        {
            if (!_sceneShellInitialized)
            {
                GraphicsDevice.Clear(new Color(12, 12, 20));
                DrawBackground();

                var initialLoading = _initialLoadingScreen ?? _mapController?.LoadingScreen;
                if (_progressBar != null)
                {
                    _progressBar.Progress = initialLoading?.Progress ?? _progressBar.Progress;
                    _progressBar.StatusText = initialLoading?.Message ?? "Preparing game interface...";
                    _progressBar.Visible = true;
                    _progressBar.Draw(gameTime);
                }
                return;
            }

            if (_initialWorldLoadInProgress || _mapController?.IsChangingWorld == true || _pendingWorldActivation != null || _initialWorldActivationCooldown || World == null || !World.Visible || World.Status != GameControlStatus.Ready)
            {
                GraphicsDevice.Clear(new Color(12, 12, 20));
                DrawBackground();
                var loading = _mapController?.LoadingScreen;
                _progressBar.Progress = loading?.Progress ?? 0f;
                _progressBar.StatusText = loading?.Message ?? "Loading...";
                _progressBar.Visible = true;
                _progressBar.Draw(gameTime);
                SchedulePendingWorldActivation();
                if (_initialWorldActivationCooldown && _pendingWorldActivation == null)
                {
                    _initialWorldActivationCooldown = false;
                    _initialWorldLoadInProgress = false;
                    _mapController?.DisposeLoadingScreen();
                    _progressBar.Visible = false;
                }
                return;
            }

            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       transform: UiScaler.SpriteTransform))
            {
                var controls = Controls.GetSnapshotArray();
                for (int i = 0; i < controls.Length; i++)
                {
                    var ctrl = controls[i];
                    if (ctrl == null || ctrl == World || ctrl == _fpsLabel || ctrl == _pingLabel || !ctrl.Visible)
                    {
                        continue;
                    }

                    ctrl.Draw(gameTime);
                }

            }

            base.Draw(gameTime);

            // Final top-most pass: draw dragged item previews above all UI windows
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       transform: UiScaler.SpriteTransform))
            {
                var sprite = GraphicsManager.Instance.Sprite;
                _inventoryControl?._pickedItemRenderer?.Draw(sprite, gameTime);
                VaultControl.Instance?.DrawPickedPreview(sprite, gameTime);
                ChaosMixControl.Instance?.DrawPickedPreview(sprite, gameTime);
                TradeControl.Instance?.DrawPickedPreview(sprite, gameTime);
                DrawPerformanceOverlay(gameTime);
            }
        }

        private void DrawPerformanceOverlay(GameTime gameTime)
        {
            _fpsLabel?.Draw(gameTime);
            _pingLabel?.Draw(gameTime);
        }

        private new void DrawBackground()
        {
            if (_backgroundTexture == null) return;

            using var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform);

            GraphicsManager.Instance.Sprite.Draw(_backgroundTexture,
                new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y), Color.White);
        }


        private Task PreloadSoundsAsync()
        {
            // Move reflection-based skill effect discovery out of the first combat packet.
            SkillVisualEffectRegistry.Initialize();

            return Task.WhenAll(
                SoundController.Instance.PreloadSoundAsync("Sound/pDropItem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pDropMoney.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/eGem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/Jewel_Sound.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pGetItem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Grass).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Snow).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Soil).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pSwim.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomord1.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomordAttack1.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomordDie.wav"));
        }

        private async Task UpdatePingAsync()
        {
            if (MuGame.Network == null)
                return;

            // System.Net Ping may perform an expensive synchronous first-use setup. Keep that
            // work away from the game thread and only publish the final value through the
            // dispatcher.
            int? ping = await Task.Run(
                async () => await MuGame.Network.PingServerAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
            MuGame.ScheduleOnMainThread(() =>
            {
                if (_pingLabel == null)
                    return;

                if (ping == _lastPingValue)
                    return;

                _lastPingValue = ping;
                _pingLabel.Text = ping.HasValue ? $"Ping: {ping.Value} ms" : "Ping: --";
            });
        }

        private void UpdateFpsLabel()
        {
            if (_fpsLabel == null)
                return;

            int fps = (int)FPSCounter.Instance.FPS_AVG;
            if (fps == _lastFpsValue)
                return;

            _lastFpsValue = fps;
            _fpsLabel.Text = $"FPS: {fps}";
        }

        private void StartWhisperToPlayer(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName) || _chatInput == null)
            {
                return;
            }

            _chatInput.StartWhisperTo(playerName);
        }

        internal void SetWorldInternal(WorldControl world)
        {
            World = world;
        }

        internal void NotifyLocalSkillAnimation(ushort skillId)
        {
            _skillController?.NotifyLocalSkillAnimation(skillId);
        }

        private void HandleUiThemeChanged(object sender, UiThemeChangedEventArgs e)
        {
            ApplyChatThemeLayout();
            ApplyUiThemeVisibility();
        }

        private void ApplyChatThemeLayout()
        {
            if (_chatLog == null || _chatInput == null)
                return;

            bool classic = !ChatUiTheme.UseModernLayout;
            if (classic)
            {
                _chatLog.X = 12;
                _chatLog.Y = UiScaler.VirtualSize.Y - UiThemeManager.Current.Metrics.ChatInputSize.Y
                    - Math.Max(1, _chatLog.ViewSize.Y) - 10;
                _chatInput.X = 12;
                _chatInput.Y = UiScaler.VirtualSize.Y - UiThemeManager.Current.Metrics.ChatInputSize.Y - 8;
            }
            else
            {
                _chatLog.X = 5;
                _chatLog.Y = UiScaler.VirtualSize.Y - 160 - ChatInputBoxControl.CHATBOX_HEIGHT;
                _chatInput.X = 5;
                _chatInput.Y = UiScaler.VirtualSize.Y - 65 - ChatInputBoxControl.CHATBOX_HEIGHT;
            }
        }

        private void ApplyUiThemeVisibility()
        {
            UiThemeId currentTheme = UiThemeManager.CurrentId;
            bool classic = currentTheme == UiThemeId.Classic;
            bool enteringTheme = !_uiThemeVisibilityInitialized || _lastAppliedUiTheme != currentTheme;
            bool pauseMenuWasVisible = _pauseMenu?.Visible == true;

            if (enteringTheme && classic)
            {
                _modernSkillSelectionWasVisible = _skillSelectionPanel?.Visible == true;
                _modernSkillSelectionWasInteractive = _skillSelectionPanel?.Interactive == true;
                _modernMasteryWasVisible = _masteryTree?.Visible == true;
                _modernMasteryWasInteractive = _masteryTree?.Interactive == true;
            }

            // Keep the Modern control alive as the shared quick-slot state owner, but never
            // let it draw or consume pointer input while Classic is active.
            if (_modernHud != null)
            {
                _modernHud.Visible = true;
                _modernHud.Interactive = !classic;
            }

            // Durability warnings belong to both themes and remain a click-through overlay.
            if (_equipmentDurabilityHud != null)
            {
                _equipmentDurabilityHud.Visible = true;
                _equipmentDurabilityHud.Interactive = false;
            }

            if (_skillSelectionPanel != null)
            {
                if (classic)
                {
                    _skillSelectionPanel.Visible = false;
                    _skillSelectionPanel.Interactive = false;
                }
                else
                {
                    bool restoreModernState = enteringTheme && _uiThemeVisibilityInitialized;
                    _skillSelectionPanel.Visible = restoreModernState
                        ? _modernSkillSelectionWasVisible
                        : _skillSelectionPanel.Visible;
                    _skillSelectionPanel.Interactive = restoreModernState
                        ? _modernSkillSelectionWasInteractive
                        : _skillSelectionPanel.Visible;
                }
            }
            if (_masteryTree != null)
            {
                if (classic)
                {
                    _masteryTree.Visible = false;
                    _masteryTree.Interactive = false;
                }
                else
                {
                    bool restoreModernState = enteringTheme && _uiThemeVisibilityInitialized;
                    _masteryTree.Visible = restoreModernState
                        ? _modernMasteryWasVisible
                        : _masteryTree.Visible;
                    _masteryTree.Interactive = restoreModernState
                        ? _modernMasteryWasInteractive
                        : _masteryTree.Visible;
                }
            }
            if (_classicMasteryTree != null && !classic)
            {
                _classicMasteryTree.Visible = false;
                _classicMasteryTree.Interactive = false;
            }

            if (_classicBottomBar != null)
            {
                _classicBottomBar.Visible = classic;
                _classicBottomBar.Interactive = classic;
            }
            if (_classicTouchActions != null)
            {
                _classicTouchActions.Visible = classic;
                _classicTouchActions.Interactive = classic;
                _classicTouchActions.MasterAlpha = classic ? 1f : 0f;
            }
            if (_classicJoystick != null)
            {
                _classicJoystick.Visible = classic;
                _classicJoystick.Interactive = classic;
            }
            if (_classicTouchMenu != null)
            {
                _classicTouchMenu.Visible = classic;
                _classicTouchMenu.Interactive = classic;
                if (!classic)
                    _classicTouchMenu.Close();
            }
            if (_classicSkillImprint != null)
            {
                if (!classic)
                {
                    _classicSkillImprint.Visible = false;
                    _classicSkillImprint.Interactive = false;
                }
                else
                {
                    _classicSkillImprint.Interactive = _classicSkillImprint.Visible;
                }
            }
            if (_classicPotionImprint != null)
            {
                if (!classic)
                {
                    _classicPotionImprint.Visible = false;
                    _classicPotionImprint.Interactive = false;
                }
                else
                {
                    _classicPotionImprint.Interactive = _classicPotionImprint.Visible;
                }
            }

            if (classic)
            {
                if (Status == GameControlStatus.Ready)
                    _ = EnsureClassicAssetsAsync();
                _classicBottomBar?.BringToFront();
                _classicTouchActions?.BringToFront();
                _classicJoystick?.BringToFront();
                _classicSkillImprint?.BringToFront();
                _classicPotionImprint?.BringToFront();
                _classicMasteryTree?.BringToFront();
                _classicTouchMenu?.BringToFront();
                _equipmentDurabilityHud?.BringToFront();
                _chatLog?.BringToFront();
                _chatInput?.BringToFront();
            }

            // The theme option lives inside the pause menu. Reordering the replacement HUD
            // must not put it above an already open settings/modal surface.
            if (pauseMenuWasVisible)
                _pauseMenu?.BringToFront();

            _lastAppliedUiTheme = currentTheme;
            _uiThemeVisibilityInitialized = true;
        }

        private async Task EnsureClassicAssetsAsync()
        {
            try
            {
                await Task.WhenAll(
                    _classicBottomBar?.Load() ?? Task.CompletedTask,
                    _classicTouchActions?.Load() ?? Task.CompletedTask,
                    _classicJoystick?.Load() ?? Task.CompletedTask,
                    _classicSkillImprint?.Load() ?? Task.CompletedTask,
                    _classicPotionImprint?.Load() ?? Task.CompletedTask,
                    _classicMasteryTree?.Load() ?? Task.CompletedTask,
                    _classicTouchMenu?.Load() ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Classic UI asset warm-up failed; controls will use procedural fallbacks.");
            }
        }

        public override void Dispose()
        {
            UiThemeManager.ThemeChanged -= HandleUiThemeChanged;
            _pendingWorldActivation = null;
            _pendingWorldActivationScheduled = false;
            _initialWorldActivationCooldown = false;
            _pendingWorldActivationCompletion?.TrySetCanceled();
            _pendingWorldActivationCompletion = null;

            if (_hero != null)
            {
                if (_windowCloseController != null)
                {
                    _hero.PlayerMoved -= _windowCloseController.OnHeroMoved;
                    _hero.PlayerTookDamage -= _windowCloseController.OnHeroTookDamage;
                }
            }
            base.Dispose();
        }
    }
}
