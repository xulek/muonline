using System;
using System.Text;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI
{
    public class DebugPanel : UIControl
    {
        private const double FastUpdateIntervalMs = 100;
        private const double SlowUpdateIntervalMs = 500;
        private const int PanelWidth = 790;
        private const int PanelHeight = 452;
        private const int CompactWidth = 520;
        private const int CompactHeight = 62;

        private readonly LabelControl _fpsLabel;
        private readonly LabelControl _mousePosLabel;
        private readonly LabelControl _playerCordsLabel;
        private readonly LabelControl _mapTileLabel;
        private readonly LabelControl _effectsStatusLabel;
        private readonly LabelControl _objectCursorLabel;
        private readonly LabelControl _tileFlagsLabel;
        private readonly LabelControl _performanceMetricsLabel;
        private readonly LabelControl _bmdMetricsLabel;
        private readonly LabelControl _batchSortingLabel;
        private readonly LabelControl _instancingStatusLabel;
        private readonly LabelControl _lightingStatusLabel;
        private readonly LabelControl _gpuSkinningStatusLabel;
        private readonly StringBuilder _sb = new(512);
        private readonly float[] _frameHistory = new float[72];

        private double _fastUpdateTimer;
        private double _slowUpdateTimer;
        private int _historyIndex;
        private string _healthText = "WARMING UP";
        private Color _healthColor = ModernHudTheme.TextGray;
        private bool _expanded;

        public DebugPanel()
        {
            Align = ControlAlign.Top | ControlAlign.Right;
            Margin = new Margin { Top = 10, Right = 10 };
            AutoViewSize = false;
            ControlSize = new Point(PanelWidth, PanelHeight);
            ViewSize = ControlSize;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;
            Interactive = false;

            _fpsLabel = AddLabel(28, 87, 13.5f, ModernHudTheme.Success, bold: true);
            _mousePosLabel = AddLabel(28, 119, 10f, ModernHudTheme.TextGray);
            _playerCordsLabel = AddLabel(28, 143, 10f, ModernHudTheme.TextWhite);
            _mapTileLabel = AddLabel(28, 167, 10f, ModernHudTheme.TextGray);
            _objectCursorLabel = AddLabel(28, 191, 10f, ModernHudTheme.SecondaryBright);
            _tileFlagsLabel = AddLabel(28, 211, 9.5f, ModernHudTheme.TextDark);

            _effectsStatusLabel = AddLabel(28, 268, 10f, ModernHudTheme.TextGray);
            _lightingStatusLabel = AddLabel(28, 293, 9.7f, ModernHudTheme.TextWhite);
            _gpuSkinningStatusLabel = AddLabel(28, 352, 9.7f, ModernHudTheme.TextWhite);
            _batchSortingLabel = AddLabel(28, 409, 9.5f, ModernHudTheme.TextDark);

            _performanceMetricsLabel = AddLabel(410, 87, 9.6f, ModernHudTheme.TextWhite);
            _bmdMetricsLabel = AddLabel(410, 268, 9.5f, ModernHudTheme.TextWhite);
            _instancingStatusLabel = AddLabel(410, 335, 9.3f, ModernHudTheme.TextWhite);

            SetLabelTextIfChanged(_fpsLabel, "FPS  --    UPS  --");
            SetLabelTextIfChanged(_mousePosLabel, "Mouse  screen --,--   ·   UI --,--");
            SetLabelTextIfChanged(_playerCordsLabel, "Player  --,--");
            SetLabelTextIfChanged(_mapTileLabel, "Map tile  --,--");
            SetLabelTextIfChanged(_objectCursorLabel, "Hover  none");
            SetLabelTextIfChanged(_tileFlagsLabel, "Tile flags  --");
            SetLabelTextIfChanged(_effectsStatusLabel, "Post FX  FXAA --   ·   AlphaRGB --");
            SetLabelTextIfChanged(_lightingStatusLabel, "Lighting data unavailable");
            SetLabelTextIfChanged(_gpuSkinningStatusLabel, "GPU skinning data unavailable");
            SetLabelTextIfChanged(_performanceMetricsLabel, "Performance data unavailable");
            SetLabelTextIfChanged(_bmdMetricsLabel, "BMD data unavailable");
            SetLabelTextIfChanged(_batchSortingLabel, "Opaque sorting  --");
            SetLabelTextIfChanged(_instancingStatusLabel, "Instancing data unavailable");
            ApplyDisplayMode();
        }

        private LabelControl AddLabel(int x, int y, float fontSize, Color color, bool bold = false)
        {
            var label = new LabelControl
            {
                X = x,
                Y = y,
                FontSize = fontSize,
                TextColor = color,
                HasShadow = false,
                IsBold = bold
            };
            Controls.Add(label);
            return label;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            if (!_expanded)
            {
                DrawCompact();
                return;
            }

            var sprite = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (pixel == null || font == null)
            {
                base.Draw(gameTime);
                return;
            }

            var rect = DisplayRectangle;
            sprite.Draw(pixel, new Rectangle(rect.X + 7, rect.Y + 9, rect.Width, rect.Height), new Color(0, 0, 0, 92));
            UiDrawHelper.DrawVerticalGradient(sprite, rect, new Color(25, 31, 41, 244), new Color(7, 10, 15, 248), 20);

            var header = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 48);
            UiDrawHelper.DrawHorizontalGradient(sprite, header, new Color(42, 51, 65, 242), new Color(16, 21, 29, 238), 24);
            sprite.Draw(pixel, new Rectangle(rect.X + 16, rect.Y + 47, rect.Width - 32, 2), new Color(ModernHudTheme.Accent.R, ModernHudTheme.Accent.G, ModernHudTheme.Accent.B, (byte)170));

            DrawSectionCard(sprite, new Rectangle(rect.X + 16, rect.Y + 58, 370, 168), "OVERVIEW", ModernHudTheme.SecondaryBright);
            DrawSectionCard(sprite, new Rectangle(rect.X + 16, rect.Y + 236, 370, 200), "RENDERING", new Color(170, 125, 220));
            DrawSectionCard(sprite, new Rectangle(rect.X + 398, rect.Y + 58, 376, 168), "PERFORMANCE", ModernHudTheme.Warning);
            DrawSectionCard(sprite, new Rectangle(rect.X + 398, rect.Y + 236, 376, 200), "PIPELINE", ModernHudTheme.AccentBright);

            DrawHeaderText(sprite, font, rect);
            DrawFrameHistory(sprite, pixel, new Rectangle(rect.Right - 286, rect.Y + 11, 132, 27));
            DrawHealthBadge(sprite, font, new Rectangle(rect.Right - 144, rect.Y + 11, 126, 27));

            UiDrawHelper.DrawBorder(sprite, rect, ModernHudTheme.BorderOuter, 2);
            UiDrawHelper.DrawBorder(sprite, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), ModernHudTheme.BorderInner);
            UiDrawHelper.DrawCornerAccents(sprite, rect, ModernHudTheme.Accent, 14, 2);

            base.Draw(gameTime);
        }

        private static void DrawSectionCard(SpriteBatch sprite, Rectangle rect, string title, Color accent)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (pixel == null || font == null)
                return;

            sprite.Draw(pixel, rect, new Color(5, 8, 13, 145));
            sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), new Color(accent.R, accent.G, accent.B, (byte)180));
            sprite.Draw(pixel, new Rectangle(rect.X + 10, rect.Y + 31, rect.Width - 20, 1), new Color(255, 255, 255, 20));
            UiDrawHelper.DrawBorder(sprite, rect, new Color(91, 104, 124, 74));

            SpriteFont renderFont = GraphicsManager.GetUiFont(9.5f, out float scale) ?? font;
            sprite.DrawString(renderFont, title, new Vector2(rect.X + 12, rect.Y + 9), accent, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static void DrawHeaderText(SpriteBatch sprite, SpriteFont font, Rectangle rect)
        {
            SpriteFont titleFont = GraphicsManager.GetUiFont(13.5f, out float titleScale) ?? font;
            SpriteFont captionFont = GraphicsManager.GetUiFont(9f, out float captionScale) ?? font;
            sprite.DrawString(titleFont, "CLIENT DIAGNOSTICS", new Vector2(rect.X + 18, rect.Y + 11), ModernHudTheme.TextGold, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
            sprite.DrawString(captionFont, "real-time renderer and runtime telemetry", new Vector2(rect.X + 18, rect.Y + 30), ModernHudTheme.TextDark, 0f, Vector2.Zero, captionScale, SpriteEffects.None, 0f);
        }

        private void DrawHealthBadge(SpriteBatch sprite, SpriteFont font, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            sprite.Draw(pixel, rect, new Color(_healthColor.R, _healthColor.G, _healthColor.B, (byte)34));
            UiDrawHelper.DrawBorder(sprite, rect, new Color(_healthColor.R, _healthColor.G, _healthColor.B, (byte)180));

            SpriteFont renderFont = GraphicsManager.GetUiFont(9.5f, out float scale) ?? font;
            Vector2 size = renderFont.MeasureString(_healthText) * scale;
            Vector2 position = new(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f);
            sprite.DrawString(renderFont, _healthText, position, _healthColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawFrameHistory(SpriteBatch sprite, Texture2D pixel, Rectangle rect)
        {
            sprite.Draw(pixel, rect, new Color(3, 6, 10, 165));
            UiDrawHelper.DrawBorder(sprite, rect, new Color(91, 104, 124, 64));

            int sampleCount = _frameHistory.Length;
            int barWidth = Math.Max(1, (rect.Width - 4) / sampleCount);
            int baseline = rect.Bottom - 2;
            int targetY = baseline - (int)Math.Round((16.67f / 40f) * (rect.Height - 4));
            sprite.Draw(pixel, new Rectangle(rect.X + 2, targetY, rect.Width - 4, 1), new Color(ModernHudTheme.Warning.R, ModernHudTheme.Warning.G, ModernHudTheme.Warning.B, (byte)75));

            for (int i = 0; i < sampleCount; i++)
            {
                int index = (_historyIndex + i) % sampleCount;
                float ms = MathHelper.Clamp(_frameHistory[index], 0f, 40f);
                int height = Math.Max(1, (int)Math.Round((ms / 40f) * (rect.Height - 4)));
                Color color = ms <= 16.67f ? ModernHudTheme.Success : ms <= 25f ? ModernHudTheme.Warning : ModernHudTheme.Danger;
                int x = rect.X + 2 + i * barWidth;
                sprite.Draw(pixel, new Rectangle(x, baseline - height, Math.Max(1, barWidth - 1), height), new Color(color.R, color.G, color.B, (byte)165));
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
                return;

            if (MuGame.Instance?.Keyboard.IsKeyDown(Keys.F10) == true &&
                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.F10))
            {
                _expanded = !_expanded;
                ApplyDisplayMode();
            }

            _fastUpdateTimer += gameTime.ElapsedGameTime.TotalMilliseconds;
            _slowUpdateTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

            bool shouldRunFastUpdate = _fastUpdateTimer >= FastUpdateIntervalMs;
            bool shouldRunSlowUpdate = _slowUpdateTimer >= SlowUpdateIntervalMs;
            if (!shouldRunFastUpdate && !shouldRunSlowUpdate)
                return;

            if (shouldRunFastUpdate)
            {
                _fastUpdateTimer = 0;
                UpdateFastMetrics();
            }

            if (shouldRunSlowUpdate)
            {
                _slowUpdateTimer = 0;
                if (_expanded)
                    UpdateSlowMetrics();
            }
        }

        private void ApplyDisplayMode()
        {
            ControlSize = _expanded
                ? new Point(PanelWidth, PanelHeight)
                : new Point(CompactWidth, CompactHeight);
            ViewSize = ControlSize;
            SetDetailedLabelsVisible(_expanded);
        }

        private void SetDetailedLabelsVisible(bool visible)
        {
            _fpsLabel.Visible = visible;
            _mousePosLabel.Visible = visible;
            _playerCordsLabel.Visible = visible;
            _mapTileLabel.Visible = visible;
            _effectsStatusLabel.Visible = visible;
            _objectCursorLabel.Visible = visible;
            _tileFlagsLabel.Visible = visible;
            _performanceMetricsLabel.Visible = visible;
            _bmdMetricsLabel.Visible = visible;
            _batchSortingLabel.Visible = visible;
            _instancingStatusLabel.Visible = visible;
            _lightingStatusLabel.Visible = visible;
            _gpuSkinningStatusLabel.Visible = visible;
        }

        private void DrawCompact()
        {
            var sprite = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (pixel == null || font == null)
                return;

            Rectangle rect = DisplayRectangle;
            sprite.Draw(pixel, new Rectangle(rect.X + 5, rect.Y + 6, rect.Width, rect.Height), new Color(0, 0, 0, 80));
            UiDrawHelper.DrawHorizontalGradient(sprite, rect, new Color(27, 34, 45, 238), new Color(7, 10, 15, 242), 20);
            sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 4, rect.Height), new Color(_healthColor.R, _healthColor.G, _healthColor.B, (byte)210));
            UiDrawHelper.DrawBorder(sprite, rect, new Color(91, 104, 124, 110), 1);

            int fps = (int)FPSCounter.Instance.FPS_AVG;
            var perf = MuGame.FramePerformance;
            var diagnostics = MuGame.Diagnostics;
            bool telemetryEnabled = diagnostics?.Enabled == true;
            bool telemetryConnected = diagnostics?.IsConnected == true;
            string connection = !telemetryEnabled
                ? "TELEMETRY OFF"
                : telemetryConnected ? "WEB CONNECTED" : "WEB OFFLINE";
            Color connectionColor = telemetryConnected ? ModernHudTheme.Success : ModernHudTheme.TextDark;

            SpriteFont primaryFont = GraphicsManager.GetUiFont(12.5f, out float primaryScale) ?? font;
            SpriteFont secondaryFont = GraphicsManager.GetUiFont(9f, out float secondaryScale) ?? font;
            sprite.DrawString(primaryFont, $"FPS {fps}   p95 {perf.P95Ms:F1} ms   p99 {perf.P99Ms:F1} ms",
                new Vector2(rect.X + 18, rect.Y + 10), _healthColor, 0f, Vector2.Zero, primaryScale, SpriteEffects.None, 0f);
            string telemetryDetails = telemetryEnabled
                ? diagnostics.DashboardUrl
                : "optional diagnostics";
            sprite.DrawString(secondaryFont, $"{connection}   ·   {telemetryDetails}   ·   F10 details",
                new Vector2(rect.X + 18, rect.Y + 36), connectionColor, 0f, Vector2.Zero, secondaryScale, SpriteEffects.None, 0f);
        }

        private void UpdateFastMetrics()
        {
            int fps = (int)FPSCounter.Instance.FPS_AVG;
            int ups = (int)UPSCounter.Instance.UPS_AVG;
            SetLabelTextIfChanged(_fpsLabel, $"FPS  {fps}    UPS  {ups}");
            _fpsLabel.TextColor = fps >= 55 ? ModernHudTheme.Success : fps >= 40 ? ModernHudTheme.Warning : ModernHudTheme.Danger;

            Point screenMouse = MuGame.Instance.Mouse.Position;
            Point uiMouse = MuGame.Instance.UiMouseState.Position;
            SetLabelTextIfChanged(_mousePosLabel, $"Mouse  screen {screenMouse.X},{screenMouse.Y}   ·   UI {uiMouse.X},{uiMouse.Y}");

            string fxaa = GraphicsManager.Instance.IsFXAAEnabled ? "ON" : "OFF";
            string alphaRgb = GraphicsManager.Instance.IsAlphaRGBEnabled ? "ON" : "OFF";
            SetLabelTextIfChanged(_effectsStatusLabel, $"Post FX  FXAA {fxaa}   ·   AlphaRGB {alphaRgb}");

            string cursorObject = World?.Scene?.MouseHoverObject?.GetType().Name ?? "none";
            SetLabelTextIfChanged(_objectCursorLabel, $"Hover  {cursorObject}");

            var framePerformance = MuGame.FramePerformance;
            float p95 = (float)framePerformance.P95Ms;
            _frameHistory[_historyIndex] = p95;
            _historyIndex = (_historyIndex + 1) % _frameHistory.Length;

            if (p95 <= 16.67f)
            {
                _healthText = $"HEALTHY  {p95:F1} ms";
                _healthColor = ModernHudTheme.Success;
            }
            else if (p95 <= 25f)
            {
                _healthText = $"BUSY  {p95:F1} ms";
                _healthColor = ModernHudTheme.Warning;
            }
            else
            {
                _healthText = $"SLOW  {p95:F1} ms";
                _healthColor = ModernHudTheme.Danger;
            }

            if (World is WalkableWorldControl walkableWorld && walkableWorld.Walker != null)
            {
                SetWorldLabelsVisible(true);
                SetLabelTextIfChanged(_playerCordsLabel, $"Player  {walkableWorld.Walker.Location.X}, {walkableWorld.Walker.Location.Y}");
                SetLabelTextIfChanged(_mapTileLabel, $"Map tile  {walkableWorld.MouseTileX}, {walkableWorld.MouseTileY}");
            }
            else
            {
                SetWorldLabelsVisible(false);
            }
        }

        private void UpdateSlowMetrics()
        {
            if (World is not WalkableWorldControl walkableWorld || walkableWorld.Walker == null)
                return;

            var flags = walkableWorld.Terrain.RequestTerrainFlag(
                (int)walkableWorld.Walker.Location.X,
                (int)walkableWorld.Walker.Location.Y);
            SetLabelTextIfChanged(_tileFlagsLabel, $"Tile flags  {flags}");

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
                .Append("Terrain  ").Append(terrainGpu ? "GPU" : "CPU")
                .Append(shaderAvailable ? string.Empty : "  ·  shader missing")
                .Append("   Objects  ").Append(objectsGpu ? "GPU" : "CPU")
                .Append('\n')
                .Append("Lights  ").Append(registeredDynamicLights).Append(" registered  ·  ")
                .Append(activeDynamicLights).Append(" active  ·  ")
                .Append(visibleDynamicLights).Append(" visible")
                .Append('\n')
                .Append("Upload  ").Append(uploadedTerrainLights)
                .Append("   Pruned  ").Append(prunedDynamicLights)
                .Append("   Duplicates  ").Append(rejectedDynamicAdds);
            SetLabelTextIfChanged(_lightingStatusLabel, _sb.ToString());
            _lightingStatusLabel.TextColor = terrainGpu && objectsGpu ? ModernHudTheme.Success : ModernHudTheme.Warning;

            _sb.Clear()
                .Append("Enabled  ").Append(Constants.ENABLE_GPU_SKINNING ? "YES" : "NO")
                .Append("   Backend  ").Append(ModelObject.IsGpuSkinningBackendSupported ? "READY" : "UNAVAILABLE")
                .Append('\n')
                .Append("Meshes  ").Append(ModelObject.LastFrameGpuSkinnedMeshesDrawn)
                .Append("   Batch draws/meshes  ")
                .Append(ModelObject.LastFrameGpuSkinnedBatchDrawCalls).Append('/')
                .Append(ModelObject.LastFrameGpuSkinnedBatchedMeshes)
                .Append(ModelObject.IsGpuSkinnedMeshBatchingRuntimeDisabled ? "  ·  disabled" : string.Empty);
            SetLabelTextIfChanged(_gpuSkinningStatusLabel, _sb.ToString());
            _gpuSkinningStatusLabel.TextColor = Constants.ENABLE_GPU_SKINNING && ModelObject.IsGpuSkinningBackendSupported
                ? ModernHudTheme.Success
                : ModernHudTheme.Warning;

            var terrainMetrics = walkableWorld.Terrain.FrameMetrics;
            var worldMetrics = walkableWorld.FrameMetrics;
            int queuedMainThreadActions = MuGame.MainThreadPendingActions;
            int processedMainThreadActions = MuGame.MainThreadProcessedActionsLastFrame;
            int queuedSchedulerTasks = MuGame.TaskScheduler?.QueuedTaskCount ?? 0;
            int processedSchedulerTasks = MuGame.TaskScheduler?.LastFrameProcessedTasks ?? 0;
            var framePerformance = MuGame.FramePerformance;

            _sb.Clear()
                .Append("Terrain  ").Append(terrainMetrics.DrawCalls).Append(" draws  ·  ")
                .Append(terrainMetrics.DrawnTriangles).Append(" tri")
                .Append('\n')
                .Append("World  ").Append(terrainMetrics.DrawnBlocks).Append(" blocks  ·  ")
                .Append(terrainMetrics.DrawnCells).Append(" cells")
                .Append('\n')
                .Append("Culling  ").Append(walkableWorld.LastCullWasRebuild ? "rebuild" : "cached")
                .Append("  ·  ").Append(walkableWorld.LastCullCandidateCount).Append(" candidates")
                .Append('\n')
                .Append("Visible  ").Append(walkableWorld.LastCullVisibleCount)
                .Append("  ·  ").Append(walkableWorld.LastCullRebuildMs.ToString("F2")).Append(" ms")
                .Append('\n')
                .Append("Static map  queue ").Append(worldMetrics.DedicatedStaticMapObjects)
                .Append("  ·  update skip ").Append(worldMetrics.StaticMapUpdateSkips)
                .Append("  ·  after skip ").Append(worldMetrics.DrawAfterSkips)
                .Append('\n')
                .Append("Particles  systems ").Append(worldMetrics.DedicatedParticleSystems)
                .Append("  ·  sprites ").Append(worldMetrics.ParticleSprites)
                .Append("  ·  batches ").Append(worldMetrics.ParticleBatchBegins)
                .Append("  ·  culled/inactive ").Append(worldMetrics.ParticleSystemsCulled)
                .Append('/').Append(worldMetrics.InactiveParticleSystemsSkipped)
                .Append('\n')
                .Append("Frame  p95 ").Append(framePerformance.P95Ms.ToString("F1"))
                .Append(" ms  ·  p99 ").Append(framePerformance.P99Ms.ToString("F1"))
                .Append(" ms  ·  alloc ").Append(framePerformance.AllocatedKb.ToString("F0")).Append(" KB")
                .Append('\n')
                .Append("Queues  main ").Append(processedMainThreadActions).Append('/').Append(queuedMainThreadActions)
                .Append(" @ ").Append(MuGame.MainThreadProcessingMs.ToString("F1")).Append(" ms  ·  tasks ")
                .Append(processedSchedulerTasks).Append('/').Append(queuedSchedulerTasks);
            SetLabelTextIfChanged(_performanceMetricsLabel, _sb.ToString());

            var bmd = BMDLoader.Instance;
            _sb.Clear()
                .Append("Buffers  VB ").Append(bmd.LastFrameVBUpdates)
                .Append("  ·  IB ").Append(bmd.LastFrameIBUploads)
                .Append('\n')
                .Append("Geometry  vertices ").Append(bmd.LastFrameVerticesTransformed)
                .Append("  ·  meshes ").Append(bmd.LastFrameMeshesProcessed)
                .Append('\n')
                .Append("Cache  ").Append(bmd.LastFrameCacheHits).Append(" hit / ")
                .Append(bmd.LastFrameCacheMisses).Append(" miss")
                .Append('\n')
                .Append("GPU cache  ")
                .Append(bmd.GpuMeshBufferCacheCount).Append('/').Append(bmd.GpuBatchBufferCacheCount)
                .Append("  ·  topology ").Append(bmd.MeshTopologyCacheCount)
                .Append('\n')
                .Append("Pruned  ").Append(bmd.LastFrameGpuMeshBuffersPruned).Append('/')
                .Append(bmd.LastFrameGpuBatchBuffersPruned).Append('/')
                .Append(bmd.LastFrameMeshTopologiesPruned);
            SetLabelTextIfChanged(_bmdMetricsLabel, _sb.ToString());

            SetLabelTextIfChanged(
                _batchSortingLabel,
                $"Opaque sorting  {(Constants.ENABLE_BATCH_OPTIMIZED_SORTING ? "ON" : "OFF")}  ·  material/state grouping");
            _batchSortingLabel.TextColor = Constants.ENABLE_BATCH_OPTIMIZED_SORTING ? ModernHudTheme.Success : ModernHudTheme.TextDark;

            _sb.Clear()
                .Append("Static  ").Append(Constants.ENABLE_MAP_OBJECT_INSTANCING ? "ON" : "OFF")
                .Append("  ·  backend ").Append(ModelObject.IsStaticMapInstancingBackendSupported ? "OK" : "N/A")
                .Append("  ·  runtime ").Append(ModelObject.IsStaticMapInstancingRuntimeDisabled ? "OFF" : "OK")
                .Append('\n')
                .Append("Objects  ").Append(ModelObject.LastFrameStaticMapInstancedObjects)
                .Append("  ·  mesh instances ").Append(ModelObject.LastFrameStaticMapInstancedMeshInstances)
                .Append('\n')
                .Append("Batches  ").Append(ModelObject.LastFrameStaticMapInstancedBatches)
                .Append("  ·  draws ").Append(ModelObject.LastFrameStaticMapInstancedDrawCalls)
                .Append("  ·  fallback ").Append(ModelObject.LastFrameStaticMapInstancingFallbacks)
                .Append('\n')
                .Append("Instance upload  ").Append(ModelObject.LastFrameStaticMapInstanceUploads)
                .Append("  ·  reused ").Append(ModelObject.LastFrameStaticMapInstanceUploadReuses)
                .Append('\n')
                .Append("Shadow inst  ").Append(Constants.ENABLE_STATIC_MAP_SHADOW_INSTANCING ? "ON" : "OFF")
                .Append(ModelObject.IsStaticMapShadowInstancingRuntimeDisabled ? " / runtime OFF" : string.Empty)
                .Append("  ·  objects ").Append(ModelObject.LastFrameStaticMapShadowInstancedObjects)
                .Append("  ·  draws ").Append(ModelObject.LastFrameStaticMapShadowInstancedDrawCalls)
                .Append('\n')
                .Append("Shadow upload  ").Append(ModelObject.LastFrameStaticMapShadowInstanceUploads)
                .Append("  ·  reused ").Append(ModelObject.LastFrameStaticMapShadowInstanceUploadReuses)
                .Append('\n')
                .Append("Multi-pose  ").Append(ModelObject.IsWalkerCrowdMultiPoseActive ? "ON" : "LEGACY")
                .Append("  ·  objects ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseObjects)
                .Append('\n')
                .Append("Crowd  mesh inst ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseMeshInstances)
                .Append("  ·  poses ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseUniquePoses)
                .Append("  ·  draws ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls)
                .Append('\n')
                .Append("Atlas  uploads ").Append(ModelObject.LastFrameWalkerCrowdMultiPosePaletteUploads)
                .Append("  ·  dirty ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseDirtyRows)
                .Append('\n')
                .Append("Atlas cache  hits ").Append(ModelObject.LastFrameWalkerCrowdMultiPosePaletteCacheHits)
                .Append("  ·  ").Append(ModelObject.LastFrameWalkerCrowdMultiPosePaletteBytes / 1024L).Append(" KB")
                .AppendLine()
                .Append("Crowd rejected  obj ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedObject)
                .Append(" (child ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedChildren)
                .Append(" mat ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMaterial)
                .Append(" anim ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedAnimation)
                .Append(" 1shot ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedOneShot)
                .Append(" type ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedTypeOrRenderer)
                .Append(")  mesh ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMesh)
                .Append("  buf ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedBuffers)
                .Append("  bones ").Append(ModelObject.LastFrameWalkerCrowdMultiPoseRejectedBones)
                .AppendLine()
                .Append("Top rejected  ").Append(string.IsNullOrEmpty(ModelObject.LastFrameWalkerCrowdTopRejections) ? "-" : ModelObject.LastFrameWalkerCrowdTopRejections);
            SetLabelTextIfChanged(_instancingStatusLabel, _sb.ToString());
        }

        private void SetWorldLabelsVisible(bool visible)
        {
            _playerCordsLabel.Visible = visible;
            _mapTileLabel.Visible = visible;
            _tileFlagsLabel.Visible = visible;
            _performanceMetricsLabel.Visible = visible;
            _bmdMetricsLabel.Visible = visible;
            _batchSortingLabel.Visible = visible;
            _lightingStatusLabel.Visible = visible;
            _gpuSkinningStatusLabel.Visible = visible;
            _instancingStatusLabel.Visible = visible;
        }

        private static void SetLabelTextIfChanged(LabelControl label, string value)
        {
            if (!string.Equals(label.Text, value, StringComparison.Ordinal))
                label.Text = value;
        }
    }
}
