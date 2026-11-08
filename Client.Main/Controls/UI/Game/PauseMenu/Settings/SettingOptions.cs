using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.PauseMenu.Settings
{
    // Option model shown by the settings window. Values are read through getters every frame, so options that
    // change each other (V-Sync / Unlimited FPS, shadow presets) always show the live state.

    internal abstract class SettingOption
    {
        public string Label { get; init; }
        public string Description { get; init; }
        /// <summary>Optional: the option is shown greyed out and ignores input while this returns false.</summary>
        public Func<bool> IsAvailable { get; init; }

        public bool Available => IsAvailable?.Invoke() ?? true;
    }

    /// <summary>Section heading inside a category.</summary>
    internal sealed class SettingHeader : SettingOption
    {
    }

    internal sealed class SettingToggle : SettingOption
    {
        public Func<bool> Get { get; init; }
        public Action<bool> Set { get; init; }
    }

    internal sealed class SettingSlider : SettingOption
    {
        public Func<float> Get { get; init; }
        public Action<float> Set { get; init; }
        public float Min { get; init; }
        public float Max { get; init; } = 100f;
        public float Step { get; init; } = 1f;
        public string Suffix { get; init; } = "%";
        /// <summary>Apply only when the drag ends (settings that persist to disk or are expensive to apply).</summary>
        public bool ApplyOnRelease { get; init; }

        public float Quantize(float value)
        {
            value = MathHelper.Clamp(value, Min, Max);
            return Step > 0f ? MathHelper.Clamp(MathF.Round((value - Min) / Step) * Step + Min, Min, Max) : value;
        }

        /// <summary>Position of <paramref name="value"/> on the track, 0..1.</summary>
        public float Fraction(float value) => Max > Min ? MathHelper.Clamp((value - Min) / (Max - Min), 0f, 1f) : 0f;

        public string Format(float value) => MathF.Round(value).ToString(CultureInfo.InvariantCulture) + Suffix;
    }

    internal sealed class SettingChoice : SettingOption
    {
        public IReadOnlyList<string> Choices { get; init; }
        /// <summary>Index of the active choice, or -1 when the current value matches none of them.</summary>
        public Func<int> GetIndex { get; init; }
        public Action<int> SetIndex { get; init; }
    }

    internal sealed class SettingsCategory
    {
        public string Name { get; init; }
        public string Icon { get; init; }
        public string Subtitle { get; init; }
        public Func<List<SettingOption>> Build { get; init; }
    }
}
