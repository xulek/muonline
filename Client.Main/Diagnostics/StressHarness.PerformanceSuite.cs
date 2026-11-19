using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Diagnostics;

public static partial class StressHarness
{
    private static readonly bool PerformanceSuiteEnabled = Environment.GetEnvironmentVariable("MU_STRESS_SUITE") == "1";
    private static readonly bool PerformanceSuiteProfiling = Environment.GetEnvironmentVariable("MU_STRESS_SUITE_PROFILE") == "1";
    internal static bool CaptureSuitePresentation => Enabled && PerformanceSuiteEnabled;
    internal static bool CaptureSuiteDeviceMetrics => PerformanceSuiteProfiling;
    private static readonly string[] SuiteScenarios = { "idle", "walk", "combat" };
    private static readonly List<string> SuiteRows = new(3);
    private static int _suiteScenario;
    private static Vector2 _suiteWalkDestination;
    private static bool _suiteWalkingOut;
    private static Vector3 _suiteLastCameraTarget;
    private static double _suiteCameraTravel;
    private static long _suiteStartQpc;
    private static long _suiteWorldRenderFailures, _suiteDrawFailures;
    private static int _suiteAttackStart;
    private static TimeSpan _suiteThreadCpuStart;
    private static long _suiteDrawCalls, _suiteTriangles, _suiteTextureChanges, _suiteTargetChanges;
    private static long _suiteStaticDraws, _suiteGpuSkinnedMeshes, _suiteModelFallbackDraws;
    private static long _suiteCpuSkinRequests;
    private static double _suiteWorldQueueMs, _suiteStaticQueueMs, _suiteTerrainMs, _suiteInterfaceMs, _suitePostProcessMs;
    private static double _suitePresentApiMs;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static bool ConfigurePerformanceSuite(MuGame game)
    {
        if (!OperatingSystem.IsWindows() || _warmupSeconds < 1 || _measureSeconds < 3)
        {
            FailPerformanceSuite(game, "The performance suite requires Windows, at least 1 s of warmup and 3 s of measurement.");
            return false;
        }

#if PERFORMANCE_RELEASE
        if (PerformanceSuiteProfiling)
        {
            FailPerformanceSuite(game, "Build Release with -p:PerformanceRelease=false for pass profiling.");
            return false;
        }
#endif
        Types.Clear();
        Types.Add(3); // Spider
        _count = 12;
        var state = MuGame.Network.GetCharacterState();
        state.CurrentHealth = state.MaximumHealth = 1000;
        return true;
    }

    private static void StartPerformanceSuite(MuGame game)
    {
        if (game.ActiveScene is not GameScene { Hero: { } hero, World: WalkableWorldControl world })
        {
            FailPerformanceSuite(game, "The performance suite needs a ready walkable scene.");
            return;
        }

        Vector2 origin = new(HeroX, HeroY);
        if (!world.IsWalkable(origin) || !FindSuiteWalkDestination(world, origin))
        {
            FailPerformanceSuite(game, "No walkable route at MU_STRESS_POS.");
            return;
        }

        hero.Location = origin;
        hero.SnapToTerrainHeight();
        var device = game.GraphicsDevice;
        var graphics = GraphicsManager.Instance;
        var backbuffer = device.PresentationParameters;
        File.WriteAllText(Path.ChangeExtension(_outPath, ".json"), JsonSerializer.Serialize(new
        {
            ProcessId = Environment.ProcessId,
            MainThreadId = GetCurrentThreadId(),
            Adapter = device.Adapter.Description,
            GraphicsProfile = device.GraphicsProfile.ToString(),
            Preset = GraphicsQualityManager.ActivePreset.ToString(),
            MapId = StressMap,
            Position = new[] { (int)HeroX, (int)HeroY },
            WalkDestination = new[] { _suiteWalkDestination.X, _suiteWalkDestination.Y },
            BackBufferWidth = backbuffer.BackBufferWidth,
            BackBufferHeight = backbuffer.BackBufferHeight,
            RenderWidth = graphics.MainRenderTarget?.Width ?? backbuffer.BackBufferWidth,
            RenderHeight = graphics.MainRenderTarget?.Height ?? backbuffer.BackBufferHeight,
            RenderScale = Constants.RENDER_SCALE,
            game.IsVSyncEnabled,
            MsaaSamples = Constants.MSAA_SAMPLES,
            Fxaa = graphics.IsFXAAEnabled,
            Bloom = Constants.ENABLE_WORLD_BLOOM,
            Ssao = Constants.ENABLE_SSAO,
            DynamicLights = Constants.ENABLE_DYNAMIC_LIGHTS,
            DynamicLightingShader = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER,
            GpuSkinning = Constants.ENABLE_GPU_SKINNING,
            Profile = PerformanceSuiteProfiling,
        }));
        Next(Phase.PerformanceSuite);
    }

