using System;
using System.Collections.Generic;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private bool? _defaultCutoutRendering;

        private struct CutoutInstanceData : IVertexType
        {
            public Matrix World;
            public Matrix WorldViewProjection;

            public static readonly VertexDeclaration Declaration = new(
                new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
                new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
                new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
                new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
                new VertexElement(64, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6),
                new VertexElement(80, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 7),
                new VertexElement(96, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 8),
                new VertexElement(112, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 9));

            VertexDeclaration IVertexType.VertexDeclaration => Declaration;
        }
        // The regular map queue excludes RGBA textures. Single cutout meshes nevertheless
        // use opaque blending and write depth in DrawMeshWithDynamicLighting. Batch only
        // consecutive matching draws in the after-pass, preserving their original order.
        internal sealed class CutoutMapBatch : IDisposable
        {
            private VertexBuffer _vertices;
            private IndexBuffer _indices;
            private Texture2D _texture;
            private int _boneCount;
            private DynamicVertexBuffer _instanceBuffer;
            private readonly VertexBufferBinding[] _vertexBindings = new VertexBufferBinding[2];
            private readonly List<ModelObject> _objects = new(64);
            private CutoutInstanceData[] _instances = new CutoutInstanceData[64];
            private EffectTechnique _technique;
            private long _lightSelection;
            private bool _disabled;

            public bool TryQueue(ModelObject model, GameTime time)
            {
                try
                {
                    return TryQueueCore(model, time);
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex,
                        "Cutout map queue disabled; restoring individual draws.");
                    return false;
                }
            }

            private bool TryQueueCore(ModelObject model, GameTime time)
            {
                if (_disabled || !model.CanBatchCutoutMapMesh())
                    return false;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(model.Model, 0,
                    out VertexBuffer vertices, out IndexBuffer indices, out int boneCount))
                    return false;

                Effect effect = GraphicsManager.Instance.DynamicLightingEffect;
                model.PrepareDynamicLightingEffect(effect, useGpuSkinning: true, requiredBoneCount: boneCount);
                string techniqueName = effect.CurrentTechnique.Name switch
                {
                    "DynamicLighting_Skinned" => "DynamicLighting_CutoutInstanced",
                    "DynamicLighting_Skinned_VertexLit" => "DynamicLighting_CutoutInstanced_VertexLit",
                    "DynamicLighting_Skinned_SunOnly" => "DynamicLighting_CutoutInstanced_SunOnly",
                    _ => null
                };
                EffectTechnique technique = techniqueName == null
                    ? null : GetModelEffectBindings(effect).GetTechnique(techniqueName);
                if (technique == null)
                    return false;

                long lightSelection = DynamicLightGpuUploader.GetAppliedSelectionToken(effect);
                // An unversioned light list cannot certify equal per-object selections.
                if (lightSelection == long.MinValue)
                    return false;
                Texture2D texture = model._meshes[0].Texture;
                if (_objects.Count > 0)
                {
                    ModelObject first = _objects[0];
                    if (!ReferenceEquals(vertices, _vertices) ||
                        !ReferenceEquals(indices, _indices) ||
                        !ReferenceEquals(texture, _texture) ||
                        !ReferenceEquals(technique, _technique) ||
                        lightSelection != _lightSelection ||
                        model.UseSunLight != first.UseSunLight ||
                        !SameCutoutPose(first, model))
                    {
                        Flush(time);
                        if (_disabled)
                            return false;
                    }
                }

                if (_objects.Count == 0)
                {
                    _vertices = vertices;
                    _indices = indices;
                    _boneCount = boneCount;
                    _texture = texture;
                    _technique = technique;
                    _lightSelection = lightSelection;
                }
                if (_objects.Count == _instances.Length)
                    Array.Resize(ref _instances, _instances.Length * 2);
                _instances[_objects.Count] = new CutoutInstanceData
                {
                    // Columns match the constant-buffer layout used by individual draws,
                    // keeping the shader's dot-product evaluation order unchanged.
                    World = Matrix.Transpose(model.WorldPosition),
                    WorldViewProjection = Matrix.Transpose(model.WorldPosition * Camera.Instance.ViewProjection)
                };
                _objects.Add(model);
                return true;
            }

            public void Flush(GameTime time)
            {
                if (_objects.Count == 0)
                    return;

                bool drawn = false;
                try
                {
                    if (!_disabled && _objects.Count > 1)
                    {
                        try
                        {
                            DrawInstances();
                            drawn = true;
                        }
                        catch (Exception ex)
                        {
                            _disabled = true;
                            MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex,
                                "Cutout map instancing disabled; restoring individual draws.");
                        }
                    }

                    if (!drawn)
                    {
                        foreach (ModelObject model in _objects)
                        {
                            try
                            {
                                model.DrawAfter(time);
                            }
                            catch (Exception ex)
                            {
                                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex,
                                    "Cutout map fallback draw failed.");
                            }
                        }
                    }
                }
                finally
                {
                    _objects.Clear();
                }
            }

            private void DrawInstances()
            {
                GraphicsDevice gd = GraphicsManager.Instance.GraphicsDevice;
                Effect effect = GraphicsManager.Instance.DynamicLightingEffect;
                BlendState previousBlend = gd.BlendState;
                DepthStencilState previousDepth = gd.DepthStencilState;
                RasterizerState previousRasterizer = gd.RasterizerState;
                SamplerState previousSampler = gd.SamplerStates[0];
                EffectTechnique previousTechnique = effect.CurrentTechnique;
                try
                {
                    // Use the representative object's exact light selection and shading mode,
                    // not the broader, vertex-lit world selection used by the opaque map queue.
                    ModelObject first = _objects[0];
                    first.PrepareDynamicLightingEffect(effect, true, _boneCount);
                    effect.CurrentTechnique = _technique;
                    ModelEffectBindings bindings = GetModelEffectBindings(effect);
                    bindings.ViewProjection?.SetValue(Camera.Instance.ViewProjection);
                    bindings.DiffuseTexture?.SetValue(_texture);

                    if (_instanceBuffer == null || _instanceBuffer.IsDisposed ||
                        _instanceBuffer.VertexCount < _objects.Count)
                    {
                        _instanceBuffer?.Dispose();
                        _instanceBuffer = new DynamicVertexBuffer(gd, CutoutInstanceData.Declaration,
                            _instances.Length, BufferUsage.WriteOnly);
                    }
                    _instanceBuffer.SetData(_instances, 0, _objects.Count, SetDataOptions.Discard);
                    _vertexBindings[0] = new VertexBufferBinding(_vertices);
                    _vertexBindings[1] = new VertexBufferBinding(_instanceBuffer, 0, 1);
                    gd.SetVertexBuffers(_vertexBindings);
                    gd.Indices = _indices;
                    gd.BlendState = BlendState.Opaque;
                    gd.DepthStencilState = DepthStencilState.Default;
                    gd.RasterizerState = RasterizerState.CullNone;
                    effect.CurrentTechnique.Passes[0].Apply();
                    ApplyQualityModelSampler(gd);
                    gd.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0,
                        _indices.IndexCount / 3, _objects.Count);
                    _staticMapInstancedObjectsThisFrame += _objects.Count;
                    _staticMapInstancedMeshInstancesThisFrame += _objects.Count;
                    _staticMapInstancedBatchesThisFrame++;
                    _staticMapInstancedDrawCallsThisFrame++;
                    _staticMapInstanceUploadsThisFrame++;
                }
                finally
                {
                    gd.SetVertexBuffer(null);
                    gd.Indices = null;
                    gd.BlendState = previousBlend;
                    gd.DepthStencilState = previousDepth;
                    gd.RasterizerState = previousRasterizer;
                    gd.SamplerStates[0] = previousSampler;
                    effect.CurrentTechnique = previousTechnique;
                }
            }

            private static bool SameCutoutPose(ModelObject left, ModelObject right)
            {
                Matrix[] a = left.GetEffectiveBoneTransforms();
                Matrix[] b = right.GetEffectiveBoneTransforms();
                return ReferenceEquals(a, b) ||
                       (a != null && b != null && a.AsSpan().SequenceEqual(b));
            }

            public void Dispose()
            {
                _objects.Clear();
                _instanceBuffer?.Dispose();
                _instanceBuffer = null;
            }
        }

        private bool CanBatchCutoutMapMesh()
        {
            if (!IsStaticMapInstancingSupported() || !IsMapPlacementObject ||
                !AllowMapObjectInstancing || !_contentLoaded || !Visible ||
                Parent != null || Children.Count != 0 || Interactive || IsMouseHover ||
                IsTransparent || TotalAlpha != 1f || UsesMutableMeshData ||
                RequiresPerFrameAnimation || ContinuousAnimation || HasAnimatedCurrentAction() ||
                LinkParentAnimation || ParentBoneLink >= 0 || TextureCoordinateOffset != Vector2.Zero ||
                Model?.Meshes?.Length != 1 || _meshes?.Length != 1 ||
                !_meshes[0].IsRgba || _meshes[0].Texture == null || _meshes[0].Texture.IsDisposed ||
                IsHiddenMesh(0) || IsBlendMesh(0) || GetMeshBlendState(0, false) != BlendState.Opaque ||
                Constants.DRAW_BOUNDING_BOXES || Constants.DRAW_BOUNDING_BOXES_INTERACTIVES)
                return false;

            StaticMapTypeCompatibility compatibility = GetStaticMapTypeCompatibility();
            _defaultCutoutRendering ??=
                IsInheritedFromModelObject(GetType(), nameof(DrawModel), typeof(bool)) &&
                GetType().GetMethod(nameof(GetRenderBoneTransforms),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, new[] { typeof(Matrix[]) }, null)?.DeclaringType == typeof(ModelObject);
            return compatibility.DefaultDrawAfter && compatibility.DefaultMeshRendering &&
                   _defaultCutoutRendering.Value &&
                   DetermineShaderForMesh(0).UseDynamicLighting && CanUseGpuSkinningForMesh(0);
        }
    }
}
