using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;
using Client.Data.OZB;
using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Client.Main.Core.Utilities.Utils;
using Client.Main.Controllers;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Handles loading of all terrain-related assets from files.
    /// </summary>
    public class TerrainLoader
    {
        private const int MaxTextureUploadsPerDispatch = 1;
        private const int WaterCausticsFrameCount = 32;

        private readonly short _worldIndex;
        private readonly TerrainData _terrainData;
        private bool _replaceTextureMapping;

        private readonly record struct TextureUploadEntry(Texture2D[] Target, int TextureIndex, string Path);

        private sealed class TerrainTextureUploadState
        {
            private readonly TerrainLoader _owner;
            private readonly TextureUploadEntry[] _entries;
            private readonly TaskCompletionSource<bool> _completion;
            private readonly string _label;
            private int _nextEntry;
            private int _loadedCount;

            public TerrainTextureUploadState(
                TerrainLoader owner,
                TextureUploadEntry[] entries,
                TaskCompletionSource<bool> completion,
                string label)
            {
                _owner = owner;
                _entries = entries;
                _completion = completion;
                _label = label;
            }

            public void ProcessNextBatch()
            {
                try
                {
                    int uploadedThisDispatch = 0;
                    while (_nextEntry < _entries.Length && uploadedThisDispatch < MaxTextureUploadsPerDispatch)
                    {
                        TextureUploadEntry entry = _entries[_nextEntry++];
                        try
                        {
                            var texture = TextureLoader.Instance.GetTexture2D(entry.Path);
                            if (texture != null && !texture.IsDisposed)
                            {
                                entry.Target[entry.TextureIndex] = texture;
                                _loadedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            // A single malformed texture must not prevent later terrain layers
                            // from being uploaded on subsequent frames.
                            Console.WriteLine($"[TerrainLoader] Failed to upload texture {entry.TextureIndex} '{entry.Path}': {ex.Message}");
                        }

                        uploadedThisDispatch++;
                    }

                    if (_nextEntry < _entries.Length)
                    {
                        MuGame.ScheduleOnMainThread(
                            static state => state.ProcessNextBatch(),
                            this,
                            MainThreadDispatcher.WorkPriority.High,
                            "TerrainLoader.UploadTextureBatch");
                        return;
                    }

                    Console.WriteLine($"[TerrainLoader] Uploaded {_loadedCount}/{_entries.Length} {_label} textures for World{_owner._worldIndex}.");

                    // Diagnostic: entries that never resolved leave their tiles permanently
                    // skipped (silent holes). List them once so missing map assets can be
                    // identified instead of blamed on rendering.
                    foreach (TextureUploadEntry missing in _entries)
                    {
                        var tex = missing.Target[missing.TextureIndex];
                        if (tex == null || tex.IsDisposed)
                            Console.WriteLine($"[TerrainLoader] Terrain texture MISSING (tiles using it will be skipped): index {missing.TextureIndex} '{missing.Path}' for World{_owner._worldIndex}.");
                    }

                    _completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }
        }

        public TerrainLoader(short worldIndex)
        {
            _worldIndex = worldIndex;
            _terrainData = new TerrainData();
        }

        public void SetTextureMapping(Dictionary<int, string> textureMapping, bool replaceDefaults)
        {
            _replaceTextureMapping = replaceDefaults;
            if (textureMapping == null)
                return;

            if (replaceDefaults)
            {
                _terrainData.TextureMappingFiles.Clear();
            }

            // Apply overrides (or replace defaults when requested)
            foreach (var kvp in textureMapping)
            {
                _terrainData.TextureMappingFiles[kvp.Key] = kvp.Value;
            }
        }

        public async Task<TerrainData> LoadAsync()
        {
            var terrainReader = new ATTReader();
            var ozbReader = new OZBReader();
            var mappingReader = new MapReader();

            var tasks = new List<Task>();
            var worldFolder = $"World{_worldIndex}";
            var fullPathWorldFolder = Path.Combine(Constants.DataPath, worldFolder);

            if (string.IsNullOrEmpty(fullPathWorldFolder) || !Directory.Exists(fullPathWorldFolder))
                return null;

            // Load terrain .att
            string attPath = GetActualPath(Path.Combine(fullPathWorldFolder, $"EncTerrain{_worldIndex}.att"));
            if (!string.IsNullOrEmpty(attPath))
                tasks.Add(terrainReader.Load(attPath).ContinueWith(t => _terrainData.Attributes = t.Result));

            // Load base terrain height map
            string heightPath = GetActualPath(Path.Combine(fullPathWorldFolder, "TerrainHeight.OZB"));
            if (!string.IsNullOrEmpty(heightPath))
                tasks.Add(ozbReader.Load(heightPath)
                    .ContinueWith(t => _terrainData.HeightMap = t.Result.Data.Select(x => new Color(x.R, x.G, x.B)).ToArray()));

            // Load terrain mapping (.map)
            string mapPath = GetActualPath(Path.Combine(fullPathWorldFolder, $"EncTerrain{_worldIndex}.map"));
            if (!string.IsNullOrEmpty(mapPath))
                tasks.Add(mappingReader.Load(mapPath).ContinueWith(t => _terrainData.Mapping = t.Result));

            // Prepare texture file list
            var textureMapFiles = new string[256];
            foreach (var kvp in _terrainData.TextureMappingFiles)
            {
                textureMapFiles[kvp.Key] = GetActualPath(Path.Combine(fullPathWorldFolder, kvp.Value));
            }
            if (!_replaceTextureMapping)
            {
                for (int i = 1; i <= 16; i++)
                {
                    var extTilePath = GetActualPath(Path.Combine(fullPathWorldFolder, $"ExtTile{i:00}.ozj"));
                    textureMapFiles[13 + i] = extTilePath;
                }
            }

            _terrainData.TexturePaths = textureMapFiles;
            _terrainData.Textures = new Texture2D[textureMapFiles.Length];

            // SourceMain IsDoppelGanger3(): the animated-water flipbook is shared by
            // WD_7ATLANSE and WD_67DOPPEL_GANGER3 (client world indexes 8 and 68).
            if (_worldIndex == 8 || _worldIndex == 68)
            {
                _terrainData.WaterCausticsTexturePaths = new string[WaterCausticsFrameCount];
                _terrainData.WaterCausticsTextures = new Texture2D[WaterCausticsFrameCount];

                for (int frame = 0; frame < WaterCausticsFrameCount; frame++)
                {
                    string path = $"Object8/wt{frame:00}.jpg";
                    _terrainData.WaterCausticsTexturePaths[frame] = path;
                    tasks.Add(TextureLoader.Instance.Prepare(path));
                }
            }

            for (int t = 0; t < textureMapFiles.Length; t++)
            {
                var path = textureMapFiles[t];
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                // File I/O and decoding may run in parallel, but Texture2D creation and SetData
                // must be serialized on the MonoGame graphics thread. Creating several terrain
                // textures concurrently on worker threads caused missing tiles and corrupted data.
                tasks.Add(TextureLoader.Instance.Prepare(path));
            }

            // Load lightmap or default to white
            string textureLightPath = GetActualPath(Path.Combine(fullPathWorldFolder, "TerrainLight.OZB"));
            if (!string.IsNullOrEmpty(textureLightPath) && File.Exists(textureLightPath))
            {
                tasks.Add(ozbReader.Load(textureLightPath)
                    .ContinueWith(ozb => _terrainData.LightData = ozb.Result.Data.Select(x => new Color(x.R, x.G, x.B)).ToArray()));
            }
            else
            {
                _terrainData.LightData = Enumerable.Repeat(Microsoft.Xna.Framework.Color.White, Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE).ToArray();
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            await UploadTexturesOnGraphicsThreadAsync(
                textureMapFiles,
                _terrainData.Textures,
                "terrain").ConfigureAwait(false);

            if (_terrainData.WaterCausticsTexturePaths != null &&
                _terrainData.WaterCausticsTextures != null)
            {
                await UploadTexturesOnGraphicsThreadAsync(
                    _terrainData.WaterCausticsTexturePaths,
                    _terrainData.WaterCausticsTextures,
                    "Atlans caustics").ConfigureAwait(false);
            }

            _terrainData.GrassWind = new float[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];

            return _terrainData;
        }

        private Task UploadTexturesOnGraphicsThreadAsync(
            string[] texturePaths,
            Texture2D[] target,
            string label)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entries = new List<TextureUploadEntry>();

            for (int textureIndex = 0; textureIndex < texturePaths.Length; textureIndex++)
            {
                string path = texturePaths[textureIndex];
                if (!string.IsNullOrEmpty(path))
                    entries.Add(new TextureUploadEntry(target, textureIndex, path));
            }

            if (entries.Count == 0)
            {
                completion.TrySetResult(true);
                return completion.Task;
            }

            var state = new TerrainTextureUploadState(this, entries.ToArray(), completion, label);
            if (MuGame.IsMainThread)
            {
                state.ProcessNextBatch();
            }
            else
            {
                MuGame.ScheduleOnMainThread(
                    static uploadState => uploadState.ProcessNextBatch(),
                    state,
                    MainThreadDispatcher.WorkPriority.High,
                    "TerrainLoader.UploadTextureBatch");
            }

            return completion.Task;
        }

    }
}
