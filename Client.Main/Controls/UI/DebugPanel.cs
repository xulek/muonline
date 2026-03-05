using System.Text;
using Client.Main.Controllers;
using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI
{
    public class DebugPanel : UIControl
    {
        private LabelControl _fpsLabel;
        private LabelControl _mousePosLabel;
        private LabelControl _playerCordsLabel;
        private LabelControl _mapTileLabel;
        private LabelControl _effectsStatusLabel;
        private LabelControl _objectCursorLabel;
        private LabelControl _tileFlagsLabel;
        private LabelControl _performanceMetricsLabel;
        private LabelControl _bmdMetricsLabel;    // New label for BMD buffer metrics
        private LabelControl _batchSortingLabel;   // NEW: Batch sorting status
        private LabelControl _instancingStatusLabel;
        private LabelControl _lightingStatusLabel; // NEW: Lighting mode status
        private LabelControl _gpuSkinningStatusLabel;
        private double _fastUpdateTimer = 0;
        private double _slowUpdateTimer = 0;
        private const double FAST_UPDATE_INTERVAL_MS = 100;
        private const double SLOW_UPDATE_INTERVAL_MS = 500;
        private StringBuilder _sb = new StringBuilder(350); // Increased capacity for new metrics

        public DebugPanel()
        {
            Align = ControlAlign.Top | ControlAlign.Right;
            Margin = new Margin { Top = 10, Right = 10 };
            Padding = new Margin { Top = 15, Left = 15 };
            ControlSize = new Point(400, 345); // Increased size for lighting + pooling metrics
            BackgroundColor = Color.Black * 0.6f;
            BorderColor = Color.White * 0.3f;
            BorderThickness = 2;

            var posX = Padding.Left;
            var posY = Padding.Top;
            var labelHeight = 20;

            Controls.Add(_fpsLabel = new LabelControl { Text = "FPS: {0}", TextColor = Color.LightGreen, X = posX, Y = posY });
            Controls.Add(_mousePosLabel = new LabelControl { Text = "Mouse Position - X: {0}, Y:{1}", TextColor = Color.LightBlue, X = posX, Y = posY += labelHeight });
            Controls.Add(_playerCordsLabel = new LabelControl { Text = "Player Cords - X: {0}, Y:{1}", TextColor = Color.LightCoral, X = posX, Y = posY += labelHeight });
            Controls.Add(_mapTileLabel = new LabelControl { Text = "MAP Tile - X: {0}, Y:{1}", TextColor = Color.LightYellow, X = posX, Y = posY += labelHeight });
            Controls.Add(_effectsStatusLabel = new LabelControl { Text = "FXAA: {0} - AlphaRGB:{1}", TextColor = Color.Yellow, X = posX, Y = posY += labelHeight });
            Controls.Add(_objectCursorLabel = new LabelControl { Text = "Cursor Object: {0}", TextColor = Color.CadetBlue, X = posX, Y = posY += labelHeight });
            Controls.Add(_tileFlagsLabel = new LabelControl { Text = "Tile Flags: {0}", TextColor = Color.Lime, X = posX, Y = posY += labelHeight });
            Controls.Add(_lightingStatusLabel = new LabelControl { Text = "Lighting: {0}", TextColor = Color.White, X = posX, Y = posY += labelHeight });
            Controls.Add(_gpuSkinningStatusLabel = new LabelControl { Text = "GPU Skin: {0}", TextColor = Color.LightGoldenrodYellow, X = posX, Y = posY += labelHeight });
            Controls.Add(_performanceMetricsLabel = new LabelControl { Text = "Perf: {0}", TextColor = Color.OrangeRed, X = posX, Y = posY += labelHeight });
            Controls.Add(_bmdMetricsLabel = new LabelControl { Text = "BMD: {0}", TextColor = Color.LightSkyBlue, X = posX, Y = posY += labelHeight });
            Controls.Add(_batchSortingLabel = new LabelControl { Text = "Batch: {0}", TextColor = Color.Magenta, X = posX, Y = posY += labelHeight });
            Controls.Add(_instancingStatusLabel = new LabelControl { Text = "Instancing: {0}", TextColor = Color.LightPink, X = posX, Y = posY += labelHeight });
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible) return;

            _fastUpdateTimer += gameTime.ElapsedGameTime.TotalMilliseconds;
            _slowUpdateTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

            bool shouldRunFastUpdate = _fastUpdateTimer >= FAST_UPDATE_INTERVAL_MS;
            bool shouldRunSlowUpdate = _slowUpdateTimer >= SLOW_UPDATE_INTERVAL_MS;
            if (!shouldRunFastUpdate && !shouldRunSlowUpdate)
            {
                return;
            }

            if (shouldRunFastUpdate)
            {
                _fastUpdateTimer = 0;
            }

            if (shouldRunSlowUpdate)
            {
                _slowUpdateTimer = 0;
            }

            if (shouldRunFastUpdate)
            {
                SetLabelTextIfChanged(_fpsLabel, $"FPS: {(int)FPSCounter.Instance.FPS_AVG}, UPS: {(int)UPSCounter.Instance.UPS_AVG}");

                Point screenMouse = MuGame.Instance.Mouse.Position;
                Point uiMouse = MuGame.Instance.UiMouseState.Position;
                SetLabelTextIfChanged(_mousePosLabel, $"Mouse Screen (X:{screenMouse.X}, Y:{screenMouse.Y}) UI (X:{uiMouse.X}, Y:{uiMouse.Y})");

                var fxaa = GraphicsManager.Instance.IsFXAAEnabled ? "ON" : "OFF";
                var alphargb = GraphicsManager.Instance.IsAlphaRGBEnabled ? "ON" : "OFF";
                SetLabelTextIfChanged(_effectsStatusLabel, $"FXAA: {fxaa} - AlphaRGB: {alphargb}");

                var cursorObj = World?.Scene?.MouseHoverObject != null ? World.Scene.MouseHoverObject.GetType().Name : "N/A";
                SetLabelTextIfChanged(_objectCursorLabel, $"Cursor Object: {cursorObj}");
            }

            if (World is WalkableWorldControl walkableWorld && walkableWorld.Walker != null)
            {
                _playerCordsLabel.Visible = true;
                _mapTileLabel.Visible = true;
                _tileFlagsLabel.Visible = true;
                _performanceMetricsLabel.Visible = true;
                _bmdMetricsLabel.Visible = true;
                _batchSortingLabel.Visible = true;
                _lightingStatusLabel.Visible = true;
                _gpuSkinningStatusLabel.Visible = true;
                _instancingStatusLabel.Visible = true;

                if (shouldRunFastUpdate)
                {
                    SetLabelTextIfChanged(_playerCordsLabel, $"Player Cords - X: {walkableWorld.Walker.Location.X}, Y: {walkableWorld.Walker.Location.Y}");
                    SetLabelTextIfChanged(_mapTileLabel, $"MAP Tile - X: {walkableWorld.MouseTileX}, Y: {walkableWorld.MouseTileY}");
                }

                if (shouldRunSlowUpdate)
                {
                    var flags = walkableWorld.Terrain.RequestTerrainFlag((int)walkableWorld.Walker.Location.X,
                                                                         (int)walkableWorld.Walker.Location.Y);
                    SetLabelTextIfChanged(_tileFlagsLabel, $"Tile Flags: {flags}");

                    bool terrainGpu = walkableWorld.Terrain?.IsGpuTerrainLighting == true;
                    bool shaderAvailable = walkableWorld.Terrain?.IsDynamicLightingShaderAvailable == true;
                    bool objectsGpu = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER && GraphicsManager.Instance.DynamicLightingEffect != null;
                    int registeredDynamicLights = walkableWorld.Terrain?.LastFrameRegisteredDynamicLights ?? 0;
                    int activeDynamicLights = walkableWorld.Terrain?.LastFrameActiveDynamicLights ?? 0;
                    int visibleDynamicLights = walkableWorld.Terrain?.LastFrameVisibleDynamicLights ?? 0;
                    int uploadedTerrainLights = walkableWorld.Terrain?.LastUploadedDynamicLights ?? 0;
                    int prunedDynamicLights = walkableWorld.Terrain?.DynamicLightsOrphansPruned ?? 0;
                    int rejectedDynamicAdds = walkableWorld.Terrain?.DynamicLightsDuplicateAddsRejected ?? 0;
                    _sb.Clear()
                      .Append("Lighting: Terrain=")
                      .Append(terrainGpu ? "GPU" : "CPU")
                      .Append(shaderAvailable ? "" : " (shader missing)")
                      .Append(" | Objects=")
                      .Append(objectsGpu ? "GPU" : "CPU")
                      .Append(" | DynReg:")
                      .Append(registeredDynamicLights)
                      .Append(" Act:")
                      .Append(activeDynamicLights)
                      .Append(" Vis:")
                      .Append(visibleDynamicLights)
                      .Append(" UpT:")
                      .Append(uploadedTerrainLights)
                      .Append(" Prn:")
                      .Append(prunedDynamicLights)
                      .Append(" Dup:")
                      .Append(rejectedDynamicAdds);
                    SetLabelTextIfChanged(_lightingStatusLabel, _sb.ToString());

                    _sb.Clear()
                      .Append("GPU Skin: Flag=")
                      .Append(Constants.ENABLE_GPU_SKINNING ? "ON" : "OFF")
                      .Append(" | Backend=")
                      .Append(ModelObject.IsGpuSkinningBackendSupported ? "OK" : "N/A")
                      .Append(" | Drawn=")
                      .Append(ModelObject.LastFrameGpuSkinnedMeshesDrawn);
                    SetLabelTextIfChanged(_gpuSkinningStatusLabel, _sb.ToString());

                    // Update terrain performance metrics display
                    var terrainMetrics = walkableWorld.Terrain.FrameMetrics;
                    int queuedMainThreadActions = MuGame.MainThreadPendingActions;
                    int processedMainThreadActions = MuGame.MainThreadProcessedActionsLastFrame;
                    int simulationSteps = MuGame.LastSimulationStepCount;
                    int queuedSchedulerTasks = MuGame.TaskScheduler?.QueuedTaskCount ?? 0;
                    int processedSchedulerTasks = MuGame.TaskScheduler?.LastFrameProcessedTasks ?? 0;
                    _sb.Clear()
                       .Append($"Terrain: Drw:{terrainMetrics.DrawCalls} ")
                       .Append($"Tri:{terrainMetrics.DrawnTriangles} ")
                       .Append($"Blk:{terrainMetrics.DrawnBlocks} ")
                       .Append($"Cel:{terrainMetrics.DrawnCells} ")
                       .Append($"| Cull:{(walkableWorld.LastCullWasRebuild ? "R" : "-")} ")
                       .Append($"C:{walkableWorld.LastCullCandidateCount} ")
                       .Append($"V:{walkableWorld.LastCullVisibleCount} ")
                       .Append($"Ms:{walkableWorld.LastCullRebuildMs:F2} ")
                       .Append($"CamV:{walkableWorld.LastCullCameraVersion} ")
                       .Append($"| Sim:{simulationSteps} ")
                       .Append($"MT:{processedMainThreadActions}/{queuedMainThreadActions} ")
                       .Append($"TS:{processedSchedulerTasks}/{queuedSchedulerTasks}");
                    SetLabelTextIfChanged(_performanceMetricsLabel, _sb.ToString());

                    // Update BMD buffer metrics
                    var bmd = BMDLoader.Instance;
                    _sb.Clear()
                      .Append($"BMD: VB:{bmd.LastFrameVBUpdates} IB:{bmd.LastFrameIBUploads} ")
                      .Append($"Vtx:{bmd.LastFrameVerticesTransformed} Mesh:{bmd.LastFrameMeshesProcessed} ")
                      .Append($"Cache:{bmd.LastFrameCacheHits}/{bmd.LastFrameCacheMisses}");
                    SetLabelTextIfChanged(_bmdMetricsLabel, _sb.ToString());

                    // Update batch sorting status
                    _sb.Clear()
                      .Append($"Batch Sort: {(Constants.ENABLE_BATCH_OPTIMIZED_SORTING ? "ON" : "OFF")} ")
                      .Append($"(Model grouping for state reduction)");
                    SetLabelTextIfChanged(_batchSortingLabel, _sb.ToString());
                    _batchSortingLabel.Visible = true;

                    _sb.Clear()
                      .Append("Instancing: ")
                      .Append(Constants.ENABLE_MAP_OBJECT_INSTANCING ? "ON" : "OFF")
                      .Append(" | BK:")
                      .Append(ModelObject.IsStaticMapInstancingBackendSupported ? "OK" : "N/A")
                      .Append(" | RT:")
                      .Append(ModelObject.IsStaticMapInstancingRuntimeDisabled ? "OFF" : "OK")
                      .Append(" | Obj:")
                      .Append(ModelObject.LastFrameStaticMapInstancedObjects)
                      .Append(" Inst:")
                      .Append(ModelObject.LastFrameStaticMapInstancedMeshInstances)
                      .Append(" B:")
                      .Append(ModelObject.LastFrameStaticMapInstancedBatches)
                      .Append(" DC:")
                      .Append(ModelObject.LastFrameStaticMapInstancedDrawCalls)
                      .Append(" FB:")
                      .Append(ModelObject.LastFrameStaticMapInstancingFallbacks);
                    SetLabelTextIfChanged(_instancingStatusLabel, _sb.ToString());
                }
            }
            else
            {
                _playerCordsLabel.Visible = false;
                _mapTileLabel.Visible = false;
                _tileFlagsLabel.Visible = false;
                _performanceMetricsLabel.Visible = false;
                _bmdMetricsLabel.Visible = false;
                _batchSortingLabel.Visible = false;
                _lightingStatusLabel.Visible = false;
                _gpuSkinningStatusLabel.Visible = false;
                _instancingStatusLabel.Visible = false;
            }
        }

        private static void SetLabelTextIfChanged(LabelControl label, string value)
        {
            if (label.Text != value)
            {
                label.Text = value;
            }
        }
    }
}
