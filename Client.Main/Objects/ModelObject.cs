using Client.Data.BMD;
using Client.Data.Texture;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Controls.UI.Game.Inventory;

namespace Client.Main.Objects
{
    /// <summary>
    /// Base class for 3D model objects with skeletal animation support.
    /// Split into partial classes for maintainability:
    /// - ModelObject.cs (this file): Core fields, properties, lifecycle
    /// - ModelObject.Rendering.cs: Draw methods, shader selection, mesh grouping
    /// - ModelObject.Animation.cs: Animation, bone matrix generation, blending
    /// - ModelObject.Buffers.cs: Dynamic buffer management, caching
    /// - ModelObject.Lighting.cs: Dynamic lighting effect preparation
    /// - ModelObject.Shadow.cs: Shadow rendering and matrix calculation
    /// </summary>
    public abstract partial class ModelObject : WorldObject
    {
        #region Static Fields and Caches

        private static readonly Dictionary<string, BlendState> _blendStateCache = new Dictionary<string, BlendState>();

        // Cached common Vector3 instances to avoid allocations
        private static readonly Vector3 _ambientLightVector = new Vector3(0.8f, 0.8f, 0.8f);
        private static readonly Vector3 _redHighlight = new Vector3(1, 0, 0);
        private static readonly Vector3 _greenHighlight = new Vector3(0, 1, 0);
        private static readonly Vector3 _maxValueVector = new Vector3(float.MaxValue);
        private static readonly Vector3 _minValueVector = new Vector3(float.MinValue);
        private static readonly Vector3 _sunColor = new Vector3(1f, 0.95f, 0.85f);

        // Cache common graphics states to avoid repeated property access
        private static readonly RasterizerState _cullClockwise = RasterizerState.CullClockwise;
        private static readonly RasterizerState _cullNone = RasterizerState.CullNone;

        private static int _gpuSkinnedMeshesDrawnThisFrame = 0;
        private static int _modelFallbackDrawCallsThisFrame = 0;
        private static int _sharedAnimationPaletteHitsThisFrame = 0;
        private static int _sharedAnimationPaletteMissesThisFrame = 0;
        private static int _gpuSkinnedBatchDrawCallsThisFrame = 0;
        private static int _gpuSkinnedBatchedMeshesThisFrame = 0;
        private static bool _gpuSkinnedMeshBatchingFailed;

        // Track per-pass preparation to avoid re-uploading shared effect parameters per mesh
        private static int _drawModelInvocationCounter = 0;
        public static int LastFrameGpuSkinnedMeshesDrawn { get; private set; }
        public static int LastFrameModelDrawCalls { get; private set; }
        public static int LastFrameModelFallbackDrawCalls { get; private set; }
        public static int LastFrameModelInstancedDrawCalls { get; private set; }
        public static int LastFrameSharedAnimationPaletteHits { get; private set; }
        public static int LastFrameSharedAnimationPaletteMisses { get; private set; }
        public static int LastFrameGpuSkinnedBatchDrawCalls { get; private set; }
        public static int LastFrameGpuSkinnedBatchedMeshes { get; private set; }
        public static bool IsGpuSkinningBackendSupported => SupportsGpuDynamicSkinning;
        public static bool IsGpuSkinnedMeshBatchingRuntimeDisabled => _gpuSkinnedMeshBatchingFailed;

        public static void BeginFrameGpuSkinningMetrics()
        {
            LastFrameGpuSkinnedMeshesDrawn = _gpuSkinnedMeshesDrawnThisFrame;
            LastFrameModelFallbackDrawCalls = _modelFallbackDrawCallsThisFrame;
            LastFrameSharedAnimationPaletteHits = _sharedAnimationPaletteHitsThisFrame;
            LastFrameSharedAnimationPaletteMisses = _sharedAnimationPaletteMissesThisFrame;
            LastFrameGpuSkinnedBatchDrawCalls = _gpuSkinnedBatchDrawCallsThisFrame;
            LastFrameGpuSkinnedBatchedMeshes = _gpuSkinnedBatchedMeshesThisFrame;
            _gpuSkinnedMeshesDrawnThisFrame = 0;
            _modelFallbackDrawCallsThisFrame = 0;
            _sharedAnimationPaletteHitsThisFrame = 0;
            _sharedAnimationPaletteMissesThisFrame = 0;
            _gpuSkinnedBatchDrawCallsThisFrame = 0;
            _gpuSkinnedBatchedMeshesThisFrame = 0;
            PruneSharedAnimationPaletteCache(MuGame.FrameIndex);
            BeginFrameStaticMapInstancingMetrics();
        }

        private static void RegisterGpuSkinnedMeshDraw(int meshInstanceCount = 1)
        {
            if (meshInstanceCount > 0)
                _gpuSkinnedMeshesDrawnThisFrame += meshInstanceCount;
        }

        private static void RegisterGpuSkinnedBatchDraw(int meshCount)
        {
            _gpuSkinnedBatchDrawCallsThisFrame++;
            if (meshCount > 0)
                _gpuSkinnedBatchedMeshesThisFrame += meshCount;
        }

