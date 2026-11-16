#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// One loaded BMD drawn several times per frame by its owning effect (the C++ effect
    /// pool renders the same MODEL_* many times with different Position / Angle / Scale /
    /// BlendMeshLight). The renderer is not added to the world; the owner sets
    /// <see cref="WorldObject.World"/>, loads it, ticks <see cref="WorldObject.Update"/> and
    /// calls <see cref="DrawInstance"/> from its own DrawAfter.
    /// </summary>
    internal sealed class SourceModelRenderer : ModelObject
    {
        private readonly string? _modelPath;
        private readonly Client.Data.BMD.BMD? _presetModel;

        public SourceModelRenderer(string modelPath, int blendMesh = -2, int hiddenMesh = -1, bool continuousAnimation = true)
        {
            _modelPath = modelPath;
            HiddenMesh = hiddenMesh;
            BlendMesh = blendMesh;
            ContinuousAnimation = continuousAnimation;
            AnimationSpeed = 1f;
            LightEnabled = false;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            DepthState = DepthStencilState.DepthRead;
            BlendState = Blendings.OneOneAdditive;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendMeshLight = 1f;
        }

        /// <summary>Draws an already loaded BMD (e.g. the caster's weapon model) with normal alpha blending.</summary>
        public SourceModelRenderer(Client.Data.BMD.BMD model)
        {
            _presetModel = model;
            HiddenMesh = -1;
            BlendMesh = -1;
            ContinuousAnimation = false;
            AnimationSpeed = 0f;
            LightEnabled = false;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            DepthState = DepthStencilState.DepthRead;
            BlendState = BlendState.NonPremultiplied;
            BlendMeshLight = 1f;
            _drawOpaquePass = true;
        }

        private sealed class UvScroll : IVertexDeformer, ITexCoordDeformer
        {
            public float OffsetU;
            public float OffsetV;
            public Vector3 DeformVertex(in Client.Data.BMD.BMDTextureVertex vertex, in Vector3 transformedPosition) => transformedPosition;
            public Vector2 DeformTexCoord(float u, float v) => new(u + OffsetU, v + OffsetV);
        }

        private UvScroll? _uvScroll;
        private UvScroll? _meshUvScroll;
        private int _scrollVMesh = -1;

        // DrawModel(true) only draws the transparent plan (blend / RGBA meshes); normally blended
        // models (weapons, stones) also need their opaque meshes.
        private bool _drawOpaquePass;

        /// <summary>o-&gt;BlendMeshTexCoordU; rebuilds the vertex buffer only when the value changes.</summary>
        public float ScrollU
        {
            get => _uvScroll?.OffsetU ?? 0f;
            set
            {
                if (MathF.Abs(ScrollU - value) < 0.0001f)
                    return;

                _uvScroll ??= new UvScroll();
                _uvScroll.OffsetU = value;
                if (_meshUvScroll != null)
                    _meshUvScroll.OffsetU = value;
                InvalidateBuffers(BufferFlagTexture);
            }
        }

        /// <summary>o-&gt;BlendMeshTexCoordV for every mesh; rebuilds the vertex buffer only when the value changes.</summary>
        public float ScrollV
        {
            get => _uvScroll?.OffsetV ?? 0f;
            set
            {
                if (MathF.Abs(ScrollV - value) < 0.0001f)
                    return;

                _uvScroll ??= new UvScroll();
                _uvScroll.OffsetV = value;
                InvalidateBuffers(BufferFlagTexture);
            }
        }

        /// <summary>Sets the V offset of one mesh without shifting the other meshes.</summary>
        public void SetMeshScrollV(int meshIndex, float value)
        {
            if (_scrollVMesh == meshIndex && (_meshUvScroll?.OffsetV ?? 0f) == value)
                return;
            _scrollVMesh = meshIndex;
            _meshUvScroll ??= new UvScroll();
            _meshUvScroll.OffsetU = ScrollU;
            _meshUvScroll.OffsetV = value;
            InvalidateBuffers(BufferFlagTexture);
        }

        protected override IVertexDeformer GetVertexDeformer() => (_uvScroll ?? _meshUvScroll)!;

        protected override IVertexDeformer GetVertexDeformerForMesh(int meshIndex) =>
            (meshIndex == _scrollVMesh ? _meshUvScroll : _uvScroll)!;

        /// <summary>
        /// Bit i set = mesh i is drawn (RenderMesh(i, ...) subsets). Set before loading; one renderer per subset so the
        /// vertex buffers never rebuild for a mask change.
        /// </summary>
        public int VisibleMeshMask { get; set; } = -1;

        protected override bool ShouldRenderMesh(int mesh) => mesh >= 31 || (VisibleMeshMask & (1 << mesh)) != 0;

        /// <summary>Non-blend meshes drawn like RenderObject (normal alpha blending), blend meshes additive.</summary>
        public void UseNormalBlend()
        {
            BlendState = BlendState.NonPremultiplied;
            _drawOpaquePass = true;
        }

        /// <summary>RenderBody(..., Texture): every mesh drawn with one replacement texture.</summary>
        public void OverrideAllTextures(Texture2D? texture)
        {
            int count = Model?.Meshes?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (texture != null)
                    SetMeshTextureOverride(i, texture);
                else
                    ClearMeshTextureOverride(i);
            }
        }

        /// <summary>Keeps one-shot animations on their last key instead of looping.</summary>
        public bool HoldLastFrame
        {
            get => HoldOnLastFrame;
            set => HoldOnLastFrame = value;
        }

        /// <summary>Jumps the animation clock (in frames) - used to sync with the caster action.</summary>
        public void SetAnimationTime(double frame) => _animTime = Math.Max(0d, frame);

        public override async Task Load()
        {
            Model = _presetModel ?? await BMDLoader.Instance.Prepare(_modelPath!);
            await base.Load();
        }

        /// <summary>Loads the renderer into <paramref name="world"/> without registering it as a world object.</summary>
        public async Task<bool> LoadInto(WorldControl? world)
        {
            if (world == null)
                return false;

            World = world;
            await Load();
            return Status == GameControlStatus.Ready && Model != null;
        }

        /// <summary>
        /// TransformByObjectBone(result, o, bone) for an instance pose; call after SetAnimationTime + Update.
        /// </summary>
        public bool TryGetInstanceBone(int bone, Vector3 position, Vector3 angleRadians, float scale, out Vector3 world)
        {
            Matrix[] bones = GetBoneTransforms();
            if (Status != GameControlStatus.Ready || bones == null || bone < 0 || bone >= bones.Length)
            {
                world = position;
                return false;
            }

            Position = position;
            Angle = angleRadians;
            Scale = scale;
            world = Vector3.Transform(bones[bone].Translation, WorldPosition);
            return true;
        }

        /// <summary>
        /// Animation(BoneTransform, frame, ...) + TransformByObjectBone(result, o, bone) for an instance pose, without
        /// touching the renderer's own animation clock.
        /// </summary>
        public bool TryGetInstanceBoneAt(double frame, int bone, Vector3 position, Vector3 angleRadians, float scale, out Vector3 world)
        {
            if (Status != GameControlStatus.Ready || !TryComputeBoneAt(0, frame, bone, out Matrix boneModel))
            {
                world = position;
                return false;
            }

            Position = position;
            Angle = angleRadians;
            Scale = scale;
            world = Vector3.Transform(boneModel.Translation, WorldPosition);
            return true;
        }

        /// <summary>
        /// Second RenderBody(RENDER_BRIGHT | RENDER_CHROME) pass after the normal one: every visible mesh additive with
        /// chrome UVs, coloured by the instance light (times alpha when alpha &lt; 0.99, as RenderMesh does for BRIGHT).
        /// </summary>
        public bool ChromeBrightPass { get; set; }

        /// <summary>True: the chrome pass uses RENDER_CHROME5 (RenderBody(... RENDER_CHROME5 | RENDER_BRIGHT)) instead of RENDER_CHROME.</summary>
        public bool ChromePass5 { get; set; }

        /// <summary>HiddenMesh argument of the chrome RenderBody call (-1: every mesh).</summary>
        public int ChromePassHiddenMesh { get; set; } = -1;

        public void DrawInstance(Vector3 position, Vector3 angleRadians, float scale, Vector3 light, float blendMeshLight, float alpha = 1f)
        {
            if (Status != GameControlStatus.Ready || Model == null || blendMeshLight <= 0.002f || alpha <= 0.002f)
                return;

            Position = position;
            Angle = angleRadians;
            Scale = scale;
            Light = light;
            BlendMeshLight = blendMeshLight;
            Alpha = alpha;

            // Buffers are only built in ModelObject.Update; this renderer is drawn without being ticked.
            EnsureDynamicBuffers();

            GraphicsDevice device = GraphicsDevice;
            RasterizerState previousRasterizer = device.RasterizerState;
            try
            {
                device.RasterizerState = RasterizerState.CullNone;
                GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
                GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
                GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;
                if (_drawOpaquePass)
                    DrawModel(false);
                DrawModel(true);
                if (ChromeBrightPass && Model.Meshes != null)
                {
                    Vector3 chromeLight = alpha < 0.99f ? light * alpha : light;
                    for (int i = 0; i < Model.Meshes.Length; i++)
                    {
                        if (i != ChromePassHiddenMesh)
                            DrawMeshItemOverlay(i, ChromePass5 ? ItemOverlayMode.Chrome5 : ItemOverlayMode.Chrome, chromeLight);
                    }
                }
            }
            finally
            {
                device.RasterizerState = previousRasterizer;
            }
        }
    }
}
