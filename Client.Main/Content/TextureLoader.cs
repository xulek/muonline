using System;
using System.Collections.Concurrent;
using System.Collections.Generic; // Added for Dictionary
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Data;
using Client.Data.Texture;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using Client.Main.Controllers;

namespace Client.Main.Content
{
    public class TextureLoader
    {
        public static TextureLoader Instance { get; } = new TextureLoader();

        public Func<TextureData, byte[]> CustomDecompressFunction = null;

        private readonly ConcurrentDictionary<string, Lazy<Task<TextureData>>> _textureTasks = new();
        private readonly ConcurrentDictionary<string, Lazy<Task<Texture2D>>> _gpuTextureTasks = new();
        private readonly ConcurrentDictionary<string, ClientTexture> _textures = new();
        private static readonly Lazy<Dictionary<string, string>> _bundledUiResources = new(() =>
            typeof(TextureLoader).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("BundledUi/", StringComparison.Ordinal))
                .ToDictionary(name => NormalizePathKey(name["BundledUi/".Length..]), name => name));

        // Cache: Key -> Resolved Full Path (or empty if not found)
        private readonly ConcurrentDictionary<string, string> _pathResolutionCache = new();

        private readonly CancellationTokenSource _cleanupCts = new();
        private readonly SemaphoreSlim _decodeGate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(60);
        private readonly TimeSpan _textureTtl = TimeSpan.FromMinutes(5);
        private GraphicsDevice _graphicsDevice;

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

        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<TextureLoader>();