        private static void RegisterModelFallbackDrawCall()
        {
            _modelFallbackDrawCallsThisFrame++;
        }

        private static void RegisterSharedAnimationPaletteHit()
        {
            _sharedAnimationPaletteHitsThisFrame++;
        }

        private static void RegisterSharedAnimationPaletteMiss()
        {
            _sharedAnimationPaletteMissesThisFrame++;
        }

        public static ILoggerFactory AppLoggerFactory { get; private set; }

        public static void SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            AppLoggerFactory = loggerFactory;
        }

        #endregion

        #region Instance Fields - Graphics Resources

        // Per-mesh consolidated state replaces 15 parallel arrays.
        private MeshRuntimeState[] _meshes;

        // Cached hint for world-level batching/sorting (avoids scanning mesh textures during Sort comparisons)
        private Texture2D _sortTextureHint;
        private bool _sortTextureHintDirty = true;

        private int[] _staticMapInstancedMeshFrameTags;
        private int[] _blendMeshIndicesScratch;

        #endregion

        #region Instance Fields - Animation State

        private int _blendFromAction = -1;
        private double _blendFromTime = 0.0;
        private Matrix[] _blendFromBones = null;
        private bool _isBlending = false;
        private float _blendElapsed = 0f;
        private float _blendDuration = 0.25f;

        protected int _priorActionIndex = 0;
        protected double _animTime = 0.0;
        public double GetAnimationTime() => _animTime;
        public double GetLoopedAnimationTime()
        {
            if (Model == null || Model.Actions == null || CurrentAction >= Model.Actions.Length)
                return _animTime;
            var action = Model.Actions[CurrentAction];
            if (action == null || action.NumAnimationKeys <= 0)
                return _animTime;
            return _animTime % action.NumAnimationKeys;
        }

        private LocalAnimationState _lastAnimationState;
        private bool _animationStateValid = false;

        #endregion

        #region Instance Fields - Buffer State

        private bool _renderShadow = false;
        private MeshDirtyFlags _invalidatedBufferFlags = MeshDirtyFlags.All;
        private float _blendMeshLight = 1f;
        private bool _contentLoaded = false;
        private bool _boundingComputed = false;
        private bool _dynamicBuffersFrozen = false;

        protected const MeshDirtyFlags BufferFlagAnimation = MeshDirtyFlags.Animation;
        protected const MeshDirtyFlags BufferFlagLighting = MeshDirtyFlags.Lighting;
        protected const MeshDirtyFlags BufferFlagTransform = MeshDirtyFlags.Transform;
        protected const MeshDirtyFlags BufferFlagMaterial = MeshDirtyFlags.Material;
        protected const MeshDirtyFlags BufferFlagTexture = MeshDirtyFlags.Texture;
        protected const MeshDirtyFlags BufferFlagAll = MeshDirtyFlags.All;

        // Bounding box update optimization
        private int _boundingFrameCounter = BoundingUpdateInterval;
        private const int BoundingUpdateInterval = 4;

        // Enhanced animation caching system
        private Matrix[] _cachedBoneMatrix = null;
        private Matrix[] _lastCachedBoneSource = null;
        private uint _lastCachedBonePoseVersion = uint.MaxValue;
        private int _lastCachedAction = -1;
        private float _lastCachedAnimTime = -1;
        private bool _boneMatrixCacheValid = false;

        private const int MaxGpuSkinBones = 256;
        private static Effect _gpuSkinCapabilityDynamicEffect;
        private static Effect _gpuSkinCapabilityItemEffect;
        private static Effect _gpuSkinCapabilityMonsterEffect;
        private static bool _gpuSkinCapability;

        // Detect support from compiled effects instead of relying on project symbols.
        // GPU geometry is shared by all material paths; every path still verifies its
        // own skinned technique before it is selected for a mesh.
        private static bool SupportsGpuDynamicSkinning
        {
            get
            {
                var graphics = GraphicsManager.Instance;
                var dynamicEffect = graphics?.DynamicLightingEffect;
                var itemEffect = graphics?.ItemMaterialEffect;
                var monsterEffect = graphics?.MonsterMaterialEffect;

                if (ReferenceEquals(dynamicEffect, _gpuSkinCapabilityDynamicEffect) &&
                    ReferenceEquals(itemEffect, _gpuSkinCapabilityItemEffect) &&
                    ReferenceEquals(monsterEffect, _gpuSkinCapabilityMonsterEffect))
                {
                    return _gpuSkinCapability;
                }

                _gpuSkinCapabilityDynamicEffect = dynamicEffect;
                _gpuSkinCapabilityItemEffect = itemEffect;
                _gpuSkinCapabilityMonsterEffect = monsterEffect;
                _gpuSkinCapability =
                    TryGetTechnique(dynamicEffect, "DynamicLighting_Skinned") != null ||
                    TryGetTechnique(itemEffect, "BasicColorDrawing_Skinned") != null ||
                    TryGetTechnique(monsterEffect, "MonsterMaterialDrawing_Skinned") != null;

                return _gpuSkinCapability;
            }
        }


