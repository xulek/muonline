using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Loading;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Controls.UI.SelectCharacter;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Networking;
using Client.Main.Objects.Player;
using Client.Main.Worlds;
using Client.Main.Scenes.SelectCharacter;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Scenes
{
    public class SelectCharacterScene : BaseScene
    {
        // Fields
        private readonly List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> _characters;
        private SelectWorld _selectWorld;
        private CharacterSelectionController _characterController;
        private readonly NetworkManager _networkManager;
        private ILogger<SelectCharacterScene> _logger;
        private (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)? _selectedCharacterInfo = null;
        private LoadingScreenControl _loadingScreen;
        private bool _initialLoadComplete = false;
        private int _currentCharacterIndex = -1;
        private bool _isSelectionInProgress = false;
        private Texture2D _backgroundTexture;
        private LoadingScreenView _loadingView;
        private bool _previousDayNightEnabled;
        private Vector3 _previousSunDirection;
        private bool _dayNightPatched;
        private CharacterRosterWindow _roster;
        private OrnateButtonControl _enterGameButton;
        private OrnateButtonControl _backButton;
        private UIControl _deletionDialog; // CharacterDeletionWindow (Modern) or CharacterDeletionDialog (Classic)
        private ClassicCharacterSelectionControl _classicSelectionChrome;
        private CharacterCreationDialog _characterCreationDialog; // Classic
        private CharacterCreationWindow _creationWindow; // Modern
        private string _currentlySelectedCharacterName = null;
        private bool _isIntentionalLogout = false;
        private bool _returnToLoginRequested;
        private readonly SemaphoreSlim _characterRefreshLock = new(1, 1);
        private CancellationTokenSource _characterRefreshCancellation;

        // Constructors
        public SelectCharacterScene(List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters, NetworkManager networkManager)
        {
            _characters = characters ?? new List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>();
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _logger = MuGame.AppLoggerFactory.CreateLogger<SelectCharacterScene>();

            _loadingScreen = new LoadingScreenControl { Visible = true, Message = "Loading Characters..." };
            Controls.Add(_loadingScreen);
            _loadingScreen.BringToFront();

            InitializeModernUI();
            UiThemeManager.ThemeChanged += HandleUiThemeChanged;

            try
            {
                _backgroundTexture = MuGame.Instance.Content.Load<Texture2D>("Background");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[SelectCharacterScene] Background load failed: {ex.Message}");
            }

            _loadingView = new LoadingScreenView { FallbackTexture = _backgroundTexture };
            Controls.Add(_loadingView);

            SubscribeToNetworkEvents();
            ApplyUiThemeVisibility();
        }

        private void DisableDayNightCycleForScene()
        {
            if (_dayNightPatched) return;

            _dayNightPatched = true;
            _previousDayNightEnabled = Constants.ENABLE_DAY_NIGHT_CYCLE;
            _previousSunDirection = Constants.SUN_DIRECTION;
            Constants.ENABLE_DAY_NIGHT_CYCLE = false;
            SunCycleManager.ResetToDefault();
        }

        private void RestoreDayNightCycle()
        {
            if (!_dayNightPatched) return;

            Constants.ENABLE_DAY_NIGHT_CYCLE = _previousDayNightEnabled;
            Constants.SUN_DIRECTION = _previousSunDirection;
            _dayNightPatched = false;
        }

        private void UpdateLoadProgress(string message, float progress)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (_loadingScreen != null && _loadingScreen.Visible)
                {
                    _loadingScreen.Message = message;
                    _loadingScreen.Progress = progress;
                }
            });
        }

        private void InitializeModernUI()
        {
            _roster = new CharacterRosterWindow { Visible = false };
            _roster.SlotSelected += (_, index) => SelectCharacterByIndex(index);
            _roster.CreateRequested += OnCreateCharacterButtonClick;
            _roster.DeleteRequested += OnDeleteCharacterButtonClick;
            _roster.EnterRequested += OnEnterGameButtonClick;
            Controls.Add(_roster);

            _enterGameButton = new OrnateButtonControl("ENTER GAME", OrnateButtonStyle.Primary, new Point(300, 50))
            {
                Align = ControlAlign.HorizontalCenter | ControlAlign.Bottom,
                Margin = new Margin { Bottom = 34 },
                Visible = false
            };
            _enterGameButton.Clicked += OnEnterGameButtonClick;
            Controls.Add(_enterGameButton);

            _backButton = new OrnateButtonControl("Back", OrnateButtonStyle.Standard, new Point(160, 40))
            {
                Align = ControlAlign.Left | ControlAlign.Bottom,
                Margin = new Margin { Left = 34, Bottom = 38 },
                Visible = false
            };
            _backButton.Clicked += OnExitButtonClick;
            Controls.Add(_backButton);

            _classicSelectionChrome = new ClassicCharacterSelectionControl
            {
                Visible = false,
                Interactive = false
            };
            _classicSelectionChrome.CharacterSelected += OnClassicCharacterSelected;
            _classicSelectionChrome.EmptySlotClicked += OnCreateCharacterButtonClick;
            _classicSelectionChrome.EnterClicked += OnEnterGameButtonClick;
            _classicSelectionChrome.DeleteClicked += OnDeleteCharacterButtonClick;
            _classicSelectionChrome.BackClicked += OnExitButtonClick;
            Controls.Add(_classicSelectionChrome);
        }

        private bool IsSelectionReady =>
            _initialLoadComplete && (_loadingScreen == null || !_loadingScreen.Visible) && !_isSelectionInProgress;

        /// <summary>Pushes the character list, selection and readiness into whichever interface is active.</summary>
        private void RefreshSelectionUi()
        {
            if (_roster == null)
                return;

            bool classic = UiThemeManager.CurrentId == UiThemeId.Classic;
            bool ready = IsSelectionReady;
            bool modalOpen = _characterCreationDialog != null || _deletionDialog != null;
            bool creating = _creationWindow != null; // the creation window takes the roster's place

            _roster.Visible = !classic && ready && !creating;
            _roster.Busy = modalOpen;
            _roster.SetSlots(_characters.Select(c => new CharacterRosterWindow.Slot(c.Name, c.Class, c.Level)).ToList());
            _roster.SelectedIndex = _currentCharacterIndex;

            _enterGameButton.Visible = !classic && ready && !creating;
            _enterGameButton.Enabled = !modalOpen && !string.IsNullOrEmpty(_currentlySelectedCharacterName);
            _backButton.Visible = !classic && ready && !creating;
            _backButton.Enabled = !modalOpen;

            if (classic)
                UpdateClassicSelectionState();
        }

        private void HandleUiThemeChanged(object sender, UiThemeChangedEventArgs e)
        {
            ApplyUiThemeVisibility();
        }

        private void ApplyUiThemeVisibility()
        {
            bool classic = UiThemeManager.CurrentId == UiThemeId.Classic;

            if (_classicSelectionChrome != null)
            {
                _classicSelectionChrome.Visible = classic && IsSelectionReady;
                _classicSelectionChrome.Interactive = _classicSelectionChrome.Visible;
                if (classic)
                {
                    _classicSelectionChrome.SetCharacters(_characters.Select(character =>
                        new ClassicCharacterSelectionControl.Entry(character.Name, character.Class, character.Level)));
                    _classicSelectionChrome.SetSelectedIndex(_currentCharacterIndex);
                    if (Status == GameControlStatus.Ready)
                        _ = _classicSelectionChrome.Load();
                    _classicSelectionChrome.BringToFront();
                }
            }

            RefreshSelectionUi();
        }

        private void UpdateClassicSelectionState()
        {
            if (_classicSelectionChrome == null)
                return;

            bool ready = _initialLoadComplete && (_loadingScreen == null || !_loadingScreen.Visible) &&
                         !_isSelectionInProgress;
            _classicSelectionChrome.SetCharacters(_characters.Select(character =>
                new ClassicCharacterSelectionControl.Entry(character.Name, character.Class, character.Level)));
            _classicSelectionChrome.SetSelectedIndex(_currentCharacterIndex);
            _classicSelectionChrome.CanEnter = ready && _currentCharacterIndex >= 0 &&
                                               !string.IsNullOrWhiteSpace(_currentlySelectedCharacterName);
            _classicSelectionChrome.Visible = UiThemeManager.CurrentId == UiThemeId.Classic && ready;
            _classicSelectionChrome.Interactive = _classicSelectionChrome.Visible;
        }

        private void OnClassicCharacterSelected(object sender, string characterName)
        {
            int index = _characters.FindIndex(character =>
                string.Equals(character.Name, characterName, StringComparison.Ordinal));
            if (index >= 0)
                SelectCharacterByIndex(index);
        }

        protected override async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            DisableDayNightCycleForScene();
            UpdateLoadProgress("Initializing Character Selection...", 0.0f);
            _logger.LogInformation(">>> SelectCharacterScene LoadSceneContentWithProgress starting...");

            try
            {
                UpdateLoadProgress("Creating Select World...", 0.05f);
                _selectWorld = new SelectWorld { Visible = false };
                Controls.Add(_selectWorld);

                UpdateLoadProgress("Initializing Select World (Graphics)...", 0.1f);
                await _selectWorld.Initialize();
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.AttachWorld",
                    MainThreadDispatcher.WorkPriority.Critical);
                World = _selectWorld;
                UpdateLoadProgress("Select World Initialized.", 0.35f);
                _logger.LogInformation("--- SelectCharacterScene: SelectWorld initialized and set.");

                if (_selectWorld.Terrain != null)
                {
                    _selectWorld.Terrain.AmbientLight = 0.6f;
                }

                // Create controller
                _characterController = new CharacterSelectionController(
                    MuGame.AppLoggerFactory.CreateLogger<CharacterSelectionController>());

                // Subscribe to events
                _characterController.CharacterClicked += OnControllerCharacterClicked;
                _characterController.CharacterDoubleClicked += OnControllerCharacterDoubleClicked;

                // Connect to world
                _selectWorld.SetController(_characterController);

                // Attaching the world invalidates control-tree state. Let that frame finish
                // before constructing and publishing character slots.
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.CreateSlots",
                    MainThreadDispatcher.WorkPriority.High);

                if (_characters.Any())
                {
                    UpdateLoadProgress("Preparing Character Data...", 0.40f);
                    await _characterController.CreateCharactersAsync(
                        _characters,
                        _selectWorld,
                        this,
                        _selectWorld.CharacterDisplayPosition,
                        _selectWorld.CharacterDisplayAngle);

                    if (_characters.Count > 0)
                    {
                        _currentCharacterIndex = 0;
                        _currentlySelectedCharacterName = _characters[0].Name;
                    }
                    else
                    {
                        _currentCharacterIndex = -1;
                    }

                    RefreshSelectionUi();

                    float characterCreationStartProgress = 0.45f;
                    float characterCreationEndProgress = 0.85f;
                    float totalCharacterProgressSpan = characterCreationEndProgress - characterCreationStartProgress;

                    if (_characters.Count > 0)
                    {
                        float progressPerCharacter = totalCharacterProgressSpan / _characters.Count;
                        for (int i = 0; i < _characters.Count; i++)
                        {
                            UpdateLoadProgress($"Configuring character {i + 1}/{_characters.Count}...", characterCreationStartProgress + (i + 1) * progressPerCharacter);
                        }
                    }
                    else
                    {
                        UpdateLoadProgress("No characters to configure.", characterCreationEndProgress);
                    }

                    UpdateLoadProgress("Preparing first character-selection frame...", 0.90f);
                    await _selectWorld.PrepareInitialRenderResourcesAsync(
                        "CharacterSelection.PrewarmWorld");
                    await MuGame.YieldToNextFrameAsync(
                        "CharacterSelection.ActivateWorld",
                        MainThreadDispatcher.WorkPriority.Critical);
                    _selectWorld.Visible = true;
                    _characterController.EnsureActiveCharacterVisible(_selectWorld);
                    _selectWorld.PrepareInitialVisibilitySnapshot();
                    UpdateLoadProgress("Character Objects Ready.", 0.94f);
                    _logger.LogInformation("--- SelectCharacterScene: Character creation finished.");
                }
                else
                {
                    _currentCharacterIndex = -1;
                    string message = "No characters found on this account.";
                    _logger.LogWarning("--- SelectCharacterScene: {Message}", message);
                    UpdateLoadProgress(message, 0.90f);
                    RefreshSelectionUi();
                }

                if (!_selectWorld.Visible)
                {
                    await _selectWorld.PrepareInitialRenderResourcesAsync(
                        "CharacterSelection.PrewarmEmptyWorld");
                    await MuGame.YieldToNextFrameAsync(
                        "CharacterSelection.ActivateEmptyWorld",
                        MainThreadDispatcher.WorkPriority.Critical);
                    _selectWorld.Visible = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "!!! SelectCharacterScene: Error during world initialization or character creation.");
                UpdateLoadProgress("Error loading character selection.", 1.0f);
                RefreshSelectionUi();
            }
            finally
            {
                _initialLoadComplete = true;
                RefreshSelectionUi();
                UpdateLoadProgress("Character Selection Ready.", 1.0f);
                _logger.LogInformation("<<< SelectCharacterScene LoadSceneContentWithProgress finished.");
            }

            // Do not complete the parent scene-initialization chain inside the activation action.
            await Task.Yield();
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
            _logger.LogInformation("SelectCharacterScene.AfterLoad() called.");
            if (_loadingScreen != null)
            {
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (_loadingScreen != null)
                    {
                        Controls.Remove(_loadingScreen);
                        _loadingScreen.Dispose();
                        _loadingScreen = null;
                        if (_loadingView != null)
                        {
                            _loadingView.Visible = false;
                        }
                        RefreshSelectionUi();
                        _roster?.BringToFront();
                        _enterGameButton?.BringToFront();
                        _backButton?.BringToFront();
                        Cursor?.BringToFront();
                        DebugPanel?.BringToFront();
                    }
                });
            }
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            RefreshSelectionUi();
        }

        public override async Task Load()
        {
            if (Status == GameControlStatus.Initializing)
            {
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
            else
            {
                _logger.LogDebug("SelectCharacterScene.Load() called outside of InitializeWithProgressReporting flow. Re-routing to progressive load.");
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
        }


        public void CharacterSelected(string characterName)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                _logger.LogInformation("Character selection attempted while loading screen is visible. Ignoring.");
                return;
            }

            int matchedIndex = -1;
            for (int i = 0; i < _characters.Count; i++)
            {
                if (string.Equals(_characters[i].Name, characterName, StringComparison.Ordinal))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                _logger.LogError("Character '{CharacterName}' selected, but not found in the character list.", characterName);
                MessageWindow.Show($"Error selecting character '{characterName}'.");
                return;
            }

            _selectedCharacterInfo = _characters[matchedIndex];
            _currentCharacterIndex = matchedIndex;
            _characterController?.SetActiveCharacter(_currentCharacterIndex);

            ClientConnectionState currentState = _networkManager.CurrentState;
            bool canSelect = currentState == ClientConnectionState.ConnectedToGameServer ||
                             currentState == ClientConnectionState.SelectingCharacter;

            if (!canSelect)
            {
                _logger.LogWarning("Character selection attempted but NetworkManager state is not ConnectedToGameServer or SelectingCharacter. State: {State}", currentState);
                MessageWindow.Show($"Cannot select character. Invalid network state: {currentState}");
                _selectedCharacterInfo = null;
                return;
            }

            _logger.LogInformation("Character '{CharacterName}' (Class: {Class}) selected in scene. Sending request...",
                                   _selectedCharacterInfo.Value.Name, _selectedCharacterInfo.Value.Class);

            DisableInteractionDuringSelection(characterName);
            _ = _networkManager.SendSelectCharacterRequestAsync(characterName);
        }

        public override void Dispose()
        {
            _logger.LogDebug("Disposing SelectCharacterScene.");
            UiThemeManager.ThemeChanged -= HandleUiThemeChanged;
            if (_classicSelectionChrome != null)
            {
                _classicSelectionChrome.CharacterSelected -= OnClassicCharacterSelected;
                _classicSelectionChrome.EmptySlotClicked -= OnCreateCharacterButtonClick;
                _classicSelectionChrome.EnterClicked -= OnEnterGameButtonClick;
                _classicSelectionChrome.DeleteClicked -= OnDeleteCharacterButtonClick;
                _classicSelectionChrome.BackClicked -= OnExitButtonClick;
            }
            UnsubscribeFromNetworkEvents();

            var refreshCancellation = Interlocked.Exchange(ref _characterRefreshCancellation, null);
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();

            if (_characterController != null)
            {
                _characterController.CharacterClicked -= OnControllerCharacterClicked;
                _characterController.CharacterDoubleClicked -= OnControllerCharacterDoubleClicked;
                _characterController.Dispose();
                _characterController = null;
            }

            CloseCharacterCreationDialog();
            if (_loadingScreen != null)
            {
                Controls.Remove(_loadingScreen);
                _loadingScreen.Dispose();
                _loadingScreen = null;
            }
            RestoreDayNightCycle();
            base.Dispose();
        }

        private void SubscribeToNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.EnteredGame += HandleEnteredGame;
                _networkManager.ErrorOccurred += HandleNetworkError;
                _networkManager.ConnectionStateChanged += HandleConnectionStateChange;
                _networkManager.CharacterListReceived += HandleCharacterListReceived;
                _networkManager.LogoutResponseReceived += HandleLogoutResponseReceived;
                _logger.LogDebug("SelectCharacterScene subscribed to NetworkManager events (including LogoutResponseReceived).");
            }
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.EnteredGame -= HandleEnteredGame;
                _networkManager.ErrorOccurred -= HandleNetworkError;
                _networkManager.ConnectionStateChanged -= HandleConnectionStateChange;
                _networkManager.CharacterListReceived -= HandleCharacterListReceived;
                _networkManager.LogoutResponseReceived -= HandleLogoutResponseReceived;
                _logger.LogDebug("SelectCharacterScene unsubscribed from NetworkManager events.");
            }
        }

        private void HandleLogoutResponseReceived(object sender, LogOutType logoutType)
        {
            _logger.LogInformation("SelectCharacterScene.HandleLogoutResponseReceived: Type={Type}", logoutType);
            // Intentional logout handling is now done in HandleConnectionStateChange
            // which reacts to the Disconnected state after logout
        }

        private void HandleCharacterListReceived(object sender,
            List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters)
        {
            var snapshot = characters?.ToList()
                ?? new List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>();
            _logger.LogInformation(
                "SelectCharacterScene.HandleCharacterListReceived: Received {Count} characters",
                snapshot.Count);

            var refreshCancellation = new CancellationTokenSource();
            var previousCancellation = Interlocked.Exchange(
                ref _characterRefreshCancellation,
                refreshCancellation);
            previousCancellation?.Cancel();
            previousCancellation?.Dispose();
            CancellationToken token = refreshCancellation.Token;

            MuGame.ScheduleOnMainThread(
                () => RefreshCharacterListAsync(snapshot, token),
                MainThreadDispatcher.WorkPriority.High,
                "HandleCharacterListReceived.RefreshInPlace");
        }

        private async Task RefreshCharacterListAsync(
            List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters,
            CancellationToken cancellationToken)
        {
            await _characterRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.Refresh.Begin",
                    MainThreadDispatcher.WorkPriority.High);

                if (MuGame.Instance.ActiveScene != this || _characterController == null || _selectWorld == null)
                    return;

                string selectedName = _currentlySelectedCharacterName;
                _isSelectionInProgress = true;
                RefreshSelectionUi();

                _characters.Clear();
                _characters.AddRange(characters);

                await _characterController.CreateCharactersAsync(
                    _characters,
                    _selectWorld,
                    this,
                    _selectWorld.CharacterDisplayPosition,
                    _selectWorld.CharacterDisplayAngle,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (MuGame.Instance.ActiveScene != this)
                    return;

                int selectedIndex = -1;
                if (!string.IsNullOrEmpty(selectedName))
                {
                    selectedIndex = _characters.FindIndex(character =>
                        string.Equals(character.Name, selectedName, StringComparison.Ordinal));
                }

                if (selectedIndex < 0 && _characters.Count > 0)
                    selectedIndex = 0;

                _currentCharacterIndex = selectedIndex;
                _currentlySelectedCharacterName = selectedIndex >= 0
                    ? _characters[selectedIndex].Name
                    : null;
                if (selectedIndex >= 0)
                    _characterController.SetActiveCharacter(selectedIndex);

                _selectedCharacterInfo = null;
                RefreshSelectionUi();
                _logger.LogInformation(
                    "Character selection refreshed in place with {Count} slots.",
                    _characters.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Character selection refresh superseded by a newer list.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing SelectCharacterScene in place.");
            }
            finally
            {
                _isSelectionInProgress = false;
                if (MuGame.Instance.ActiveScene == this)
                    RefreshSelectionUi();
                _characterRefreshLock.Release();
            }
        }

        private void HandleEnteredGame(object sender, EventArgs e)
        {
            _logger.LogInformation(">>> SelectCharacterScene.HandleEnteredGame: Event received.");

            if (!_selectedCharacterInfo.HasValue)
            {
                _logger.LogError("!!! SelectCharacterScene.HandleEnteredGame: EnteredGame event received, but _selectedCharacterInfo is null. Cannot change to GameScene.");
                if (_loadingScreen != null)
                {
                    MuGame.ScheduleOnMainThread(() =>
                    {
                        Controls.Remove(_loadingScreen);
                        _loadingScreen.Dispose();
                        _loadingScreen = null;
                        EnableInteractionAfterSelection();
                    });
                }
                return;
            }

            var characterInfo = _selectedCharacterInfo.Value;
            _logger.LogInformation("--- SelectCharacterScene.HandleEnteredGame: Scheduling scene change to GameScene for character: {Name} ({Class})",
                characterInfo.Name, characterInfo.Class);

            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogInformation("--- SelectCharacterScene.HandleEnteredGame (UI Thread): Executing scheduled scene change...");
                if (MuGame.Instance.ActiveScene == this)
                {
                    try
                    {
                        MuGame.Instance.ChangeScene(new GameScene(characterInfo, _networkManager));
                        _logger.LogInformation("<<< SelectCharacterScene.HandleEnteredGame (UI Thread): ChangeScene to GameScene call completed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "!!! SelectCharacterScene.HandleEnteredGame (UI Thread): Exception during ChangeScene to GameScene.");
                        EnableInteractionAfterSelection();
                    }
                }
                else
                {
                    _logger.LogWarning("<<< SelectCharacterScene.HandleEnteredGame (UI Thread): Scene changed before execution. Aborting change to GameScene.");
                }
            }, MainThreadDispatcher.WorkPriority.Critical, "HandleEnteredGame");
        }

        private void HandleNetworkError(object sender, string errorMessage)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogError("SelectCharacterScene received NetworkError: {Error}", errorMessage);
                MessageWindow.Show($"Network Error: {errorMessage}");
                EnableInteractionAfterSelection();
                RequestReturnToLogin();
            });
        }

        private void HandleConnectionStateChange(object sender, ClientConnectionState newState)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogDebug("SelectCharacterScene received ConnectionStateChanged: {NewState}", newState);
                if (newState == ClientConnectionState.Disconnected)
                {
                    if (_isIntentionalLogout)
                    {
                        _logger.LogInformation("Intentional logout - returning to LoginScene.");
                    }
                    else
                    {
                        _logger.LogWarning("Disconnected while in character selection. Returning to LoginScene.");
                        MessageWindow.Show("Connection lost.");
                    }

                    RequestReturnToLogin();
                }
            });
        }

        private void RequestReturnToLogin()
        {
            if (_returnToLoginRequested || MuGame.Instance.ActiveScene != this)
                return;

            _returnToLoginRequested = true;
            MuGame.Instance.ChangeScene<LoginScene>();
        }

        private void DisableInteractionDuringSelection(string characterName)
        {
            _isSelectionInProgress = true;
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = false;
            }
            if (_characterController != null)
            {
                foreach (var player in _characterController.Characters)
                {
                    player.Interactive = false;
                }
                foreach (var label in _characterController.Labels.Values)
                {
                    label.Visible = false;
                }
            }
            if (_loadingScreen == null)
            {
                _loadingScreen = new LoadingScreenControl { Visible = true };
                Controls.Add(_loadingScreen);
            }
            _loadingScreen.Message = $"Entering game as {characterName}...";
            _loadingScreen.Progress = 0f;
            _loadingScreen.Visible = true;
            _loadingScreen.BringToFront();
            Cursor?.BringToFront();
            RefreshSelectionUi();
        }

        private void EnableInteractionAfterSelection()
        {
            _isSelectionInProgress = false;
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = true;
            }
            if (_characterController != null)
            {
                foreach (var player in _characterController.Characters)
                {
                    player.Interactive = true;
                }
                // Labels visibility will be restored by controller's active character logic
                if (_characterController.ActiveCharacter != null)
                {
                    var activePlayer = _characterController.ActiveCharacter;
                    if (_characterController.Labels.TryGetValue(activePlayer, out var label))
                    {
                        label.Visible = true;
                    }
                }
            }
            _selectedCharacterInfo = null;

            if (_loadingScreen != null)
            {
                Controls.Remove(_loadingScreen);
                _loadingScreen.Dispose();
                _loadingScreen = null;
            }

            RefreshSelectionUi();
        }

        // Diagnostics (StressHarness screenshots).
        internal void DiagnosticsOpenCreation() => OnCreateCharacterButtonClick(this, EventArgs.Empty);
        internal void DiagnosticsCloseCreation() => CloseCharacterCreationDialog();
        internal void DiagnosticsOpenDeletion() => OnDeleteCharacterButtonClick(this, EventArgs.Empty);
        internal void DiagnosticsCloseDeletion() => CloseDeletionDialog();
        internal void DiagnosticsShowMessage() => MessageWindow.Show("Creating character 'Arthas'...\nPlease wait for server response.");

        private void OnCreateCharacterButtonClick(object sender, EventArgs e)
        {
            if (_characterCreationDialog != null || _creationWindow != null)
                return;

            _logger.LogInformation("Opening character creation...");
            if (UiThemeManager.CurrentId == UiThemeId.Classic)
                _ = OpenClassicCreationAsync();
            else
                _ = OpenModernCreationAsync();
        }

        /// <summary>Modern: the creation window replaces the roster and the selected class is previewed in 3D.</summary>
        private async Task OpenModernCreationAsync()
        {
            var window = new CharacterCreationWindow { Visible = false };
            _creationWindow = window;
            window.CreateRequested += OnCharacterCreateRequested;
            window.CancelRequested += OnCharacterCreationCancelled;
            window.SelectionChanged += (_, _) => _ = _selectWorld?.ShowCreateRoleFace(window.SelectedClass.PreviewModel);
            Controls.Add(window);

            if (_selectWorld != null)
                _selectWorld.Interactive = false;
            _selectWorld?.BeginCreateRoleFace();
            SetSelectionCharactersVisible(false);
            RefreshSelectionUi();

            try
            {
                await window.Initialize();
                if (!ReferenceEquals(_creationWindow, window))
                    return;
                if (_selectWorld != null)
                    await _selectWorld.ShowCreateRoleFace(window.SelectedClass.PreviewModel);

                window.Visible = true;
                window.BringToFront();
                window.FocusName();
                Cursor?.BringToFront();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open the character creation window.");
                CloseCharacterCreationDialog();
            }
        }

        private async Task OpenClassicCreationAsync()
        {
            // Keep the dialog hidden until its child controls and Classic textures are ready.
            // Showing it earlier renders procedural fallbacks for one or more frames and the
            // first touch can land before the name field has been initialized.
            CharacterCreationDialog dialog = new() { Visible = false };
            _characterCreationDialog = dialog;
            dialog.CharacterCreateRequested += OnCharacterCreateRequested;
            dialog.CancelRequested += OnCharacterCreationCancelled;
            dialog.SelectionChanged += OnCharacterCreationSelectionChanged;
            Controls.Add(dialog);

            if (_selectWorld != null)
                _selectWorld.Interactive = false;
            RefreshSelectionUi();

            if (_classicSelectionChrome != null)
            {
                _classicSelectionChrome.Visible = false;
                _classicSelectionChrome.Interactive = false;
            }

            _selectWorld?.BeginCreateRoleFace();
            SetSelectionCharactersVisible(false);

            try
            {
                await dialog.Initialize();
                if (!ReferenceEquals(_characterCreationDialog, dialog))
                    return;
                if (dialog.Status != GameControlStatus.Ready)
                    throw new InvalidOperationException("Character creation dialog did not reach the ready state.");

                if (_selectWorld != null)
                    await _selectWorld.ShowCreateRoleFace(dialog.SelectedClassPreviewModel);

                dialog.Visible = true;
                dialog.BringToFront();
                Cursor?.BringToFront();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize the character creation dialog.");
                CloseCharacterCreationDialog();
            }
        }

        private void OnCharacterCreateRequested(object sender, (string Name, CharacterClassNumber Class) data)
        {
            _logger.LogInformation("Character creation requested: Name={Name}, Class={Class}", data.Name, data.Class);

            // Close dialog
            CloseCharacterCreationDialog();

            // Send create character request
            var characterService = _networkManager?.GetCharacterService();
            if (characterService != null)
            {
                _ = characterService.SendCreateCharacterRequestAsync(data.Name, data.Class);
                MessageWindow.Show($"Creating character '{data.Name}'...\nPlease wait for server response.");

                // Request updated character list after a short delay
                _ = RefreshCharacterListAfterDelay();
            }
            else
            {
                _logger.LogError("CharacterService not available - cannot create character.");
                MessageWindow.Show("Error: Cannot create character at this time.");
            }
        }

        private async Task RefreshCharacterListAfterDelay()
        {
            // Wait for server to process creation
            await Task.Delay(2000);

            _logger.LogInformation("Requesting updated character list after creation...");
            var characterService = _networkManager?.GetCharacterService();
            if (characterService != null)
            {
                await characterService.RequestCharacterListAsync();
                // Note: The character list handler will update the scene
            }
        }

        private void OnCharacterCreationCancelled(object sender, EventArgs e)
        {
            _logger.LogInformation("Character creation cancelled.");
            CloseCharacterCreationDialog();
        }

        private void OnControllerCharacterClicked(object sender, string characterName)
        {
            _logger.LogInformation("Controller: Character '{Name}' clicked.", characterName);
            _currentlySelectedCharacterName = characterName;

            // Find index
            for (int i = 0; i < _characters.Count; i++)
            {
                if (_characters[i].Name == characterName)
                {
                    _currentCharacterIndex = i;
                    break;
                }
            }

            RefreshSelectionUi();
        }

        private void OnControllerCharacterDoubleClicked(object sender, string characterName)
        {
            _logger.LogInformation("Controller: Character '{Name}' double-clicked.", characterName);
            CharacterSelected(characterName);
        }

        private void OnDeleteCharacterButtonClick(object sender, EventArgs e)
        {
            if (_deletionDialog != null)
                return;

            int index = _characters.FindIndex(c => string.Equals(c.Name, _currentlySelectedCharacterName, StringComparison.Ordinal));
            if (index < 0)
            {
                _logger.LogWarning("Delete requested but no character is selected.");
                return;
            }

            var character = _characters[index];
            _logger.LogInformation("Delete requested for character '{Name}'.", character.Name);

            if (UiThemeManager.CurrentId == UiThemeId.Classic)
            {
                var dialog = new CharacterDeletionDialog(character.Name);
                dialog.DeleteConfirmed += (_, code) => ConfirmDeletion(character.Name, code);
                dialog.CancelRequested += (_, _) => CloseDeletionDialog();
                _deletionDialog = dialog;
            }
            else
            {
                var window = new CharacterDeletionWindow(new CharacterRosterWindow.Slot(character.Name, character.Class, character.Level));
                window.DeleteConfirmed += (_, code) => ConfirmDeletion(character.Name, code);
                window.CancelRequested += (_, _) => CloseDeletionDialog();
                _deletionDialog = window;
            }

            Controls.Add(_deletionDialog);
            _deletionDialog.BringToFront();
            (_deletionDialog as CharacterDeletionWindow)?.FocusCode();
            Cursor?.BringToFront();

            if (_selectWorld != null)
                _selectWorld.Interactive = false;
            RefreshSelectionUi();
        }

        private void ConfirmDeletion(string characterName, string securityCode)
        {
            _logger.LogInformation("User confirmed deletion of '{Name}' with security code.", characterName);
            var characterService = _networkManager?.GetCharacterService();
            if (characterService != null)
            {
                _ = characterService.SendDeleteCharacterRequestAsync(characterName, securityCode);
                MessageWindow.Show($"Deleting character '{characterName}'...\nPlease wait for server response.");
                _currentlySelectedCharacterName = null;
            }
            else
            {
                _logger.LogError("CharacterService not available - cannot delete character.");
                MessageWindow.Show("Error: Cannot delete character at this time.");
            }

            CloseDeletionDialog();
        }

        private void CloseDeletionDialog()
        {
            if (_deletionDialog == null)
                return;

            Controls.Remove(_deletionDialog);
            _deletionDialog.Dispose();
            _deletionDialog = null;

            if (_selectWorld != null)
                _selectWorld.Interactive = true;
            RefreshSelectionUi();
        }

        private void CloseCharacterCreationDialog()
        {
            if (_characterCreationDialog != null)
            {
                _characterCreationDialog.CharacterCreateRequested -= OnCharacterCreateRequested;
                _characterCreationDialog.CancelRequested -= OnCharacterCreationCancelled;
                _characterCreationDialog.SelectionChanged -= OnCharacterCreationSelectionChanged;
                Controls.Remove(_characterCreationDialog);
                _characterCreationDialog.Dispose();
                _characterCreationDialog = null;
            }

            if (_creationWindow != null)
            {
                _creationWindow.CreateRequested -= OnCharacterCreateRequested;
                _creationWindow.CancelRequested -= OnCharacterCreationCancelled;
                Controls.Remove(_creationWindow);
                _creationWindow.Dispose();
                _creationWindow = null;
            }

            _selectWorld?.HideCreateRoleFace();
            SetSelectionCharactersVisible(true);

            // Re-enable interactions
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = true;
            }
            UpdateClassicSelectionState();
            RefreshSelectionUi();
        }

        private void OnCharacterCreationSelectionChanged(object sender, EventArgs e)
        {
            if (sender is CharacterCreationDialog dialog &&
                ReferenceEquals(dialog, _characterCreationDialog) &&
                UiThemeManager.CurrentId == UiThemeId.Classic)
            {
                _ = _selectWorld?.ShowCreateRoleFace(dialog.SelectedClassPreviewModel);
            }
        }

        private void SetSelectionCharactersVisible(bool visible)
        {
            if (_characterController == null)
                return;

            int activeIndex = _characterController.ActiveIndex;
            for (int i = 0; i < _characterController.Characters.Count; i++)
            {
                var player = _characterController.Characters[i];
                bool active = visible && i == activeIndex;
                player.Hidden = !active;
                player.Interactive = active;

                if (_characterController.Labels.TryGetValue(player, out var label))
                    label.Visible = active;
            }

            if (visible && activeIndex >= 0)
                _characterController.SetActiveCharacter(activeIndex);
        }

        public override void Update(GameTime gameTime)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                _loadingScreen.Update(gameTime);
                Cursor?.Update(gameTime);
                DebugPanel?.Update(gameTime);
                return;
            }
            if (!_initialLoadComplete && Status == GameControlStatus.Initializing)
            {
                Cursor?.Update(gameTime);
                DebugPanel?.Update(gameTime);
                return;
            }

            base.Update(gameTime);
        }

        private void SelectCharacterByIndex(int index)
        {
            if (index < 0 || index >= _characters.Count || _characterController == null)
                return;

            _currentCharacterIndex = index;
            var character = _characters[index];
            _currentlySelectedCharacterName = character.Name;
            _characterController.SetActiveCharacter(_currentCharacterIndex);
            RefreshSelectionUi();

            _logger.LogInformation("Character '{Name}' selected via card click.", character.Name);
        }

        public override void Draw(GameTime gameTime)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                GraphicsDevice.Clear(new Color(12, 12, 20));
                _loadingView.Progress = _loadingScreen.Progress;
                _loadingView.StatusText = _loadingScreen.Message;
                _loadingView.Visible = true;
                _loadingView.Draw(gameTime);
                return;
            }

            // Draw 3D world first
            base.Draw(gameTime);

            // Cursor and debug panel on top of the 3D world and the interface
            DrawCursorOverlay(gameTime);
        }

        private void DrawCursorOverlay(GameTime gameTime)
        {
            // Draw cursor and debug panel on top of everything
            using (var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform))
            {
                Cursor?.Draw(gameTime);
                DebugPanel?.Draw(gameTime);
            }
        }

        private void OnEnterGameButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentlySelectedCharacterName))
            {
                MessageWindow.Show("Please select a character first.");
                return;
            }

            // Enter game with selected character
            var matchedIndex = _characters.FindIndex(c => c.Name.Equals(_currentlySelectedCharacterName, StringComparison.OrdinalIgnoreCase));
            if (matchedIndex < 0)
            {
                _logger.LogWarning("Character '{Name}' not found in character list.", _currentlySelectedCharacterName);
                MessageWindow.Show($"Error: Character '{_currentlySelectedCharacterName}' not found.");
                return;
            }

            _selectedCharacterInfo = _characters[matchedIndex];
            _currentCharacterIndex = matchedIndex;
            _characterController?.SetActiveCharacter(_currentCharacterIndex);

            ClientConnectionState currentState = _networkManager.CurrentState;
            bool canSelect = currentState == ClientConnectionState.ConnectedToGameServer ||
                             currentState == ClientConnectionState.SelectingCharacter;

            if (!canSelect)
            {
                _logger.LogWarning("Character selection attempted but NetworkManager state is not ConnectedToGameServer or SelectingCharacter. State: {State}", currentState);
                MessageWindow.Show($"Cannot select character. Invalid network state: {currentState}");
                _selectedCharacterInfo = null;
                return;
            }

            _logger.LogInformation("Character '{CharacterName}' (Class: {Class}) selected in scene. Sending request...",
                                   _selectedCharacterInfo.Value.Name, _selectedCharacterInfo.Value.Class);

            DisableInteractionDuringSelection(_currentlySelectedCharacterName);
            _ = _networkManager.SendSelectCharacterRequestAsync(_currentlySelectedCharacterName);
        }

        private void OnExitButtonClick(object sender, EventArgs e)
        {
            if (_isIntentionalLogout)
                return;

            _logger.LogInformation("Exit button clicked - returning to login.");
            _isIntentionalLogout = true;
            _isSelectionInProgress = true;

            if (_selectWorld != null)
                _selectWorld.Interactive = false;

            if (_loadingScreen == null)
            {
                _loadingScreen = new LoadingScreenControl { Visible = true };
                Controls.Add(_loadingScreen);
            }

            _loadingScreen.Message = "Returning to login...";
            _loadingScreen.Progress = 0f;
            _loadingScreen.Visible = true;
            _loadingScreen.BringToFront();
            Cursor?.BringToFront();
            RefreshSelectionUi();

            _ = _networkManager.GetCharacterService().SendLogoutRequestAsync(LogOutType.BackToServerSelection);
        }
    }
}
