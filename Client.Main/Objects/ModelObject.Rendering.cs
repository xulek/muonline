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
        // Struct to hold shader selection results - now optimized for key comparison
        private readonly struct ShaderSelection : IEquatable<ShaderSelection>
        {
            public readonly bool UseDynamicLighting;
            public readonly bool UseItemMaterial;
            public readonly bool UseMonsterMaterial;
            public readonly bool NeedsSpecialShader;

            public ShaderSelection(bool useDynamicLighting, bool useItemMaterial, bool useMonsterMaterial)
            {
                UseDynamicLighting = useDynamicLighting;
                UseItemMaterial = useItemMaterial;
                UseMonsterMaterial = useMonsterMaterial;
                NeedsSpecialShader = useItemMaterial || useMonsterMaterial || useDynamicLighting;
            }

            public bool Equals(ShaderSelection other) =>
                UseDynamicLighting == other.UseDynamicLighting &&
                UseItemMaterial == other.UseItemMaterial &&
                UseMonsterMaterial == other.UseMonsterMaterial;

            public override bool Equals(object obj) => obj is ShaderSelection other && Equals(other);

            public override int GetHashCode() =>
                (UseDynamicLighting ? 1 : 0) | (UseItemMaterial ? 2 : 0) | (UseMonsterMaterial ? 4 : 0);
        }

        // State grouping optimization - now includes shader selection
        private readonly struct MeshStateKey : IEquatable<MeshStateKey>
        {
            public readonly Texture2D Texture;
            public readonly BlendState BlendState;
            public readonly bool TwoSided;
            public readonly ShaderSelection Shader;

            public MeshStateKey(Texture2D tex, BlendState blend, bool twoSided, ShaderSelection shader)
            {
                Texture = tex;
                BlendState = blend;
                TwoSided = twoSided;
                Shader = shader;
            }

            public bool Equals(MeshStateKey other) =>
                ReferenceEquals(Texture, other.Texture) &&
                ReferenceEquals(BlendState, other.BlendState) &&
                TwoSided == other.TwoSided &&
                Shader.Equals(other.Shader);

            public override bool Equals(object obj) => obj is MeshStateKey o && Equals(o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + (Texture?.GetHashCode() ?? 0);
                    h = h * 31 + (BlendState?.GetHashCode() ?? 0);
                    h = h * 31 + (TwoSided ? 1 : 0);
                    h = h * 31 + Shader.GetHashCode();
                    return h;
                }
            }
        }

        // State grouping optimization - now persistent to avoid per-frame classification
        private readonly Dictionary<MeshStateKey, List<int>> _meshGroupsSolid = new Dictionary<MeshStateKey, List<int>>(16);
        private readonly Dictionary<MeshStateKey, List<int>> _meshGroupsAfter = new Dictionary<MeshStateKey, List<int>>(16);
        private bool _meshGroupsInvalidated = true;
        private readonly Stack<List<int>> _meshGroupPool = new Stack<List<int>>(32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<int> RentMeshList()
            => _meshGroupPool.Count > 0 ? _meshGroupPool.Pop() : new List<int>(8);

        private void ReleaseMeshGroups(Dictionary<MeshStateKey, List<int>> groups)
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

        private void ClearAllMeshGroups()
        {
            ReleaseMeshGroups(_meshGroupsSolid);
            ReleaseMeshGroups(_meshGroupsAfter);
            _meshGroupsInvalidated = true;
        }

        // Hint for world-level batching: returns first visible mesh texture (if any)
        internal Texture2D GetSortTextureHint()
        {
            if (!_sortTextureHintDirty)
                return _sortTextureHint;

            _sortTextureHintDirty = false;
            _sortTextureHint = null;

            if (_boneTextures == null)
                return null;

            for (int i = 0; i < _boneTextures.Length; i++)
            {
                var tex = _boneTextures[i];
                if (tex != null && !IsHiddenMesh(i))
                {
                    _sortTextureHint = tex;
                    break;
                }
            }

            return _sortTextureHint;
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
            if (_meshIsRGBA == null || mesh < 0 || mesh >= _meshIsRGBA.Length)
                return false;

            if (_meshIsRGBA[mesh] || isBlendMesh)
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

            return _meshIsRGBA != null && (uint)mesh < (uint)_meshIsRGBA.Length && _meshIsRGBA[mesh];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsHiddenMesh(int mesh)
        {
            if (_meshHiddenByScript == null || (uint)mesh >= (uint)_meshHiddenByScript.Length)
                return false;

            return HiddenMesh == mesh || HiddenMesh == -2 || _meshHiddenByScript[mesh];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool IsBlendMesh(int mesh)
        {
            if (_meshBlendByScript == null || (uint)mesh >= (uint)_meshBlendByScript.Length)
                return false;

            return BlendMesh == mesh || BlendMesh == -2 || _meshBlendByScript[mesh];
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
            // Only force standard path for fading monsters (to guarantee alpha/darken visibility)
            if (this is MonsterObject mo && mo.IsDead)
                return new ShaderSelection(false, false, false);

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
            bool useDynamicLighting = !useItemMaterial && !useMonsterMaterial &&
                                      Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                      GraphicsManager.Instance.DynamicLightingEffect != null;

            return new ShaderSelection(useDynamicLighting, useItemMaterial, useMonsterMaterial);
        }

        // Determines if this mesh needs special shader path and cannot use fast alpha path
        private bool NeedsSpecialShaderForMesh(int mesh)
        {
            return DetermineShaderForMesh(mesh).NeedsSpecialShader;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || _boneIndexBuffers == null) return;

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
            if (Model?.Meshes == null || _boneVertexBuffers == null)
            {
                ClearAllMeshGroups();
                return;
            }

            int meshCount = Model.Meshes.Length;
            if (meshCount == 0)
            {
                ClearAllMeshGroups();
                return;
            }

            _drawModelInvocationId = ++_drawModelInvocationCounter;

            // Cache commonly used values
            var view = Camera.Instance.View;
            var projection = Camera.Instance.Projection;
            var worldPos = WorldPosition;

            // Pre-calculate shadow and highlight states at object level
            bool doShadow = false;
            Matrix shadowMatrix = Matrix.Identity;
            bool useShadowMap = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                GraphicsManager.Instance.ShadowMapRenderer?.IsReady == true;
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

            // Group meshes by render state to minimize state changes
            if (_meshGroupsInvalidated || _meshGroupsSolid.Count == 0 || _meshGroupsAfter.Count == 0)
                RebuildMeshGroups();

            var meshGroups = isAfterDraw ? _meshGroupsAfter : _meshGroupsSolid;

            // Render each group with minimal state changes
            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.AlphaTestEffect3D;
                // Object-level alpha is constant; set once for the pass
                if (effect != null && effect.Alpha != TotalAlpha)
                    effect.Alpha = TotalAlpha;

                // Pass shared parameters once per object to avoid redundant shader sets
                ApplySharedShaderParameters();

                foreach (var kvp in meshGroups)
                {
                    var stateKey = kvp.Key;
                    var meshIndices = kvp.Value;
                    if (meshIndices.Count == 0) continue;

                    // Apply basic render state once per group
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

                    // Specialized shader setup once per group
                    var shader = stateKey.Shader;
                    bool isSpecial = shader.NeedsSpecialShader;
                    
                    if (isSpecial)
                    {
                        // Some special shaders need per-mesh param updates, but we can still
                        // avoid re-applying the basic effect in this loop branch.
                    }
                    else if (effect != null)
                    {
                        if (effect.Texture != stateKey.Texture)
                                effect.Texture = stateKey.Texture;

                        var passes = effect.CurrentTechnique.Passes;
                        for (int p = 0; p < passes.Count; p++)
                            passes[p].Apply();
                    }

                    // Object-level shadow/highlight passes (only if not after-draw)
                    if (doShadow && !useShadowMap)
                        DrawMeshesShadow(meshIndices, shadowMatrix, view, projection, shadowOpacity);
                    if (highlightAllowed)
                        DrawMeshesHighlight(meshIndices, highlightMatrix, highlightColor);

                    // Restore state if shadow/highlight pass changed it
                    if (!isSpecial && (doShadow || highlightAllowed) && effect != null)
                    {
                        var passes = effect.CurrentTechnique.Passes;
                        for (int p = 0; p < passes.Count; p++)
                            passes[p].Apply();
                    }

                    // Draw meshes
                    bool forcePerMesh = isSpecial || (!Constants.ENABLE_DYNAMIC_LIGHTING_SHADER && stateKey.BlendState != BlendState.Opaque);
                    
                    for (int n = 0; n < meshIndices.Count; n++)
                    {
                        int mi = meshIndices[n];
                        if (forcePerMesh)
                        {
                            DrawMesh(mi); 
                            
                            // Restore basic effect if next meshes might need it (after special shader or per-mesh alpha)
                            if (!isSpecial && effect != null)
                            {
                                var passes = effect.CurrentTechnique.Passes;
                                for (int p = 0; p < passes.Count; p++)
                                    passes[p].Apply();
                            }
                        }
                        else
                        {
                            DrawMeshFastAlpha(mi);
                        }
                    }
                }
            }
            finally
            {
                // Groups are now persistent, no need to release them per pass.
            }
        }

        // Fast path draw for standard alpha-tested meshes (no special shaders)
        private void DrawMeshFastAlpha(int mesh)
        {
            if (_boneVertexBuffers == null || _boneIndexBuffers == null || _boneTextures == null)
                return;
            if (mesh < 0 ||
                mesh >= _boneVertexBuffers.Length ||
                mesh >= _boneIndexBuffers.Length ||
                mesh >= _boneTextures.Length ||
                _boneVertexBuffers[mesh] == null ||
                _boneIndexBuffers[mesh] == null ||
                _boneTextures[mesh] == null ||
                IsHiddenMesh(mesh))
                return;

            var gd = GraphicsDevice;
            gd.SetVertexBuffer(_boneVertexBuffers[mesh]);
            gd.Indices = _boneIndexBuffers[mesh];
            int primitiveCount = gd.Indices.IndexCount / 3;
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
        }

        private void RebuildMeshGroups()
        {
            ReleaseMeshGroups(_meshGroupsSolid);
            ReleaseMeshGroups(_meshGroupsAfter);

            if (Model?.Meshes == null)
                return;

            int meshCount = Model.Meshes.Length;

            for (int i = 0; i < meshCount; i++)
            {
                if (IsHiddenMesh(i)) continue;

                bool isBlend = IsBlendMesh(i);
                bool isRGBA = _meshIsRGBA != null && i < _meshIsRGBA.Length && _meshIsRGBA[i];

                if (_boneTextures == null || i >= _boneTextures.Length)
                    continue;

                var tex = _boneTextures[i];
                bool twoSided = IsMeshTwoSided(i, isBlend);
                BlendState blend = GetMeshBlendState(i, isBlend);
                var shader = DetermineShaderForMesh(i);
                var key = new MeshStateKey(tex, blend, twoSided, shader);

                // Sort into appropriate pass grouping
                bool isTransparent = isRGBA || isBlend;
                var targetGroups = isTransparent ? _meshGroupsAfter : _meshGroupsSolid;

                if (!targetGroups.TryGetValue(key, out var list))
                {
                    list = RentMeshList();
                    targetGroups[key] = list;
                }

                list.Add(i);
            }

            _meshGroupsInvalidated = false;
        }

        // Keep legacy for compatibility if needed, but redirects to persistent state
        private void GroupMeshesByState(bool isAfterDraw)
        {
            if (_meshGroupsInvalidated)
                RebuildMeshGroups();
        }

        private void DrawMeshesShadow(List<int> meshIndices, Matrix shadowMatrix, Matrix view, Matrix projection, float shadowOpacity)
        {
            DrawShadowMeshes(meshIndices, view, projection, shadowMatrix, shadowOpacity);
        }

        private void DrawMeshesHighlight(List<int> meshIndices, Matrix highlightMatrix, Vector3 highlightColor)
        {
            for (int n = 0; n < meshIndices.Count; n++)
            {
                int mi = meshIndices[n];
                if (_boneVertexBuffers == null || _boneIndexBuffers == null || _boneTextures == null)
                    return;
                if (mi < 0 ||
                    mi >= _boneVertexBuffers.Length ||
                    mi >= _boneIndexBuffers.Length ||
                    mi >= _boneTextures.Length)
                {
                    continue;
                }
                DrawMeshHighlight(mi, highlightMatrix, highlightColor);
            }
        }

        public virtual void DrawMesh(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_boneVertexBuffers?[mesh] == null ||
                _boneIndexBuffers?[mesh] == null ||
                _boneTextures?[mesh] == null ||
                IsHiddenMesh(mesh))
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

                    // Determine which shader to use (if any)
                    var shaderSelection = DetermineShaderForMesh(mesh);

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

                    var alphaEffect = GraphicsManager.Instance.AlphaTestEffect3D;

                    // Cache frequently used values
                    bool isBlendMesh = IsBlendMesh(mesh);
                    BlendState blendState = GetMeshBlendState(mesh, isBlendMesh);
                    // Always use AlphaTestEffect - it has ReferenceAlpha=2 which discards very low alpha
                    // pixels similar to DynamicLightingEffect's clip(finalAlpha - 0.01), preventing
                    // black outlines and depth buffer issues with semi-transparent meshes
                    var vertexBuffer = _boneVertexBuffers[mesh];
                    var indexBuffer = _boneIndexBuffers[mesh];
                    var texture = _boneTextures[mesh];

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

        /// <summary>
        /// PERFORMANCE: Applies common shader parameters (matrices, eye, sun) once per ModelObject
        /// to avoid redundant per-mesh updates.
        /// </summary>
        private void ApplySharedShaderParameters()
        {
            var gd = GraphicsDevice;
            var gm = GraphicsManager.Instance;
            
            // Shared parameters like View, Projection, Sun, Time are now updated once per frame 
            // in GraphicsManager.UpdateGlobalShaderParameters() called in MuGame.Draw().
            
            // We only need to set object-specific parameters here.
            var world = WorldPosition;
            var cam = Camera.Instance;
            if (cam == null) return;
            
            var worldViewProj = world * cam.View * cam.Projection;
            float alpha = TotalAlpha;

            // Use a bitmask or flags from RebuildMeshGroups to know which shaders are actually used
            // by this specific object instance this frame.
            bool usesDL = false;
            bool usesIM = false;
            bool usesMM = false;

            foreach (var key in _meshGroupsSolid.Keys)
            {
                if (key.Shader.UseDynamicLighting) usesDL = true;
                if (key.Shader.UseItemMaterial) usesIM = true;
                if (key.Shader.UseMonsterMaterial) usesMM = true;
            }
            foreach (var key in _meshGroupsAfter.Keys)
            {
                if (key.Shader.UseDynamicLighting) usesDL = true;
                if (key.Shader.UseItemMaterial) usesIM = true;
                if (key.Shader.UseMonsterMaterial) usesMM = true;
            }

            if (usesDL && gm.DynamicLightingEffect != null)
            {
                gm.DynamicLightingEffect.Parameters["WorldViewProjection"]?.SetValue(worldViewProj);
                gm.DynamicLightingEffect.Parameters["World"]?.SetValue(world);
                gm.DynamicLightingEffect.Parameters["Alpha"]?.SetValue(alpha);
            }

            if (usesIM && gm.ItemMaterialEffect != null)
            {
                gm.ItemMaterialEffect.Parameters["WorldViewProjection"]?.SetValue(worldViewProj);
                gm.ItemMaterialEffect.Parameters["World"]?.SetValue(world);
                gm.ItemMaterialEffect.Parameters["Alpha"]?.SetValue(alpha);
            }

            if (usesMM && gm.MonsterMaterialEffect != null)
            {
                gm.MonsterMaterialEffect.Parameters["WorldViewProjection"]?.SetValue(worldViewProj);
                gm.MonsterMaterialEffect.Parameters["World"]?.SetValue(world);
                gm.MonsterMaterialEffect.Parameters["Alpha"]?.SetValue(alpha);
            }
        }

        public virtual void DrawMeshWithItemMaterial(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_boneVertexBuffers?[mesh] == null ||
                _boneIndexBuffers?[mesh] == null ||
                _boneTextures?[mesh] == null ||
                IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.ItemMaterialEffect;

                if (effect == null)
                {
                    DrawMesh(mesh);
                    return;
                }

                effect.CurrentTechnique = effect.Techniques[0];
                GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var vertexBuffer = _boneVertexBuffers[mesh];
                    var indexBuffer = _boneIndexBuffers[mesh];
                    var texture = _boneTextures[mesh];

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

                    gd.BlendState = blendState;

                    // Shared parameters are now applied once per object in ApplySharedShaderParameters()
                    // only parameters that vary PER-MESH should be here.
                    
                    // Set item properties (can vary per mesh if needed)
                    int itemOptions = ItemLevel & 0x0F;
                    if (IsExcellentItem)
                        itemOptions |= 0x10;

                    effect.Parameters["ItemOptions"]?.SetValue(itemOptions);
                    effect.Parameters["IsAncient"]?.SetValue(IsAncientItem);
                    effect.Parameters["IsExcellent"]?.SetValue(IsExcellentItem);
                    effect.Parameters["DiffuseTexture"]?.SetValue(texture);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                    }

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
                DrawMesh(mesh);
            }
        }

        public virtual void DrawMeshWithMonsterMaterial(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_boneVertexBuffers?[mesh] == null ||
                _boneIndexBuffers?[mesh] == null ||
                _boneTextures?[mesh] == null ||
                IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.MonsterMaterialEffect;

                if (effect == null)
                {
                    DrawMesh(mesh);
                    return;
                }

                effect.CurrentTechnique = effect.Techniques[0];
                GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var vertexBuffer = _boneVertexBuffers[mesh];
                    var indexBuffer = _boneIndexBuffers[mesh];
                    var texture = _boneTextures[mesh];

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

                    gd.BlendState = blendState;

                    Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
                    if (sunDir.LengthSquared() < 0.0001f)
                        sunDir = new Vector3(1f, 0f, -0.6f);
                    sunDir = Vector3.Normalize(sunDir);
                    bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
                    bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();

                    // Set matrices
                    effect.Parameters["World"]?.SetValue(WorldPosition);
                    effect.Parameters["View"]?.SetValue(Camera.Instance.View);
                    effect.Parameters["Projection"]?.SetValue(Camera.Instance.Projection);
                    effect.Parameters["EyePosition"]?.SetValue(Camera.Instance.Position);
                    effect.Parameters["LightDirection"]?.SetValue(sunDir);
                    effect.Parameters["ShadowStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

                    // Set texture
                    effect.Parameters["DiffuseTexture"]?.SetValue(texture);

                    // Set monster-specific properties
                    effect.Parameters["GlowColor"]?.SetValue(GlowColor);
                    effect.Parameters["GlowIntensity"]?.SetValue(GlowIntensity);
                    effect.Parameters["EnableGlow"]?.SetValue(GlowIntensity > 0.0f && !SimpleColorMode);
                    effect.Parameters["SimpleColorMode"]?.SetValue(SimpleColorMode);
                    effect.Parameters["Time"]?.SetValue(GetCachedTime());
                    effect.Parameters["Alpha"]?.SetValue(TotalAlpha);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                    }

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
                DrawMesh(mesh);
            }
        }

        public virtual void DrawMeshWithDynamicLighting(int mesh)
        {
            if (Model?.Meshes == null || mesh < 0 || mesh >= Model.Meshes.Length)
                return;
            if (_boneVertexBuffers?[mesh] == null ||
                _boneIndexBuffers?[mesh] == null ||
                _boneTextures?[mesh] == null ||
                IsHiddenMesh(mesh))
                return;

            try
            {
                var gd = GraphicsDevice;
                var effect = GraphicsManager.Instance.DynamicLightingEffect;

                if (effect == null)
                {
                    DrawMesh(mesh); // Fallback to standard rendering
                    return;
                }

                var prevDepthState = gd.DepthStencilState;
                bool depthStateChanged = false;

                try
                {
                    bool isBlendMesh = IsBlendMesh(mesh);
                    var vertexBuffer = _boneVertexBuffers[mesh];
                    var indexBuffer = _boneIndexBuffers[mesh];
                    var texture = _boneTextures[mesh];

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

                    gd.BlendState = blendState;

                    if (_dynamicLightingPreparedInvocationId != _drawModelInvocationId)
                    {
                        PrepareDynamicLightingEffect(effect);
                        _dynamicLightingPreparedInvocationId = _drawModelInvocationId;
                    }

                    // Set texture
                    effect.Parameters["DiffuseTexture"]?.SetValue(texture);

                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;

                    int primitiveCount = indexBuffer.IndexCount / 3;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                    }

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
                _logger?.LogDebug("Error in DrawMeshWithDynamicLighting: {Message}", ex.Message);
                DrawMesh(mesh); // Fallback to standard rendering
            }
        }

        public virtual void DrawMeshHighlight(int mesh, Matrix highlightMatrix, Vector3 highlightColor)
        {
            if (IsHiddenMesh(mesh) || _boneVertexBuffers == null || _boneIndexBuffers == null || _boneTextures == null)
                return;

            // Defensive range checks to avoid races when buffers are swapped during async loads
            if (mesh < 0 ||
                mesh >= _boneVertexBuffers.Length ||
                mesh >= _boneIndexBuffers.Length ||
                mesh >= _boneTextures.Length)
            {
                return;
            }

            VertexBuffer vertexBuffer = _boneVertexBuffers[mesh];
            IndexBuffer indexBuffer = _boneIndexBuffers[mesh];

            if (vertexBuffer == null || indexBuffer == null)
                return;

            int primitiveCount = indexBuffer.IndexCount / 3;

            // Save previous graphics states
            var previousDepthState = GraphicsDevice.DepthStencilState;
            var previousBlendState = GraphicsDevice.BlendState;

            var alphaTestEffect = GraphicsManager.Instance.AlphaTestEffect3D;
            if (alphaTestEffect == null || alphaTestEffect.CurrentTechnique == null) return;

            float prevAlpha = alphaTestEffect.Alpha;

            alphaTestEffect.World = highlightMatrix;
            alphaTestEffect.Texture = _boneTextures[mesh];
            alphaTestEffect.DiffuseColor = highlightColor;
            alphaTestEffect.Alpha = 1f;

            // Configure depth and blend states for drawing the highlight
            GraphicsDevice.DepthStencilState = GraphicsManager.ReadOnlyDepth;
            GraphicsDevice.BlendState = BlendState.Additive;

            // Draw the mesh highlight
            foreach (EffectPass pass in alphaTestEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.SetVertexBuffer(vertexBuffer);
                GraphicsDevice.Indices = indexBuffer;
                GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            }

            alphaTestEffect.Alpha = prevAlpha;

            // Restore previous graphics states
            GraphicsDevice.DepthStencilState = previousDepthState;
            GraphicsDevice.BlendState = previousBlendState;

            alphaTestEffect.World = WorldPosition;
            alphaTestEffect.DiffuseColor = Vector3.One;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible) return;

            var gd = GraphicsDevice;
            var prevCull = gd.RasterizerState;
            gd.RasterizerState = RasterizerState.CullCounterClockwise;

            GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
            GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
            GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;

            DrawModel(true);    // RGBA / blend mesh
            base.DrawAfter(gameTime);

            gd.RasterizerState = prevCull;
        }
    }
}