        #endregion

        #region Instance Fields - Lighting State

        private Vector3 _lastFrameLight = Vector3.Zero;
        private double _lastLightUpdateTime = 0;

        // Quantized lighting sample (reduces CPU work without visible change)
        private const float _LIGHT_SAMPLE_GRID = 8f; // world units per cell
        private const double FrameTimeMs = 1000.0 / 60.0; // ~16.67ms at 60 FPS
        private Vector2 _lastLightSampleCell = new Vector2(float.MaxValue);
        private Vector3 _lastSampledLight = Vector3.Zero;

        private static readonly DynamicLightGpuUploader _dynamicLightUploader = new(32);

        private int _drawModelInvocationId = 0;
        private int _dynamicLightingPreparedInvocationId = -1;
        private bool _dynamicLightingPreparedWithGpuSkinning = false;
        private int _dynamicLightingPreparedGpuBoneCount = 0;

        #endregion

        #region Instance Fields - Cached State

        private float _animationStepAccumulatorSeconds = 0f;
        private double _lastAnimationTotalSeconds; // TotalGameTime at last Animation() call — used to compute true elapsed when throttled
        private uint _animationPoseVersion = 0;
        private uint _lastLinkedParentPoseVersion = uint.MaxValue;
        private ModelObject _lastLinkedParentModel = null;
        private double _lastFrameTimeMs = 0; // To track timing in methods without GameTime
        private float _drawShaderTimeSeconds = 0f;
        private int _animationSampleActionIndex = -1;
        private int _animationSampleFrame0 = 0;
        private int _animationSampleFrame1 = 0;
        private int _animationSampleInterpolationBucket = 0;
        private bool _animationSampleValid = false;


        #endregion

        #region Properties

        public float ShadowOpacity { get; set; } = 1f;
        public Color Color { get; set; } = Color.White;
        public Vector2 TextureCoordinateOffset { get; protected set; } = Vector2.Zero;
        public int TextureCoordinateOffsetMeshIndex { get; protected set; } = -1;
        public ItemDefinition ItemDefinition { get; set; }
        protected Matrix[] BoneTransform { get; set; }
        // Shared animation palettes are immutable render snapshots. They are kept
        // separate from the writable per-object BoneTransform array so a monster can
        // enter a shared pose without copying every matrix and can detach safely when
        // an action transition starts.
        private Matrix[] _sharedAnimationRenderBones;
        public Matrix[] GetBoneTransforms() => GetEffectiveBoneTransforms();
        public int CurrentAction { get; set; }
        public int CurrentFrame { get; private set; }
        public int ParentBoneLink { get; set; } = -1;

        private BMD _model;
        // Meshes from this index on were appended from linked models (BMDLoader.GetModelWithAttachedMeshes): they
        // are drawn like the model's own meshes but without its item material and item overlays.
        private int _firstAttachedMesh = int.MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsAttachedMesh(int mesh) => mesh >= _firstAttachedMesh;

        // The model has more than one action or an action with more than one key.
        private bool _modelHasOwnAnimation;

        private static bool HasOwnAnimation(BMD model)
        {
            var actions = model?.Actions;
            if (actions == null || actions.Length == 0)
                return false;
            if (actions.Length > 1)
                return true;
            var action = actions[0];
            return action != null && (action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys) > 1;
        }

        /// <summary>The model has meshes appended from linked models.</summary>
        internal bool HasAttachedMeshes => _firstAttachedMesh != int.MaxValue;

        public BMD Model
        {
            get => _model;
            set
            {
                if (_model != value)
                {
                    _model = value;
                    _firstAttachedMesh = BMDLoader.Instance.GetFirstAttachedMesh(value);
                    _modelHasOwnAnimation = HasOwnAnimation(value);
                    InvalidateMeshRenderPlan();
                    // If the model changes after the object has already been loaded,
                    // we need to re-run the content loading logic to update buffers, textures, etc.
                    if (Status is GameControlStatus.Ready or GameControlStatus.Error)
                    {
                        _ = LoadContent();
                    }
                }
            }
        }

        public Matrix ParentBodyOrigin
        {
            get
            {
                if (ParentBoneLink >= 0 && Parent is ModelObject modelObject)
                {
                    Matrix[] parentBones = modelObject.GetEffectiveBoneTransforms();
                    if (parentBones != null && ParentBoneLink < parentBones.Length)
                        return parentBones[ParentBoneLink];
                }
                return Matrix.Identity;
            }
        }

        public float BodyHeight { get; private set; }

        private int _hiddenMesh = -1;
        private int _blendMesh = -1;

        public int HiddenMesh
        {
            get => _hiddenMesh;
            set
            {
                if (_hiddenMesh == value)
                    return;

                _hiddenMesh = value;
                InvalidateMeshRenderPlan();
            }
        }

