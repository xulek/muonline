using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Diagnostics;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    public partial class ModelObject
    {
        private readonly record struct EquipmentBatchKey(VertexBuffer Vertices, IndexBuffer Indices, Texture2D Texture,
            int ItemGroup, int ItemIndex, bool TwoSided);

        private static readonly Dictionary<EquipmentBatchKey, List<int>> s_equipmentBatches = new();
        private static readonly List<List<int>> s_equipmentBatchDrawLists = new();
        private static readonly VertexBufferBinding[] s_equipmentVertexBindings = new VertexBufferBinding[2];
        private static SkinnedEquipmentInstanceData[] s_equipmentInstances = new SkinnedEquipmentInstanceData[32];
        private static DynamicVertexBuffer s_equipmentInstanceBuffer;
        private static Effect s_failedEquipmentInstancingEffect;

        private static void DrawPlayerEquipmentBatches(GraphicsDevice gd, List<DeferredMeshDraw> draws, int firstItem)
        {
            var effect = GraphicsManager.Instance.ItemMaterialEffect;
            if (effect == null || ReferenceEquals(s_failedEquipmentInstancingEffect, effect))
                return;

            var bindings = GetModelEffectBindings(effect);
            var technique = bindings.GetTechnique("BasicColorDrawing_SkinnedEquipmentInstanced");
            if (technique == null)
                return;

            var previousTechnique = effect.CurrentTechnique;
            var token = DrawPathCounter.Start();
            try
            {
                // Only the sorted opaque ItemMaterial group is reordered. Draws already flushed for overlays
                // have no owner here. Every other deferred mesh keeps its original submission path.
                for (int i = firstItem; i < draws.Count && (draws[i].SortKey >> 56) == (long)MeshShaderKind.ItemMaterial; i++)
                {
                    var draw = draws[i];
                    if (draw.Owner == null || !draw.Owner.TryGetEquipmentBatchKey(draw.Mesh, out var key))
                        continue;

                    if (!s_equipmentBatches.TryGetValue(key, out var indices))
                    {
                        int group = s_equipmentBatches.Count;
                        if (group == s_equipmentBatchDrawLists.Count)
                            s_equipmentBatchDrawLists.Add(new List<int>());
                        indices = s_equipmentBatchDrawLists[group];
                        s_equipmentBatches.Add(key, indices);
                    }

                    indices.Add(i);
                }

                foreach (var (key, indices) in s_equipmentBatches)
                {
                    if (indices.Count >= 2)
                        DrawEquipmentInstances(gd, effect, bindings, technique, key, draws, indices);
                }
            }
            catch (Exception ex)
            {
                // Successful batches clear their own entries. An unsupported draw can fall back in this frame.
                s_failedEquipmentInstancingEffect = effect;
                AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex, "Player equipment instancing fell back to individual draws");
            }
            finally
            {
                for (int i = 0; i < s_equipmentBatches.Count; i++)
                    s_equipmentBatchDrawLists[i].Clear();
                s_equipmentBatches.Clear();
                effect.CurrentTechnique = previousTechnique;
                DrawPathCounter.StopExtra(token, DrawPathCounter.Extra.EquipmentInstancing);
            }
        }

        private bool TryGetEquipmentBatchKey(int mesh, out EquipmentBatchKey key)
        {
            key = default;
            if (this is not WeaponObject && !IsCompositeBodyPart(this))
                return false;

            if (Status != GameControlStatus.Ready || !Visible || TotalAlpha != 1f || IsMouseHover ||
                Parent is not PlayerObject { IsMainWalker: false, IsMouseHover: false, IsCloaked: false } ||
                SpecialItemSpec != null || PcRoomEffect || _activeOverlayMode != ItemOverlayMode.None ||
                !CanUseGpuSkinGeometry() || _meshes == null || (uint)mesh >= (uint)_meshes.Length || IsHiddenMesh(mesh))
                return false;

            var state = _meshes[mesh];
            if (state.IsRgba || !state.GpuSkinEnabled || state.GpuBoneCount <= 0 ||
                state.GpuVertexBuffer is not { IsDisposed: false } || state.GpuIndexBuffer is not { IsDisposed: false } ||
                state.Texture is not { IsDisposed: false })
                return false;

            key = new EquipmentBatchKey(state.GpuVertexBuffer, state.GpuIndexBuffer, state.Texture,
                MaterialItemGroup, MaterialItemIndex, IsMeshTwoSided(mesh, false));
            return true;
        }

        private static void DrawEquipmentInstances(GraphicsDevice gd, Effect effect, ModelEffectBindings bindings,
            EffectTechnique technique, EquipmentBatchKey key, List<DeferredMeshDraw> draws, List<int> indices)
        {
            if (s_equipmentInstances.Length < indices.Count)
                Array.Resize(ref s_equipmentInstances, Math.Max(s_equipmentInstances.Length * 2, indices.Count));

            int instanceCount = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                var draw = draws[indices[i]];
                var owner = draw.Owner;
                var (bones, version) = owner.GetGpuSkinPose();
                if (!GpuSkinPalette.TryGetPreparedBase(gd, bones, version, owner._meshes[draw.Mesh].GpuBoneCount, out int paletteBase))
                    continue;

                indices[instanceCount] = indices[i];
                s_equipmentInstances[instanceCount++] = new SkinnedEquipmentInstanceData
                {
                    World = owner.WorldPosition,
                    PaletteAndOptions = new Vector4(paletteBase, owner.GetCompositeItemOptions(),
                        owner.HasExcellentGlow ? 1f : 0f, owner.IsAncientItem ? 1f : 0f)
                };
            }

            if (instanceCount < 2)
                return;

            if (s_equipmentInstanceBuffer is not { IsDisposed: false } || s_equipmentInstanceBuffer.GraphicsDevice != gd ||
                s_equipmentInstanceBuffer.VertexCount < instanceCount)
            {
                s_equipmentInstanceBuffer?.Dispose();
                s_equipmentInstanceBuffer = new DynamicVertexBuffer(gd, SkinnedEquipmentInstanceData.VertexDeclaration,
                    s_equipmentInstances.Length, BufferUsage.WriteOnly);
            }

            var first = draws[indices[0]].Owner;
            effect.CurrentTechnique = technique;
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);
            first.ApplyItemMaterialUpgradeBindings(bindings);
            bindings.ViewProjection.SetValue(Camera.Instance.ViewProjection);
            bindings.BonePaletteTexture.SetValue(GpuSkinPalette.Texture);
            bindings.BonePaletteBoundTexture = GpuSkinPalette.Texture;
            bindings.DiffuseTexture.SetValue(key.Texture);
            Vector3 sunDirection = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDirection.LengthSquared() < 0.0001f)
                sunDirection = new Vector3(1f, 0f, -0.6f);
            bindings.LightDirection?.SetValue(Vector3.Normalize(sunDirection));
            bindings.ShadowStrength?.SetValue(0f);
            bindings.Time?.SetValue(first.GetShaderTimeSeconds());
            bindings.Alpha?.SetValue(1f);

            s_equipmentInstanceBuffer.SetData(s_equipmentInstances, 0, instanceCount, SetDataOptions.Discard);
            s_equipmentVertexBindings[0] = new VertexBufferBinding(key.Vertices);
            s_equipmentVertexBindings[1] = new VertexBufferBinding(s_equipmentInstanceBuffer, 0, 1);
            gd.SetVertexBuffers(s_equipmentVertexBindings);
            gd.Indices = key.Indices;
            SetMeshBlendState(gd, BlendState.Opaque);
            SetMeshRasterizerState(gd, key.TwoSided ? _cullNone : _cullClockwise);
            technique.Passes[0].Apply();
            ApplyQualityModelSampler(gd);
            gd.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, key.Indices.IndexCount / 3, instanceCount);

            RegisterGpuSkinnedMeshDraw(instanceCount);
            RegisterGpuSkinnedBatchDraw(instanceCount);
            for (int i = 0; i < instanceCount; i++)
                draws[indices[i]] = default;
        }

        internal static void DisposeEquipmentInstancing()
        {
            s_equipmentInstanceBuffer?.Dispose();
            s_equipmentInstanceBuffer = null;
            s_failedEquipmentInstancingEffect = null;
            Array.Clear(s_equipmentVertexBindings);
        }
    }
}
