using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private readonly struct ShadowRebuildJob
        {
            public ShadowRebuildJob(ModelObject owner, TerrainShadowMeshCache cache, Matrix shadowWorld)
            {
                Owner = owner;
                Cache = cache;
                ShadowWorld = shadowWorld;
            }

            public ModelObject Owner { get; }
            public TerrainShadowMeshCache Cache { get; }
            public Matrix ShadowWorld { get; }
        }

        private static readonly List<ShadowRebuildJob> ShadowRebuildJobs = new(512);
        private static int _parallelShadowRebuilds;
        private static Task _shadowPrepassTask;

        /// <summary>Diagnostics: main-thread time spent waiting for the background shadow rebuild.</summary>
        public static long DiagShadowPrepassWaitTicks;

        /// <summary>
        /// Starts rebuilding the terrain-conformed shadow meshes of the visible objects on worker threads at the
        /// beginning of the frame. The main thread keeps drawing (terrain, map) and joins the work at the first
        /// shadow cache access (<see cref="CompleteShadowPrepass"/>); the draw pass then finds the caches up to
        /// date for the same pose and shadow matrix. Skinning every shadow vertex, projecting it and sampling the
        /// terrain under it was the largest CPU cost left on the main thread in player crowds.
        /// </summary>
        internal static void PrepareProjectedShadows(WorldControl world)
        {
            CompleteShadowPrepass();
            ShadowRebuildJobs.Clear();
            if (!Constants.ENABLE_PARALLEL_SHADOW_PREPASS || world?.Terrain == null || !world.EnableShadows ||
                !Constants.ENABLE_MERGED_MESH_SHADOWS || world.Terrain.HasSnowShadowSurface)
                return;
            if (Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight)
                return;

            var visible = world.VisibleObjects;
            for (int i = 0; i < visible.Count; i++)
                CollectShadowRebuildJobs(visible[i], 0);

            int count = ShadowRebuildJobs.Count;
            if (count == 0)
                return;

            var terrain = world.Terrain;
            _shadowPrepassTask = Task.Run(() => Parallel.For(0, count, index =>
            {
                ShadowRebuildJob job = ShadowRebuildJobs[index];
                if (job.Owner.TryUpdateTerrainShadowMeshCacheCore(job.Cache, job.ShadowWorld, terrain, out bool rebuilt) && rebuilt)
                    Interlocked.Increment(ref _parallelShadowRebuilds);
            }));
        }

        /// <summary>
        /// Waits for the background shadow rebuild of this frame. Called before any shadow cache is used and at
        /// the end of the frame, before the next update changes poses again.
        /// </summary>
        internal static void CompleteShadowPrepass()
        {
            Task task = _shadowPrepassTask;
            if (task == null)
                return;

            _shadowPrepassTask = null;
            try
            {
                if (!task.IsCompleted)
                {
                    long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    task.Wait();
                    DiagShadowPrepassWaitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - waitStarted;
                }
            }
            catch (AggregateException ex)
            {
                // A failed job leaves its cache as it was; the draw pass rebuilds it on the main thread.
                AppLoggerFactory?.CreateLogger<ModelObject>()?.LogDebug(ex, "Background shadow rebuild failed");
            }

            DiagShadowRebuilds += _parallelShadowRebuilds;
            _parallelShadowRebuilds = 0;
            ShadowRebuildJobs.Clear();
        }

        private static void CollectShadowRebuildJobs(WorldObject obj, int depth)
        {
            if (obj == null || !obj.Visible || depth > 4)
                return;

            if (obj is ModelObject model)
                model.CollectOwnShadowRebuildJobs();

            foreach (WorldObject child in obj.Children)
                CollectShadowRebuildJobs(child, depth + 1);
        }

        // Mirrors the projected-shadow decisions of DrawModel / DrawProjectedShadowPass for the opaque pass.
        private void CollectOwnShadowRebuildJobs()
        {
            if (!RenderShadow || LowQuality || _meshes == null || Model?.Meshes == null ||
                !CanUseCachedTerrainConformedShadowPath() || UsesRenderedShadowMapForCurrentObject())
                return;
            if ((LinkParentAnimation || ParentBoneLink >= 0) && !IsPlayerOrNpcShadowPart())
                return;
            if (ShouldUseBlobShadowForCurrentPass() || GetVertexDeformer() != null)
                return;
            // Actor parts on the GPU shadow path have no CPU shadow mesh to rebuild.
            if (CanUseGpuActorShadows())
                return;
            if (!TryGetShadowMatrix(out Matrix shadowWorld) ||
                !ValidateWorldMatrix(WorldPosition) || !ValidateWorldMatrix(shadowWorld))
                return;

            List<int> meshes = CollectOpaqueShadowMeshes();
            for (int n = 0; n < meshes.Count; n++)
            {
                int mesh = meshes[n];
                if ((uint)mesh >= (uint)_meshes.Length || IsHiddenMesh(mesh) || IsStaticMapMeshQueuedForInstancing(mesh))
                    continue;

                var ms = _meshes[mesh];
                if (ms?.Texture == null || ms.IsRgba ||
                    !TryGetOrCreateTerrainShadowMeshCache(mesh, out TerrainShadowMeshCache cache))
                    continue;

                ShadowRebuildJobs.Add(new ShadowRebuildJob(this, cache, shadowWorld));
            }
        }
    }
}