        public int BlendMesh
        {
            get => _blendMesh;
            set
            {
                if (_blendMesh == value)
                    return;

                _blendMesh = value;
                InvalidateMeshRenderPlan();
            }
        }

        private BlendState _blendMeshState = Blendings.OneOneAdditive;
        public BlendState BlendMeshState
        {
            get => _blendMeshState;
            set
            {
                if (ReferenceEquals(_blendMeshState, value))
                    return;

                _blendMeshState = value;
                InvalidateMeshRenderPlan();
            }
        }

        public float BlendMeshLight
        {
            get => _blendMeshLight;
            set
            {
                if (MathF.Abs(_blendMeshLight - value) < 0.0001f)
                    return;

                _blendMeshLight = value;
                // BlendMeshLight is baked into CPU vertex colors. Material-only invalidation
                // left the cached light untouched, so animated blend brightness did not update.
                InvalidateBuffers(MeshDirtyFlags.Lighting | MeshDirtyFlags.Material);
            }
        }

        public bool RenderShadow
        {
            get => _renderShadow;
            set
            {
                if (_renderShadow == value)
                    return;

                _renderShadow = value;
                OnRenderShadowChanged();
            }
        }
        public float AnimationSpeed { get; set; } = 4f;
        public bool ContinuousAnimation { get; set; }

        /// <summary>
        /// Indicates if this object can be rendered to a static cache surface.
        /// Objects with skeletal animation, continuous animation, or per-frame effects should return false.
        /// Override in subclasses that have dynamic visuals (flickering, fading, etc.).
        /// </summary>
        public virtual bool IsStaticForCaching => !ContinuousAnimation
            && (Model?.Bones == null || Model.Bones.Length == 0)
            && !RequiresPerFrameAnimation
            && !UsesMutableMeshData;

        /// <summary>
        /// When true, the animation will stop at the last frame instead of looping.
        /// Used for one-shot animations like skills or deaths.
        /// </summary>
        protected bool HoldOnLastFrame { get; set; }

        protected ILogger _logger;

        private int _itemLevel;
        public int ItemLevel
        {
            get => _itemLevel;
            set
            {
                if (_itemLevel == value)
                    return;

                _itemLevel = value;
                InvalidateMeshRenderPlan();
            }
        }
        private int _materialItemGroup = -1;
        /// <summary>Item group of the rendered item (0 = swords ... 15 = scrolls), -1 when unknown.</summary>
        public int MaterialItemGroup
        {
            get => _materialItemGroup;
            set
            {
                if (_materialItemGroup == value)
                    return;

                _materialItemGroup = value;
                InvalidateMeshRenderPlan();
            }
        }

        private int _materialItemIndex = -1;
        public int MaterialItemIndex
        {
            get => _materialItemIndex;
            set
            {
                if (_materialItemIndex == value)
                    return;

                _materialItemIndex = value;
                InvalidateMeshRenderPlan();
            }
        }

        /// <summary>Upgrade level used for the item glow after SourceMain's per-item remapping.</summary>
        public int EffectiveItemLevel => MaterialItemGroup >= 0
            ? ItemVisualRules.GetRenderLevel(MaterialItemGroup, MaterialItemIndex, ItemLevel)
            : ItemLevel;

        /// <summary>Excellent pulse is suppressed for wings and capes.</summary>
        public bool HasExcellentGlow => IsExcellentItem &&
            (MaterialItemGroup < 0 || ItemVisualRules.AllowsExcellentGlow(MaterialItemGroup, MaterialItemIndex));

        /// <summary>True when the item needs the item material shader (+3 and higher, excellent or ancient).</summary>
        public bool HasItemMaterialEffect => EffectiveItemLevel >= 3 || HasExcellentGlow || IsAncientItem ||
            ItemVisualRules.GetLowLevelChromeMode(MaterialItemGroup, MaterialItemIndex) != 0;

        private bool _isExcellentItem;
        public bool IsExcellentItem
        {
            get => _isExcellentItem;
            set
            {
                if (_isExcellentItem == value)
                    return;

                _isExcellentItem = value;
                InvalidateMeshRenderPlan();
            }
        }

        private bool _isAncientItem;
        public bool IsAncientItem
        {
            get => _isAncientItem;
            set
            {
                if (_isAncientItem == value)
                    return;

                _isAncientItem = value;
                InvalidateMeshRenderPlan();
            }
        }

        // Monster/NPC glow properties
        public Vector3 GlowColor { get; set; } = new Vector3(1.0f, 0.8f, 0.0f); // Default gold
        public float GlowIntensity { get; set; } = 0.0f;
        public bool EnableCustomShader { get; set; } = false;
        public bool SimpleColorMode { get; set; } = false;
        public bool UseSunLight { get; set; } = true;

        public int AnimationUpdateStride { get; private set; } = 1;
        protected virtual bool RequiresPerFrameAnimation => false;