    private static bool FindSuiteWalkDestination(WalkableWorldControl world, Vector2 origin)
    {
        ReadOnlySpan<Vector2> directions = [new(1, 1), new(0, 1), new(1, 0), new(-1, 1),
                                            new(-1, 0), new(0, -1), new(-1, -1), new(1, -1)];
        for (int tiles = 10; tiles >= 4; tiles--)
        {
            foreach (Vector2 direction in directions)
            {
                Vector2 destination = origin + direction * tiles;
                if (!Pathfinding.IsDirectPathWalkable(origin, destination, world))
                    continue;
                _suiteWalkDestination = destination;
                return true;
            }
        }
        return false;
    }

    private static void UpdatePerformanceSuite(MuGame game, FrameProfiler.Snapshot frame, double elapsed,
        GraphicsMetrics deviceMetrics, double presentMs)
    {
        if (game.ActiveScene is not GameScene { Hero: { } hero, World: WalkableWorldControl world })
        {
            FailPerformanceSuite(game, "The suite lost its game scene.");
            return;
        }

        if (_suiteScenario == 1 && !hero.IsMoving)
        {
            Vector2 target = _suiteWalkingOut ? new Vector2(HeroX, HeroY) : _suiteWalkDestination;
            _suiteWalkingOut = !_suiteWalkingOut;
            hero.MoveTo(target, sendToServer: false, usePathfinding: false);
        }
        if (_suiteScenario == 2)
        {
            int previousStep = _monsterAttackStep;
            ReplayMonsterAttacks(game);
            if (previousStep != _monsterAttackStep)
                hero.PlayAction((ushort)PlayerAction.PlayerAttackFist, true);
        }

        if (_suiteStartQpc == 0)
        {
            if (elapsed < _warmupSeconds)
                return;
            if (_suiteScenario == 2 && !SuiteMonstersReady(world))
            {
                FailPerformanceSuite(game, "The combat scenario did not load all 12 monsters.");
                return;
            }
            if (_suiteScenario == 1)
            {
                hero.StopMovement();
                hero.Location = new Vector2(HeroX, HeroY);
                hero.SnapToTerrainHeight();
                _suiteWalkingOut = false;
            }
            _suiteLastCameraTarget = Camera.Instance.Target;
            _suiteWorldRenderFailures = world.FrameMetrics.LastRenderFailureSequence;
            _suiteDrawFailures = MuGame.LatestDrawException.Sequence;
            _suiteAttackStart = _monsterAttackStep;
            if (PerformanceSuiteProfiling)
                _suiteThreadCpuStart = MainThreadCpu();
            _suiteStartQpc = Stopwatch.GetTimestamp();
            PhaseClock.Restart();
            return;
        }

        if (_suiteScenario == 1)
        {
            Vector3 cameraTarget = Camera.Instance.Target;
            _suiteCameraTravel += Vector2.Distance(new(_suiteLastCameraTarget.X, _suiteLastCameraTarget.Y),
                new(cameraTarget.X, cameraTarget.Y));
            _suiteLastCameraTarget = cameraTarget;
        }
        _suitePresentApiMs += presentMs;
        if (PerformanceSuiteProfiling)
            AccumulatePerformanceSuiteProfile(game, frame, deviceMetrics);
        else
            _frames++;
        if (elapsed < _measureSeconds)
            return;

        long endQpc = Stopwatch.GetTimestamp();
        if (_suiteWorldRenderFailures != world.FrameMetrics.LastRenderFailureSequence ||
            _suiteDrawFailures != MuGame.LatestDrawException.Sequence ||
            (_suiteScenario == 1 && _suiteCameraTravel < Constants.TERRAIN_SCALE) ||
            (_suiteScenario == 2 && (_monsterAttackStep == _suiteAttackStart || !SuiteMonstersReady(world))))
        {
            FailPerformanceSuite(game, $"Invalid {SuiteScenarios[_suiteScenario]} workload or render failure.");
            return;
        }

        WritePerformanceSuiteRow(endQpc);
        SaveScreenshot(game, $"{_label}_{SuiteScenarios[_suiteScenario]}");
        if (++_suiteScenario == SuiteScenarios.Length)
        {
            const string header = "scenario;startQpc;endQpc;qpcFrequency;frames;gameFps;cpuMs;updateMs;drawMs;worldObjectsMs;worldQueueMs;staticQueueMs;terrainMs;interfaceMs;postProcessMs;mainCpuMsPerSecond;visibleObjects;drawCalls;triangles;textureChanges;targetChanges;staticDrawCalls;gpuSkinnedMeshes;modelFallbackDrawCalls;cpuSkinRequests;cameraTravel;attackWaves;presentApiMs";
            File.WriteAllLines(_outPath, new[] { header, SuiteRows[0], SuiteRows[1], SuiteRows[2] });
            _phase = Phase.Done;
            PrintMicrobenchmarks();
            game.Exit();
            return;
        }

        _suiteStartQpc = 0;
        _frames = 0;
        _sum = default;
        _suiteDrawCalls = _suiteTriangles = _suiteTextureChanges = _suiteTargetChanges = 0;
        _suiteStaticDraws = _suiteGpuSkinnedMeshes = _suiteModelFallbackDraws = _suiteCpuSkinRequests = 0;
        _suiteWorldQueueMs = _suiteStaticQueueMs = _suiteTerrainMs = _suiteInterfaceMs = _suitePostProcessMs = 0;
        _suitePresentApiMs = 0;
        if (_suiteScenario == 2)
        {
            hero.StopMovement();
            hero.Location = new Vector2(HeroX, HeroY);
            hero.SnapToTerrainHeight();
            _typeIndex = 0;
            _nextMonsterAttack = game.GameTime.TotalGameTime.TotalSeconds + 0.5;
            _monsterAttackShotAt = double.PositiveInfinity;
            Inject(BuildSpawnPacket(Types[0], _count));
        }
        PhaseClock.Restart();
    }

