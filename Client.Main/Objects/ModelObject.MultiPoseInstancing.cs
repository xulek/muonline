using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private const bool EnableWalkerCrowdMultiPoseInstancing = true;
        private const int CrowdBonePaletteMaxWidth = MaxGpuSkinBones * 4;
        private const int InitialCrowdPoseCapacity = 16;

        private readonly struct WalkerCrowdMultiPoseBatchKey : IEquatable<WalkerCrowdMultiPoseBatchKey>
        {
            public WalkerCrowdMultiPoseBatchKey(BMD model, int meshIndex, Texture2D texture, bool twoSided)
            {
                Model = model;
                MeshIndex = meshIndex;
                Texture = texture;
                TwoSided = twoSided;
            }

            public BMD Model { get; }
            public int MeshIndex { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }

            public bool Equals(WalkerCrowdMultiPoseBatchKey other) =>
                ReferenceEquals(Model, other.Model) &&
                MeshIndex == other.MeshIndex &&
                ReferenceEquals(Texture, other.Texture) &&
                TwoSided == other.TwoSided;

            public override bool Equals(object obj) => obj is WalkerCrowdMultiPoseBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Model);
                    hash = (hash * 31) + MeshIndex;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Texture);
                    hash = (hash * 31) + (TwoSided ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct WalkerCrowdPoseKey : IEquatable<WalkerCrowdPoseKey>
        {
            public WalkerCrowdPoseKey(Matrix[] bones, uint poseVersion)
            {
                Bones = bones;
                PoseVersion = poseVersion;
            }

            public Matrix[] Bones { get; }
            public uint PoseVersion { get; }

            public bool Equals(WalkerCrowdPoseKey other) =>
                ReferenceEquals(Bones, other.Bones) && PoseVersion == other.PoseVersion;

            public override bool Equals(object obj) => obj is WalkerCrowdPoseKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Bones) * 397) ^ (int)PoseVersion;
                }
            }
        }

        private readonly struct WalkerCrowdPoseUpload
        {
            public WalkerCrowdPoseUpload(Matrix[] bones, uint poseVersion)
            {
                Bones = bones;
                PoseVersion = poseVersion;
            }

            public Matrix[] Bones { get; }
            public uint PoseVersion { get; }
        }

        private sealed class PackedImmutableCrowdPose
        {
            public PackedImmutableCrowdPose(Matrix[] bones)
            {
                int boneCount = Math.Min(bones?.Length ?? 0, MaxGpuSkinBones);
                BoneCount = boneCount;
                Rows = new Vector4[boneCount * 4];

                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    Matrix matrix = bones[boneIndex];
                    int row = boneIndex * 4;
                    Rows[row + 0] = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
                    Rows[row + 1] = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
                    Rows[row + 2] = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
                    Rows[row + 3] = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
                }
            }

            public int BoneCount { get; }
            public Vector4[] Rows { get; }
        }

        private sealed class WalkerCrowdMultiPoseBatch : IDisposable
        {
            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public bool TwoSided;
            public Texture2D Texture;
            public readonly List<SkinnedCrowdInstanceData> Instances = new(64);
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public SkinnedCrowdInstanceData[] UploadBuffer = Array.Empty<SkinnedCrowdInstanceData>();
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                UploadBuffer = Array.Empty<SkinnedCrowdInstanceData>();
                Instances.Clear();
            }
        }

        private sealed class WalkerCrowdPaletteTextureSlot : IDisposable
        {
            public Texture2D Texture;
            public int Width;
            public int Height;
            public Matrix[][] UploadedSources = Array.Empty<Matrix[]>();
            public uint[] UploadedVersions = Array.Empty<uint>();
            public int[] UploadedWidths = Array.Empty<int>();

            public void EnsureMetadataCapacity(int rowCount)
            {
                if (UploadedSources.Length >= rowCount)
                    return;

                int newSize = Math.Max(rowCount, UploadedSources.Length == 0
                    ? InitialCrowdPoseCapacity
                    : UploadedSources.Length * 2);
                Array.Resize(ref UploadedSources, newSize);
                Array.Resize(ref UploadedVersions, newSize);
                Array.Resize(ref UploadedWidths, newSize);
            }

            public void InvalidateMetadata()
            {
                Array.Clear(UploadedSources, 0, UploadedSources.Length);
                Array.Clear(UploadedVersions, 0, UploadedVersions.Length);
                Array.Clear(UploadedWidths, 0, UploadedWidths.Length);
            }

            public void Dispose()
            {
                Texture?.Dispose();
                Texture = null;
                Width = 0;
                Height = 0;
                UploadedSources = Array.Empty<Matrix[]>();
                UploadedVersions = Array.Empty<uint>();
                UploadedWidths = Array.Empty<int>();
            }
        }

        private static readonly Dictionary<WalkerCrowdMultiPoseBatchKey, WalkerCrowdMultiPoseBatch>
            _walkerCrowdMultiPoseBatches = new(128);
        private static readonly List<WalkerCrowdMultiPoseBatch> _walkerCrowdMultiPoseActiveBatches = new(128);
        private static readonly Dictionary<WalkerCrowdPoseKey, int> _walkerCrowdPoseRows = new(128);
        private static readonly List<WalkerCrowdPoseUpload> _walkerCrowdPoseUploads = new(128);

        private static Effect _cachedWalkerCrowdMultiPoseEffect;
        private static EffectTechnique _cachedWalkerCrowdMultiPoseTechnique;
        private static EffectTechnique _cachedWalkerCrowdMultiPoseVertexLitTechnique;
        private static EffectTechnique _cachedWalkerCrowdMultiPoseSunOnlyTechnique;
        private static readonly ConditionalWeakTable<Matrix[], PackedImmutableCrowdPose>
            _packedImmutableCrowdPoses = new();
        private static readonly WalkerCrowdPaletteTextureSlot[] _walkerCrowdPaletteTextureRing =
        {
            new WalkerCrowdPaletteTextureSlot(),
            new WalkerCrowdPaletteTextureSlot(),
            new WalkerCrowdPaletteTextureSlot(),
        };
        private static int _walkerCrowdPaletteTextureCursor;
        private static Texture2D _walkerCrowdActiveBonePaletteTexture;
        private static int _walkerCrowdMaxBonesThisFlush;
        private static Vector4[] _walkerCrowdBonePaletteUpload = Array.Empty<Vector4>();
        private static Vector4[] _walkerCrowdBonePaletteRowUpload = Array.Empty<Vector4>();
        private static int[] _walkerCrowdDirtyPoseRows = Array.Empty<int>();
        private static bool _walkerCrowdMultiPoseInstancingFailed;
        private static bool _walkerCrowdPartialPaletteUpdatesSupported = true;

        private static int _walkerCrowdMultiPoseObjectsThisFrame;
        private static int _walkerCrowdMultiPoseMeshInstancesThisFrame;
        private static int _walkerCrowdMultiPoseDrawCallsThisFrame;
        private static int _walkerCrowdMultiPoseUniquePosesThisFrame;
        private static int _walkerCrowdMultiPosePaletteUploadsThisFrame;
        private static int _walkerCrowdMultiPoseDirtyRowsThisFrame;
        private static long _walkerCrowdMultiPosePaletteBytesThisFrame;
        private static int _walkerCrowdMultiPosePaletteCacheHitsThisFrame;
        private static int _walkerCrowdMultiPoseAttemptsThisFrame;
        private static int _walkerCrowdMultiPoseQueuedObjectsThisFrame;
        private static int _walkerCrowdMultiPoseRejectedObjectThisFrame;
        private static int _walkerCrowdMultiPoseRejectedMeshThisFrame;
        private static int _walkerCrowdMultiPoseRejectedBuffersThisFrame;
        private static int _walkerCrowdMultiPoseRejectedBonesThisFrame;
        private static int _walkerCrowdMultiPoseRejectedPaletteThisFrame;
        private static int _walkerCrowdMultiPoseRejectedUnsupportedThisFrame;
        private static int _walkerCrowdMultiPoseRejectedChildrenThisFrame;
        private static int _walkerCrowdMultiPoseRejectedTypeOrRendererThisFrame;
        private static int _walkerCrowdMultiPoseRejectedMutableMeshThisFrame;
        private static int _walkerCrowdMultiPoseRejectedVisibilityThisFrame;
        private static int _walkerCrowdMultiPoseRejectedAnimationThisFrame;
        private static int _walkerCrowdMultiPoseRejectedOneShotThisFrame;
        private static int _walkerCrowdMultiPoseRejectedMaterialThisFrame;

        public static int LastFrameWalkerCrowdMultiPoseObjects { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseMeshInstances { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseDrawCalls { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseUniquePoses { get; private set; }
        public static int LastFrameWalkerCrowdMultiPosePaletteUploads { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseDirtyRows { get; private set; }
        public static long LastFrameWalkerCrowdMultiPosePaletteBytes { get; private set; }
        public static int LastFrameWalkerCrowdMultiPosePaletteCacheHits { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseAttempts { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseQueuedObjects { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedObject { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedMesh { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedBuffers { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedBones { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedPalette { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedUnsupported { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedChildren { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedTypeOrRenderer { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedMutableMesh { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedVisibility { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedAnimation { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedOneShot { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseRejectedMaterial { get; private set; }
        public static bool IsWalkerCrowdMultiPoseActive => IsWalkerCrowdMultiPoseInstancingSupported();

        private static void BeginFrameWalkerCrowdMultiPoseMetrics()
        {
            LastFrameWalkerCrowdMultiPoseObjects = _walkerCrowdMultiPoseObjectsThisFrame;
            LastFrameWalkerCrowdMultiPoseMeshInstances = _walkerCrowdMultiPoseMeshInstancesThisFrame;
            LastFrameWalkerCrowdMultiPoseDrawCalls = _walkerCrowdMultiPoseDrawCallsThisFrame;
            LastFrameWalkerCrowdMultiPoseUniquePoses = _walkerCrowdMultiPoseUniquePosesThisFrame;
            LastFrameWalkerCrowdMultiPosePaletteUploads = _walkerCrowdMultiPosePaletteUploadsThisFrame;
            LastFrameWalkerCrowdMultiPoseDirtyRows = _walkerCrowdMultiPoseDirtyRowsThisFrame;
            LastFrameWalkerCrowdMultiPosePaletteBytes = _walkerCrowdMultiPosePaletteBytesThisFrame;
            LastFrameWalkerCrowdMultiPosePaletteCacheHits = _walkerCrowdMultiPosePaletteCacheHitsThisFrame;
            LastFrameWalkerCrowdMultiPoseAttempts = _walkerCrowdMultiPoseAttemptsThisFrame;
            LastFrameWalkerCrowdMultiPoseQueuedObjects = _walkerCrowdMultiPoseQueuedObjectsThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedObject = _walkerCrowdMultiPoseRejectedObjectThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedMesh = _walkerCrowdMultiPoseRejectedMeshThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedBuffers = _walkerCrowdMultiPoseRejectedBuffersThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedBones = _walkerCrowdMultiPoseRejectedBonesThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedPalette = _walkerCrowdMultiPoseRejectedPaletteThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedUnsupported = _walkerCrowdMultiPoseRejectedUnsupportedThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedChildren = _walkerCrowdMultiPoseRejectedChildrenThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedTypeOrRenderer = _walkerCrowdMultiPoseRejectedTypeOrRendererThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedMutableMesh = _walkerCrowdMultiPoseRejectedMutableMeshThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedVisibility = _walkerCrowdMultiPoseRejectedVisibilityThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedAnimation = _walkerCrowdMultiPoseRejectedAnimationThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedOneShot = _walkerCrowdMultiPoseRejectedOneShotThisFrame;
            LastFrameWalkerCrowdMultiPoseRejectedMaterial = _walkerCrowdMultiPoseRejectedMaterialThisFrame;

            _walkerCrowdMultiPoseObjectsThisFrame = 0;
            _walkerCrowdMultiPoseMeshInstancesThisFrame = 0;
            _walkerCrowdMultiPoseDrawCallsThisFrame = 0;
            _walkerCrowdMultiPoseUniquePosesThisFrame = 0;
            _walkerCrowdMultiPosePaletteUploadsThisFrame = 0;
            _walkerCrowdMultiPoseDirtyRowsThisFrame = 0;
            _walkerCrowdMultiPosePaletteBytesThisFrame = 0;
            _walkerCrowdMultiPosePaletteCacheHitsThisFrame = 0;
            _walkerCrowdMultiPoseAttemptsThisFrame = 0;
            _walkerCrowdMultiPoseQueuedObjectsThisFrame = 0;
            _walkerCrowdMultiPoseRejectedObjectThisFrame = 0;
            _walkerCrowdMultiPoseRejectedMeshThisFrame = 0;
            _walkerCrowdMultiPoseRejectedBuffersThisFrame = 0;
            _walkerCrowdMultiPoseRejectedBonesThisFrame = 0;
            _walkerCrowdMultiPoseRejectedPaletteThisFrame = 0;
            _walkerCrowdMultiPoseRejectedUnsupportedThisFrame = 0;
            _walkerCrowdMultiPoseRejectedChildrenThisFrame = 0;
            _walkerCrowdMultiPoseRejectedTypeOrRendererThisFrame = 0;
            _walkerCrowdMultiPoseRejectedMutableMeshThisFrame = 0;
            _walkerCrowdMultiPoseRejectedVisibilityThisFrame = 0;
            _walkerCrowdMultiPoseRejectedAnimationThisFrame = 0;
            _walkerCrowdMultiPoseRejectedOneShotThisFrame = 0;
            _walkerCrowdMultiPoseRejectedMaterialThisFrame = 0;
        }

        private static bool IsWalkerCrowdInstancingSupported() =>
            IsWalkerCrowdMultiPoseInstancingSupported() || IsWalkerCrowdLegacyInstancingSupported();

        private static bool IsWalkerCrowdMultiPoseInstancingSupported()
        {
            if (!EnableWalkerCrowdMultiPoseInstancing ||
                _walkerCrowdMultiPoseInstancingFailed ||
                !Constants.ENABLE_WALKER_CROWD_INSTANCING ||
                !Constants.ENABLE_GPU_SKINNING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            Effect effect = GraphicsManager.Instance?.DynamicLightingEffect;
            if (effect == null)
                return false;

            if (!ReferenceEquals(_cachedWalkerCrowdMultiPoseEffect, effect))
            {
                _cachedWalkerCrowdMultiPoseEffect = effect;
                _cachedWalkerCrowdMultiPoseTechnique = TryGetTechnique(
                    effect,
                    "DynamicLighting_SkinnedMultiPoseInstanced");
                _cachedWalkerCrowdMultiPoseVertexLitTechnique = TryGetTechnique(
                    effect,
                    "DynamicLighting_SkinnedMultiPoseInstanced_VertexLit");
                _cachedWalkerCrowdMultiPoseSunOnlyTechnique = TryGetTechnique(
                    effect,
                    "DynamicLighting_SkinnedMultiPoseInstanced_SunOnly");
            }

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            EffectTechnique selectedTechnique = Constants.ENABLE_CROWD_VERTEX_LIGHTING
                ? _cachedWalkerCrowdMultiPoseVertexLitTechnique ?? _cachedWalkerCrowdMultiPoseTechnique
                : _cachedWalkerCrowdMultiPoseTechnique;
            return selectedTechnique != null &&
                   bindings?.CrowdBonePaletteTexture != null &&
                   bindings.CrowdBonePaletteRowCount != null;
        }

        private bool TryQueueWalkerCrowdForInstancing()
        {
            if (IsWalkerCrowdMultiPoseInstancingSupported() && TryQueueWalkerCrowdMultiPoseForInstancing())
                return true;

            return TryQueueWalkerCrowdLegacyForInstancing();
        }

        private static void RegisterWalkerCrowdObjectRejection(WalkerCrowdRejectionReason reason)
        {
            switch (reason)
            {
                case WalkerCrowdRejectionReason.Unsupported:
                    _walkerCrowdMultiPoseRejectedUnsupportedThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.Children:
                    _walkerCrowdMultiPoseRejectedChildrenThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.TypeOrRenderer:
                    _walkerCrowdMultiPoseRejectedTypeOrRendererThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.MutableMesh:
                    _walkerCrowdMultiPoseRejectedMutableMeshThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.Visibility:
                    _walkerCrowdMultiPoseRejectedVisibilityThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.Animation:
                    _walkerCrowdMultiPoseRejectedAnimationThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.OneShot:
                    _walkerCrowdMultiPoseRejectedOneShotThisFrame++;
                    break;
                case WalkerCrowdRejectionReason.Material:
                    _walkerCrowdMultiPoseRejectedMaterialThisFrame++;
                    break;
            }
        }

        internal static bool HasPendingWalkerCrowdInstancingBatches() =>
            _walkerCrowdMultiPoseActiveBatches.Count > 0 || HasPendingWalkerCrowdLegacyInstancingBatches();

        internal static void FlushWalkerCrowdInstancingBatches(WorldControl world)
        {
            if (_walkerCrowdMultiPoseActiveBatches.Count > 0)
                FlushWalkerCrowdMultiPoseInstancingBatches(world);

            if (HasPendingWalkerCrowdLegacyInstancingBatches())
                FlushWalkerCrowdLegacyInstancingBatches(world);
        }

        private bool TryQueueWalkerCrowdMultiPoseForInstancing()
        {
            _walkerCrowdMultiPoseAttemptsThisFrame++;

            if (!CanUseWalkerCrowdInstancing(out WalkerCrowdRejectionReason rejectionReason) ||
                Model?.Meshes == null || _meshes == null)
            {
                _walkerCrowdMultiPoseRejectedObjectThisFrame++;
                RegisterWalkerCrowdObjectRejection(rejectionReason);
                return false;
            }

            int meshCount = Model.Meshes.Length;
            bool queuedAnyMesh = false;
            int requiredBoneCount = 0;

            // Validate the complete object before mutating any frame batch. This guarantees
            // that a failed multi-pose attempt can safely fall back to the legacy path.
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!CanUseWalkerCrowdMeshForInstancing(meshIndex))
                {
                    _walkerCrowdMultiPoseRejectedMeshThisFrame++;
                    return false;
                }

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out int boneCount) ||
                    boneCount <= 0 ||
                    boneCount > MaxGpuSkinBones)
                {
                    _walkerCrowdMultiPoseRejectedBuffersThisFrame++;
                    return false;
                }

                requiredBoneCount = Math.Max(requiredBoneCount, boneCount);
                queuedAnyMesh = true;
            }

            if (!queuedAnyMesh)
            {
                _walkerCrowdMultiPoseRejectedMeshThisFrame++;
                return false;
            }

            Matrix[] bones = GetEffectiveBoneTransforms();
            bones = GetRenderBoneTransforms(bones) ?? bones;
            if (bones == null || bones.Length == 0 || bones.Length > MaxGpuSkinBones)
            {
                _walkerCrowdMultiPoseRejectedBonesThisFrame++;
                return false;
            }

            int paletteRow = RegisterWalkerCrowdPose(
                bones,
                GetEffectiveBonePoseVersion(),
                requiredBoneCount);
            if (paletteRow < 0)
            {
                _walkerCrowdMultiPoseRejectedPaletteThisFrame++;
                return false;
            }

            var instanceData = new SkinnedCrowdInstanceData(
                WorldPosition,
                GetCrowdInstancingBodyColor(),
                paletteRow);

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out VertexBuffer geometryVB,
                    out IndexBuffer geometryIB,
                    out _))
                {
                    _walkerCrowdMultiPoseRejectedBuffersThisFrame++;
                    return false;
                }

                bool twoSided = IsMeshTwoSided(meshIndex, false);
                Texture2D texture = _meshes[meshIndex].Texture;
                var key = new WalkerCrowdMultiPoseBatchKey(Model, meshIndex, texture, twoSided);

                if (!_walkerCrowdMultiPoseBatches.TryGetValue(key, out WalkerCrowdMultiPoseBatch batch))
                {
                    batch = new WalkerCrowdMultiPoseBatch();
                    _walkerCrowdMultiPoseBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = geometryVB;
                batch.GeometryIndexBuffer = geometryIB;
                batch.PrimitiveCount = geometryIB.IndexCount / 3;
                batch.TwoSided = twoSided;
                batch.Texture = texture;

                if (batch.Instances.Count == 0)
                    _walkerCrowdMultiPoseActiveBatches.Add(batch);

                batch.Instances.Add(instanceData);
                _walkerCrowdMultiPoseMeshInstancesThisFrame++;
            }

            _walkerCrowdMultiPoseObjectsThisFrame++;
            _walkerCrowdMultiPoseQueuedObjectsThisFrame++;
            return true;
        }

        private static int RegisterWalkerCrowdPose(
            Matrix[] bones,
            uint poseVersion,
            int requiredBoneCount)
        {
            _walkerCrowdMaxBonesThisFlush = Math.Max(
                _walkerCrowdMaxBonesThisFlush,
                Math.Min(Math.Max(requiredBoneCount, bones?.Length ?? 0), MaxGpuSkinBones));

            var key = new WalkerCrowdPoseKey(bones, poseVersion);
            if (_walkerCrowdPoseRows.TryGetValue(key, out int existingRow))
                return existingRow;

            int row = _walkerCrowdPoseUploads.Count;
            _walkerCrowdPoseRows.Add(key, row);
            _walkerCrowdPoseUploads.Add(new WalkerCrowdPoseUpload(bones, poseVersion));
            return row;
        }

        private static void FlushWalkerCrowdMultiPoseInstancingBatches(WorldControl world)
        {
            if (_walkerCrowdMultiPoseActiveBatches.Count == 0)
                return;

            if (!IsWalkerCrowdMultiPoseInstancingSupported())
            {
                ClearWalkerCrowdMultiPoseQueues();
                return;
            }

            GraphicsManager graphicsManager = GraphicsManager.Instance;
            Effect effect = graphicsManager.DynamicLightingEffect;
            GraphicsDevice gd = graphicsManager.GraphicsDevice;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);

            EffectTechnique crowdTechnique = Constants.ENABLE_CROWD_VERTEX_LIGHTING
                ? _cachedWalkerCrowdMultiPoseVertexLitTechnique ?? _cachedWalkerCrowdMultiPoseTechnique
                : _cachedWalkerCrowdMultiPoseTechnique;
            if (effect == null || gd == null || bindings == null || crowdTechnique == null)
            {
                ClearWalkerCrowdMultiPoseQueues();
                return;
            }

            BlendState previousBlend = gd.BlendState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];
            EffectTechnique previousTechnique = effect.CurrentTechnique;

            try
            {
                int poseCount = _walkerCrowdPoseUploads.Count;
                if (poseCount <= 0 || !UploadWalkerCrowdBonePalette(gd, poseCount))
                    throw new InvalidOperationException("Unable to upload the multi-pose crowd bone palette.");

                PrepareStaticMapInstancingEffect(
                    effect,
                    world,
                    crowdTechnique,
                    _cachedWalkerCrowdMultiPoseSunOnlyTechnique);
                bindings.CrowdBonePaletteTexture.SetValue(_walkerCrowdActiveBonePaletteTexture);
                bindings.CrowdBonePaletteRowCount.SetValue((float)poseCount);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _walkerCrowdMultiPoseActiveBatches.Count; i++)
                {
                    WalkerCrowdMultiPoseBatch batch = _walkerCrowdMultiPoseActiveBatches[i];
                    int instanceCount = batch.Instances.Count;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null ||
                        batch.GeometryVertexBuffer.IsDisposed ||
                        batch.GeometryIndexBuffer == null ||
                        batch.GeometryIndexBuffer.IsDisposed ||
                        batch.Texture == null ||
                        batch.Texture.IsDisposed)
                    {
                        continue;
                    }

                    EnsureWalkerCrowdMultiPoseUploadBuffer(batch, instanceCount);
                    for (int j = 0; j < instanceCount; j++)
                        batch.UploadBuffer[j] = batch.Instances[j];

                    EnsureWalkerCrowdMultiPoseInstanceBuffer(gd, batch, instanceCount);
                    batch.InstanceBuffer.SetData(batch.UploadBuffer, 0, instanceCount, SetDataOptions.Discard);

                    gd.RasterizerState = batch.TwoSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullClockwise;
                    bindings.DiffuseTexture?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    RegisterGpuSkinnedMeshDraw(instanceCount);
                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int passIndex = 0; passIndex < passCount; passIndex++)
                    {
                        effect.CurrentTechnique.Passes[passIndex].Apply();
                        ApplyQualityModelSampler(gd);
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                        _walkerCrowdMultiPoseDrawCallsThisFrame++;
                    }
                }

                _walkerCrowdMultiPoseUniquePosesThisFrame += poseCount;
            }
            catch (Exception ex)
            {
                _walkerCrowdMultiPoseInstancingFailed = true;
                DisposeWalkerCrowdMultiPoseGpuResources();
                MuGame.AppLoggerFactory?
                    .CreateLogger<ModelObject>()?
                    .LogWarning(
                        ex,
                        "Multi-pose crowd instancing disabled after runtime failure; the legacy GPU crowd path will be used.");
            }
            finally
            {
                effect.CurrentTechnique = previousTechnique;
                gd.BlendState = previousBlend;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
                ClearWalkerCrowdMultiPoseQueues();
            }
        }

        private static bool UploadWalkerCrowdBonePalette(GraphicsDevice gd, int poseCount)
        {
            int paletteBoneCapacity = ResolveCrowdPaletteBoneCapacity(_walkerCrowdMaxBonesThisFlush);
            int uploadWidth = paletteBoneCapacity * 4;
            WalkerCrowdPaletteTextureSlot slot = AcquireWalkerCrowdBonePaletteTextureSlot(
                gd,
                uploadWidth,
                poseCount);
            if (slot?.Texture == null || slot.Texture.IsDisposed)
                return false;

            _walkerCrowdActiveBonePaletteTexture = slot.Texture;
            slot.EnsureMetadataCapacity(poseCount);

            if (_walkerCrowdDirtyPoseRows.Length < poseCount)
                _walkerCrowdDirtyPoseRows = new int[RoundUpToPowerOfTwo(poseCount)];

            int dirtyCount = 0;
            for (int poseIndex = 0; poseIndex < poseCount; poseIndex++)
            {
                WalkerCrowdPoseUpload pose = _walkerCrowdPoseUploads[poseIndex];
                bool isDirty = !ReferenceEquals(slot.UploadedSources[poseIndex], pose.Bones) ||
                               slot.UploadedVersions[poseIndex] != pose.PoseVersion ||
                               slot.UploadedWidths[poseIndex] < uploadWidth;
                if (isDirty)
                    _walkerCrowdDirtyPoseRows[dirtyCount++] = poseIndex;
            }

            if (dirtyCount == 0)
            {
                _walkerCrowdMultiPosePaletteCacheHitsThisFrame += poseCount;
                return true;
            }

            _walkerCrowdMultiPoseDirtyRowsThisFrame += dirtyCount;
            _walkerCrowdMultiPosePaletteCacheHitsThisFrame += poseCount - dirtyCount;

            // A few changed poses are cheaper as row-sized updates. When many rows changed,
            // one contiguous upload avoids a large number of D3D UpdateSubresource calls.
            bool usePartialRows = _walkerCrowdPartialPaletteUpdatesSupported &&
                                  (dirtyCount <= 4 || dirtyCount * 3 <= poseCount);
            if (usePartialRows)
            {
                if (_walkerCrowdBonePaletteRowUpload.Length < uploadWidth)
                    _walkerCrowdBonePaletteRowUpload = new Vector4[RoundUpToPowerOfTwo(uploadWidth)];

                try
                {
                    for (int i = 0; i < dirtyCount; i++)
                    {
                        int poseIndex = _walkerCrowdDirtyPoseRows[i];
                        WalkerCrowdPoseUpload pose = _walkerCrowdPoseUploads[poseIndex];
                        PackWalkerCrowdPoseRow(pose, uploadWidth, _walkerCrowdBonePaletteRowUpload, 0);
                        slot.Texture.SetData(
                            0,
                            new Rectangle(0, poseIndex, uploadWidth, 1),
                            _walkerCrowdBonePaletteRowUpload,
                            0,
                            uploadWidth);
                        MarkWalkerCrowdPaletteRowUploaded(slot, poseIndex, pose, uploadWidth);
                    }

                    _walkerCrowdMultiPosePaletteUploadsThisFrame += dirtyCount;
                    _walkerCrowdMultiPosePaletteBytesThisFrame += dirtyCount * uploadWidth * 16L;
                    return true;
                }
                catch (Exception ex)
                {
                    // Some backends implement full texture uploads but reject sub-rect updates.
                    // Disable only the partial optimization and immediately fall back to the
                    // proven full-atlas path instead of disabling multi-pose instancing.
                    _walkerCrowdPartialPaletteUpdatesSupported = false;
                    slot.InvalidateMetadata();
                    MuGame.AppLoggerFactory?
                        .CreateLogger<ModelObject>()?
                        .LogDebug(ex, "Partial crowd palette updates are unavailable; using full atlas uploads.");
                }
            }

            int vectorCount = checked(uploadWidth * poseCount);
            if (_walkerCrowdBonePaletteUpload.Length < vectorCount)
                _walkerCrowdBonePaletteUpload = new Vector4[RoundUpToPowerOfTwo(vectorCount)];

            for (int poseIndex = 0; poseIndex < poseCount; poseIndex++)
            {
                WalkerCrowdPoseUpload pose = _walkerCrowdPoseUploads[poseIndex];
                PackWalkerCrowdPoseRow(
                    pose,
                    uploadWidth,
                    _walkerCrowdBonePaletteUpload,
                    poseIndex * uploadWidth);
                MarkWalkerCrowdPaletteRowUploaded(slot, poseIndex, pose, uploadWidth);
            }

            slot.Texture.SetData(
                0,
                new Rectangle(0, 0, uploadWidth, poseCount),
                _walkerCrowdBonePaletteUpload,
                0,
                vectorCount);
            _walkerCrowdMultiPosePaletteUploadsThisFrame++;
            _walkerCrowdMultiPosePaletteBytesThisFrame += vectorCount * 16L;
            return true;
        }

        private static void PackWalkerCrowdPoseRow(
            WalkerCrowdPoseUpload pose,
            int uploadWidth,
            Vector4[] destination,
            int destinationOffset)
        {
            Matrix[] bones = pose.Bones;
            int boneCapacity = uploadWidth / 4;
            int boneCount = Math.Min(bones?.Length ?? 0, boneCapacity);
            int copiedRows = 0;

            if (pose.PoseVersion == uint.MaxValue && bones != null)
            {
                PackedImmutableCrowdPose packed = _packedImmutableCrowdPoses.GetValue(
                    bones,
                    static source => new PackedImmutableCrowdPose(source));
                copiedRows = Math.Min(packed.Rows.Length, boneCount * 4);
                if (copiedRows > 0)
                    Array.Copy(packed.Rows, 0, destination, destinationOffset, copiedRows);
            }
            else if (bones != null)
            {
                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    Matrix matrix = bones[boneIndex];
                    int texel = destinationOffset + (boneIndex * 4);
                    destination[texel + 0] = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
                    destination[texel + 1] = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
                    destination[texel + 2] = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
                    destination[texel + 3] = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
                }
                copiedRows = boneCount * 4;
            }

            for (int texel = copiedRows; texel < uploadWidth; texel += 4)
            {
                int target = destinationOffset + texel;
                destination[target + 0] = new Vector4(1f, 0f, 0f, 0f);
                destination[target + 1] = new Vector4(0f, 1f, 0f, 0f);
                destination[target + 2] = new Vector4(0f, 0f, 1f, 0f);
                destination[target + 3] = new Vector4(0f, 0f, 0f, 1f);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MarkWalkerCrowdPaletteRowUploaded(
            WalkerCrowdPaletteTextureSlot slot,
            int poseIndex,
            WalkerCrowdPoseUpload pose,
            int uploadWidth)
        {
            slot.UploadedSources[poseIndex] = pose.Bones;
            slot.UploadedVersions[poseIndex] = pose.PoseVersion;
            slot.UploadedWidths[poseIndex] = uploadWidth;
        }

        private static WalkerCrowdPaletteTextureSlot AcquireWalkerCrowdBonePaletteTextureSlot(
            GraphicsDevice gd,
            int requiredWidth,
            int poseCount)
        {
            int slotIndex = _walkerCrowdPaletteTextureCursor++ % _walkerCrowdPaletteTextureRing.Length;
            WalkerCrowdPaletteTextureSlot slot = _walkerCrowdPaletteTextureRing[slotIndex];
            int requiredHeight = Math.Max(InitialCrowdPoseCapacity, RoundUpToPowerOfTwo(poseCount));

            if (slot.Texture == null ||
                slot.Texture.IsDisposed ||
                !ReferenceEquals(slot.Texture.GraphicsDevice, gd) ||
                slot.Width < requiredWidth ||
                slot.Height < requiredHeight)
            {
                int textureWidth = Math.Min(
                    CrowdBonePaletteMaxWidth,
                    Math.Max(requiredWidth, slot.Width));
                int textureHeight = Math.Max(requiredHeight, slot.Height);

                slot.Dispose();
                slot.Texture = new Texture2D(
                    gd,
                    textureWidth,
                    textureHeight,
                    false,
                    SurfaceFormat.Vector4);
                slot.Width = textureWidth;
                slot.Height = textureHeight;
                slot.EnsureMetadataCapacity(textureHeight);
                slot.InvalidateMetadata();
            }
            else
            {
                slot.EnsureMetadataCapacity(slot.Height);
            }

            return slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveCrowdPaletteBoneCapacity(int boneCount)
        {
            if (boneCount <= 32)
                return 32;
            if (boneCount <= 64)
                return 64;
            if (boneCount <= 128)
                return 128;
            return MaxGpuSkinBones;
        }

        private static void EnsureWalkerCrowdMultiPoseUploadBuffer(
            WalkerCrowdMultiPoseBatch batch,
            int instanceCount)
        {
            if (batch.UploadBuffer.Length >= instanceCount)
                return;

            int newSize = Math.Max(
                instanceCount,
                batch.UploadBuffer.Length == 0 ? 64 : batch.UploadBuffer.Length * 2);
            batch.UploadBuffer = new SkinnedCrowdInstanceData[newSize];
        }

        private static void EnsureWalkerCrowdMultiPoseInstanceBuffer(
            GraphicsDevice gd,
            WalkerCrowdMultiPoseBatch batch,
            int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                ReferenceEquals(batch.InstanceBuffer.GraphicsDevice, gd) &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                SkinnedCrowdInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
        }

        private static void ClearWalkerCrowdMultiPoseQueues()
        {
            for (int i = 0; i < _walkerCrowdMultiPoseActiveBatches.Count; i++)
                _walkerCrowdMultiPoseActiveBatches[i].Instances.Clear();

            _walkerCrowdMultiPoseActiveBatches.Clear();
            _walkerCrowdPoseRows.Clear();
            _walkerCrowdPoseUploads.Clear();
            _walkerCrowdMaxBonesThisFlush = 0;
        }

        private static void DisposeWalkerCrowdMultiPoseGpuResources()
        {
            foreach (WalkerCrowdMultiPoseBatch batch in _walkerCrowdMultiPoseBatches.Values)
                batch.Dispose();

            _walkerCrowdMultiPoseBatches.Clear();
            _walkerCrowdMultiPoseActiveBatches.Clear();
            _walkerCrowdPoseRows.Clear();
            _walkerCrowdPoseUploads.Clear();

            for (int i = 0; i < _walkerCrowdPaletteTextureRing.Length; i++)
                _walkerCrowdPaletteTextureRing[i].Dispose();

            _walkerCrowdPaletteTextureCursor = 0;
            _walkerCrowdActiveBonePaletteTexture = null;
            _walkerCrowdMaxBonesThisFlush = 0;
            _walkerCrowdBonePaletteUpload = Array.Empty<Vector4>();
            _walkerCrowdBonePaletteRowUpload = Array.Empty<Vector4>();
            _walkerCrowdDirtyPoseRows = Array.Empty<int>();
            _walkerCrowdPartialPaletteUpdatesSupported = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RoundUpToPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
