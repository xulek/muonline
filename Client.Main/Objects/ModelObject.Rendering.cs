using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private enum MeshShaderKind : byte
        {
            AlphaTest = 0,
            DynamicLighting = 1,
            ItemMaterial = 2,
            MonsterMaterial = 3,
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyQualityModelSampler(GraphicsDevice graphicsDevice)
        {
            SamplerState sampler = GraphicsManager.GetQualityLinearWrapSamplerState();
            if (!ReferenceEquals(graphicsDevice.SamplerStates[0], sampler))
                graphicsDevice.SamplerStates[0] = sampler;
        }

        // Struct to hold shader selection results
        private readonly struct ShaderSelection
        {
            public readonly bool UseDynamicLighting;
            public readonly bool UseItemMaterial;
            public readonly bool UseMonsterMaterial;
            public readonly bool NeedsSpecialShader;
            public readonly MeshShaderKind Kind;

            public ShaderSelection(bool useDynamicLighting, bool useItemMaterial, bool useMonsterMaterial)
            {
                UseDynamicLighting = useDynamicLighting;
                UseItemMaterial = useItemMaterial;
                UseMonsterMaterial = useMonsterMaterial;
                NeedsSpecialShader = useItemMaterial || useMonsterMaterial || useDynamicLighting;
                Kind = useItemMaterial
                    ? MeshShaderKind.ItemMaterial
                    : useMonsterMaterial
                        ? MeshShaderKind.MonsterMaterial
                        : useDynamicLighting
                            ? MeshShaderKind.DynamicLighting
                            : MeshShaderKind.AlphaTest;
            }
        }

        // State grouping optimization
        private readonly struct MeshStateKey : IEquatable<MeshStateKey>
        {
            public readonly Texture2D Texture;
            public readonly BlendState BlendState;
            public readonly bool TwoSided;
            public readonly MeshShaderKind ShaderKind;
            public readonly int DrawOrder;

            public MeshStateKey(Texture2D tex, BlendState blend, bool twoSided, MeshShaderKind shaderKind, int drawOrder)
            {
                Texture = tex;
                BlendState = blend;
                TwoSided = twoSided;
                ShaderKind = shaderKind;
                DrawOrder = drawOrder;
            }

            public bool Equals(MeshStateKey other) =>
                ReferenceEquals(Texture, other.Texture) &&
                ReferenceEquals(BlendState, other.BlendState) &&
                TwoSided == other.TwoSided &&
                ShaderKind == other.ShaderKind &&
                DrawOrder == other.DrawOrder;

            public override bool Equals(object obj) => obj is MeshStateKey o && Equals(o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + (Texture?.GetHashCode() ?? 0);
                    h = h * 31 + (BlendState?.GetHashCode() ?? 0);
                    h = h * 31 + (TwoSided ? 1 : 0);
                    h = h * 31 + (int)ShaderKind;
                    h = h * 31 + DrawOrder;
                    return h;
                }
            }
        }

        private sealed class FastMeshBatchBuffer
        {
            public DynamicVertexBuffer VertexBuffer;
            public DynamicIndexBuffer IndexBuffer;
            public bool IndexBufferIs16Bit;
            public int PrimitiveCount;
            public int MeshHash;
            public Color Color;
            public uint PoseVersion;
            public bool IsValid;
        }

        // Persistent render plans. Mesh classification and state grouping are rebuilt only
        // when material visibility or texture state changes, not once per object per frame.
        private readonly Dictionary<MeshStateKey, List<int>> _opaqueMeshPlan = new(32);
        private readonly Dictionary<MeshStateKey, List<int>> _transparentMeshPlan = new(32);
        private readonly Dictionary<MeshStateKey, FastMeshBatchBuffer> _fastMeshBatchBuffers = new(16);
        private readonly Stack<List<int>> _meshGroupPool = new(64);
        private uint _meshRenderPlanVersion = 1;
        private uint _builtMeshRenderPlanVersion;
        private BlendState _plannedBlendState;
        private BlendState _plannedBlendMeshState;
        private bool _plannedLowQuality;
        private bool _plannedPreserveBlendMeshes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<int> RentMeshList()
            => _meshGroupPool.Count > 0 ? _meshGroupPool.Pop() : new List<int>(8);

        private void ReleaseMeshGroupDictionary(Dictionary<MeshStateKey, List<int>> groups)
        {
            if (groups.Count == 0)
                return;

            foreach (var list in groups.Values)
            {
                list.Clear();
                if (list.Capacity > 128)
                    list.Capacity = 128;
                _meshGroupPool.Push(list);
            }

            groups.Clear();
        }

        private void ClearMeshRenderPlans()
        {
            ReleaseMeshGroupDictionary(_opaqueMeshPlan);
            ReleaseMeshGroupDictionary(_transparentMeshPlan);
            _builtMeshRenderPlanVersion = 0;
        }

        private void InvalidateMeshRenderPlan()
        {
            ReleaseFastMeshBatchBuffers();
            _builtStaticMapInstancingPlanVersion = 0;
            _staticMapInstancingMeshPlanCount = 0;
            _staticMapInstancingOpaqueMeshCount = 0;
            _staticMapInstancingPlanRetryFrame = 0;
            _staticMapInstancingPlanValidationFrame = 0;
            unchecked { _meshRenderPlanVersion++; }
            if (_meshRenderPlanVersion == 0)
                _meshRenderPlanVersion = 1;
            _sortTextureHintDirty = true;
            _sortTextureHint = null;
        }

        private Dictionary<MeshStateKey, List<int>> GetMeshRenderPlan(bool isAfterDraw)
        {
            EnsureMeshRenderPlans();
            return isAfterDraw ? _transparentMeshPlan : _opaqueMeshPlan;
        }

        private void EnsureMeshRenderPlans()
        {
            bool preserveBlendMeshes = RenderPolicy.PreserveBlendMeshesInLowQuality;
            if (_builtMeshRenderPlanVersion == _meshRenderPlanVersion &&
                ReferenceEquals(_plannedBlendState, BlendState) &&
                ReferenceEquals(_plannedBlendMeshState, BlendMeshState) &&
                _plannedLowQuality == LowQuality &&
                _plannedPreserveBlendMeshes == preserveBlendMeshes)
            {
                return;
            }

            RebuildMeshRenderPlans(preserveBlendMeshes);
        }

        private void RebuildMeshRenderPlans(bool preserveBlendMeshes)
        {
            ReleaseMeshGroupDictionary(_opaqueMeshPlan);
            ReleaseMeshGroupDictionary(_transparentMeshPlan);

            if (Model?.Meshes != null && _meshes != null)
            {
                int meshCount = Math.Min(Model.Meshes.Length, _meshes.Length);
                for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                {
                    if (IsHiddenMesh(meshIndex))
                        continue;

                    bool isBlend = IsBlendMesh(meshIndex);
                    bool isRgba = _meshes[meshIndex].IsRgba;
                    if (LowQuality && isBlend && !preserveBlendMeshes)
                        continue;

                    var target = (isRgba || isBlend) ? _transparentMeshPlan : _opaqueMeshPlan;
                    Texture2D texture = _meshes[meshIndex].Texture;
                    bool twoSided = IsMeshTwoSided(meshIndex, isBlend);
                    BlendState blend = GetMeshBlendState(meshIndex, isBlend);
                    MeshShaderKind shaderKind = DetermineShaderForMesh(meshIndex).Kind;
                    int drawOrder = shaderKind == MeshShaderKind.AlphaTest && (isRgba || isBlend)
                        ? meshIndex
                        : -1;
                    var key = new MeshStateKey(texture, blend, twoSided, shaderKind, drawOrder);

                    if (!target.TryGetValue(key, out List<int> list))
                    {
                        list = RentMeshList();
                        target.Add(key, list);
                    }

                    list.Add(meshIndex);
                }
            }

            _plannedBlendState = BlendState;
            _plannedBlendMeshState = BlendMeshState;
            _plannedLowQuality = LowQuality;
            _plannedPreserveBlendMeshes = preserveBlendMeshes;
            _builtMeshRenderPlanVersion = _meshRenderPlanVersion;
        }

        // Hint for world-level batching: returns first visible mesh texture (if any)
        internal Texture2D GetSortTextureHint()
        {
            if (!_sortTextureHintDirty)
                return _sortTextureHint;

            _sortTextureHintDirty = false;
            _sortTextureHint = null;

            if (_meshes == null)
                return null;

            for (int i = 0; i < _meshes.Length; i++)
            {
                var tex = _meshes[i].Texture;
                if (tex != null && !IsHiddenMesh(i))
                {
                    _sortTextureHint = tex;
                    break;
                }
            }

            return _sortTextureHint;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsStaticMapMeshQueuedForInstancing(int mesh)
        {
            int[] frameTags = _staticMapInstancedMeshFrameTags;
            if (frameTags == null || (uint)mesh >= (uint)frameTags.Length)
                return false;

            return frameTags[mesh] == MuGame.FrameIndex + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BlendState GetMeshBlendState(int mesh, bool isBlendMesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return isBlendMesh ? BlendMeshState : BlendState;

            var meshConf = Model.Meshes[mesh];

            // Check for custom blend state from JSON config
            if (meshConf.BlendingMode != null && _blendStateCache.TryGetValue(meshConf.BlendingMode, out var customBlendState))
                return customBlendState;

            // Cache custom blend states dynamically
            if (meshConf.BlendingMode != null && meshConf.BlendingMode != "Opaque")
            {
                var field = typeof(Blendings).GetField(meshConf.BlendingMode, BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    customBlendState = (BlendState)field.GetValue(null);
                    _blendStateCache[meshConf.BlendingMode] = customBlendState;
                    return customBlendState;
                }
            }

            // Default to instance properties which can be changed dynamically by code
            // IMPORTANT: Use instance properties, not cached states, as they can be modified at runtime!
            return isBlendMesh ? BlendMeshState : BlendState;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsMeshTwoSided(int mesh, bool isBlendMesh)
        {
            if (ForceTwoSidedMeshes)
                return true;

            if (_meshes == null || mesh < 0 || mesh >= _meshes.Length)
                return false;

            if (_meshes[mesh].IsRgba || isBlendMesh)
                return true;

            if (Model?.Meshes != null && mesh < Model.Meshes.Length)
            {
                var meshConf = Model.Meshes[mesh];
                return meshConf.BlendingMode != null && meshConf.BlendingMode != "Opaque";
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsTransparentMesh(int mesh, bool isBlendMesh)
        {
            if (isBlendMesh)
                return true;

            return _meshes != null && (uint)mesh < (uint)_meshes.Length && _meshes[mesh].IsRgba;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool ShouldRenderMesh(int mesh)
        {
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsHiddenMesh(int mesh)
        {
            if (_meshes == null || (uint)mesh >= (uint)_meshes.Length)
                return false;

            return !ShouldRenderMesh(mesh) ||
                   HiddenMesh == mesh || HiddenMesh == -2 || _meshes[mesh].HiddenByScript;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool IsBlendMesh(int mesh)
        {
            if (_meshes == null || (uint)mesh >= (uint)_meshes.Length)
                return false;

            if (BlendMesh == -2 || _meshes[mesh].BlendByScript)
                return true;

            // SourceMain5.2 BMD::RenderMesh parity: the blend pass triggers when the
            // mesh's TEXTURE slot equals BlendMesh ("m->Texture == blendMeshIndex"),
            // not when the mesh index equals it.
            if (BlendMesh >= 0 && Model != null && (uint)mesh < (uint)Model.Meshes.Length)
                return Model.Meshes[mesh].Texture == BlendMesh;

            return false;
        }

        /// <summary>
        /// Gets depth bias for different object types to reduce Z-fighting
        /// </summary>
        protected virtual float GetDepthBias()
        {
            // Small bias values - negative values bring objects closer to camera
            var objectType = GetType();

            if (objectType == typeof(PlayerObject))
                return -0.00001f;  // Players slightly closer
            if (objectType == typeof(DroppedItemObject))
                return -0.00002f;  // Items even closer
            if (objectType == typeof(NPCObject))
                return -0.000005f; // NPCs slightly closer than terrain

            return 0f; // Default - no bias for terrain and other objects
        }

        /// <summary>
        /// Determines if item material effect should be applied to a specific mesh
        /// </summary>
        protected virtual bool ShouldApplyItemMaterial(int meshIndex)
        {
            // By default, apply to all meshes
            // Override in specific classes to exclude certain meshes
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ShaderSelection DetermineShaderForMesh(int mesh)
        {
            // Item material shader (for excellent/ancient/high level items)
            bool useItemMaterial = Constants.ENABLE_ITEM_MATERIAL_SHADER &&
                                   (ItemLevel >= 7 || IsExcellentItem || IsAncientItem) &&
                                   GraphicsManager.Instance.ItemMaterialEffect != null &&
                                   ShouldApplyItemMaterial(mesh);

            // Monster material shader
            bool useMonsterMaterial = Constants.ENABLE_MONSTER_MATERIAL_SHADER &&
                                      EnableCustomShader &&
                                      GraphicsManager.Instance.MonsterMaterialEffect != null;

            // Dynamic lighting shader (used when no special material is active)
            bool useDynamicLighting = AllowDynamicLightingShader &&
                                      !useItemMaterial && !useMonsterMaterial &&
                                      Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                      GraphicsManager.Instance.DynamicLightingEffect != null;

            return new ShaderSelection(useDynamicLighting, useItemMaterial, useMonsterMaterial);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanUseGpuSkinGeometry()
        {
            return SupportsGpuDynamicSkinning &&
                   Constants.ENABLE_GPU_SKINNING &&
                   AllowGpuSkinning &&
                   !UsesMutableMeshData &&
                   GetVertexDeformer() == null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanUseGpuSkinningForMesh(int mesh)
        {
            if (!CanUseGpuSkinGeometry())
                return false;

            ShaderSelection selection = DetermineShaderForMesh(mesh);
#if NATIVE_BACKEND
            // Native SM6 material effects currently lose meshes in their BoneMatrices
            // techniques. Keep the material pixel shader, but feed it CPU-skinned geometry.
            if (selection.UseItemMaterial || selection.UseMonsterMaterial)
                return false;
#endif
            if (selection.UseItemMaterial)
            {
                return TryGetTechnique(
                    GraphicsManager.Instance.ItemMaterialEffect,
                    "BasicColorDrawing_Skinned") != null;
            }

            if (selection.UseMonsterMaterial)
            {
                return TryGetTechnique(
                    GraphicsManager.Instance.MonsterMaterialEffect,
                    "MonsterMaterialDrawing_Skinned") != null;
            }

            return selection.UseDynamicLighting &&
                   TryGetTechnique(
                       GraphicsManager.Instance.DynamicLightingEffect,
                       "DynamicLighting_Skinned") != null;
        }

        private bool TryResolveMaterialMeshBuffers(
            int mesh,
            Effect effect,
            string baseTechniqueName,
            string skinnedTechniqueName,
            out VertexBuffer vertexBuffer,
            out IndexBuffer indexBuffer,
            out bool usingGpuSkinning)
        {
            vertexBuffer = null;
            indexBuffer = null;
            usingGpuSkinning = false;

            EffectTechnique baseTechnique = TryGetTechnique(effect, baseTechniqueName) ??
                                            (effect != null && effect.Techniques.Count > 0
                                                ? effect.Techniques[0]
                                                : null);
            if (baseTechnique == null)
                return false;

            if (CanUseGpuSkinningForMesh(mesh) && EnsureGpuSkinnedMeshForMainPass(mesh))
            {
                var state = _meshes[mesh];
                EffectTechnique skinnedTechnique = TryGetTechnique(effect, skinnedTechniqueName);
                if (skinnedTechnique != null &&
                    state.GpuVertexBuffer != null && !state.GpuVertexBuffer.IsDisposed &&
                    state.GpuIndexBuffer != null && !state.GpuIndexBuffer.IsDisposed &&
                    TryUploadGpuSkinBoneMatrices(effect, state.GpuBoneCount))
                {
                    effect.CurrentTechnique = skinnedTechnique;
                    vertexBuffer = state.GpuVertexBuffer;
                    indexBuffer = state.GpuIndexBuffer;
                    usingGpuSkinning = true;
                    return true;
                }
            }

            effect.CurrentTechnique = baseTechnique;
            vertexBuffer = _meshes?[mesh]?.CpuVertexBuffer;
            indexBuffer = _meshes?[mesh]?.CpuIndexBuffer;
            return vertexBuffer != null && !vertexBuffer.IsDisposed &&
                   indexBuffer != null && !indexBuffer.IsDisposed;
        }

        // Determines if this mesh needs special shader path and cannot use fast alpha path
        private bool NeedsSpecialShaderForMesh(int mesh)
        {
            return DetermineShaderForMesh(mesh).NeedsSpecialShader;
        }

        private bool DrawRootBlobShadowIfNeeded(
            bool isAfterDraw,
            Matrix view,
            Matrix projection,
            Vector3 worldPosition)
        {
            bool representedInShadowMap = UsesRenderedShadowMapForCurrentObject();
            bool persistentActorShadow = RequiresPersistentActorGroundShadow();
            bool needsSafetyBlob = representedInShadowMap &&
                                   (persistentActorShadow || NeedsModularShadowSafetyBlob());

            if (isAfterDraw ||
                !RenderShadow ||
                (LowQuality && !persistentActorShadow) ||
                (representedInShadowMap && !needsSafetyBlob) ||
                (Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight) ||
                !ShouldUseBlobShadowForCurrentPass() ||
                !TryGetGroundBlobShadowMatrix(out Matrix shadowMatrix))
            {
                return false;
            }

            // Modular player/NPC actors can temporarily have only a subset of body-part
            // casters ready. Keep a subtle root footprint under the detailed shadow-map
            // result so the shadow never degenerates to a single helmet or weapon.
            float shadowOpacity;
            if (!representedInShadowMap)
            {
                shadowOpacity = ShadowOpacity;
            }
            else if (persistentActorShadow)
            {
                // Keep player/NPC grounding clearly visible even when the detailed shadow map
                // contains only a subset of modular body parts.
                shadowOpacity = ShadowOpacity * 0.65f;
            }
            else
            {
                shadowOpacity = ShadowOpacity * 0.35f;
            }
            if (World?.Terrain != null)
            {
                var dyn = World.Terrain.EvaluateDynamicLight(
                    new Vector2(worldPosition.X, worldPosition.Y));
                float lum = (0.2126f * dyn.X + 0.7152f * dyn.Y + 0.0722f * dyn.Z) / 255f;
                shadowOpacity *= MathHelper.Clamp(1f - lum * 0.6f, 0.35f, 1f);
            }

            DrawBlobShadow(view, projection, shadowMatrix, shadowOpacity);
            return true;
        }

        private void DrawProjectedShadowPass(
            List<int> meshIndices,
            bool doShadow,
            bool useShadowMap,
            Matrix shadowMatrix,
            Matrix view,
            Matrix projection,
            float shadowOpacity,
            ref bool drewBlobShadow)
        {
            if (!doShadow || useShadowMap)
                return;

            // Non-actor attachments can share the parent's cheap fallback. Player/NPC body
            // parts, however, form the actor silhouette and must participate when the root
            // was omitted from the shadow map.
            if ((LinkParentAnimation || ParentBoneLink >= 0) && !IsPlayerOrNpcShadowPart())
                return;

            if (ShouldUseBlobShadowForCurrentPass())
            {
                if (!drewBlobShadow && TryGetGroundBlobShadowMatrix(out Matrix blobShadowMatrix))
                {
                    DrawBlobShadow(view, projection, blobShadowMatrix, shadowOpacity);
                    drewBlobShadow = true;
                }
            }
            else
            {
                DrawMeshesShadow(meshIndices, shadowMatrix, view, projection, shadowOpacity);
            }
        }

        internal void DrawQueuedCrowdInstancingSidePasses(GameTime gameTime)
        {
            if (!Visible)
                return;

            DrawBoundingBox3D();
            SetDrawShaderTimeSeconds((float)gameTime.TotalGameTime.TotalSeconds);

            if (Model?.Meshes != null && _meshes != null)
            {
                var view = Camera.Instance.View;
                var projection = Camera.Instance.Projection;
                var worldPos = WorldPosition;

                bool useShadowMap = UsesRenderedShadowMapForCurrentObject();
                bool isNight = Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight;
                bool doShadow = false;
                Matrix shadowMatrix = Matrix.Identity;
                if (RenderShadow && !LowQuality && !useShadowMap && !isNight)
                    doShadow = TryGetShadowMatrix(out shadowMatrix);

                bool highlightAllowed = !LowQuality && IsMouseHover &&
                                        !(this is MonsterObject monster && monster.IsDead);
                Matrix highlightMatrix = Matrix.Identity;
                Vector3 highlightColor = Vector3.One;
                if (highlightAllowed)
                {
                    const float scaleHighlight = 0.015f;
                    const float scaleFactor = 1f + scaleHighlight;
                    highlightMatrix = Matrix.CreateScale(scaleFactor) *
                                      Matrix.CreateTranslation(-scaleHighlight, -scaleHighlight, -scaleHighlight) *
                                      worldPos;
                    highlightColor = this is MonsterObject ? _redHighlight : _greenHighlight;
                }

                bool rootBlobDrawn = DrawRootBlobShadowIfNeeded(
                    isAfterDraw: false,
                    view,
                    projection,
                    worldPos.Translation);

                if (doShadow || highlightAllowed)
                {
                    float shadowOpacity = ShadowOpacity;
                    if (doShadow && World?.Terrain != null)
                    {
                        var dyn = World.Terrain.EvaluateDynamicLight(
                            new Vector2(worldPos.Translation.X, worldPos.Translation.Y));
                        float lum = (0.2126f * dyn.X + 0.7152f * dyn.Y + 0.0722f * dyn.Z) / 255f;
                        shadowOpacity *= MathHelper.Clamp(1f - lum * 0.6f, 0.35f, 1f);
                    }

                    var meshGroups = GroupMeshesByState(false);
                    {
                        bool drewBlobShadow = rootBlobDrawn;
                        foreach (var kvp in meshGroups)
                        {
                            if (kvp.Value.Count == 0)
                                continue;

                            if (doShadow)
                            {
                                DrawProjectedShadowPass(
                                    kvp.Value,
                                    doShadow,
                                    useShadowMap,
                                    shadowMatrix,
                                    view,
                                    projection,
                                    shadowOpacity,
                                    ref drewBlobShadow);
                            }

                            if (highlightAllowed)
                                DrawMeshesHighlight(kvp.Value, highlightMatrix, highlightColor);
                        }
                    }
                }
            }

            DrawChildrenOnly(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            SetDrawShaderTimeSeconds((float)gameTime.TotalGameTime.TotalSeconds);

            // Modular actors can temporarily have no drawable root mesh while their body
            // parts are already ready. Do not suppress children or the stable ground shadow
            // just because the root model has no buffer set.
            if (_meshes == null || Model?.Meshes == null)
            {
                DrawRootBlobShadowIfNeeded(
                    isAfterDraw: false,
                    Camera.Instance.View,
                    Camera.Instance.Projection,
                    WorldPosition.Translation);
                base.Draw(gameTime);
                return;
            }

            var gd = GraphicsDevice;
            var prevCull = gd.RasterizerState;
            gd.RasterizerState = _cullClockwise;

            GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
            GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
            GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;

            DrawModel(false);   // solid pass
            base.Draw(gameTime);

            gd.RasterizerState = prevCull;
        }

        public virtual void DrawModel(bool isAfterDraw)
        {
            if (Model?.Meshes == null || _meshes == null)
                return;

            int meshCount = Model.Meshes.Length;
            if (meshCount == 0)
                return;

            _drawModelInvocationId = ++_drawModelInvocationCounter;

            // Cache commonly used values before resolving mesh groups. Modular player-model
            // NPCs can have no visible root mesh at all; their root blob must still be drawn.
            var view = Camera.Instance.View;
            var projection = Camera.Instance.Projection;
            var worldPos = WorldPosition;
            bool rootBlobDrawn = DrawRootBlobShadowIfNeeded(
                isAfterDraw,
                view,
                projection,
                worldPos.Translation);

            // Resolve the persistent plan before touching the remaining graphics state.
            var meshGroups = GroupMeshesByState(isAfterDraw);
            if (meshGroups.Count == 0)
                return;

            // Pre-calculate shadow and highlight states at object level
            bool doShadow = false;
            Matrix shadowMatrix = Matrix.Identity;
            bool useShadowMap = UsesRenderedShadowMapForCurrentObject();
            // Skip blob shadows at night when day-night cycle is active
            bool isNight = Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight;
            if (!isAfterDraw && RenderShadow && !LowQuality && !useShadowMap && !isNight)
                doShadow = TryGetShadowMatrix(out shadowMatrix);
            float shadowOpacity = ShadowOpacity;
            if (doShadow && World?.Terrain != null)
            {
                // Fade blob shadow slightly in strong local light so ground illumination stays visible.
                var dyn = World.Terrain.EvaluateDynamicLight(new Vector2(worldPos.Translation.X, worldPos.Translation.Y));
                float lum = (0.2126f * dyn.X + 0.7152f * dyn.Y + 0.0722f * dyn.Z) / 255f;
                shadowOpacity *= MathHelper.Clamp(1f - lum * 0.6f, 0.35f, 1f);
            }

            bool highlightAllowed = !isAfterDraw && !LowQuality && IsMouseHover &&
                                   !(this is MonsterObject m && m.IsDead);
            Matrix highlightMatrix = Matrix.Identity;
            Vector3 highlightColor = Vector3.One;

            if (highlightAllowed)
            {
                const float scaleHighlight = 0.015f;
                const float scaleFactor = 1f + scaleHighlight;
                highlightMatrix = Matrix.CreateScale(scaleFactor) *
                    Matrix.CreateTranslation(-scaleHighlight, -scaleHighlight, -scaleHighlight) *
                    worldPos;
                highlightColor = this is MonsterObject ? _redHighlight : _greenHighlight;
            }

            // Render each persistent group with minimal state changes.
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.AlphaTestEffect3D;
                // Object-level alpha is constant; set once for the pass
                if (effect != null && effect.Alpha != TotalAlpha)
                    effect.Alpha = TotalAlpha;
                bool drewBlobShadow = rootBlobDrawn;

                foreach (var kvp in meshGroups)
                {
                    var stateKey = kvp.Key;
                    var meshIndices = kvp.Value;
                    if (meshIndices.Count == 0) continue;

                    // Apply render state once per group (with object depth bias)
                    if (gd.BlendState != stateKey.BlendState)
                        gd.BlendState = stateKey.BlendState;
                    float depthBias = GetDepthBias();
                    RasterizerState targetRasterizer;
                    if (depthBias != 0f)
                    {
                        var cm = stateKey.TwoSided ? CullMode.None : CullMode.CullClockwiseFace;
                        targetRasterizer = GraphicsManager.GetCachedRasterizerState(depthBias, cm);
                    }
                    else
                    {
                        targetRasterizer = stateKey.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    }
                    if (gd.RasterizerState != targetRasterizer)
                        gd.RasterizerState = targetRasterizer;
                    if (effect != null && effect.Texture != stateKey.Texture)
                        effect.Texture = stateKey.Texture;
                    // Bind effect once per group. Rebind the sampler afterwards because
                    // Effect.Apply can restore the sampler state embedded in the shader.
                    if (effect != null)
                    {
                        var passes = effect.CurrentTechnique.Passes;
                        for (int p = 0; p < passes.Count; p++)
                            passes[p].Apply();
                        ApplyQualityModelSampler(gd);
                    }

                    // Object-level shadow and highlight passes
                    DrawProjectedShadowPass(
                        meshIndices,
                        doShadow,
                        useShadowMap,
                        shadowMatrix,
                        view,
                        projection,
                        shadowOpacity,
                        ref drewBlobShadow);
                    if (highlightAllowed)
                        DrawMeshesHighlight(meshIndices, highlightMatrix, highlightColor);

                    // Shadow/highlight passes change the active shader; reapply the main effect before fast draws.
                    if (effect != null)
                    {
                        var passes = effect.CurrentTechnique.Passes;
                        for (int p = 0; p < passes.Count; p++)
                            passes[p].Apply();
                        ApplyQualityModelSampler(gd);
                    }

                    // Draw all meshes in this state group
                    // When dynamic lighting is disabled and blend state is non-opaque, force per-mesh path
                    // to ensure proper DepthStencilState handling and BasicEffect usage for alpha blending
                    bool forcePerMeshTransparency = !Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                                    stateKey.BlendState != BlendState.Opaque;
                    if (!forcePerMeshTransparency &&
                        TryDrawGpuSkinnedMeshBatch(stateKey, meshIndices, isAfterDraw))
                    {
                        continue;
                    }

                    if (!forcePerMeshTransparency &&
                        TryDrawFastAlphaMeshBatch(stateKey, meshIndices, isAfterDraw))
                    {
                        continue;
                    }

                    for (int n = 0; n < meshIndices.Count; n++)
                    {
                        int mi = meshIndices[n];
                        if (NeedsSpecialShaderForMesh(mi) || forcePerMeshTransparency)
                        {
                            DrawMesh(mi); // Falls back to full per-mesh path for special shaders or forced transparency

                            // Per-mesh draws can change the active shader; reapply the group effect
                            // before any fast draws that follow.
                            if (!forcePerMeshTransparency && effect != null)
                            {
                                var passes = effect.CurrentTechnique.Passes;
                                for (int p = 0; p < passes.Count; p++)
                                    passes[p].Apply();
                                ApplyQualityModelSampler(gd);
                            }
                        }
                        else
                        {
                            DrawMeshFastAlpha(mi); // Fast path: VB/IB bind + draw only
                        }
                    }
                }
            }
        }

        private bool TryDrawGpuSkinnedMeshBatch(MeshStateKey stateKey, List<int> meshIndices, bool isAfterDraw)
        {
            if (_gpuSkinnedMeshBatchingFailed ||
                !Constants.ENABLE_BMD_MESH_BATCHING ||
                stateKey.ShaderKind == MeshShaderKind.AlphaTest ||
                meshIndices == null || meshIndices.Count <= 1 ||
                Model?.Meshes == null || _meshes == null ||
                !CanUseGpuSkinGeometry())
            {
                return false;
            }

            for (int i = 0; i < meshIndices.Count; i++)
            {
                int meshIndex = meshIndices[i];
                if ((uint)meshIndex >= (uint)Model.Meshes.Length ||
                    (uint)meshIndex >= (uint)_meshes.Length ||
                    IsHiddenMesh(meshIndex) ||
                    IsStaticMapMeshQueuedForInstancing(meshIndex) ||
                    !ReferenceEquals(_meshes[meshIndex].Texture, stateKey.Texture) ||
                    DetermineShaderForMesh(meshIndex).Kind != stateKey.ShaderKind ||
                    !CanUseGpuSkinningForMesh(meshIndex))
                {
                    return false;
                }
            }

            var gd = GraphicsDevice;
            DepthStencilState previousDepth = gd.DepthStencilState;
            bool useReadOnlyDepth = isAfterDraw || !ReferenceEquals(stateKey.BlendState, BlendState.Opaque);

            try
            {
                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBatchBuffers(
                    Model,
                    meshIndices,
                    out VertexBuffer vertexBuffer,
                    out IndexBuffer indexBuffer,
                    out int boneCount))
                {
                    return false;
                }

                Effect effect = PrepareGpuSkinnedBatchEffect(stateKey, boneCount);
                if (effect?.CurrentTechnique == null)
                    return false;

                if (useReadOnlyDepth)
                    gd.DepthStencilState = GraphicsManager.ReadOnlyDepth;

                gd.SetVertexBuffer(vertexBuffer);
                gd.Indices = indexBuffer;
                int primitiveCount = indexBuffer.IndexCount / 3;
                int passCount = effect.CurrentTechnique.Passes.Count;
                for (int passIndex = 0; passIndex < passCount; passIndex++)
                {
                    effect.CurrentTechnique.Passes[passIndex].Apply();
                    ApplyQualityModelSampler(gd);
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                }

                RegisterGpuSkinnedMeshDraw(meshIndices.Count);
                RegisterGpuSkinnedBatchDraw(meshIndices.Count);
                return true;
            }
            catch (Exception ex)
            {
                _gpuSkinnedMeshBatchingFailed = true;
                _logger?.LogWarning(ex, "Combined GPU-skinned mesh batching disabled after runtime failure.");
                return false;
            }
            finally
            {
                if (useReadOnlyDepth)
                    gd.DepthStencilState = previousDepth;
            }
        }

        private Effect PrepareGpuSkinnedBatchEffect(MeshStateKey stateKey, int boneCount)
        {
            return stateKey.ShaderKind switch
            {
                MeshShaderKind.DynamicLighting => PrepareDynamicLightingGpuBatch(stateKey.Texture, boneCount),
                MeshShaderKind.ItemMaterial => PrepareItemMaterialGpuBatch(stateKey.Texture, boneCount),
                MeshShaderKind.MonsterMaterial => PrepareMonsterMaterialGpuBatch(stateKey.Texture, boneCount),
                _ => null,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDynamicLightingSkinnedTechnique(EffectTechnique technique)
        {
            string name = technique?.Name;
            return string.Equals(name, "DynamicLighting_Skinned", StringComparison.Ordinal) ||
                   string.Equals(name, "DynamicLighting_Skinned_VertexLit", StringComparison.Ordinal) ||
                   string.Equals(name, "DynamicLighting_Skinned_SunOnly", StringComparison.Ordinal);
        }

        private Effect PrepareDynamicLightingGpuBatch(Texture2D texture, int boneCount)
        {
            Effect effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return null;

            PrepareDynamicLightingEffect(effect, useGpuSkinning: true, requiredBoneCount: boneCount);
            if (!IsDynamicLightingSkinnedTechnique(effect.CurrentTechnique))
                return null;

            GetModelEffectBindings(effect)?.DiffuseTexture?.SetValue(texture);
            return effect;
        }

        private void ApplyItemMaterialUpgradeBindings(ModelEffectBindings bindings)
        {
            if (bindings == null)
                return;

            GraphicsManager graphics = GraphicsManager.Instance;
            Texture2D fallback = graphics.BlackPixel ?? graphics.Pixel;

            bindings.Chrome02Texture?.SetValue(graphics.ItemChrome02Texture ?? fallback);
            bindings.Shiny01Texture?.SetValue(graphics.ItemShiny01Texture ?? fallback);
            bindings.Chrome01Texture?.SetValue(graphics.ItemChrome01Texture ?? fallback);
            bindings.ItemMaterialGroup?.SetValue(MaterialItemGroup);
            bindings.ItemMaterialIndex?.SetValue(MaterialItemIndex);
            bindings.HighLevelTexturesAvailable?.SetValue(
                graphics.HasItemUpgradeTextures ? 1f : 0f);
        }

        private bool CanUseFastItemUpgradeShader()
        {
            if (!Constants.ENABLE_FAST_MATERIAL_SHADERS || IsAncientItem || IsExcellentItem)
                return false;

            bool highLevelArmor = GraphicsManager.Instance.HasItemUpgradeTextures &&
                                  MaterialItemGroup >= 7 && MaterialItemGroup <= 11 &&
                                  ItemLevel >= 11;
            return ItemLevel >= 7 && !highLevelArmor;
        }

        private bool CanUseFastMonsterMaterialShader() =>
            Constants.ENABLE_FAST_MATERIAL_SHADERS &&
            GlowIntensity <= 0.0f &&
            !SimpleColorMode;

        private void ResolveItemMaterialTechniqueNames(out string baseTechnique, out string skinnedTechnique)
        {
            if (CanUseFastItemUpgradeShader())
            {
                baseTechnique = "BasicColorDrawing_UpgradeFast";
                skinnedTechnique = "BasicColorDrawing_UpgradeFast_Skinned";
                return;
            }

            baseTechnique = "BasicColorDrawing";
            skinnedTechnique = "BasicColorDrawing_Skinned";
        }

        private void ResolveMonsterMaterialTechniqueNames(out string baseTechnique, out string skinnedTechnique)
        {
            if (CanUseFastMonsterMaterialShader())
            {
                baseTechnique = "MonsterMaterialDrawing_Basic";
                skinnedTechnique = "MonsterMaterialDrawing_Basic_Skinned";
                return;
            }

            baseTechnique = "MonsterMaterialDrawing";
            skinnedTechnique = "MonsterMaterialDrawing_Skinned";
        }

        private Effect PrepareItemMaterialGpuBatch(Texture2D texture, int boneCount)
        {
            Effect effect = GraphicsManager.Instance.ItemMaterialEffect;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            bool fastItemShader = CanUseFastItemUpgradeShader();
            string skinnedTechniqueName = fastItemShader
                ? "BasicColorDrawing_UpgradeFast_Skinned"
                : "BasicColorDrawing_Skinned";
            EffectTechnique technique = bindings?.GetTechnique(skinnedTechniqueName);
            if (effect == null || bindings == null || technique == null ||
                !TryUploadGpuSkinBoneMatrices(effect, bindings, boneCount))
            {
                return null;
            }

            effect.CurrentTechnique = technique;
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

            var camera = Camera.Instance;
            Matrix world = WorldPosition;
            bindings.WorldViewProjection?.SetValue(world * camera.ViewProjection);
            bindings.World?.SetValue(world);
            if (!fastItemShader)
                bindings.EyePosition?.SetValue(camera.Position);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);
            bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
            bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();
            bindings.LightDirection?.SetValue(sunDir);
            bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);
            bindings.DiffuseTexture?.SetValue(texture);
            if (!fastItemShader)
                ApplyItemMaterialUpgradeBindings(bindings);

            int itemOptions = ItemLevel & 0x0F;
            if (IsExcellentItem)
                itemOptions |= 0x10;
            bindings.ItemOptions?.SetValue(itemOptions);
            bindings.Time?.SetValue(GetShaderTimeSeconds());
            if (!fastItemShader)
            {
                bindings.IsAncient?.SetValue(IsAncientItem);
                bindings.IsExcellent?.SetValue(IsExcellentItem);
            }
            bindings.Alpha?.SetValue(TotalAlpha);
            return effect;
        }

        private Effect PrepareMonsterMaterialGpuBatch(Texture2D texture, int boneCount)
        {
            Effect effect = GraphicsManager.Instance.MonsterMaterialEffect;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            bool fastMonsterShader = CanUseFastMonsterMaterialShader();
            string skinnedTechniqueName = fastMonsterShader
                ? "MonsterMaterialDrawing_Basic_Skinned"
                : "MonsterMaterialDrawing_Skinned";
            EffectTechnique technique = bindings?.GetTechnique(skinnedTechniqueName);
            if (effect == null || bindings == null || technique == null ||
                !TryUploadGpuSkinBoneMatrices(effect, bindings, boneCount))
            {
                return null;
            }

            effect.CurrentTechnique = technique;
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

            var camera = Camera.Instance;
            Matrix world = WorldPosition;
            bindings.WorldViewProjection?.SetValue(world * camera.ViewProjection);
            bindings.World?.SetValue(world);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);
            bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
            bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();
            bindings.LightDirection?.SetValue(sunDir);
            bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);
            bindings.DiffuseTexture?.SetValue(texture);
            if (!fastMonsterShader)
            {
                bindings.GlowColor?.SetValue(GlowColor);
                bindings.GlowIntensity?.SetValue(GlowIntensity);
                bindings.EnableGlow?.SetValue(GlowIntensity > 0f && !SimpleColorMode);
                bindings.SimpleColorMode?.SetValue(SimpleColorMode);
                bindings.Time?.SetValue(GetShaderTimeSeconds());
            }
            bindings.Alpha?.SetValue(TotalAlpha);
            return effect;
        }

        private bool TryDrawFastAlphaMeshBatch(MeshStateKey stateKey, List<int> meshIndices, bool isAfterDraw)
        {
            if (!Constants.ENABLE_BMD_MESH_BATCHING ||
                isAfterDraw ||
                meshIndices == null ||
                meshIndices.Count <= 1 ||
                Model?.Meshes == null ||
                RequiresPerFrameAnimation ||
                HasAnimatedCurrentAction() ||
                GetVertexDeformer() != null ||
                UsesMutableMeshData)
            {
                return false;
            }

            if (!CanBatchFastAlphaMeshes(stateKey, meshIndices))
                return false;

            Matrix[] bones = GetCachedBoneTransforms();
            bones = GetRenderBoneTransforms(bones) ?? bones;
            if (bones == null || bones.Length == 0)
                return false;

            if (!TryResolveOpaqueBodyColor(out Color bodyColor))
                return false;

            int meshHash = CalculateMeshListHash(meshIndices);
            if (!_fastMeshBatchBuffers.TryGetValue(stateKey, out var batch))
            {
                batch = new FastMeshBatchBuffer();
                _fastMeshBatchBuffers[stateKey] = batch;
            }

            if (!batch.IsValid ||
                batch.MeshHash != meshHash ||
                batch.PoseVersion != _animationPoseVersion ||
                batch.Color.PackedValue != bodyColor.PackedValue ||
                batch.VertexBuffer == null ||
                batch.VertexBuffer.IsDisposed ||
                batch.IndexBuffer == null ||
                batch.IndexBuffer.IsDisposed)
            {
                if (!BMDLoader.Instance.GetModelBatchBuffers(
                    Model,
                    meshIndices,
                    bodyColor,
                    bones,
                    ref batch.VertexBuffer,
                    ref batch.IndexBuffer,
                    ref batch.IndexBufferIs16Bit))
                {
                    batch.IsValid = false;
                    return false;
                }

                batch.PrimitiveCount = batch.IndexBuffer.IndexCount / 3;
                batch.MeshHash = meshHash;
                batch.PoseVersion = _animationPoseVersion;
                batch.Color = bodyColor;
                batch.IsValid = true;
            }

            var gd = GraphicsDevice;
            gd.SetVertexBuffer(batch.VertexBuffer);
            gd.Indices = batch.IndexBuffer;
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.PrimitiveCount);
            RegisterModelFallbackDrawCall();
            return true;
        }

        private bool CanBatchFastAlphaMeshes(MeshStateKey stateKey, List<int> meshIndices)
        {
            for (int i = 0; i < meshIndices.Count; i++)
            {
                int meshIndex = meshIndices[i];
                if ((uint)meshIndex >= (uint)Model.Meshes.Length ||
                    IsHiddenMesh(meshIndex) ||
                    IsStaticMapMeshQueuedForInstancing(meshIndex) ||
                    IsBlendMesh(meshIndex) ||
                    NeedsSpecialShaderForMesh(meshIndex) ||
                    _meshes != null && (uint)meshIndex < (uint)_meshes.Length && _meshes[meshIndex].IsRgba ||
                    _meshes == null ||
                    (uint)meshIndex >= (uint)_meshes.Length ||
                    !ReferenceEquals(_meshes[meshIndex].Texture, stateKey.Texture))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryResolveOpaqueBodyColor(out Color bodyColor)
        {
            Vector3 meshLight = Light;
            Vector3 worldTranslation = WorldPosition.Translation;
            if (LightEnabled && World?.Terrain != null)
                meshLight = EvaluateCombinedTerrainLight(worldTranslation.X, worldTranslation.Y) + Light;

            float r = MathF.Min(Color.R * (meshLight.X * TotalAlpha), 255f);
            float g = MathF.Min(Color.G * (meshLight.Y * TotalAlpha), 255f);
            float b = MathF.Min(Color.B * (meshLight.Z * TotalAlpha), 255f);
            bodyColor = new Color((byte)r, (byte)g, (byte)b);
            return true;
        }

        private static int CalculateMeshListHash(List<int> meshIndices)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < meshIndices.Count; i++)
                    hash = hash * 31 + meshIndices[i];
                return hash;
            }
        }

        private void ReleaseFastMeshBatchBuffers()
        {
            foreach (var batch in _fastMeshBatchBuffers.Values)
            {
                DynamicBufferPool.ReturnVertexBuffer(batch.VertexBuffer);
                DynamicBufferPool.ReturnIndexBuffer(batch.IndexBuffer);
                batch.VertexBuffer = null;
                batch.IndexBuffer = null;
                batch.IsValid = false;
            }

            _fastMeshBatchBuffers.Clear();
        }

        // Fast path draw for standard alpha-tested meshes (no special shaders)
        private void DrawMeshFastAlpha(int mesh)
        {
            if (_meshes == null || mesh >= _meshes.Length)
                return;
            if (_meshes[mesh].CpuVertexBuffer == null ||
                _meshes[mesh].CpuIndexBuffer == null ||
                _meshes[mesh].Texture == null ||
                IsHiddenMesh(mesh))
                return;

            if (IsStaticMapMeshQueuedForInstancing(mesh))
                return;

            var gd = GraphicsDevice;
            gd.SetVertexBuffer(_meshes[mesh].CpuVertexBuffer);
            gd.Indices = _meshes[mesh].CpuIndexBuffer;
            int primitiveCount = gd.Indices.IndexCount / 3;
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
        }

        private Dictionary<MeshStateKey, List<int>> GroupMeshesByState(bool isAfterDraw)
        {
            return GetMeshRenderPlan(isAfterDraw);
        }

        private void DrawMeshesShadow(List<int> meshIndices, Matrix shadowMatrix, Matrix view, Matrix projection, float shadowOpacity)
        {
            for (int n = 0; n < meshIndices.Count; n++)
            {
                int meshIndex = meshIndices[n];
                if (IsStaticMapMeshQueuedForInstancing(meshIndex))
                    continue;

                DrawShadowMesh(meshIndex, view, projection, shadowMatrix, shadowOpacity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldUseBlobShadowForCurrentPass()
        {
            if (IsPlayerOrNpcShadowPart())
                return false;

            if (this is not WalkerObject)
                return false;

            // Keep the existing explicit compatibility switch for users who prefer the
            // legacy full-mesh monster shadow when shadow mapping is disabled.
            if (this is MonsterObject && MuGame.AppSettings?.Graphics?.ForceMonsterMeshShadows == true)
                return false;

            // A projected animated mesh forces a second CPU-skinned copy even when the
            // main pass uses GPU skinning. A single blob quad preserves grounding without
            // duplicating animation, uploads and draw calls for every walker.
            return true;
        }

        private void DrawMeshesHighlight(List<int> meshIndices, Matrix highlightMatrix, Vector3 highlightColor)
        {
            for (int n = 0; n < meshIndices.Count; n++)
            {
                int mi = meshIndices[n];
                if (_meshes == null || mi >= _meshes.Length)
                    return;
                if (mi < 0 || IsStaticMapMeshQueuedForInstancing(mi))
                    continue;

                DrawMeshHighlight(mi, highlightMatrix, highlightColor);
            }
        }

        public virtual void DrawMesh(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_meshes?[mesh]?.Texture == null || IsHiddenMesh(mesh))
                return;

            if (IsStaticMapMeshQueuedForInstancing(mesh))
                return;

            var shaderSelection = DetermineShaderForMesh(mesh);

            // Route every skinned-capable shader before checking CPU buffers. GPU-only
            // objects intentionally release their CPU copy, especially after crowd instancing.
            if (shaderSelection.UseItemMaterial)
            {
                DrawMeshWithItemMaterial(mesh);
                return;
            }

            if (shaderSelection.UseMonsterMaterial)
            {
                DrawMeshWithMonsterMaterial(mesh);
                return;
            }

            if (shaderSelection.UseDynamicLighting)
            {
                DrawMeshWithDynamicLighting(mesh);
                return;
            }

            bool hasCpuBuffers = _meshes?[mesh]?.CpuVertexBuffer != null &&
                                 _meshes?[mesh]?.CpuIndexBuffer != null;
            if (!hasCpuBuffers)
                return;

            try
            {
                var gd = GraphicsDevice;
                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    // Apply small depth bias based on object type to reduce Z-fighting
                    var prevRasterizer = gd.RasterizerState;
                    var depthBias = GetDepthBias();
                    if (depthBias != 0f)
                    {
                        // PERFORMANCE: Use cached RasterizerState to avoid per-mesh allocation
                        gd.RasterizerState = GraphicsManager.GetCachedRasterizerState(depthBias, prevRasterizer.CullMode, prevRasterizer);
                    }

                    var alphaEffect = GraphicsManager.Instance.AlphaTestEffect3D;

                    // Cache frequently used values
                    bool isBlendMesh = IsBlendMesh(mesh);
                    BlendState blendState = GetMeshBlendState(mesh, isBlendMesh);
                    // Always use AlphaTestEffect - it has ReferenceAlpha=2 which discards very low alpha
                    // pixels similar to DynamicLightingEffect's clip(finalAlpha - 0.01), preventing
                    // black outlines and depth buffer issues with semi-transparent meshes
                    var vertexBuffer = _meshes[mesh].CpuVertexBuffer;
                    var indexBuffer = _meshes[mesh].CpuIndexBuffer;
                    var texture = _meshes[mesh].Texture;

                    // Batch state changes - save current states
                    var originalRasterizer = gd.RasterizerState;
                    var prevBlend = gd.BlendState;
                    float prevAlpha = alphaEffect?.Alpha ?? 1f;

                    // Get mesh rendering states using helper methods
                    bool isTwoSided = IsMeshTwoSided(mesh, isBlendMesh);

                    // Apply final rasterizer state (considering depth bias and culling)
                    if (depthBias != 0f)
                    {
                        // PERFORMANCE: Use cached RasterizerState to avoid per-mesh allocation
                        CullMode cullMode = isTwoSided ? CullMode.None : CullMode.CullClockwiseFace;
                        gd.RasterizerState = GraphicsManager.GetCachedRasterizerState(depthBias, cullMode, originalRasterizer);
                    }
                    else
                    {
                        gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;
                    }

                    if (isBlendMesh)
                    {
                        gd.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                        depthStateChanged = true;
                    }
                    else if (!ReferenceEquals(gd.DepthStencilState, DepthStencilState.Default))
                    {
                        // Opaque meshes must write depth even when their object rides the
                        // transparent list (e.g. Valkyrie body + blend veil). Otherwise
                        // geometry drawn later behind them would cover the whole object.
                        gd.DepthStencilState = DepthStencilState.Default;
                        depthStateChanged = true;
                    }

                    gd.BlendState = blendState;

                    // Set buffers once
                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    // Draw with optimized primitive count calculation
                    int primitiveCount = indexBuffer.IndexCount / 3;

                    // Always use AlphaTestEffect - it discards very low alpha pixels (ReferenceAlpha=2)
                    // similar to DynamicLightingEffect's clip(finalAlpha - 0.01), preventing black
                    // outlines and depth issues while still allowing proper alpha blending
                    if (alphaEffect != null)
                    {
                        alphaEffect.Texture = texture;
                        alphaEffect.Alpha = TotalAlpha;

                        var technique = alphaEffect.CurrentTechnique;
                        var passes = technique.Passes;
                        int passCount = passes.Count;

                        for (int p = 0; p < passCount; p++)
                        {
                            passes[p].Apply();
                            ApplyQualityModelSampler(gd);
                            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                        }

                        alphaEffect.Alpha = prevAlpha;
                    }

                    gd.BlendState = prevBlend;
                    gd.RasterizerState = originalRasterizer;
                }
                finally
                {
                    if (depthStateChanged)
                        gd.DepthStencilState = prevDepthState;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawMesh: {Message}", ex.Message);
            }
        }

        public virtual void DrawMeshWithItemMaterial(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_meshes?[mesh]?.Texture == null || IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.ItemMaterialEffect;

                if (effect == null)
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                ResolveItemMaterialTechniqueNames(out string baseTechniqueName, out string skinnedTechniqueName);
                bool fastItemShader = CanUseFastItemUpgradeShader();
                if (!TryResolveMaterialMeshBuffers(
                    mesh,
                    effect,
                    baseTechniqueName,
                    skinnedTechniqueName,
                    out VertexBuffer vertexBuffer,
                    out IndexBuffer indexBuffer,
                    out bool usingGpuSkinning))
                {
                    return;
                }

                GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var texture = _meshes[mesh].Texture;

                    var prevCull = gd.RasterizerState;
                    var prevBlend = gd.BlendState;

                    // Get mesh rendering states using helper methods
                    bool isTwoSided = IsMeshTwoSided(mesh, isBlendMesh);
                    BlendState blendState = GetMeshBlendState(mesh, isBlendMesh);

                    gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;

                    if (isBlendMesh)
                    {
                        gd.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                        depthStateChanged = true;
                    }
                    else if (!ReferenceEquals(gd.DepthStencilState, DepthStencilState.Default))
                    {
                        // Opaque meshes must write depth even when their object rides the
                        // transparent list (e.g. Valkyrie body + blend veil). Otherwise
                        // geometry drawn later behind them would cover the whole object.
                        gd.DepthStencilState = DepthStencilState.Default;
                        depthStateChanged = true;
                    }

                    gd.BlendState = blendState;

                    Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
                    if (sunDir.LengthSquared() < 0.0001f)
                        sunDir = new Vector3(1f, 0f, -0.6f);
                    sunDir = Vector3.Normalize(sunDir);
                    bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
                    bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();

                    // Set world view projection matrix
                    Matrix worldViewProjection = WorldPosition * Camera.Instance.ViewProjection;
                    bindings.WorldViewProjection?.SetValue(worldViewProjection);
                    bindings.World?.SetValue(WorldPosition);
                    if (!fastItemShader)
                        bindings.EyePosition?.SetValue(Camera.Instance.Position);
                    bindings.LightDirection?.SetValue(sunDir);
                    bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

                    // Set texture
                    bindings.DiffuseTexture?.SetValue(texture);
                    if (!fastItemShader)
                        ApplyItemMaterialUpgradeBindings(bindings);

                    // Set item properties
                    int itemOptions = ItemLevel & 0x0F;
                    if (IsExcellentItem)
                        itemOptions |= 0x10;

                    bindings.ItemOptions?.SetValue(itemOptions);
                    bindings.Time?.SetValue(GetShaderTimeSeconds());
                    if (!fastItemShader)
                    {
                        bindings.IsAncient?.SetValue(IsAncientItem);
                        bindings.IsExcellent?.SetValue(IsExcellentItem);
                    }
                    bindings.Alpha?.SetValue(TotalAlpha);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;
                    if (usingGpuSkinning)
                        RegisterGpuSkinnedMeshDraw();

                    effect.CurrentTechnique.Passes[0].Apply();
                    ApplyQualityModelSampler(gd);
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);

                    gd.BlendState = prevBlend;
                    gd.RasterizerState = prevCull;
                }
                finally
                {
                    if (depthStateChanged)
                        gd.DepthStencilState = prevDepthState;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawMeshWithItemMaterial: {Message}", ex.Message);
            }
        }

        public virtual void DrawMeshWithMonsterMaterial(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_meshes?[mesh]?.Texture == null || IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.MonsterMaterialEffect;

                if (effect == null)
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                ResolveMonsterMaterialTechniqueNames(out string baseTechniqueName, out string skinnedTechniqueName);
                bool fastMonsterShader = CanUseFastMonsterMaterialShader();
                if (!TryResolveMaterialMeshBuffers(
                    mesh,
                    effect,
                    baseTechniqueName,
                    skinnedTechniqueName,
                    out VertexBuffer vertexBuffer,
                    out IndexBuffer indexBuffer,
                    out bool usingGpuSkinning))
                {
                    return;
                }

                GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var texture = _meshes[mesh].Texture;

                    var prevCull = gd.RasterizerState;
                    var prevBlend = gd.BlendState;

                    // Get mesh rendering states using helper methods
                    bool isTwoSided = IsMeshTwoSided(mesh, isBlendMesh);
                    BlendState blendState = GetMeshBlendState(mesh, isBlendMesh);

                    gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;

                    if (isBlendMesh)
                    {
                        gd.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                        depthStateChanged = true;
                    }
                    else if (!ReferenceEquals(gd.DepthStencilState, DepthStencilState.Default))
                    {
                        // Opaque meshes must write depth even when their object rides the
                        // transparent list (e.g. Valkyrie body + blend veil). Otherwise
                        // geometry drawn later behind them would cover the whole object.
                        gd.DepthStencilState = DepthStencilState.Default;
                        depthStateChanged = true;
                    }

                    gd.BlendState = blendState;

                    Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
                    if (sunDir.LengthSquared() < 0.0001f)
                        sunDir = new Vector3(1f, 0f, -0.6f);
                    sunDir = Vector3.Normalize(sunDir);
                    bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
                    bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();

                    // Set matrices
                    bindings.WorldViewProjection?.SetValue(
                        WorldPosition * Camera.Instance.ViewProjection);
                    bindings.World?.SetValue(WorldPosition);
                    bindings.LightDirection?.SetValue(sunDir);
                    bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

                    // Set texture
                    bindings.DiffuseTexture?.SetValue(texture);

                    // Set monster-specific properties
                    if (!fastMonsterShader)
                    {
                        bindings.GlowColor?.SetValue(GlowColor);
                        bindings.GlowIntensity?.SetValue(GlowIntensity);
                        bindings.EnableGlow?.SetValue(GlowIntensity > 0.0f && !SimpleColorMode);
                        bindings.SimpleColorMode?.SetValue(SimpleColorMode);
                        bindings.Time?.SetValue(GetShaderTimeSeconds());
                    }
                    bindings.Alpha?.SetValue(TotalAlpha);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;
                    if (usingGpuSkinning)
                        RegisterGpuSkinnedMeshDraw();

                    effect.CurrentTechnique.Passes[0].Apply();
                    ApplyQualityModelSampler(gd);
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);

                    gd.BlendState = prevBlend;
                    gd.RasterizerState = prevCull;
                }
                finally
                {
                    if (depthStateChanged)
                        gd.DepthStencilState = prevDepthState;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawMeshWithMonsterMaterial: {Message}", ex.Message);
            }
        }

        public virtual void DrawMeshWithDynamicLighting(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_meshes?[mesh]?.Texture == null || IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.DynamicLightingEffect;

                if (effect == null)
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var texture = _meshes[mesh].Texture;
                    // A monster can leave crowd instancing when an action changes or a
                    // one-shot starts. Do not trust a stale per-instance flag from the
                    // previous path; lazily attach the shared GPU geometry for this draw.
                    bool useGpuSkinning = CanUseGpuSkinningForMesh(mesh) &&
                                               EnsureGpuSkinnedMeshForMainPass(mesh);

                    VertexBuffer vertexBuffer = useGpuSkinning ? _meshes[mesh].GpuVertexBuffer : _meshes?[mesh]?.CpuVertexBuffer;
                    IndexBuffer indexBuffer = useGpuSkinning ? _meshes[mesh].GpuIndexBuffer : _meshes?[mesh]?.CpuIndexBuffer;
                    if (vertexBuffer == null || indexBuffer == null)
                        return;

                    var prevCull = gd.RasterizerState;
                    var prevBlend = gd.BlendState;

                    // Get mesh rendering states using helper methods
                    bool isTwoSided = IsMeshTwoSided(mesh, isBlendMesh);
                    BlendState blendState = GetMeshBlendState(mesh, isBlendMesh);

                    gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;

                    if (isBlendMesh)
                    {
                        gd.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                        depthStateChanged = true;
                    }
                    else if (!ReferenceEquals(gd.DepthStencilState, DepthStencilState.Default))
                    {
                        // Opaque meshes must write depth even when their object rides the
                        // transparent list (e.g. Valkyrie body + blend veil). Otherwise
                        // geometry drawn later behind them would cover the whole object.
                        gd.DepthStencilState = DepthStencilState.Default;
                        depthStateChanged = true;
                    }

                    gd.BlendState = blendState;

                    int requiredBoneCount = useGpuSkinning &&
                                            _meshes != null &&
                                            (uint)mesh < (uint)_meshes.Length
                        ? _meshes[mesh].GpuBoneCount
                        : 0;

                    // DynamicLightingEffect is shared by terrain, shadows, hover and all
                    // objects. Rebind the technique and bone palette for every mesh draw;
                    // invocation-level caching allowed another pass to leave Highlight or
                    // Terrain active and caused an intermittent CPU fallback.
                    PrepareDynamicLightingEffect(effect, useGpuSkinning, requiredBoneCount);
                    _dynamicLightingPreparedInvocationId = _drawModelInvocationId;
                    _dynamicLightingPreparedWithGpuSkinning = useGpuSkinning;
                    _dynamicLightingPreparedGpuBoneCount = useGpuSkinning ? requiredBoneCount : 0;

                    if (useGpuSkinning &&
                        !IsDynamicLightingSkinnedTechnique(effect.CurrentTechnique))
                    {
                        // Do not silently oscillate between GPU and CPU because of leaked
                        // effect state. Retry the exact skinned binding once.
                        PrepareDynamicLightingEffect(effect, true, requiredBoneCount);
                    }

                    if (useGpuSkinning &&
                        !IsDynamicLightingSkinnedTechnique(effect.CurrentTechnique))
                    {
                        vertexBuffer = _meshes?[mesh]?.CpuVertexBuffer;
                        indexBuffer = _meshes?[mesh]?.CpuIndexBuffer;
                        if (vertexBuffer == null || indexBuffer == null)
                            return;
                        useGpuSkinning = false;
                    }

                    if (useGpuSkinning)
                        RegisterGpuSkinnedMeshDraw();

                    // Texture scrolling is mesh-local when index >= 0, or applies to all meshes when index < 0.
                    Vector2 meshTextureOffset = (TextureCoordinateOffsetMeshIndex < 0 || mesh == TextureCoordinateOffsetMeshIndex)
                        ? TextureCoordinateOffset
                        : Vector2.Zero;
                    bindings.TextureCoordinateOffset?.SetValue(meshTextureOffset);

                    // Set texture
                    bindings.DiffuseTexture?.SetValue(texture);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;

                    effect.CurrentTechnique.Passes[0].Apply();
                    ApplyQualityModelSampler(gd);
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);

                    gd.BlendState = prevBlend;
                    gd.RasterizerState = prevCull;
                }
                finally
                {
                    // Never leave a per-mesh UV offset resident in the shared effect.
                    bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);
                    if (depthStateChanged)
                        gd.DepthStencilState = prevDepthState;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawMeshWithDynamicLighting: {Message}", ex.Message);
            }
        }

        public virtual void DrawMeshHighlight(int mesh, Matrix highlightMatrix, Vector3 highlightColor)
        {
            if (IsHiddenMesh(mesh) || _meshes == null ||
                mesh < 0 || mesh >= _meshes.Length ||
                _meshes[mesh].Texture == null)
            {
                return;
            }

            var previousDepthState = GraphicsDevice.DepthStencilState;
            var previousBlendState = GraphicsDevice.BlendState;

            try
            {
                // Keep hover on the GPU. Hovered walkers leave crowd instancing so they can
                // be selected independently, but their geometry and bone palette remain skinned.
                if (EnsureGpuSkinnedMeshForMainPass(mesh))
                {
                    var effect = GraphicsManager.Instance.DynamicLightingEffect;
                    var technique = TryGetTechnique(effect, "Highlight_Skinned");
                    var state = _meshes[mesh];
                    ModelEffectBindings bindings = GetModelEffectBindings(effect);

                    if (technique != null && bindings != null &&
                        state.GpuVertexBuffer != null && !state.GpuVertexBuffer.IsDisposed &&
                        state.GpuIndexBuffer != null && !state.GpuIndexBuffer.IsDisposed &&
                        TryUploadGpuSkinBoneMatrices(effect, bindings, state.GpuBoneCount))
                    {
                        effect.CurrentTechnique = technique;
                        bindings.World?.SetValue(highlightMatrix);
                        bindings.WorldViewProjection?.SetValue(
                            highlightMatrix * Camera.Instance.ViewProjection);
                        bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);
                        bindings.DiffuseTexture?.SetValue(state.Texture);
                        bindings.HighlightColor?.SetValue(highlightColor);
                        bindings.Alpha?.SetValue(1f);

                        GraphicsDevice.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                        GraphicsDevice.BlendState = BlendState.Additive;
                        GraphicsDevice.SetVertexBuffer(state.GpuVertexBuffer);
                        GraphicsDevice.Indices = state.GpuIndexBuffer;

                        int primitiveCount = state.GpuIndexBuffer.IndexCount / 3;
                        technique.Passes[0].Apply();
                        ApplyQualityModelSampler(GraphicsDevice);
                        GraphicsDevice.DrawIndexedPrimitives(
                            PrimitiveType.TriangleList, 0, 0, primitiveCount);

                        // Highlight uses the shared dynamic-lighting effect with another
                        // technique. Force the following main mesh draw to restore all
                        // skinned lighting parameters instead of trusting the per-object cache.
                        _dynamicLightingPreparedInvocationId = -1;
                        _dynamicLightingPreparedWithGpuSkinning = false;
                        _dynamicLightingPreparedGpuBoneCount = 0;
                        return;
                    }
                }

                VertexBuffer vertexBuffer = _meshes[mesh].CpuVertexBuffer;
                IndexBuffer indexBuffer = _meshes[mesh].CpuIndexBuffer;
                if (vertexBuffer == null || indexBuffer == null)
                    return;

                var alphaTestEffect = GraphicsManager.Instance.AlphaTestEffect3D;
                if (alphaTestEffect == null || alphaTestEffect.CurrentTechnique == null)
                    return;

                float previousAlpha = alphaTestEffect.Alpha;
                alphaTestEffect.World = highlightMatrix;
                alphaTestEffect.Texture = _meshes[mesh].Texture;
                alphaTestEffect.DiffuseColor = highlightColor;
                alphaTestEffect.Alpha = 1f;

                GraphicsDevice.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                GraphicsDevice.BlendState = BlendState.Additive;
                GraphicsDevice.SetVertexBuffer(vertexBuffer);
                GraphicsDevice.Indices = indexBuffer;

                int cpuPrimitiveCount = indexBuffer.IndexCount / 3;
                alphaTestEffect.CurrentTechnique.Passes[0].Apply();
                ApplyQualityModelSampler(GraphicsDevice);
                GraphicsDevice.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, cpuPrimitiveCount);

                alphaTestEffect.Alpha = previousAlpha;
                alphaTestEffect.World = WorldPosition;
                alphaTestEffect.DiffuseColor = Vector3.One;
            }
            finally
            {
                GraphicsDevice.DepthStencilState = previousDepthState;
                GraphicsDevice.BlendState = previousBlendState;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible) return;

            EnsureMeshRenderPlans();
            if (_transparentMeshPlan.Count > 0)
            {
                SetDrawShaderTimeSeconds((float)gameTime.TotalGameTime.TotalSeconds);

                var gd = GraphicsDevice;
                var prevCull = gd.RasterizerState;
                gd.RasterizerState = RasterizerState.CullCounterClockwise;

                GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
                GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
                GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;

                DrawModel(true);
                gd.RasterizerState = prevCull;
            }

#if ANDROID
            DrawBoundingBox2D();
            DrawHoverName();
#endif
            DrawChildrenAfterOnly(gameTime);
        }
    }
}
