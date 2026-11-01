using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Diagnostics
{
    /// <summary>
    /// Offline crowd stress scenes (no server): the real <see cref="GameScene"/> is started with a fake character,
    /// monsters are spawned through the real packet router (AddNpcsToScope 0x13), measured and removed again
    /// (MapObjectOutOfScope 0x14). Results go to a CSV file and the game exits afterwards.
    /// <para>
    /// Environment: MU_STRESS=1, MU_STRESS_TYPES=3,2,40 (monster types), MU_STRESS_COUNT=80, MU_STRESS_WARMUP=6,
    /// MU_STRESS_SECONDS=8, MU_STRESS_OUT=path.csv, MU_STRESS_LABEL=text, MU_STRESS_CHILDREN=0/1
    /// (Constants.ENABLE_WALKER_CROWD_INSTANCING_WITH_CHILDREN), MU_STRESS_CROWD=0/1 (walker crowd instancing),
    /// MU_STRESS_WEAPONS=0/1 (Constants.ENABLE_WALKER_CROWD_CHILD_INSTANCING), MU_STRESS_TRACE=1 (per-second trace).
    /// </para>
    /// </summary>
    public static class StressHarness
    {
        public static bool Enabled { get; } = Environment.GetEnvironmentVariable("MU_STRESS") == "1";

        private enum Phase { WaitingForScene, Settle, Spawn, Warmup, Measure, Despawn, Done }

        private const byte HeroX = 135, HeroY = 128;
        private const ushort FirstMonsterId = 0x0200;

        private static readonly List<ushort> Types = new();
        private static int _count = 80;
        private static double _warmupSeconds = 6, _measureSeconds = 8;
        private static string _outPath = "stress-results.csv";
        private static string _label = string.Empty;

        private static Phase _phase = Phase.WaitingForScene;
        private static int _typeIndex = -1; // -1 = baseline without monsters
        private static readonly Stopwatch PhaseClock = new();
        private static readonly List<string> Rows = new();
        private static Sample _sum;
        private static int _frames;
        private static int _lastReport = -1;

        private struct Sample
        {
            public double UpdateMs, DrawMs, CpuMs, WorldObjectsMs, ShadowMs, TerrainMs, SceneAfterMs, WorldCullMs, WorldHoverMs, ScopePumpMs;
            public long Visible, ModelObjects, AnimationUpdates, CrowdObjects, CrowdDraws, CrowdPoses, RejectedObjects, RejectedMesh, DrawCallsEstimate;
        }

        /// <summary>Called once at startup instead of connecting and showing the login scene.</summary>
        public static void Start(MuGame game)
        {
            foreach (var part in (Environment.GetEnvironmentVariable("MU_STRESS_TYPES") ?? "3,24,2,0,7,16,40").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (ushort.TryParse(part.Trim(), out var type)) Types.Add(type);
            _count = ParseInt("MU_STRESS_COUNT", 80);
            _warmupSeconds = ParseInt("MU_STRESS_WARMUP", 6);
            _measureSeconds = ParseInt("MU_STRESS_SECONDS", 8);
            _outPath = Environment.GetEnvironmentVariable("MU_STRESS_OUT") ?? Path.Combine(AppContext.BaseDirectory, "stress-results.csv");
            _label = Environment.GetEnvironmentVariable("MU_STRESS_LABEL") ?? string.Empty;

            if (Environment.GetEnvironmentVariable("MU_STRESS_CHILDREN") is { } children)
                Constants.ENABLE_WALKER_CROWD_INSTANCING_WITH_CHILDREN = children == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_MERGED_SHADOWS") is { } mergedShadows)
                Constants.ENABLE_MERGED_MESH_SHADOWS = mergedShadows == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_WEAPONS") is { } weapons)
                Constants.ENABLE_WALKER_CROWD_CHILD_INSTANCING = weapons == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_CROWD") is { } crowd)
                Constants.ENABLE_WALKER_CROWD_INSTANCING = crowd == "1";
            Constants.UNLIMITED_FPS = true;
            Constants.DISABLE_VSYNC = true;
            game.ApplyGraphicsOptions();

            var state = MuGame.Network.GetCharacterState();
            state.Id = 0x0001;
            state.Name = "Stress";
            state.Class = CharacterClassNumber.DarkKnight;
            state.Level = 400;
            state.MapId = 0;
            state.PositionX = HeroX;
            state.PositionY = HeroY;
            var appearance = new byte[25];
            appearance[0] = (byte)((int)CharacterClassNumber.DarkKnight << 3);
            // Same data LoadScene prepares before the game scene (gates, skills).
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await Data.GateDataManager.Instance.LoadData();
                await Core.Utilities.SkillDatabase.Initialize();
                MuGame.ScheduleOnMainThread(() =>
                {
                    game.ChangeScene(new GameScene(("Stress", CharacterClassNumber.DarkKnight, (ushort)400, appearance)));
                    PhaseClock.Restart();
                });
            });
            PhaseClock.Restart();
            Console.WriteLine($"[stress] adapter: {game.GraphicsDevice?.Adapter?.Description} profile={game.GraphicsDevice?.GraphicsProfile} backbuffer={game.GraphicsDevice?.PresentationParameters.BackBufferWidth}x{game.GraphicsDevice?.PresentationParameters.BackBufferHeight}");
            foreach (var adapter in Microsoft.Xna.Framework.Graphics.GraphicsAdapter.Adapters)
                Console.WriteLine($"[stress] available adapter: {adapter.Description} default={adapter.IsDefaultAdapter}");
            Console.WriteLine($"[stress] start: types {string.Join(",", Types)}, count {_count}, children {Constants.ENABLE_WALKER_CROWD_INSTANCING_WITH_CHILDREN}, crowd {Constants.ENABLE_WALKER_CROWD_INSTANCING}");
        }

        private static int ParseInt(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

        /// <summary>Called at the end of every drawn frame.</summary>
        public static void OnFrame(MuGame game, FrameProfiler.Snapshot frame)
        {

            if (!Enabled || _phase == Phase.Done) return;
            double elapsed = PhaseClock.Elapsed.TotalSeconds;

            switch (_phase)
            {
                case Phase.WaitingForScene:
                    if (game.ActiveScene is GameScene { Hero: not null } scene && scene.World is WalkableWorldControl { Status: Models.GameControlStatus.Ready } world && world.Visible)
                        Next(Phase.Settle);
                    else if (elapsed > 90)
                        Finish(game, "scene did not load");
                    else if ((int)elapsed / 5 != _lastReport)
                    {
                        _lastReport = (int)elapsed / 5;
                        var s = game.ActiveScene;
                        var w = s?.World;
                        var hero = (s as GameScene)?.Hero;
                        Console.WriteLine($"[stress] t={elapsed:F0}s scene={s?.GetType().Name} sceneStatus={s?.Status} world={w?.GetType().Name} worldStatus={w?.Status} worldVisible={w?.Visible} hero={hero?.Status} heroPos={hero?.Position} cam={Camera.Instance?.Position}");
                    }
                    break;

                case Phase.Settle:
                    if (elapsed >= 3) Next(Phase.Spawn);
                    break;

                case Phase.Spawn:
                    if (_typeIndex >= 0)
                        Inject(BuildSpawnPacket(Types[_typeIndex], _count));
                    _sum = default;
                    _frames = 0;
                    Next(Phase.Warmup);
                    break;

                case Phase.Warmup:
                    Wander(game);
                    if (elapsed >= _warmupSeconds) Next(Phase.Measure);
                    break;

                case Phase.Measure:
                    Accumulate(game, frame);
                    Trace(elapsed, frame);
                    Wander(game);
                    if (elapsed >= _measureSeconds)
                    {
                        WriteRow(game, elapsed);
                        Next(Phase.Despawn);
                    }
                    break;

                case Phase.Despawn:
                    if (_typeIndex >= 0)
                        Inject(BuildDespawnPacket(_count));
                    _typeIndex++;
                    if (_typeIndex >= Types.Count)
                        Finish(game, null);
                    else
                        Next(Phase.Spawn);
                    break;
            }
        }

        private static void Next(Phase phase)
        {
            _phase = phase;
            PhaseClock.Restart();
        }

        // MU_STRESS_TRACE=1: one line per second of the measured phase, to find periodic drops.
        private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("MU_STRESS_TRACE") == "1";
        private static readonly bool TypeTraceEnabled = ObjectTypeProfiler.Enabled = TraceEnabled;
        private static int _traceSecond = -1, _traceFrames, _traceDirty, _tracePoses, _traceGc0, _traceGc1, _traceGc2;
        private static double _traceWorstMs;
        private static int _traceSlow;
        private static TimeSpan _traceProcCpu, _traceMainCpu, _traceGcPause;
        private static long _traceTpItems;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

        private static double _benchSink;

        /// <summary>
        /// Fixed amount of scalar work timed on the main thread. When it takes longer, the core runs slower
        /// (clock/power limits or contention); when it stays constant, a slower frame means more game work.
        /// </summary>
        private static double CpuBenchmarkMicroseconds()
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                long start = Stopwatch.GetTimestamp();
                double x = 1.0001;
                for (int i = 0; i < 200_000; i++)
                    x = x * 1.0000001 + 0.0000001;
                _benchSink += x;
                best = Math.Min(best, (Stopwatch.GetTimestamp() - start) * 1_000_000.0 / Stopwatch.Frequency);
            }
            return best;
        }

        /// <summary>CPU time of the calling (main) thread; OnFrame runs on the game thread.</summary>
        private static TimeSpan MainThreadCpu() =>
            GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user)
                ? TimeSpan.FromTicks(kernel + user)
                : TimeSpan.Zero;
        private static double _traceScene, _traceWbase, _traceShadow;
        private static double _traceUpd, _traceDraw, _traceWobj, _traceTerr, _traceAfter, _tracePost, _traceFw;
        private static readonly System.Diagnostics.Stopwatch TraceClock = System.Diagnostics.Stopwatch.StartNew();
        private static long _traceBytes, _traceAlloc;

        private static void Trace(double elapsed, FrameProfiler.Snapshot frame)
        {
            if (!TraceEnabled) return;
            int second = (int)elapsed;
            if (second != _traceSecond)
            {
                if (_traceSecond >= 0 && _traceFrames > 0)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"[stress-trace] t={_traceSecond} fps={_traceFrames} worstMs={_traceWorstMs:F1} slow15={_traceSlow} poses={_tracePoses / _traceFrames} dirty={_traceDirty / _traceFrames} paletteKB={_traceBytes / 1024 / _traceFrames} allocKB={(GC.GetTotalAllocatedBytes() - _traceAlloc) / 1024} gc={GC.CollectionCount(0) - _traceGc0}/{GC.CollectionCount(1) - _traceGc1}/{GC.CollectionCount(2) - _traceGc2} upd={_traceUpd / _traceFrames:F2} draw={_traceDraw / _traceFrames:F2} wobj={_traceWobj / _traceFrames:F2} terr={_traceTerr / _traceFrames:F2} after={_traceAfter / _traceFrames:F2} post={_tracePost / _traceFrames:F2} fw={_traceFw / _traceFrames:F2} scene={_traceScene / _traceFrames:F2} wbase={_traceWbase / _traceFrames:F2} shadow={_traceShadow / _traceFrames:F2} procCpu={(Process.GetCurrentProcess().TotalProcessorTime - _traceProcCpu).TotalMilliseconds:F0} mainCpu={(MainThreadCpu() - _traceMainCpu).TotalMilliseconds:F0} tpItems={ThreadPool.CompletedWorkItemCount - _traceTpItems} tpThreads={ThreadPool.ThreadCount} gcPauseMs={(GC.GetTotalPauseDuration() - _traceGcPause).TotalMilliseconds:F1} benchUs={CpuBenchmarkMicroseconds():F0} shRebuild={ModelObject.DiagShadowRebuilds / _traceFrames}/{ModelObject.DiagShadowRebuildTicks * 1000.0 / Stopwatch.Frequency / _traceFrames:F2}ms shShift={ModelObject.DiagShadowShifts / _traceFrames} shDraw={ModelObject.DiagShadowDraws / _traceFrames}/{ModelObject.DiagShadowDrawTicks * 1000.0 / Stopwatch.Frequency / _traceFrames:F2}ms shMerged={ModelObject.DiagShadowMergedDraws / _traceFrames}/{ModelObject.DiagShadowMergedMeshes / _traceFrames}"));
                    ModelObject.DiagShadowMergedDraws = ModelObject.DiagShadowMergedMeshes = 0;
                    ModelObject.DiagShadowRebuilds = ModelObject.DiagShadowRebuildTicks = ModelObject.DiagShadowShifts = ModelObject.DiagShadowDraws = ModelObject.DiagShadowDrawTicks = 0;
                    Console.WriteLine($"[stress-types] t={_traceSecond} {ObjectTypeProfiler.TakeTop(8, _traceFrames)}");
                }
                _traceSecond = second; _traceFrames = 0; _traceDirty = 0; _tracePoses = 0; _traceBytes = 0; _traceWorstMs = 0; _traceSlow = 0;
                _traceScene = _traceWbase = _traceShadow = 0;
                _traceUpd = _traceDraw = _traceWobj = _traceTerr = _traceAfter = _tracePost = _traceFw = 0;
                _traceGc0 = GC.CollectionCount(0); _traceGc1 = GC.CollectionCount(1); _traceGc2 = GC.CollectionCount(2);
                _traceAlloc = GC.GetTotalAllocatedBytes();
                _traceProcCpu = Process.GetCurrentProcess().TotalProcessorTime;
                _traceMainCpu = MainThreadCpu();
                _traceTpItems = ThreadPool.CompletedWorkItemCount;
                _traceGcPause = GC.GetTotalPauseDuration();
            }
            _traceFrames++;
            double frameMs = TraceClock.Elapsed.TotalMilliseconds;
            TraceClock.Restart();
            _traceWorstMs = Math.Max(_traceWorstMs, frameMs);
            if (frameMs > 15) _traceSlow++;
            _traceDirty += ModelObject.LastFrameWalkerCrowdMultiPoseDirtyRows;
            var passes = RenderPassProfiler.Current;
            _traceUpd += frame.UpdateMs; _traceDraw += frame.DrawMs; _traceWobj += passes.WorldObjectsMs;
            _traceTerr += passes.TerrainOpaqueMs; _traceAfter += passes.SceneAfterMs; _tracePost += passes.PostProcessMs;
            _traceFw += passes.FrameworkDrawMs;
            _traceScene += passes.SceneDrawMs; _traceWbase += passes.WorldBaseMs; _traceShadow += passes.ShadowMs;
            _tracePoses += ModelObject.LastFrameWalkerCrowdMultiPoseUniquePoses;
            _traceBytes += ModelObject.LastFrameWalkerCrowdMultiPosePaletteBytes;
        }

        private static void Accumulate(MuGame game, FrameProfiler.Snapshot frame)
        {

            var passes = RenderPassProfiler.Current;
            var update = UpdatePassProfiler.Current;
            var metrics = game.ActiveScene?.World?.FrameMetrics;
            _frames++;
            _sum.UpdateMs += frame.UpdateMs;
            _sum.DrawMs += frame.DrawMs;
            _sum.CpuMs += frame.CpuFrameMs;
            _sum.WorldObjectsMs += passes.WorldObjectsMs;
            _sum.ShadowMs += passes.ShadowMs;
            _sum.TerrainMs += passes.TerrainOpaqueMs;
            _sum.SceneAfterMs += passes.SceneAfterMs;
            _sum.WorldCullMs += update.WorldCullMs;
            _sum.WorldHoverMs += update.WorldHoverMs;
            _sum.ScopePumpMs += update.GameScopePumpMs;
            _sum.Visible += metrics?.VisibleObjects ?? 0;
            _sum.ModelObjects += metrics?.ModelObjects ?? 0;
            _sum.AnimationUpdates += metrics?.AnimationUpdates ?? 0;
            _sum.CrowdObjects += ModelObject.LastFrameWalkerCrowdMultiPoseObjects;
            _sum.CrowdDraws += ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls;
            _sum.CrowdPoses += ModelObject.LastFrameWalkerCrowdMultiPoseUniquePoses;
            _sum.RejectedObjects += ModelObject.LastFrameWalkerCrowdMultiPoseRejectedObject;
            _sum.RejectedMesh += ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMesh;
            var terrain = game.ActiveScene?.World?.Terrain?.FrameMetrics;
            int individual = Math.Max(0, ModelObject.LastFrameGpuSkinnedMeshesDrawn - ModelObject.LastFrameGpuSkinnedBatchedMeshes
                - ModelObject.LastFrameStaticMapInstancedMeshInstances - ModelObject.LastFrameWalkerCrowdMultiPoseMeshInstances);
            _sum.DrawCallsEstimate += (terrain?.DrawCalls ?? 0) + individual + ModelObject.LastFrameGpuSkinnedBatchDrawCalls
                + ModelObject.LastFrameStaticMapInstancedDrawCalls + ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls + ModelObject.LastFrameModelFallbackDrawCalls;
        }

        private static void WriteRow(MuGame game, double seconds)
        {
            string type = _typeIndex < 0 ? "baseline" : Types[_typeIndex].ToString(CultureInfo.InvariantCulture);
            string name = _typeIndex < 0 ? "-" : Core.Utilities.NpcDatabase.GetNpcName(Types[_typeIndex]);
            double n = Math.Max(1, _frames);
            string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
            string row = string.Join(";",
                _label, type, name, _typeIndex < 0 ? 0 : _count,
                F(_frames / seconds), F(_sum.CpuMs / n), F(_sum.UpdateMs / n), F(_sum.DrawMs / n),
                F(_sum.WorldObjectsMs / n), F(_sum.ShadowMs / n), F(_sum.TerrainMs / n), F(_sum.SceneAfterMs / n),
                F(_sum.WorldCullMs / n), F(_sum.WorldHoverMs / n), F(_sum.ScopePumpMs / n),
                F(_sum.Visible / n), F(_sum.ModelObjects / n), F(_sum.AnimationUpdates / n),
                F(_sum.CrowdObjects / n), F(_sum.CrowdDraws / n), F(_sum.CrowdPoses / n),
                F(_sum.RejectedObjects / n), F(_sum.RejectedMesh / n), F(_sum.DrawCallsEstimate / n),
                (ModelObject.LastFrameWalkerCrowdTopRejections ?? string.Empty).Replace(';', ','));
            Rows.Add(row);
            Console.WriteLine($"[stress] {row}");
            SaveScreenshot(game, $"{_label}_{type}");
        }

        /// <summary>MU_STRESS_SHOTS=directory saves the back buffer of every measured scene as PNG.</summary>
        private static void SaveScreenshot(MuGame game, string name)
        {
            string dir = Environment.GetEnvironmentVariable("MU_STRESS_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                var gd = game.GraphicsDevice;
                int w = gd.PresentationParameters.BackBufferWidth, h = gd.PresentationParameters.BackBufferHeight;
                var data = new Microsoft.Xna.Framework.Color[w * h];
                gd.GetBackBufferData(data);
                using var tex = new Microsoft.Xna.Framework.Graphics.Texture2D(gd, w, h);
                tex.SetData(data);
                Directory.CreateDirectory(dir);
                using var fs = File.Create(Path.Combine(dir, name + ".png"));
                tex.SaveAsPng(fs, w, h);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[stress] screenshot failed: {ex.Message}");
            }
        }

        private static void Finish(MuGame game, string error)
        {
            _phase = Phase.Done;
            const string header = "label;type;name;count;fps;cpuMs;updateMs;drawMs;worldObjectsMs;shadowMs;terrainMs;sceneAfterMs;cullMs;hoverMs;scopePumpMs;visible;modelObjects;animUpdates;crowdObjects;crowdDraws;crowdPoses;rejectedObj;rejectedMesh;drawCalls;topRejected";
            bool exists = File.Exists(_outPath);
            var text = new StringBuilder();
            if (!exists) text.AppendLine(header);
            foreach (var row in Rows) text.AppendLine(row);
            if (error != null) text.AppendLine($"{_label};error;{error}");
            File.AppendAllText(_outPath, text.ToString());
            Console.WriteLine($"[stress] done -> {_outPath}");
            game.Exit();
        }

        private static void Inject(byte[] packet) => _ = MuGame.Network.InjectPacketForDiagnosticsAsync(packet);

        private static byte[] BuildSpawnPacket(ushort type, int count)
        {
            const int entry = 10;
            var data = new byte[5 + count * entry];
            data[0] = 0xC2;
            data[1] = (byte)(data.Length >> 8);
            data[2] = (byte)data.Length;
            data[3] = 0x13;
            data[4] = (byte)count;
            int side = (int)Math.Ceiling(Math.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                ushort id = (ushort)(FirstMonsterId + i);
                // Grid around the hero, two tiles apart, in front of the camera.
                byte x = (byte)(HeroX - side + (i % side) * 2);
                byte y = (byte)(HeroY - side + (i / side) * 2);
                int o = 5 + i * entry;
                data[o] = (byte)(id >> 8); data[o + 1] = (byte)id;
                data[o + 2] = (byte)(type >> 8); data[o + 3] = (byte)type;
                data[o + 4] = x; data[o + 5] = y; data[o + 6] = x; data[o + 7] = y;
                data[o + 8] = (byte)((i % 8) << 4);
                data[o + 9] = 0;
            }
            return data;
        }

        // MU_STRESS_WALK=1: like the server's monster AI, every few seconds half of the monsters walk a short
        // distance (ObjectWalked without a packet), so walking animations are part of the measurement.
        private static readonly bool WalkEnabled = Environment.GetEnvironmentVariable("MU_STRESS_WALK") == "1";
        private static readonly Stopwatch WalkClock = Stopwatch.StartNew();
        private static readonly Random WalkRandom = new(1234);
        private static int _walkCycle;

        private static void Wander(MuGame game)
        {
            if (!WalkEnabled || _typeIndex < 0 || WalkClock.Elapsed.TotalSeconds < 3)
                return;
            WalkClock.Restart();
            var world = game.ActiveScene?.World;
            if (world == null) return;
            _walkCycle++;
            int side = (int)Math.Ceiling(Math.Sqrt(_count));
            for (int i = 0; i < _count; i++)
            {
                if ((i + _walkCycle) % 2 != 0) continue;
                if (!world.TryGetWalkerById((ushort)(FirstMonsterId + i), out var walker) || walker == null) continue;
                int x = HeroX - side + (i % side) * 2 + WalkRandom.Next(-2, 3);
                int y = HeroY - side + (i / side) * 2 + WalkRandom.Next(-2, 3);
                walker.MoveTo(new Vector2(x, y), sendToServer: false, usePathfinding: false);
            }
        }

        private static byte[] BuildDespawnPacket(int count)
        {
            var data = new byte[4 + count * 2];
            data[0] = 0xC1;
            data[1] = (byte)data.Length;
            data[2] = 0x14;
            data[3] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                ushort id = (ushort)(FirstMonsterId + i);
                data[4 + i * 2] = (byte)(id >> 8);
                data[5 + i * 2] = (byte)id;
            }
            return data;
        }
    }
}
