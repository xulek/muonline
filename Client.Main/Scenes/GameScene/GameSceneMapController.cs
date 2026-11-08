using System;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Controls.UI.Game.Hud;
using Client.Main.Controls.UI.Game.Map;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Worlds;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Scenes
{
    internal sealed class GameSceneMapController
    {
        private readonly GameScene _scene;
        private readonly GameControl _hud;
        private readonly ProgressBarControl _progressBar;
        private readonly ChatLogWindow _chatLog;
        private readonly ChatInputBoxControl _chatInput;
        private readonly MapListControl _mapListControl;
        private readonly DebugPanel _debugPanel;
        private readonly CursorControl _cursor;
        private readonly GameSceneScopeImportController _scopeImportController;
        private readonly ILogger _logger;

        private LoadingScreenControl _loadingScreen;
        private bool _isChangingWorld;
        private MapNameControl _currentMapNameControl;

        public GameSceneMapController(
            GameScene scene,
            GameControl hud,
            ProgressBarControl progressBar,
            ChatLogWindow chatLog,
            ChatInputBoxControl chatInput,
            MapListControl mapListControl,
            DebugPanel debugPanel,
            CursorControl cursor,
            GameSceneScopeImportController scopeImportController,
            ILogger logger,
            LoadingScreenControl loadingScreen = null)
        {
            _scene = scene;
            _hud = hud;
            _progressBar = progressBar;
            _chatLog = chatLog;
            _chatInput = chatInput;
            _mapListControl = mapListControl;
            _debugPanel = debugPanel;
            _cursor = cursor;
            _scopeImportController = scopeImportController;
            _logger = logger;
            _loadingScreen = loadingScreen;
        }

        public bool IsChangingWorld => _isChangingWorld;
        public LoadingScreenControl LoadingScreen => _loadingScreen;

        public void EnsureLoadingScreen(string message = "Loading Game...")
        {
            if (_loadingScreen == null)
            {
                _loadingScreen = new LoadingScreenControl { Visible = true, Message = message };
                _scene.Controls.Add(_loadingScreen);
                _loadingScreen.BringToFront();
            }
            else
            {
                _loadingScreen.Visible = true;
                _loadingScreen.Message = message;
                _loadingScreen.BringToFront();
            }
        }

        public void DisposeLoadingScreen()
        {
            if (_loadingScreen != null)
            {
                _scene.Controls.Remove(_loadingScreen);
                _loadingScreen.Dispose();
                _loadingScreen = null;
            }
        }

        public void UpdateLoadProgress(string message, float progress)
        {
            void ApplyProgress()
            {
                if (_loadingScreen == null)
                    return;

                _loadingScreen.Message = message;
                _loadingScreen.Progress = progress;
            }

            if (MuGame.IsMainThread)
            {
                ApplyProgress();
                return;
            }

            MuGame.ScheduleOnMainThread(
                ApplyProgress,
                MainThreadDispatcher.WorkPriority.High,
                "MapChange.UpdateLoadProgress");
        }

        public void UpdateLoading(GameTime gameTime)
        {
            _loadingScreen?.Update(gameTime);
        }

        public async Task ChangeMap([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type worldType)
        {
            ArgumentNullException.ThrowIfNull(worldType);
            if (_isChangingWorld)
            {
                _logger?.LogWarning("Ignoring overlapping map change to {WorldType}.", worldType.Name);
                return;
            }

            _isChangingWorld = true;
            WorldControl previousWorld = _scene.World;
            WorldControl nextWorld = null;

            try
            {
                // End the network-handler/respawn dispatcher action before any scene mutation.
                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.Begin",
                    MainThreadDispatcher.WorkPriority.Critical);

                // The old world keeps updating until the new one is shown; its area audio (pubs, buildings,
                // rain, looping objects) would follow the hero's new coordinates and override the new map's.
                if (previousWorld != null)
                    previousWorld.AudioSuppressed = true;

                _scopeImportController?.ClearObjectTracking();
                EnsureLoadingScreen($"Loading {worldType.Name}...");
                _loadingScreen.Progress = 0f;
                _hud.Visible = false;

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.PrepareHero",
                    MainThreadDispatcher.WorkPriority.Critical);

                _scene.Hero.Hidden = true;
                _scene.Hero.Reset();
                _loadingScreen.Progress = 0.05f;

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.CreateWorld",
                    MainThreadDispatcher.WorkPriority.Critical);

                nextWorld = (WorldControl)Activator.CreateInstance(worldType);
                nextWorld.Visible = false;
                if (nextWorld is WalkableWorldControl walkable)
                    walkable.Walker = _scene.Hero;

                // Warm the destination audio cache while the world is still initializing.
                string nextMusic = nextWorld.BackgroundMusicPath;
                if (string.IsNullOrEmpty(nextMusic) && nextWorld.HasWorldInfo)
                    nextMusic = WorldAudioDatabase.GetMusic(nextWorld.MapId);
                if (!string.IsNullOrEmpty(nextMusic))
                    SoundController.Instance.PreloadBackgroundMusic(nextMusic);

                string nextAmbient = nextWorld.AmbientSoundPath;
                if (string.IsNullOrEmpty(nextAmbient) && nextWorld.HasWorldInfo)
                    nextAmbient = WorldAudioDatabase.GetAmbient(nextWorld.MapId);
                if (!string.IsNullOrEmpty(nextAmbient))
                    SoundController.Instance.PreloadSound(nextAmbient);

                // Attach the shell so initialization can resolve Scene and GraphicsDevice, but
                // keep it hidden and leave Scene.World on the old world until warm-up completes.
                _scene.Controls.Add(nextWorld);
                _loadingScreen.Progress = 0.1f;

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.InitializeWorld",
                    MainThreadDispatcher.WorkPriority.Critical);

                _logger?.LogDebug("GameScene.ChangeMap<{World}>: Initializing hidden world...", worldType.Name);
                await nextWorld.Initialize();

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.AttachHiddenWorld",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Make imports target the hidden replacement world, but keep rendering on the
                // loading screen until all available objects and resources have been prepared.
                previousWorld?.Objects.Remove(_scene.Hero);
                _scene.Hero.World = nextWorld;
                _scene.Hero.SnapToTerrainHeight(updateCamera: false);
                if (!nextWorld.Objects.Contains(_scene.Hero))
                    nextWorld.Objects.Add(_scene.Hero);
                _scene.SetWorldInternal(nextWorld);

                // The local PlayerObject survives map changes, therefore the server does not have
                // to resend slot 8. Explicitly restore the helper from the current equipment state
                // and reset its world-local spawn/interpolation state for the destination map.
                await _scene.Hero.RestoreEquippedHelperAfterWorldChangeAsync();

                if (nextWorld.Status == GameControlStatus.Ready)
                {
                    _loadingScreen.Progress = 0.45f;
                    await (_scopeImportController?.ImportPendingRemotePlayersAsync() ?? Task.CompletedTask);
                    await MuGame.YieldToNextFrameAsync(
                        $"MapChange.{worldType.Name}.ImportNpcs",
                        MainThreadDispatcher.WorkPriority.High);
                    await (_scopeImportController?.ImportPendingNpcsMonstersAsync() ?? Task.CompletedTask);
                    await MuGame.YieldToNextFrameAsync(
                        $"MapChange.{worldType.Name}.ImportDrops",
                        MainThreadDispatcher.WorkPriority.High);
                    await (_scopeImportController?.ImportPendingDroppedItemsAsync() ?? Task.CompletedTask);
                }
                else
                {
                    _logger?.LogWarning(
                        "GameScene.ChangeMap: World not ready after Initialize (Status: {Status}).",
                        nextWorld.Status);
                }

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.PrepareResources",
                    MainThreadDispatcher.WorkPriority.Critical);
                nextWorld.PrepareInitialVisibilitySnapshot();
                await nextWorld.PrepareInitialRenderResourcesAsync(
                    $"MapChange.{worldType.Name}.PrewarmModel");
                _scene.Hero.PrepareRenderResourcesForFirstFrame();

                _loadingScreen.Progress = 0.7f;
                await _scene.QueueWorldActivationAfterLoadingFrame(() =>
                {
                    // Re-sample the destination terrain at the exact activation point. This
                    // also updates the camera target before the first visible gameplay frame.
                    _scene.Hero.SnapToTerrainHeight();
                    nextWorld.PrepareInitialVisibilitySnapshot();
                    nextWorld.Visible = true;
                    _scene.Hero.Hidden = false;
                    CharacterSpawnEffect.Start(_scene.Hero);
                }, $"MapChange.{worldType.Name}.Activate", cleanupLoadingUi: false);

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.DisposePrevious",
                    MainThreadDispatcher.WorkPriority.High);

                if (previousWorld != null)
                {
                    _scene.Controls.Remove(previousWorld);
                    previousWorld.Dispose();
                    previousWorld = null;
                    _logger?.LogDebug("GameScene.ChangeMap: Disposed previous world.");
                }
                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.NotifyReady",
                    MainThreadDispatcher.WorkPriority.High);

                _loadingScreen.Progress = 0.95f;
                await MuGame.Network.SendClientReadyAfterMapChangeAsync();

                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.Finalize",
                    MainThreadDispatcher.WorkPriority.Critical);

                nextWorld.PrepareInitialVisibilitySnapshot();
                _scene.ScheduleMapNameUpdateNextFrame($"MapChange.{worldType.Name}.UpdateMapName");
                _ = _scene.RefreshMiniMapAsync();
                MuGame.ResetFramePerformanceWindow($"world {nextWorld.WorldIndex} ready");
                _logger?.LogDebug("GameScene.ChangeMap<{World}>: ChangeMap completed.", worldType.Name);

                // Break the nested async continuation chain before the cleanup/final caller
                // continuations can run inside MapChange.*.Finalize.
                await Task.Yield();
            }
            catch
            {
                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.Rollback",
                    MainThreadDispatcher.WorkPriority.Critical);

                if (previousWorld != null)
                {
                    // The previous world is still alive, so a complete rollback is safe even
                    // when the replacement had already been published for one loading frame.
                    if (nextWorld != null && !ReferenceEquals(nextWorld, previousWorld))
                    {
                        nextWorld.Objects.Remove(_scene.Hero);
                        _scene.Controls.Remove(nextWorld);
                        nextWorld.Dispose();
                        nextWorld = null;
                    }

                    _scene.SetWorldInternal(previousWorld);
                    if (previousWorld is WalkableWorldControl previousWalkable)
                        previousWalkable.Walker = _scene.Hero;

                    _scene.Hero.World = previousWorld;
                    _scene.Hero.SnapToTerrainHeight();
                    if (!previousWorld.Objects.Contains(_scene.Hero))
                        previousWorld.Objects.Add(_scene.Hero);

                    previousWorld.Visible = true;
                    previousWorld.AudioSuppressed = false;
                    previousWorld.ApplyWorldAudio();
                }
                else if (nextWorld != null)
                {
                    // The old world was already disposed. Keep the initialized replacement
                    // attached instead of leaving the scene without a valid WorldControl.
                    _scene.SetWorldInternal(nextWorld);
                    nextWorld.Visible = true;
                }

                _scene.Hero.Hidden = false;
                throw;
            }
            finally
            {
                await MuGame.YieldToNextFrameAsync(
                    $"MapChange.{worldType.Name}.Cleanup",
                    MainThreadDispatcher.WorkPriority.Critical);

                DisposeLoadingScreen();
                if (_progressBar != null)
                    _progressBar.Visible = false;
                _hud.Visible = true;
                _isChangingWorld = false;
            }
        }

        public void UpdateMapName()
        {
            if (string.IsNullOrEmpty(_scene.World?.Name))
                return;

            if (_currentMapNameControl == null)
            {
                _currentMapNameControl = new MapNameControl();
                _scene.Controls.Add(_currentMapNameControl);
            }

            _currentMapNameControl.ShowMapName(_scene.World.Name);
            _currentMapNameControl.BringToFront();
            _chatLog?.BringToFront();
            _chatInput?.BringToFront();
            _mapListControl?.BringToFront();
            _debugPanel?.BringToFront();
            _cursor?.BringToFront();
        }
    }
}
