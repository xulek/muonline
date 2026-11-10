using System;
using System.Collections.Generic;
using System.Globalization;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.PauseMenu.Settings
{
    /// <summary>
    /// Categories and options of the settings window. To add an option, append a SettingToggle / SettingSlider /
    /// SettingChoice to the category builder; side effects that touch the engine belong in <see cref="SettingsActions"/>.
    /// </summary>
    internal static class SettingsCatalog
    {
        /// <summary>Every option of every category, with its "Category/Label" key.</summary>
        public static IEnumerable<(string Key, SettingOption Option)> AllOptions()
        {
            foreach (var category in Create(() => { }))
            {
                if (category.Name == "Advanced")
                    continue;   // debug views: never restored
                foreach (var option in category.Build())
                    if (option is not SettingHeader && option.Persist)
                        yield return ($"{category.Name}/{option.Label}", option);
            }
        }

        public static List<SettingsCategory> Create(Action rebuild) => new()
        {
            new SettingsCategory { Name = "Display", Icon = "display", Subtitle = "Resolution, window mode and frame rate", Build = () => BuildDisplay(rebuild) },
            new SettingsCategory { Name = "Graphics", Icon = "graphics", Subtitle = "Overall quality, textures and shadows", Build = BuildGraphics },
            new SettingsCategory { Name = "Lighting", Icon = "lighting", Subtitle = "Sun, dynamic lights and material shaders", Build = BuildLighting },
            new SettingsCategory { Name = "Audio", Icon = "audio", Subtitle = "Music and sound effects", Build = BuildAudio },
            new SettingsCategory { Name = "Interface", Icon = "interface", Subtitle = "HUD style and on-screen information", Build = BuildInterface },
            new SettingsCategory { Name = "Performance", Icon = "performance", Subtitle = "Update rates and low-end hardware options", Build = BuildPerformance },
            new SettingsCategory { Name = "Advanced", Icon = "advanced", Subtitle = "Developer and debugging views", Build = BuildAdvanced },
        };

        // --- Categories ---------------------------------------------------------------------------------

        private static List<SettingOption> BuildDisplay(Action rebuild)
        {
            var list = new List<SettingOption> { new SettingHeader { Label = "Screen" } };

            var settings = MuGame.AppSettings?.Graphics;
            if (settings != null)
            {
                var resolutions = SettingsActions.SupportedResolutions(settings.IsFullScreen);
                var labels = resolutions.ConvertAll(r => $"{r.X} x {r.Y}");
                list.Add(new SettingChoice
                {
                    Label = "Resolution",
                    Persist = false,
                    Description = "Window or screen size. 16:9 modes keep the interface proportions intact.",
                    Choices = labels,
                    GetIndex = () => resolutions.FindIndex(r => r.X == settings.Width && r.Y == settings.Height),
                    SetIndex = i => SettingsActions.SetResolution(resolutions[i].X, resolutions[i].Y)
                });
                list.Add(new SettingToggle
                {
                    Label = "Fullscreen",
                    Persist = false,
                    Description = "Run the game in exclusive fullscreen instead of a window.",
                    Get = () => settings.IsFullScreen,
                    Set = value =>
                    {
                        SettingsActions.SetFullscreen(value);
                        rebuild(); // fullscreen limits the resolution list to adapter modes
                    }
                });
            }

            float[] scales = { 0.375f, 0.5f, 0.6f, 0.75f, 0.9f, 1f, 1.25f, 1.5f, 2f, 3f };
            list.Add(new SettingChoice
            {
                Label = "Render Scale",
                Description = "Internal 3D resolution. Below 100% is faster, above 100% supersamples for sharper edges.",
                Choices = Array.ConvertAll(scales, s => (s * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%"),
                GetIndex = () => Array.FindIndex(scales, s => MathF.Abs(Constants.RENDER_SCALE - s) < 0.01f),
                SetIndex = i => SettingsActions.SetRenderScale(scales[i])
            });

            list.Add(new SettingHeader { Label = "Frame Rate" });
            list.Add(new SettingToggle
            {
                Label = "V-Sync",
                Description = "Synchronise frames with the monitor refresh rate to remove tearing.",
                Get = () => !Constants.DISABLE_VSYNC,
                Set = SettingsActions.SetVSync
            });
            list.Add(new SettingToggle
            {
                Label = "Unlimited FPS",
                Description = "Render as fast as possible. Turns V-Sync off.",
                Get = () => Constants.UNLIMITED_FPS,
                Set = SettingsActions.SetUnlimitedFps
            });
            return list;
        }

        private static List<SettingOption> BuildGraphics()
        {
            GraphicsQualityPreset[] presets = { GraphicsQualityPreset.Auto, GraphicsQualityPreset.Low, GraphicsQualityPreset.Medium, GraphicsQualityPreset.High };
            Constants.ShadowQuality[] shadows =
            {
                Constants.ShadowQuality.Off, Constants.ShadowQuality.Low, Constants.ShadowQuality.Medium,
                Constants.ShadowQuality.High, Constants.ShadowQuality.Ultra
            };

            return new List<SettingOption>
            {
                new SettingHeader { Label = "Quality" },
                new SettingChoice
                {
                    Label = "Quality Preset",
                    Persist = false,
                    Description = "Auto detects your GPU. Low 0.75x, Medium 0.90x and High 1.00x render scale.",
                    Choices = new[] { "Auto", "Low", "Medium", "High" },
                    GetIndex = () => Array.IndexOf(presets, GraphicsQualityManager.UserPreset),
                    SetIndex = i => SettingsActions.ApplyQualityPreset(presets[i])
                },
                new SettingToggle
                {
                    Label = "High Quality Textures",
                    Description = "Use full resolution textures for the world and characters.",
                    Get = () => Constants.HIGH_QUALITY_TEXTURES,
                    Set = v => Constants.HIGH_QUALITY_TEXTURES = v
                },
                new SettingToggle
                {
                    Label = "Remastered Textures",
                    Persist = false,
                    Description = "Detailed high resolution textures for buildings, walls, ground and water. Off shows the original ones.",
                    Get = () => Constants.REMASTERED_TEXTURES,
                    Set = SettingsActions.SetRemasteredTextures
                },
                new SettingToggle
                {
                    Label = "Anti-Aliasing (FXAA)",
                    Description = "Smooth jagged edges with a fast post-process filter.",
                    Get = () => GraphicsManager.Instance.IsFXAAEnabled,
                    Set = v => GraphicsManager.Instance.IsFXAAEnabled = v
                },
                new SettingToggle
                {
                    Label = "Grass",
                    Description = "Draw grass on the terrain.",
                    Get = () => Constants.DRAW_GRASS,
                    Set = SettingsActions.SetGrass
                },
                new SettingToggle
                {
                    Label = "Shadows on Grass",
                    Description = "Shadows of characters and objects fall on the grass blades, not only on the ground under them.",
                    Get = () => GroundShadowMask.Enabled,
                    Set = v => GroundShadowMask.Enabled = v,
                    IsAvailable = () => Constants.DRAW_GRASS
                },
                new SettingToggle
                {
                    Label = "Ground Variation",
                    Description = "Breaks up the repeating pattern of ground textures.",
                    Get = () => Constants.TERRAIN_ANTI_TILING,
                    Set = v => Constants.TERRAIN_ANTI_TILING = v
                },
                new SettingToggle
                {
                    Label = "Weapon Trails",
                    Description = "Glowing trails behind swung weapons.",
                    Get = () => Constants.ENABLE_WEAPON_TRAIL,
                    Set = v => Constants.ENABLE_WEAPON_TRAIL = v
                },
                new SettingToggle
                {
                    Label = "Menu Scene Effects",
                    Description = "Glow, animated water, mist, fireflies and stars on the login and character selection scenes.",
                    Get = () => Constants.ENABLE_MENU_SCENE_EFFECTS,
                    Set = v => Constants.ENABLE_MENU_SCENE_EFFECTS = v
                },
                new SettingHeader { Label = "Shadows" },
                new SettingChoice
                {
                    Label = "Shadow Quality",
                    Description = "Shadow map resolution and distance: Low 512px, Medium/High 1024px, Ultra 2048px.",
                    Choices = new[] { "Off", "Low", "Medium", "High", "Ultra" },
                    GetIndex = () => Array.IndexOf(shadows, Constants.GetCurrentShadowQuality()),
                    SetIndex = i =>
                    {
                        Constants.ApplyShadowQualityPreset(shadows[i]);
                        SettingsActions.OnShadowSettingChanged();
                    }
                },
                new SettingToggle
                {
                    Label = "Shadow Mapping",
                    Description = "Real-time shadows cast by the sun. Enabling it picks Medium quality if shadows were off.",
                    Get = () => Constants.ENABLE_SHADOW_MAPPING,
                    Set = v =>
                    {
                        Constants.ENABLE_SHADOW_MAPPING = v;
                        if (v && Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Off)
                            Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Medium);
                        SettingsActions.OnShadowSettingChanged();
                    }
                },
                new SettingToggle
                {
                    Label = "Monster Mesh Shadows",
                    Persist = false,
                    Description = "Always give monsters full mesh shadows instead of simple blob shadows.",
                    Get = () => MuGame.AppSettings?.Graphics?.ForceMonsterMeshShadows == true,
                    Set = v =>
                    {
                        var graphics = MuGame.AppSettings?.Graphics;
                        if (graphics == null)
                            return;
                        graphics.ForceMonsterMeshShadows = v;
                        MuGame.PersistMonsterShadowMode(v);
                        SettingsActions.OnShadowSettingChanged();
                    }
                },
            };
        }

        private static List<SettingOption> BuildLighting() => new()
        {
            new SettingHeader { Label = "Sun" },
            new SettingToggle
            {
                Label = "Sun Light",
                Description = "Directional sunlight on the world.",
                Get = () => Constants.SUN_ENABLED,
                Set = v => Constants.SUN_ENABLED = v
            },
            new SettingToggle
            {
                Label = "Day-Night Cycle",
                Description = "Move the sun with the real-world clock.",
                Get = () => Constants.ENABLE_DAY_NIGHT_CYCLE,
                Set = v =>
                {
                    Constants.ENABLE_DAY_NIGHT_CYCLE = v;
                    if (!v)
                        SunCycleManager.ResetToDefault();
                }
            },
            new SettingChoice
            {
                Label = "Sun Direction",
                Description = "Side of the world the sunlight comes from.",
                Choices = new[] { "West", "East" },
                GetIndex = () => Constants.SUN_DIRECTION.X >= 0f ? 1 : 0,
                SetIndex = i =>
                {
                    var dir = Constants.SUN_DIRECTION;
                    if (dir.LengthSquared() < 0.0001f)
                        dir = new Vector3(1f, 0f, -0.6f);
                    dir.X = Math.Abs(dir.X) * (i == 1 ? 1f : -1f);
                    Constants.SUN_DIRECTION = dir;
                }
            },
            new SettingSlider
            {
                Label = "Sun Strength",
                Description = "Brightness of the sunlight.",
                Get = () => Constants.SUN_STRENGTH * 100f,
                Set = v => Constants.SUN_STRENGTH = MathHelper.Clamp(v, 0f, 200f) / 100f,
                Max = 200f,
                Step = 5f
            },
            new SettingSlider
            {
                Label = "Sun Shadow",
                Description = "Darkness of the areas the sun does not reach.",
                Get = () => Constants.SUN_SHADOW_STRENGTH * 100f,
                Set = v => Constants.SUN_SHADOW_STRENGTH = MathHelper.Clamp(v, 0f, 100f) / 100f,
                Step = 5f
            },
            new SettingHeader { Label = "Lights & Materials" },
            new SettingToggle
            {
                Label = "Dynamic Lights",
                Description = "Torches, spells and effects light up their surroundings.",
                Get = () => Constants.ENABLE_DYNAMIC_LIGHTS,
                Set = v => Constants.ENABLE_DYNAMIC_LIGHTS = v
            },
            new SettingToggle
            {
                Label = "GPU Lighting Shader",
                Description = "Compute dynamic lighting per pixel on the GPU. Required for terrain GPU lighting.",
                Get = () => Constants.ENABLE_DYNAMIC_LIGHTING_SHADER,
                Set = v =>
                {
                    Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = v;
                    if (!v)
                        Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
                }
            },
            new SettingToggle
            {
                Label = "Terrain GPU Lighting",
                Description = "Light the terrain with the GPU shader. Needs the GPU lighting shader.",
                Get = () => Constants.ENABLE_TERRAIN_GPU_LIGHTING,
                Set = v => Constants.ENABLE_TERRAIN_GPU_LIGHTING = v,
                IsAvailable = () => Constants.ENABLE_DYNAMIC_LIGHTING_SHADER
            },
            new SettingToggle
            {
                Label = "Item Material Shader",
                Description = "Metallic shine and glow on excellent and upgraded items.",
                Get = () => Constants.ENABLE_ITEM_MATERIAL_SHADER,
                Set = v => Constants.ENABLE_ITEM_MATERIAL_SHADER = v
            },
            new SettingToggle
            {
                Label = "Monster Material Shader",
                Description = "Material effects on monsters.",
                Get = () => Constants.ENABLE_MONSTER_MATERIAL_SHADER,
                Set = v => Constants.ENABLE_MONSTER_MATERIAL_SHADER = v
            },
        };

        private static List<SettingOption> BuildAudio() => new()
        {
            new SettingHeader { Label = "Music" },
            new SettingToggle
            {
                Label = "Background Music",
                Description = "Play the music of the current map.",
                Get = () => Constants.BACKGROUND_MUSIC,
                Set = v =>
                {
                    Constants.BACKGROUND_MUSIC = v;
                    SettingsActions.ApplyBackgroundMusicSetting(v);
                }
            },
            new SettingSlider
            {
                Label = "Music Volume",
                Description = "Volume of the background music.",
                Get = () => Constants.BACKGROUND_MUSIC_VOLUME,
                Set = v =>
                {
                    Constants.BACKGROUND_MUSIC_VOLUME = v;
                    if (Constants.BACKGROUND_MUSIC)
                        SoundController.Instance.ApplyBackgroundMusicVolume();
                },
                Step = 5f,
                IsAvailable = () => Constants.BACKGROUND_MUSIC
            },
            new SettingHeader { Label = "Sound Effects" },
            new SettingToggle
            {
                Label = "Sound Effects",
                Description = "Combat, skill, ambient and interface sounds.",
                Get = () => Constants.SOUND_EFFECTS,
                Set = v =>
                {
                    Constants.SOUND_EFFECTS = v;
                    SoundController.Instance.ApplySoundEffectsVolume();
                }
            },
            new SettingSlider
            {
                Label = "Effects Volume",
                Description = "Volume of all sound effects.",
                Get = () => Constants.SOUND_EFFECTS_VOLUME,
                Set = v =>
                {
                    Constants.SOUND_EFFECTS_VOLUME = v;
                    SoundController.Instance.ApplySoundEffectsVolume();
                },
                Step = 5f,
                IsAvailable = () => Constants.SOUND_EFFECTS
            },
        };

        private static List<SettingOption> BuildInterface() => new()
        {
            new SettingHeader { Label = "Layout" },
            new SettingChoice
            {
                Label = "Interface Style",
                    Persist = false,
                Description = "Modern is the original interface. Classic uses the alternate HUD, windows and assets.",
                Choices = new[] { "Modern", "Classic" },
                GetIndex = () => UiThemeManager.CurrentId == UiThemeId.Classic ? 1 : 0,
                SetIndex = i => UiThemeManager.SetTheme(i == 1 ? UiThemeId.Classic : UiThemeId.Modern)
            },
            new SettingHeader { Label = "Movement & Camera" },
            new SettingToggle
            {
                Label = "Smooth Movement",
                Description = "Characters walk paths in smooth curves without stopping at every tile. Off: original tile-by-tile walk.",
                Get = () => Constants.SMOOTH_PATH_CORNERS,
                Set = v =>
                {
                    Constants.SMOOTH_PATH_CORNERS = v;
                    Constants.CONTINUOUS_PATH_WALK = v;
                }
            },
            new SettingToggle
            {
                Label = "Smooth Camera Height",
                Description = "The camera follows the ground height smoothly instead of every bump.",
                Get = () => Constants.SMOOTH_CAMERA_HEIGHT,
                Set = v => Constants.SMOOTH_CAMERA_HEIGHT = v
            },
            new SettingHeader { Label = "Information" },
            new SettingToggle
            {
                Label = "Statistics Panel",
                Description = "Show the FPS and rendering statistics panel.",
                Get = () => Constants.SHOW_DEBUG_PANEL,
                Set = v =>
                {
                    Constants.SHOW_DEBUG_PANEL = v;
                    SettingsActions.ApplyDebugPanelSetting();
                }
            },
        };

        private static List<SettingOption> BuildPerformance() => new()
        {
            new SettingHeader { Label = "Update Rates" },
            PerformanceFpsSlider("Animation Update Rate", "How often character and monster animations are updated.",
                () => Constants.ANIMATION_UPDATE_FPS,
                (graphics, fps) =>
                {
                    Constants.ANIMATION_UPDATE_FPS = fps;
                    if (graphics != null)
                        graphics.AnimationUpdateFps = fps;
                }),
            PerformanceFpsSlider("Light Update Rate", "How often dynamic lights are recalculated.",
                () => Constants.DYNAMIC_LIGHT_UPDATE_FPS,
                (graphics, fps) =>
                {
                    Constants.DYNAMIC_LIGHT_UPDATE_FPS = fps;
                    if (graphics != null)
                        graphics.DynamicLightUpdateFps = fps;
                }),
            new SettingHeader { Label = "Hardware" },
            new SettingToggle
            {
                Label = "Optimize for Integrated GPU",
                Description = "Cheaper rendering paths for laptops and integrated graphics.",
                Get = () => Constants.OPTIMIZE_FOR_INTEGRATED_GPU,
                Set = v => Constants.OPTIMIZE_FOR_INTEGRATED_GPU = v
            },
            new SettingToggle
            {
                Label = "Dynamic Buffer Pool",
                Description = "Reuse GPU buffers between frames to reduce stutter.",
                Get = () => Constants.ENABLE_DYNAMIC_BUFFER_POOL,
                Set = DynamicBufferPool.SetEnabled
            },
            new SettingToggle
            {
                Label = "Item Material Animation",
                Description = "Animate the shine of item materials.",
                Get = () => Constants.ENABLE_ITEM_MATERIAL_ANIMATION,
                Set = v => Constants.ENABLE_ITEM_MATERIAL_ANIMATION = v
            },
            new SettingToggle
            {
                Label = "Low Quality Switch",
                Description = "Allow switching to low quality rendering when performance drops.",
                Get = () => Constants.ENABLE_LOW_QUALITY_SWITCH,
                Set = v => Constants.ENABLE_LOW_QUALITY_SWITCH = v
            },
            new SettingToggle
            {
                Label = "Low Quality on Login Screen",
                Description = "Render the login scene in low quality.",
                Get = () => Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE,
                Set = v => Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE = v
            },
        };

        private static List<SettingOption> BuildAdvanced() => new()
        {
            new SettingHeader { Label = "Debug Views" },
            new SettingToggle
            {
                Label = "Bounding Boxes",
                Description = "Outline the bounds of every world object.",
                Get = () => Constants.DRAW_BOUNDING_BOXES,
                Set = v => Constants.DRAW_BOUNDING_BOXES = v
            },
            new SettingToggle
            {
                Label = "Interactive Bounding Boxes",
                Description = "Outline the bounds of clickable objects only.",
                Get = () => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES,
                Set = v => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES = v
            },
            new SettingToggle
            {
                Label = "Lighting Areas",
                Description = "Visualise the areas affected by dynamic lights.",
                Get = () => Constants.DEBUG_LIGHTING_AREAS,
                Set = v => Constants.DEBUG_LIGHTING_AREAS = v
            },
        };

        private static SettingSlider PerformanceFpsSlider(string label, string description, Func<int> get,
            Action<Client.Main.Configuration.GraphicsSettings, int> set) => new()
        {
            Label = label,
            Description = description,
            Get = () => get(),
            Set = value =>
            {
                int fps = Constants.ClampPerformanceFps((int)value);
                var graphics = MuGame.AppSettings?.Graphics;
                set(graphics, fps);
                if (graphics != null)
                    MuGame.PersistGraphicsPerformanceCaps(graphics.DynamicLightUpdateFps, graphics.AnimationUpdateFps);
            },
            Min = Constants.MIN_PERFORMANCE_FPS_CAP,
            Max = Constants.MAX_PERFORMANCE_FPS_CAP,
            Step = 1f,
            Suffix = " FPS",
            ApplyOnRelease = true,
            Persist = false
        };
    }
}