        /// <summary>
        /// Pose computed every frame instead of at <see cref="Constants.ANIMATION_UPDATE_FPS"/>: the player's own
        /// character (and what it carries) is always in view and in the middle of the screen, where a pose held for
        /// several frames reads as stutter on high refresh rate screens.
        /// </summary>
        protected internal virtual bool AnimateEveryFrame => Parent is ModelObject parent && parent.AnimateEveryFrame;
        public bool RequiresPerFrameWorldUpdate => RequiresPerFrameAnimation || UsesMutableMeshData;
        protected virtual bool AllowAnimationUpdates => true;
        protected virtual bool AllowLightingUpdates => true;
        protected virtual bool AllowDynamicLightingShader => true;
        /// <summary>
        /// Allows legacy models with unsupported or unusual skin data to opt out of the
        /// GPU-skinned path and use the same CPU geometry path as the inventory preview.
        /// </summary>
        protected virtual bool AllowGpuSkinning => true;
        /// <summary>
        /// Forces all meshes to render two-sided. Some legacy effect/helper BMDs were authored
        /// for an OpenGL render path with culling disabled and otherwise disappear completely
        /// after the coordinate-system conversion used by the world renderer.
        /// </summary>
        protected virtual bool ForceTwoSidedMeshes => false;
        protected virtual bool AllowMapObjectInstancing => true;
        protected virtual bool UsesMutableMeshData => false;
        protected virtual bool PreserveBlendMeshesInLowQuality => false;
        protected virtual bool FreezeDynamicBuffersAfterFirstBuild => false;
        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(
            alwaysUpdate: RequiresPerFrameWorldUpdate,
            preserveBlendMeshesInLowQuality: PreserveBlendMeshesInLowQuality);
        internal uint AnimationPoseVersion => _animationPoseVersion;

        #endregion

        #region Constructor

        public ModelObject()
        {
            _logger = AppLoggerFactory?.CreateLogger(GetType());
            // MatrixChanged subscription removed - UpdateWorldPosition was a no-op
        }

        #endregion

        #region Lifecycle Methods

        public override async Task LoadContent()
        {
            await base.LoadContent();

            ReleaseDynamicBuffers();

            if (Model == null)
            {
                // This is a valid state, e.g., when an item is unequipped.
                // Clear out graphics resources to ensure it becomes invisible.
                _meshes = null;
                _logger?.LogDebug("Model is null for {ObjectName}. Clearing buffers. This is likely an unequip action.", ObjectName);
                // Set to Ready because it's a valid, though non-renderable, state.
                Status = GameControlStatus.Ready;
                return;
            }

            int meshCount = Model.Meshes.Length;
            _meshes = new MeshRuntimeState[meshCount];
            for (int i = 0; i < meshCount; i++)
                _meshes[i] = new MeshRuntimeState { BufferCache = new MeshBufferCache { IsValid = false } };

            // PERFORMANCE: Preload all textures during LoadContent to avoid SetData during gameplay
            var texturePreloadTasks = new List<Task>();

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                var mesh = Model.Meshes[meshIndex];
                string texturePath = BMDLoader.Instance.GetTexturePath(Model, mesh.TexturePath);

                _meshes[meshIndex].TexturePath = texturePath;

                if (!string.IsNullOrEmpty(texturePath))
                    texturePreloadTasks.Add(TextureLoader.Instance.Prepare(texturePath));
            }

