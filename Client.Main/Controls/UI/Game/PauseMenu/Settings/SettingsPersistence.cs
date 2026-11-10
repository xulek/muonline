using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Client.Main.Controls.UI.Game.PauseMenu.Settings
{
    /// <summary>
    /// Saves the values of the settings window to the local settings file (MuOnlineSettings.Options, keyed
    /// "Category/Label") and applies them again at start, so toggles, sliders and choices survive a restart without each
    /// option needing its own save code. Options saved elsewhere (resolution, quality preset, update rates...) opt out
    /// with <see cref="SettingOption.Persist"/>.
    /// </summary>
    internal static class SettingsPersistence
    {
        private const string Section = "Options";

        public static void Save()
        {
            var logger = MuGame.AppLoggerFactory?.CreateLogger("SettingsPersistence");
            try
            {
                var values = new JsonObject();
                foreach (var (key, option) in SettingsCatalog.AllOptions())
                {
                    switch (option)
                    {
                        case SettingToggle toggle:
                            values[key] = toggle.Get();
                            break;
                        case SettingSlider slider:
                            values[key] = slider.Get();
                            break;
                        case SettingChoice choice:
                            int index = choice.GetIndex();
                            if (index >= 0 && index < choice.Choices.Count)
                                values[key] = choice.Choices[index];
                            break;
                    }
                }

                MuGame.PersistSettingsSection(Section, values);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to save settings.");
            }
        }

        /// <summary>Applies the saved values; options whose value already matches are left alone.</summary>
        public static void Restore()
        {
            var logger = MuGame.AppLoggerFactory?.CreateLogger("SettingsPersistence");
            JsonObject values = MuGame.ReadSettingsSection(Section);
            if (values == null)
                return;

            foreach (var (key, option) in SettingsCatalog.AllOptions())
            {
                if (values[key] is not JsonValue value)
                    continue;
                try
                {
                    switch (option)
                    {
                        case SettingToggle toggle when value.TryGetValue(out bool on) && toggle.Get() != on:
                            toggle.Set(on);
                            break;
                        case SettingSlider slider when value.TryGetValue(out double number) &&
                                                       MathF.Abs(slider.Get() - (float)number) > 0.0001f:
                            slider.Set(slider.Quantize(Math.Clamp((float)number, slider.Min, slider.Max)));
                            break;
                        case SettingChoice choice when value.TryGetValue(out string label):
                            int index = choice.Choices is { } choices ? IndexOf(choices, label) : -1;
                            if (index >= 0 && index != choice.GetIndex())
                                choice.SetIndex(index);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to restore setting {Key}.", key);
                }
            }
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> choices, string label)
        {
            for (int i = 0; i < choices.Count; i++)
                if (string.Equals(choices[i], label, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