        // Precompiled logging messages
        private static readonly Action<ILogger, string, Exception> _logUnsupportedExt =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1001, nameof(_logUnsupportedExt)), "Unsupported file extension: {Ext}");
        private static readonly Action<ILogger, string, Exception> _logFailedLoadData =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1002, nameof(_logFailedLoadData)), "Failed to load texture data from: {Path}");
        private static readonly Action<ILogger, string, Exception> _logFileNotFound =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1003, nameof(_logFileNotFound)), "Texture file not found: {Path}");

        private TextureLoader()
        {
            Task.Run(() => CleanupLoopAsync(_cleanupCts.Token));
        }

        public void SetGraphicsDevice(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            _logger = MuGame.AppLoggerFactory?.CreateLogger<TextureLoader>();
        }

        public Task<TextureData> Prepare(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

            string normalizedKey = NormalizePathKey(path);
            var lazyTextureTask = _textureTasks.GetOrAdd(
                normalizedKey,
                _ => new Lazy<Task<TextureData>>(
                    () => PrepareBoundedAsync(path),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return lazyTextureTask.Value;
        }

        private async Task<TextureData> PrepareBoundedAsync(string path)
        {
            await _decodeGate.WaitAsync(_cleanupCts.Token).ConfigureAwait(false);
            try
            {
                // Readers combine file I/O and CPU decoding. Keep both off the render thread
                // and bound concurrency to avoid thread-pool, disk and GC spikes on map entry.
                return await Task.Run(() => InternalPrepare(path), _cleanupCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _decodeGate.Release();
            }
        }

        public async Task<Texture2D> PrepareAndGetTexture(string path)
        {
            await Prepare(path).ConfigureAwait(false);

            string normalizedKey = NormalizePathKey(path);
            var lazyUpload = _gpuTextureTasks.GetOrAdd(
                normalizedKey,
                _ => new Lazy<Task<Texture2D>>(
                    () => CreateTextureOnGraphicsThreadAsync(path, normalizedKey),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return await lazyUpload.Value.ConfigureAwait(false);
        }

        private Task<Texture2D> CreateTextureOnGraphicsThreadAsync(string path, string normalizedKey)
        {
            if (MuGame.IsMainThread)
                return Task.FromResult(GetTexture2D(path));

            var completion = new TaskCompletionSource<Texture2D>(TaskCreationOptions.RunContinuationsAsynchronously);
            MuGame.ScheduleOnMainThread(static state =>
            {
                var (loader, texturePath, cacheKey, source) = state;
                try
                {
                    Texture2D texture = loader.GetTexture2D(texturePath);
                    if (texture == null)
                        loader._gpuTextureTasks.TryRemove(cacheKey, out _);
                    source.TrySetResult(texture);
                }
                catch (Exception ex)
                {
                    loader._gpuTextureTasks.TryRemove(cacheKey, out _);
                    source.TrySetException(ex);
                }
            },
            (this, path, normalizedKey, completion),
            MainThreadDispatcher.WorkPriority.High,
            "TextureLoader.UploadTexture");

            return completion.Task;
        }

        private async Task<TextureData> InternalPrepare(string path)
        {
            try
            {
                // Note: path is relative here (e.g. "Interface/GF_logo.ozj")
                path = path.Replace('\\', '/');
                var dataPath = Path.Combine(Constants.DataPath, path);
                string ext = Path.GetExtension(path)?.ToLowerInvariant();

                if (string.IsNullOrEmpty(ext) || !_readers.TryGetValue(ext, out var reader))
                {
                    if (_logger != null && _logger.IsEnabled(LogLevel.Debug)) _logUnsupportedExt(_logger, ext ?? string.Empty, null);
                    return null;
                }

                string fullPath = FindTexturePath(dataPath, ext);
                TextureData data;
                if (fullPath != null)
                {
                    data = await reader.Load(fullPath).ConfigureAwait(false);
                }
                else
                {
                    string resourcePath = Path.ChangeExtension(path, reader.GetType().Name.Replace("Reader", ""));
                    if (!_bundledUiResources.Value.TryGetValue(NormalizePathKey(resourcePath), out string resourceName))
                        return null;

                    using var stream = typeof(TextureLoader).Assembly.GetManifestResourceStream(resourceName);
                    data = await reader.Load(stream).ConfigureAwait(false);
                }
                if (data == null)
                {
                    if (_logger != null && _logger.IsEnabled(LogLevel.Debug)) _logFailedLoadData(_logger, fullPath, null);
                    return null;
                }

                // Android needs RGBA uploads for DXT assets. Decode while still on the
                // bounded loader worker, before publishing the data to the render thread.
                byte[] preparedRgba = null;
                if (data.IsCompressed && CustomDecompressFunction is { } decompress)
                {
                    preparedRgba = decompress(data);
                    if (preparedRgba == null || preparedRgba.Length != checked(data.Width * data.Height * 4))
                        throw new InvalidDataException($"Invalid decompressed texture: {path}");
                }

                PrepareMipData(data, path);

                var clientTexture = new ClientTexture
                {
                    Info = data,
                    PreparedRgba = preparedRgba,
                    Script = ParseScript(path),
                    LastAccessUtc = DateTime.UtcNow
                };

                _textures.TryAdd(NormalizePathKey(path), clientTexture);
                return clientTexture.Info;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to decode texture {Path}", path);
                return null;
            }
        }

        private string FindTexturePath(string fullPath, string ext)
        {
            if (!_readers.TryGetValue(ext, out var reader)) return null;

            // Determine expected extension based on reader type logic (legacy MU logic)
            string expectedExtension = reader.GetType().Name.ToLowerInvariant().Replace("reader", "");

            // 1. Try path with correct extension
            string expectedFilePath = Path.ChangeExtension(fullPath, expectedExtension);
            string actualPath = ResolveCaseInsensitivePath(expectedFilePath);

            if (actualPath != null)
                return actualPath;

            // 2. Try MU specific fallback: look in "texture" subdirectory of the parent
            // e.g., if looking for Data/World1/Terrain.jpg, look in Data/World1/texture/Terrain.jpg
            string parentFolder = Path.GetDirectoryName(expectedFilePath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                string fileName = Path.GetFileName(expectedFilePath);
                string fallbackPath = Path.Combine(parentFolder, "texture", fileName);
                actualPath = ResolveCaseInsensitivePath(fallbackPath);

                if (actualPath != null)
                    return actualPath;
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Debug)) _logFileNotFound(_logger, expectedFilePath, null);
            return null;
        }

        /// <summary>
        /// Resolves a file path on a case-sensitive file system by finding the actual existing file 
        /// matching the path case-insensitively. Handles both mixed-case directories and filenames.
        /// </summary>
        private string ResolveCaseInsensitivePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            string cacheKey = NormalizePathKey(fullPath);

            // Check cache
            if (_pathResolutionCache.TryGetValue(cacheKey, out var cachedPath))
                return string.IsNullOrEmpty(cachedPath) ? null : cachedPath;

            // Fast path: Check if file exists exactly as requested
            if (File.Exists(fullPath))
            {
                _pathResolutionCache.TryAdd(cacheKey, fullPath);
                return fullPath;
            }

            // Slow path: Resolve path components
            string resolvedPath = RecursiveResolvePath(fullPath);

            // Update cache (store empty string for failure to avoid repeated failed lookups)
            _pathResolutionCache.TryAdd(cacheKey, resolvedPath ?? string.Empty);

            return resolvedPath;
        }

        private string RecursiveResolvePath(string path)
        {
            // If the path points to an existing directory or file, we are good.
            if (File.Exists(path) || Directory.Exists(path))
                return path;

            // Get the parent directory
            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
                return null; // Root reached and not found

            // Recursively resolve the parent first
            // This ensures we find the "Anchor" (e.g. Constants.DataPath) correctly
            string resolvedParent = RecursiveResolvePath(parent);
            if (resolvedParent == null)
                return null; // Parent chain broken

            // Now that we have a valid parent, try to find the child (file or dir) ignoring case
            string childName = Path.GetFileName(path);

            try
            {
                // Check files in the resolved parent
                var files = Directory.GetFiles(resolvedParent);
                foreach (var file in files)
                {
                    if (string.Equals(Path.GetFileName(file), childName, StringComparison.OrdinalIgnoreCase))
                        return file;
                }

                // Check directories in the resolved parent
                var dirs = Directory.GetDirectories(resolvedParent);
                foreach (var dir in dirs)
                {
                    if (string.Equals(Path.GetFileName(dir), childName, StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch (Exception ex)
            {
                // Access denied or IO error (common on Android root dirs)
                // We ignore this and return null because we can't search here.
                _logger?.LogTrace($"Error listing directory {resolvedParent}: {ex.Message}");
            }

            return null;
        }

        private static string NormalizePathKey(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').ToLowerInvariant();
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
            _textures.TryGetValue(NormalizePathKey(path), out var value) ? TouchAndReturn(value).Info : null;

        public TextureScript GetScript(string path) =>
            string.IsNullOrWhiteSpace(path) ? null :
            _textures.TryGetValue(NormalizePathKey(path), out var value) ? TouchAndReturn(value).Script : null;

        public Texture2D GetTexture2D(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalizedKey = NormalizePathKey(path);

            if (!_textures.TryGetValue(normalizedKey, out ClientTexture clientTexture))
                return null;

            Touch(clientTexture);

            if (clientTexture.Texture != null && !clientTexture.Texture.IsDisposed)
                return clientTexture.Texture;

            var textureInfo = clientTexture.Info;
            if (textureInfo?.Width <= 0 || textureInfo.Height <= 0 || textureInfo.Data == null)
                return null;

            // Prevent duplicate GPU uploads when several async loads complete at the same time.
            // The caller is still responsible for invoking this method on the graphics thread.
            lock (clientTexture)
            {
                if (clientTexture.Texture != null && !clientTexture.Texture.IsDisposed)
                    return clientTexture.Texture;

                try
                {
                    // Generated mip data normally arrives from the background decode path.
                    // Rebuild it here only if a previously uploaded GPU texture was externally
                    // disposed and needs to be recreated.
                    PrepareMipData(textureInfo, path);

                    Texture2D texture;
                    bool isCompressed = textureInfo.IsCompressed;

                    if (CustomDecompressFunction != null && isCompressed)
                    {
                        // Custom decompressors return a single RGBA base level. Keep this path
                        // compatible with existing integrations instead of guessing their format.
                        // Normal loads arrive decoded. The fallback handles a disposed GPU
                        // resource being recreated from the retained compressed source.
                        byte[] data = clientTexture.PreparedRgba ?? CustomDecompressFunction(textureInfo);
                        texture = new Texture2D(
                            _graphicsDevice,
                            textureInfo.Width,
                            textureInfo.Height,
                            false,
                            SurfaceFormat.Color);
                        texture.SetData(data);
                    }
                    else if (isCompressed)
                    {
                        texture = CreateCompressedTexture(textureInfo);
                    }
                    else
                    {
                        texture = CreateColorTexture(textureInfo, path);
                        if (texture == null)
                            return null;
                    }

                    clientTexture.Texture = texture;
                    clientTexture.PreparedRgba = null;

                    // Runtime-generated mip levels are only staging data. The GPU owns the full
                    // chain now, so release the extra CPU memory while retaining the base asset.
                    if (textureInfo.MipDataGenerated)
                    {
                        textureInfo.MipData = Array.Empty<byte[]>();
                        textureInfo.MipDataGenerated = false;
                    }

                    return texture;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed creating Texture2D for {Path}", path);
                    return null;
                }
            }
        }

        private Texture2D CreateCompressedTexture(TextureData textureInfo)
        {
            byte[][] levels = GetUsableMipLevels(textureInfo);
            bool useMipMaps = HasCompleteMipChain(textureInfo.Width, textureInfo.Height, levels.Length);

            var texture = new Texture2D(
                _graphicsDevice,
                textureInfo.Width,
                textureInfo.Height,
                useMipMaps,
                textureInfo.Format.ToXNA());

            int uploadLevels = useMipMaps ? levels.Length : 1;
            for (int level = 0; level < uploadLevels; level++)
            {
                byte[] levelData = levels[level];
                texture.SetData(level, null, levelData, 0, levelData.Length);
            }

            return texture;
        }

        private Texture2D CreateColorTexture(TextureData textureInfo, string path)
        {
            int components = textureInfo.Components;
            if (components != 3 && components != 4)
            {
                _logger?.LogDebug(
                    "Unsupported texture components: {Components} for texture {Path}",
                    components,
                    path);
                return null;
            }

            byte[][] levels = GetUsableMipLevels(textureInfo);
            bool useMipMaps = HasCompleteMipChain(textureInfo.Width, textureInfo.Height, levels.Length);
            var texture = new Texture2D(
                _graphicsDevice,
                textureInfo.Width,
                textureInfo.Height,
                useMipMaps,
                SurfaceFormat.Color);

            int levelWidth = textureInfo.Width;
            int levelHeight = textureInfo.Height;
            int uploadLevels = useMipMaps ? levels.Length : 1;

            try
            {
                for (int level = 0; level < uploadLevels; level++)
                {
                    byte[] levelData = levels[level];
                    int pixelCount = checked(levelWidth * levelHeight);
                    int requiredBytes = checked(pixelCount * components);
                    if (levelData == null || levelData.Length < requiredBytes)
                    {
                        _logger?.LogDebug(
                            "Texture mip data is truncated at level {Level}: {ActualBytes}/{RequiredBytes} for {Path}",
                            level,
                            levelData?.Length ?? 0,
                            requiredBytes,
                            path);
                        texture.Dispose();
                        return null;
                    }

                    if (components == 4)
                    {
                        // SurfaceFormat.Color is RGBA8. Avoid the old per-pixel Color[] conversion
                        // and upload the decoded bytes directly.
                        texture.SetData(level, null, levelData, 0, requiredBytes);
                    }
                    else
                    {
                        UploadRgbLevel(texture, level, levelData, pixelCount);
                    }

                    levelWidth = Math.Max(1, levelWidth / 2);
                    levelHeight = Math.Max(1, levelHeight / 2);
                }

                return texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        private static void UploadRgbLevel(Texture2D texture, int level, byte[] data, int pixelCount)
        {
            var pool = System.Buffers.ArrayPool<Color>.Shared;
            Color[] pixels = pool.Rent(pixelCount);
            try
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int dataIndex = i * 3;
                    pixels[i] = new Color(
                        data[dataIndex],
                        data[dataIndex + 1],
                        data[dataIndex + 2],
                        byte.MaxValue);
                }

                texture.SetData(level, null, pixels, 0, pixelCount);
            }
            finally
            {
                pool.Return(pixels);
            }
        }

        private static byte[][] GetUsableMipLevels(TextureData textureInfo)
        {
            if (textureInfo.MipData != null && textureInfo.MipData.Length > 0)
                return textureInfo.MipData;

            return new[] { textureInfo.Data };
        }

        private static bool HasCompleteMipChain(int width, int height, int mipCount)
            => mipCount > 1 && mipCount == GetExpectedMipCount(width, height);

        private static int GetExpectedMipCount(int width, int height)
        {
            int count = 1;
            while (width > 1 || height > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                count++;
            }

            return count;
        }

        private static void PrepareMipData(TextureData textureInfo, string path)
        {
            if (textureInfo == null || textureInfo.IsCompressed ||
                textureInfo.MipData is { Length: > 0 } ||
                !ShouldGenerateMipMaps(path, textureInfo))
            {
                return;
            }

            textureInfo.MipData = GenerateMipChain(
                textureInfo.Data,
                textureInfo.Width,
                textureInfo.Height,
                textureInfo.Components);
            textureInfo.MipDataGenerated = textureInfo.MipData.Length > 1;
        }

        private static bool ShouldGenerateMipMaps(string path, TextureData textureInfo)
        {
            if (!Constants.HIGH_QUALITY_TEXTURES ||
                textureInfo == null || textureInfo.IsCompressed ||
                textureInfo.Width <= 1 && textureInfo.Height <= 1 ||
                textureInfo.Components is not (3 or 4) ||
                textureInfo.Data == null)
            {
                return false;
            }

            string normalized = NormalizePathKey(path);
            if (string.IsNullOrEmpty(normalized))
                return false;

            // UI is rendered in screen space and does not benefit from a mip chain. Excluding
            // it avoids extra CPU memory while keeping world, model and terrain textures filtered.
            return !ContainsPathSegment(normalized, "interface") &&
                   !ContainsPathSegment(normalized, "ui") &&
                   !ContainsPathSegment(normalized, "font") &&
                   !ContainsPathSegment(normalized, "fonts") &&
                   !ContainsPathSegment(normalized, "cursor");
        }

        private static bool ContainsPathSegment(string normalizedPath, string segment)
        {
            if (normalizedPath.StartsWith(segment + "/", StringComparison.Ordinal))
                return true;

            return normalizedPath.Contains("/" + segment + "/", StringComparison.Ordinal);
        }

        private static byte[][] GenerateMipChain(
            byte[] baseData,
            int width,
            int height,
            int components)
        {
            int baseBytes = checked(width * height * components);
            if (baseData == null || baseData.Length < baseBytes)
                return Array.Empty<byte[]>();

            int expectedLevels = GetExpectedMipCount(width, height);
            var levels = new byte[expectedLevels][];
            levels[0] = baseData;

            byte[] source = baseData;
            int sourceWidth = width;
            int sourceHeight = height;

            for (int level = 1; level < expectedLevels; level++)
            {
                int targetWidth = Math.Max(1, sourceWidth / 2);
                int targetHeight = Math.Max(1, sourceHeight / 2);
                byte[] target = new byte[checked(targetWidth * targetHeight * components)];

                DownsampleMip(
                    source,
                    sourceWidth,
                    sourceHeight,
                    target,
                    targetWidth,
                    targetHeight,
                    components);

                levels[level] = target;
                source = target;
                sourceWidth = targetWidth;
                sourceHeight = targetHeight;
            }

            return levels;
        }

        private static void DownsampleMip(
            byte[] source,
            int sourceWidth,
            int sourceHeight,
            byte[] target,
            int targetWidth,
            int targetHeight,
            int components)
        {
            for (int y = 0; y < targetHeight; y++)
            {
                int y0 = Math.Min(sourceHeight - 1, y * 2);
                int y1 = Math.Min(sourceHeight - 1, y0 + 1);

                for (int x = 0; x < targetWidth; x++)
                {
                    int x0 = Math.Min(sourceWidth - 1, x * 2);
                    int x1 = Math.Min(sourceWidth - 1, x0 + 1);

                    int i00 = (y0 * sourceWidth + x0) * components;
                    int i10 = (y0 * sourceWidth + x1) * components;
                    int i01 = (y1 * sourceWidth + x0) * components;
                    int i11 = (y1 * sourceWidth + x1) * components;
                    int dst = (y * targetWidth + x) * components;

                    if (components == 3)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            int sum = source[i00 + channel] + source[i10 + channel] +
                                      source[i01 + channel] + source[i11 + channel];
                            target[dst + channel] = (byte)((sum + 2) / 4);
                        }
                        continue;
                    }

                    int a00 = source[i00 + 3];
                    int a10 = source[i10 + 3];
                    int a01 = source[i01 + 3];
                    int a11 = source[i11 + 3];
                    int alphaSum = a00 + a10 + a01 + a11;

                    target[dst + 3] = (byte)((alphaSum + 2) / 4);
                    if (alphaSum == 0)
                    {
                        target[dst] = 0;
                        target[dst + 1] = 0;
                        target[dst + 2] = 0;
                        continue;
                    }

                    for (int channel = 0; channel < 3; channel++)
                    {
                        int weighted = source[i00 + channel] * a00 +
                                       source[i10 + channel] * a10 +
                                       source[i01 + channel] * a01 +
                                       source[i11 + channel] * a11;
                        target[dst + channel] = (byte)((weighted + alphaSum / 2) / alphaSum);
                    }
                }
            }
        }

        private ClientTexture TouchAndReturn(ClientTexture texture)
        {
            Touch(texture);
            return texture;
        }

        private static void Touch(ClientTexture texture)
        {
            if (texture == null)
                return;

            // Texture lookups happen in draw paths. A system-clock query for every mesh or UI
            // element is unnecessary; refresh the TTL timestamp at most once per second at 60 FPS.
            int frame = MuGame.FrameIndex;
            int previousFrame = Volatile.Read(ref texture.LastAccessFrame);
            if (frame >= previousFrame && frame - previousFrame < 60)
                return;

            if (Interlocked.CompareExchange(ref texture.LastAccessFrame, frame, previousFrame) == previousFrame)
                texture.LastAccessUtc = DateTime.UtcNow;
        }

        private async Task CleanupLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_cleanupInterval, token).ConfigureAwait(false);
                    CleanupStaleTextures();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "TextureLoader cleanup loop error.");
                }
            }
        }

        private void CleanupStaleTextures()
        {
            var cutoff = DateTime.UtcNow - _textureTtl;
            var staleKeys = _textures
                .Where(kvp =>
                    kvp.Value != null &&
                    kvp.Value.LastAccessUtc <= cutoff &&
                    (kvp.Value.Texture == null || kvp.Value.Texture.IsDisposed))
                .Select(kvp => kvp.Key)
                .ToArray();

            if (staleKeys.Length == 0)
            {
                return;
            }

            // Dispose GPU resources on the main thread
            Client.Main.MuGame.ScheduleOnMainThread(() =>
            {
                foreach (var key in staleKeys)
                {
                    if (_textures.TryRemove(key, out var removed))
                    {
                        try
                        {
                            if (removed.Texture != null && !removed.Texture.IsDisposed)
                                removed.Texture.Dispose();
                            removed.Texture = null;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed disposing texture {Path} during cleanup.", key);
                        }

                        _textureTasks.TryRemove(key, out _);
                        _gpuTextureTasks.TryRemove(key, out _);
                        _pathResolutionCache.TryRemove(key, out _); // Clean cache too
                    }
                }
            },
            MainThreadDispatcher.WorkPriority.Low,
            "TextureLoader.CleanupStaleTextures");
        }
    }
}
