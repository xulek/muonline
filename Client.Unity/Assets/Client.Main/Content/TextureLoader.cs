using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Client.Data;
using Client.Data.Texture;
using UnityEngine;

namespace Client.Main.Content
{
    public class TextureLoader
    {
        public static TextureLoader Instance { get; } = new TextureLoader();

        private readonly ConcurrentDictionary<string, Task<TextureData>> _textureTasks = new();
        private readonly ConcurrentDictionary<string, ClientTexture> _textures = new();

        private readonly Dictionary<string, BaseReader<TextureData>> _readers = new()
        {
            { ".ozt", new OZTReader() },
            { ".tga", new OZTReader() },
            { ".ozj", new OZJReader() },
            { ".jpg", new OZJReader() },
            { ".ozp", new OZPReader() },
            { ".png", new OZPReader() },
            { ".ozd", new OZDReader() },
            { ".dds", new OZDReader() }
        };


        public Task<TextureData> Prepare(string path)
        {
            UnityEngine.Debug.Log("This is from Prepare: " + path);

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

            string normalizedKey = path.ToLowerInvariant();

            if (_textureTasks.TryGetValue(normalizedKey, out var task))
                return task;

            task = InternalPrepare(path);
            _textureTasks.TryAdd(normalizedKey, task);
            return task;
        }

        public async Task<Texture2D> PrepareAndGetTexture(string path)
        {
            UnityEngine.Debug.Log("This is from PrepareAndGetTexture: " + path);
            await Prepare(path);
            return GetTexture2D(path);
        }

        private async Task<TextureData> InternalPrepare(string path)
        {
            try
            {
                var dataPath = Path.Combine(Constants.DataPath2, path);
                string ext = Path.GetExtension(path)?.ToLowerInvariant();

                UnityEngine.Debug.Log("This is from InternalPrepare, path: " + path);
                UnityEngine.Debug.Log("This is from InternalPrepare, dataPath: " + dataPath);
                UnityEngine.Debug.Log("This is from InternalPrepare, ext: " + ext);

                if (!_readers.TryGetValue(ext, out var reader))
                {
                    UnityEngine.Debug.Log($"Unsupported file extension: {ext}");
                    return null;
                }

                string fullPath = FindTexturePath(dataPath, ext);

                UnityEngine.Debug.Log("fullpath from internal prepare: " + fullPath);

                if (fullPath == null) return null;

                var data = await reader.Load(fullPath);

                if (data == null)
                {
                    UnityEngine.Debug.Log($"Failed to load texture data from: {fullPath}");
                    return null;
                }

                var clientTexture = new ClientTexture
                {
                    Info = data,
                    Script = ParseScript(path)
                };

                _textures.TryAdd(path.ToLowerInvariant(), clientTexture);
                return clientTexture.Info;
            }
            catch (Exception)
            {
                //Debug.WriteLine($"Failed to load asset {path}: {e.Message}");
                return null;
            }
        }

        private string FindTexturePath(string dataPath, string ext)
        {
            string expectedExtension = _readers[ext].GetType().Name.ToLowerInvariant().Replace("reader", "");
            string expectedFilePath = Path.ChangeExtension(dataPath, expectedExtension);

            UnityEngine.Debug.Log("This is from FindTexturePath, expectedFilePath: " + expectedFilePath + "-" + " expectedExtension: " + expectedExtension);

            string actualPath = GetActualPath(expectedFilePath);
            if (actualPath != null)
                return actualPath;

            string parentFolder = Path.GetDirectoryName(expectedFilePath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                string newFullPath = Path.Combine(parentFolder, "texture", Path.GetFileName(expectedFilePath));
                actualPath = GetActualPath(newFullPath);
                if (actualPath != null)
                    return actualPath;
            }

            //Debug.WriteLine($"Texture file not found: {expectedFilePath}");
            return null;
        }

        private string GetActualPath(string path)
        {
            if (File.Exists(path))
                return path;

            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);

            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }

            return null;
        }

        private static TextureScript ParseScript(string fileName)
        {
            if (fileName.Contains("mu_rgb_lights.jpg", StringComparison.OrdinalIgnoreCase))
                return new TextureScript { Bright = true };

            var tokens = Path.GetFileNameWithoutExtension(fileName).Split('_');

            if (tokens.Length > 1)
            {
                var script = new TextureScript();
                var token = tokens[^1].ToLowerInvariant();

                switch (token)
                {
                    case "a": script.Alpha = true; break;
                    case "r": script.Bright = true; break;
                    case "h": script.HiddenMesh = true; break;
                    case "s": script.StreamMesh = true; break;
                    case "n": script.NoneBlendMesh = true; break;
                    case "dc": script.ShadowMesh = 1; break; // NoneTexture
                    case "dt": script.ShadowMesh = 2; break; // Texture
                    default: return null;
                }

                return script;
            }

            return null;
        }

        public TextureData Get(string path) =>
            string.IsNullOrWhiteSpace(path) ? null :
            _textures.TryGetValue(path.ToLowerInvariant(), out var value) ? value.Info : null;

        public TextureScript GetScript(string path) =>
            string.IsNullOrWhiteSpace(path) ? null :
            _textures.TryGetValue(path.ToLowerInvariant(), out var value) ? value.Script : null;

        public Texture2D GetTexture2D(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalizedKey = path.ToLowerInvariant();

            if (!_textures.TryGetValue(normalizedKey, out ClientTexture textureData))
                return null;

            if (textureData.Texture != null)
                return textureData.Texture;

            if (textureData.Info?.Width == 0 || textureData.Info?.Height == 0 || textureData.Info.Data == null)
                return null;

            var texture = new Texture2D((int)textureData.Info.Width, (int)textureData.Info.Height);
            textureData.Texture = texture;

            int pixelCount = texture.width * texture.height;
            int components = textureData.Info.Components;

            if (components != 3 && components != 4)
            {
                //Debug.WriteLine($"Unsupported texture components: {components} for texture {path}");
                return null;
            }

            Color[] pixelData = new Color[pixelCount];
            byte[] data = textureData.Info.Data;

            Parallel.For(0, pixelData.Length, (i) =>
            {
                int dataIndex = i * components;
                byte r = data[dataIndex];
                byte g = data[dataIndex + 1];
                byte b = data[dataIndex + 2];
                byte a = components == 4 ? data[dataIndex + 3] : (byte)255;
                pixelData[i] = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            });

            texture.SetPixels(pixelData);
            texture.Apply();
            return texture;
        }

    }
}
