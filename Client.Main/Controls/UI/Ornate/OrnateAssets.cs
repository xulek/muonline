using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>
    /// Textures of the ornate (MU-styled) UI kit: WebP files in <c>Client.Main/Assets/Interface/Ornate</c>, embedded
    /// into the client. Every texture is addressed by its path relative to that folder without the extension
    /// (<c>"Frame/corner"</c>, <c>"Icons/resume"</c>) and loaded lazily on first use: until it is ready the getters
    /// return null and the drawing code falls back to flat colours. Assets are produced with <c>tools/ui_assets.py</c>.
    /// </summary>
    public static class OrnateAssets
    {
        public const string Root = "Interface/Ornate/";

        private static readonly ConcurrentDictionary<string, Task<Texture2D>> Tasks = new(StringComparer.OrdinalIgnoreCase);

        public static Texture2D Corner => Get("Frame/corner");
        public static Texture2D Background => Get("Frame/background");
        public static Texture2D Title => Get("Frame/title");
        public static Texture2D Divider => Get("Frame/divider");
        public static Texture2D Button => Get("Controls/button");
        public static Texture2D ButtonHover => Get("Controls/button_hover");
        public static Texture2D ButtonPrimary => Get("Controls/button_primary");
        public static Texture2D ButtonPrimaryHover => Get("Controls/button_primary_hover");
        public static Texture2D Card => Get("Controls/card");
        public static Texture2D ProgressFrame => Get("Controls/progress_frame");
        public static Texture2D LoadingSigil => Get("Controls/loading_sigil");
        public static Texture2D CardSelected => Get("Controls/card_selected");
        public static Texture2D ToggleOn => Get("Controls/toggle_on");
        public static Texture2D ToggleOff => Get("Controls/toggle_off");
        public static Texture2D SliderKnob => Get("Controls/slider_knob");
        public static Texture2D ArrowLeft => Get("Controls/arrow_left");
        public static Texture2D Close => Get("Controls/close");

        private static readonly string[] KitPaths =
        {
            "Frame/corner", "Frame/background", "Frame/title", "Frame/divider",
            "Controls/button", "Controls/button_hover", "Controls/button_primary", "Controls/button_primary_hover",
            "Controls/card", "Controls/card_selected", "Controls/toggle_on", "Controls/toggle_off",
            "Controls/slider_knob", "Controls/arrow_left", "Controls/close"
        };

        /// <summary>Icon from <c>Ornate/Icons</c>, e.g. <c>Icon("settings")</c>: medallion with its own gold ring.</summary>
        public static Texture2D Icon(string name) => string.IsNullOrEmpty(name) ? null : Get("Icons/" + name);

        /// <summary>
        /// Round emblem from <c>Ornate/Emblems</c>: the inside of an icon's medallion without its ring, made to sit in a
        /// socket of another plate (e.g. <see cref="OrnateWidgets.Card"/>).
        /// </summary>
        public static Texture2D Emblem(string name) => string.IsNullOrEmpty(name) ? null : Get("Emblems/" + name);

        /// <summary>Returns the texture when loaded, otherwise starts loading it and returns null.</summary>
        public static Texture2D Get(string path)
        {
            Task<Texture2D> task = Tasks.GetOrAdd(path, Load);
            if (!task.IsCompletedSuccessfully)
                return null;

            Texture2D texture = task.Result;
            return texture != null && !texture.IsDisposed ? texture : null;
        }

        /// <summary>
        /// Loads the frame and control textures plus the given extra paths (<c>"Icons/resume"</c>,
        /// <c>"Emblems/class_elf"</c>), so a window opens without pop-in.
        /// </summary>
        public static Task PreloadAsync(IEnumerable<string> paths = null)
        {
            var tasks = new List<Task>();
            foreach (string path in KitPaths)
                tasks.Add(Tasks.GetOrAdd(path, Load));
            if (paths != null)
                foreach (string path in paths)
                    tasks.Add(Tasks.GetOrAdd(path, Load));
            return Task.WhenAll(tasks);
        }

        private static async Task<Texture2D> Load(string path)
        {
            try
            {
                return await UiThemeManager.LoadThemeTextureAsync(Root + path + ".webp");
            }
            catch
            {
                return null;
            }
        }
    }
}
