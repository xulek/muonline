using Client.Data.ATT;
using Client.Data.CAP;
using Client.Data.OBJS;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Particles;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Controls
{
    // Comparers for deterministic depth ordering. Opaque objects are rendered front-to-back,
    // while transparent objects are rendered back-to-front.
    sealed class WorldObjectDepthAsc : IComparer<WorldObject>
    {
        public static readonly WorldObjectDepthAsc Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            int cmp = a.RenderSortDepth.CompareTo(b.RenderSortDepth);
            if (cmp != 0) return cmp;
            return a.NetworkId.CompareTo(b.NetworkId);
        }
    }

    sealed class WorldObjectDepthDesc : IComparer<WorldObject>
    {
        public static readonly WorldObjectDepthDesc Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return 1;
            if (b is null) return -1;

            int cmp = b.RenderSortDepth.CompareTo(a.RenderSortDepth);
            if (cmp != 0) return cmp;
            return b.NetworkId.CompareTo(a.NetworkId);
        }
    }

    sealed class SourceParticleBatchComparer : IComparer<SourceParticleSystem>
    {
        public static readonly SourceParticleBatchComparer Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(SourceParticleSystem a, SourceParticleSystem b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            int comparison = a.ParticleBatchBlendKey.CompareTo(b.ParticleBatchBlendKey);
            if (comparison != 0) return comparison;

            comparison = a.ParticleBatchTextureKey.CompareTo(b.ParticleBatchTextureKey);
            if (comparison != 0) return comparison;

            comparison = a.NetworkId.CompareTo(b.NetworkId);
            return comparison != 0
                ? comparison
                : RuntimeHelpers.GetHashCode(a).CompareTo(RuntimeHelpers.GetHashCode(b));
        }
    }

    sealed class WorldObjectOpaqueBatchComparer : IComparer<WorldObject>
    {
        public static readonly WorldObjectOpaqueBatchComparer Instance = new();
        private const float DepthBucketSize = 512f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DepthBucket(float depth)
        {
            if (!float.IsFinite(depth))
                return int.MaxValue;
            return (int)MathF.Floor(depth / DepthBucketSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Prepare(WorldObject obj, float depth)
        {
            obj.RenderSortDepth = depth;
            obj.RenderSortDepthBucket = DepthBucket(depth);
            obj.RenderSortReferenceKey = RuntimeHelpers.GetHashCode(obj);
            obj.RenderSortBlendKey = obj.BlendState == null ? 0 : RuntimeHelpers.GetHashCode(obj.BlendState);

            if (obj is ModelObject model)
            {
                obj.RenderSortIsModel = true;
                obj.RenderSortModelKey = model.Model == null ? 0 : RuntimeHelpers.GetHashCode(model.Model);
                var texture = model.GetSortTextureHint();
                obj.RenderSortTextureKey = texture == null ? 0 : RuntimeHelpers.GetHashCode(texture);
            }
            else
            {
                obj.RenderSortIsModel = false;
                obj.RenderSortModelKey = 0;
                obj.RenderSortTextureKey = 0;
            }
        }

        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            // Keep approximate front-to-back ordering for early-Z, then group nearby
            // objects by model/material to reduce state and geometry switches.
            int comparison = a.RenderSortDepthBucket.CompareTo(b.RenderSortDepthBucket);
            if (comparison != 0) return comparison;

            comparison = b.RenderSortIsModel.CompareTo(a.RenderSortIsModel);
            if (comparison != 0) return comparison;

            if (a.RenderSortIsModel && b.RenderSortIsModel)
            {
                comparison = a.RenderSortModelKey.CompareTo(b.RenderSortModelKey);
                if (comparison != 0) return comparison;

                comparison = a.RenderSortTextureKey.CompareTo(b.RenderSortTextureKey);
                if (comparison != 0) return comparison;
            }

            comparison = a.RenderSortBlendKey.CompareTo(b.RenderSortBlendKey);
            if (comparison != 0) return comparison;

            comparison = a.RenderSortDepth.CompareTo(b.RenderSortDepth);
            if (comparison != 0) return comparison;

            comparison = a.NetworkId.CompareTo(b.NetworkId);
            return comparison != 0
                ? comparison
                : a.RenderSortReferenceKey.CompareTo(b.RenderSortReferenceKey);
        }
    }

    /// <summary>
    /// Base class for rendering and managing world objects in a game scene.
    /// </summary>
    public abstract class WorldControl : GameControl
    {
        // --- Fields & Constants ---
        private int _renderCounter;
        private int _lastRenderMetricsLogFrame = -10000;
        private DepthStencilState _currentDepthState = DepthStencilState.Default;
        private static readonly DepthStencilState DepthStateDefault = DepthStencilState.Default;
        private static readonly DepthStencilState DepthStateDepthRead = DepthStencilState.DepthRead;


        private readonly List<WorldObject> _solidBehind = [];
        private readonly List<WorldObject> _transparentObjects = [];
        private readonly List<SourceParticleSystem> _dedicatedParticleSystems = [];
        private readonly List<WorldObject> _lateEffectObjects = [];
        private readonly HashSet<WorldObject> _queuedLateEffectObjects = [];
        private readonly List<DamageTextObject> _queuedDamageTexts = [];
        private bool _collectLateEffectDraws;
        private readonly List<ModelObject> _dedicatedStaticMapObjects = [];
        private readonly List<ModelObject> _queuedDedicatedStaticMapObjects = [];
        private readonly List<ModelObject> _queuedSolidStaticMapObjects = [];
        private readonly ModelObject.CutoutMapBatch _cutoutMapBatch = new();
        private readonly List<ModelObject> _queuedCrowdSidePasses = [];
        private readonly List<WalkerObject> _walkers = [];
        private readonly List<PlayerObject> _players = [];
        private readonly List<MonsterObject> _monsters = [];
        private readonly List<DroppedItemObject> _droppedItems = [];
        private readonly DroppedItemRenderSelector _droppedItemSelector = new();
        private readonly Queue<WorldObject> _objectsToInitialize = [];
        private readonly HashSet<WorldObject> _queuedForInitialization = [];
        private readonly ConcurrentDictionary<WorldObject, bool> _initializationHiddenStates = new();
        private readonly List<WorldObject> _visibleObjects = [];
        private readonly Dictionary<WorldObject, RenderFaultState> _renderFaults = [];
        private long _renderFailureSequence;
        private CameraState? _deferredCameraState;

        // Snapshot of objects that survived this frame's visibility/culling pass.
        // Overlay/UI passes (nameplates, bbox, hover) should iterate this rather than the
        // full World.Objects snapshot to avoid touching everything on the map.
        public IReadOnlyList<WorldObject> VisibleObjects => _visibleObjects;
        private readonly Dictionary<WorldObject, int> _visibleObjectIndices = [];
        private bool _isUpdatingVisibleObjects;
        private bool _visibleObjectsNeedCompaction;
        private readonly HashSet<WorldObject> _positionDirtyObjects = [];
        private readonly HashSet<WorldObject> _spatialDirtyObjects = [];
        private WorldObject[] _dirtyVisibilityScratch = Array.Empty<WorldObject>();
        private byte[] _visibilityResultScratch = Array.Empty<byte>();
        private byte[] _dirtyVisibilityResultScratch = Array.Empty<byte>();
        private bool _dirtyVisibleObjects = true;
        private bool _hasVisibilitySnapshot;
        private ulong _lastCulledCameraVersion;
        private Vector3 _lastCulledCameraPosition;
        private Vector3 _lastCulledCameraDirection = Vector3.UnitY;
        private float _lastCulledViewFar = float.NaN;
        private float _lastCulledFov = float.NaN;
        private float _lastCulledAspectRatio = float.NaN;
        private readonly Stopwatch _cullingStopwatch = new();
        private const int MaxObjectInitializationsPerFrame = 8;
        private const int MaxConcurrentObjectInitializations = 4;
        private int _activeObjectInitializations;
        private const float CameraCullMoveThreshold = 32f;
        private const float CameraCullDirectionDotThreshold = 0.9995f;
        private const float WorldCullGuardBand = 64f;
        private const float NearUpdateDistanceSq = 2200f * 2200f;
        private const float MidUpdateDistanceSq = 4200f * 4200f;
        private const float FarUpdateDistanceSq = 6200f * 6200f;
        private const int ParallelVisibleRebuildThreshold = 1536;
        private const int ParallelDirtyRefreshThreshold = 1024;
        private const int SpatialSectorTileSize = 16;
        private const int SpatialSectorsPerAxis = Constants.TERRAIN_SIZE / SpatialSectorTileSize;
        private const int SpatialInvalidSector = -1;
        private const int SpatialRebuildPaddingSectors = 1;
        private static readonly ParallelOptions VisibleParallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2)
        };
        private readonly List<WorldObject>[,] _spatialSectors = new List<WorldObject>[SpatialSectorsPerAxis, SpatialSectorsPerAxis];
        private readonly List<WorldObject> _spatialOffGridObjects = [];
        private readonly Dictionary<WorldObject, int> _spatialObjectSectors = [];
        private readonly List<WorldObject> _spatialCandidates = [];

        public Dictionary<ushort, WalkerObject> WalkerObjectsById { get; } = [];

        private ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<WorldControl>();

        private sealed class RenderFaultState
        {
            public int FailureCount;
            public int RetryFrame;
            public string Phase;
        }

        // --- Properties ---

        public string BackgroundMusicPath { get; set; }
        public string AmbientSoundPath { get; set; }

        public TerrainControl Terrain { get; }
        public WorldFrameMetrics FrameMetrics { get; } = new();

        private static long s_nextWorldInstanceId;

        public long WorldInstanceId { get; }
        public short WorldIndex { get; private set; }
        public bool IsSunWorld { get; protected set; } = true;

        public bool EnableShadows { get; protected set; } = true;

        public ChildrenCollection<WorldObject> Objects { get; private set; }
        = new ChildrenCollection<WorldObject>(null);
        public IReadOnlyList<WalkerObject> Walkers => _walkers;
        public IReadOnlyList<PlayerObject> Players => _players;
        public IReadOnlyList<MonsterObject> Monsters => _monsters;
        public IReadOnlyList<DroppedItemObject> DroppedItems => _droppedItems;
        public ulong LastCullCameraVersion => _lastCulledCameraVersion;
        public int LastCullCandidateCount { get; private set; }
        public int LastCullVisibleCount { get; private set; }
        public float LastCullRebuildMs { get; private set; }
        public bool LastCullWasRebuild { get; private set; }

        /// <summary>
        /// Menu worlds can be initialized while the previous scene is still visible. Such
        /// worlds must not mutate the process-wide camera until their scene becomes active.
        /// </summary>
        protected virtual bool DeferCameraActivation => false;

        public Type[] MapTileObjects { get; } = new Type[Constants.TERRAIN_SIZE];

        public ushort MapId { get; protected set; }

        public new string Name { get; protected set; }

        // --- Constructor ---

        public WorldControl(short worldIndex)
        {
            WorldInstanceId = Interlocked.Increment(ref s_nextWorldInstanceId);
            AutoViewSize = false;
            ViewSize = new(MuGame.Instance.Width, MuGame.Instance.Height);
            WorldIndex = worldIndex;
            if (Constants.SUN_WORLD_INDICES != null && Constants.SUN_WORLD_INDICES.Length > 0)
            {
                IsSunWorld = Array.IndexOf(Constants.SUN_WORLD_INDICES, worldIndex) >= 0;
            }

            var worldInfo = (WorldInfoAttribute)Attribute.GetCustomAttribute(GetType(), typeof(WorldInfoAttribute));
            if (worldInfo != null)
            {
                MapId = worldInfo.MapId;
                Name = worldInfo.DisplayName;
            }

            Controls.Add(Terrain = new TerrainControl { WorldIndex = worldIndex });
            Objects.ControlAdded += OnObjectAdded;
            Objects.ControlRemoved += OnObjectRemoved;

            for (int y = 0; y < SpatialSectorsPerAxis; y++)
            {
                for (int x = 0; x < SpatialSectorsPerAxis; x++)
                {
                    _spatialSectors[x, y] = new List<WorldObject>(16);
                }
            }
        }

        private void Object_PositionChanged(object sender, EventArgs e)
        {
            if (sender is WorldObject obj)
            {
                // Position can change several times while a modular actor updates its root,
                // equipment and helper transforms. Defer spatial-bucket maintenance so each
                // root is re-registered at most once before the next culling/query phase.
                _positionDirtyObjects.Add(obj);
                _spatialDirtyObjects.Add(obj);
            }
            else
            {
                _dirtyVisibleObjects = true;
            }

            MarkWorldGeometryChanged();
        }

        private int _worldGeometryVersion;
        private int _worldGeometryDirty;

        // Geometry changes are coalesced until the shadow pass consumes them. This avoids a
        // process-wide atomic increment for every transform notification and prevents an
        // off-screen world from invalidating the active world's shadow selection.
        public int WorldGeometryVersion => Volatile.Read(ref _worldGeometryVersion);

        private void MarkWorldGeometryChanged()
        {
            if (Volatile.Read(ref _worldGeometryDirty) == 0)
                Interlocked.Exchange(ref _worldGeometryDirty, 1);
        }

        internal int CommitWorldGeometryVersionForRender()
        {
            if (Interlocked.Exchange(ref _worldGeometryDirty, 0) != 0)
                Interlocked.Increment(ref _worldGeometryVersion);

            return Volatile.Read(ref _worldGeometryVersion);
        }

        private void Object_StatusChanged(object sender, EventArgs e)
        {
            if (sender is not WorldObject obj)
                return;

            if (obj.Status == GameControlStatus.Ready)
            {
                _positionDirtyObjects.Add(obj);
                _spatialDirtyObjects.Add(obj);
                MarkWorldGeometryChanged();
                return;
            }

            if (obj.Status == GameControlStatus.Disposed || obj.Status == GameControlStatus.Error)
            {
                RemoveVisibleObject(obj);
                _positionDirtyObjects.Remove(obj);
                _spatialDirtyObjects.Remove(obj);
                UnregisterSpatialObject(obj);
                MarkWorldGeometryChanged();
            }
        }

        // --- Lifecycle Methods ---

        public override async Task Load()
        {
            await base.Load();

            CreateMapTileObjects();

            float viewportAspectRatio = GraphicsDevice.Viewport.AspectRatio;
            if (!DeferCameraActivation)
                Camera.Instance.AspectRatio = viewportAspectRatio;

            var worldFolder = $"World{WorldIndex}";
            var dataPath = Constants.DataPath;
            var tasks = new List<Task>();

            // Build the complete camera state locally. Deferred menu worlds must retain their
            // original hard-coded camera adjustments even when the CAP file is missing, without
            // mutating the camera of the scene which is still being presented.
            CameraState cameraState = Camera.Instance.CaptureState().With(
                aspectRatio: viewportAspectRatio);
            bool cameraFileLoaded = false;

            var capPath = Path.Combine(dataPath, worldFolder, "Camera_Angle_Position.bmd");
            if (File.Exists(capPath))
            {
                var capReader = new CAPReader();
                var data = await capReader.Load(capPath);
                cameraState = cameraState.With(
                    fieldOfView: data.CameraFOV * Constants.FOV_SCALE,
                    position: data.CameraPosition,
                    target: data.HeroPosition);
                cameraFileLoaded = true;
            }

            ConfigureCameraState(ref cameraState);

            if (DeferCameraActivation)
                _deferredCameraState = cameraState;
            else if (cameraFileLoaded)
                Camera.Instance.ApplyState(cameraState);

            // Load terrain OBJ
            var objPath = Path.Combine(dataPath, worldFolder, $"EncTerrain{WorldIndex}.obj");
            if (File.Exists(objPath))
            {
                var reader = new OBJReader();
                OBJ obj = await reader.Load(objPath);
                foreach (var mapObj in obj.Objects)
                {
                    var instance = WorldObjectFactory.CreateMapTileObject(this, mapObj);
                    if (instance != null) tasks.Add(instance.Load());
                }
            }

            // tasks.Add(Container.Load());
            await Task.WhenAll(tasks);

            // Play or stop background music
            if (!string.IsNullOrEmpty(BackgroundMusicPath))
                SoundController.Instance.PlayBackgroundMusic(BackgroundMusicPath);
            else
                SoundController.Instance.StopBackgroundMusic();

            // Play or stop ambient sound
            if (!string.IsNullOrEmpty(AmbientSoundPath))
                SoundController.Instance.PlayAmbientSound(AmbientSoundPath);
            else
                SoundController.Instance.StopAmbientSound();
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
        }

        /// <summary>
        /// Allows a world to adjust the camera loaded from Camera_Angle_Position.bmd without
        /// touching the global camera during off-screen scene initialization.
        /// </summary>
        protected virtual void ConfigureCameraState(ref CameraState cameraState)
        {
        }

        /// <summary>
        /// Applies a camera which was intentionally deferred until the owning scene is active.
        /// Returns true when a deferred state was applied.
        /// </summary>
        public bool ActivateDeferredCamera()
        {
            if (!_deferredCameraState.HasValue)
                return false;

            Camera.Instance.ApplyState(_deferredCameraState.Value);
            _dirtyVisibleObjects = true;
            PrepareInitialVisibilitySnapshot();
            return true;
        }

        public override void Update(GameTime time)
        {
            long worldBaseStarted = UpdatePassProfiler.Start();
            base.Update(time);
            UpdatePassProfiler.AddWorldBase(worldBaseStarted);

            if (Status != GameControlStatus.Ready) return;
            FrameMetrics.Reset();
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            LastCullWasRebuild = false;

            long initializationStarted = UpdatePassProfiler.Start();
            if (_objectsToInitialize.Count > 0)
            {
                int availableSlots = Math.Max(0, MaxConcurrentObjectInitializations - Volatile.Read(ref _activeObjectInitializations));
                int initCount = Math.Min(
                    Math.Min(MaxObjectInitializationsPerFrame, _objectsToInitialize.Count),
                    availableSlots);
                for (int i = 0; i < initCount; i++)
                {
                    var obj = _objectsToInitialize.Dequeue();
                    _queuedForInitialization.Remove(obj);

                    if (!IsRegisteredRootObject(obj) || obj.Status != GameControlStatus.NonInitialized)
                        continue;

                    if (!QueueObjectInitialization(obj))
                        EnqueueObjectInitialization(obj);
                }
            }
            UpdatePassProfiler.AddWorldInitialization(initializationStarted);

            long visibilityStarted = UpdatePassProfiler.Start();
            FlushSpatialUpdates();
            // Keep update list current for object movement, but defer full camera recull to end of update
            // so rendering uses the latest camera state from this frame.
            if (_positionDirtyObjects.Count > 0 && !_dirtyVisibleObjects)
            {
                RefreshDirtyVisibleObjects();
            }

            UpdateVisibleObjects(time);
            FlushSpatialUpdates();
            UpdatePassProfiler.AddWorldVisibility(visibilityStarted);

            long cullStarted = UpdatePassProfiler.Start();
            var camera = Camera.Instance;
            ulong cameraVersion = camera.CullingStateVersion;
            bool needsFullRebuild =
                _dirtyVisibleObjects ||
                (Constants.ENABLE_CROWD_SPATIAL_CULLING && _spatialObjectSectors.Count != Objects.Count) ||
                HasSignificantCameraCullChange(camera) ||
                !_hasVisibilitySnapshot;

            if (needsFullRebuild)
            {
                RebuildVisibleObjects();
                _dirtyVisibleObjects = false;
                CaptureCulledCameraState(camera, cameraVersion);
            }
            else if (_positionDirtyObjects.Count > 0)
            {
                RefreshDirtyVisibleObjects();
            }
            UpdatePassProfiler.AddWorldCull(cullStarted);

            long hoverStarted = UpdatePassProfiler.Start();
            WorldHoverSystem.UpdateHover(_visibleObjects, Scene);
            UpdatePassProfiler.AddWorldHover(hoverStarted);
        }

        internal void PrepareInitialVisibilitySnapshot()
        {
            if (Status != GameControlStatus.Ready)
                return;

            var camera = Camera.Instance;
            RebuildVisibleObjects();
            _dirtyVisibleObjects = false;
            CaptureCulledCameraState(camera, camera.CullingStateVersion);
        }

        /// <summary>
        /// Publishes an already initialized object to rendering and immediately refreshes
        /// its spatial/culling state. Scene workflows can therefore keep a model hidden
        /// until all CPU/GPU resources are ready without risking a stale culling snapshot.
        /// </summary>
        internal bool ActivateObjectForRendering(WorldObject obj, bool forceFullVisibilityRebuild = false)
        {
            if (obj == null ||
                obj.Status != GameControlStatus.Ready ||
                !ReferenceEquals(obj.World, this) ||
                !Objects.Contains(obj))
            {
                return false;
            }

            _initializationHiddenStates.TryRemove(obj, out _);
            _renderFaults.Remove(obj);
            obj.Hidden = false;
            RegisterSpatialObject(obj);
            UpdateSpatialRegistration(obj);
            _positionDirtyObjects.Add(obj);

            if (forceFullVisibilityRebuild || !_hasVisibilitySnapshot)
                _dirtyVisibleObjects = true;

            MarkWorldGeometryChanged();
            return true;
        }

        internal bool IsObjectVisibleInSnapshot(WorldObject obj) =>
            obj != null && _visibleObjectIndices.ContainsKey(obj);

        internal void ClearObjectRenderFault(WorldObject obj)
        {
            if (obj != null)
                _renderFaults.Remove(obj);
        }

        internal bool IsObjectInitializationPending(WorldObject obj) =>
            obj != null &&
            (_queuedForInitialization.Contains(obj) ||
             _initializationHiddenStates.ContainsKey(obj));

        internal async Task PrepareInitialRenderResourcesAsync(string phaseName)
        {
            PrepareInitialVisibilitySnapshot();
            if (Terrain != null)
            {
                await Terrain.PrepareInitialRenderResourcesAsync($"{phaseName}.Terrain");
                await MuGame.YieldToNextFrameAsync(
                    $"{phaseName}.Models",
                    MainThreadDispatcher.WorkPriority.High);
            }

            long sliceStarted = Stopwatch.GetTimestamp();
            int preparedInSlice = 0;
            int preparedTotal = 0;
            for (int i = 0; i < _visibleObjects.Count; i++)
            {
                if (_visibleObjects[i] is not ModelObject model || !model.Visible)
                    continue;

                model.PrepareRenderResourcesForFirstFrame();
                preparedInSlice++;
                preparedTotal++;

                if (preparedInSlice < 4 &&
                    Stopwatch.GetElapsedTime(sliceStarted).TotalMilliseconds < 2d)
                {
                    continue;
                }

                preparedInSlice = 0;
                sliceStarted = Stopwatch.GetTimestamp();
                await MuGame.YieldToNextFrameAsync(
                    $"{phaseName}.{preparedTotal}",
                    MainThreadDispatcher.WorkPriority.High);
            }
        }

        private bool QueueObjectInitialization(WorldObject obj)
        {
            if (!IsRegisteredRootObject(obj) || obj.Status != GameControlStatus.NonInitialized)
                return true;

            var scheduler = MuGame.TaskScheduler;
            if (scheduler == null || Volatile.Read(ref _activeObjectInitializations) >= MaxConcurrentObjectInitializations)
                return false;

            // Objects inserted into a live world stay hidden until CPU decode, GPU texture
            // upload and first-frame buffers are all ready. This prevents a cold-loaded
            // monster or effect from interrupting the first Draw in which it appears.
            _initializationHiddenStates.TryAdd(obj, obj.Hidden);
            obj.Hidden = true;

            Interlocked.Increment(ref _activeObjectInitializations);
            bool queued = scheduler.QueueTask(
                () => LoadInitializedObjectAsync(obj),
                Controllers.TaskScheduler.Priority.Low,
                $"WorldObject.Initialize.{obj.GetType().Name}.{obj.NetworkId:X4}");

            if (!queued)
            {
                Interlocked.Decrement(ref _activeObjectInitializations);
                if (_initializationHiddenStates.TryRemove(obj, out bool originalHidden))
                    obj.Hidden = originalHidden;
            }

            return queued;
        }

        private void EnqueueObjectInitialization(WorldObject obj)
        {
            // Status changes are also raised by child models and by roots which are assigned
            // a World before they are published in World.Objects. Only registered root objects
            // may use the world's asynchronous initialization pipeline; otherwise PlayerObject
            // body parts and character-selection actors can be loaded twice concurrently.
            if (!IsRegisteredRootObject(obj) || obj.Status != GameControlStatus.NonInitialized)
                return;

            if (_queuedForInitialization.Add(obj))
                _objectsToInitialize.Enqueue(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRegisteredRootObject(WorldObject obj)
        {
            return obj != null &&
                   obj.Parent == null &&
                   ReferenceEquals(obj.World, this) &&
                   Objects.Contains(obj);
        }

        private async Task LoadInitializedObjectAsync(WorldObject obj)
        {
            try
            {
                await obj.Load().ConfigureAwait(false);
                if (obj is ModelObject model)
                    await model.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);

                if (obj.Status != GameControlStatus.Ready)
                {
                    // WorldObject.Load() reports content failures through Status=Error and does
                    // not rethrow. Treat that as a normal failed initialization instead of
                    // manufacturing a first-chance exception which breaks the debugger and can
                    // race a manually managed scene object.
                    _logger?.LogWarning(
                        "World object {ObjectType} ({NetworkId:X4}) finished Load() with status {Status}; removing it from world {WorldIndex}.",
                        obj.GetType().Name,
                        obj.NetworkId,
                        obj.Status,
                        WorldIndex);
                    ScheduleFailedObjectRemoval(obj);
                    return;
                }

                var publishCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                MuGame.ScheduleOnMainThread(() =>
                {
                    try
                    {
                        if (!ReferenceEquals(obj.World, this) ||
                            obj.Status == GameControlStatus.Disposed ||
                            !Objects.Contains(obj))
                        {
                            publishCompletion.TrySetResult(false);
                            return;
                        }

                        if (obj is WalkerObject walker && Terrain?.Status == GameControlStatus.Ready)
                            walker.SnapToTerrainHeight(updateCamera: false);

                        if (obj is ModelObject readyModel)
                            readyModel.PrepareRenderResourcesForFirstFrame();

                        bool restoreHidden =
                            _initializationHiddenStates.TryRemove(obj, out bool originalHidden) &&
                            originalHidden;

                        if (restoreHidden)
                        {
                            obj.Hidden = true;
                        }
                        else
                        {
                            ActivateObjectForRendering(
                                obj,
                                forceFullVisibilityRebuild: true);
                        }

                        publishCompletion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        publishCompletion.TrySetException(ex);
                    }
                }, MainThreadDispatcher.WorkPriority.High, $"WorldObject.Publish.{obj.GetType().Name}.{obj.NetworkId:X4}");

                await publishCompletion.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize world object {ObjectType} ({NetworkId:X4}).", obj.GetType().Name, obj.NetworkId);
                ScheduleFailedObjectRemoval(obj);
            }
            finally
            {
                Interlocked.Decrement(ref _activeObjectInitializations);
            }
        }

        private void ScheduleFailedObjectRemoval(WorldObject obj)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _initializationHiddenStates.TryRemove(obj, out _);
                _queuedForInitialization.Remove(obj);

                if (ReferenceEquals(obj.World, this) && Objects.Contains(obj))
                    RemoveObject(obj);

                if (obj.Status != GameControlStatus.Disposed)
                    obj.Dispose();
            }, MainThreadDispatcher.WorkPriority.High,
               $"WorldObject.RemoveFailed.{obj.GetType().Name}.{obj.NetworkId:X4}");
        }

        private int _preparedShadowFrame = int.MinValue;

        internal void PrepareShadowMapForDraw()
        {
            if (Status != GameControlStatus.Ready) return;
            _preparedShadowFrame = MuGame.FrameIndex;

            // Build shadow map before any scene drawing. A newly published modular actor can
            // still expose a bad shader/buffer combination; contain that pass so it cannot
            // abort the whole frame after the target was cleared.
            if (EnableShadows && Constants.ENABLE_SHADOW_MAPPING && GraphicsManager.Instance.ShadowMapRenderer != null)
            {
                var shadowStarted = RenderPassProfiler.Start();
                try
                {
                    GraphicsManager.Instance.ShadowMapRenderer.RenderShadowMap(this);
                }
                catch (Exception ex)
                {
                    RecordRenderFailure(null, "Draw.ShadowMap", ex);
                    RestoreSafeWorldRenderState();
                }
                finally
                {
                    RenderPassProfiler.AddShadow(shadowStarted);
                }
            }
        }

        public override void Draw(GameTime time)
        {
            if (Status != GameControlStatus.Ready) return;

            OverheadNameplateRenderer.BeginFrame();

            // GameScene prepares shadows before binding its scaled/MSAA scene target.
            // Other callers retain the original preparation immediately before drawing.
            if (_preparedShadowFrame != MuGame.FrameIndex)
                PrepareShadowMapForDraw();
            _preparedShadowFrame = int.MinValue;

            var worldBaseStarted = RenderPassProfiler.Start();
            try
            {
                base.Draw(time);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.WorldBase", ex);
                RestoreSafeWorldRenderState();
            }
            finally
            {
                RenderPassProfiler.AddWorldBase(worldBaseStarted);
            }

            var objectsStarted = RenderPassProfiler.Start();
            try
            {
                RenderObjects(time);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.WorldObjects", ex);
                RestoreSafeWorldRenderState();
            }
            finally
            {
                RenderPassProfiler.AddWorldObjects(objectsStarted);
            }
        }

        private void RestoreSafeWorldRenderState()
        {
            try
            {
                Helpers.SpriteBatchScope.ForceReset();
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            }
            catch (Exception stateException)
            {
                _logger?.LogDebug(stateException, "Failed to restore safe world render state.");
            }
        }

        // --- Object Management ---

        public bool IsWalkable(Vector2 position)
        {
            var terrainFlag = Terrain.RequestTerrainFlag((int)position.X, (int)position.Y);
            bool hasNoMove = terrainFlag.HasFlag(TWFlags.NoMove);

            // In Blood Castle, when the event is active (timer started), allow crossing the bridge
            // even if NoMove flag is set (the bridge area is normally blocked until event starts)
            if (hasNoMove && UI.Game.BloodCastleTimeControl.IsEventActive)
            {
                // Check if we're on a Blood Castle map (map IDs 11-17 and 52)
                var charState = MuGame.Network?.GetCharacterState();
                if (charState != null)
                {
                    var mapId = charState.MapId;
                    if ((mapId >= 11 && mapId <= 17) || mapId == 52)
                    {
                        return true; // Allow movement during active Blood Castle event
                    }
                }
            }

            return !hasNoMove;
        }

        private void OnObjectAdded(WorldObject worldObject)
        {

            // NPC roots are world-local. A stale asynchronous spawn or a retained scene
            // reference must never attach an NPC created for another WorldControl instance
            // to the new map. Hide it immediately and remove it through the main-thread queue.
            if (worldObject is NPCObject &&
                worldObject.OwningWorldInstanceId != 0 &&
                worldObject.OwningWorldInstanceId != WorldInstanceId)
            {
                long previousWorldInstanceId = worldObject.OwningWorldInstanceId;
                worldObject.Hidden = true;

                _logger?.LogWarning(
                    "Rejected stale NPC {NpcType} ({NetworkId:X4}) from world instance {OldWorld}; current world instance is {CurrentWorld}.",
                    worldObject.GetType().Name,
                    worldObject.NetworkId,
                    previousWorldInstanceId,
                    WorldInstanceId);

                MuGame.ScheduleOnMainThread(() =>
                {
                    Objects.Remove(worldObject);
                    worldObject.Dispose();
                }, MainThreadDispatcher.WorkPriority.Critical);
                return;
            }

            if (worldObject.OwningWorldInstanceId == 0 || worldObject is PlayerObject)
                worldObject.OwningWorldInstanceId = WorldInstanceId;

            worldObject.World = this;
            worldObject.HiddenChanged += Object_HiddenChanged;
            worldObject.PositionChanged += Object_PositionChanged;
            worldObject.StatusChanged += Object_StatusChanged;

            TrackObjectType(worldObject);
            if (worldObject is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                if (WalkerObjectsById.TryGetValue(walker.NetworkId, out var existing))
                {
                    if (!ReferenceEquals(existing, walker))
                    {
                        _logger?.LogWarning("Replacing WalkerObject ID {Id:X4} - old: {OldType}, new: {NewType}",
                                           walker.NetworkId, existing.GetType().Name, walker.GetType().Name);

                        // Disposing without removing leaves a stale entry in Objects/_walkers.
                        // A later lookup can then suppress publication of the replacement.
                        RemoveObject(existing);
                        if (existing.Status != GameControlStatus.Disposed)
                            existing.Dispose();
                    }
                }
                WalkerObjectsById[walker.NetworkId] = walker; // Always update/add
            }

            RegisterSpatialObject(worldObject);
            _positionDirtyObjects.Add(worldObject);
            MarkWorldGeometryChanged();
            if (worldObject.Status == GameControlStatus.NonInitialized)
                EnqueueObjectInitialization(worldObject);
        }

        private void Object_HiddenChanged(object sender, EventArgs e)
        {
            var obj = sender as WorldObject;
            if (obj == null)
                return;

            if (obj.Hidden)
                RemoveVisibleObject(obj);
            else
                _positionDirtyObjects.Add(obj);

            MarkWorldGeometryChanged();
        }

        private void OnObjectRemoved(WorldObject worldObject)
        {
            UntrackObjectType(worldObject);
            if (worldObject is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                if (WalkerObjectsById.TryGetValue(walker.NetworkId, out var storedWalker))
                {
                    // Only remove if it's the same object reference
                    if (ReferenceEquals(storedWalker, walker))
                    {
                        WalkerObjectsById.Remove(walker.NetworkId);
                        _logger?.LogTrace("Removed walker {Id:X4} from dictionary.", walker.NetworkId);
                    }
                    else
                    {
                        _logger?.LogDebug("Walker {Id:X4} removed from Objects but different object in dictionary.", walker.NetworkId);
                    }
                }
            }

            worldObject.HiddenChanged -= Object_HiddenChanged;
            worldObject.PositionChanged -= Object_PositionChanged;
            worldObject.StatusChanged -= Object_StatusChanged;

            RemoveVisibleObject(worldObject);
            _positionDirtyObjects.Remove(worldObject);
            _spatialDirtyObjects.Remove(worldObject);
            _queuedForInitialization.Remove(worldObject);
            _initializationHiddenStates.TryRemove(worldObject, out _);
            _renderFaults.Remove(worldObject);
            UnregisterSpatialObject(worldObject);
            MarkWorldGeometryChanged();
        }

        private void TrackObjectType(WorldObject obj)
        {
            if (obj is WalkerObject walker)
                _walkers.Add(walker);

            if (obj is PlayerObject player)
                _players.Add(player);

            if (obj is MonsterObject monster)
                _monsters.Add(monster);

            if (obj is DroppedItemObject drop)
                _droppedItems.Add(drop);
        }

        private void UntrackObjectType(WorldObject obj)
        {
            if (obj is WalkerObject walker)
                _walkers.Remove(walker);

            if (obj is PlayerObject player)
                _players.Remove(player);

            if (obj is MonsterObject monster)
                _monsters.Remove(monster);

            if (obj is DroppedItemObject drop)
                _droppedItems.Remove(drop);
        }

        public void DebugListWalkers()
        {
            _logger?.LogDebug("=== Walker Debug Info ===");
            _logger?.LogDebug("Objects collection count: {Count}", _walkers.Count);
            _logger?.LogDebug("WalkerObjectsById count: {Count}", WalkerObjectsById.Count);

            foreach (var walker in _walkers)
            {
                _logger?.LogDebug("Objects: {Type} {Id:X4}", walker.GetType().Name, walker.NetworkId);
            }

            foreach (var kvp in WalkerObjectsById)
            {
                _logger?.LogDebug("Dictionary: {Id:X4} -> {Type}", kvp.Key, kvp.Value.GetType().Name);
            }

            if (this is WalkableWorldControl walkable && walkable.Walker != null)
            {
                _logger?.LogDebug("Local player: {Type} {Id:X4}", walkable.Walker.GetType().Name, walkable.Walker.NetworkId);
            }
            _logger?.LogDebug("=== End Walker Debug ===");
        }

        /// <summary>
        /// Attempts to retrieve a walker by its network ID.
        /// Checks local player first, then dictionary, then full Objects search as fallback.
        /// </summary>
        public virtual bool TryGetWalkerById(ushort networkId, out WalkerObject walker)
        {
            static bool IsUsable(WorldControl world, WalkerObject candidate) =>
                candidate != null &&
                ReferenceEquals(candidate.World, world) &&
                candidate.Status is not (GameControlStatus.Error or GameControlStatus.Disposed) &&
                world.Objects.Contains(candidate);

            // First check: local player in WalkableWorldControl.
            if (this is WalkableWorldControl walkable &&
                walkable.Walker?.NetworkId == networkId &&
                IsUsable(this, walkable.Walker))
            {
                walker = walkable.Walker;
                return true;
            }

            // Second check: dictionary, but never return a disposed/stale instance.
            if (WalkerObjectsById.TryGetValue(networkId, out walker))
            {
                if (IsUsable(this, walker))
                    return true;

                WalkerObjectsById.Remove(networkId);
                walker = null;
            }

            // Third check: fallback search in tracked walkers list.
            for (int i = _walkers.Count - 1; i >= 0; i--)
            {
                var candidate = _walkers[i];
                if (candidate == null || candidate.NetworkId != networkId)
                    continue;

                if (!IsUsable(this, candidate))
                {
                    _walkers.RemoveAt(i);
                    continue;
                }

                walker = candidate;
                WalkerObjectsById[networkId] = walker;
                _logger?.LogDebug("Sync fix: Added walker {Id:X4} to dictionary during lookup.", networkId);
                return true;
            }

            walker = null;
            return false;
        }

        public bool ContainsWalkerId(ushort networkId) =>
            WalkerObjectsById.ContainsKey(networkId);

        public WalkerObject FindWalkerById(ushort networkId) =>
            TryGetWalkerById(networkId, out var walker) ? walker : null;

        public PlayerObject FindPlayerById(ushort networkId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (player != null && player.NetworkId == networkId)
                    return player;
            }
            return null;
        }

        public DroppedItemObject FindDroppedItemById(ushort networkId)
        {
            for (int i = 0; i < _droppedItems.Count; i++)
            {
                var drop = _droppedItems[i];
                if (drop != null && drop.NetworkId == networkId)
                    return drop;
            }
            return null;
        }

        public MonsterObject FindMonsterById(ushort networkId)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                var monster = _monsters[i];
                if (monster != null && monster.NetworkId == networkId)
                    return monster;
            }
            return null;
        }

        /// <summary>
        /// Removes an object from the scene and dictionary if applicable.
        /// </summary>
        public bool RemoveObject(WorldObject obj)
        {
            bool removed = Objects.Remove(obj);
            if (removed && obj is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                WalkerObjectsById.Remove(walker.NetworkId);
            }
            return removed;
        }

        // --- Rendering Helpers ---

        /// <summary>
        /// Allows a specialized world to own the final draw of selected objects while still
        /// keeping them in the regular update, spatial and culling pipelines.
        /// </summary>
        protected virtual bool IsExternallyRenderedObject(WorldObject obj) => false;

        private void RenderObjects(GameTime time)
        {
            _droppedItemSelector.SelectRenderableItems(_droppedItems, time);

            _renderCounter = 0;
            _solidBehind.Clear();
            _transparentObjects.Clear();
            _dedicatedParticleSystems.Clear();
            _lateEffectObjects.Clear();
            _queuedLateEffectObjects.Clear();
            _queuedDamageTexts.Clear();
            _dedicatedStaticMapObjects.Clear();
            _queuedDedicatedStaticMapObjects.Clear();

            var objects = _visibleObjects;

            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];

                if (!obj.Visible || IsExternallyRenderedObject(obj))
                    continue;

                // Non-transparent additive particle systems do not write depth and their
                // color accumulation is order-independent. Pull them out of depth buckets so
                // all opaque models can finish first, then submit the particles through a
                // small number of texture-sorted SpriteBatch groups after all map meshes.
                if (obj is SourceParticleSystem sourceParticles)
                {
                    if (!sourceParticles.HasActiveParticles)
                    {
                        sourceParticles.RenderOrder = ++_renderCounter;
                        FrameMetrics.InactiveParticleSystemsSkipped++;
                        continue;
                    }

                    if (sourceParticles.CanUseDedicatedWorldBatch)
                    {
                        _queuedLateEffectObjects.Add(sourceParticles);
                        _dedicatedParticleSystems.Add(sourceParticles);
                        continue;
                    }
                }

                if (obj is ModelObject staticMapModel &&
                    staticMapModel.CanUseDedicatedStaticMapRenderQueue())
                {
                    _dedicatedStaticMapObjects.Add(staticMapModel);
                    continue;
                }

                float depth = obj.Depth;
                obj.RenderSortDepth = depth;

                if (obj.IsTransparent)
                {
                    _transparentObjects.Add(obj);
                }
                else
                {
                    if (Constants.ENABLE_BATCH_OPTIMIZED_SORTING)
                        WorldObjectOpaqueBatchComparer.Prepare(obj, depth);

                    _solidBehind.Add(obj);
                }
            }

            FrameMetrics.SolidBehindObjects = _solidBehind.Count;
            FrameMetrics.SolidInFrontObjects = 0;
            FrameMetrics.TransparentObjects = _transparentObjects.Count;
            FrameMetrics.DedicatedParticleSystems = _dedicatedParticleSystems.Count;
            FrameMetrics.DedicatedStaticMapObjects = _dedicatedStaticMapObjects.Count;

            if (_solidBehind.Count > 1)
            {
                IComparer<WorldObject> comparer = Constants.ENABLE_BATCH_OPTIMIZED_SORTING
                    ? WorldObjectOpaqueBatchComparer.Instance
                    : WorldObjectDepthAsc.Instance;
                _solidBehind.Sort(comparer);
            }

            if (_transparentObjects.Count > 1)
                _transparentObjects.Sort(WorldObjectDepthDesc.Instance);

            // Attached fire/glow must wait too: RGBA bridge/grass meshes are submitted
            // in DrawAfter and would otherwise paint over effects in front of them.
            _collectLateEffectDraws = true;
            try
            {
                DrawDedicatedStaticMapObjects(time);
                DrawListWithSpriteBatchGrouping(_solidBehind, DepthStateDefault, time);
                DrawListWithSpriteBatchGrouping(_transparentObjects, DepthStateDepthRead, time);
                DrawAfterPass(_solidBehind, DepthStateDefault, time);
                DrawAfterPass(_transparentObjects, DepthStateDepthRead, time);
            }
            finally
            {
                _collectLateEffectDraws = false;
            }

            FrameMetrics.DedicatedParticleSystems = _dedicatedParticleSystems.Count;
            DrawDedicatedParticleSystems(time);
            DrawListWithSpriteBatchGrouping(_lateEffectObjects, DepthStateDepthRead, time);
            _lateEffectObjects.Clear();
            _queuedLateEffectObjects.Clear();

            // SourceMain5.2 renders effects after every world-object pass. Keep additive
            // effects depth-tested, but defer them until map meshes (including their
            // transparent DrawAfter meshes) have finished, so geometry behind an effect
            // cannot paint over it later in the frame.
            try
            {
                FieryAuraEffect.FlushLateWorldDraws(this);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.LateFieryAura", ex);
            }

            try
            {
                ScrollOfFlameEffect.FlushLateWorldDraws(this);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.LateScrollOfFlame", ex);
            }

            DrawQueuedDamageTexts(time);

            try
            {
                OverheadNameplateRenderer.FlushQueuedNameplates(GraphicsManager.Instance.Sprite);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "OverheadNameplates", ex);
            }

            LogRenderMetricsIfEnabled();
        }

        internal bool TryQueueLateEffectDraw(WorldObject obj)
        {
            if (!_collectLateEffectDraws || obj == null || !obj.Visible ||
                !ReferenceEquals(obj.World, this) ||
                (obj is not SpriteObject && obj is not SourceParticleSystem))
                return false;

            BlendState blend = obj.BlendState;
            if (blend == null || blend.ColorBlendFunction != BlendFunction.Add ||
                blend.ColorDestinationBlend != Blend.One ||
                (blend.ColorSourceBlend != Blend.One && blend.ColorSourceBlend != Blend.SourceAlpha))
                return false;

            if (_queuedLateEffectObjects.Add(obj))
            {
                if (obj is SourceParticleSystem particles && particles.CanUseDedicatedWorldBatch)
                    _dedicatedParticleSystems.Add(particles);
                else
                    _lateEffectObjects.Add(obj);
            }
            return true;
        }

        private void DrawDedicatedParticleSystems(GameTime time)
        {
            int count = _dedicatedParticleSystems.Count;
            if (count == 0)
                return;

            if (count > 1)
                _dedicatedParticleSystems.Sort(SourceParticleBatchComparer.Instance);

            Camera camera = Camera.Instance;
            GraphicsDevice graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
            if (camera == null || graphicsDevice == null || spriteBatch == null)
                return;

            SourceParticleSystem.RenderContext context =
                SourceParticleSystem.CreateRenderContext(camera, graphicsDevice.Viewport);
            SpriteBatchScope? scope = null;
            BlendState currentBlend = null;

            for (int i = 0; i < count; i++)
            {
                SourceParticleSystem particles = _dedicatedParticleSystems[i];
                if (particles == null || ShouldSkipRender(particles))
                {
                    if (particles != null)
                        particles.RenderOrder = ++_renderCounter;
                    continue;
                }

                BlendState blend = particles.ParticleBatchBlendState;
                if (scope == null || !ReferenceEquals(currentBlend, blend))
                {
                    CloseSpriteScopeSafely(ref scope, "Draw.ParticleBatchStateChange");
                    scope = new SpriteBatchScope(
                        spriteBatch,
                        SpriteSortMode.Texture,
                        blend,
                        SamplerState.LinearClamp,
                        DepthStencilState.DepthRead,
                        RasterizerState.CullNone);
                    currentBlend = blend;
                    FrameMetrics.ParticleBatchBegins++;
                }

                try
                {
                    int drawn = particles.DrawIntoActiveBatch(context);
                    if (drawn < 0)
                    {
                        FrameMetrics.ParticleSystemsCulled++;
                    }
                    else
                    {
                        FrameMetrics.ParticleSprites += drawn;
                        ClearRenderFault(particles, "Draw.ParticleBatch");
                    }
                }
                catch (Exception ex)
                {
                    CloseSpriteScopeSafely(ref scope, "Draw.ParticleBatchRecovery");
                    currentBlend = null;
                    RecordRenderFailure(particles, "Draw.ParticleBatch", ex);
                }

                particles.RenderOrder = ++_renderCounter;
                FrameMetrics.SpriteBatchObjects++;
            }

            CloseSpriteScopeSafely(ref scope, "Draw.ParticleBatchEnd");
        }

        private void DrawDedicatedStaticMapObjects(GameTime time)
        {
            int count = _dedicatedStaticMapObjects.Count;
            if (count == 0)
                return;

            SetDepthState(DepthStateDefault);
            for (int i = 0; i < count; i++)
            {
                ModelObject model = _dedicatedStaticMapObjects[i];
                if (model == null || ShouldSkipRender(model))
                    continue;

                ModelObject.StaticMapInstancingQueueResult result =
                    ModelObject.TryQueueStaticMapObjectForInstancing(model);
                if (result == ModelObject.StaticMapInstancingQueueResult.Full)
                {
                    _queuedDedicatedStaticMapObjects.Add(model);
                    _renderFaults.Remove(model);
                    model.RenderOrder = ++_renderCounter;
                    FrameMetrics.ModelObjects++;
                    continue;
                }

                // A texture or shared GPU buffer can be replaced between classification and
                // drawing. Keep a contained fallback instead of dropping the placement.
                try
                {
                    model.Draw(time);
                    ClearRenderFault(model, "Draw.StaticMapFallback");
                    FrameMetrics.ModelObjects++;
                }
                catch (Exception ex)
                {
                    RecordRenderFailure(model, "Draw.StaticMapFallback", ex);
                }

                model.RenderOrder = ++_renderCounter;
            }

            if (ModelObject.HasPendingStaticMapInstancingBatches() &&
                !FlushStaticMapBatchesSafely())
            {
                // This path is intentionally failure-only. Restore the classic opaque draw in
                // the same frame so a lost shader or shared GPU buffer cannot create holes.
                for (int i = 0; i < _queuedDedicatedStaticMapObjects.Count; i++)
                {
                    ModelObject model = _queuedDedicatedStaticMapObjects[i];
                    if (model == null || ShouldSkipRender(model))
                        continue;

                    model.CancelStaticMapInstancingForCurrentFrame();
                    ModelObject.RegisterStaticMapInstancingFallback();
                    try
                    {
                        model.Draw(time);
                        ClearRenderFault(model, "Draw.StaticMapBatchFallback");
                    }
                    catch (Exception ex)
                    {
                        RecordRenderFailure(model, "Draw.StaticMapBatchFallback", ex);
                    }
                }
            }

            _queuedDedicatedStaticMapObjects.Clear();
        }

        private void LogRenderMetricsIfEnabled()
        {
            if (Constants.RENDER_METRICS_LEVEL < 2)
                return;

            int frame = MuGame.FrameIndex;
            if (frame - _lastRenderMetricsLogFrame < 180)
                return;

            _lastRenderMetricsLogFrame = frame;
            _logger?.LogInformation(
                "World perf W:{WorldIndex} Cull:{CullMode} C:{CullCandidates} V:{Visible} Ms:{CullMs:F2} Lists:{Behind}/{Front}/{Transparent} StaticBatch:{StaticBatch} Particles:{ParticleSystems}/{ParticleSprites}/B{ParticleBegins}/C{ParticleCulled}/I{ParticleInactive} DrawObj S:{SpriteObjects} M:{ModelObjects} UpdateSkip:{StaticUpdateSkips} AfterSkip:{DrawAfterSkips} Anim U:{AnimUpdates} Skip:{AnimSkips} LQ:{LowQuality}",
                WorldIndex,
                LastCullWasRebuild ? "R" : "I",
                FrameMetrics.CullCandidates,
                FrameMetrics.VisibleObjects,
                FrameMetrics.CullMs,
                FrameMetrics.SolidBehindObjects,
                FrameMetrics.SolidInFrontObjects,
                FrameMetrics.TransparentObjects,
                FrameMetrics.DedicatedStaticMapObjects,
                FrameMetrics.DedicatedParticleSystems,
                FrameMetrics.ParticleSprites,
                FrameMetrics.ParticleBatchBegins,
                FrameMetrics.ParticleSystemsCulled,
                FrameMetrics.InactiveParticleSystemsSkipped,
                FrameMetrics.SpriteBatchObjects,
                FrameMetrics.ModelObjects,
                FrameMetrics.StaticMapUpdateSkips,
                FrameMetrics.DrawAfterSkips,
                FrameMetrics.AnimationUpdates,
                FrameMetrics.AnimationSkips,
                FrameMetrics.LowQualityObjects);
        }

        private void DrawListWithSpriteBatchGrouping(List<WorldObject> list, DepthStencilState depthState, GameTime time)
        {
            if (list.Count == 0)
                return;

            SetDepthState(depthState);
            bool canUseMapInstancing = depthState == DepthStateDefault && Constants.ENABLE_MAP_OBJECT_INSTANCING;
            bool canUseWalkerCrowdInstancing = depthState == DepthStateDefault && Constants.ENABLE_WALKER_CROWD_INSTANCING;
            _queuedCrowdSidePasses.Clear();
            _queuedSolidStaticMapObjects.Clear();

            var spriteBatch = GraphicsManager.Instance.Sprite;
            Helpers.SpriteBatchScope? scope = null;
            BlendState currentBlend = null;
            SamplerState currentSampler = null;
            DepthStencilState currentBatchDepth = null;

            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj == null)
                    continue;

                if (ShouldSkipRender(obj))
                {
                    obj.RenderOrder = ++_renderCounter;
                    continue;
                }

                if (TryQueueLateEffectDraw(obj))
                    continue;

                bool usesSpriteBatch =
                    obj is SpriteObject ||
                    obj is SourceParticleSystem ||
                    obj is ElfBuffOrbTrail;

                if (usesSpriteBatch)
                {
                    FrameMetrics.SpriteBatchObjects++;

                    if (canUseWalkerCrowdInstancing &&
                        (ModelObject.HasPendingWalkerCrowdInstancingBatches() || _queuedCrowdSidePasses.Count > 0))
                    {
                        FlushWalkerCrowdBatchesAndSidePasses(time);
                    }

                    if (canUseMapInstancing && ModelObject.HasPendingStaticMapInstancingBatches() &&
                        !FlushStaticMapBatchesSafely())
                    {
                        // Same failure-only fallback as the dedicated static-map path below:
                        // redraw queued placements classically so a lost batch cannot
                        // leave tagged meshes undrawn for this frame.
                        FallbackQueuedSolidStaticMapObjects(time);
                    }

                    var blend = obj.BlendState ?? BlendState.AlphaBlend;
                    SamplerState sampler;
                    if (obj is SourceParticleSystem || obj is ElfBuffOrbTrail)
                    {
                        sampler = SamplerState.LinearClamp;
                    }
                    else
                    {
                        sampler = ReferenceEquals(blend, BlendState.Additive)
                            ? GraphicsManager.GetQualityLinearSamplerState()
                            : GraphicsManager.GetQualitySamplerState();
                    }
                    // Additive sprites must not write depth because their quads would occlude
                    // later transparent geometry.
                    var objectDepthState = ResolveObjectDepthState(obj, depthState);
                    var batchDepth =
                        obj is SourceParticleSystem ||
                        obj is ElfBuffOrbTrail ||
                        obj is ElfBuffOrbitingLight
                            ? DepthStencilState.DepthRead
                            : objectDepthState;

                    if (scope == null ||
                        !ReferenceEquals(blend, currentBlend) ||
                        !ReferenceEquals(sampler, currentSampler) ||
                        !ReferenceEquals(batchDepth, currentBatchDepth))
                    {
                        CloseSpriteScopeSafely(ref scope, "Draw.SpriteBatchStateChange");
                        scope = new Helpers.SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, blend, sampler, batchDepth);
                        currentBlend = blend;
                        currentSampler = sampler;
                        currentBatchDepth = batchDepth;
                    }

                    try
                    {
                        obj.Draw(time);
                        ClearRenderFault(obj, "Draw.Sprite");
                    }
                    catch (Exception ex)
                    {
                        // A failed SpriteBatch draw may leave the current batch in a
                        // partially configured state. Close it before continuing.
                        CloseSpriteScopeSafely(ref scope, "Draw.SpriteBatchRecovery");
                        currentBlend = null;
                        currentSampler = null;
                        currentBatchDepth = null;
                        RecordRenderFailure(obj, "Draw.Sprite", ex);
                    }
                }
                else
                {
                    if (obj is ModelObject)
                        FrameMetrics.ModelObjects++;

                    CloseSpriteScopeSafely(ref scope, "Draw.SpriteBatchToModel");
                    currentBlend = null;
                    currentSampler = null;
                    currentBatchDepth = null;

                    if (canUseWalkerCrowdInstancing &&
                        ModelObject.IsWalkerCrowdInstancingCandidate(obj) &&
                        ModelObject.TryQueueWalkerCrowdForInstancing(obj))
                    {
                        if (obj is ModelObject queuedMonster)
                            _queuedCrowdSidePasses.Add(queuedMonster);

                        obj.RenderOrder = ++_renderCounter;
                        continue;
                    }

                    var staticMapQueueResult = canUseMapInstancing
                        ? ModelObject.TryQueueStaticMapObjectForInstancing(obj)
                        : ModelObject.StaticMapInstancingQueueResult.None;

                    if (canUseMapInstancing &&
                        staticMapQueueResult == ModelObject.StaticMapInstancingQueueResult.None &&
                        ModelObject.IsStaticMapInstancingPathAvailable() &&
                        obj is ModelObject mapModel &&
                        mapModel.IsMapPlacementObject)
                        ModelObject.RegisterStaticMapInstancingFallback();

                    if (canUseMapInstancing &&
                        staticMapQueueResult != ModelObject.StaticMapInstancingQueueResult.None &&
                        obj is ModelObject queuedMapModel)
                        _queuedSolidStaticMapObjects.Add(queuedMapModel);

                    SetDepthState(ResolveObjectDepthState(obj, depthState));
                    try
                    {
                        obj.Draw(time);
                        ClearRenderFault(obj, "Draw.Model");
                    }
                    catch (Exception ex)
                    {
                        RecordRenderFailure(obj, "Draw.Model", ex);
                    }
                }

                obj.RenderOrder = ++_renderCounter;
            }

            CloseSpriteScopeSafely(ref scope, "Draw.SpriteBatchEnd");

            if (canUseWalkerCrowdInstancing &&
                (ModelObject.HasPendingWalkerCrowdInstancingBatches() || _queuedCrowdSidePasses.Count > 0))
            {
                FlushWalkerCrowdBatchesAndSidePasses(time);
            }

            if (canUseMapInstancing && ModelObject.HasPendingStaticMapInstancingBatches() &&
                !FlushStaticMapBatchesSafely())
            {
                // Failure-only fallback mirroring DrawDedicatedStaticMapObjects:
                // classic redraw of everything queued in this list so tagged meshes
                // are never left undrawn for the frame.
                FallbackQueuedSolidStaticMapObjects(time);
            }
        }

        private void FallbackQueuedSolidStaticMapObjects(GameTime time)
        {
            for (int i = 0; i < _queuedSolidStaticMapObjects.Count; i++)
            {
                ModelObject model = _queuedSolidStaticMapObjects[i];
                if (model == null || ShouldSkipRender(model))
                    continue;

                model.CancelStaticMapInstancingForCurrentFrame();
                ModelObject.RegisterStaticMapInstancingFallback();
                try
                {
                    model.Draw(time);
                    ClearRenderFault(model, "Draw.StaticMapBatchFallback");
                }
                catch (Exception ex)
                {
                    RecordRenderFailure(model, "Draw.StaticMapBatchFallback", ex);
                }
            }

            _queuedSolidStaticMapObjects.Clear();
        }

        private void FlushWalkerCrowdBatchesAndSidePasses(GameTime time)
        {
            try
            {
                if (ModelObject.HasPendingWalkerCrowdInstancingBatches())
                    ModelObject.FlushWalkerCrowdInstancingBatches(this);

                for (int i = 0; i < _queuedCrowdSidePasses.Count; i++)
                {
                    ModelObject model = _queuedCrowdSidePasses[i];
                    if (model == null || ShouldSkipRender(model))
                        continue;

                    try
                    {
                        model.DrawQueuedCrowdInstancingSidePasses(time);
                        ClearRenderFault(model, "Draw.CrowdSidePass");
                    }
                    catch (Exception ex)
                    {
                        RecordRenderFailure(model, "Draw.CrowdSidePass", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.CrowdBatch", ex);
                ModelObject.ResetWorldScopedInstancingState();
            }
            finally
            {
                _queuedCrowdSidePasses.Clear();
            }
        }

        private bool FlushStaticMapBatchesSafely()
        {
            try
            {
                return ModelObject.FlushStaticMapInstancingBatches(this);
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "Draw.StaticMapBatch", ex);
                ModelObject.ResetWorldScopedInstancingState();
                return false;
            }
        }

        private void DrawAfterPass(List<WorldObject> list, DepthStencilState state, GameTime time)
        {
            int objCount = list.Count;
            if (objCount == 0)
                return;

            SetDepthState(state);

            for (int i = 0; i < objCount; i++)
            {
                WorldObject obj = list[i];
                if (obj == null || ShouldSkipRender(obj))
                    continue;

                if (obj is DamageTextObject damageText)
                {
                    _queuedDamageTexts.Add(damageText);
                    continue;
                }

                if (obj is ModelObject model && model.CanSkipDefaultDrawAfterPass())
                {
                    FrameMetrics.DrawAfterSkips++;
                    continue;
                }

                SetDepthState(ResolveObjectDepthState(obj, state));
                try
                {
                    if (obj is ModelObject cutout && _cutoutMapBatch.TryQueue(cutout, time))
                        continue;

                    _cutoutMapBatch.Flush(time);
                    obj.DrawAfter(time);
                    ClearRenderFault(obj, "DrawAfter");
                }
                catch (Exception ex)
                {
                    _cutoutMapBatch.Flush(time);
                    RecordRenderFailure(obj, "DrawAfter", ex);
                }
            }

            _cutoutMapBatch.Flush(time);
        }

        private void DrawQueuedDamageTexts(GameTime time)
        {
            // Keep screen-space labels above the late world effects, in one batch.
            if (_queuedDamageTexts.Count == 0)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            try
            {
                using (new Helpers.SpriteBatchScope(
                    sb,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.AnisotropicClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    UiScaler.SpriteTransform))
                {
                    for (int i = 0; i < _queuedDamageTexts.Count; i++)
                    {
                        DamageTextObject damageText = _queuedDamageTexts[i];
                        if (ShouldSkipRender(damageText))
                            continue;

                        try
                        {
                            damageText.DrawAfter(time);
                            ClearRenderFault(damageText, "DrawAfter.DamageText");
                        }
                        catch (Exception ex)
                        {
                            RecordRenderFailure(damageText, "DrawAfter.DamageText", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RecordRenderFailure(null, "DrawAfter.DamageBatch", ex);
            }
            finally
            {
                _queuedDamageTexts.Clear();
            }
        }

        private void CloseSpriteScopeSafely(ref Helpers.SpriteBatchScope? scope, string phase)
        {
            if (!scope.HasValue)
                return;

            try
            {
                scope.Value.Dispose();
            }
            catch (Exception ex)
            {
                Helpers.SpriteBatchScope.ForceReset();
                RecordRenderFailure(null, phase, ex);
            }
            finally
            {
                scope = null;
            }
        }

        private bool ShouldSkipRender(WorldObject obj)
        {
            if (obj == null || !_renderFaults.TryGetValue(obj, out RenderFaultState state))
                return false;

            return MuGame.FrameIndex < state.RetryFrame;
        }

        private void ClearRenderFault(WorldObject obj, string phase)
        {
            if (obj != null &&
                _renderFaults.TryGetValue(obj, out RenderFaultState state) &&
                string.Equals(state.Phase, phase, StringComparison.Ordinal))
            {
                _renderFaults.Remove(obj);
            }
        }

        private static string GetSafeRenderObjectName(WorldObject obj)
        {
            if (obj == null)
                return null;

            try
            {
                return string.IsNullOrWhiteSpace(obj.DisplayName)
                    ? obj.ObjectName
                    : obj.DisplayName;
            }
            catch
            {
                // Failure reporting must never throw a second exception while handling Draw.
                return obj.ObjectName;
            }
        }

        private void RecordRenderFailure(WorldObject obj, string phase, Exception exception)
        {
            string objectName = GetSafeRenderObjectName(obj);
            FrameMetrics.RenderFailures++;
            FrameMetrics.LastRenderFailureSequence = Interlocked.Increment(ref _renderFailureSequence);
            FrameMetrics.LastRenderFailureFrameIndex = MuGame.FrameIndex;
            FrameMetrics.LastRenderFailurePhase = phase;
            FrameMetrics.LastRenderFailureType = obj?.GetType().FullName ?? "WorldRenderBatch";
            FrameMetrics.LastRenderFailureName = objectName;
            FrameMetrics.LastRenderFailureNetworkId = obj?.NetworkId ?? 0;
            FrameMetrics.LastRenderFailureMessage = exception.Message;

            int failureCount = 1;
            int retryDelay = 1;
            if (obj != null)
            {
                if (!_renderFaults.TryGetValue(obj, out RenderFaultState state))
                {
                    state = new RenderFaultState();
                    _renderFaults[obj] = state;
                }

                if (!string.Equals(state.Phase, phase, StringComparison.Ordinal))
                    state.FailureCount = 0;

                state.Phase = phase;
                state.FailureCount++;
                failureCount = state.FailureCount;
                retryDelay = Math.Min(120, 1 << Math.Min(6, state.FailureCount));
                state.RetryFrame = MuGame.FrameIndex + retryDelay;
            }

            // Log the first failure and exponential checkpoints. Repeated failures are
            // retried with backoff instead of flooding the log or blacking out the frame.
            if (failureCount == 1 || (failureCount & (failureCount - 1)) == 0)
            {
                _logger?.LogError(
                    exception,
                    "Contained render failure in {Phase} for {ObjectType} '{ObjectName}' ({NetworkId:X4}); retry in {RetryFrames} frames.",
                    phase,
                    obj?.GetType().Name ?? "batch",
                    objectName ?? "<unnamed>",
                    obj?.NetworkId ?? 0,
                    retryDelay);

                MuGame.Diagnostics?.PublishEvent(
                    "render-object",
                    $"Contained {phase} failure for {obj?.GetType().Name ?? "batch"}",
                    Client.Telemetry.TelemetrySeverity.Error,
                    new Dictionary<string, string>
                    {
                        ["phase"] = phase,
                        ["objectType"] = obj?.GetType().FullName ?? "WorldRenderBatch",
                        ["objectName"] = objectName ?? string.Empty,
                        ["networkId"] = (obj?.NetworkId ?? 0).ToString("X4"),
                        ["retryFrames"] = retryDelay.ToString(),
                        ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["message"] = exception.Message
                    });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDepthState(DepthStencilState state)
        {
            if (_currentDepthState != state)
            {
                GraphicsDevice.DepthStencilState = state;
                _currentDepthState = state;
            }
        }

        private static DepthStencilState ResolveObjectDepthState(WorldObject obj, DepthStencilState passState)
        {
            if (obj?.DepthState != null && !ReferenceEquals(obj.DepthState, DepthStateDefault))
                return obj.DepthState;

            return passState;
        }

        // Fast path for loops where camera info is already cached
        private static bool IsObjectInView(WorldObject obj, Vector2 cam2, float maxDistSq, BoundingFrustum frustum)
        {
            var pos3 = obj.WorldPosition.Translation;
            float dx = cam2.X - pos3.X;
            float dy = cam2.Y - pos3.Y;
            if (dx * dx + dy * dy > maxDistSq)
                return false;

            if (frustum == null)
                return false;

            BoundingBox bounds = obj.BoundingBoxWorld;
            Vector3 margin = new(WorldCullGuardBand);
            return frustum.Contains(new BoundingBox(bounds.Min - margin, bounds.Max + margin)) != ContainmentType.Disjoint;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldForceVisible(WorldObject obj)
        {
            if (obj == null)
                return false;

            var policy = obj.RenderPolicy;

            if (policy.ForceVisible || (obj is WalkerObject walker && walker.IsMainWalker))
            {
                return true;
            }

            // Character-selection actors are staged at a fixed cinematic position. Their
            // modular bounding boxes can briefly be stale while body parts switch models,
            // so they must not disappear because of a transient frustum result.
            if (obj.World?.WorldIndex == 94 && obj is PlayerObject)
                return true;

            return obj.World?.WorldIndex == 95
                && (policy.ForceVisibleInLoginWorld || HasForceVisibleEffectChild(obj));
        }

        private static bool HasForceVisibleEffectChild(WorldObject obj)
        {
            if (obj == null)
                return false;

            var children = obj.Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child?.RenderPolicy.ForceVisible == true || child?.RenderPolicy.ForceVisibleInLoginWorld == true)
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddVisibleObject(WorldObject obj)
        {
            if (obj == null)
                return;

            if (_visibleObjectIndices.TryAdd(obj, _visibleObjects.Count))
                _visibleObjects.Add(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveVisibleObject(WorldObject obj)
        {
            if (obj == null)
                return;

            if (!_visibleObjectIndices.TryGetValue(obj, out int index) ||
                (uint)index >= (uint)_visibleObjects.Count)
            {
                index = _visibleObjects.IndexOf(obj);
                if (index < 0)
                {
                    _visibleObjectIndices.Remove(obj);
                    return;
                }
            }

            if (_isUpdatingVisibleObjects)
            {
                _visibleObjects[index] = null;
                _visibleObjectIndices.Remove(obj);
                _visibleObjectsNeedCompaction = true;
                return;
            }

            int lastIndex = _visibleObjects.Count - 1;
            WorldObject last = _visibleObjects[lastIndex];
            if (index != lastIndex)
            {
                _visibleObjects[index] = last;
                if (last != null)
                    _visibleObjectIndices[last] = index;
            }

            _visibleObjects.RemoveAt(lastIndex);
            _visibleObjectIndices.Remove(obj);
        }

        private void CompactVisibleObjects()
        {
            if (!_visibleObjectsNeedCompaction)
                return;

            int writeIndex = 0;
            for (int readIndex = 0; readIndex < _visibleObjects.Count; readIndex++)
            {
                WorldObject obj = _visibleObjects[readIndex];
                if (obj == null)
                    continue;

                _visibleObjects[writeIndex] = obj;
                _visibleObjectIndices[obj] = writeIndex;
                writeIndex++;
            }

            if (writeIndex < _visibleObjects.Count)
                _visibleObjects.RemoveRange(writeIndex, _visibleObjects.Count - writeIndex);

            _visibleObjectsNeedCompaction = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PackSpatialSector(int sectorX, int sectorY) => (sectorY * SpatialSectorsPerAxis) + sectorX;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetSpatialSector(Vector3 worldPos, out int sectorX, out int sectorY)
        {
            int tileX = (int)MathF.Floor(worldPos.X / Constants.TERRAIN_SCALE);
            int tileY = (int)MathF.Floor(worldPos.Y / Constants.TERRAIN_SCALE);

            if ((uint)tileX >= Constants.TERRAIN_SIZE || (uint)tileY >= Constants.TERRAIN_SIZE)
            {
                sectorX = 0;
                sectorY = 0;
                return false;
            }

            sectorX = tileX / SpatialSectorTileSize;
            sectorY = tileY / SpatialSectorTileSize;

            if ((uint)sectorX >= SpatialSectorsPerAxis || (uint)sectorY >= SpatialSectorsPerAxis)
            {
                sectorX = 0;
                sectorY = 0;
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveSpatialSector(WorldObject obj)
        {
            if (obj == null)
                return SpatialInvalidSector;

            return TryGetSpatialSector(obj.WorldPosition.Translation, out int sectorX, out int sectorY)
                ? PackSpatialSector(sectorX, sectorY)
                : SpatialInvalidSector;
        }

        private void AddToSpatialBucket(WorldObject obj, int sector)
        {
            if (sector == SpatialInvalidSector)
            {
                _spatialOffGridObjects.Add(obj);
                return;
            }

            int sectorX = sector % SpatialSectorsPerAxis;
            int sectorY = sector / SpatialSectorsPerAxis;
            _spatialSectors[sectorX, sectorY].Add(obj);
        }

        private void RemoveFromSpatialBucket(WorldObject obj, int sector)
        {
            if (sector == SpatialInvalidSector)
            {
                _spatialOffGridObjects.Remove(obj);
                return;
            }

            int sectorX = sector % SpatialSectorsPerAxis;
            int sectorY = sector / SpatialSectorsPerAxis;
            _spatialSectors[sectorX, sectorY].Remove(obj);
        }

        private void RegisterSpatialObject(WorldObject obj)
        {
            if (obj == null || _spatialObjectSectors.ContainsKey(obj))
                return;

            int sector = ResolveSpatialSector(obj);
            _spatialObjectSectors[obj] = sector;
            AddToSpatialBucket(obj, sector);
        }

        private void UnregisterSpatialObject(WorldObject obj)
        {
            if (obj == null || !_spatialObjectSectors.TryGetValue(obj, out int oldSector))
                return;

            RemoveFromSpatialBucket(obj, oldSector);
            _spatialObjectSectors.Remove(obj);
        }

        private void UpdateSpatialRegistration(WorldObject obj)
        {
            if (obj == null)
                return;

            if (obj.Status == GameControlStatus.Disposed || obj.Status == GameControlStatus.Error)
            {
                UnregisterSpatialObject(obj);
                return;
            }

            int newSector = ResolveSpatialSector(obj);
            if (!_spatialObjectSectors.TryGetValue(obj, out int oldSector))
            {
                _spatialObjectSectors[obj] = newSector;
                AddToSpatialBucket(obj, newSector);
                return;
            }

            if (oldSector == newSector)
                return;

            RemoveFromSpatialBucket(obj, oldSector);
            AddToSpatialBucket(obj, newSector);
            _spatialObjectSectors[obj] = newSector;
        }

        private void RebuildSpatialGridFromSnapshot()
        {
            foreach (var pair in _spatialSectors)
                pair.Clear();

            _spatialOffGridObjects.Clear();
            _spatialObjectSectors.Clear();

            var snapshot = Objects.GetSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var obj = snapshot[i];
                if (obj == null)
                    continue;

                int sector = ResolveSpatialSector(obj);
                _spatialObjectSectors[obj] = sector;
                AddToSpatialBucket(obj, sector);
            }

            _spatialDirtyObjects.Clear();
        }

        private void FlushSpatialUpdates()
        {
            if (_spatialDirtyObjects.Count == 0)
                return;

            foreach (WorldObject obj in _spatialDirtyObjects)
                UpdateSpatialRegistration(obj);

            _spatialDirtyObjects.Clear();
        }

        private void BuildSpatialCandidates(Vector2 center, float maxViewDistance)
        {
            _spatialCandidates.Clear();

            if (!TryGetSpatialSector(new Vector3(center, 0f), out int centerSectorX, out int centerSectorY))
            {
                centerSectorX = Math.Clamp((int)MathF.Floor(center.X / Constants.TERRAIN_SCALE) / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
                centerSectorY = Math.Clamp((int)MathF.Floor(center.Y / Constants.TERRAIN_SCALE) / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            }

            int sectorRadius = (int)MathF.Ceiling(maxViewDistance / (Constants.TERRAIN_SCALE * SpatialSectorTileSize)) + SpatialRebuildPaddingSectors;
            int minSectorX = Math.Max(0, centerSectorX - sectorRadius);
            int maxSectorX = Math.Min(SpatialSectorsPerAxis - 1, centerSectorX + sectorRadius);
            int minSectorY = Math.Max(0, centerSectorY - sectorRadius);
            int maxSectorY = Math.Min(SpatialSectorsPerAxis - 1, centerSectorY + sectorRadius);

            for (int sectorY = minSectorY; sectorY <= maxSectorY; sectorY++)
            {
                for (int sectorX = minSectorX; sectorX <= maxSectorX; sectorX++)
                {
                    var bucket = _spatialSectors[sectorX, sectorY];
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        WorldObject obj = bucket[i];
                        if (obj != null)
                            _spatialCandidates.Add(obj);
                    }
                }
            }

            for (int i = 0; i < _spatialOffGridObjects.Count; i++)
            {
                WorldObject obj = _spatialOffGridObjects[i];
                if (obj != null)
                    _spatialCandidates.Add(obj);
            }

            // Every registered object belongs to exactly one sector (or the off-grid list),
            // so the normal candidate walk cannot produce duplicates. Only force-visible
            // objects outside the selected sector rectangle need to be appended.
            var snapshot = Objects.GetSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                WorldObject obj = snapshot[i];
                if (!ShouldForceVisible(obj) || !_spatialObjectSectors.TryGetValue(obj, out int sector))
                    continue;

                if (sector == SpatialInvalidSector)
                    continue; // Already included through the off-grid list.

                int sectorX = sector % SpatialSectorsPerAxis;
                int sectorY = sector / SpatialSectorsPerAxis;
                if (sectorX >= minSectorX && sectorX <= maxSectorX &&
                    sectorY >= minSectorY && sectorY <= maxSectorY)
                {
                    continue; // Already included through its regular bucket.
                }

                _spatialCandidates.Add(obj);
            }
        }

        private void RebuildVisibleObjects()
        {
            _cullingStopwatch.Restart();
            _visibleObjects.Clear();
            _visibleObjectIndices.Clear();
            _visibleObjectsNeedCompaction = false;
            _positionDirtyObjects.Clear();

            var camera = Camera.Instance;
            var camPos = camera.Position;
            var cam2 = new Vector2(camPos.X, camPos.Y);
            float maxViewDistance = camera.ViewFar + Constants.MAX_CAMERA_DISTANCE + 250f;
            float maxDistSq = maxViewDistance * maxViewDistance;
            var frustum = camera.Frustum;
            IReadOnlyList<WorldObject> snapshot;
            if (Constants.ENABLE_CROWD_SPATIAL_CULLING)
            {
                if (_spatialObjectSectors.Count != Objects.Count)
                {
                    RebuildSpatialGridFromSnapshot();
                }

                var focus = camera.Target;
                BuildSpatialCandidates(new Vector2(focus.X, focus.Y), maxViewDistance);
                snapshot = _spatialCandidates;
            }
            else
            {
                snapshot = Objects.GetSnapshot();
            }

            LastCullCandidateCount = snapshot.Count;
            bool useParallel = Environment.ProcessorCount > 1 &&
                               snapshot.Count >= ParallelVisibleRebuildThreshold;

            if (useParallel)
            {
                EnsureVisibilityResultScratchCapacity(snapshot.Count);
                Parallel.For(0, snapshot.Count, VisibleParallelOptions, i =>
                {
                    var obj = snapshot[i];
                    _visibilityResultScratch[i] =
                        obj != null && obj.Visible &&
                        (ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum))
                            ? (byte)1
                            : (byte)0;
                });

                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (_visibilityResultScratch[i] != 0)
                        _visibleObjects.Add(snapshot[i]);
                }
            }
            else
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var obj = snapshot[i];
                    if (obj == null || !obj.Visible)
                        continue;

                    if (ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum))
                        _visibleObjects.Add(obj);
                }
            }

            for (int i = 0; i < _visibleObjects.Count; i++)
            {
                var obj = _visibleObjects[i];
                if (obj != null)
                    _visibleObjectIndices[obj] = i;
            }

            _cullingStopwatch.Stop();
            _hasVisibilitySnapshot = true;
            LastCullVisibleCount = _visibleObjects.Count;
            LastCullRebuildMs = (float)_cullingStopwatch.Elapsed.TotalMilliseconds;
            LastCullWasRebuild = true;
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            FrameMetrics.CullWasRebuild = true;
        }

        private void RefreshDirtyVisibleObjects()
        {
            int dirtyCount = _positionDirtyObjects.Count;
            if (dirtyCount == 0)
                return;

            _cullingStopwatch.Restart();
            var camera = Camera.Instance;
            var camPos = camera.Position;
            var cam2 = new Vector2(camPos.X, camPos.Y);
            float maxViewDistance = camera.ViewFar + Constants.MAX_CAMERA_DISTANCE + 250f;
            float maxDistSq = maxViewDistance * maxViewDistance;
            var frustum = camera.Frustum;

            EnsureDirtyVisibilityScratchCapacity(dirtyCount);
            _positionDirtyObjects.CopyTo(_dirtyVisibilityScratch);
            _positionDirtyObjects.Clear();

            bool useParallel = Environment.ProcessorCount > 2 &&
                               dirtyCount >= ParallelDirtyRefreshThreshold;

            if (useParallel)
            {
                EnsureDirtyVisibilityResultScratchCapacity(dirtyCount);
                Parallel.For(0, dirtyCount, VisibleParallelOptions, i =>
                {
                    var obj = _dirtyVisibilityScratch[i];
                    _dirtyVisibilityResultScratch[i] =
                        obj != null && obj.Visible &&
                        (ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum))
                            ? (byte)1
                            : (byte)0;
                });

                for (int i = 0; i < dirtyCount; i++)
                {
                    var obj = _dirtyVisibilityScratch[i];
                    if (obj == null)
                        continue;

                    if (_dirtyVisibilityResultScratch[i] != 0)
                        AddVisibleObject(obj);
                    else
                        RemoveVisibleObject(obj);
                }
            }
            else
            {
                for (int i = 0; i < dirtyCount; i++)
                {
                    var obj = _dirtyVisibilityScratch[i];
                    if (obj == null || !obj.Visible)
                    {
                        if (obj != null)
                            RemoveVisibleObject(obj);
                        continue;
                    }

                    bool inView = ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum);
                    if (inView)
                        AddVisibleObject(obj);
                    else
                        RemoveVisibleObject(obj);
                }
            }

            Array.Clear(_dirtyVisibilityScratch, 0, dirtyCount);
            _cullingStopwatch.Stop();
            LastCullCandidateCount = dirtyCount;
            LastCullVisibleCount = _visibleObjects.Count;
            LastCullRebuildMs = (float)_cullingStopwatch.Elapsed.TotalMilliseconds;
            LastCullWasRebuild = false;
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            FrameMetrics.CullWasRebuild = false;
        }

        private void EnsureDirtyVisibilityScratchCapacity(int required)
        {
            if (_dirtyVisibilityScratch.Length >= required)
                return;

            int capacity = 256;
            while (capacity < required)
                capacity <<= 1;

            _dirtyVisibilityScratch = new WorldObject[capacity];
        }

        private void EnsureVisibilityResultScratchCapacity(int required)
        {
            if (_visibilityResultScratch.Length >= required)
                return;

            int capacity = 2048;
            while (capacity < required)
                capacity <<= 1;

            _visibilityResultScratch = new byte[capacity];
        }

        private void EnsureDirtyVisibilityResultScratchCapacity(int required)
        {
            if (_dirtyVisibilityResultScratch.Length >= required)
                return;

            int capacity = 1024;
            while (capacity < required)
                capacity <<= 1;

            _dirtyVisibilityResultScratch = new byte[capacity];
        }

        private void UpdateVisibleObjects(GameTime time)
        {
            var objects = _visibleObjects;
            if (objects.Count == 0)
                return;

            var camera = Camera.Instance;
            var camPos = camera.Position;
            float camX = camPos.X;
            float camY = camPos.Y;
            int frame = MuGame.FrameIndex;

            bool profileObjectUpdates = MuGame.Diagnostics?.Enabled == true;
            _isUpdatingVisibleObjects = true;
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    var obj = objects[i];
                    if (obj == null || !obj.Visible)
                        continue;

                    // Static map placements have no movement, animation, child update or CPU
                    // lighting work. Reject them before distance/quality calculations as well as
                    // before the virtual Update call.
                    if (obj is ModelObject staticMapModel &&
                        staticMapModel.CanSkipStaticMapWorldUpdate())
                    {
                        FrameMetrics.StaticMapUpdateSkips++;
                        continue;
                    }

                    int stride = 1;
                    if (!ShouldAlwaysUpdate(obj))
                    {
                        var position = obj.WorldPosition.Translation;
                        float dx = camX - position.X;
                        float dy = camY - position.Y;
                        float distanceSquared = dx * dx + dy * dy;
                        stride = Constants.ENABLE_ANIMATION_THROTTLING
                            ? ResolveUpdateStride(distanceSquared)
                            : 1;
                    }

                    bool lowQuality = stride > 1;
                    obj.SetLowQuality(lowQuality);
                    if (obj is ModelObject model)
                        model.SetAnimationUpdateStride(stride);

                    if (lowQuality)
                    {
                        FrameMetrics.LowQualityObjects++;
                        if (((frame + obj.UpdateOffset) % stride) != 0)
                            FrameMetrics.AnimationSkips++;
                        else
                            FrameMetrics.AnimationUpdates++;
                    }
                    else
                    {
                        FrameMetrics.AnimationUpdates++;
                    }

                    // Movement, network interpolation and gameplay state still update every
                    // frame. ModelObject throttles only its expensive bone-pose calculation.
                    if (!profileObjectUpdates)
                    {
                        obj.Update(time);
                    }
                    else
                    {
                        long objectStarted = Stopwatch.GetTimestamp();
                        obj.Update(time);
                        double objectMs = Stopwatch.GetElapsedTime(objectStarted).TotalMilliseconds;
                        if (objectMs > FrameMetrics.LongestObjectUpdateMs)
                        {
                            FrameMetrics.LongestObjectUpdateMs = objectMs;
                            FrameMetrics.LongestObjectUpdateType = obj.GetType().Name;
                            FrameMetrics.LongestObjectUpdateName = obj.DisplayName;
                            FrameMetrics.LongestObjectUpdateNetworkId = obj.NetworkId;
                        }
                    }
                }
            }
            finally
            {
                _isUpdatingVisibleObjects = false;
                CompactVisibleObjects();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldAlwaysUpdate(WorldObject obj)
        {
            if (obj == null)
                return false;

            if (obj.RenderPolicy.AlwaysUpdate)
                return true;

            return (obj.Interactive && obj is not MonsterObject)
                || obj is DroppedItemObject
                || (obj is MonsterObject monster && monster.IsOneShotPlaying);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveUpdateStride(float distSq)
        {
            if (distSq <= NearUpdateDistanceSq) return 1;
            if (distSq <= MidUpdateDistanceSq) return 2;
            if (distSq <= FarUpdateDistanceSq) return 4;
            return 6;
        }


        private bool HasSignificantCameraCullChange(Camera camera)
        {
            if (!_hasVisibilitySnapshot || camera == null)
                return true;

            float moveThresholdSq = CameraCullMoveThreshold * CameraCullMoveThreshold;
            if (Vector3.DistanceSquared(camera.Position, _lastCulledCameraPosition) >= moveThresholdSq)
                return true;

            Vector3 direction = camera.Target - camera.Position;
            if (direction.LengthSquared() > 1e-8f)
                direction.Normalize();
            else
                direction = Vector3.UnitY;

            if (Vector3.Dot(direction, _lastCulledCameraDirection) < CameraCullDirectionDotThreshold)
                return true;

            return MathF.Abs(camera.ViewFar - _lastCulledViewFar) > 0.01f ||
                   MathF.Abs(camera.FOV - _lastCulledFov) > 0.001f ||
                   MathF.Abs(camera.AspectRatio - _lastCulledAspectRatio) > 0.0001f;
        }

        private void CaptureCulledCameraState(Camera camera, ulong cameraVersion)
        {
            _lastCulledCameraVersion = cameraVersion;
            _lastCulledCameraPosition = camera.Position;

            Vector3 direction = camera.Target - camera.Position;
            if (direction.LengthSquared() > 1e-8f)
                direction.Normalize();
            else
                direction = Vector3.UnitY;

            _lastCulledCameraDirection = direction;
            _lastCulledViewFar = camera.ViewFar;
            _lastCulledFov = camera.FOV;
            _lastCulledAspectRatio = camera.AspectRatio;
        }

        internal void CollectObjectsNear(Vector3 center, float radius, List<WorldObject> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            FlushSpatialUpdates();
            destination.Clear();
            float safeRadius = MathF.Max(0f, radius);

            if (!Constants.ENABLE_CROWD_SPATIAL_CULLING)
            {
                var snapshot = Objects.GetSnapshot();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var obj = snapshot[i];
                    if (obj != null)
                        destination.Add(obj);
                }
                return;
            }

            if (_spatialObjectSectors.Count != Objects.Count)
                RebuildSpatialGridFromSnapshot();

            int centerTileX = (int)MathF.Floor(center.X / Constants.TERRAIN_SCALE);
            int centerTileY = (int)MathF.Floor(center.Y / Constants.TERRAIN_SCALE);
            int centerSectorX = Math.Clamp(centerTileX / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            int centerSectorY = Math.Clamp(centerTileY / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            int sectorRadius = (int)MathF.Ceiling(safeRadius / (Constants.TERRAIN_SCALE * SpatialSectorTileSize)) + 1;

            int minX = Math.Max(0, centerSectorX - sectorRadius);
            int maxX = Math.Min(SpatialSectorsPerAxis - 1, centerSectorX + sectorRadius);
            int minY = Math.Max(0, centerSectorY - sectorRadius);
            int maxY = Math.Min(SpatialSectorsPerAxis - 1, centerSectorY + sectorRadius);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var bucket = _spatialSectors[x, y];
                    for (int i = 0; i < bucket.Count; i++)
                        destination.Add(bucket[i]);
                }
            }

            for (int i = 0; i < _spatialOffGridObjects.Count; i++)
                destination.Add(_spatialOffGridObjects[i]);
        }


        // --- Map Tile Initialization ---

        protected virtual void CreateMapTileObjects()
        {
            var defaultType = typeof(MapTileObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = defaultType;
        }

        // --- Disposal ---

        public override void Dispose()
        {
            _cutoutMapBatch.Dispose();
            var sw = Stopwatch.StartNew();

            // Dispose can occur after objects were queued for a later instanced flush. Clear
            // those per-frame queues before releasing world objects to prevent one-frame ghosts
            // or persistent stale batches after a teleport.
            ModelObject.ResetWorldScopedInstancingState();

            // Dispose and remove all objects except the local player
            foreach (var obj in Objects.ToArray())
            {
                if (this is WalkableWorldControl wc &&
                    obj is PlayerObject player &&
                    wc.Walker == player)
                    continue;

                RemoveObject(obj);
                obj.Dispose();
            }

            Objects.Clear();
            WalkerObjectsById.Clear();
            _walkers.Clear();
            _players.Clear();
            _monsters.Clear();
            _droppedItems.Clear();
            _dedicatedStaticMapObjects.Clear();
            _queuedDedicatedStaticMapObjects.Clear();
            _solidBehind.Clear();
            _transparentObjects.Clear();
            _objectsToInitialize.Clear();
            _queuedForInitialization.Clear();
            _visibleObjectIndices.Clear();
            _visibleObjectsNeedCompaction = false;
            _isUpdatingVisibleObjects = false;
            _positionDirtyObjects.Clear();
            _spatialDirtyObjects.Clear();
            _spatialObjectSectors.Clear();
            _spatialOffGridObjects.Clear();
            _spatialCandidates.Clear();
            _hasVisibilitySnapshot = false;
            foreach (var bucket in _spatialSectors)
                bucket.Clear();

            sw.Stop();
            var elapsedObjects = sw.ElapsedMilliseconds;
            sw.Restart();

            base.Dispose();

            sw.Stop();
            var elapsedBase = sw.ElapsedMilliseconds;
            _logger?.LogDebug($"Dispose WorldControl {WorldIndex} - Objects: {elapsedObjects}ms, Base: {elapsedBase}ms");
        }

        public void OnWorldObjectStatusChanged(WorldObject worldObject)
        {
            // WorldObject propagates status notifications through its World reference even
            // before publication and for every child model. The world owns lifecycle only for
            // roots present in Objects; manually preloaded selection actors and equipment must
            // not be requeued here.
            if (!IsRegisteredRootObject(worldObject))
                return;

            if (worldObject.Status == GameControlStatus.NonInitialized)
            {
                EnqueueObjectInitialization(worldObject);
                return;
            }

            if (worldObject.Status == GameControlStatus.Ready)
            {
                _positionDirtyObjects.Add(worldObject);
                UpdateSpatialRegistration(worldObject);
                return;
            }

            if (worldObject.Status == GameControlStatus.Disposed || worldObject.Status == GameControlStatus.Error)
            {
                RemoveVisibleObject(worldObject);
                _positionDirtyObjects.Remove(worldObject);
                UnregisterSpatialObject(worldObject);
            }
        }
    }
}
