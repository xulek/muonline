using Client.Data.BMD;
using Client.Data.Texture;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        // Per-mesh buffer cache
        private struct MeshBufferCache
        {
            public DynamicVertexBuffer VertexBuffer;
            public DynamicIndexBuffer IndexBuffer;
            public Vector3 CachedLight;
            public Color CachedBodyColor;
            public uint LastUpdateFrame;
            public bool IsValid;
        }

        /// <summary>
        /// Allows derived objects to deform vertices procedurally during buffer generation.
        /// Default returns null (no deformation).
        /// </summary>
        protected virtual IVertexDeformer GetVertexDeformer()
        {
            return null;
        }

        private void SetDynamicBuffers(bool allowHidden = false)
        {
            if (_invalidatedBufferFlags == MeshDirtyFlags.None || Model?.Meshes == null)
                return;

            try
            {
                int meshCount = Model.Meshes.Length;
                if (ShouldSkipDynamicBufferUpdate(meshCount, allowHidden))
                    return;

                uint currentFrame = unchecked((uint)MuGame.FrameIndex);

                if (ShouldSkipTransformOnlyBufferUpdate())
                    return;

                EnsureMeshRuntimeState(meshCount);

                // Bone transforms are expensive to prepare. Delay until a mesh actually needs CPU skinning.
                Matrix[] bones = null;

                IVertexDeformer vertexDeformer = GetVertexDeformer();
                bool hasVertexDeformer = vertexDeformer != null;
                bool usesMutableMeshData = UsesMutableMeshData;
                bool skipSharedMeshCache = hasVertexDeformer || usesMutableMeshData;
                bool projectedShadowCpuBuffersRequired = RequiresCpuProjectedShadowBuffers();
                bool canUseStaticMapCpuSkip = !usesMutableMeshData &&
                                              !projectedShadowCpuBuffersRequired &&
                                              CanUseStaticMapInstancing();
                bool canUseWalkerCrowdCpuSkip = !usesMutableMeshData &&
                                                 !projectedShadowCpuBuffersRequired &&
                                                 CanFullyUseWalkerCrowdInstancingForCpuSkip();

                // Calculate lighting only once if lighting flags are set
                bool needLightCalculation = (_invalidatedBufferFlags & MeshDirtyFlags.Lighting) != 0;
                Vector3 baseLight = Vector3.Zero;
                Vector3 worldTranslation = WorldPosition.Translation;

                if (needLightCalculation && LightEnabled && World?.Terrain != null)
                {
                    baseLight = EvaluateCombinedTerrainLight(worldTranslation.X, worldTranslation.Y) + Light;
                }
                else if (needLightCalculation)
                {
                    baseLight = Light;
                }

                // Pre-calculate common color components (cache to avoid property access)
                float colorR = Color.R;
                float colorG = Color.G;
                float colorB = Color.B;
                float totalAlpha = TotalAlpha;
                float blendMeshLight = BlendMeshLight;
                bool textureDirty = (_invalidatedBufferFlags & MeshDirtyFlags.Texture) != 0;
                bool hasPendingTextureResources = false;

                // Process only meshes that need updates
                for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                {
                    try
                    {
                        ref var cache = ref _meshes[meshIndex].BufferCache;
                        var mesh = Model.Meshes[meshIndex];

                        if (IsHiddenMesh(meshIndex) && (_invalidatedBufferFlags & MeshDirtyFlags.Texture) == 0)
                            continue;

                        // Main-pass GPU skinning and projected mesh shadows are independent concerns.
                        // A projected shadow may still need CPU-skinned buffers, but that must not disable
                        // GPU skinning for the visible model. In that case both buffer paths are maintained.
                        bool canUseGpuSkinning = CanUseGpuSkinningForMesh(meshIndex);

                        var ms = _meshes[meshIndex];
                        bool gpuSkinReady = canUseGpuSkinning &&
                                            ms.GpuSkinEnabled &&
                                            ms.GpuVertexBuffer != null && !ms.GpuVertexBuffer.IsDisposed &&
                                            ms.GpuIndexBuffer != null && !ms.GpuIndexBuffer.IsDisposed &&
                                            ms.GpuBoneCount > 0;

                        bool gpuSkinActive = canUseGpuSkinning &&
                                             (gpuSkinReady ||
                                              TryEnableGpuSkinnedMesh(
                                                  meshIndex,
                                                  mesh,
                                                  preserveCpuBuffers: projectedShadowCpuBuffersRequired));

                        if (gpuSkinActive)
                        {
                            bool gpuTextureReady = EnsureMeshTextureLoaded(meshIndex, mesh, allowLazyLoad: textureDirty);
                            if (!gpuTextureReady)
                                hasPendingTextureResources = true;

                            cache.IsValid = false;

                            // No CPU-only side pass needs animated geometry, so release stale CPU buffers.
                            if (!projectedShadowCpuBuffersRequired)
                            {
                                ReleaseCpuMeshBuffers(meshIndex);
                                continue;
                            }

                            // Keep building/updating CPU buffers below only for the projected shadow pass.
                        }
                        // GpuSkinEnabled describes attached immutable GPU geometry, not the
                        // shader selected for this particular frame. Do not clear it during
                        // temporary walk/stop blends, hover transitions or material changes.

                        // Calculate mesh-specific lighting
                        bool isBlend = IsBlendMesh(meshIndex);
                        Vector3 meshLight = needLightCalculation
                            ? (isBlend ? baseLight * blendMeshLight : baseLight * totalAlpha)
                            : cache.CachedLight;

                        // Check if this specific mesh needs update - only on real changes
                        bool meshNeedsUpdate = !cache.IsValid ||
                                             (needLightCalculation && Vector3.DistanceSquared(meshLight, cache.CachedLight) > 0.01f) ||
                                             (_invalidatedBufferFlags & (MeshDirtyFlags.Animation | MeshDirtyFlags.Transform | MeshDirtyFlags.Lighting | MeshDirtyFlags.Material | MeshDirtyFlags.Texture)) != 0;

                        if (!meshNeedsUpdate)
                            continue;

                        // Optimized color calculation with clamping - use byte directly to avoid float→int→byte conversion
                        float r = MathF.Min(colorR * meshLight.X, 255f);
                        float g = MathF.Min(colorG * meshLight.Y, 255f);
                        float b = MathF.Min(colorB * meshLight.Z, 255f);
                        Color bodyColor = new Color((byte)r, (byte)g, (byte)b);

                        bool textureReady = EnsureMeshTextureLoaded(
                            meshIndex,
                            mesh,
                            allowLazyLoad: textureDirty);
                        if (!textureReady)
                        {
                            hasPendingTextureResources = true;
                        }

                        if (CanSkipCpuDynamicBufferBuildForInstancing(
                            meshIndex,
                            mesh,
                            textureReady,
                            canUseStaticMapCpuSkip,
                            canUseWalkerCrowdCpuSkip))
                        {
                            ReleaseCpuMeshBuffers(meshIndex);
                            continue;
                        }

                        // Skip expensive buffer generation if color hasn't changed
                        bool colorChanged = cache.CachedBodyColor.PackedValue != bodyColor.PackedValue;
                        if (!colorChanged &&
                            textureReady &&
                            cache.IsValid &&
                            (_invalidatedBufferFlags & (MeshDirtyFlags.Animation | MeshDirtyFlags.Texture)) == 0)
                            continue;

                        if (bones == null)
                        {
                            bones = GetCachedBoneTransforms();
                            bones = GetRenderBoneTransforms(bones) ?? bones;
                            if (bones == null)
                            {
                                _logger?.LogDebug("SetDynamicBuffers: BoneTransform == null – skip");
                                return;
                            }
                        }

                        // Generate buffers only when necessary
                        BMDLoader.Instance.GetModelBuffers(
                            Model, meshIndex, bodyColor, bones,
                            ref _meshes[meshIndex].CpuVertexBuffer,
                            ref _meshes[meshIndex].CpuIndexBuffer,
                            skipSharedMeshCache,
                            vertexDeformer,
                            RuntimeHelpers.GetHashCode(this),
                            _animationPoseVersion);

                        cache.VertexBuffer = _meshes[meshIndex].CpuVertexBuffer;
                        cache.IndexBuffer = _meshes[meshIndex].CpuIndexBuffer;
                        cache.CachedLight = meshLight;
                        cache.CachedBodyColor = bodyColor;
                        cache.LastUpdateFrame = currentFrame;
                        cache.IsValid = true;
                    }
                    catch (Exception exMesh)
                    {
                        _logger?.LogError(exMesh, "SetDynamicBuffers - mesh {MeshIndex}", meshIndex);
                    }
                }

                // Keep texture invalidation alive until every mesh has a resolved Texture2D.
                // This prevents one-frame load races from leaving attachments (e.g. NPC wings)
                // permanently invisible.
                _invalidatedBufferFlags = hasPendingTextureResources
                    ? MeshDirtyFlags.Texture
                    : MeshDirtyFlags.None;
            }
            catch (Exception ex)
            {
                _logger?.LogCritical(ex, "SetDynamicBuffers FATAL");
            }
        }

        private void EnsureMeshRuntimeState(int meshCount)
        {
            if (_meshes?.Length == meshCount && _blendMeshIndicesScratch?.Length == meshCount)
                return;

            if (_meshes == null || _meshes.Length != meshCount)
            {
                var old = _meshes;
                _meshes = new MeshRuntimeState[meshCount];
                if (old != null)
                {
                    for (int i = 0; i < Math.Min(old.Length, meshCount); i++)
                        _meshes[i] = old[i];
                }

                for (int i = 0; i < meshCount; i++)
                    _meshes[i] ??= new MeshRuntimeState { BufferCache = new MeshBufferCache { IsValid = false } };
            }

            EnsureArraySize(ref _blendMeshIndicesScratch, meshCount);
        }

        private bool ShouldSkipDynamicBufferUpdate(int meshCount, bool allowHidden)
        {
            if (meshCount == 0)
                return true;

            if (!Visible && !allowHidden)
            {
                // Keep pending work for the loading-screen warmup or first visible
                // update. Clearing it here can leave an offscreen model unprepared.
                return true;
            }

            return false;
        }

        private bool ShouldSkipTransformOnlyBufferUpdate()
        {
            if ((_invalidatedBufferFlags & ~MeshDirtyFlags.Transform) != 0)
                return false;

            _invalidatedBufferFlags &= ~MeshDirtyFlags.Transform;
            return true;
        }

        private bool RequiresCpuProjectedShadowBuffers()
        {
            bool isNight = Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight;
            if (!RenderShadow || LowQuality || isNight)
                return false;

            bool shadowMapReady = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                  GraphicsManager.Instance.ShadowMapRenderer?.IsReady == true;
            bool representedInShadowMap = UsesRenderedShadowMapForCurrentObject();
            bool actorMeshShadow = IsPlayerOrNpcShadowPart();

            if (representedInShadowMap)
            {
                // The shadow-map pass normally consumes immutable GPU geometry. When the
                // effect lacks ShadowCaster_Skinned, keep CPU buffers as a correctness
                // fallback instead of silently dropping armor/body-part shadows.
                return !SupportsGpuSkinnedShadowCaster();
            }

            // The terrain-conformed projected-shadow path owns a compact persistent shadow
            // vertex buffer and reads animation bones directly. Do not keep a second complete
            // CPU-skinned model copy solely for this pass when GPU skinning is active.
            if (actorMeshShadow)
                return !CanUseCachedTerrainConformedShadowPath();

            // A ready map can still omit this actor because of caster limits or a stale
            // selection. Other walkers use one root blob shadow and do not need duplicate
            // CPU-skinned copies for linked equipment.
            if (shadowMapReady && (ShouldUseBlobShadowForCurrentPass() || LinkParentAnimation || ParentBoneLink >= 0))
                return false;

            if (LinkParentAnimation || ParentBoneLink >= 0)
                return false;

            // Blob shadows use a static quad and do not consume the model's CPU-skinned mesh.
            if (ShouldUseBlobShadowForCurrentPass())
                return false;

            return true;
        }

        private bool CanFullyUseWalkerCrowdInstancingForCpuSkip()
        {
            if (!CanUseWalkerCrowdInstancing() || Model?.Meshes == null)
                return false;

            bool queuedAnyMesh = false;
            int meshCount = Model.Meshes.Length;
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!CanUseWalkerCrowdMeshForInstancing(meshIndex))
                    return false;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out _))
                {
                    return false;
                }

                queuedAnyMesh = true;
            }

            return queuedAnyMesh;
        }

        private bool CanSkipCpuDynamicBufferBuildForInstancing(
            int meshIndex,
            BMDTextureMesh mesh,
            bool textureReady,
            bool canUseStaticMapCpuSkip,
            bool canUseWalkerCrowdCpuSkip)
        {
            if (!textureReady || Model == null || mesh == null)
                return false;

            if (canUseStaticMapCpuSkip &&
                CanUseStaticMapMeshForInstancing(meshIndex) &&
                BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out _))
            {
                return true;
            }

            if (canUseWalkerCrowdCpuSkip && ShouldQueueWalkerCrowdMesh(meshIndex))
                return true;

            return false;
        }

        internal async Task PrepareGpuTexturesForFirstFrameAsync()
        {
            if (!_contentLoaded || Status == GameControlStatus.Disposed || _meshes == null)
                return;

            // ModelObject.LoadContent decodes texture data off-thread. Complete the GPU upload
            // before publishing a newly spawned object so its first Draw cannot cold-create
            // Texture2D resources inside the render pass.
            for (int i = 0; i < _meshes.Length; i++)
            {
                string texturePath = _meshes[i]?.TexturePath;
                if (string.IsNullOrEmpty(texturePath))
                    continue;

                await TextureLoader.Instance.PrepareAndGetTexture(texturePath).ConfigureAwait(false);
            }

            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is ModelObject childModel)
                    await childModel.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);
            }
        }

        internal void PrepareRenderResourcesForFirstFrame()
        {
            if (!_contentLoaded || Status == GameControlStatus.Disposed)
                return;

            SetDynamicBuffers(allowHidden: true);

            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is ModelObject childModel)
                    childModel.PrepareRenderResourcesForFirstFrame();
            }
        }

        private void ReleaseCpuMeshBuffers(int meshIndex)
        {
            if (_meshes != null && (uint)meshIndex < (uint)_meshes.Length)
            {
                var ms = _meshes[meshIndex];
                if (ms.CpuVertexBuffer != null)
                {
                    DynamicBufferPool.ReturnVertexBuffer(ms.CpuVertexBuffer);
                    ms.CpuVertexBuffer = null;
                }
                if (ms.CpuIndexBuffer != null)
                {
                    DynamicBufferPool.ReturnIndexBuffer(ms.CpuIndexBuffer);
                    ms.CpuIndexBuffer = null;
                }
            }
        }

        /// <summary>
        /// Ensures that the individual main-pass renderer can use GPU skinning.
        /// Crowd instancing and individual rendering are separate paths; when a walker
        /// leaves a crowd batch (for example during an attack transition), the mesh may
        /// not yet have its per-instance GPU state attached even though shared GPU
        /// geometry already exists in BMDLoader. Reacquire it lazily at draw time.
        /// </summary>
        private bool EnsureGpuSkinnedMeshForMainPass(int meshIndex)
        {
            if (_meshes == null || Model?.Meshes == null ||
                (uint)meshIndex >= (uint)_meshes.Length ||
                (uint)meshIndex >= (uint)Model.Meshes.Length)
            {
                return false;
            }

            var ms = _meshes[meshIndex];
            if (ms.GpuSkinEnabled &&
                ms.GpuVertexBuffer != null && !ms.GpuVertexBuffer.IsDisposed &&
                ms.GpuIndexBuffer != null && !ms.GpuIndexBuffer.IsDisposed &&
                ms.GpuBoneCount > 0)
            {
                int frame = MuGame.FrameIndex;
                int age = frame - ms.LastGpuCacheTouchFrame;
                if (age < 0 || age >= 300)
                {
                    BMDLoader.Instance.TouchGpuSkinnedMeshBuffers(Model, meshIndex);
                    ms.LastGpuCacheTouchFrame = frame;
                }
                return true;
            }

            if (!CanUseGpuSkinGeometry())
                return false;

            return TryEnableGpuSkinnedMesh(
                meshIndex,
                Model.Meshes[meshIndex],
                preserveCpuBuffers: RequiresCpuProjectedShadowBuffers());
        }

        private bool TryEnableGpuSkinnedMesh(
            int meshIndex,
            BMDTextureMesh mesh,
            bool preserveCpuBuffers)
        {
            if (_meshes == null || Model == null || mesh == null ||
                (uint)meshIndex >= (uint)_meshes.Length)
                return false;

            if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                Model, meshIndex, out var vertexBuffer, out var indexBuffer, out var boneCount))
                return false;

            if (boneCount <= 0 || boneCount > MaxGpuSkinBones)
                return false;

            _meshes[meshIndex].GpuVertexBuffer = vertexBuffer;
            _meshes[meshIndex].GpuIndexBuffer = indexBuffer;
            _meshes[meshIndex].GpuBoneCount = boneCount;
            _meshes[meshIndex].GpuSkinEnabled = true;
            _meshes[meshIndex].LastGpuCacheTouchFrame = MuGame.FrameIndex;

            // Normally GPU skinning replaces CPU buffers. When projected mesh shadows are
            // active, retain/build the CPU path only for that side pass.
            if (!preserveCpuBuffers)
                ReleaseCpuMeshBuffers(meshIndex);

            return true;
        }

        private bool EnsureMeshTextureLoaded(int meshIndex, BMDTextureMesh mesh, bool allowLazyLoad)
        {
            if (_meshes == null || mesh == null || Model == null ||
                (uint)meshIndex >= (uint)_meshes.Length)
                return false;

            var ms = _meshes[meshIndex];

            Texture2D overrideTexture = ms.TextureOverride;
            if (overrideTexture != null && !overrideTexture.IsDisposed)
            {
                if (!ReferenceEquals(ms.Texture, overrideTexture))
                {
                    ms.Texture = overrideTexture;
                    InvalidateMeshRenderPlan();
                }

                return true;
            }

            string texturePath = ms.TexturePath;

            if (string.IsNullOrEmpty(texturePath))
            {
                texturePath = BMDLoader.Instance.GetTexturePath(Model, mesh.TexturePath);
                ms.TexturePath = texturePath;
            }

            if (string.IsNullOrEmpty(texturePath))
                return false;

            if (allowLazyLoad && ms.Texture == null)
                _ = TextureLoader.Instance.Prepare(texturePath);

            var resolvedTexture = TextureLoader.Instance.GetTexture2D(texturePath);
            if (!ReferenceEquals(ms.Texture, resolvedTexture))
            {
                ms.Texture = resolvedTexture;
                InvalidateMeshRenderPlan();
            }

            bool needsMetadataRefresh = allowLazyLoad || ms.Script == null || ms.Data == null;
            if (!needsMetadataRefresh)
                return ms.Texture != null;

            var script = TextureLoader.Instance.GetScript(texturePath);
            var data = TextureLoader.Instance.Get(texturePath);
            bool isRgba = data?.Components == 4;
            bool hiddenByScript = script?.HiddenMesh ?? false;
            bool blendByScript = script?.Bright ?? false;
            bool renderPlanChanged = !ReferenceEquals(ms.Script, script) ||
                                     !ReferenceEquals(ms.Data, data) ||
                                     ms.IsRgba != isRgba ||
                                     ms.HiddenByScript != hiddenByScript ||
                                     ms.BlendByScript != blendByScript;

            ms.Script = script;
            ms.Data = data;
            ms.IsRgba = isRgba;
            ms.HiddenByScript = hiddenByScript;
            ms.BlendByScript = blendByScript;

            if (renderPlanChanged)
                InvalidateMeshRenderPlan();

            return ms.Texture != null;
        }

        private Matrix[] GetCachedBoneTransforms()
        {
            Matrix[] bones = GetEffectiveBoneTransforms();
            if (bones == null) return null;

            float currentAnimTime = (float)_animTime;
            uint activePoseVersion = LinkParentAnimation && Parent is ModelObject parentModel
                ? parentModel.GetEffectiveBonePoseVersion()
                : GetEffectiveBonePoseVersion();

            // For child objects that link to parent animation OR have ParentBoneLink, always use fresh bone transforms
            // This ensures weapons and accessories animate properly during blending
            // Also always use fresh transforms for PlayerObjects to avoid rendering issues
            if (LinkParentAnimation || ParentBoneLink >= 0 || this is PlayerObject)
            {
                return bones;
            }

            // Check if we can use cached bone matrix for main objects
            // But be more conservative - only cache if animation time hasn't changed at all
            if (_boneMatrixCacheValid &&
                ReferenceEquals(_lastCachedBoneSource, bones) &&
                _lastCachedBonePoseVersion == activePoseVersion &&
                _lastCachedAction == CurrentAction &&
                Math.Abs(_lastCachedAnimTime - currentAnimTime) < 0.0001f &&
                _cachedBoneMatrix != null &&
                _cachedBoneMatrix.Length == bones.Length)
            {
                return _cachedBoneMatrix;
            }

            // Update cache
            if (_cachedBoneMatrix == null || _cachedBoneMatrix.Length != bones.Length)
            {
                _cachedBoneMatrix = new Matrix[bones.Length];
            }

            Array.Copy(bones, _cachedBoneMatrix, bones.Length);

            _lastCachedBoneSource = bones;
            _lastCachedBonePoseVersion = activePoseVersion;
            _lastCachedAction = CurrentAction;
            _lastCachedAnimTime = currentAnimTime;
            _boneMatrixCacheValid = true;

            return _cachedBoneMatrix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureArraySize<T>(ref T[] array, int size)
        {
            if (array is null || array.Length != size)
                array = new T[size];
        }

        public void InvalidateBuffers(MeshDirtyFlags flags = MeshDirtyFlags.All)
        {
            _dynamicBuffersFrozen = false;
            _invalidatedBufferFlags |= flags;
            if ((flags & MeshDirtyFlags.Texture) != 0)
            {
                _sortTextureHintDirty = true;
                _sortTextureHint = null;
            }

            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is not ModelObject modelObject ||
                    !ReferenceEquals(modelObject.Parent, this))
                {
                    continue;
                }

                MeshDirtyFlags childFlags = flags;
                bool poseDependsOnParent = modelObject.LinkParentAnimation || modelObject.ParentBoneLink >= 0;

                if (poseDependsOnParent)
                {
                    // Bone-linked parts refresh their transform from the parent pose, while
                    // LinkParentAnimation parts also invalidate animation data after observing
                    // the parent's pose version. Recursive propagation repeats the same walk.
                    childFlags &= ~(MeshDirtyFlags.Transform | MeshDirtyFlags.Animation);
                }

                if (childFlags != MeshDirtyFlags.None)
                    modelObject.InvalidateBuffers(childFlags);
            }
        }

        private void ReleaseDynamicBuffers()
        {
            var meshes = _meshes;
            if (meshes != null)
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    var ms = meshes[i];
                    if (ms == null) continue;
                    if (ms.CpuVertexBuffer != null)
                    {
                        DynamicBufferPool.ReturnVertexBuffer(ms.CpuVertexBuffer);
                        ms.CpuVertexBuffer = null;
                    }
                    if (ms.CpuIndexBuffer != null)
                    {
                        DynamicBufferPool.ReturnIndexBuffer(ms.CpuIndexBuffer);
                        ms.CpuIndexBuffer = null;
                    }
                    ref var cache = ref ms.BufferCache;
                    cache.VertexBuffer = null;
                    cache.IndexBuffer = null;
                    cache.IsValid = false;
                }
            }

            ReleaseFastMeshBatchBuffers();
        }
    }
}
