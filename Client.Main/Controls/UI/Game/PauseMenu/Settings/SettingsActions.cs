using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.PauseMenu.Settings
{
    /// <summary>Engine side effects of the settings window (graphics device, audio, persisted settings).</summary>
    internal static class SettingsActions
    {
        private static readonly ILogger Logger = MuGame.AppLoggerFactory?.CreateLogger("Settings");

        public static List<Point> SupportedResolutions(bool fullscreen)
        {
            var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
            var current = adapter.CurrentDisplayMode;
            var result = new List<Point>();

            // Standard 16:9 resolutions only, to keep the UI aspect ratio.
            foreach (var candidate in new[] { new Point(1280, 720), new Point(1600, 900), new Point(1920, 1080), new Point(2560, 1440), new Point(3840, 2160) })
            {
                bool supported;
                if (!fullscreen)
                {
                    supported = candidate.X <= current.Width && candidate.Y <= current.Height;
                }
                else
                {
                    supported = false;
                    foreach (var mode in adapter.SupportedDisplayModes)
                    {
                        if (mode.Width == candidate.X && mode.Height == candidate.Y)
                        {
                            supported = true;
                            break;
                        }
                    }
                }

                if (supported)
                    result.Add(candidate);
            }

            return result;
        }

        public static void SetResolution(int width, int height)
        {
            var settings = MuGame.AppSettings?.Graphics;
            if (settings == null)
                return;

            settings.Width = width;
            settings.Height = height;
            MuGame.ScheduleOnMainThread(() =>
            {
                MuGame.Instance.ApplyGraphicsConfiguration(settings);
                GraphicsManager.Instance.UpdateRenderScale();
            });
            MuGame.PersistDisplaySettings(width, height, settings.IsFullScreen);
        }

        public static void SetFullscreen(bool enabled)
        {
            var settings = MuGame.AppSettings?.Graphics;
            if (settings == null)
                return;

            settings.IsFullScreen = enabled;
            MuGame.ScheduleOnMainThread(() =>
            {
                MuGame.Instance.ApplyGraphicsConfiguration(settings);
                GraphicsManager.Instance.UpdateRenderScale();
            });
            MuGame.PersistDisplaySettings(settings.Width, settings.Height, enabled);
        }

        public static void SetRenderScale(float scale)
        {
            float clamped = MathHelper.Clamp(scale, 0.3f, 3.0f);
            if (Math.Abs(Constants.RENDER_SCALE - clamped) < 0.0001f)
                return;

            Constants.RENDER_SCALE = clamped;
            GraphicsManager.Instance.UpdateRenderScale();
        }

        public static void ApplyQualityPreset(GraphicsQualityPreset preset)
        {
            // Reapplying the same preset needlessly resets the graphics device and can present a black frame.
            if (GraphicsQualityManager.UserPreset == preset)
                return;

            MuGame.ScheduleOnMainThread(() =>
            {
                var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                GraphicsQualityManager.ApplyPreset(preset, adapter, Logger);
                MuGame.Instance?.ApplyGraphicsOptions();
                GraphicsManager.Instance?.UpdateRenderScale();
            });

            if (MuGame.AppSettings?.Graphics != null)
                MuGame.AppSettings.Graphics.QualityPreset = preset.ToString();
            MuGame.PersistGraphicsPreset(preset);
        }

        public static void SetVSync(bool enabled)
        {
            Constants.DISABLE_VSYNC = !enabled;
            if (enabled)
                Constants.UNLIMITED_FPS = false;
            MuGame.ScheduleOnMainThread(() => MuGame.Instance?.ApplyGraphicsOptions());
        }

        public static void SetUnlimitedFps(bool enabled)
        {
            Constants.UNLIMITED_FPS = enabled;
            if (enabled)
                Constants.DISABLE_VSYNC = true;
            MuGame.ScheduleOnMainThread(() => MuGame.Instance?.ApplyGraphicsOptions());
        }

        public static void SetGrass(bool enabled)
        {
            Constants.DRAW_GRASS = enabled;
            if (enabled)
                (MuGame.Instance?.ActiveScene as BaseScene)?.World?.Terrain?.ReloadGrassIfNeeded();
        }

        public static void OnShadowSettingChanged()
        {
            // Force the shadow map renderer to recreate render targets with the new settings.
            GraphicsManager.Instance?.ShadowMapRenderer?.EnsureRenderTarget();
        }

        public static void ApplyBackgroundMusicSetting(bool enabled)
        {
            if (!enabled)
            {
                SoundController.Instance.StopBackgroundMusic();
                return;
            }

            var music = (MuGame.Instance?.ActiveScene as BaseScene)?.World?.BackgroundMusicPath;
            if (!string.IsNullOrEmpty(music))
            {
                SoundController.Instance.PlayBackgroundMusic(music);
                SoundController.Instance.ApplyBackgroundMusicVolume();
            }
        }

        public static void ApplyDebugPanelSetting()
        {
            if (MuGame.Instance?.ActiveScene is BaseScene scene && scene.DebugPanel != null)
            {
                scene.DebugPanel.Visible = Constants.SHOW_DEBUG_PANEL;
                if (Constants.SHOW_DEBUG_PANEL)
                    scene.DebugPanel.BringToFront();
            }
        }
    }
}
