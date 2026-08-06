using Client.Main.Configuration;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Data;
using Client.Main.Diagnostics;
using Client.Main.Graphics;
using Client.Main.Networking;
using Client.Main.Objects;
using Client.Main.Scenes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Client.Telemetry;


namespace Client.Main
{
    public class MuGame : Game
    {
        private const string LocalSettingsFileName = "appsettings.local.json";
        public readonly record struct SlowFrameSnapshot(
            long Sequence,
            long FrameIndex,
            long ObservedTimestamp,
            double CpuFrameMs,
            double UpdateMs,
            double DrawMs,
            double AllocatedKb,
            double UpdateAllocatedKb,
            double DrawAllocatedKb,
            double ProcessAllocatedKb,
            UpdatePassProfiler.Snapshot UpdatePasses,
            RenderPassProfiler.Snapshot RenderPasses,
            double LongestObjectUpdateMs,
            string LongestObjectUpdateType,
            string LongestObjectUpdateName,
            ushort LongestObjectUpdateNetworkId);

        public readonly record struct DrawExceptionSnapshot(
            long Sequence,
            long FrameIndex,
            long ObservedTimestamp,
            string Phase,
            string ExceptionType,
            string Message);

        private const int MaxMainThreadActionsPerFrame = 96;
        private static readonly TimeSpan MaxMainThreadActionTimePerFrame = TimeSpan.FromMilliseconds(2);
        private static readonly TimeSpan SimulationFixedStep = TimeSpan.FromSeconds(1.0 / 60.0);
        private const int SimulationMaxStepsPerFrame = 4;
        private static readonly TimeSpan SimulationMaxAcceptedElapsed = TimeSpan.FromMilliseconds(250);
        // Static Fields
        private static Controllers.TaskScheduler _taskScheduler;
        private static readonly MainThreadDispatcher _mainThreadDispatcher = new(null, MaxMainThreadActionsPerFrame, MaxMainThreadActionTimePerFrame);
        private static int _mainThreadId;
        private static readonly FrameProfiler _frameProfiler = new();
        private static long _slowFrameSequence;
        private static SlowFrameSnapshot _latestSlowFrame;
        private static long _drawExceptionSequence;
        private static DrawExceptionSnapshot _latestDrawException;

        // Static Properties
        public static MuGame Instance { get; private set; }
        public static Random Random { get; } = new Random();
        public static IConfiguration AppConfiguration { get; private set; }
        public static ILoggerFactory AppLoggerFactory { get; private set; }
        public static MuOnlineSettings AppSettings { get; private set; }
        public static NetworkManager Network { get; private set; }
        public static string ConfigDirectory { get; private set; }
        public static string LocalSettingsPath => Path.Combine(ConfigDirectory ?? AppContext.BaseDirectory, LocalSettingsFileName);
        public static Controllers.TaskScheduler TaskScheduler => _taskScheduler;
        public static int FrameIndex { get; private set; }
        public static int MainThreadPendingActions => _mainThreadDispatcher.PendingCount;
        public static bool IsMainThread => _mainThreadId != 0 && Environment.CurrentManagedThreadId == _mainThreadId;
        public static int MainThreadProcessedActionsLastFrame => _mainThreadDispatcher.LastProcessedCount;
        public static long MainThreadProcessedActionsTotal => _mainThreadDispatcher.TotalProcessedCount;
        public static double MainThreadProcessingMs => _mainThreadDispatcher.LastProcessDurationMs;
        public static double MainThreadLongestActionMs => _mainThreadDispatcher.LastLongestActionDurationMs;
        public static double MainThreadLongestActionQueueMs => _mainThreadDispatcher.LastLongestActionQueueMs;
        public static string MainThreadLongestActionName => _mainThreadDispatcher.LastLongestActionName;
        public static bool MainThreadBudgetExceeded => _mainThreadDispatcher.LastBudgetExceeded;
        public static double MainThreadBudgetOverrunMs => _mainThreadDispatcher.LastBudgetOverrunMs;
        public static MainThreadDispatcher.SlowActionSnapshot MainThreadLatestSlowAction => _mainThreadDispatcher.LatestSlowAction;
        public static int LastSimulationStepCount { get; private set; }
        public static double LastSimulationAcceptedElapsedMs { get; private set; }
        public static double LastSimulationAccumulationAlpha { get; private set; }
        public static FrameProfiler.Snapshot FramePerformance => _frameProfiler.Current;
        public static SlowFrameSnapshot LatestSlowFrame => _latestSlowFrame;
        public static DrawExceptionSnapshot LatestDrawException => _latestDrawException;
        public static TelemetryPublisher Diagnostics => Instance?._telemetryPublisher;

        // Instance Fields
        private readonly GraphicsDeviceManager _graphics;
        private ILogger _logger = AppLoggerFactory?.CreateLogger<MuGame>();
        private readonly DeterministicFrameClock _simulationClock = new(
            SimulationFixedStep,
            SimulationMaxStepsPerFrame,
            SimulationMaxAcceptedElapsed);
        private bool _networkDisposed = false;
        private CharacterState _characterState;
        private ScopeManager _scopeManager;
        private readonly SemaphoreSlim _sceneChangeLock = new(1, 1);
        private int _sceneChangeGeneration;
        private float _scaleFactor;
        private TelemetryPublisher _telemetryPublisher;
        private bool _effectCacheValid = false;
        private Point _lastEffectTargetSize;
        private Matrix _cachedEffectOrtho;
        private Vector2 _cachedEffectResolution;
        private Vector2 _lastValidMouseInBackBuffer;
        private ulong _lastMouseRayCameraVersion = ulong.MaxValue;
        private int _lastSlowFrameEventFrame = -1000;
        private int _lastHighAllocationEventFrame = -1000;
        private const double SlowCpuFrameEventThresholdMs = 25d;
        private const double HighAllocationFrameThresholdKb = 256d;
        private const int PassiveDetailedProfileIntervalFrames = 15;
        private const double DetailedProfileContinuationThresholdMs = 16.0d;
        private static readonly Color FallbackClearColor = new(12, 12, 20);
        private string _currentDrawPhase = "Idle";
        private bool _detailedPassProfilingThisFrame;

        // Public Instance Properties
        public BaseScene ActiveScene { get; private set; }
        public int Width => _graphics.PreferredBackBufferWidth;
        public int Height => _graphics.PreferredBackBufferHeight;
        public bool IsVSyncEnabled => _graphics.SynchronizeWithVerticalRetrace;
        public GameWindow GameWindow => this.Window;
        public MouseState PrevMouseState { get; private set; }
        public MouseState Mouse { get; set; }
        public MouseState PrevUiMouseState { get; private set; }
        public MouseState UiMouseState { get; private set; }
        public Point UiMousePosition { get; private set; }
        public KeyboardState PrevKeyboard { get; private set; }
        public KeyboardState Keyboard { get; private set; }
        public TouchCollection PrevTouchState { get; private set; }
        public TouchCollection Touch { get; private set; }
        public Point UiTouchPosition { get; private set; }
        public Ray MouseRay { get; private set; }
        /// <summary>
        /// Mouse position converted to back buffer space (for 3D raycasting).
        /// In fullscreen borderless mode, window size may differ from back buffer size.
        /// </summary>
        public Vector2 MouseInBackBuffer { get; private set; }
        public GameTime GameTime { get; private set; }
        public DepthStencilState DisableDepthMask { get; } = new DepthStencilState
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = false
        };