            if (texturePreloadTasks.Count > 0)
                await Task.WhenAll(texturePreloadTasks);

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                string texturePath = _meshes[meshIndex].TexturePath;
                _meshes[meshIndex].Script = TextureLoader.Instance.GetScript(texturePath);
                _meshes[meshIndex].Data = TextureLoader.Instance.Get(texturePath);
                _meshes[meshIndex].IsRgba = _meshes[meshIndex].Data?.Components == 4;
                _meshes[meshIndex].HiddenByScript = _meshes[meshIndex].Script?.HiddenMesh ?? false;
                _meshes[meshIndex].BlendByScript = _meshes[meshIndex].Script?.Bright ?? false;
            }

            InvalidateMeshRenderPlan();

            _blendMeshIndicesScratch = new int[meshCount];
            _staticMapInstancedMeshFrameTags = new int[meshCount];

            InvalidateBuffers(MeshDirtyFlags.All);
            _dynamicBuffersFrozen = false;
            _contentLoaded = true;

            if (Model?.Bones != null && Model.Bones.Length > 0)
            {
                _sharedAnimationRenderBones = null;
                BoneTransform = new Matrix[Model.Bones.Length];

                // Pre-allocate blend buffer to avoid allocations during action transitions
                if (_blendFromBones == null || _blendFromBones.Length != Model.Bones.Length)
                    _blendFromBones = new Matrix[Model.Bones.Length];

                if (Model.Actions != null && Model.Actions.Length > 0)
                {
                    GenerateBoneMatrix(0, 0, 0, 0);
                }
                else
                {
                    for (int i = 0; i < Model.Bones.Length; i++)
                    {
                        var bone = Model.Bones[i];
                        var localMatrix = Matrix.Identity;

                        BoneTransform[i] = (bone.Parent != -1 && bone.Parent < BoneTransform.Length)
                            ? localMatrix * BoneTransform[bone.Parent]
                            : localMatrix;
                    }
                }
            }

            UpdateBoundings();
        }

        // Item effects carried by this model (item link emitters, auras); run by ItemEffectSystem.
        private List<Client.Main.Objects.Effects.ItemEffect> _itemEffects;

        /// <summary>Attaches an item effect; it is updated and drawn with this model and disposed with it.</summary>
        internal void AddItemEffect(Client.Main.Objects.Effects.ItemEffect effect)
        {
            effect.Owner = this;
            // Copy on write: models attach effects while loading, possibly off the main thread.
            var effects = _itemEffects == null
                ? new List<Client.Main.Objects.Effects.ItemEffect>(1)
                : new List<Client.Main.Objects.Effects.ItemEffect>(_itemEffects);
            effects.Add(effect);
            _itemEffects = effects;
        }

        public override void Update(GameTime gameTime)
        {
            if (World == null || !_contentLoaded || !Visible) return;

            // A part on a parent bone whose model has no animation of its own (one action of one key, e.g. most
            // weapons) only follows the parent: LoadContent generated its bones once.
            bool followsParentOnly = ParentBoneLink >= 0 && !ContinuousAnimation && !_modelHasOwnAnimation;
            if (!LinkParentAnimation && AllowAnimationUpdates && !FreezeAnimationPose && !followsParentOnly)
            {
                Animation(gameTime);
            }

            if (LinkParentAnimation && Parent is ModelObject parent)
            {
                CurrentAction = parent.CurrentAction;
                _animTime = parent._animTime;
                _isBlending = parent._isBlending;
                _blendElapsed = parent._blendElapsed;

                if (!ReferenceEquals(_lastLinkedParentModel, parent))
                {
                    _lastLinkedParentModel = parent;
                    _lastLinkedParentPoseVersion = uint.MaxValue;
                }

                uint parentPoseVersion = parent.AnimationPoseVersion;
                if (_lastLinkedParentPoseVersion != parentPoseVersion)
                {
                    // Linked equipment uses the parent's bone palette, but the per-buffer
                    // cache is keyed by this object's pose version. Advance the child
                    // version whenever the effective parent pose changes, otherwise the
                    // cache can incorrectly keep the first uploaded animation frame.
                    unchecked { _animationPoseVersion++; }
                    InvalidateBuffers(MeshDirtyFlags.Animation);
                    _lastLinkedParentPoseVersion = parentPoseVersion;
                }
            }

            // Parts that inherit their pose only follow the parent; their animation stride is set by the walker
            // (PlayerObject.UpdateEquipmentAnimationStride).
            if (ParentBoneLink >= 0 || LinkParentAnimation)
                RecalculateWorldPosition();

            base.Update(gameTime);

            if (AllowLightingUpdates && !LinkParentAnimation && _contentLoaded)
            {
                // Like old code: Check if lighting has changed significantly (for static objects)
                bool hasDynamicLightingShader = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                               GraphicsManager.Instance.DynamicLightingEffect != null;

                // The shader path samples terrain/dynamic lighting during draw. Do not perform
                // an additional terrain lookup here because its result cannot invalidate a CPU buffer.
                if (!hasDynamicLightingShader)
                {
                    Vector3 currentLight;

                    if (LightEnabled && World?.Terrain != null)
                    {
                        var pos = WorldPosition.Translation;
                        var cell = new Vector2(
                            MathF.Floor(pos.X / _LIGHT_SAMPLE_GRID),
                            MathF.Floor(pos.Y / _LIGHT_SAMPLE_GRID));

                        if (_lastLightSampleCell != cell)
                        {
                            _lastSampledLight = EvaluateCombinedTerrainLight(pos.X, pos.Y);
                            _lastLightSampleCell = cell;
                        }

                        currentLight = _lastSampledLight + Light;
                    }
                    else
                    {
                        currentLight = Light;
                    }

                    // Reduce throttling for PlayerObjects to ensure proper rendering
                    bool isMainPlayer = this is PlayerObject p && p.IsMainWalker;
                    double lightUpdateInterval = isMainPlayer
                        ? FrameTimeMs
                        : (RequiresPerFrameAnimation ? 50 : 1000); // throttle static objects heavily
                    float lightThreshold = isMainPlayer ? 0.001f : 0.01f;   // More sensitive for main player

                    double currentTime = gameTime.TotalGameTime.TotalMilliseconds;
                    bool shouldCheckLight = currentTime - _lastLightUpdateTime > lightUpdateInterval;

                    if (shouldCheckLight)
                    {
                        bool lightChanged = Vector3.DistanceSquared(currentLight, _lastFrameLight) > lightThreshold;
                        if (lightChanged)
                        {
                            InvalidateBuffers(MeshDirtyFlags.Lighting);
                            _lastFrameLight = currentLight;
                        }
                        _lastLightUpdateTime = currentTime;
                    }
                }
            }

            // Track frame time for methods that need it
            _lastFrameTimeMs = gameTime.TotalGameTime.TotalMilliseconds;

            if (_itemEffects is { } itemEffects)
            {
                for (int i = 0; i < itemEffects.Count; i++)
                    Client.Main.Objects.Effects.ItemEffectSystem.Update(itemEffects[i], gameTime);
            }

            // Like old code: always call SetDynamicBuffers when content is loaded
            if (_contentLoaded)
            {
                if (!_dynamicBuffersFrozen)
                {
                    SetDynamicBuffers();
                    if (FreezeDynamicBuffersAfterFirstBuild && _invalidatedBufferFlags == 0)
                        _dynamicBuffersFrozen = true;
                }
            }
        }

        /// <summary>
        /// Replaces a mesh texture at runtime without modifying the shared BMD definition.
        /// The override remains active across buffer refreshes until explicitly replaced or cleared.
        /// </summary>
        protected bool SetMeshTextureOverride(int meshIndex, Texture2D texture)
        {
            if (_meshes == null || (uint)meshIndex >= (uint)_meshes.Length)
                return false;

            MeshRuntimeState meshState = _meshes[meshIndex];
            if (ReferenceEquals(meshState.TextureOverride, texture) &&
                ReferenceEquals(meshState.Texture, texture))
            {
                return false;
            }

            meshState.TextureOverride = texture;
            meshState.Texture = texture;
            InvalidateMeshRenderPlan();
            return true;
        }

        protected Texture2D GetMeshTexture(int meshIndex)
        {
            if (_meshes == null || (uint)meshIndex >= (uint)_meshes.Length)
                return null;

            return _meshes[meshIndex].Texture;
        }

        protected bool ClearMeshTextureOverride(int meshIndex)
        {
            if (_meshes == null || (uint)meshIndex >= (uint)_meshes.Length)
                return false;

            MeshRuntimeState meshState = _meshes[meshIndex];
            if (meshState.TextureOverride == null)
                return false;

            meshState.TextureOverride = null;
            meshState.Texture = null;
            InvalidateBuffers(MeshDirtyFlags.Texture);
            InvalidateMeshRenderPlan();
            return true;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (IsLoadInProgress)
                return;

            ReleaseTerrainShadowResources();
            _playerComposite?.Dispose();
            _playerComposite = null;
            _mergedShadow?.Dispose();
            _mergedShadow = null;
            if (_itemEffects != null)
            {
                foreach (var itemEffect in _itemEffects)
                    itemEffect.Dispose();
                _itemEffects = null;
            }
            Model = null;
            BoneTransform = null;
            _sharedAnimationRenderBones = null;
            _invalidatedBufferFlags = MeshDirtyFlags.None;
            ReleaseDynamicBuffers();

            _meshes = null;
            _staticMapInstancedMeshFrameTags = null;
            _staticMapInstancingMeshPlan = Array.Empty<StaticMapInstancingMeshPlan>();
            _staticMapInstancingMeshPlanCount = 0;
            _staticMapInstancingOpaqueMeshCount = 0;
            _builtStaticMapInstancingPlanVersion = 0;
            _staticMapInstancingPlanRetryFrame = 0;
            _staticMapInstancingPlanValidationFrame = 0;
            _blendMeshIndicesScratch = null;
            _contentLoaded = false;
            _boundingComputed = false;

            _cachedBoneMatrix = null;
            _lastCachedBoneSource = null;
            _lastCachedBonePoseVersion = uint.MaxValue;
            _boneMatrixCacheValid = false;
            _animationStateValid = false;
            _animationStepAccumulatorSeconds = 0f;
            _animationPoseVersion = 0;
            _lastLinkedParentPoseVersion = uint.MaxValue;
            _lastLinkedParentModel = null;

            ReleaseFastMeshBatchBuffers();
            ClearMeshRenderPlans();
            _meshGroupPool.Clear();
        }

        #endregion

        #region Helper Methods

        private Vector3 EvaluateCombinedTerrainLight(float x, float y)
        {
            var terrain = World?.Terrain;
            if (!LightEnabled || terrain == null)
                return Vector3.Zero;

            Vector3 result = terrain.EvaluateTerrainLight(x, y);
            if (Constants.ENABLE_DYNAMIC_LIGHTS)
            {
                // Dynamic-light CPU values use the legacy 0..255 scale, while
                // baked terrain light is normalized to 0..1. Normalize before
                // combining them for vertex-color fallback paths.
                result += terrain.EvaluateDynamicLight(new Vector2(x, y)) / 255f;
            }

            return Vector3.Clamp(result, Vector3.Zero, Vector3.One);
        }

        private bool HasWalkerAncestor()
        {
            // Disable sun-lighting for objects that belong to player/NPC/monster hierarchies
            ModelObject current = this;
            while (current != null)
            {
                if (current is WalkerObject)
                    return true;
                current = current.Parent as ModelObject;
            }
            return false;
        }

        private void SetDrawShaderTimeSeconds(float timeSeconds)
        {
            if (!float.IsNaN(timeSeconds) && !float.IsInfinity(timeSeconds) && timeSeconds >= 0f)
            {
                _drawShaderTimeSeconds = timeSeconds;
            }
        }

        private float GetShaderTimeSeconds()
        {
            return _drawShaderTimeSeconds;
        }

        public void SetAnimationUpdateStride(int stride)
        {
            int newStride = Math.Max(1, stride);
            if (AnimationUpdateStride == newStride)
                return;

            AnimationUpdateStride = newStride;
        }

        private void OnRenderShadowChanged()
        {
            bool modularActorRoot = this is PlayerObject || this is NPCObject;
            foreach (var obj in Children)
            {
                if (obj is ModelObject modelObj &&
                    (modularActorRoot || modelObj.LinkParentAnimation || modelObj.ParentBoneLink >= 0))
                {
                    modelObj.RenderShadow = RenderShadow;
                }
            }
        }

        private int ResolveBoundingUpdateInterval()
        {
            if (IsMouseHover)
                return BoundingUpdateInterval;

            if (this is WalkerObject walker && (walker.IsMainWalker || walker.IsOneShotPlaying))
                return BoundingUpdateInterval;

            var camera = Camera.Instance;
            if (camera == null)
                return BoundingUpdateInterval;

            Vector3 position = WorldPosition.Translation;
            Vector3 cameraPosition = camera.Position;
            float dx = position.X - cameraPosition.X;
            float dy = position.Y - cameraPosition.Y;
            float distanceSquared = dx * dx + dy * dy;

            const float mediumDistanceSquared = 900f * 900f;
            const float farDistanceSquared = 1500f * 1500f;
            if (distanceSquared > farDistanceSquared)
                return 30;
            if (distanceSquared > mediumDistanceSquared)
                return 12;

            return BoundingUpdateInterval;
        }

        private void UpdateBoundings()
        {
            if (!RequiresPerFrameAnimation && _contentLoaded && _boundingComputed)
                return;

            // Animated local bounds change much less than the object transform itself.
            // Keep nearby/interactive actors accurate, but avoid sampling every animated
            // mesh at the same frequency when it occupies only a few pixels on screen.
            int boundingUpdateInterval = ResolveBoundingUpdateInterval();
            if (_boundingComputed && _boundingFrameCounter++ < boundingUpdateInterval)
                return;

            _boundingFrameCounter = 0;

            Matrix[] activeBones = GetEffectiveBoneTransforms();
            if (Model?.Meshes == null || Model.Meshes.Length == 0 || activeBones == null) return;

            // Use faster min/max calculation with cached vectors
            Vector3 min = _maxValueVector;
            Vector3 max = _minValueVector;

            bool hasValidVertices = false;
            var meshes = Model.Meshes;
            var bones = activeBones;
            int boneCount = bones.Length;

            // Optimized: Only sample every 4th vertex for performance while maintaining accuracy
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                var mesh = meshes[meshIndex];
                var vertices = mesh.Vertices;
                if (vertices == null) continue;

                int step = Math.Max(1, vertices.Length / 32); // Sample max 32 vertices per mesh
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex += step)
                {
                    var vertex = vertices[vertexIndex];
                    int boneIndex = vertex.Node;

                    if (boneIndex < 0 || boneIndex >= boneCount) continue;

                    Vector3 transformedPosition = Vector3.Transform(vertex.Position, bones[boneIndex]);

                    // Optimized min/max calculation - avoid method calls
                    if (transformedPosition.X < min.X) min.X = transformedPosition.X;
                    if (transformedPosition.Y < min.Y) min.Y = transformedPosition.Y;
                    if (transformedPosition.Z < min.Z) min.Z = transformedPosition.Z;

                    if (transformedPosition.X > max.X) max.X = transformedPosition.X;
                    if (transformedPosition.Y > max.Y) max.Y = transformedPosition.Y;
                    if (transformedPosition.Z > max.Z) max.Z = transformedPosition.Z;

                    hasValidVertices = true;
                }
            }

            if (hasValidVertices)
            {
                BoundingBoxLocal = new BoundingBox(min, max);
                _boundingComputed = true;
            }
        }

        protected override void RecalculateWorldPosition()
        {
            Matrix localMatrix = Matrix.CreateScale(Scale) *
                                 Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(Angle)) *
                                 Matrix.CreateTranslation(Position);

            Matrix newWorldPosition;
            if (Parent != null)
            {
                newWorldPosition = localMatrix * ParentBodyOrigin * Parent.WorldPosition;
            }
            else
            {
                newWorldPosition = localMatrix;
            }

            if (WorldPosition != newWorldPosition)
            {
                WorldPosition = newWorldPosition;
                InvalidateBuffers(MeshDirtyFlags.Transform);
            }
        }

        #endregion
    }
}
