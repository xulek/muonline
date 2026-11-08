using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    // Player and NPC actors are drawn part by part (body, helm, armor, weapons, wings), and their per-mesh draws
    // alternate between the DynamicLighting, ItemMaterial and MonsterMaterial effects. Every effect switch costs
    // the integrated GPU driver several microseconds more than the draw itself. In the opaque world pass these
    // per-mesh draws are queued and submitted grouped by effect: opaque meshes sorted by shader and texture
    // (depth testing makes their order irrelevant), blended meshes afterwards in their original order. The
    // queue is flushed before anything that needs the meshes already drawn: item overlays on the same
    // geometry, non-actor objects of the list and the end of the list.
    public partial class ModelObject
    {
        private readonly struct DeferredMeshDraw
        {
            public DeferredMeshDraw(ModelObject owner, int mesh, long sortKey)
            {
                Owner = owner;
                Mesh = mesh;
                SortKey = sortKey;
            }

            public ModelObject Owner { get; }
            public int Mesh { get; }
            public long SortKey { get; }
        }

        private static bool s_deferActorMeshDraws;
        private static bool s_flushingDeferredMeshDraws;
        private static readonly List<DeferredMeshDraw> s_deferredOpaqueMeshDraws = new(512);
        private static readonly List<DeferredMeshDraw> s_deferredBlendedMeshDraws = new(128);
        private static readonly Comparison<DeferredMeshDraw> s_deferredMeshOrder = (a, b) => a.SortKey.CompareTo(b.SortKey);

        internal static void BeginDeferredActorMeshDraws()
        {
            s_deferredOpaqueMeshDraws.Clear();
            s_deferredBlendedMeshDraws.Clear();
            s_deferActorMeshDraws = Constants.ENABLE_DEFERRED_ACTOR_MESH_DRAWS;
        }

        internal static void EndDeferredActorMeshDraws(GraphicsDevice graphicsDevice)
        {
            try
            {
                FlushDeferredActorMeshDraws(graphicsDevice);
            }
            finally
            {
                s_deferActorMeshDraws = false;
            }
        }

        /// <summary>Submits the queued actor mesh draws: opaque ones grouped by effect, then the blended ones.</summary>
        internal static void FlushDeferredActorMeshDraws(GraphicsDevice graphicsDevice)
        {
            if (s_flushingDeferredMeshDraws ||
                (s_deferredOpaqueMeshDraws.Count == 0 && s_deferredBlendedMeshDraws.Count == 0))
                return;

            s_flushingDeferredMeshDraws = true;
            try
            {
                // List.Sort is unstable; the insertion index in the key's low bits keeps equal keys in order.
                s_deferredOpaqueMeshDraws.Sort(s_deferredMeshOrder);
                graphicsDevice.DepthStencilState = DepthStencilState.Default;
                DrawDeferredMeshList(s_deferredOpaqueMeshDraws);
                graphicsDevice.DepthStencilState = DepthStencilState.Default;
                DrawDeferredMeshList(s_deferredBlendedMeshDraws);
            }
            finally
            {
                s_deferredOpaqueMeshDraws.Clear();
                s_deferredBlendedMeshDraws.Clear();
                s_flushingDeferredMeshDraws = false;
            }
        }

        // Mesh index of a queued merged player body (ModelObject.PlayerComposite).
        private const int DeferredCompositeMesh = -1;

        private static void DrawDeferredMeshList(List<DeferredMeshDraw> draws)
        {
            // Only mesh draws happen here: consecutive meshes of one object share their lighting setup, and the
            // blend and rasterizer states the draws change are restored once at the end.
            GraphicsDevice gd = MuGame.Instance.GraphicsDevice;
            BlendState blend = gd.BlendState;
            RasterizerState rasterizer = gd.RasterizerState;
            int enclosingScope = BeginMeshDrawScope();
            try
            {
                for (int i = 0; i < draws.Count; i++)
                {
                    DeferredMeshDraw draw = draws[i];
                    if (draw.Owner == null || draw.Owner.Status == Models.GameControlStatus.Disposed || !draw.Owner.Visible)
                        continue;
                    draw.Owner.DrawDeferred(draw.Mesh);
                }
            }
            finally
            {
                EndMeshDrawScope(enclosingScope);
                SetMeshBlendState(gd, blend);
                SetMeshRasterizerState(gd, rasterizer);
            }
        }

        private void DrawDeferred(int mesh)
        {
            if (mesh == DeferredCompositeMesh)
                DrawDeferredComposite();
            else
                DrawMesh(mesh);
        }

        // The after pass (transparent meshes) queues the actors' additive blend meshes: they write no depth and
        // their blending is commutative, so grouping them by effect leaves the image unchanged. In the after pass
        // of the opaque list, whose objects are sorted for batching and not by depth, the depth-writing RGBA
        // meshes are queued too. Every other draw of the pass flushes the queue first.
        private static bool s_deferActorAfterMeshDraws;
        private static bool s_deferActorAfterRgbaMeshDraws;
        private static readonly List<DeferredMeshDraw> s_deferredAdditiveMeshDraws = new(256);

        internal static void BeginDeferredActorAfterMeshDraws(bool includeRgbaMeshes)
        {
            s_deferredAdditiveMeshDraws.Clear();
            s_deferActorAfterMeshDraws = Constants.ENABLE_DEFERRED_ACTOR_MESH_DRAWS;
            s_deferActorAfterRgbaMeshDraws = includeRgbaMeshes;
        }

        internal static void EndDeferredActorAfterMeshDraws(GraphicsDevice graphicsDevice)
        {
            try
            {
                FlushDeferredActorAfterMeshDraws(graphicsDevice);
            }
            finally
            {
                s_deferActorAfterMeshDraws = false;
            }
        }

        /// <summary>Submits the queued additive actor meshes of the after pass, grouped by effect.</summary>
        internal static void FlushDeferredActorAfterMeshDraws(GraphicsDevice graphicsDevice)
        {
            if (s_flushingDeferredMeshDraws || s_deferredAdditiveMeshDraws.Count == 0)
                return;

            s_flushingDeferredMeshDraws = true;
            DepthStencilState depthState = graphicsDevice.DepthStencilState;
            try
            {
                s_deferredAdditiveMeshDraws.Sort(s_deferredMeshOrder);
                DrawDeferredMeshList(s_deferredAdditiveMeshDraws);
            }
            finally
            {
                s_deferredAdditiveMeshDraws.Clear();
                graphicsDevice.DepthStencilState = depthState;
                s_flushingDeferredMeshDraws = false;
            }
        }

        private static bool IsCommutativeBlend(BlendState blend) =>
            blend != null &&
            blend.ColorBlendFunction == BlendFunction.Add && blend.ColorDestinationBlend == Blend.One &&
            blend.AlphaBlendFunction == BlendFunction.Add && blend.AlphaDestinationBlend == Blend.One;

        /// <summary>
        /// Queues the after-pass draws of a mesh group of an actor part when all of them are additive blend
        /// meshes; false (nothing queued) when the group must draw now.
        /// </summary>
        private bool TryDeferActorAfterMeshGroup(MeshShaderKind shaderKind, List<int> meshIndices)
        {
            if (!s_deferActorAfterMeshDraws || s_flushingDeferredMeshDraws ||
                shaderKind == MeshShaderKind.AlphaTest || _activeOverlayMode != ItemOverlayMode.None ||
                !IsPlayerOrNpcShadowPart())
                return false;

            for (int i = 0; i < meshIndices.Count; i++)
            {
                int mesh = meshIndices[i];
                bool queueable = IsBlendMesh(mesh)
                    ? IsCommutativeBlend(GetMeshBlendState(mesh, true))
                    : s_deferActorAfterRgbaMeshDraws;
                if (!queueable)
                    return false;
            }

            for (int i = 0; i < meshIndices.Count; i++)
            {
                int mesh = meshIndices[i];
                int variant = CanUseGpuSkinningForMesh(mesh) ? 1 : 0;
                long sortKey = ((long)shaderKind << 56) | ((long)variant << 48) |
                               (uint)(s_deferredAdditiveMeshDraws.Count & 0xFFFFFF);
                s_deferredAdditiveMeshDraws.Add(new DeferredMeshDraw(this, mesh, sortKey));
            }
            return true;
        }

        private static bool CanDeferActorDraw() => s_deferActorMeshDraws && !s_flushingDeferredMeshDraws;

        /// <summary>Queues this player's merged body; it is drawn first, before the other actor meshes.</summary>
        private void QueueDeferredComposite() =>
            s_deferredOpaqueMeshDraws.Add(new DeferredMeshDraw(this, DeferredCompositeMesh, s_deferredOpaqueMeshDraws.Count & 0xFFFFFF));

        /// <summary>
        /// Submits only this object's queued meshes, for a pass drawn on top of them (item overlays). The other
        /// actors' queued meshes keep their grouping.
        /// </summary>
        private void FlushOwnDeferredMeshDraws()
        {
            if (s_flushingDeferredMeshDraws ||
                (s_deferredOpaqueMeshDraws.Count == 0 && s_deferredBlendedMeshDraws.Count == 0 &&
                 s_deferredAdditiveMeshDraws.Count == 0))
                return;

            s_flushingDeferredMeshDraws = true;
            try
            {
                DrawAndRemoveOwn(s_deferredOpaqueMeshDraws);
                DrawAndRemoveOwn(s_deferredBlendedMeshDraws);
                DrawAndRemoveOwn(s_deferredAdditiveMeshDraws);
            }
            finally
            {
                s_flushingDeferredMeshDraws = false;
            }
        }

        private void DrawAndRemoveOwn(List<DeferredMeshDraw> draws)
        {
            for (int i = 0; i < draws.Count; i++)
            {
                DeferredMeshDraw draw = draws[i];
                if (!ReferenceEquals(draw.Owner, this))
                    continue;
                // Keep the slot (and the insertion order of the rest); a null owner is skipped later.
                draws[i] = new DeferredMeshDraw(null, draw.Mesh, draw.SortKey);
                if (Status != Models.GameControlStatus.Disposed && Visible)
                    DrawDeferred(draw.Mesh);
            }
        }

        /// <summary>Queues a per-mesh draw of an actor part in the opaque pass; false when it must draw now.</summary>
        private bool TryDeferActorMeshDraw(int mesh, MeshShaderKind shaderKind, BlendState groupBlend)
        {
            if (!CanDeferActorDraw() || shaderKind == MeshShaderKind.AlphaTest || !IsPlayerOrNpcShadowPart())
                return false;

            bool opaque = ReferenceEquals(groupBlend, BlendState.Opaque) && !IsBlendMesh(mesh);
            if (!opaque)
            {
                s_deferredBlendedMeshDraws.Add(new DeferredMeshDraw(this, mesh, s_deferredBlendedMeshDraws.Count));
                return true;
            }

            // Effect first, then the technique variant the draw will pick (skinned, vertex lit), then submission
            // order: the meshes of one object stay next to each other and share their lighting setup
            // (PrepareDynamicLightingEffectOncePerScope). Texture changes measured cheaper than that setup.
            int variant = CanUseGpuSkinningForMesh(mesh) ? 1 : 0;
            if (shaderKind == MeshShaderKind.DynamicLighting && ShouldUseVertexDynamicLighting(WorldPosition.Translation))
                variant |= 2;
            long sortKey = ((long)shaderKind << 56) | ((long)variant << 48) |
                           (uint)(s_deferredOpaqueMeshDraws.Count & 0xFFFFFF);
            s_deferredOpaqueMeshDraws.Add(new DeferredMeshDraw(this, mesh, sortKey));
            return true;
        }
    }
}