        // Constructors
        public MuGame()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
            Instance = this;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1280,
                PreferredBackBufferHeight = 720,
                PreferMultiSampling = Constants.MSAA_ENABLED,
                HardwareModeSwitch = false // Required for dynamic resolution changes at runtime
            };

            if (OperatingSystem.IsAndroid())
            {
                _graphics.IsFullScreen = true;
                _graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
                _graphics.SynchronizeWithVerticalRetrace = true;
                // Screen size will be configured in Initialize() using GraphicsAdapter
                IsFixedTimeStep = false;
                TargetElapsedTime = TimeSpan.FromMilliseconds(16.67);
            }
            else if (OperatingSystem.IsIOS())
            {
                _graphics.IsFullScreen = true;
                _graphics.SynchronizeWithVerticalRetrace = true;
                IsFixedTimeStep = false;
                TargetElapsedTime = TimeSpan.FromMilliseconds(16.67);
            }
            else if (Constants.UNLIMITED_FPS)
            {
                _graphics.SynchronizeWithVerticalRetrace = false;
                IsFixedTimeStep = false;
                TargetElapsedTime = TimeSpan.FromMilliseconds(1);
            }

            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
            _graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            _graphics.ApplyChanges();

            // UiScaler configuration moved to Initialize() - Window.ClientBounds may be invalid in constructor

            Content.RootDirectory = "Content";

            // Register handler for game exit (X button, Alt+F4, etc.)
            // This ensures proper cleanup and process termination.
            this.Exiting += OnGameExiting;
        }

        /// <summary>
        /// Schedules an action to be executed on the main game thread during the next Update cycle.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public static void ScheduleOnMainThread(
            Action action,
            MainThreadDispatcher.WorkPriority priority = MainThreadDispatcher.WorkPriority.Normal,
            string name = null)
        {
            _mainThreadDispatcher.Enqueue(action, priority, name);
        }

        /// <summary>
        /// Schedules a stateful action to be executed on the main game thread without creating a closure.
        /// </summary>
        public static void ScheduleOnMainThread<TState>(
            Action<TState> action,
            TState state,
            MainThreadDispatcher.WorkPriority priority = MainThreadDispatcher.WorkPriority.Normal,
            string name = null)
        {
            _mainThreadDispatcher.Enqueue(action, state, priority, name);
        }

        /// <summary>
        /// Schedules an async action to be started on the main game thread.
        /// </summary>
        public static void ScheduleOnMainThread(
            Func<Task> action,
            MainThreadDispatcher.WorkPriority priority = MainThreadDispatcher.WorkPriority.Normal,
            string name = null)
        {
            _mainThreadDispatcher.Enqueue(action, priority, name);
        }

        /// <summary>
        /// Suspends the current async workflow until the dispatcher reaches the next update.
        /// The continuation is completed on the game thread, which allows expensive scene and
        /// world transitions to be split into named, frame-budgeted phases without touching
        /// graphics resources from a worker thread.
        /// </summary>
        public static MainThreadNextFrameAwaitable YieldToNextFrameAsync(
            string name,
            MainThreadDispatcher.WorkPriority priority = MainThreadDispatcher.WorkPriority.High)
            => new(
                _mainThreadDispatcher,
                priority,
                string.IsNullOrWhiteSpace(name) ? "MainThread.Yield" : name);

        /// <summary>
        /// Awaitable which schedules the async state-machine continuation itself for the next
        /// dispatcher frame. Unlike completing a TaskCompletionSource inside the dispatcher,
        /// this cannot resume a chain of parent async methods inline in the current action.
        /// </summary>
        public readonly struct MainThreadNextFrameAwaitable
        {
            private readonly MainThreadDispatcher _dispatcher;
            private readonly MainThreadDispatcher.WorkPriority _priority;
            private readonly string _name;

            internal MainThreadNextFrameAwaitable(
                MainThreadDispatcher dispatcher,
                MainThreadDispatcher.WorkPriority priority,
                string name)
            {
                _dispatcher = dispatcher;
                _priority = priority;
                _name = name;
            }

            public Awaiter GetAwaiter() => new(_dispatcher, _priority, _name);

            public readonly struct Awaiter : ICriticalNotifyCompletion
            {
                private readonly MainThreadDispatcher _dispatcher;
                private readonly MainThreadDispatcher.WorkPriority _priority;
                private readonly string _name;

                internal Awaiter(
                    MainThreadDispatcher dispatcher,
                    MainThreadDispatcher.WorkPriority priority,
                    string name)
                {
                    _dispatcher = dispatcher;
                    _priority = priority;
                    _name = name;
                }

                public bool IsCompleted => false;
                public void GetResult() { }

                public void OnCompleted(Action continuation)
                    => QueueContinuation(continuation);

                public void UnsafeOnCompleted(Action continuation)
                    => QueueContinuation(continuation);

                private void QueueContinuation(Action continuation)
                {
                    if (continuation == null)
                        throw new ArgumentNullException(nameof(continuation));

                    _dispatcher.EnqueueNextFrame(continuation, _priority, _name);
                }
            }
        }

        /// <summary>
        /// Clears rolling frame statistics after a scene or world boundary. Current-frame
        /// timing remains valid, while the next frame starts a fresh wall-time interval.
        /// </summary>
        public static void ResetFramePerformanceWindow(string reason)
        {
            _frameProfiler.ResetRollingWindow();
            Instance?._telemetryPublisher?.PublishEvent(
                "performance",
                $"Frame statistics reset: {reason}",
                TelemetrySeverity.Info);
        }

        private static bool ValidateSettings(MuOnlineSettings settings, ILogger logger)
        {
            // Reuse the validation logic from MuOnlineConsole's App.axaml.cs
            // (Ensure necessary using for TargetProtocolVersion is present)
            if (settings == null) { logger.LogError("❌ Failed to load config from 'MuOnlineSettings'."); return false; }
            bool isValid = true;
            if (string.IsNullOrWhiteSpace(settings.ConnectServerHost) || settings.ConnectServerPort == 0) { logger.LogError("❌ Connect Server host/port invalid."); isValid = false; }
            if (string.IsNullOrWhiteSpace(settings.ProtocolVersion) || !Enum.TryParse<TargetProtocolVersion>(settings.ProtocolVersion, true, out _)) // Case-insensitive parse
            { logger.LogError("❌ ProtocolVersion '{V}' invalid. Valid: {Vs}", settings.ProtocolVersion, string.Join(", ", Enum.GetNames<TargetProtocolVersion>())); isValid = false; }
            if (string.IsNullOrWhiteSpace(settings.ClientVersion)) { logger.LogWarning("⚠️ ClientVersion not set."); }
            if (string.IsNullOrWhiteSpace(settings.ClientSerial)) { logger.LogWarning("⚠️ ClientSerial not set."); }
            if (settings.Graphics == null)
            {
                logger.LogError("❌ Graphics settings missing from configuration.");
                isValid = false;
            }
            else
            {
                if (settings.Graphics.Width <= 0 || settings.Graphics.Height <= 0)
                {
                    logger.LogError("❌ Graphics resolution {W}x{H} invalid.", settings.Graphics.Width, settings.Graphics.Height);
                    isValid = false;
                }

                if (settings.Graphics.UiVirtualWidth <= 0 || settings.Graphics.UiVirtualHeight <= 0)
                {
                    logger.LogError("❌ UI virtual resolution {W}x{H} invalid.", settings.Graphics.UiVirtualWidth, settings.Graphics.UiVirtualHeight);
                    isValid = false;
                }

                settings.Graphics.DynamicLightUpdateFps = Constants.ClampPerformanceFps(settings.Graphics.DynamicLightUpdateFps);
                settings.Graphics.AnimationUpdateFps = Constants.ClampPerformanceFps(settings.Graphics.AnimationUpdateFps);
            }
            return isValid;
        }

        private static void ApplyPerformanceCapsFromSettings(GraphicsSettings graphics)
        {
            if (graphics == null)
                return;

            int dynamicLightUpdateFps = Constants.ClampPerformanceFps(graphics.DynamicLightUpdateFps);
            int animationUpdateFps = Constants.ClampPerformanceFps(graphics.AnimationUpdateFps);

            graphics.DynamicLightUpdateFps = dynamicLightUpdateFps;
            graphics.AnimationUpdateFps = animationUpdateFps;

            Constants.DYNAMIC_LIGHT_UPDATE_FPS = dynamicLightUpdateFps;
            Constants.ANIMATION_UPDATE_FPS = animationUpdateFps;
            Constants.ENABLE_CROWD_SPATIAL_CULLING = graphics.EnableCrowdSpatialCulling;
            Constants.ENABLE_WALKER_CROWD_INSTANCING = graphics.EnableWalkerCrowdInstancing;
            Constants.ENABLE_ANIMATION_THROTTLING = graphics.EnableAnimationThrottling;
            Constants.ENABLE_SHARED_ANIMATION_PALETTES = graphics.EnableSharedAnimationPalettes;
            Constants.ENABLE_BMD_MESH_BATCHING = graphics.EnableBmdMeshBatching;
            Constants.ENABLE_EFFECT_POOLING = graphics.EnableEffectPooling;
            Constants.RENDER_METRICS_LEVEL = Math.Clamp(graphics.RenderMetricsLevel, 0, 2);
        }

        public static void DisposeInstance()
        {
            if (Instance != null)
            {
                Instance.Dispose();
                Instance = null;
            }
        }

        // Public Instance Methods
        public void ChangeScene<T>() where T : BaseScene, new()
        {
            if (typeof(T) == typeof(GameScene))
            {
                // This code should never be called for GameScene anymore
                // You can throw an exception or log a warning
                throw new InvalidOperationException("GameScene requires parameters. Use ChangeScene(BaseScene newScene) instead.");
            }
            _logger.LogInformation(">>> ChangeScene<{SceneType}>() called (generic)", typeof(T).Name);
            BaseScene newScene = new T(); // Creating an instance using the parameterless constructor
            _ = ObserveSceneChangeAsync(ChangeSceneInternalAsync(newScene));
        }

        // NEW method accepting a scene instance
        public void ChangeScene(BaseScene newScene)
        {
            if (newScene == null)
            {
                _logger.LogError("Attempted to change scene to a null instance.");
                throw new ArgumentNullException(nameof(newScene));
            }
            _logger.LogInformation(">>> ChangeScene(BaseScene newScene) called with scene type: {SceneType}", newScene.GetType().Name);
            _ = ObserveSceneChangeAsync(ChangeSceneInternalAsync(newScene));
        }

        // Protected Instance Methods
        /// <summary>
        /// Gets the actual screen size using GraphicsAdapter for mobile platforms.
        /// </summary>
        private Point GetActualScreenSize()
        {
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            {
                // Use GraphicsAdapter to get actual display size
                var adapter = base.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                if (adapter != null)
                {
                    var displayMode = adapter.CurrentDisplayMode;
                    return new Point(displayMode.Width, displayMode.Height);
                }

                // Fallback to configured size
                return new Point(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            }
            else
            {
                return new Point(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            }
        }

        private static (string Directory, string FileName) ResolveSettingsFileLocation()
        {
            string configuredPath = string.IsNullOrWhiteSpace(Constants.SETTINGS_PATH)
                ? "appsettings.json"
                : Constants.SETTINGS_PATH;

            if (Path.IsPathRooted(configuredPath))
            {
                string fullPath = Path.GetFullPath(configuredPath);
                return (Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory, Path.GetFileName(fullPath));
            }

            // Prefer output folder so "dotnet run --project ..." works from any cwd.
            string outputCandidate = Path.Combine(AppContext.BaseDirectory, configuredPath);
            if (File.Exists(outputCandidate))
            {
                return (Path.GetDirectoryName(outputCandidate) ?? AppContext.BaseDirectory, Path.GetFileName(outputCandidate));
            }

            string currentDirectoryCandidate = Path.GetFullPath(configuredPath);
            return (Path.GetDirectoryName(currentDirectoryCandidate) ?? AppContext.BaseDirectory, Path.GetFileName(currentDirectoryCandidate));
        }

        protected override void Initialize()
        {
            (string configDirectory, string configFileName) = ResolveSettingsFileLocation();
            ConfigDirectory = configDirectory;
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(ConfigDirectory)
#if PERFORMANCE_RELEASE
                .AddJsonFile(configFileName, optional: false, reloadOnChange: false)
                .AddJsonFile(LocalSettingsFileName, optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.performance.json", optional: true, reloadOnChange: false);
#else
                .AddJsonFile(configFileName, optional: false, reloadOnChange: true)
                .AddJsonFile(LocalSettingsFileName, optional: true, reloadOnChange: true);
#endif
            AppConfiguration = configurationBuilder.Build();

            // --- Logging Setup ---
#if PERFORMANCE_RELEASE
            // The maximum-performance build intentionally removes providers and configuration
            // reload watchers. Error handling remains active, but disabled log calls terminate at
            // the singleton null logger without console/file I/O or provider fan-out.
            AppLoggerFactory = NullLoggerFactory.Instance;
#else
            AppLoggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddConfiguration(AppConfiguration.GetSection("Logging"));

                bool enableSimpleConsole = AppConfiguration.GetValue("Logging:SimpleConsole:Enabled", false);
                if (enableSimpleConsole)
                {
                    builder.AddSimpleConsole(options =>
                    {
                        AppConfiguration.GetSection("Logging:SimpleConsole").Bind(options);
                    });
                }
            });
#endif

            _logger = AppLoggerFactory.CreateLogger<MuGame>();
            _mainThreadDispatcher.SetLogger(_logger);
            var bootLogger = AppLoggerFactory.CreateLogger("MuGame.Boot"); // Logger for startup

            // --- Load Settings ---
            AppSettings = AppConfiguration.GetSection("MuOnlineSettings").Get<MuOnlineSettings>();
            if (AppSettings == null || !ValidateSettings(AppSettings, bootLogger)) // Add validation
            {
                bootLogger.LogCritical("❌ Invalid application settings found in appsettings.json. Shutting down.");
#if !IOS
                Exit(); // Stop the game if settings are invalid
#endif
                return;
            }
            bootLogger.LogInformation("✅ Configuration loaded.");

            AppSettings.Ui ??= new UiSettings();
            UiThemeManager.ConfigurePersistence(PersistUiTheme);
            UiThemeManager.Initialize(AppSettings.Ui.Theme, bootLogger);
            AppSettings.Ui.Theme = UiThemeManager.CurrentId.ToString();

            // --- Initialize Network Manager ---
            // Needs CharacterState and ScopeManager - create basic instances for now
            // You'll likely manage these more centrally later
            _characterState = new CharacterState(AppLoggerFactory);
            _scopeManager = new ScopeManager(AppLoggerFactory, _characterState);
            Network = new NetworkManager(AppLoggerFactory, AppSettings, _characterState, _scopeManager);
            bootLogger.LogInformation("✅ Network Manager initialized.");

            // Initialize TaskScheduler
            _taskScheduler = new Controllers.TaskScheduler(AppLoggerFactory, _mainThreadDispatcher);
            bootLogger.LogInformation("✅ TaskScheduler initialized.");

#if PERFORMANCE_RELEASE
            _telemetryPublisher = null;
#else
            var diagnosticsOptions = AppConfiguration.GetSection("Diagnostics").Get<TelemetryPublisherOptions>()
                ?? new TelemetryPublisherOptions();
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
                diagnosticsOptions.Enabled = false;

            if (diagnosticsOptions.Enabled)
            {
                _telemetryPublisher = new TelemetryPublisher(
                    diagnosticsOptions,
                    AppLoggerFactory.CreateLogger<TelemetryPublisher>());
                _telemetryPublisher.Start();
                bootLogger.LogInformation("✅ Diagnostics telemetry enabled. Pipe={PipeName}", diagnosticsOptions.PipeName);
            }
            else
            {
                _telemetryPublisher = null;
                bootLogger.LogInformation("Diagnostics telemetry is disabled.");
            }
#endif

            IsMouseVisible = false; // Keep this if you want a custom cursor

            ApplyGraphicsConfiguration(AppSettings.Graphics);
            GraphicsQualityManager.ApplyFromSettings(AppSettings.Graphics, GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter, _logger);
            ApplyPerformanceCapsFromSettings(AppSettings.Graphics);
            ApplyGraphicsOptions();

            // Configure screen size for mobile platforms AFTER graphics device is ready
            if (OperatingSystem.IsAndroid())
            {
                var screenSize = GetActualScreenSize();
                _graphics.PreferredBackBufferWidth = screenSize.X;
                _graphics.PreferredBackBufferHeight = screenSize.Y;
                _graphics.ApplyChanges();

                UiScaler.Configure(
                    screenSize.X,
                    screenSize.Y,
                    Constants.BASE_UI_WIDTH,
                    Constants.BASE_UI_HEIGHT,
                    ScaleMode.Stretch);

                bootLogger.LogInformation("✅ Android UiScaler configured: Screen={Width}x{Height}, Virtual={VWidth}x{VHeight}, ScaleX={ScaleX:F4}, ScaleY={ScaleY:F4}",
                    screenSize.X, screenSize.Y,
                    Constants.BASE_UI_WIDTH, Constants.BASE_UI_HEIGHT,
                    UiScaler.ScaleX, UiScaler.ScaleY);
            }
            else if (OperatingSystem.IsIOS())
            {
                var screenSize = GetActualScreenSize();
                _graphics.PreferredBackBufferWidth = screenSize.X;
                _graphics.PreferredBackBufferHeight = screenSize.Y;
                _graphics.ApplyChanges();

                UiScaler.Configure(
                    screenSize.X,
                    screenSize.Y,
                    Constants.BASE_UI_WIDTH,
                    Constants.BASE_UI_HEIGHT,
                    ScaleMode.Stretch);

                bootLogger.LogInformation("✅ iOS UiScaler configured: Screen={Width}x{Height}, Virtual={VWidth}x{VHeight}, ScaleX={ScaleX:F4}, ScaleY={ScaleY:F4}",
                    screenSize.X, screenSize.Y,
                    Constants.BASE_UI_WIDTH, Constants.BASE_UI_HEIGHT,
                    UiScaler.ScaleX, UiScaler.ScaleY);
            }
            else
            {
                UiScaler.Configure(
                   _graphics.PreferredBackBufferWidth,
                   _graphics.PreferredBackBufferHeight,
                   Constants.BASE_UI_WIDTH,
                   Constants.BASE_UI_HEIGHT,
                   ScaleMode.Uniform);
            }

            _logger?.LogDebug("UI scale factor set to {Scale:F3} (virtual {VirtualWidth}x{VirtualHeight} -> actual {ActualWidth}x{ActualHeight}).",
                UiScaler.Scale,
                UiScaler.VirtualSize.X,
                UiScaler.VirtualSize.Y,
                UiScaler.ActualSize.X,
                UiScaler.ActualSize.Y);

            base.Initialize();
        }

        protected override void UnloadContent()
        {
            BmdPreviewRenderer.ClearCache(releaseGpuResources: true);
            UiRenderTargetPool.Clear();
            base.UnloadContent();
            GraphicsManager.Instance.Dispose();
            DisposeNetworkSafely();   // ← only place called in UnloadContent
            DisposeTelemetrySafely();
            AppLoggerFactory?.Dispose(); // AppLoggerFactory can be null if Initialize failed early
        }

        protected override void Update(GameTime gameTime)
        {
#if PERFORMANCE_RELEASE
            _detailedPassProfilingThisFrame = false;
#else
            _frameProfiler.BeginUpdate(FrameIndex + 1L);
            _detailedPassProfilingThisFrame = ShouldCollectDetailedPassTimings();
#endif
            UpdatePassProfiler.BeginFrame(_detailedPassProfilingThisFrame);

            long dispatcherStarted = UpdatePassProfiler.Start();
            UPSCounter.Instance.CalcUPS(gameTime);

            var simStep = _simulationClock.Advance(gameTime.ElapsedGameTime);
            LastSimulationStepCount = simStep.StepCount;
            LastSimulationAcceptedElapsedMs = simStep.AcceptedElapsed.TotalMilliseconds;
            LastSimulationAccumulationAlpha = simStep.InterpolationAlpha;

            // Keep background/main-thread work on a fixed budget. Scaling it after a slow
            // frame creates a feedback loop that makes the next frame even slower.
            ProcessMainThreadActions();
            UpdatePassProfiler.AddDispatcher(dispatcherStarted);

            try // outer try
            {
                long globalStarted = UpdatePassProfiler.Start();
                GameTime = gameTime;
                FrameIndex++;
                UpdateInputInfo(gameTime);
                CheckShaderToggles();
                SunCycleManager.Update();
                Camera.Instance.UpdateShake((float)gameTime.ElapsedGameTime.TotalSeconds);
                UpdatePassProfiler.AddGlobal(globalStarted);

                long sceneStarted = UpdatePassProfiler.Start();
                try // inner try for ActiveScene.Update
                {
                    ActiveScene?.Update(gameTime);
                }
                catch (Exception sceneEx)
                {
                    long exceptionStarted = UpdatePassProfiler.Start();
                    UpdatePassProfiler.RecordSceneException(sceneEx, FrameIndex);
                    _logger?.LogCritical(sceneEx, "Unhandled exception in ActiveScene.Update ({SceneType})!", ActiveScene?.GetType().Name ?? "null");
                    UpdatePassProfiler.AddSceneException(exceptionStarted);
                    // Keep the client alive, but retain the exception in telemetry so a slow
                    // frame caused by synchronous logging is not reported as unexplained.
                }
                finally
                {
                    UpdatePassProfiler.AddScene(sceneStarted);
                }

                try // inner try for base.Update
                {
                    long frameworkStarted = UpdatePassProfiler.Start();
                    base.Update(gameTime);
                    UpdatePassProfiler.AddFramework(frameworkStarted);
                }
                catch (Exception baseEx)
                {
                    _logger?.LogCritical(baseEx, "Unhandled exception in base.Update!");
                    // Consider stopping the game
                    // Exit();
                }
            }
            catch (Exception e) // Catch other unexpected errors in Update
            {
                _logger?.LogCritical(e, "Unhandled exception in MuGame.Update loop (outside scene/base update)!");
                // Exit();
            }
            finally
            {
#if PERFORMANCE_RELEASE
                UpdatePassProfiler.EndFrame(0d);
#else
                _frameProfiler.EndUpdate();
                UpdatePassProfiler.EndFrame(_frameProfiler.Current.UpdateMs);
#endif
            }
        }


        private bool ShouldCollectDetailedPassTimings()
        {
#if PERFORMANCE_RELEASE
            return false;
#else
            if (ActiveScene?.DebugPanel?.Visible == true)
                return true;

            if (_telemetryPublisher is { Enabled: true, IsConnected: true })
                return true;

            if (_frameProfiler.Current.CpuFrameMs >= DetailedProfileContinuationThresholdMs)
                return true;

            return FrameIndex % PassiveDetailedProfileIntervalFrames == 0;
#endif
        }

        private void ProcessMainThreadActions()
        {
            _taskScheduler?.BeginFrame();
            _mainThreadDispatcher.ProcessPending();
            _taskScheduler?.EndFrame(_mainThreadDispatcher.LastProcessDurationMs);
        }

        protected override void LoadContent()
        {
            GraphicsManager.Instance.Init(GraphicsDevice, Content);

            // Warm up the item database before inventory/NPC shop packets arrive. Public
            // lookups remain synchronous for compatibility, but normally complete from memory.
            _ = ItemDatabase.PreloadAsync().ContinueWith(t =>
            {
                if (t.Exception != null)
                    _logger?.LogWarning(t.Exception, "Failed to preload item database");
            }, System.Threading.Tasks.TaskScheduler.Default);

            // --- LOAD PLAYER IDLE POSE DATA ---
            // Load bone transformations from Player.bmd for inventory item rendering
            // Equipment items (armor, boots, etc.) use LinkParentAnimation and need player bone poses
            _ = PlayerIdlePoseProvider.EnsureLoadedAsync().ContinueWith(t =>
            {
                if (t.Exception != null)
                    _logger?.LogWarning(t.Exception, "Failed to load player idle pose data for inventory rendering");
                else
                    _logger?.LogDebug("Player idle pose data loaded successfully for inventory");
            });
            // --- END PLAYER IDLE POSE DATA ---

            // --- START NETWORK CONNECTION ---
            // Start connecting to the Connect Server when the game loads
            // We do this *after* GraphicsManager is init because some UI might depend on it
            if (Network != null) // Ensure network was initialized
            {
                // Start connection without blocking LoadContent
                _ = Network.ConnectToConnectServerAsync();
            }
            else
            {
                _logger?.LogCritical("Network Manager is null during LoadContent. Cannot connect.");
                // Potentially exit or handle error
            }
            // --- END NETWORK CONNECTION ---

            // Load the initial scene (e.g., LoginScene) AFTER network connection starts.
            _ = ObserveSceneChangeAsync(ChangeSceneAsync(Constants.ENTRY_SCENE));
        }

        protected override void Draw(GameTime gameTime)
        {
#if !PERFORMANCE_RELEASE
            bool publishTelemetry = _telemetryPublisher?.TryBeginSnapshot() == true;
            _frameProfiler.BeginDraw();
#endif

            // Section profilers are sampled during normal gameplay and become continuous
            // whenever diagnostics are visible/connected or the previous frame was slow.
            // The overall FrameProfiler remains active every frame.
            RenderPassProfiler.BeginFrame(_detailedPassProfilingThisFrame);
            _currentDrawPhase = "Frame.Begin";
            try
            {
                _currentDrawPhase = "Frame.Metrics";
                DynamicBufferPool.BeginFrame(FrameIndex);
                BMDLoader.Instance.BeginFrame();
                ModelObject.BeginFrameGpuSkinningMetrics();

                FPSCounter.Instance.CalcFPS(gameTime);
                bool recoveredFrame = false;
                if (ShouldUseIntermediateRenderTarget())
                {
                    _currentDrawPhase = "RenderTargets.Ensure";
                    bool alphaRgbEnabled = GraphicsManager.Instance.IsAlphaRGBEnabled && GraphicsManager.Instance.AlphaRGBEffect != null;
                    bool fxaaEnabled = GraphicsManager.Instance.IsFXAAEnabled && GraphicsManager.Instance.FXAAEffect != null;
                    int postProcessPasses = (alphaRgbEnabled ? 1 : 0) + (fxaaEnabled ? 1 : 0);
                    bool gameSceneRecovery = ActiveScene is GameScene;
                    GraphicsManager.Instance.EnsureRenderTargets(
                        requireTempTarget1: postProcessPasses >= 1,
                        requireTempTarget2: postProcessPasses >= 2,
                        requireRecoveryTarget: gameSceneRecovery);

                    bool containedRenderFailure = DrawSceneToMainRenderTarget(gameTime);
                    if (gameSceneRecovery &&
                        containedRenderFailure &&
                        GraphicsManager.Instance.HasRecoveryFrame)
                    {
                        // WorldControl contains individual model/batch failures so one bad newly
                        // loaded object does not terminate the client. Do not treat that partial
                        // render as a complete frame or replace the last known-good scene with it.
                        _currentDrawPhase = "Frame.RecoverContainedRenderFailure";
                        DrawEmergencyFallbackFrame();
                        recoveredFrame = true;
                    }
                    else
                    {
                        _currentDrawPhase = "PostProcess";
                        var postProcessStarted = RenderPassProfiler.Start();
                        ApplyPostProcessingEffects();
                        RenderPassProfiler.AddPostProcess(postProcessStarted);
                    }
                }
                else
                {
                    DrawSceneDirectToBackBuffer(gameTime);
                }

                _currentDrawPhase = "FrameworkDraw";
                var frameworkDrawStarted = RenderPassProfiler.Start();
                base.Draw(gameTime);
                RenderPassProfiler.AddFrameworkDraw(frameworkDrawStarted);

                // Commit only a fully completed GameScene frame. The next frame renders into
                // the alternate target after a deterministic black clear, while the just-
                // completed image remains untouched for emergency recovery.
                if (ActiveScene is GameScene && !recoveredFrame)
                    GraphicsManager.Instance.CommitSceneFrame();

                _currentDrawPhase = "Frame.Complete";
            }
            catch (Exception exception)
            {
                RecordDrawException(exception, _currentDrawPhase);
                DrawEmergencyFallbackFrame();
            }
            finally
            {
                try
                {
                    GraphicsDevice.SetRenderTarget(null);
                }
                catch (Exception resetException)
                {
                    RecordDrawException(resetException, "Frame.Finally.ResetRenderTarget");
                }

                _currentDrawPhase = "Idle";
#if !PERFORMANCE_RELEASE
                _frameProfiler.EndDraw();
                PublishSlowCpuFrameEvent();
                PublishHighAllocationFrameEvent();
                if (publishTelemetry)
                    _telemetryPublisher?.PublishSnapshot(this);
#endif
            }
        }

        private void RecordDrawException(Exception exception, string phase)
        {
            long sequence = Interlocked.Increment(ref _drawExceptionSequence);
            _latestDrawException = new DrawExceptionSnapshot(
                sequence,
                FrameIndex,
                Stopwatch.GetTimestamp(),
                phase ?? "Unknown",
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message);

            _logger?.LogError(
                exception,
                "Unhandled draw exception in phase {DrawPhase} on frame {FrameIndex}. Emergency fallback frame will be presented.",
                phase,
                FrameIndex);

            _telemetryPublisher?.PublishEvent(
                "draw-exception",
                $"Unhandled draw exception in {phase}",
                TelemetrySeverity.Error,
                new Dictionary<string, string>
                {
                    ["phase"] = phase ?? "Unknown",
                    ["frameIndex"] = FrameIndex.ToString(CultureInfo.InvariantCulture),
                    ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                    ["message"] = exception.Message
                });
        }

        private void DrawEmergencyFallbackFrame()
        {
            try
            {
                GraphicsDevice.SetRenderTarget(null);
                Helpers.SpriteBatchScope.ForceReset();

                SpriteBatch sprite = GraphicsManager.Instance?.Sprite;
                var graphics = GraphicsManager.Instance;
                RenderTarget2D preservedScene = graphics?.RecoveryRenderTarget;
                if (ActiveScene is GameScene &&
                    graphics?.HasRecoveryFrame == true &&
                    sprite != null && preservedScene != null && !preservedScene.IsDisposed)
                {
                    // Present the preserved/partially recovered scene instead of replacing the
                    // whole frame with a dark clear. GameScene renders through a preserved
                    // intermediate target specifically so a contained loading/render fault
                    // cannot produce a one-frame black flash.
                    sprite.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.Opaque,
                        GraphicsManager.GetQualityLinearSamplerState(),
                        DepthStencilState.None,
                        RasterizerState.CullNone,
                        null,
                        Matrix.Identity);
                    sprite.Draw(preservedScene, GraphicsDevice.Viewport.Bounds, Color.White);
                    sprite.End();
                    return;
                }

                GraphicsDevice.Clear(FallbackClearColor);
                Texture2D pixel = GraphicsManager.Instance?.Pixel;
                if (pixel == null || sprite == null || pixel.IsDisposed)
                    return;

                sprite.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Opaque,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity);
                sprite.Draw(pixel, GraphicsDevice.Viewport.Bounds, FallbackClearColor);
                sprite.End();
            }
            catch (Exception fallbackException)
            {
                _logger?.LogError(fallbackException, "Failed to draw emergency fallback frame.");
            }
        }

        private void PublishSlowCpuFrameEvent()
        {
            var frame = _frameProfiler.Current;
            if (frame.CpuFrameMs < SlowCpuFrameEventThresholdMs)
                return;

            var passes = RenderPassProfiler.Current;
            var updatePasses = UpdatePassProfiler.Current;
            var slowWorldMetrics = ActiveScene?.World?.FrameMetrics;
            long slowSequence = Interlocked.Increment(ref _slowFrameSequence);
            _latestSlowFrame = new SlowFrameSnapshot(
                slowSequence,
                FrameIndex,
                Stopwatch.GetTimestamp(),
                frame.CpuFrameMs,
                frame.UpdateMs,
                frame.DrawMs,
                frame.AllocatedKb,
                frame.UpdateAllocatedKb,
                frame.DrawAllocatedKb,
                frame.ProcessAllocatedKb,
                updatePasses,
                passes,
                slowWorldMetrics?.LongestObjectUpdateMs ?? 0d,
                slowWorldMetrics?.LongestObjectUpdateType,
                slowWorldMetrics?.LongestObjectUpdateName,
                slowWorldMetrics?.LongestObjectUpdateNetworkId ?? 0);

            // Avoid flooding the event stream during a sustained slow period. The latest full
            // record above is still retained and exported in every subsequent snapshot.
            if (FrameIndex - _lastSlowFrameEventFrame < 15 && frame.CpuFrameMs < 100d)
                return;

            _lastSlowFrameEventFrame = FrameIndex;
            var world = ActiveScene?.World;
            var worldMetrics = world?.FrameMetrics;
            var terrainMetrics = world?.Terrain?.FrameMetrics;
            int individuallyDrawnGpuSkinnedMeshes = Math.Max(
                0,
                ModelObject.LastFrameGpuSkinnedMeshesDrawn
                - ModelObject.LastFrameGpuSkinnedBatchedMeshes
                - ModelObject.LastFrameStaticMapInstancedMeshInstances
                - ModelObject.LastFrameWalkerCrowdMultiPoseMeshInstances);
            int estimatedDrawCalls = (terrainMetrics?.DrawCalls ?? 0)
                + individuallyDrawnGpuSkinnedMeshes
                + ModelObject.LastFrameGpuSkinnedBatchDrawCalls
                + ModelObject.LastFrameStaticMapInstancedDrawCalls
                + ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls
                + ModelObject.LastFrameModelFallbackDrawCalls;
            string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

            var properties = new Dictionary<string, string>(20)
            {
                ["frameIndex"] = FrameIndex.ToString(CultureInfo.InvariantCulture),
                ["scene"] = ActiveScene?.GetType().Name ?? "None",
                ["worldIndex"] = world?.WorldIndex.ToString(CultureInfo.InvariantCulture) ?? "",
                ["cpuFrameMs"] = Number(frame.CpuFrameMs),
                ["updateMs"] = Number(frame.UpdateMs),
                ["drawMs"] = Number(frame.DrawMs),
                ["sceneDrawMs"] = Number(passes.SceneDrawMs),
                ["worldObjectsMs"] = Number(passes.WorldObjectsMs),
                ["terrainOpaqueMs"] = Number(passes.TerrainOpaqueMs),
                ["frameworkDrawMs"] = Number(passes.FrameworkDrawMs),
                ["allocatedKb"] = Number(frame.AllocatedKb),
                ["updateAllocatedKb"] = Number(_frameProfiler.LastUpdateAllocatedKb),
                ["drawAllocatedKb"] = Number(_frameProfiler.LastDrawAllocatedKb),
                ["visibleObjects"] = (worldMetrics?.VisibleObjects ?? 0).ToString(CultureInfo.InvariantCulture),
                ["estimatedDrawCalls"] = estimatedDrawCalls.ToString(CultureInfo.InvariantCulture),
                ["terrainDrawCalls"] = (terrainMetrics?.DrawCalls ?? 0).ToString(CultureInfo.InvariantCulture),
                ["mainAction"] = MainThreadLongestActionName ?? string.Empty,
                ["mainActionMs"] = Number(MainThreadLongestActionMs),
                ["mainQueue"] = MainThreadPendingActions.ToString(CultureInfo.InvariantCulture),
                ["isActive"] = IsActive.ToString(),
                ["updateDispatcherMs"] = Number(updatePasses.DispatcherMs),
                ["updateGlobalMs"] = Number(updatePasses.GlobalMs),
                ["updateSceneMs"] = Number(updatePasses.SceneMs),
                ["updateFrameworkMs"] = Number(updatePasses.FrameworkMs),
                ["sceneInputMs"] = Number(updatePasses.SceneInputMs),
                ["sceneControlTreeMs"] = Number(updatePasses.SceneControlTreeMs),
                ["scenePostMs"] = Number(updatePasses.ScenePostMs),
                ["worldUpdateBaseMs"] = Number(updatePasses.WorldBaseMs),
                ["worldInitializationMs"] = Number(updatePasses.WorldInitializationMs),
                ["worldVisibilityMs"] = Number(updatePasses.WorldVisibilityMs),
                ["worldUpdateCullMs"] = Number(updatePasses.WorldCullMs),
                ["worldHoverMs"] = Number(updatePasses.WorldHoverMs),
                ["updateUnaccountedMs"] = Number(updatePasses.UnaccountedMs),
                ["sceneExceptionMs"] = Number(updatePasses.SceneExceptionMs),
                ["sceneExceptionType"] = updatePasses.SceneExceptionType ?? string.Empty,
                ["sceneDrawAllocatedKb"] = Number(passes.SceneDrawAllocatedKb),
                ["worldObjectsAllocatedKb"] = Number(passes.WorldObjectsAllocatedKb),
                ["terrainOpaqueAllocatedKb"] = Number(passes.TerrainOpaqueAllocatedKb),
                ["frameworkDrawAllocatedKb"] = Number(passes.FrameworkDrawAllocatedKb)
            };

            _telemetryPublisher?.PublishEvent(
                "slow-frame",
                $"CPU frame {frame.CpuFrameMs:F1} ms in {properties["scene"]}",
                frame.CpuFrameMs >= 50d ? TelemetrySeverity.Warning : TelemetrySeverity.Info,
                properties);
        }

        private void PublishHighAllocationFrameEvent()
        {
            var frame = _frameProfiler.Current;
            if (frame.AllocatedKb < HighAllocationFrameThresholdKb ||
                FrameIndex - _lastHighAllocationEventFrame < 120)
            {
                return;
            }

            _lastHighAllocationEventFrame = FrameIndex;
            var passes = RenderPassProfiler.Current;
            var properties = new Dictionary<string, string>(14)
            {
                ["frameIndex"] = FrameIndex.ToString(CultureInfo.InvariantCulture),
                ["scene"] = ActiveScene?.GetType().Name ?? "None",
                ["worldIndex"] = ActiveScene?.World?.WorldIndex.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["allocatedKb"] = frame.AllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["processAllocatedKb"] = frame.ProcessAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["updateAllocatedKb"] = _frameProfiler.LastUpdateAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["drawAllocatedKb"] = _frameProfiler.LastDrawAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["updateMs"] = frame.UpdateMs.ToString("F3", CultureInfo.InvariantCulture),
                ["drawMs"] = frame.DrawMs.ToString("F3", CultureInfo.InvariantCulture),
                ["sceneDrawMs"] = passes.SceneDrawMs.ToString("F3", CultureInfo.InvariantCulture),
                ["worldObjectsMs"] = passes.WorldObjectsMs.ToString("F3", CultureInfo.InvariantCulture),
                ["sceneDrawAllocatedKb"] = passes.SceneDrawAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["worldObjectsAllocatedKb"] = passes.WorldObjectsAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["terrainOpaqueAllocatedKb"] = passes.TerrainOpaqueAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["frameworkDrawAllocatedKb"] = passes.FrameworkDrawAllocatedKb.ToString("F3", CultureInfo.InvariantCulture),
                ["visibleObjects"] = (ActiveScene?.World?.FrameMetrics.VisibleObjects ?? 0).ToString(CultureInfo.InvariantCulture),
                ["isActive"] = IsActive.ToString()
            };

            _telemetryPublisher?.PublishEvent(
                "high-allocation",
                $"Allocated {frame.AllocatedKb:F0} KB in one frame ({properties["scene"]})",
                TelemetrySeverity.Warning,
                properties);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeNetworkSafely();   // ← won't be called a second time
                _taskScheduler?.Dispose();
                DisposeTelemetrySafely();
                AppLoggerFactory?.Dispose();
            }
            base.Dispose(disposing);
            Instance = null;
        }

        // Private Instance Methods
        // Private helper method for changing scenes. Scene changes are serialized so two
        // network/UI requests cannot dispose and initialize scenes concurrently.
        private async Task ChangeSceneInternalAsync(BaseScene newScene)
        {
            if (newScene == null)
                throw new ArgumentNullException(nameof(newScene));

            int requestGeneration = Interlocked.Increment(ref _sceneChangeGeneration);
            await _sceneChangeLock.WaitAsync();
            try
            {
                if (requestGeneration != Volatile.Read(ref _sceneChangeGeneration))
                {
                    newScene.Dispose();
                    return;
                }

                string sceneName = newScene.GetType().Name;
                _logger?.LogInformation("--- Scene change starting: {SceneType}", sceneName);
                _telemetryPublisher?.PublishEvent("scene", $"Loading {sceneName}", TelemetrySeverity.Info);

                // Returning to the dispatcher before transition work prevents the event
                // handler which requested the scene from owning the complete transition cost.
                await YieldToNextFrameAsync(
                    $"SceneChange.{sceneName}.Prepare",
                    MainThreadDispatcher.WorkPriority.Critical);

                BaseScene previousScene = ActiveScene;
                bool activateBeforeInitialization =
                    previousScene == null || newScene.CanRenderWhileInitializing;
                if (activateBeforeInitialization)
                {
                    try
                    {
                        // GameScene builds its loading shell while the previous scene is still
                        // visible. Its first active frame therefore already contains the
                        // background and progress UI instead of a cleared black target.
                        await newScene.PrepareForFirstPresentedFrameAsync();
                    }
                    catch (Exception preparationException)
                    {
                        try
                        {
                            newScene.Dispose();
                        }
                        catch (Exception disposeException)
                        {
                            _logger?.LogDebug(disposeException, "Failed disposing scene after presentation preparation error.");
                        }

                        _logger?.LogError(
                            preparationException,
                            "Scene presentation preparation failed for {SceneType}.",
                            sceneName);
                        _telemetryPublisher?.PublishEvent(
                            "scene",
                            $"Failed to prepare {sceneName}: {preparationException.Message}",
                            TelemetrySeverity.Error);
                        return;
                    }

                    if (requestGeneration != Volatile.Read(ref _sceneChangeGeneration))
                    {
                        newScene.Dispose();
                        return;
                    }
                }

                // Reserve the progressive path before the scene becomes active. Otherwise
                // GameControl.Update can start the generic Initialize() method on the first
                // transition frame and race the explicit scene-loading workflow.
                newScene.ReserveProgressiveInitialization();

                ushort? sourceMapId = previousScene?.World?.MapId;
                bool enteringGameFromCharacterSelection =
                    newScene is GameScene && previousScene is SelectCharacterScene;
                bool preserveCurrentMapScope =
                    newScene is GameScene &&
                    (enteringGameFromCharacterSelection ||
                     (sourceMapId.HasValue && sourceMapId.Value != _characterState.MapId));
                _scopeManager?.BeginWorldTransition(
                    sourceMapId,
                    preserveCurrentMapScope,
                    acceptCurrentMapScopeUpdates: enteringGameFromCharacterSelection);
                ModelObject.ResetWorldScopedInstancingState();

                if (activateBeforeInitialization)
                {
                    // Never publish a null ActiveScene. Scenes with a complete loading shell are
                    // activated first, so rendering continues seamlessly while initialization runs.
                    ActiveScene = newScene;
                    newScene.OnActivated();
                    _frameProfiler.ResetRollingWindow();

                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.DisposePrevious",
                        MainThreadDispatcher.WorkPriority.Critical);

                    DisposePreviousScene(previousScene);

                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.ReleasePreviewCache",
                        MainThreadDispatcher.WorkPriority.High);

                    BmdPreviewRenderer.ClearCache(releaseGpuResources: true);
                }

                try
                {
                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.Initialize",
                        MainThreadDispatcher.WorkPriority.Critical);
                    await newScene.InitializeWithProgressReporting(null);

                    // A scene which was activated before initialization did not have its world
                    // yet when OnActivated was first called. Re-run the activation hook now so
                    // a newly created deferred menu-world camera is published before the next
                    // presented frame.
                    if (activateBeforeInitialization && ReferenceEquals(ActiveScene, newScene))
                        newScene.OnActivated();

                    // Scene initialization may complete from a dispatcher continuation. Force
                    // the parent transition to unwind before its final phase is queued.
                    await Task.Yield();
                }
                catch (Exception ex)
                {
                    if (ReferenceEquals(ActiveScene, newScene))
                        ActiveScene = null;

                    try
                    {
                        newScene.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        _logger?.LogDebug(disposeException, "Failed disposing scene after initialization error.");
                    }

                    _scopeManager?.CompleteWorldTransition();
                    _logger?.LogError(ex, "Scene initialization failed for {SceneType}.", sceneName);
                    _telemetryPublisher?.PublishEvent(
                        "scene",
                        $"Failed to load {sceneName}: {ex.Message}",
                        TelemetrySeverity.Error);
                    return;
                }

                if (!activateBeforeInitialization)
                {
                    // Scenes without their own initialization renderer remain off-screen until
                    // fully ready. The previous scene stays visible, then the swap happens
                    // atomically without an intermediate black frame.
                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.Activate",
                        MainThreadDispatcher.WorkPriority.Critical);

                    ActiveScene = newScene;
                    newScene.OnActivated();
                    _frameProfiler.ResetRollingWindow();

                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.DisposePrevious",
                        MainThreadDispatcher.WorkPriority.Critical);

                    DisposePreviousScene(previousScene);

                    await YieldToNextFrameAsync(
                        $"SceneChange.{sceneName}.ReleasePreviewCache",
                        MainThreadDispatcher.WorkPriority.High);

                    BmdPreviewRenderer.ClearCache(releaseGpuResources: true);
                }

                await YieldToNextFrameAsync(
                    $"SceneChange.{sceneName}.Finalize",
                    MainThreadDispatcher.WorkPriority.Critical);

                _scopeManager?.CompleteWorldTransition();
                ResetFramePerformanceWindow($"scene {sceneName} ready");
                _logger?.LogInformation("--- Scene change completed: {SceneType}", sceneName);
                _telemetryPublisher?.PublishEvent("scene", $"Loaded {sceneName}", TelemetrySeverity.Info);
            }
            finally
            {
                _sceneChangeLock.Release();
            }
        }

        private void DisposePreviousScene(BaseScene previousScene)
        {
            if (previousScene == null)
                return;

            try
            {
                previousScene.Dispose();
            }
            catch (Exception disposeException)
            {
                _logger?.LogError(
                    disposeException,
                    "Failed disposing previous scene {SceneType}.",
                    previousScene.GetType().Name);
            }
        }

        private async Task ObserveSceneChangeAsync(Task sceneChangeTask)
        {
            try
            {
                await sceneChangeTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unhandled scene change failure.");
            }
        }

        private void CheckShaderToggles()
        {
            if (Keyboard.IsKeyDown(Keys.LeftShift))
            {
                if (PrevKeyboard.IsKeyDown(Keys.F1) && Keyboard.IsKeyUp(Keys.F1))
                    GraphicsManager.Instance.IsAlphaRGBEnabled = !GraphicsManager.Instance.IsAlphaRGBEnabled;
                else if (PrevKeyboard.IsKeyDown(Keys.F2) && Keyboard.IsKeyUp(Keys.F2))
                    GraphicsManager.Instance.IsFXAAEnabled = !GraphicsManager.Instance.IsFXAAEnabled;
            }

            if (PrevKeyboard.IsKeyDown(Keys.F8) && Keyboard.IsKeyUp(Keys.F8))
                Constants.DRAW_BOUNDING_BOXES = !Constants.DRAW_BOUNDING_BOXES;
            else if (PrevKeyboard.IsKeyDown(Keys.F9) && Keyboard.IsKeyUp(Keys.F9))
                Constants.DRAW_BOUNDING_BOXES_INTERACTIVES = !Constants.DRAW_BOUNDING_BOXES_INTERACTIVES;
        }

        public void ApplyGraphicsOptions()
        {
            bool graphicsDeviceChangeRequired = false;
#if !(ANDROID || IOS)
            bool runUncapped = Constants.UNLIMITED_FPS || Constants.DISABLE_VSYNC;
            bool desiredVSync = !runUncapped;
            graphicsDeviceChangeRequired |=
                _graphics.SynchronizeWithVerticalRetrace != desiredVSync;
            _graphics.SynchronizeWithVerticalRetrace = desiredVSync;
            IsFixedTimeStep = !runUncapped;
            TargetElapsedTime = runUncapped
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromSeconds(1.0 / 60.0);
#endif
            graphicsDeviceChangeRequired |=
                _graphics.PreferMultiSampling != Constants.MSAA_ENABLED;
            _graphics.PreferMultiSampling = Constants.MSAA_ENABLED;

            // ApplyChanges recreates the backbuffer and render targets. Avoid doing that for
            // no-op UI refreshes because it can produce a visible black frame.
            if (graphicsDeviceChangeRequired)
                _graphics.ApplyChanges();
        }

        private Task ChangeSceneAsync(Type sceneType)
        {
            if (sceneType == null)
                throw new ArgumentNullException(nameof(sceneType));

            var scene = (BaseScene)Activator.CreateInstance(sceneType);
            return ChangeSceneInternalAsync(scene);
        }

        private void UpdateInputInfo(GameTime gameTime)
        {
            var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
            var touchState = TouchPanel.GetState();

            PrevMouseState = Mouse;
            PrevUiMouseState = UiMouseState;
            PrevKeyboard = Keyboard;
            PrevTouchState = Touch;

            // --- PHYSICAL DEVICES ---
            // Always update keyboard and touch state
            Keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            Touch = touchState;

            // Get back buffer and window dimensions
            int backBufferWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int backBufferHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;

            // In borderless fullscreen, Window.ClientBounds may return screen size while
            // back buffer is smaller. We need to check if mouse needs scaling.
            // Use Window.ClientBounds for the actual area where mouse events are reported.
            var clientBounds = Window.ClientBounds;
            int inputAreaWidth = clientBounds.Width > 0 ? clientBounds.Width : backBufferWidth;
            int inputAreaHeight = clientBounds.Height > 0 ? clientBounds.Height : backBufferHeight;

            // Calculate scale factors for converting input coords to back buffer coords
            float scaleX = (float)backBufferWidth / inputAreaWidth;
            float scaleY = (float)backBufferHeight / inputAreaHeight;

            // Convert mouse position from window space to back buffer space
            float mouseBackBufferX = mouseState.X * scaleX;
            float mouseBackBufferY = mouseState.Y * scaleY;
            var mouseBackBufferPos = new Vector2(mouseBackBufferX, mouseBackBufferY);

            // Check if mouse is within back buffer bounds (using converted coordinates)
            var mouseInWindow = mouseBackBufferX >= 0 && mouseBackBufferX < backBufferWidth &&
                                mouseBackBufferY >= 0 && mouseBackBufferY < backBufferHeight;

            if (!IsActive || !mouseInWindow)
            {
                Mouse = PrevMouseState;
                MouseInBackBuffer = _lastValidMouseInBackBuffer;
#if !ANDROID
                Keyboard = new KeyboardState(); // Clear keyboard on PC when inactive
#endif
            }
            else
            {
                Mouse = mouseState;
                MouseInBackBuffer = mouseBackBufferPos;
                _lastValidMouseInBackBuffer = mouseBackBufferPos;
            }

            // --- VIRTUAL MOUSE (UI) ---

            if (Touch.Count > 0)
            {
                // CASE 1: Touch detected (Android/Touchscreen)
                var touch = Touch[0];
                var touchPos = touch.Position;

                // Convert touch position to virtual UI coordinates
                var virtualTouchPos = UiScaler.ToVirtual(new Microsoft.Xna.Framework.Point((int)touchPos.X, (int)touchPos.Y));

                UiTouchPosition = virtualTouchPos;
                UiMousePosition = virtualTouchPos; // Mouse follows finger

                // Touch as left mouse button
                var leftButtonState = (touch.State == TouchLocationState.Pressed || touch.State == TouchLocationState.Moved)
                    ? Microsoft.Xna.Framework.Input.ButtonState.Pressed
                    : Microsoft.Xna.Framework.Input.ButtonState.Released;

                UiMouseState = new Microsoft.Xna.Framework.Input.MouseState(
                    virtualTouchPos.X,
                    virtualTouchPos.Y,
                    0,
                    leftButtonState,
                    Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released);
            }
            else
            {
                if (OperatingSystem.IsAndroid())
                {
                    // Keep the cursor at the last touch position when finger is lifted.
                    // This prevents the UI system from thinking we clicked outside the control.
                    // The cursor will stay at this position until next touch - we DON'T move it to (-1, -1).

                    UiMousePosition = PrevUiMouseState.Position;
                    UiTouchPosition = PrevUiMouseState.Position;

                    UiMouseState = new Microsoft.Xna.Framework.Input.MouseState(
                        UiMousePosition.X, UiMousePosition.Y,
                        0,
                        Microsoft.Xna.Framework.Input.ButtonState.Released,
                        Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released);
                }
                else
                {
                    // WINDOWS: Use standard mouse
                    UiMousePosition = UiScaler.ToVirtual(Mouse.Position);
                    UiTouchPosition = UiMousePosition;

                    UiMouseState = new Microsoft.Xna.Framework.Input.MouseState(
                        UiMousePosition.X,
                        UiMousePosition.Y,
                        Mouse.ScrollWheelValue,
                        Mouse.LeftButton,
                        Mouse.MiddleButton,
                        Mouse.RightButton,
                        Mouse.XButton1,
                        Mouse.XButton2);
                }
            }

            // Update MouseRay when mouse position changes OR when touch position changes
            bool shouldUpdateRay = false;

            if (PrevMouseState.Position != Mouse.Position)
                shouldUpdateRay = true;

            if (PrevTouchState.Count != Touch.Count)
                shouldUpdateRay = true;

            // Check if touch position changed (finger moved)
            if (Touch.Count > 0 && PrevTouchState.Count > 0)
            {
                if (Touch[0].Position != PrevTouchState[0].Position)
                    shouldUpdateRay = true;
            }

            ulong cameraVersion = Camera.Instance.StateVersion;
            if (_lastMouseRayCameraVersion != cameraVersion)
                shouldUpdateRay = true;

            if (shouldUpdateRay)
            {
                UpdateMouseRay();
                _lastMouseRayCameraVersion = cameraVersion;
            }
        }

        private void UpdateMouseRay()
        {
            // Use touch position if available, otherwise use mouse position in back buffer space
            Vector2 inputPosition;
            if (Touch.Count > 0)
            {
                // Touch position needs same scaling as mouse in fullscreen borderless mode
                var touchPos = Touch[0].Position;
                int backBufferWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
                int backBufferHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;

                var clientBounds = Window.ClientBounds;
                int inputAreaWidth = clientBounds.Width > 0 ? clientBounds.Width : backBufferWidth;
                int inputAreaHeight = clientBounds.Height > 0 ? clientBounds.Height : backBufferHeight;

                float scaleX = (float)backBufferWidth / inputAreaWidth;
                float scaleY = (float)backBufferHeight / inputAreaHeight;
                inputPosition = new Vector2(touchPos.X * scaleX, touchPos.Y * scaleY);
            }
            else
            {
                // Use pre-calculated mouse position in back buffer space
                inputPosition = MouseInBackBuffer;
            }

            // Create viewport matching actual back buffer size for correct unprojection
            // (GraphicsDevice.Viewport may be set to render target size from previous frame)
            var backBufferViewport = new Viewport(
                0, 0,
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight);

            Vector3 farSource = new Vector3(inputPosition, 1f);
            Vector3 farPoint = backBufferViewport.Unproject(
                farSource,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            Vector3 nearPoint = Camera.Instance.Position;
            Vector3 direction = farPoint - nearPoint;
            direction.Normalize();
            MouseRay = new Ray(nearPoint, direction);
        }

        private bool DrawSceneToMainRenderTarget(GameTime gameTime)
        {
            var world = ActiveScene?.World;
            long renderFailureSequence = world?.FrameMetrics.LastRenderFailureSequence ?? 0;

            GraphicsDevice.SetRenderTarget(GraphicsManager.Instance.MainRenderTarget);

            // Always start the current image from the canonical scene background. Keeping
            // color data from an older frame caused stale terrain/objects to remain visible
            // in map holes. A separate recovery target now owns the last complete frame.
            GraphicsDevice.Clear(FallbackClearColor);

            // Ensure correct culling for 3D models
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.AlphaBlend;

            _currentDrawPhase = "Scene.Draw";
            var sceneDrawStarted = RenderPassProfiler.Start();
            ActiveScene?.Draw(gameTime);
            RenderPassProfiler.AddSceneDraw(sceneDrawStarted);

            _currentDrawPhase = "Scene.DrawAfter";
            var sceneAfterStarted = RenderPassProfiler.Start();
            ActiveScene?.DrawAfter(gameTime);
            RenderPassProfiler.AddSceneAfter(sceneAfterStarted);

            GraphicsDevice.SetRenderTarget(null);
            return world != null &&
                   world.FrameMetrics.LastRenderFailureSequence != renderFailureSequence;
        }

        private void DrawSceneDirectToBackBuffer(GameTime gameTime)
        {
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(FallbackClearColor);

            // Keep the same scene state setup as the intermediate render path.
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.AlphaBlend;

            _currentDrawPhase = "Scene.Draw";
            var sceneDrawStarted = RenderPassProfiler.Start();
            ActiveScene?.Draw(gameTime);
            RenderPassProfiler.AddSceneDraw(sceneDrawStarted);

            _currentDrawPhase = "Scene.DrawAfter";
            var sceneAfterStarted = RenderPassProfiler.Start();
            ActiveScene?.DrawAfter(gameTime);
            RenderPassProfiler.AddSceneAfter(sceneAfterStarted);
        }

        private bool ShouldUseIntermediateRenderTarget()
        {
            // GameScene always renders off-screen so a late render/resource failure can
            // present preserved scene color instead of flashing the backbuffer black.
            if (ActiveScene is GameScene)
                return true;

            // Required when resolution scaling is active or gamma-correction pass is needed.
            if (MathF.Abs(Constants.RENDER_SCALE - 1.0f) > 0.0001f || Constants.MSAA_ENABLED)
                return true;

            var graphics = GraphicsManager.Instance;
            if (graphics == null)
                return false;

            bool alphaRgb = graphics.IsAlphaRGBEnabled && graphics.AlphaRGBEffect != null;
            bool fxaa = graphics.IsFXAAEnabled && graphics.FXAAEffect != null;
            return alphaRgb || fxaa;
        }

        private void ApplyPostProcessingEffects()
        {
            RenderTarget2D sourceTarget = GraphicsManager.Instance.MainRenderTarget;

            if (GraphicsManager.Instance.IsAlphaRGBEnabled && GraphicsManager.Instance.AlphaRGBEffect != null)
            {
                RenderTarget2D destination = GraphicsManager.Instance.TempTarget1;
                ApplyEffect(GraphicsManager.Instance.AlphaRGBEffect, sourceTarget, destination);
                sourceTarget = destination;
            }

            if (GraphicsManager.Instance.IsFXAAEnabled && GraphicsManager.Instance.FXAAEffect != null)
            {
                RenderTarget2D destination = ReferenceEquals(sourceTarget, GraphicsManager.Instance.MainRenderTarget)
                    ? GraphicsManager.Instance.TempTarget1
                    : GraphicsManager.Instance.TempTarget2;
                ApplyEffect(GraphicsManager.Instance.FXAAEffect, sourceTarget, destination);
                sourceTarget = destination;
            }

            DrawFinalImageToScreen(sourceTarget);
        }

        private void ApplyEffect(Effect effect, RenderTarget2D source, RenderTarget2D destination)
        {
            GraphicsDevice.SetRenderTarget(destination);

            // Every post-processing pass covers the full destination. Clearing and
            // alpha-blending add bandwidth without changing the final image.
            EnsureEffectCache(destination.Width, destination.Height);

            if (effect == GraphicsManager.Instance.FXAAEffect)
            {
                effect.Parameters["Resolution"]?.SetValue(_cachedEffectResolution);
            }

            if (effect == GraphicsManager.Instance.FXAAEffect || effect == GraphicsManager.Instance.AlphaRGBEffect)
            {
                effect.Parameters["WorldViewProjection"]?.SetValue(_cachedEffectOrtho);
            }

            GraphicsManager.Instance.Sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, GraphicsManager.GetQualityLinearSamplerState(), DepthStencilState.None, RasterizerState.CullNone, effect);
            GraphicsManager.Instance.Sprite.Draw(source, GraphicsDevice.Viewport.Bounds, Color.White);
            GraphicsManager.Instance.Sprite.End();
        }

        private void DrawFinalImageToScreen(RenderTarget2D sourceTarget)
        {
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(FallbackClearColor);

            Effect gammaEffect = Constants.MSAA_ENABLED ? GraphicsManager.Instance.GammaCorrectionEffect : null;

            GraphicsManager.Instance.Sprite.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                GraphicsManager.GetQualityLinearSamplerState(),
                DepthStencilState.None,
                RasterizerState.CullNone,
                gammaEffect);

            GraphicsManager.Instance.Sprite.Draw(sourceTarget, GraphicsDevice.Viewport.Bounds, Color.White);
            GraphicsManager.Instance.Sprite.End();
        }

        public void ApplyGraphicsConfiguration(GraphicsSettings graphics)
        {
            if (graphics == null)
            {
                return;
            }

            if (OperatingSystem.IsAndroid())
            {
                // On Android, use actual screen size from GraphicsAdapter
                var screenSize = GetActualScreenSize();
                _graphics.PreferredBackBufferWidth = screenSize.X;
                _graphics.PreferredBackBufferHeight = screenSize.Y;
                _graphics.ApplyChanges();

                UiScaler.Configure(
                    screenSize.X,
                    screenSize.Y,
                    Math.Max(1, graphics.UiVirtualWidth),
                    Math.Max(1, graphics.UiVirtualHeight),
                    ScaleMode.Stretch);

                Camera.Instance.AspectRatio = (float)screenSize.X / screenSize.Y;

                _logger?.LogDebug("Android graphics configured: {Width}x{Height}, UiScaler: {ScaleX:F4}x{ScaleY:F4}",
                    screenSize.X, screenSize.Y,
                    UiScaler.ScaleX, UiScaler.ScaleY);
            }
            else if (OperatingSystem.IsIOS())
            {
                // On iOS, use actual screen size, not config values
                var screenSize = GetActualScreenSize();
                _graphics.PreferredBackBufferWidth = screenSize.X;
                _graphics.PreferredBackBufferHeight = screenSize.Y;
                _graphics.ApplyChanges();

                UiScaler.Configure(
                    screenSize.X,
                    screenSize.Y,
                    Math.Max(1, graphics.UiVirtualWidth),
                    Math.Max(1, graphics.UiVirtualHeight),
                    ScaleMode.Stretch);

                Camera.Instance.AspectRatio = (float)screenSize.X / screenSize.Y;

                _logger?.LogDebug("iOS graphics configured: {Width}x{Height}, UiScaler: {ScaleX:F4}x{ScaleY:F4}",
                    screenSize.X, screenSize.Y,
                    UiScaler.ScaleX, UiScaler.ScaleY);
            }
            else
            {
                _graphics.PreferredBackBufferWidth = Math.Max(1, graphics.Width);
                _graphics.PreferredBackBufferHeight = Math.Max(1, graphics.Height);
                _graphics.IsFullScreen = graphics.IsFullScreen;

                // In fullscreen mode, we need HardwareModeSwitch = true to actually change resolution.
                // In windowed/borderless mode, keep it false to avoid exclusive fullscreen.
                _graphics.HardwareModeSwitch = graphics.IsFullScreen;

                _graphics.ApplyChanges();

                // Get actual window/backbuffer size after ApplyChanges (may differ from preferred)
                int actualWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
                int actualHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;

                // Update viewport to match actual back buffer size (required for correct mouse ray casting)
                GraphicsDevice.Viewport = new Viewport(0, 0, actualWidth, actualHeight);

                UiScaler.Configure(
                    actualWidth,
                    actualHeight,
                    Math.Max(1, graphics.UiVirtualWidth),
                    Math.Max(1, graphics.UiVirtualHeight),
                    ScaleMode.Uniform);

                // Update camera aspect ratio after resolution change
                Camera.Instance.AspectRatio = (float)actualWidth / actualHeight;
            }

            _scaleFactor = UiScaler.Scale;
        }

        private void EnsureEffectCache(int width, int height)
        {
            var size = new Point(width, height);
            if (_effectCacheValid && size == _lastEffectTargetSize)
            {
                return;
            }

            _lastEffectTargetSize = size;
            _cachedEffectResolution = new Vector2(width, height);
            _cachedEffectOrtho = Matrix.CreateOrthographicOffCenter(
                0,
                width,
                height,
                0,
                0,
                1);
            _effectCacheValid = true;
        }

        /// <summary>
        /// Synchronously disposes <see cref="Network"/> exactly once,
        /// swallowing <see cref="ObjectDisposedException"/> which occurs,
        /// if the CancellationTokenSource inside NetworkManager was already disposed.
        /// </summary>
        private void DisposeTelemetrySafely()
        {
            var publisher = Interlocked.Exchange(ref _telemetryPublisher, null);
            if (publisher == null)
                return;

            try
            {
                publisher.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception telemetryDisposeException)
            {
                _logger?.LogDebug(telemetryDisposeException, "Failed disposing diagnostics telemetry publisher.");
            }
        }

        private void DisposeNetworkSafely()
        {
            if (_networkDisposed || Network == null)
                return;

            _networkDisposed = true;

            try
            {
                // Fire-and-forget async dispose to avoid deadlock on shutdown.
                // Blocking here (e.g., .GetAwaiter().GetResult()) can cause a hang if async code needs the main thread.
                _ = Network.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Ignore – NetworkManager already cleaned up earlier
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error while disposing NetworkManager.");
            }
        }

        public static void PersistConnectSettings(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                return;
            }

            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                muSettings["ConnectServerHost"] = host;
                muSettings["ConnectServerPort"] = port;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved ConnectServer settings to {Path}", LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist connect server settings to disk.");
            }
        }

        public static void PersistUiTheme(UiThemeId theme)
        {
            if (!Enum.IsDefined(theme))
                theme = UiThemeId.Modern;

            if (AppSettings?.Ui != null)
                AppSettings.Ui.Theme = theme.ToString();

            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Ui"] is not JsonObject uiSettings)
                {
                    uiSettings = new JsonObject();
                    muSettings["Ui"] = uiSettings;
                }

                uiSettings["Theme"] = theme.ToString();
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved UI theme {Theme} to {Path}", theme, LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist UI theme {Theme}.", theme);
            }
        }

        public static bool TryLoadQuickSlotAssignments(
            string characterName,
            out int activeSkillSlot,
            out ushort?[] skillSlots,
            out (byte Group, int Id)?[] potionSlots)
        {
            activeSkillSlot = 3;
            skillSlots = new ushort?[13];
            // Keep reading legacy three-slot files while allowing Season6's five item slots.
            potionSlots = new (byte Group, int Id)?[5];

            string key = NormalizeQuickSlotCharacterKey(characterName);
            if (key == null)
            {
                return false;
            }

            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings ||
                    muSettings["Ui"] is not JsonObject uiSettings ||
                    uiSettings["QuickSlots"] is not JsonObject quickSlots ||
                    quickSlots[key] is not JsonObject characterSlots)
                {
                    return false;
                }

                if (characterSlots["ActiveSkillSlot"] is JsonValue activeValue &&
                    activeValue.TryGetValue(out int loadedActive))
                {
                    activeSkillSlot = loadedActive;
                }

                if (characterSlots["Skills"] is JsonArray skillsArray)
                {
                    for (int i = 0; i < Math.Min(skillSlots.Length, skillsArray.Count); i++)
                    {
                        if (skillsArray[i] is JsonValue skillValue &&
                            skillValue.TryGetValue(out ushort skillId))
                        {
                            skillSlots[i] = skillId;
                        }
                    }
                }

                if (characterSlots["Potions"] is JsonArray potionsArray)
                {
                    for (int i = 0; i < Math.Min(potionSlots.Length, potionsArray.Count); i++)
                    {
                        if (potionsArray[i] is not JsonObject potionObject)
                            continue;

                        if (potionObject["Group"] is JsonValue groupValue &&
                            potionObject["Id"] is JsonValue idValue &&
                            groupValue.TryGetValue(out byte group) &&
                            idValue.TryGetValue(out int id))
                        {
                            potionSlots[i] = (group, id);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to load quick slot assignments for character {CharacterName}.", characterName);
                return false;
            }
        }

        public static void PersistQuickSlotAssignments(
            string characterName,
            int activeSkillSlot,
            IReadOnlyList<ushort?> skillSlots,
            IReadOnlyList<(byte Group, int Id)?> potionSlots)
        {
            string key = NormalizeQuickSlotCharacterKey(characterName);
            if (key == null)
            {
                return;
            }

            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Ui"] is not JsonObject uiSettings)
                {
                    uiSettings = new JsonObject();
                    muSettings["Ui"] = uiSettings;
                }

                if (uiSettings["QuickSlots"] is not JsonObject quickSlots)
                {
                    quickSlots = new JsonObject();
                    uiSettings["QuickSlots"] = quickSlots;
                }

                var skillsArray = new JsonArray();
                for (int i = 0; i < skillSlots.Count; i++)
                {
                    skillsArray.Add(skillSlots[i].HasValue ? JsonValue.Create(skillSlots[i]!.Value) : null);
                }

                var potionsArray = new JsonArray();
                for (int i = 0; i < potionSlots.Count; i++)
                {
                    var potion = potionSlots[i];
                    if (!potion.HasValue)
                    {
                        potionsArray.Add(null);
                        continue;
                    }

                    potionsArray.Add(new JsonObject
                    {
                        ["Group"] = potion.Value.Group,
                        ["Id"] = potion.Value.Id
                    });
                }

                quickSlots[key] = new JsonObject
                {
                    ["CharacterName"] = characterName.Trim(),
                    ["ActiveSkillSlot"] = activeSkillSlot,
                    ["Skills"] = skillsArray,
                    ["Potions"] = potionsArray
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved quick slot assignments for {CharacterName} to {Path}", characterName, LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist quick slot assignments for character {CharacterName}.", characterName);
            }
        }

        public static void PersistGraphicsPreset(GraphicsQualityPreset preset)
        {
            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Graphics"] is not JsonObject graphics)
                {
                    graphics = new JsonObject();
                    muSettings["Graphics"] = graphics;
                }

                graphics["QualityPreset"] = preset.ToString();

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved graphics preset to {Path}", LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist graphics preset to disk.");
            }
        }

        public static void PersistDisplaySettings(int width, int height, bool isFullScreen)
        {
            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Graphics"] is not JsonObject graphics)
                {
                    graphics = new JsonObject();
                    muSettings["Graphics"] = graphics;
                }

                graphics["Width"] = width;
                graphics["Height"] = height;
                graphics["IsFullScreen"] = isFullScreen;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved display settings to {Path}", LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist display settings to disk.");
            }
        }

        public static void PersistGraphicsPerformanceCaps(int dynamicLightFps, int animationFps)
        {
            int clampedDynamicLightFps = Constants.ClampPerformanceFps(dynamicLightFps);
            int clampedAnimationFps = Constants.ClampPerformanceFps(animationFps);

            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Graphics"] is not JsonObject graphics)
                {
                    graphics = new JsonObject();
                    muSettings["Graphics"] = graphics;
                }

                graphics["DynamicLightUpdateFps"] = clampedDynamicLightFps;
                graphics["AnimationUpdateFps"] = clampedAnimationFps;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved graphics performance caps to {Path}", LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist graphics performance caps to disk.");
            }
        }

        public static void PersistMonsterShadowMode(bool forceMonsterMeshShadows)
        {
            var logger = AppLoggerFactory?.CreateLogger<MuGame>();
            try
            {
                Directory.CreateDirectory(ConfigDirectory ?? AppContext.BaseDirectory);

                JsonObject root = LoadLocalSettings(logger);
                if (root["MuOnlineSettings"] is not JsonObject muSettings)
                {
                    muSettings = new JsonObject();
                    root["MuOnlineSettings"] = muSettings;
                }

                if (muSettings["Graphics"] is not JsonObject graphics)
                {
                    graphics = new JsonObject();
                    muSettings["Graphics"] = graphics;
                }

                graphics["ForceMonsterMeshShadows"] = forceMonsterMeshShadows;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(LocalSettingsPath, root.ToJsonString(options));
                logger?.LogInformation("Saved monster shadow mode to {Path}", LocalSettingsPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to persist monster shadow mode to disk.");
            }
        }

        private static JsonObject LoadLocalSettings(ILogger logger)
        {
            if (!File.Exists(LocalSettingsPath))
            {
                return new JsonObject();
            }

            try
            {
                var text = File.ReadAllText(LocalSettingsPath);
                return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to read existing local settings; recreating file.");
                return new JsonObject();
            }
        }

        private static string NormalizeQuickSlotCharacterKey(string characterName)
        {
            string trimmed = characterName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "???")
            {
                return null;
            }

            return trimmed.ToUpperInvariant();
        }

        /// <summary>
        /// Handles the Game.Exiting event to ensure proper cleanup and process termination.
        /// </summary>
        private void OnGameExiting(object sender, EventArgs e)
        {
            // Dispose the game instance and all resources
            DisposeInstance();
            // Force process exit to avoid lingering background process
            Environment.Exit(0);
        }
    }
}