    private static bool SuiteMonstersReady(WalkableWorldControl world)
    {
        for (int i = 0; i < _count; i++)
        {
            if (!world.TryGetWalkerById(StressMonsterId(Types[0], i), out var monster) || !monster.Visible)
                return false;
        }
        return true;
    }

    private static void AccumulatePerformanceSuiteProfile(MuGame game, FrameProfiler.Snapshot frame, GraphicsMetrics metrics)
    {
        Accumulate(game, frame);
        _suiteDrawCalls += metrics.DrawCount;
        _suiteTriangles += metrics.PrimitiveCount;
        _suiteTextureChanges += metrics.TextureCount;
        _suiteTargetChanges += metrics.TargetCount;
        _suiteStaticDraws += ModelObject.LastFrameStaticMapInstancedDrawCalls;
        _suiteGpuSkinnedMeshes += ModelObject.LastFrameGpuSkinnedMeshesDrawn;
        _suiteModelFallbackDraws += ModelObject.LastFrameModelFallbackDrawCalls;
        var passes = RenderPassProfiler.Current;
        _suiteWorldQueueMs += passes.WorldQueueMs;
        _suiteStaticQueueMs += passes.StaticQueueMs;
        _suiteTerrainMs += passes.TerrainOpaqueMs + passes.TerrainAfterMs;
        _suiteInterfaceMs += passes.InterfaceMs;
        _suitePostProcessMs += passes.PostProcessMs;
    }

    internal static void RecordSuiteCpuSkinRequest()
    {
        if (PerformanceSuiteProfiling && _suiteStartQpc != 0)
            _suiteCpuSkinRequests++;
    }

    private static void WritePerformanceSuiteRow(long endQpc)
    {
        double seconds = (endQpc - _suiteStartQpc) / (double)Stopwatch.Frequency;
        string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
        string Average(double total) => PerformanceSuiteProfiling ? Number(total / _frames) : string.Empty;
        string row = string.Join(";", SuiteScenarios[_suiteScenario], _suiteStartQpc, endQpc, Stopwatch.Frequency,
            _frames, Number(_frames / seconds), Average(_sum.CpuMs), Average(_sum.UpdateMs), Average(_sum.DrawMs),
            Average(_sum.WorldObjectsMs), Average(_suiteWorldQueueMs), Average(_suiteStaticQueueMs),
            Average(_suiteTerrainMs), Average(_suiteInterfaceMs), Average(_suitePostProcessMs),
            PerformanceSuiteProfiling ? Number((MainThreadCpu() - _suiteThreadCpuStart).TotalMilliseconds / seconds) : string.Empty,
            Average(_sum.Visible), Average(_suiteDrawCalls), Average(_suiteTriangles), Average(_suiteTextureChanges),
            Average(_suiteTargetChanges), Average(_suiteStaticDraws), Average(_suiteGpuSkinnedMeshes),
            Average(_suiteModelFallbackDraws), Average(_suiteCpuSkinRequests),
            Number(_suiteScenario == 1 ? _suiteCameraTravel : 0), _monsterAttackStep - _suiteAttackStart,
            Number(_suitePresentApiMs / _frames));
        SuiteRows.Add(row);
        Console.WriteLine($"[perf-suite] {row}");
    }

    private static void FailPerformanceSuite(MuGame game, string message)
    {
        Console.Error.WriteLine($"[perf-suite] {message}");
        _phase = Phase.Done;
        Environment.ExitCode = 1;
        game.Exit();
    }
}
