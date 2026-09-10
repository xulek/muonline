using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private static readonly Dictionary<Type, bool> NpcCrowdRenderingCompatibility = new();
        private static readonly ConcurrentDictionary<Type, StaticMapTypeCompatibility> StaticMapRenderingCompatibility = new();

        private readonly struct StaticMapTypeCompatibility
        {
            public StaticMapTypeCompatibility(
                bool defaultUpdate,
                bool defaultDraw,
                bool defaultDrawAfter,
                bool defaultMeshRendering,
                bool defaultShadowCaster)
            {
                DefaultUpdate = defaultUpdate;
                DefaultDraw = defaultDraw;
                DefaultDrawAfter = defaultDrawAfter;
                DefaultMeshRendering = defaultMeshRendering;
                DefaultShadowCaster = defaultShadowCaster;
            }

            public bool DefaultUpdate { get; }
            public bool DefaultDraw { get; }
            public bool DefaultDrawAfter { get; }
            public bool DefaultMeshRendering { get; }
            public bool DefaultShadowCaster { get; }
        }

        private StaticMapTypeCompatibility GetStaticMapTypeCompatibility()
        {
            if (_staticMapTypeCompatibilityInitialized)
                return _staticMapTypeCompatibility;

            Type type = GetType();
            StaticMapTypeCompatibility compatibility = StaticMapRenderingCompatibility.GetOrAdd(
                type,
                static concreteType => new StaticMapTypeCompatibility(
                    IsInheritedFromModelObject(concreteType, "Update", typeof(GameTime)),
                    IsInheritedFromModelObject(concreteType, "Draw", typeof(GameTime)),
                    IsInheritedFromModelObject(concreteType, "DrawAfter", typeof(GameTime)),
                    IsInheritedFromModelObject(concreteType, "DrawMesh", typeof(int)) &&
                    IsInheritedFromModelObject(concreteType, "DrawMeshWithItemMaterial", typeof(int)) &&
                    IsInheritedFromModelObject(concreteType, "DrawMeshWithMonsterMaterial", typeof(int)) &&
                    IsInheritedFromModelObject(concreteType, "DrawMeshWithDynamicLighting", typeof(int)),
                    IsInheritedFromModelObject(concreteType, "DrawShadowCaster", typeof(Effect), typeof(Matrix))));

            _staticMapTypeCompatibility = compatibility;
            _staticMapTypeCompatibilityInitialized = true;
            return compatibility;
        }

        private static bool UsesDefaultNpcCrowdRendering(NPCObject npc)
        {
            Type type = npc.GetType();
            lock (NpcCrowdRenderingCompatibility)
            {
                if (NpcCrowdRenderingCompatibility.TryGetValue(type, out bool compatible))
                    return compatible;

                compatible = IsInheritedFromModelObject(type, "Draw", typeof(GameTime))
                    && IsInheritedFromModelObject(type, "DrawAfter", typeof(GameTime))
                    && IsInheritedFromModelObject(type, "DrawMesh", typeof(int))
                    && IsInheritedFromModelObject(type, "DrawMeshWithItemMaterial", typeof(int))
                    && IsInheritedFromModelObject(type, "DrawMeshWithMonsterMaterial", typeof(int))
                    && IsInheritedFromModelObject(type, "DrawMeshWithDynamicLighting", typeof(int));

                NpcCrowdRenderingCompatibility[type] = compatible;
                return compatible;
            }
        }

        private static bool IsInheritedFromModelObject(Type type, string methodName, params Type[] parameterTypes)
        {
            var method = type.GetMethod(methodName, parameterTypes);
            return method?.DeclaringType == typeof(ModelObject);
        }

        internal enum StaticMapInstancingQueueResult
        {
            None,
            Partial,
            Full,
        }

        private readonly struct StaticMapInstancingBatchKey : IEquatable<StaticMapInstancingBatchKey>
        {
            public StaticMapInstancingBatchKey(
                BMD model,
                int meshIndex,
                Texture2D texture,
                bool twoSided,
                int poseKey)
            {
                Model = model;
                MeshIndex = meshIndex;
                Texture = texture;
                TwoSided = twoSided;
                PoseKey = poseKey;
            }

            public BMD Model { get; }
            public int MeshIndex { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }
            public int PoseKey { get; }

            public bool Equals(StaticMapInstancingBatchKey other)
            {
                return ReferenceEquals(Model, other.Model)
                    && MeshIndex == other.MeshIndex
                    && ReferenceEquals(Texture, other.Texture)
                    && TwoSided == other.TwoSided
                    && PoseKey == other.PoseKey;
            }

            public override bool Equals(object obj) => obj is StaticMapInstancingBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Model);
                    hash = (hash * 31) + MeshIndex;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Texture);
                    hash = (hash * 31) + (TwoSided ? 1 : 0);
                    hash = (hash * 31) + PoseKey;
                    return hash;
                }
            }
        }

        private readonly struct StaticMapInstancingMeshPlan
        {
            public StaticMapInstancingMeshPlan(
                int meshIndex,
                VertexBuffer geometryVertexBuffer,
                IndexBuffer geometryIndexBuffer,
                int boneCount,
                Texture2D texture,
                bool twoSided)
            {
                MeshIndex = meshIndex;
                GeometryVertexBuffer = geometryVertexBuffer;
                GeometryIndexBuffer = geometryIndexBuffer;
                BoneCount = boneCount;
                Texture = texture;
                TwoSided = twoSided;
            }

            public int MeshIndex { get; }
            public VertexBuffer GeometryVertexBuffer { get; }
            public IndexBuffer GeometryIndexBuffer { get; }
            public int BoneCount { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }
        }

        private readonly struct WalkerCrowdInstancingBatchKey : IEquatable<WalkerCrowdInstancingBatchKey>
        {
            public WalkerCrowdInstancingBatchKey(
                BMD model,
                int meshIndex,
                Texture2D texture,
                bool twoSided,
                int actionIndex,
                int frame0,
                int frame1,
                int interpolationBucket,
                int transitionPoseDiscriminator)
            {
                Model = model;
                MeshIndex = meshIndex;
                Texture = texture;
                TwoSided = twoSided;
                ActionIndex = actionIndex;
                Frame0 = frame0;
                Frame1 = frame1;
                InterpolationBucket = interpolationBucket;
                TransitionPoseDiscriminator = transitionPoseDiscriminator;
            }

            public BMD Model { get; }
            public int MeshIndex { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }
            public int ActionIndex { get; }
            public int Frame0 { get; }
            public int Frame1 { get; }
            public int InterpolationBucket { get; }
            public int TransitionPoseDiscriminator { get; }

            public bool Equals(WalkerCrowdInstancingBatchKey other)
            {
                return ReferenceEquals(Model, other.Model)
                    && MeshIndex == other.MeshIndex
                    && ReferenceEquals(Texture, other.Texture)
                    && TwoSided == other.TwoSided
                    && ActionIndex == other.ActionIndex
                    && Frame0 == other.Frame0
                    && Frame1 == other.Frame1
                    && InterpolationBucket == other.InterpolationBucket
                    && TransitionPoseDiscriminator == other.TransitionPoseDiscriminator;
            }

            public override bool Equals(object obj) => obj is WalkerCrowdInstancingBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Model);
                    hash = (hash * 31) + MeshIndex;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Texture);
                    hash = (hash * 31) + (TwoSided ? 1 : 0);
                    hash = (hash * 31) + ActionIndex;
                    hash = (hash * 31) + Frame0;
                    hash = (hash * 31) + Frame1;
                    hash = (hash * 31) + InterpolationBucket;
                    hash = (hash * 31) + TransitionPoseDiscriminator;
                    return hash;
                }
            }
        }

        private sealed class StaticMapInstancingBatch : IDisposable
        {
            private const ulong EmptySignature = 1469598103934665603UL;

            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public int BoneCount;
            public bool TwoSided;
            public Texture2D Texture;
            public ModelObject PoseSource;
            public StaticModelInstanceData[] InstanceData = new StaticModelInstanceData[64];
            public int InstanceCount;
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];
            public ulong QueueSignature = EmptySignature;
            public ulong UploadedSignature;
            public int UploadedInstanceCount;
            public long UploadedWorldInstanceId;
            public bool UploadedInstancesValid;

            public void AddInstance(in StaticModelInstanceData instanceData)
            {
                if (InstanceCount == InstanceData.Length)
                    Array.Resize(ref InstanceData, Math.Max(64, InstanceData.Length * 2));

                InstanceData[InstanceCount++] = instanceData;
            }

            public void ResetQueue()
            {
                InstanceCount = 0;
                QueueSignature = EmptySignature;
            }

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                InstanceData = Array.Empty<StaticModelInstanceData>();
                ResetQueue();
                UploadedInstancesValid = false;
                UploadedInstanceCount = 0;
                UploadedWorldInstanceId = 0;
                UploadedSignature = 0;
            }
        }

        private sealed class WalkerCrowdInstancingBatch : IDisposable
        {
            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public int BoneCount;
            public bool TwoSided;
            public Texture2D Texture;
            public ModelObject PoseSource;
            public readonly List<StaticModelInstanceData> Instances = new List<StaticModelInstanceData>(64);
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public StaticModelInstanceData[] UploadBuffer = Array.Empty<StaticModelInstanceData>();
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                UploadBuffer = Array.Empty<StaticModelInstanceData>();
                Instances.Clear();
            }
        }

        private static readonly Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch> _staticMapInstancingBatches = new Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch>(128);
        private static readonly List<StaticMapInstancingBatch> _staticMapInstancingActiveBatches = new List<StaticMapInstancingBatch>(128);
        private static readonly Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch> _staticMapShadowInstancingBatches = new Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch>(128);
        private static readonly List<StaticMapInstancingBatch> _staticMapShadowInstancingActiveBatches = new List<StaticMapInstancingBatch>(128);
        private static readonly DynamicLightGpuUploader _staticInstancingLightUploader = new(32);
        private static readonly Dictionary<WalkerCrowdInstancingBatchKey, WalkerCrowdInstancingBatch> _walkerCrowdInstancingBatches = new Dictionary<WalkerCrowdInstancingBatchKey, WalkerCrowdInstancingBatch>(128);
        private static readonly List<WalkerCrowdInstancingBatch> _walkerCrowdInstancingActiveBatches = new List<WalkerCrowdInstancingBatch>(128);
        private static bool _staticMapInstancingFailed;
        private static bool _staticMapShadowInstancingFailed;
        private static bool _walkerCrowdInstancingFailed;
        private static Effect _cachedStaticMapInstancingEffect;
        private static EffectTechnique _cachedStaticMapInstancingTechnique;
        private static EffectTechnique _cachedStaticMapVertexLitInstancingTechnique;
        private static EffectTechnique _cachedStaticMapSunOnlyInstancingTechnique;
        private static EffectTechnique _cachedStaticMapShadowInstancingTechnique;
        private static readonly Matrix _identity = Matrix.Identity;

        private static int _staticMapInstancedObjectsThisFrame = 0;
        private static int _staticMapInstancedMeshInstancesThisFrame = 0;
        private static int _staticMapInstancedBatchesThisFrame = 0;
        private static int _staticMapInstancedDrawCallsThisFrame = 0;
        private static int _staticMapInstancingFallbacksThisFrame = 0;
        private static int _staticMapInstanceUploadsThisFrame = 0;
        private static int _staticMapInstanceUploadReusesThisFrame = 0;
        private static int _staticMapShadowInstancedObjectsThisFrame = 0;
        private static int _staticMapShadowInstancedDrawCallsThisFrame = 0;
        private static int _staticMapShadowInstanceUploadsThisFrame = 0;
        private static int _staticMapShadowInstanceUploadReusesThisFrame = 0;

        private StaticMapInstancingMeshPlan[] _staticMapInstancingMeshPlan = Array.Empty<StaticMapInstancingMeshPlan>();
        private int _staticMapInstancingMeshPlanCount;
        private int _staticMapInstancingOpaqueMeshCount;
        private uint _builtStaticMapInstancingPlanVersion;
        private int _staticMapInstancingPlanRetryFrame;
        private int _staticMapInstancingPlanValidationFrame;
        private StaticMapTypeCompatibility _staticMapTypeCompatibility;
        private bool _staticMapTypeCompatibilityInitialized;

        public static int LastFrameStaticMapInstancedObjects { get; private set; }
        public static int LastFrameStaticMapInstancedMeshInstances { get; private set; }
        public static int LastFrameStaticMapInstancedBatches { get; private set; }
        public static int LastFrameStaticMapInstancedDrawCalls { get; private set; }
        public static int LastFrameStaticMapInstancingFallbacks { get; private set; }
        public static int LastFrameStaticMapInstanceUploads { get; private set; }
        public static int LastFrameStaticMapInstanceUploadReuses { get; private set; }
        public static int LastFrameStaticMapShadowInstancedObjects { get; private set; }
        public static int LastFrameStaticMapShadowInstancedDrawCalls { get; private set; }
        public static int LastFrameStaticMapShadowInstanceUploads { get; private set; }
        public static int LastFrameStaticMapShadowInstanceUploadReuses { get; private set; }
        public static bool IsStaticMapInstancingBackendSupported => SupportsGpuDynamicSkinning;
        public static bool IsStaticMapInstancingRuntimeDisabled =>
            _staticMapInstancingFailed || (_walkerCrowdInstancingFailed && _walkerCrowdMultiPoseInstancingFailed);
        public static bool IsStaticMapShadowInstancingRuntimeDisabled => _staticMapShadowInstancingFailed;

        private static void BeginFrameStaticMapInstancingMetrics()
        {
            LastFrameStaticMapInstancedObjects = _staticMapInstancedObjectsThisFrame;
            LastFrameStaticMapInstancedMeshInstances = _staticMapInstancedMeshInstancesThisFrame;
            LastFrameStaticMapInstancedBatches = _staticMapInstancedBatchesThisFrame;
            LastFrameStaticMapInstancedDrawCalls = _staticMapInstancedDrawCallsThisFrame;
            LastFrameStaticMapInstancingFallbacks = _staticMapInstancingFallbacksThisFrame;
            LastFrameStaticMapInstanceUploads = _staticMapInstanceUploadsThisFrame;
            LastFrameStaticMapInstanceUploadReuses = _staticMapInstanceUploadReusesThisFrame;
            LastFrameStaticMapShadowInstancedObjects = _staticMapShadowInstancedObjectsThisFrame;
            LastFrameStaticMapShadowInstancedDrawCalls = _staticMapShadowInstancedDrawCallsThisFrame;
            LastFrameStaticMapShadowInstanceUploads = _staticMapShadowInstanceUploadsThisFrame;
            LastFrameStaticMapShadowInstanceUploadReuses = _staticMapShadowInstanceUploadReusesThisFrame;

            _staticMapInstancedObjectsThisFrame = 0;
            _staticMapInstancedMeshInstancesThisFrame = 0;
            _staticMapInstancedBatchesThisFrame = 0;
            _staticMapInstancedDrawCallsThisFrame = 0;
            _staticMapInstancingFallbacksThisFrame = 0;
            _staticMapInstanceUploadsThisFrame = 0;
            _staticMapInstanceUploadReusesThisFrame = 0;
            _staticMapShadowInstancedObjectsThisFrame = 0;
            _staticMapShadowInstancedDrawCallsThisFrame = 0;
            _staticMapShadowInstanceUploadsThisFrame = 0;
            _staticMapShadowInstanceUploadReusesThisFrame = 0;
            BeginFrameWalkerCrowdMultiPoseMetrics();
        }

        internal static void RegisterStaticMapInstancingFallback()
        {
            _staticMapInstancingFallbacksThisFrame++;
        }

        internal static bool IsStaticMapInstancingPathAvailable()
        {
            return IsStaticMapInstancingSupported();
        }

        internal static StaticMapInstancingQueueResult TryQueueStaticMapObjectForInstancing(WorldObject obj)
        {
            if (obj is not ModelObject modelObject)
                return StaticMapInstancingQueueResult.None;

            return modelObject.TryQueueStaticMapObjectForInstancing();
        }

        internal static bool TryQueueStaticMapShadowCaster(ModelObject modelObject)
        {
            if (modelObject == null)
                return false;

            try
            {
                return modelObject.TryQueueStaticMapShadowCaster();
            }
            catch
            {
                ClearStaticMapShadowInstancingQueues();
                return false;
            }
        }

        internal static void RegisterStaticMapShadowInstancedObjects(int count)
        {
            if (count > 0)
                _staticMapShadowInstancedObjectsThisFrame += count;
        }

        internal static bool IsWalkerCrowdInstancingCandidate(WorldObject obj)
        {
            return obj is MonsterObject ||
                   obj is NPCObject npc && UsesDefaultNpcCrowdRendering(npc);
        }

        internal static bool TryQueueWalkerCrowdForInstancing(WorldObject obj)
        {
            if (obj is not ModelObject modelObject || !IsWalkerCrowdInstancingCandidate(obj))
                return false;

            return modelObject.TryQueueWalkerCrowdForInstancing();
        }

        internal static bool FlushStaticMapInstancingBatches(WorldControl world)
        {
            if (_staticMapInstancingActiveBatches.Count == 0)
                return true;

            bool success = true;
            if (_staticMapInstancingFailed || !IsStaticMapInstancingSupported())
            {
                ClearStaticMapInstancingQueues();
                return false;
            }

            var graphicsManager = GraphicsManager.Instance;
            var effect = graphicsManager.DynamicLightingEffect;
            EffectTechnique staticMapTechnique = Constants.ENABLE_STATIC_MAP_VERTEX_LIGHTING
                ? _cachedStaticMapVertexLitInstancingTechnique ?? _cachedStaticMapInstancingTechnique
                : _cachedStaticMapInstancingTechnique;
            if (effect == null || staticMapTechnique == null)
            {
                ClearStaticMapInstancingQueues();
                return false;
            }

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            var gd = graphicsManager.GraphicsDevice;
            var prevBlend = gd.BlendState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];
            var prevTechnique = effect.CurrentTechnique;

            try
            {
                PrepareStaticMapInstancingEffect(
                    effect,
                    world,
                    staticMapTechnique,
                    _cachedStaticMapSunOnlyInstancingTechnique);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _staticMapInstancingActiveBatches.Count; i++)
                {
                    var batch = _staticMapInstancingActiveBatches[i];
                    int instanceCount = batch.InstanceCount;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null || batch.GeometryVertexBuffer.IsDisposed ||
                        batch.GeometryIndexBuffer == null || batch.GeometryIndexBuffer.IsDisposed ||
                        batch.Texture == null || batch.Texture.IsDisposed ||
                        batch.PoseSource == null)
                    {
                        success = false;
                        continue;
                    }

                    if (!batch.PoseSource.TryUploadGpuSkinBoneMatrices(effect, batch.BoneCount))
                    {
                        success = false;
                        continue;
                    }

                    bool instanceBufferRecreated = EnsureInstanceVertexBuffer(gd, batch, instanceCount);
                    bool instanceUploadRequired =
                        !Constants.ENABLE_STATIC_MAP_INSTANCE_UPLOAD_CACHE ||
                        instanceBufferRecreated ||
                        !batch.UploadedInstancesValid ||
                        batch.UploadedWorldInstanceId != (world?.WorldInstanceId ?? 0) ||
                        batch.UploadedInstanceCount != instanceCount ||
                        batch.UploadedSignature != batch.QueueSignature;

                    if (instanceUploadRequired)
                    {
                        batch.InstanceBuffer.SetData(batch.InstanceData, 0, instanceCount, SetDataOptions.Discard);
                        batch.UploadedInstancesValid = true;
                        batch.UploadedWorldInstanceId = world?.WorldInstanceId ?? 0;
                        batch.UploadedInstanceCount = instanceCount;
                        batch.UploadedSignature = batch.QueueSignature;
                        _staticMapInstanceUploadsThisFrame++;
                    }
                    else
                    {
                        _staticMapInstanceUploadReusesThisFrame++;
                    }

                    gd.RasterizerState = batch.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    bindings.DiffuseTexture?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    _staticMapInstancedBatchesThisFrame++;
                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int p = 0; p < passCount; p++)
                    {
                        effect.CurrentTechnique.Passes[p].Apply();
                        ApplyQualityModelSampler(gd);
                        _staticMapInstancedDrawCallsThisFrame++;
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                    }
                }
            }
            catch (Exception ex)
            {
                success = false;
                _staticMapInstancingFailed = true;
                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex, "Static map hardware instancing disabled after runtime failure.");
            }
            finally
            {
                effect.CurrentTechnique = prevTechnique;
                gd.BlendState = prevBlend;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
                ClearStaticMapInstancingQueues();
            }

            return success;
        }

        internal static bool FlushStaticMapShadowInstancingBatches(
            Effect shadowEffect,
            Matrix lightViewProjection,
            WorldControl world)
        {
            if (_staticMapShadowInstancingActiveBatches.Count == 0)
                return true;

            bool success = true;
            if (shadowEffect == null || !IsStaticMapShadowInstancingSupported())
            {
                ClearStaticMapShadowInstancingQueues();
                return false;
            }

            ModelEffectBindings bindings = GetModelEffectBindings(shadowEffect);
            var gd = GraphicsManager.Instance.GraphicsDevice;
            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRaster = gd.RasterizerState;
            var previousSampler = gd.SamplerStates[0];
            var previousTechnique = shadowEffect.CurrentTechnique;

            try
            {
                shadowEffect.CurrentTechnique = _cachedStaticMapShadowInstancingTechnique;
                bindings.World?.SetValue(_identity);
                bindings.LightViewProjection?.SetValue(lightViewProjection);
                int shadowSize = GraphicsManager.Instance.ShadowMapRenderer?.ShadowMap?.Width
                    ?? Math.Max(256, Constants.SHADOW_MAP_SIZE);
                bindings.ShadowMapTexelSize?.SetValue(new Vector2(1f / shadowSize, 1f / shadowSize));
                bindings.ShadowBias?.SetValue(Constants.SHADOW_BIAS);
                bindings.ShadowNormalBias?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                bindings.SunDirection?.SetValue(
                    GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION);
                bindings.UseProceduralTerrainUv?.SetValue(0.0f);
                bindings.IsWaterTexture?.SetValue(0.0f);
                bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);

                gd.BlendState = BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.Default;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _staticMapShadowInstancingActiveBatches.Count; i++)
                {
                    StaticMapInstancingBatch batch = _staticMapShadowInstancingActiveBatches[i];
                    int instanceCount = batch.InstanceCount;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null || batch.GeometryVertexBuffer.IsDisposed ||
                        batch.GeometryIndexBuffer == null || batch.GeometryIndexBuffer.IsDisposed ||
                        batch.Texture == null || batch.Texture.IsDisposed ||
                        batch.PoseSource == null ||
                        !batch.PoseSource.TryUploadGpuSkinBoneMatrices(shadowEffect, batch.BoneCount))
                    {
                        success = false;
                        continue;
                    }

                    bool instanceBufferRecreated = EnsureInstanceVertexBuffer(gd, batch, instanceCount);
                    bool uploadRequired =
                        !Constants.ENABLE_STATIC_MAP_INSTANCE_UPLOAD_CACHE ||
                        instanceBufferRecreated ||
                        !batch.UploadedInstancesValid ||
                        batch.UploadedWorldInstanceId != (world?.WorldInstanceId ?? 0) ||
                        batch.UploadedInstanceCount != instanceCount ||
                        batch.UploadedSignature != batch.QueueSignature;

                    if (uploadRequired)
                    {
                        batch.InstanceBuffer.SetData(batch.InstanceData, 0, instanceCount, SetDataOptions.Discard);
                        batch.UploadedInstancesValid = true;
                        batch.UploadedWorldInstanceId = world?.WorldInstanceId ?? 0;
                        batch.UploadedInstanceCount = instanceCount;
                        batch.UploadedSignature = batch.QueueSignature;
                        _staticMapShadowInstanceUploadsThisFrame++;
                    }
                    else
                    {
                        _staticMapShadowInstanceUploadReusesThisFrame++;
                    }

                    gd.RasterizerState = batch.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    bindings.DiffuseTexture?.SetValue(batch.Texture);
                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    int passCount = shadowEffect.CurrentTechnique.Passes.Count;
                    for (int passIndex = 0; passIndex < passCount; passIndex++)
                    {
                        shadowEffect.CurrentTechnique.Passes[passIndex].Apply();
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                        _staticMapShadowInstancedDrawCallsThisFrame++;
                    }
                }
            }
            catch (Exception ex)
            {
                success = false;
                _staticMapShadowInstancingFailed = true;
                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(
                    ex,
                    "Static map shadow instancing disabled after runtime failure.");
            }
            finally
            {
                if (previousTechnique != null)
                    shadowEffect.CurrentTechnique = previousTechnique;
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRaster;
                gd.SamplerStates[0] = previousSampler;
                ClearStaticMapShadowInstancingQueues();
            }

            return success;
        }

        private static void FlushWalkerCrowdLegacyInstancingBatches(WorldControl world)
        {
            if (_walkerCrowdInstancingActiveBatches.Count == 0)
                return;

            if (_walkerCrowdInstancingFailed || !IsWalkerCrowdLegacyInstancingSupported())
            {
                ClearWalkerCrowdLegacyInstancingQueues();
                return;
            }

            var graphicsManager = GraphicsManager.Instance;
            var effect = graphicsManager.DynamicLightingEffect;
            if (effect == null || _cachedStaticMapInstancingTechnique == null)
            {
                ClearWalkerCrowdLegacyInstancingQueues();
                return;
            }

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            var gd = graphicsManager.GraphicsDevice;
            var prevBlend = gd.BlendState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];
            var prevTechnique = effect.CurrentTechnique;

            try
            {
                EffectTechnique crowdTechnique = Constants.ENABLE_CROWD_VERTEX_LIGHTING
                    ? _cachedStaticMapVertexLitInstancingTechnique ?? _cachedStaticMapInstancingTechnique
                    : _cachedStaticMapInstancingTechnique;
                PrepareStaticMapInstancingEffect(
                    effect,
                    world,
                    crowdTechnique,
                    _cachedStaticMapSunOnlyInstancingTechnique);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _walkerCrowdInstancingActiveBatches.Count; i++)
                {
                    var batch = _walkerCrowdInstancingActiveBatches[i];
                    int instanceCount = batch.Instances.Count;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null ||
                        batch.GeometryIndexBuffer == null ||
                        batch.Texture == null ||
                        batch.PoseSource == null)
                    {
                        continue;
                    }

                    if (!batch.PoseSource.TryUploadGpuSkinBoneMatrices(effect, batch.BoneCount))
                        continue;

                    EnsureInstanceUploadBuffer(batch, instanceCount);
                    for (int j = 0; j < instanceCount; j++)
                        batch.UploadBuffer[j] = batch.Instances[j];

                    EnsureInstanceVertexBuffer(gd, batch, instanceCount);
                    batch.InstanceBuffer.SetData(batch.UploadBuffer, 0, instanceCount, SetDataOptions.Discard);

                    gd.RasterizerState = batch.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    bindings.DiffuseTexture?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    // Count every GPU-skinned mesh instance. Previously the metric only
                    // counted the non-instanced path, making crowd-instanced monsters look
                    // as if GPU skinning had been disabled.
                    RegisterGpuSkinnedMeshDraw(instanceCount);

                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int p = 0; p < passCount; p++)
                    {
                        effect.CurrentTechnique.Passes[p].Apply();
                        ApplyQualityModelSampler(gd);
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _walkerCrowdInstancingFailed = true;
                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex, "Walker crowd hardware instancing disabled after runtime failure.");
            }
            finally
            {
                effect.CurrentTechnique = prevTechnique;
                gd.BlendState = prevBlend;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
                ClearWalkerCrowdLegacyInstancingQueues();
            }
        }

        internal static bool HasPendingStaticMapInstancingBatches() => _staticMapInstancingActiveBatches.Count > 0;

        internal void CancelStaticMapInstancingForCurrentFrame()
        {
            if (_staticMapInstancedMeshFrameTags == null)
                return;

            Array.Clear(_staticMapInstancedMeshFrameTags, 0, _staticMapInstancedMeshFrameTags.Length);
        }
        private static bool HasPendingWalkerCrowdLegacyInstancingBatches() => _walkerCrowdInstancingActiveBatches.Count > 0;

        /// <summary>
        /// Clears only per-frame, world-scoped instance queues. Persistent geometry buffers stay
        /// cached, but no transform, pose row or side-pass from the disposed world can be drawn
        /// by the next scene. This must run on the main/render thread during world transitions.
        /// </summary>
        internal static void ResetWorldScopedInstancingState()
        {
            DisposeStaticMapInstancingBatches();
            ClearWalkerCrowdLegacyInstancingQueues();
            ClearWalkerCrowdMultiPoseQueues();
        }

        private static void DisposeStaticMapInstancingBatches()
        {
            foreach (StaticMapInstancingBatch batch in _staticMapInstancingBatches.Values)
                batch.Dispose();

            _staticMapInstancingBatches.Clear();
            _staticMapInstancingActiveBatches.Clear();

            foreach (StaticMapInstancingBatch batch in _staticMapShadowInstancingBatches.Values)
                batch.Dispose();

            _staticMapShadowInstancingBatches.Clear();
            _staticMapShadowInstancingActiveBatches.Clear();
        }

        private static bool EnsureInstanceVertexBuffer(GraphicsDevice gd, StaticMapInstancingBatch batch, int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return false;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                StaticModelInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
            batch.UploadedInstancesValid = false;
            return true;
        }

        private static void EnsureInstanceUploadBuffer(WalkerCrowdInstancingBatch batch, int instanceCount)
        {
            if (batch.UploadBuffer.Length >= instanceCount)
                return;

            int newSize = Math.Max(instanceCount, batch.UploadBuffer.Length == 0 ? 64 : batch.UploadBuffer.Length * 2);
            batch.UploadBuffer = new StaticModelInstanceData[newSize];
        }

        private static void EnsureInstanceVertexBuffer(GraphicsDevice gd, WalkerCrowdInstancingBatch batch, int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                StaticModelInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
        }

        private static void ClearStaticMapInstancingQueues()
        {
            for (int i = 0; i < _staticMapInstancingActiveBatches.Count; i++)
                _staticMapInstancingActiveBatches[i].ResetQueue();

            _staticMapInstancingActiveBatches.Clear();
        }

        private static void ClearStaticMapShadowInstancingQueues()
        {
            for (int i = 0; i < _staticMapShadowInstancingActiveBatches.Count; i++)
                _staticMapShadowInstancingActiveBatches[i].ResetQueue();

            _staticMapShadowInstancingActiveBatches.Clear();
        }

        private static void ClearWalkerCrowdLegacyInstancingQueues()
        {
            for (int i = 0; i < _walkerCrowdInstancingActiveBatches.Count; i++)
                _walkerCrowdInstancingActiveBatches[i].Instances.Clear();

            _walkerCrowdInstancingActiveBatches.Clear();
        }

        private static bool IsStaticMapInstancingSupported()
        {
            if (_staticMapInstancingFailed ||
                !Constants.ENABLE_MAP_OBJECT_INSTANCING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return false;

            if (!ReferenceEquals(_cachedStaticMapInstancingEffect, effect))
            {
                _cachedStaticMapInstancingEffect = effect;
                _cachedStaticMapInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced");
                _cachedStaticMapVertexLitInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_VertexLit");
                _cachedStaticMapSunOnlyInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_SunOnly");
                _cachedStaticMapShadowInstancingTechnique = TryGetTechnique(effect, "ShadowCaster_SkinnedInstanced");
            }

            return Constants.ENABLE_STATIC_MAP_VERTEX_LIGHTING
                ? _cachedStaticMapVertexLitInstancingTechnique != null || _cachedStaticMapInstancingTechnique != null
                : _cachedStaticMapInstancingTechnique != null;
        }

        private static bool IsStaticMapShadowInstancingSupported()
        {
            if (_staticMapShadowInstancingFailed ||
                !Constants.ENABLE_STATIC_MAP_SHADOW_INSTANCING ||
                !Constants.ENABLE_GPU_SKINNING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return false;

            if (!ReferenceEquals(_cachedStaticMapInstancingEffect, effect))
            {
                _cachedStaticMapInstancingEffect = effect;
                _cachedStaticMapInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced");
                _cachedStaticMapVertexLitInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_VertexLit");
                _cachedStaticMapSunOnlyInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_SunOnly");
                _cachedStaticMapShadowInstancingTechnique = TryGetTechnique(effect, "ShadowCaster_SkinnedInstanced");
            }

            return _cachedStaticMapShadowInstancingTechnique != null;
        }

        private static bool IsWalkerCrowdLegacyInstancingSupported()
        {
            if (_walkerCrowdInstancingFailed ||
                !Constants.ENABLE_WALKER_CROWD_INSTANCING ||
                !Constants.ENABLE_GPU_SKINNING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return false;

            if (!ReferenceEquals(_cachedStaticMapInstancingEffect, effect))
            {
                _cachedStaticMapInstancingEffect = effect;
                _cachedStaticMapInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced");
                _cachedStaticMapVertexLitInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_VertexLit");
                _cachedStaticMapSunOnlyInstancingTechnique = TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced_SunOnly");
                _cachedStaticMapShadowInstancingTechnique = TryGetTechnique(effect, "ShadowCaster_SkinnedInstanced");
            }

            return Constants.ENABLE_CROWD_VERTEX_LIGHTING
                ? _cachedStaticMapVertexLitInstancingTechnique != null || _cachedStaticMapInstancingTechnique != null
                : _cachedStaticMapInstancingTechnique != null;
        }

        private StaticMapInstancingQueueResult TryQueueStaticMapObjectForInstancing()
        {
            if (!CanUseStaticMapInstancingObjectState() || !EnsureStaticMapInstancingPlan())
                return StaticMapInstancingQueueResult.None;

            EnsureStaticMapInstancingFrameTags(Model.Meshes.Length);
            int instancingFrameTag = MuGame.FrameIndex + 1;
            byte alpha = (byte)MathHelper.Clamp(TotalAlpha * 255f, 0f, 255f);
            int poseKey = GetStaticMapPoseKey();
            var instanceData = new StaticModelInstanceData(WorldPosition, new Color((byte)255, (byte)255, (byte)255, alpha));

            for (int planIndex = 0; planIndex < _staticMapInstancingMeshPlanCount; planIndex++)
            {
                StaticMapInstancingMeshPlan meshPlan = _staticMapInstancingMeshPlan[planIndex];
                var key = new StaticMapInstancingBatchKey(
                    Model,
                    meshPlan.MeshIndex,
                    meshPlan.Texture,
                    meshPlan.TwoSided,
                    poseKey);
                if (!_staticMapInstancingBatches.TryGetValue(key, out var batch))
                {
                    batch = new StaticMapInstancingBatch();
                    _staticMapInstancingBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = meshPlan.GeometryVertexBuffer;
                batch.GeometryIndexBuffer = meshPlan.GeometryIndexBuffer;
                batch.PrimitiveCount = meshPlan.GeometryIndexBuffer.IndexCount / 3;
                batch.BoneCount = meshPlan.BoneCount;
                batch.TwoSided = meshPlan.TwoSided;
                batch.Texture = meshPlan.Texture;

                if (batch.InstanceCount == 0)
                {
                    batch.PoseSource = this;
                    _staticMapInstancingActiveBatches.Add(batch);
                }

                batch.AddInstance(instanceData);
                batch.QueueSignature = MixStaticMapInstanceSignature(
                    batch.QueueSignature,
                    RuntimeHelpers.GetHashCode(this),
                    TransformVersion,
                    alpha);
                _staticMapInstancedMeshFrameTags[meshPlan.MeshIndex] = instancingFrameTag;
                _staticMapInstancedMeshInstancesThisFrame++;
            }

            _staticMapInstancedObjectsThisFrame++;
            return _staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount
                ? StaticMapInstancingQueueResult.Full
                : StaticMapInstancingQueueResult.Partial;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetStaticMapPoseKey()
        {
            if (Model?.Actions == null || Model.Actions.Length == 0)
                return 0;

            return Math.Clamp(CurrentAction, 0, Model.Actions.Length - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MixStaticMapInstanceSignature(
            ulong current,
            int objectIdentity,
            uint transformVersion,
            byte alpha)
        {
            unchecked
            {
                current ^= (uint)objectIdentity;
                current *= 1099511628211UL;
                current ^= transformVersion;
                current *= 1099511628211UL;
                current ^= alpha;
                current *= 1099511628211UL;
                return current;
            }
        }

        private bool TryQueueStaticMapShadowCaster()
        {
            if (!CanUseFullStaticMapShadowInstancing())
                return false;

            byte alpha = 255;
            int poseKey = GetStaticMapPoseKey();
            var instanceData = new StaticModelInstanceData(WorldPosition, Color.White);
            for (int planIndex = 0; planIndex < _staticMapInstancingMeshPlanCount; planIndex++)
            {
                StaticMapInstancingMeshPlan meshPlan = _staticMapInstancingMeshPlan[planIndex];
                var key = new StaticMapInstancingBatchKey(
                    Model,
                    meshPlan.MeshIndex,
                    meshPlan.Texture,
                    meshPlan.TwoSided,
                    poseKey);

                if (!_staticMapShadowInstancingBatches.TryGetValue(key, out StaticMapInstancingBatch batch))
                {
                    batch = new StaticMapInstancingBatch();
                    _staticMapShadowInstancingBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = meshPlan.GeometryVertexBuffer;
                batch.GeometryIndexBuffer = meshPlan.GeometryIndexBuffer;
                batch.PrimitiveCount = meshPlan.GeometryIndexBuffer.IndexCount / 3;
                batch.BoneCount = meshPlan.BoneCount;
                batch.TwoSided = meshPlan.TwoSided;
                batch.Texture = meshPlan.Texture;

                if (batch.InstanceCount == 0)
                {
                    batch.PoseSource = this;
                    _staticMapShadowInstancingActiveBatches.Add(batch);
                }

                batch.AddInstance(instanceData);
                batch.QueueSignature = MixStaticMapInstanceSignature(
                    batch.QueueSignature,
                    RuntimeHelpers.GetHashCode(this),
                    TransformVersion,
                    alpha);
            }

            return true;
        }

        private bool TryQueueWalkerCrowdLegacyForInstancing()
        {
            if (!CanUseWalkerCrowdInstancing())
                return false;

            if (Model?.Meshes == null || _meshes == null)
                return false;

            int meshCount = Model.Meshes.Length;
            var instanceData = new StaticModelInstanceData(WorldPosition, GetCrowdInstancingBodyColor());
            bool queuedAnyMesh = false;

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!CanUseWalkerCrowdMeshForInstancing(meshIndex))
                    return false;

                queuedAnyMesh = true;
            }

            if (!queuedAnyMesh)
                return false;

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out _))
                {
                    return false;
                }
            }

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out var geometryVB,
                    out var geometryIB,
                    out var boneCount))
                {
                    return false;
                }

                bool twoSided = IsMeshTwoSided(meshIndex, false);
                Texture2D texture = _meshes[meshIndex].Texture;
                // During an action cross-fade the final bone palette also depends on the
                // previous action and the per-object blend progress. Keep the object on the
                // GPU-instanced path, but isolate that temporary palette in a one-object batch.
                // Once blending finishes the discriminator returns to zero and matching
                // monsters are grouped together again.
                int transitionPoseDiscriminator = _isBlending
                    ? RuntimeHelpers.GetHashCode(this)
                    : 0;
                int batchActionIndex = _isBlending ? -1 : _animationSampleActionIndex;
                int batchFrame0 = _isBlending ? 0 : _animationSampleFrame0;
                int batchFrame1 = _isBlending ? 0 : _animationSampleFrame1;
                int batchInterpolationBucket = _isBlending ? 0 : _animationSampleInterpolationBucket;

                var key = new WalkerCrowdInstancingBatchKey(
                    Model,
                    meshIndex,
                    texture,
                    twoSided,
                    batchActionIndex,
                    batchFrame0,
                    batchFrame1,
                    batchInterpolationBucket,
                    transitionPoseDiscriminator);

                if (!_walkerCrowdInstancingBatches.TryGetValue(key, out var batch))
                {
                    batch = new WalkerCrowdInstancingBatch();
                    _walkerCrowdInstancingBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = geometryVB;
                batch.GeometryIndexBuffer = geometryIB;
                batch.PrimitiveCount = geometryIB.IndexCount / 3;
                batch.BoneCount = boneCount;
                batch.TwoSided = twoSided;
                batch.Texture = texture;

                if (batch.Instances.Count == 0)
                {
                    batch.PoseSource = this;
                    _walkerCrowdInstancingActiveBatches.Add(batch);
                }

                batch.Instances.Add(instanceData);
            }

            return true;
        }

        internal bool CanUseCompactStaticMapShadowSignature
        {
            get
            {
                return IsMapPlacementObject &&
                       Children.Count == 0 &&
                       !RequiresPerFrameAnimation &&
                       !ContinuousAnimation &&
                       !LinkParentAnimation &&
                       ParentBoneLink < 0 &&
                       !HasAnimatedCurrentAction();
            }
        }

        private bool CanUseFullStaticMapShadowInstancing()
        {
            if (!IsStaticMapShadowInstancingSupported() ||
                !RenderShadow ||
                Children.Count != 0 ||
                !GetStaticMapTypeCompatibility().DefaultShadowCaster)
            {
                return false;
            }

            EnsureMeshRenderPlans();
            if (!CanUseStaticMapInstancingObjectState() ||
                _transparentMeshPlan.Count != 0 ||
                !EnsureStaticMapInstancingPlan())
            {
                return false;
            }

            return _staticMapInstancingOpaqueMeshCount > 0 &&
                   _staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount;
        }

        internal bool CanUseDedicatedStaticMapRenderQueue()
        {
            if (!Constants.ENABLE_STATIC_MAP_RENDER_QUEUE ||
                Constants.DRAW_BOUNDING_BOXES ||
                Constants.DRAW_BOUNDING_BOXES_INTERACTIVES ||
                IsTransparent ||
                Interactive ||
                Children.Count != 0)
            {
                return false;
            }

            StaticMapTypeCompatibility compatibility = GetStaticMapTypeCompatibility();
            if (!compatibility.DefaultUpdate ||
                !compatibility.DefaultDraw ||
                !compatibility.DefaultDrawAfter ||
                !compatibility.DefaultMeshRendering)
            {
                return false;
            }

            EnsureMeshRenderPlans();
            if (!CanUseStaticMapInstancingObjectState() ||
                _transparentMeshPlan.Count != 0 ||
                !EnsureStaticMapInstancingPlan())
            {
                return false;
            }

            return _staticMapInstancingOpaqueMeshCount > 0 &&
                   _staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount;
        }

        internal bool CanSkipStaticMapWorldUpdate()
        {
            if (!Constants.ENABLE_STATIC_MAP_UPDATE_SKIP ||
                !IsMapPlacementObject ||
                !_contentLoaded ||
                !Visible ||
                Children.Count != 0 ||
                RequiresPerFrameWorldUpdate ||
                ContinuousAnimation ||
                LinkParentAnimation ||
                ParentBoneLink >= 0 ||
                HasAnimatedCurrentAction() ||
                _invalidatedBufferFlags != MeshDirtyFlags.None)
            {
                return false;
            }

            if (!GetStaticMapTypeCompatibility().DefaultUpdate)
                return false;

            // CPU-lit fallback geometry still needs its periodic lighting refresh. The shader
            // path evaluates terrain, sun and dynamic lights during rendering, so a fully static
            // placement has no meaningful per-frame ModelObject work once its buffers are ready.
            if (AllowLightingUpdates &&
                (!Constants.ENABLE_DYNAMIC_LIGHTING_SHADER ||
                 GraphicsManager.Instance.DynamicLightingEffect == null))
            {
                return false;
            }

            return true;
        }

        internal bool CanSkipDefaultDrawAfterPass()
        {
            if (!IsMapPlacementObject ||
                Constants.DRAW_BOUNDING_BOXES ||
                Constants.DRAW_BOUNDING_BOXES_INTERACTIVES ||
                Children.Count != 0 ||
                !GetStaticMapTypeCompatibility().DefaultDrawAfter)
            {
                return false;
            }

            EnsureMeshRenderPlans();
            return _transparentMeshPlan.Count == 0;
        }

        private bool CanUseStaticMapInstancing()
        {
            return CanUseStaticMapInstancingObjectState() && EnsureStaticMapInstancingPlan();
        }

        private bool CanUseStaticMapInstancingObjectState()
        {
            if (!IsStaticMapInstancingSupported())
                return false;

            if (!IsMapPlacementObject || !AllowMapObjectInstancing)
                return false;

            if (UsesMutableMeshData)
                return false;

            if (HasAnimatedCurrentAction())
                return false;

            if (!Visible || Model?.Meshes == null || Model.Meshes.Length == 0)
                return false;

            if (LinkParentAnimation || ParentBoneLink >= 0 || RequiresPerFrameAnimation || ContinuousAnimation)
                return false;

            if (TotalAlpha < 0.999f)
                return false;

            return true;
        }

        private bool EnsureStaticMapInstancingPlan()
        {
            int frame = MuGame.FrameIndex;
            if (_builtStaticMapInstancingPlanVersion == _meshRenderPlanVersion &&
                frame < _staticMapInstancingPlanValidationFrame &&
                (_staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount ||
                 frame < _staticMapInstancingPlanRetryFrame))
            {
                return _staticMapInstancingMeshPlanCount > 0;
            }

            if (_builtStaticMapInstancingPlanVersion == _meshRenderPlanVersion &&
                IsStaticMapInstancingPlanAlive() &&
                (_staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount ||
                 frame < _staticMapInstancingPlanRetryFrame))
            {
                _staticMapInstancingPlanValidationFrame = frame + 120;
                return _staticMapInstancingMeshPlanCount > 0;
            }

            RebuildStaticMapInstancingPlan();
            return _staticMapInstancingMeshPlanCount > 0;
        }

        private bool IsStaticMapInstancingPlanAlive()
        {
            if (_builtStaticMapInstancingPlanVersion == 0)
                return false;

            for (int i = 0; i < _staticMapInstancingMeshPlanCount; i++)
            {
                StaticMapInstancingMeshPlan plan = _staticMapInstancingMeshPlan[i];
                if (plan.GeometryVertexBuffer == null || plan.GeometryVertexBuffer.IsDisposed ||
                    plan.GeometryIndexBuffer == null || plan.GeometryIndexBuffer.IsDisposed ||
                    plan.Texture == null || plan.Texture.IsDisposed ||
                    _meshes == null ||
                    (uint)plan.MeshIndex >= (uint)_meshes.Length ||
                    !ReferenceEquals(plan.Texture, _meshes[plan.MeshIndex].Texture))
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildStaticMapInstancingPlan()
        {
            _staticMapInstancingMeshPlanCount = 0;
            _staticMapInstancingOpaqueMeshCount = 0;
            _builtStaticMapInstancingPlanVersion = _meshRenderPlanVersion;
            _staticMapInstancingPlanRetryFrame = MuGame.FrameIndex + 120;
            _staticMapInstancingPlanValidationFrame = MuGame.FrameIndex + 120;

            if (Model?.Meshes == null || _meshes == null)
                return;

            int meshCount = Math.Min(Model.Meshes.Length, _meshes.Length);
            if (_staticMapInstancingMeshPlan.Length < meshCount)
                _staticMapInstancingMeshPlan = new StaticMapInstancingMeshPlan[meshCount];

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueStaticMapMesh(meshIndex))
                    continue;

                _staticMapInstancingOpaqueMeshCount++;
                if (!CanUseStaticMapMeshForInstancing(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out VertexBuffer geometryVertexBuffer,
                    out IndexBuffer geometryIndexBuffer,
                    out int boneCount))
                {
                    continue;
                }

                _staticMapInstancingMeshPlan[_staticMapInstancingMeshPlanCount++] =
                    new StaticMapInstancingMeshPlan(
                        meshIndex,
                        geometryVertexBuffer,
                        geometryIndexBuffer,
                        boneCount,
                        _meshes[meshIndex].Texture,
                        IsMeshTwoSided(meshIndex, false));
            }

            if (_staticMapInstancingMeshPlanCount == _staticMapInstancingOpaqueMeshCount)
                _staticMapInstancingPlanRetryFrame = int.MaxValue;
        }

        private enum WalkerCrowdRejectionReason
        {
            None,
            Unsupported,
            Children,
            TypeOrRenderer,
            MutableMesh,
            Visibility,
            Animation,
            OneShot,
            Material
        }

        private bool CanUseWalkerCrowdInstancing() =>
            CanUseWalkerCrowdInstancing(out _);

        private bool CanUseWalkerCrowdInstancing(out WalkerCrowdRejectionReason reason)
        {
            if (!IsWalkerCrowdInstancingSupported())
            {
                reason = WalkerCrowdRejectionReason.Unsupported;
                return false;
            }

            if (Children.Count > 0)
            {
                reason = WalkerCrowdRejectionReason.Children;
                return false;
            }

            WalkerObject walker;
            bool isMonster = this is MonsterObject;
            if (isMonster)
                walker = (MonsterObject)this;
            else if (this is NPCObject npc && UsesDefaultNpcCrowdRendering(npc))
                walker = npc;
            else
            {
                reason = WalkerCrowdRejectionReason.TypeOrRenderer;
                return false;
            }

            if (UsesMutableMeshData)
            {
                reason = WalkerCrowdRejectionReason.MutableMesh;
                return false;
            }

            if (!Visible || Model?.Meshes == null || Model.Meshes.Length == 0)
            {
                reason = WalkerCrowdRejectionReason.Visibility;
                return false;
            }

            if (LinkParentAnimation || ParentBoneLink >= 0 || ContinuousAnimation || !_animationSampleValid ||
                _animationSampleActionIndex < 0)
            {
                reason = WalkerCrowdRejectionReason.Animation;
                return false;
            }

            if (walker.IsOneShotPlaying &&
                (!isMonster || !walker.IsAttackOrSkillAnimationPlaying()))
            {
                reason = WalkerCrowdRejectionReason.OneShot;
                return false;
            }

            if (TotalAlpha < 0.999f || EnableCustomShader || HasVisibleTransparentMapMesh())
            {
                reason = WalkerCrowdRejectionReason.Material;
                return false;
            }

            reason = WalkerCrowdRejectionReason.None;
            return true;
        }

        private bool HasAnimatedCurrentAction()
        {
            if (Model?.Actions == null || Model.Actions.Length == 0)
                return false;

            int actionIndex = Math.Clamp(CurrentAction, 0, Model.Actions.Length - 1);
            var action = Model.Actions[actionIndex];
            return action != null && action.NumAnimationKeys > 1;
        }

        private bool CanUseStaticMapMeshForInstancing(int meshIndex)
        {
            if (!ShouldQueueStaticMapMesh(meshIndex))
                return false;

            if (_meshes == null || meshIndex >= _meshes.Length || _meshes[meshIndex].Texture == null)
                return false;

            var shaderSelection = DetermineShaderForMesh(meshIndex);
            if (shaderSelection.UseItemMaterial || shaderSelection.UseMonsterMaterial)
                return false;

            return shaderSelection.UseDynamicLighting;
        }

        private bool CanUseWalkerCrowdMeshForInstancing(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (_meshes == null || meshIndex >= _meshes.Length || _meshes[meshIndex].Texture == null)
                return false;

            var shaderSelection = DetermineShaderForMesh(meshIndex);
            if (shaderSelection.UseItemMaterial || shaderSelection.UseMonsterMaterial)
                return false;

            var blendState = GetMeshBlendState(meshIndex, false);
            if (!ReferenceEquals(blendState, BlendState.Opaque))
                return false;

            return shaderSelection.UseDynamicLighting;
        }

        private void EnsureStaticMapInstancingFrameTags(int meshCount)
        {
            if (_staticMapInstancedMeshFrameTags != null && _staticMapInstancedMeshFrameTags.Length >= meshCount)
                return;

            _staticMapInstancedMeshFrameTags = new int[meshCount];
        }

        private bool ShouldQueueStaticMapMesh(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (IsHiddenMesh(meshIndex))
                return false;

            bool isBlend = IsBlendMesh(meshIndex);
            bool isRGBA = _meshes != null &&
                          (uint)meshIndex < (uint)_meshes.Length &&
                          _meshes[meshIndex].IsRgba;

            if (isBlend || isRGBA)
                return false;

            string blendingMode = Model.Meshes[meshIndex].BlendingMode;
            return string.IsNullOrEmpty(blendingMode) ||
                   string.Equals(blendingMode, "Opaque", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasVisibleTransparentMapMesh()
        {
            if (Model?.Meshes == null)
                return false;

            for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
            {
                if (IsHiddenMesh(meshIndex))
                    continue;

                bool isBlend = IsBlendMesh(meshIndex);
                bool isRGBA = _meshes != null &&
                              (uint)meshIndex < (uint)_meshes.Length &&
                              _meshes[meshIndex].IsRgba;

                if (isBlend || isRGBA)
                    return true;

                string blendingMode = Model.Meshes[meshIndex].BlendingMode;
                if (!string.IsNullOrEmpty(blendingMode) &&
                    !string.Equals(blendingMode, "Opaque", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ShouldQueueWalkerCrowdMesh(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (IsHiddenMesh(meshIndex))
                return false;

            bool isBlend = IsBlendMesh(meshIndex);
            bool isRGBA = _meshes != null &&
                          (uint)meshIndex < (uint)_meshes.Length &&
                          _meshes[meshIndex].IsRgba;

            return !isRGBA && !isBlend;
        }

        private Color GetCrowdInstancingBodyColor()
        {
            Vector3 meshLight = Light;
            if (LightEnabled && World?.Terrain != null)
            {
                Vector3 worldTranslation = WorldPosition.Translation;
                meshLight = EvaluateCombinedTerrainLight(worldTranslation.X, worldTranslation.Y) + Light;
            }

            float lightScale = TotalAlpha;
            byte alpha = (byte)MathHelper.Clamp(TotalAlpha * 255f, 0f, 255f);
            float r = MathF.Min(Color.R * (meshLight.X * lightScale), 255f);
            float g = MathF.Min(Color.G * (meshLight.Y * lightScale), 255f);
            float b = MathF.Min(Color.B * (meshLight.Z * lightScale), 255f);
            return new Color((byte)r, (byte)g, (byte)b, alpha);
        }

        private static void PrepareStaticMapInstancingEffect(
            Effect effect,
            WorldControl world,
            EffectTechnique technique = null,
            EffectTechnique sunOnlyTechnique = null)
        {
            EffectTechnique selectedTechnique = technique ?? _cachedStaticMapInstancingTechnique;
            if (effect == null || selectedTechnique == null)
                return;

            effect.CurrentTechnique = selectedTechnique;

            var camera = Camera.Instance;
            if (camera == null)
                return;

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            bindings.ViewProjection?.SetValue(camera.ViewProjection);
            bindings.Alpha?.SetValue(1f);
            bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);
            bindings.TerrainDynamicIntensityScale?.SetValue(1.5f);
            bindings.DebugLightingAreas?.SetValue(Constants.DEBUG_LIGHTING_AREAS ? 1.0f : 0.0f);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);
            bool sunEnabled = Constants.SUN_ENABLED && (world?.IsSunWorld ?? true);

            bindings.SunDirection?.SetValue(sunDir);
            bindings.SunColor?.SetValue(_sunColor);
            bindings.SunStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveSunStrength() : 0f);
            bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);
            bindings.AmbientLight?.SetValue(_ambientLightVector * SunCycleManager.AmbientMultiplier);

            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);
            int activeLightCount = UploadStaticMapInstancingDynamicLights(effect, world);
            if (activeLightCount == 0 && sunOnlyTechnique != null)
                effect.CurrentTechnique = sunOnlyTechnique;
        }

        private static int UploadStaticMapInstancingDynamicLights(Effect effect, WorldControl world)
        {
            var terrain = world?.Terrain;
            if (!Constants.ENABLE_DYNAMIC_LIGHTS || terrain == null)
            {
                _staticInstancingLightUploader.Clear(effect);
                return 0;
            }

            var visibleLights = terrain.VisibleLights;
            if (visibleLights == null || visibleLights.Count == 0)
            {
                _staticInstancingLightUploader.Clear(effect);
                return 0;
            }

            int maxLights = Math.Min(
                DynamicLightGpuUploader.ResolveEffectCapacity(effect, 32),
                Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 16 : 32);

            Vector2 focus = Camera.Instance != null
                ? new Vector2(Camera.Instance.Target.X, Camera.Instance.Target.Y)
                : Vector2.Zero;
            float focusRadius = ResolveStaticMapInstancingLightCoverageRadius();

            return _staticInstancingLightUploader.Upload(
                effect,
                visibleLights,
                focus,
                maxLights,
                focusRadius,
                terrain.VisibleLightsVersion,
                cacheCellSize: 192f);
        }

        private static float ResolveStaticMapInstancingLightCoverageRadius()
        {
            var camera = Camera.Instance;
            if (camera == null)
                return Constants.MAX_CAMERA_DISTANCE;

            float cameraDistance = Vector3.Distance(camera.Position, camera.Target);
            if (!float.IsFinite(cameraDistance) || cameraDistance <= 0f)
                return Constants.MAX_CAMERA_DISTANCE;

            return MathHelper.Clamp(cameraDistance * 1.6f, 900f, 3200f);
        }
    }
}
