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

        private enum Phase { WaitingForScene, Settle, Spawn, Warmup, Measure, Despawn, Inventory, PauseMenu, MapChange, Hover, GrassToggle, Birds, BackItems, Done }

        // MU_STRESS_MAP=<map id>, MU_STRESS_POS=x,y (tile) choose where the scenes run (default Lorencia 135,128).
        private static readonly byte StressMap = byte.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_MAP"), out byte map) ? map : (byte)0;
        private static readonly byte HeroX = ParsePos(0, 135), HeroY = ParsePos(1, 128);

        private static byte ParsePos(int index, byte fallback)
        {
            var parts = (Environment.GetEnvironmentVariable("MU_STRESS_POS") ?? string.Empty).Split(',');
            return parts.Length == 2 && byte.TryParse(parts[index], out byte value) ? value : fallback;
        }

        // MU_STRESS_LIGHTS=N adds N dynamic lights around the hero (radius 250-400, warm colours);
        // MU_STRESS_LIGHT_FLICKER=1 changes their intensity every frame like fire effects do.
        private static readonly int StressLightCount = int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_LIGHTS"), out int lights) ? lights : 0;
        private static readonly bool StressLightFlicker = Environment.GetEnvironmentVariable("MU_STRESS_LIGHT_FLICKER") == "1";
        private static readonly List<DynamicLight> StressLights = new();

        private static void AddStressLights(MuGame game)
        {
            var terrain = game.ActiveScene?.World?.Terrain;
            if (StressLightCount <= 0 || terrain == null || StressLights.Count > 0)
                return;

            var random = new Random(3);
            int side = (int)Math.Ceiling(Math.Sqrt(StressLightCount));
            for (int i = 0; i < StressLightCount; i++)
            {
                float x = (HeroX - side + (i % side) * 2 + 0.5f) * Constants.TERRAIN_SCALE;
                float y = (HeroY - side + (i / side) * 2 + 0.5f) * Constants.TERRAIN_SCALE;
                var light = new DynamicLight
                {
                    // Lights without a live owner in this world are dropped by the light manager.
                    Owner = (game.ActiveScene as GameScene)?.Hero,
                    Position = new Vector3(x, y, terrain.RequestTerrainHeight(x, y) + 80f),
                    Color = new Vector3(1f, 0.6f + (float)random.NextDouble() * 0.3f, 0.3f),
                    Radius = 250f + (float)random.NextDouble() * 150f,
                    Intensity = 1f
                };
                terrain.AddDynamicLight(light);
                StressLights.Add(light);
            }
            Console.WriteLine($"[stress] added {StressLights.Count} dynamic lights");
        }

        private static void FlickerStressLights()
        {
            if (!StressLightFlicker)
                return;
            double t = PhaseClock.Elapsed.TotalSeconds;
            for (int i = 0; i < StressLights.Count; i++)
                StressLights[i].Intensity = 0.8f + 0.2f * MathF.Sin((float)(t * 9.0 + i * 1.7));
        }
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
            if (Environment.GetEnvironmentVariable("MU_STRESS_GPU_SKIN") is { } gpuSkin)
                Constants.ENABLE_GPU_SKINNING = gpuSkin == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_VERTEX_LIT") is { } vertexLit)
                Constants.ENABLE_DISTANCE_VERTEX_LIGHTING = vertexLit == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_PRESET") is { } preset &&
                System.Enum.TryParse(preset, true, out Graphics.GraphicsQualityPreset qualityPreset))
                Graphics.GraphicsQualityManager.ApplyPreset(qualityPreset, null, null);
            if (Environment.GetEnvironmentVariable("MU_STRESS_IGPU") is { } igpu)
                Constants.OPTIMIZE_FOR_INTEGRATED_GPU = igpu == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_DL_SHADER") is { } dlShader)
                Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = dlShader == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_DYN_LIGHTS") is { } dynLights)
                Constants.ENABLE_DYNAMIC_LIGHTS = dynLights == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_MONSTER_MATERIAL") is { } monsterMaterial)
                Constants.ENABLE_MONSTER_MATERIAL_SHADER = monsterMaterial == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_GRASS") is { } grass)
                Constants.DRAW_GRASS = grass == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_TERRAIN_GPU") is { } terrainGpu)
                Constants.ENABLE_TERRAIN_GPU_LIGHTING = terrainGpu == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_DEFER_MESHES") is { } deferMeshes)
                Constants.ENABLE_DEFERRED_ACTOR_MESH_DRAWS = deferMeshes == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_COMPOSITE") is { } composite)
                Constants.ENABLE_PLAYER_COMPOSITE = composite == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_ITEM_MATERIAL") is { } itemMaterial)
                Constants.ENABLE_ITEM_MATERIAL_SHADER = itemMaterial == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_MAP_CROWD") is { } mapCrowd)
                Constants.ENABLE_ANIMATED_MAP_CROWD_INSTANCING = mapCrowd == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_MAP_TRANSPARENT") is { } mapTransparent)
                Constants.ENABLE_STATIC_MAP_TRANSPARENT_INSTANCING = mapTransparent == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_SOUND") is { } sound)
                Constants.SOUND_EFFECTS = sound == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_CROWD_TRANSPARENT") is { } crowdTransparent)
                Constants.ENABLE_WALKER_CROWD_TRANSPARENT_INSTANCING = crowdTransparent == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_WEAPONS") is { } weapons)
                Constants.ENABLE_WALKER_CROWD_CHILD_INSTANCING = weapons == "1";
            if (Environment.GetEnvironmentVariable("MU_STRESS_CROWD") is { } crowd)
                Constants.ENABLE_WALKER_CROWD_INSTANCING = crowd == "1";
            if (float.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_RENDER_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float renderScale))
                Constants.RENDER_SCALE = renderScale;
            Constants.UNLIMITED_FPS = true;
            Constants.DISABLE_VSYNC = true;
            game.ApplyGraphicsOptions();
            // MU_STRESS_RENDER_SCALE / MU_STRESS_PRESET change the render scale after the UI was configured.
            GraphicsManager.Instance?.UpdateRenderScale();
            if (int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_ANIMFPS"), out int animFps))
                Constants.ANIMATION_UPDATE_FPS = animFps;
            if (int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_LIGHTFPS"), out int lightFps))
                Constants.DYNAMIC_LIGHT_UPDATE_FPS = lightFps;

            var state = MuGame.Network.GetCharacterState();
            state.Id = 0x0001;
            state.Name = "Stress";
            state.Class = CharacterClassNumber.DarkKnight;
            state.Level = 400;
            state.MapId = StressMap;
            state.PositionX = HeroX;
            state.PositionY = HeroY;
            var appearance = new byte[25];
            appearance[0] = (byte)((int)CharacterClassNumber.DarkKnight << 3);
            // Same data LoadScene prepares before the game scene (gates, skills).
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await Data.GateDataManager.Instance.LoadData();
                    await Core.Utilities.SkillDatabase.Initialize();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[stress] scene data load failed: {ex}");
                }

                MuGame.ScheduleOnMainThread(() =>
                {
                    try
                    {
                        if (SelectScene)
                            game.ChangeScene(new SelectCharacterScene(SelectSceneCharacters(), MuGame.Network));
                        else
                            game.ChangeScene(new GameScene(("Stress", CharacterClassNumber.DarkKnight, (ushort)400, appearance)));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[stress] scene change failed: {ex}");
                    }
                    PhaseClock.Restart();
                });
            });
            PhaseClock.Restart();
            Console.WriteLine($"[stress] adapter: {game.GraphicsDevice?.Adapter?.Description} profile={game.GraphicsDevice?.GraphicsProfile} backbuffer={game.GraphicsDevice?.PresentationParameters.BackBufferWidth}x{game.GraphicsDevice?.PresentationParameters.BackBufferHeight}");
            foreach (var adapter in Microsoft.Xna.Framework.Graphics.GraphicsAdapter.Adapters)
                Console.WriteLine($"[stress] available adapter: {adapter.Description} default={ReferenceEquals(adapter, Microsoft.Xna.Framework.Graphics.GraphicsAdapter.DefaultAdapter)}");
            foreach (var (effectName, error) in GraphicsManager.Instance.EffectLoadErrors)
                Console.WriteLine($"[stress] effect {effectName} failed to load: {error}");
            Console.WriteLine($"[stress] graphics: dlShader={Constants.ENABLE_DYNAMIC_LIGHTING_SHADER} terrainGpu={Constants.ENABLE_TERRAIN_GPU_LIGHTING} dynLights={Constants.ENABLE_DYNAMIC_LIGHTS} itemMat={Constants.ENABLE_ITEM_MATERIAL_SHADER} monsterMat={Constants.ENABLE_MONSTER_MATERIAL_SHADER} grass={Constants.DRAW_GRASS} hqTex={Constants.HIGH_QUALITY_TEXTURES} scale={Constants.RENDER_SCALE} msaa={Constants.MSAA_ENABLED} igpu={Constants.OPTIMIZE_FOR_INTEGRATED_GPU} shadowMap={Constants.ENABLE_SHADOW_MAPPING} gpuSkin={Constants.ENABLE_GPU_SKINNING}");
            Console.WriteLine($"[stress] start: types {string.Join(",", Types)}, count {_count}, children {Constants.ENABLE_WALKER_CROWD_INSTANCING_WITH_CHILDREN}, crowd {Constants.ENABLE_WALKER_CROWD_INSTANCING}");
        }

        private static bool _worldErrorReported;

        private static int ParseInt(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

        /// <summary>Called at the end of every drawn frame.</summary>
        public static void OnFrame(MuGame game, FrameProfiler.Snapshot frame)
        {
            if (!Enabled || _phase == Phase.Done) return;
            if (SelectScene)
            {
                UpdateSelectSceneShots(game);
                return;
            }
            FlickerStressLights();
            // MU_STRESS_SPIN_US: extra busy CPU time per frame. A CPU-bound frame slows down by that amount,
            // a GPU-bound one hides it while waiting for the GPU.
            if (SpinMicroseconds > 0)
            {
                long spinUntil = Stopwatch.GetTimestamp() + SpinMicroseconds * Stopwatch.Frequency / 1_000_000;
                while (Stopwatch.GetTimestamp() < spinUntil) { }
            }
            double elapsed = PhaseClock.Elapsed.TotalSeconds;

            switch (_phase)
            {
                case Phase.WaitingForScene:
                    CaptureLoadingShot(game, "loading_initial", elapsed);
                    if (game.ActiveScene is GameScene { Hero: not null } scene && scene.World is WalkableWorldControl { Status: Models.GameControlStatus.Ready } world && world.Visible)
                        Next(Phase.Settle);
                    else if (game.ActiveScene?.World is { Status: Models.GameControlStatus.Error, InitializationError: { } worldError } && !_worldErrorReported)
                    {
                        _worldErrorReported = true;
                        Console.WriteLine($"[stress] world initialization failed: {worldError}");
                    }
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
                    if (elapsed >= 3)
                    {
                        AddStressLights(game);
                        if (LoadingShots)
                        {
                            _loadingShotTimes.Clear();
                            _ = (game.ActiveScene as GameScene)?.ChangeMap(typeof(Client.Main.Worlds.DeviasWorld));
                            Next(Phase.MapChange);
                        }
                        else if (HoverScene)
                        {
                            _hoverStep = -1;
                            Next(Phase.Hover);
                        }
                        else if (GrassToggleScene)
                        {
                            _grassStep = 0;
                            Next(Phase.GrassToggle);
                        }
                        else if (BirdsScene)
                        {
                            Next(Phase.Birds);
                        }
                        else if (BackItemsScene)
                        {
                            _backItemsSpawned = false;
                            Next(Phase.BackItems);
                        }
                        else if (PauseMenuScene)
                        {
                            _pauseShot = -1;
                            Next(Phase.PauseMenu);
                        }
                        else if (InventoryScene)
                        {
                            FillInventory(game);
                            Next(Phase.Inventory);
                        }
                        else
                        {
                            Next(Phase.Spawn);
                        }
                    }
                    break;

                case Phase.Hover:
                    UpdateHoverShots(game, elapsed);
                    break;

                case Phase.GrassToggle:
                    UpdateGrassToggleShots(game, elapsed);
                    break;

                case Phase.Birds:
                    UpdateBirdShots(game, elapsed);
                    break;

                case Phase.BackItems:
                    UpdateBackItemShots(game, elapsed);
                    break;

                case Phase.MapChange:
                    CaptureLoadingShot(game, "loading_mapchange", elapsed);
                    if (elapsed > 3 && game.ActiveScene?.World is Client.Main.Worlds.DeviasWorld { Status: Models.GameControlStatus.Ready, Visible: true })
                    {
                        _arrivedAt ??= elapsed; // the map name banner shows once the loading screen is gone
                        if (elapsed - _arrivedAt >= 2.5)
                        {
                            SaveScreenshot(game, $"{_label}_loading_arrived");
                            Finish(game, null);
                        }
                    }
                    else if (elapsed > 90)
                    {
                        Finish(game, "map change did not finish");
                    }
                    break;

                case Phase.PauseMenu:
                    UpdatePauseMenuShots(game, elapsed);
                    break;

                case Phase.Inventory:
                    if (elapsed >= 4)
                    {
                        SaveScreenshot(game, $"{_label}_inventory");
                        Finish(game, null);
                    }
                    break;

                case Phase.Spawn:
                    if (_typeIndex >= 0 && _count > 0)
                        Inject(BuildSpawnPacket(Types[_typeIndex], _count));
                    if (_typeIndex >= 0)
                        SpawnPlayers();
                    _sum = default;
                    _frames = 0;
                    Next(Phase.Warmup);
                    break;

                case Phase.Warmup:
                    Wander(game);
                    SwitchPresetOnce(elapsed);
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
                    if (_typeIndex >= 0 && _count > 0)
                        Inject(BuildDespawnPacket(_count));
                    if (_typeIndex >= 0 && PlayerCount > 0)
                        Inject(BuildDespawnPacket(PlayerCount, FirstPlayerId));
                    _typeIndex++;
                    if (_typeIndex >= Types.Count)
                        Finish(game, null);
                    else
                        Next(Phase.Spawn);
                    break;
            }
        }

        // MU_STRESS_INVENTORY=1: fills equipment and inventory with sample items, opens the inventory window and
        // saves a screenshot (MU_STRESS_SHOTS) to check item model previews without a server.
        private static readonly bool InventoryScene = Environment.GetEnvironmentVariable("MU_STRESS_INVENTORY") == "1";

        // MU_STRESS_SWITCH_PRESET=Low: switches the quality preset during the warmup the way the settings window does
        // (the scene is already loaded with MU_STRESS_PRESET), to catch state left over from the previous preset.
        private static bool _presetSwitched;

        private static void SwitchPresetOnce(double elapsed)
        {
            if (_presetSwitched || elapsed < 1.0 ||
                !System.Enum.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_SWITCH_PRESET"), true, out Graphics.GraphicsQualityPreset preset))
                return;
            _presetSwitched = true;
            Console.WriteLine($"[stress] switching quality preset to {preset}");
            Client.Main.Controls.UI.Game.PauseMenu.Settings.SettingsActions.ApplyQualityPreset(preset);
        }

        // MU_STRESS_GRASS_TOGGLE=1: switches the Grass setting off and on through the settings path and screenshots every
        // state; the grass pass state is printed at each step (batches, textures, ready flag).
        private static readonly bool GrassToggleScene = Environment.GetEnvironmentVariable("MU_STRESS_GRASS_TOGGLE") == "1";
        private static int _grassStep;
        private static bool _grassMoved;

        private static void UpdateGrassToggleShots(MuGame game, double elapsed)
        {
            var terrain = game.ActiveScene?.World?.Terrain;
            if (terrain == null)
            {
                if (elapsed > 90)
                    Finish(game, "no terrain for grass toggle");
                return;
            }

            // The spawn plaza has no terrain grass: walk the hero to the nearest grass tile so the shots show it.
            if (!_grassMoved && game.ActiveScene is GameScene { Hero: { } hero } &&
                terrain.TryFindGrassTile(hero.Position, 1500f, out Vector2 grassTile))
            {
                _grassMoved = true;
                hero.Location = grassTile;
                hero.Position = hero.TargetPosition;
                Console.WriteLine($"[stress] hero moved to grass tile {grassTile}");
            }

            if (elapsed < 4)
                return;

            string[] names = { "start", "off", "on_again" };
            Console.WriteLine($"[stress] grass {names[_grassStep]}: {terrain.DescribeGrassForDiagnostics()}");
            SaveScreenshot(game, $"{_label}_grass_{names[_grassStep]}");
            if (++_grassStep >= names.Length)
            {
                Finish(game, null);
                return;
            }
            Client.Main.Controls.UI.Game.PauseMenu.Settings.SettingsActions.SetGrass(_grassStep != 1);
            PhaseClock.Restart();
        }

        // MU_STRESS_BIRDS=1: lets the Lorencia flock spawn and prints how many birds fly over town (SafeZone) tiles.
        private static readonly bool BirdsScene = Environment.GetEnvironmentVariable("MU_STRESS_BIRDS") == "1";

        // MU_STRESS_BACKITEMS=1: spawns players with swords, shields, bows + arrows, crossbows + bolts and staffs in the
        // spawn plaza (a safe zone, so the weapons are holstered on their backs), two rows facing opposite ways.
        private static readonly bool BackItemsScene = Environment.GetEnvironmentVariable("MU_STRESS_BACKITEMS") == "1";
        private static double? _arrivedAt;
        private static bool _backItemsSpawned;
        // MU_STRESS_BACK_AT=x,y: centre tile of the lineup (default: the hero's tile); MU_STRESS_BACK_DIR=0..7: the
        // direction the players face. The lineup is one row of naked players so only the weapons stand out.
        private static readonly int BackDirection = int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_BACK_DIR"), out int backDir) ? backDir : 0;
        private static readonly byte BackAtX = ParsePair("MU_STRESS_BACK_AT", 0, HeroX), BackAtY = ParsePair("MU_STRESS_BACK_AT", 1, HeroY);

        private static byte ParsePair(string name, int index, byte fallback)
        {
            var parts = (Environment.GetEnvironmentVariable(name) ?? string.Empty).Split(',');
            return parts.Length == 2 && byte.TryParse(parts[index], out byte value) ? value : fallback;
        }

        private static readonly (byte Class, (byte, short) Slot0, (byte, short) Slot1)[] BackItemSets =
        {
            (4, (0, 0), (6, 0)),   // Dark Knight: sword + small shield
            (4, (0, 5), (6, 6)),   // sword + large shield
            (4, (0, 1), (0, 2)),   // two swords
            (8, (4, 15), (4, 2)),  // Elf: arrows + bow
            (8, (4, 8), (4, 7)),   // crossbow + bolts
            (0, (5, 0), (6, 3)),   // Dark Wizard: staff + shield
        };

        private static void UpdateBackItemShots(MuGame game, double elapsed)
        {
            if (!_backItemsSpawned)
            {
                _backItemsSpawned = true;
                for (int i = 0; i < BackItemSets.Length; i++)
                {
                    var set = BackItemSets[i];
                    byte x = (byte)(BackAtX - 3 + i), y = BackAtY;
                    Inject(BuildPlayerPacket((ushort)(FirstPlayerId + i), x, y, $"Back{i}", set.Class, i,
                        set.Slot0, set.Slot1, BackDirection, bare: true));
                }
                Console.WriteLine($"[stress] back item placement self check (max matrix error): {Client.Main.Objects.Player.BackItemPlacement.SelfCheck():E2}");
                Constants.FOV_SCALE = 0.4f; // zoom in on the lineup (the camera controller applies it every frame)
                PhaseClock.Restart();
                return;
            }

            if (elapsed < 9)
                return;

            if (game.ActiveScene is GameScene { Hero: { } backHero })
                Console.WriteLine($"[stress] back items: hero tile {backHero.Location}, lineup at {BackAtX},{BackAtY}");
            SaveScreenshot(game, $"{_label}_backitems");
            Finish(game, null);
        }

        private static bool _birdsLanded, _birdsGroundShot;

        // Waits for the flock, sends three birds down next to the hero, then samples for 30 s: birds inside map
        // objects (bounding boxes) and how high the feet of landed birds are above the terrain.
        private static void UpdateBirdShots(MuGame game, double elapsed)
        {
            if (elapsed < 8)
                return;

            if (game.ActiveScene?.World is not Client.Main.Worlds.LorenciaWorld { BirdsForDiagnostics: { } birds } ||
                game.ActiveScene is not GameScene { Hero: { } hero })
            {
                Finish(game, "birds check needs Lorencia (MU_STRESS_MAP=0)");
                return;
            }

            if (!_birdsLanded)
            {
                _birdsLanded = true;
                birds.LandNear(hero.Position, 3);
            }

            birds.SampleForDiagnostics();
            if (!_birdsGroundShot && elapsed >= 14)
            {
                _birdsGroundShot = true;
                SaveScreenshot(game, $"{_label}_birds_ground");
            }

            if (elapsed < 38)
                return;

            Console.WriteLine($"[stress] birds: {birds.DescribeForDiagnostics()}");
            Console.WriteLine($"[stress] birds samples: {birds.DescribeSamplesForDiagnostics()}");
            Console.WriteLine($"[stress] birds inside: {birds.DescribeInsideForDiagnostics()}");
            Console.WriteLine($"[stress] birds obstacle types: {birds.DescribeObstacleTypesForDiagnostics()}");
            SaveScreenshot(game, $"{_label}_birds");
            Finish(game, null);
        }

        // MU_STRESS_HOVER=1: hovers the nearest interactive map object (no mouse needed) and screenshots it before,
        // during and after the hover; the object's draw state is printed at every step.
        private static readonly bool HoverScene = Environment.GetEnvironmentVariable("MU_STRESS_HOVER") == "1";
        private static int _hoverStep;
        private static ModelObject _hoverTarget;

        private static void UpdateHoverShots(MuGame game, double elapsed)
        {
            if (_hoverStep == -1)
            {
                var hero = (game.ActiveScene as GameScene)?.Hero;
                float best = float.MaxValue;
                foreach (var obj in game.ActiveScene.World.Objects.GetSnapshotArray())
                {
                    if (obj is not ModelObject model || obj is WalkerObject || !obj.Interactive || !obj.Visible || hero == null)
                        continue;
                    float d = Vector3.DistanceSquared(obj.WorldPosition.Translation, hero.WorldPosition.Translation);
                    if (d < best) { best = d; _hoverTarget = model; }
                }
                Console.WriteLine($"[stress] hover target: {_hoverTarget?.GetType().Name} at {_hoverTarget?.WorldPosition.Translation} dist={MathF.Sqrt(best):F0}");
                if (_hoverTarget == null) { Finish(game, "no interactive object"); return; }
                _hoverStep = 0;
                PhaseClock.Restart();
                return;
            }

            if (_hoverStep == 1)
                _hoverTarget.IsMouseHover = true;
            if (elapsed < 1.5)
                return;

            string[] names = { "before", "hovered", "after" };
            Console.WriteLine($"[stress] hover {names[_hoverStep]}: {_hoverTarget.DescribeDrawStateForDiagnostics()}");
            SaveScreenshot(game, $"{_label}_hover_{names[_hoverStep]}");
            if (_hoverStep == 1)
                _hoverTarget.IsMouseHover = false;
            if (++_hoverStep >= names.Length)
            {
                Finish(game, null);
                return;
            }
            PhaseClock.Restart();
        }

        // MU_STRESS_LOADING=1: screenshots of the initial game loading screen and of a map change to Devias.
        private static readonly bool LoadingShots = Environment.GetEnvironmentVariable("MU_STRESS_LOADING") == "1";
        private static readonly double[] LoadingShotSeconds = { 0.25, 1.2, 3.0 };
        private static readonly HashSet<double> _loadingShotTimes = new();

        private static void CaptureLoadingShot(MuGame game, string name, double elapsed)
        {
            if (!LoadingShots)
                return;
            foreach (double at in LoadingShotSeconds)
            {
                if (elapsed >= at && _loadingShotTimes.Add(at))
                    SaveScreenshot(game, $"{_label}_{name}_{at:0.0#}s");
            }
        }

        // MU_STRESS_SELECT=1: character selection with fake characters, screenshots with hover states.
        private static readonly bool SelectScene = Environment.GetEnvironmentVariable("MU_STRESS_SELECT") == "1";
        private static int _selectShot = -1;

        private static List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> SelectSceneCharacters()
        {
            var list = new List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>();
            foreach (var (name, cls, level) in new[]
                     {
                         ("Arthas", CharacterClassNumber.BladeKnight, (ushort)400),
                         ("Merlin", CharacterClassNumber.SoulMaster, (ushort)356),
                         ("Sylvana", CharacterClassNumber.MuseElf, (ushort)220),
                     })
            {
                var appearance = new byte[25];
                appearance[0] = (byte)((int)cls << 3);
                list.Add((name, cls, level, appearance));
            }
            return list;
        }

        // One step per screenshot: what to open first, where the pointer is (virtual 1280x720), how long to wait.
        private static readonly (Action<SelectCharacterScene> Setup, Point? Pointer, double Wait)[] SelectShots =
        {
            (null, null, 5),                                          // roster, first character selected
            (null, new Point(1100, 248), 1.5),                         // hover a character card
            (null, new Point(1100, 432), 1.5),                         // hover an empty slot
            (null, new Point(640, 660), 1.5),                          // hover Enter Game
            (s => s.DiagnosticsOpenCreation(), new Point(1097, 210), 3), // creation, hover a class emblem
            (null, new Point(1127, 583), 1.5),                         // hover Create
            (s => { s.DiagnosticsCloseCreation(); s.DiagnosticsOpenDeletion(); }, new Point(739, 475), 1.5),
            (s => { s.DiagnosticsCloseDeletion(); s.DiagnosticsShowMessage(); }, null, 1.5),
        };

        private static void UpdateSelectSceneShots(MuGame game)
        {
            if (game.ActiveScene is not SelectCharacterScene { Status: Models.GameControlStatus.Ready } scene || scene.World?.Visible != true)
            {
                if (PhaseClock.Elapsed.TotalSeconds > 90)
                    Finish(game, "select scene did not load");
                return;
            }

            if (_selectShot < 0)
            {
                _selectShot = 0;
                PhaseClock.Restart();
                SelectShots[0].Setup?.Invoke(scene);
            }

            var shot = SelectShots[_selectShot];
            Client.Main.Controls.UI.Ornate.OrnatePointer.DiagnosticPosition = shot.Pointer;
            if (PhaseClock.Elapsed.TotalSeconds < shot.Wait)
                return;

            SaveScreenshot(game, $"{_label}_select_{_selectShot}");
            if (++_selectShot >= SelectShots.Length)
            {
                Finish(game, null);
                return;
            }
            SelectShots[_selectShot].Setup?.Invoke(scene);
            PhaseClock.Restart();
        }

        // MU_STRESS_PAUSE=1: opens the pause menu and every settings category, one screenshot each.
        private static readonly bool PauseMenuScene = Environment.GetEnvironmentVariable("MU_STRESS_PAUSE") == "1";
        private static int _pauseShot;

        private static void UpdatePauseMenuShots(MuGame game, double elapsed)
        {
            var pauseMenu = (game.ActiveScene as GameScene)?.PauseMenu;
            if (pauseMenu == null)
            {
                Finish(game, "pause menu missing");
                return;
            }

            if (_pauseShot == -1)
            {
                pauseMenu.Visible = true;
                pauseMenu.BringToFront();
                _pauseShot = 0;
                PhaseClock.Restart();
                return;
            }

            // Hover a representative element in some shots: main menu button, selector arrow, slider row, tab.
            Client.Main.Controls.UI.Ornate.OrnatePointer.DiagnosticPosition = _pauseShot switch
            {
                0 => new Point(640, 294),
                1 => new Point(1027, 256),
                3 => new Point(560, 358),
                6 => new Point(330, 314),
                _ => null
            };

            if (elapsed < 1.5)
                return;

            SaveScreenshot(game, _pauseShot == 0 ? $"{_label}_pause_menu" : $"{_label}_pause_settings_{_pauseShot - 1}");
            if (_pauseShot >= pauseMenu.SettingsCategoryCount)
            {
                Finish(game, null);
                return;
            }

            pauseMenu.ShowSettingsCategory(_pauseShot);
            _pauseShot++;
            PhaseClock.Restart();
        }

        private static byte[] SampleItem(byte group, short number, byte level = 0, byte flags = 0) =>
            new byte[] { (byte)((group << 4) | ((number >> 8) & 0x0F)), (byte)number, level, 255, flags };

        private static void FillInventory(MuGame game)
        {
            var state = MuGame.Network.GetCharacterState();
            state.ClearInventory();
            // Equipment: weapon, shield, helm, armor, pants, gloves, boots, wings, pendant, rings.
            state.AddOrUpdateInventoryItem(0, SampleItem(0, 13, 9));    // Double Blade +9
            state.AddOrUpdateInventoryItem(1, SampleItem(6, 6, 7));     // shield
            state.AddOrUpdateInventoryItem(2, SampleItem(7, 1, 11));    // helm +11
            state.AddOrUpdateInventoryItem(3, SampleItem(8, 1, 11));    // armor
            state.AddOrUpdateInventoryItem(4, SampleItem(9, 1, 11));    // pants
            state.AddOrUpdateInventoryItem(5, SampleItem(10, 1, 11));   // gloves
            state.AddOrUpdateInventoryItem(6, SampleItem(11, 1, 11));   // boots
            state.AddOrUpdateInventoryItem(7, SampleItem(12, 5));       // wings
            state.AddOrUpdateInventoryItem(9, SampleItem(13, 12));      // pendant
            state.AddOrUpdateInventoryItem(10, SampleItem(13, 8));      // ring
            // Inventory grid (slot 12 = first cell, 8 cells per row).
            state.AddOrUpdateInventoryItem(12, SampleItem(14, 13));     // Jewel of Bless
            state.AddOrUpdateInventoryItem(13, SampleItem(14, 14));     // Jewel of Soul
            state.AddOrUpdateInventoryItem(14, SampleItem(14, 16));     // Jewel of Life
            state.AddOrUpdateInventoryItem(15, SampleItem(12, 15));     // Jewel of Chaos
            state.AddOrUpdateInventoryItem(16, SampleItem(14, 3));      // potion
            state.AddOrUpdateInventoryItem(17, SampleItem(14, 6));      // mana potion
            state.AddOrUpdateInventoryItem(28, SampleItem(1, 2, 4));    // axe
            state.AddOrUpdateInventoryItem(30, SampleItem(5, 0, 3));    // staff
            state.AddOrUpdateInventoryItem(32, SampleItem(4, 3));       // bow
            state.AddOrUpdateInventoryItem(34, SampleItem(2, 3, 13));   // mace +13
            state.AddOrUpdateInventoryItem(52, SampleItem(8, 5, 0));    // armor
            state.AddOrUpdateInventoryItem(54, SampleItem(12, 2));      // wings
            state.AddOrUpdateInventoryItem(57, SampleItem(14, 19, 1));  // Devil's Invitation
            state.RaiseInventoryChanged();
            if (game.ActiveScene is GameScene scene)
                scene.InventoryControl?.Show();
            Console.WriteLine("[stress] inventory filled and opened");
        }

        private static void Next(Phase phase)
        {
            _phase = phase;
            PhaseClock.Restart();
        }

        // MU_STRESS_TRACE=1: one line per second of the measured phase, to find periodic drops.
        private static readonly int MU_TYPES_TOP = int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_TYPES_TOP"), out int top) ? top : 8;
        private static readonly long SpinMicroseconds = long.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_SPIN_US"), out long spin) ? spin : 0;
        private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("MU_STRESS_TRACE") == "1";
        private static readonly bool TypeTraceEnabled = ObjectTypeProfiler.Enabled = TraceEnabled && Environment.GetEnvironmentVariable("MU_STRESS_TYPES_PROFILE") != "0";
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

        private static int[] _memBench;
        private static int[] _cacheBench;

        /// <summary>
        /// Dependent random reads over 2 MB (L3 hits, L2 misses): their latency follows the ring/uncore clock,
        /// which the arithmetic benchmark does not see.
        /// </summary>
        private static double CacheBenchmarkMicroseconds()
        {
            const int length = 512 * 1024;
            if (_cacheBench == null)
            {
                _cacheBench = new int[length];
                // Single random cycle so every read depends on the previous one.
                var order = new int[length];
                for (int i = 0; i < length; i++) order[i] = i;
                var random = new Random(11);
                for (int i = length - 1; i > 0; i--) { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                for (int i = 0; i < length; i++) _cacheBench[order[i]] = order[(i + 1) % length];
            }

            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                long start = Stopwatch.GetTimestamp();
                int index = 0;
                for (int i = 0; i < 100_000; i++)
                    index = _cacheBench[index];
                _benchSink += index;
                best = Math.Min(best, (Stopwatch.GetTimestamp() - start) * 1_000_000.0 / Stopwatch.Frequency);
            }
            return best;
        }

        /// <summary>
        /// Dependent random reads over 64 MB (cache misses to DRAM). Unlike the arithmetic benchmark this slows
        /// down when the integrated GPU competes for memory bandwidth/latency.
        /// </summary>
        private static double MemoryBenchmarkMicroseconds()
        {
            const int length = 16 * 1024 * 1024;
            if (_memBench == null)
            {
                _memBench = new int[length];
                var random = new Random(7);
                for (int i = 0; i < length; i++)
                    _memBench[i] = random.Next(length);
            }

            long start = Stopwatch.GetTimestamp();
            int index = 0;
            for (int i = 0; i < 20_000; i++)
                index = _memBench[index];
            _benchSink += index;
            return (Stopwatch.GetTimestamp() - start) * 1_000_000.0 / Stopwatch.Frequency;
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
                        $"[stress-trace] t={_traceSecond} fps={_traceFrames} worstMs={_traceWorstMs:F1} slow15={_traceSlow} poses={_tracePoses / _traceFrames} dirty={_traceDirty / _traceFrames} paletteKB={_traceBytes / 1024 / _traceFrames} allocKB={(GC.GetTotalAllocatedBytes() - _traceAlloc) / 1024} gc={GC.CollectionCount(0) - _traceGc0}/{GC.CollectionCount(1) - _traceGc1}/{GC.CollectionCount(2) - _traceGc2} upd={_traceUpd / _traceFrames:F2} draw={_traceDraw / _traceFrames:F2} wobj={_traceWobj / _traceFrames:F2} terr={_traceTerr / _traceFrames:F2} after={_traceAfter / _traceFrames:F2} post={_tracePost / _traceFrames:F2} fw={_traceFw / _traceFrames:F2} scene={_traceScene / _traceFrames:F2} wbase={_traceWbase / _traceFrames:F2} shadow={_traceShadow / _traceFrames:F2} procCpu={(Process.GetCurrentProcess().TotalProcessorTime - _traceProcCpu).TotalMilliseconds:F0} mainCpu={(MainThreadCpu() - _traceMainCpu).TotalMilliseconds:F0} tpItems={ThreadPool.CompletedWorkItemCount - _traceTpItems} tpThreads={ThreadPool.ThreadCount} gcPauseMs={(GC.GetTotalPauseDuration() - _traceGcPause).TotalMilliseconds:F1} benchUs={CpuBenchmarkMicroseconds():F0} memBenchUs={MemoryBenchmarkMicroseconds():F0} l3BenchUs={CacheBenchmarkMicroseconds():F0} composite={ModelObject.LastFramePlayerComposites}/{ModelObject.LastFramePlayerCompositeMeshes} gpuDraws={_traceGpuDraws / _traceFrames} gpuPrims={_traceGpuPrims / _traceFrames} gpuTex={_traceGpuTex / _traceFrames} gpuVs={_traceGpuVs / _traceFrames} gpuPs={_traceGpuPs / _traceFrames} gpuTargets={_traceGpuTargets / _traceFrames} sprites={_traceSprites / _traceFrames} shProj={ModelObject.DiagProjectedShadowDraws / _traceFrames} dlPrep={ModelObject.DiagDynamicLightingPrepares / _traceFrames}/{ModelObject.DiagDynamicLightingPrepareTicks * 1000.0 / Stopwatch.Frequency / _traceFrames:F2}ms blob={ModelObject.DiagBlobShadowDraws / _traceFrames} crowdFlush={_traceCrowdFlushes / (double)_traceFrames:F2} dc={FormatSteps(_traceStepDraws, _traceFrames)} tc={FormatSteps(_traceStepTextures, _traceFrames)}"));
                    ModelObject.DiagBlobShadowDraws = 0;
                    ModelObject.DiagProjectedShadowDraws = 0;
                    ModelObject.DiagDynamicLightingPrepares = ModelObject.DiagDynamicLightingPrepareTicks = 0;
                    Console.WriteLine($"[stress-types] t={_traceSecond} {ObjectTypeProfiler.TakeTop(MU_TYPES_TOP, _traceFrames)}");
                    Console.WriteLine($"[stress-paths] t={_traceSecond} {DrawPathCounter.Take(_traceFrames)}");
                }
                _traceGpuDraws = _traceGpuPrims = _traceGpuTex = _traceGpuVs = _traceGpuPs = _traceGpuTargets = _traceSprites = 0;
                _traceCrowdFlushes = 0;
                Array.Clear(_traceStepDraws);
                Array.Clear(_traceStepTextures);
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
            _traceCrowdFlushes += ModelObject.LastFrameWalkerCrowdMultiPosePaletteUploads;
            RenderDrawCounter.Mark(RenderDrawCounter.Step.Other);
            for (int step = 0; step < (int)RenderDrawCounter.Step.Count; step++)
            {
                _traceStepDraws[step] += RenderDrawCounter.GetDraws((RenderDrawCounter.Step)step);
                _traceStepTextures[step] += RenderDrawCounter.GetTextures((RenderDrawCounter.Step)step);
            }
            // Device metrics of this frame (reset by MonoGame at Present, which follows OnFrame).
            if (MuGame.Instance?.GraphicsDevice is { } device)
            {
                var gm = device.Metrics;
                _traceGpuDraws += gm.DrawCount; _traceGpuPrims += gm.PrimitiveCount; _traceGpuTex += gm.TextureCount;
                _traceGpuVs += gm.VertexShaderCount; _traceGpuPs += gm.PixelShaderCount; _traceGpuTargets += gm.TargetCount;
                _traceSprites += gm.SpriteCount;
            }
        }

        private static long _traceGpuDraws, _traceGpuPrims, _traceGpuTex, _traceGpuVs, _traceGpuPs, _traceGpuTargets, _traceSprites;
        private static long _traceCrowdFlushes;
        private static readonly long[] _traceStepDraws = new long[(int)RenderDrawCounter.Step.Count];
        private static readonly long[] _traceStepTextures = new long[(int)RenderDrawCounter.Step.Count];

        /// <summary>Per-frame averages per world render step, in <see cref="RenderDrawCounter.Step"/> order.</summary>
        private static string FormatSteps(long[] totals, int frames)
        {
            var parts = new string[totals.Length];
            for (int i = 0; i < totals.Length; i++)
                parts[i] = $"{(RenderDrawCounter.Step)i}:{totals[i] / frames}";
            return string.Join(",", parts);
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
            if (Environment.GetEnvironmentVariable("MU_STRESS_DRAWBENCH") == "1")
                DrawBench(game);
        }

        /// <summary>MU_STRESS_SHOTS=directory saves the back buffer of every measured scene as PNG.</summary>
        // MU_STRESS_DRAWBENCH=1: per-draw cost of player part meshes with and without state changes
        // (shader switch, palette switch between players, texture switch), printed after each scene.
        private static void DrawBench(MuGame game)
        {
            var worldObjects = game.ActiveScene?.World?.Objects;
            if (worldObjects == null) return;
            var players = new List<Objects.Player.PlayerObject>();
            foreach (var obj in worldObjects)
                if (obj is Objects.Player.PlayerObject p && p.Armor?.Model != null && p.Helm?.Model != null && p.Pants?.Model != null) players.Add(p);
            if (players.Count < 2) return;

            ModelObject itemPart = null, plainPart = null;
            foreach (var p in players)
            {
                if (itemPart == null && p.Armor.ItemLevel >= 7) itemPart = p.Armor;
                if (plainPart == null && p.Armor.ItemLevel < 3) plainPart = p.Armor;
            }

            void Bench(string name, Action<int> draw)
            {
                for (int round = 0; round < 2; round++)
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 400; i++) draw(i);
                    double f = 1_000_000.0 / Stopwatch.Frequency / 400;
                    double us = (Stopwatch.GetTimestamp() - start) * f;
                    Console.WriteLine($"[bench] {name} {us:F1} us/draw");
                }
            }

            Bench("same mesh", i => players[0].Armor.DrawMesh(0));
            if (itemPart != null) Bench("same itemMaterial mesh", i => itemPart.DrawMesh(0));
            if (itemPart != null && plainPart != null) Bench("alternate shaders", i => (i % 2 == 0 ? itemPart : plainPart).DrawMesh(0));
            Bench("alternate players same part", i => players[i % 2].Pants.DrawMesh(0));
            Bench("alternate textures one player", i => (i % 2 == 0 ? (ModelObject)players[0].Pants : players[0].Armor).DrawMesh(0));

            // Reference costs without game code: the state left by the last DrawMesh, redrawn as is, and with a
            // per-draw matrix change (constant buffer upload) before the draw.
            var gd = game.GraphicsDevice;
            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            int primitives = gd.Indices != null ? gd.Indices.IndexCount / 3 : 0;
            if (effect != null && primitives > 0)
            {
                var world = effect.Parameters["World"];
                var pass = effect.CurrentTechnique.Passes[0];
                pass.Apply();
                Bench("raw draw", i => gd.DrawIndexedPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleList, 0, 0, primitives));
                Bench("raw world+apply+draw", i =>
                {
                    world?.SetValue(Matrix.CreateTranslation(i & 7, 0f, 0f));
                    pass.Apply();
                    gd.DrawIndexedPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleList, 0, 0, primitives);
                });
                Bench("raw apply only", i =>
                {
                    world?.SetValue(Matrix.CreateTranslation(i & 7, 0f, 0f));
                    pass.Apply();
                });
            }
        }

        internal static void SaveScreenshot(MuGame game, string name)
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

        // MU_STRESS_PLAYERS=N spawns N players (AddCharacterToScopeExtended) with varied classes, sets, weapons,
        // glow levels and wings around the hero for every scene; MU_STRESS_PLAYER_WINGS=0 removes the wings,
        // MU_STRESS_PLAYER_SAME=1 gives everybody the same class and equipment. MU_STRESS_COUNT=0: players only.
        private static readonly int PlayerCount = int.TryParse(Environment.GetEnvironmentVariable("MU_STRESS_PLAYERS"), out int players) ? players : 0;
        private static readonly bool PlayerWings = Environment.GetEnvironmentVariable("MU_STRESS_PLAYER_WINGS") != "0";
        private static readonly bool PlayerSame = Environment.GetEnvironmentVariable("MU_STRESS_PLAYER_SAME") == "1";
        private const ushort FirstPlayerId = 0x0400;

        private static readonly byte[] PlayerClasses = { 4, 7, 0, 3, 8, 11, 12, 13, 16, 17, 20, 23, 24, 25 };
        // Wings: 1st/2nd level (0..6), 3rd level (36..43).
        private static readonly short[] PlayerWingNumbers = { 0, 1, 2, 3, 4, 5, 6, 36, 37, 38, 39, 40, 41, 42, 43 };

        private static void SpawnPlayers()
        {
            if (PlayerCount <= 0)
                return;

            int side = (int)Math.Ceiling(Math.Sqrt(PlayerCount));
            for (int i = 0; i < PlayerCount; i++)
            {
                int v = PlayerSame ? 1 : i;
                byte classValue = PlayerClasses[v % PlayerClasses.Length];
                byte x = (byte)(HeroX - side + (i % side) * 2 + 1);
                byte y = (byte)(HeroY - side + (i / side) * 2 + 1);
                Inject(BuildPlayerPacket((ushort)(FirstPlayerId + i), x, y, $"Stress{i}", classValue, v));
            }
        }

        private static byte[] BuildPlayerPacket(ushort id, byte x, byte y, string name, byte classValue, int variant,
            (byte Group, short Number)? slot0 = null, (byte Group, short Number)? slot1 = null, int? direction = null, bool bare = false)
        {
            var data = new byte[54];
            data[0] = 0xC2;
            data[1] = (byte)(data.Length >> 8);
            data[2] = (byte)data.Length;
            data[3] = 0x12;
            data[4] = (byte)id; data[5] = (byte)(id >> 8);
            data[6] = x; data[7] = y; data[8] = x; data[9] = y;
            data[10] = (byte)(((direction ?? variant) % 8) << 4);
            System.Text.Encoding.UTF8.GetBytes(name, 0, Math.Min(name.Length, 10), data, 16);
            var app = data.AsSpan(26);
            app[0] = classValue;
            app[1] = 0;
            var items = app.Slice(2, 25);
            byte glow = (byte)(variant % 8);
            bool excellent = variant % 3 == 1;
            bool ancient = variant % 5 == 2;
            int set = 1 + (variant * 7) % 30;
            // Weapons by class family: swords, staffs, bows, scepters, books, gloves.
            (byte Group, short Number) weapon = classValue switch
            {
                0 or 2 or 3 => ((byte)5, (short)(variant % 10)),
                8 or 10 or 11 => ((byte)4, (short)(variant % 18)),
                16 or 17 => ((byte)2, (short)(8 + variant % 6)),
                20 or 22 or 23 => ((byte)5, (short)(21 + variant % 6)),
                24 or 25 => ((byte)0, (short)(32 + variant % 4)),
                _ => ((byte)0, (short)(variant % 21)),
            };
            var first = slot0 ?? weapon;
            var second = slot1 ?? ((byte)6, (short)(variant % 15));
            WriteShiny(items[0..3], first.Group, first.Number, glow, excellent, ancient);
            WriteShiny(items[3..6], second.Group, second.Number, glow, excellent, ancient);
            for (int part = 0; part < 5; part++)
            {
                if (bare)
                {
                    items[6 + part * 3] = 0xFF; items[7 + part * 3] = 0xFF; items[8 + part * 3] = 0;
                    continue;
                }
                WriteShiny(items[(6 + part * 3)..(9 + part * 3)], (byte)(7 + part), (short)set, glow, excellent, ancient);
            }
            if (PlayerWings)
            {
                short wing = PlayerWingNumbers[variant % PlayerWingNumbers.Length];
                items[21] = (byte)((12 << 4) | ((wing >> 8) & 0x0F));
                items[22] = (byte)wing;
            }
            else
            {
                items[21] = 0xFF; items[22] = 0xFF;
            }
            items[23] = 0xFF; items[24] = 0xFF; // no pet
            app[27] = 0; // no effects
            return data;
        }

        private static void WriteShiny(Span<byte> target, byte group, short number, byte glow, bool excellent, bool ancient)
        {
            target[0] = (byte)((group << 4) | ((number >> 8) & 0x0F));
            target[1] = (byte)number;
            target[2] = (byte)((glow << 4) | (excellent ? 0x08 : 0) | (ancient ? 0x04 : 0));
        }

        private static byte[] BuildDespawnPacket(int count, ushort firstId = FirstMonsterId)
        {
            var data = new byte[4 + count * 2];
            data[0] = 0xC1;
            data[1] = (byte)data.Length;
            data[2] = 0x14;
            data[3] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                ushort id = (ushort)(firstId + i);
                data[4 + i * 2] = (byte)(id >> 8);
                data[5 + i * 2] = (byte)id;
            }
            return data;
        }
    }
}
