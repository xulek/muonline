using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Client.Main;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Data.BMD;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// Utility for generating simple 3D previews of BMD models with proper BlendState support.
    /// </summary>
    public static class BmdPreviewRenderer
    {
        private readonly struct ItemRenderProperties
        {
            public static ItemRenderProperties Default => new(0, false, false, -1, -1);

            public ItemRenderProperties(
                int level,
                bool isExcellent,
                bool isAncient,
                int itemGroup,
                int itemIndex)
            {
                Level = Math.Clamp(level, 0, 15);
                IsExcellent = isExcellent;
                IsAncient = isAncient;
                ItemGroup = itemGroup;
                ItemIndex = itemIndex;
            }

            public int Level { get; }
            public bool IsExcellent { get; }
            public bool IsAncient { get; }
            public int ItemGroup { get; }
            public int ItemIndex { get; }

            public bool RequiresDistinctKey => Level != 0 || IsExcellent || IsAncient;
            public bool ShouldUseItemMaterial => Level >= 7 || IsExcellent || IsAncient;
            public int ItemOptions => (Level & 0x0F) | (IsExcellent ? 0x10 : 0);
        }

        private sealed class PreviewCacheEntry : IDisposable
        {
            public PreviewCacheEntry(RenderTarget2D texture, float lastUpdateTime, bool requiresAnimation)
            {
                Texture = texture;
                LastUpdateTime = lastUpdateTime;
                RequiresAnimation = requiresAnimation;
                LastAccessFrame = MuGame.FrameIndex;
                LastRenderedFrame = MuGame.FrameIndex;
            }

            public RenderTarget2D Texture { get; private set; }
            public float LastUpdateTime { get; set; }
            public bool RequiresAnimation { get; set; }
            public int LastAccessFrame { get; set; }
            public int LastRenderedFrame { get; set; }

            public void UpdateTexture(RenderTarget2D texture)
            {
                if (ReferenceEquals(Texture, texture))
                    return;

                ReturnPreviewTarget(Texture);
                Texture = texture;
            }

            public void Dispose()
            {
                ReturnPreviewTarget(Texture);
                Texture = null;
            }

            public void DisposePermanently()
            {
                Texture?.Dispose();
                Texture = null;
            }
        }

        private sealed class PreviewMeshGeometry
        {
            public DynamicVertexBuffer VertexBuffer;
            public DynamicIndexBuffer IndexBuffer;
            public bool Skip;

            public bool IsValid => Skip ||
                (VertexBuffer != null && !VertexBuffer.IsDisposed &&
                 IndexBuffer != null && !IndexBuffer.IsDisposed);

            public void Release(bool permanent = false)
            {
                if (permanent)
                {
                    VertexBuffer?.Dispose();
                    IndexBuffer?.Dispose();
                }
                else
                {
                    DynamicBufferPool.ReturnVertexBuffer(VertexBuffer);
                    DynamicBufferPool.ReturnIndexBuffer(IndexBuffer);
                }

                VertexBuffer = null;
                IndexBuffer = null;
            }
        }

        private sealed class PreviewPoseGeometry : IDisposable
        {
            public Matrix[] Bones;
            public BoundingBox Bounds;
            public int[] MeshOrder;
            public PreviewMeshGeometry[] Meshes;
            public bool UsesPlayerIdlePose;

            public bool IsValid
            {
                get
                {
                    if (Bones == null || MeshOrder == null || Meshes == null)
                        return false;

                    for (int i = 0; i < Meshes.Length; i++)
                    {
                        if (Meshes[i] == null || !Meshes[i].IsValid)
                            return false;
                    }

                    return true;
                }
            }

            public void Dispose() => Dispose(permanent: false);

            public void Dispose(bool permanent)
            {
                if (Meshes != null)
                {
                    for (int i = 0; i < Meshes.Length; i++)
                        Meshes[i]?.Release(permanent);
                }

                Bones = null;
                MeshOrder = null;
                Meshes = null;
            }
        }

        private sealed class PreviewModelGeometry : IDisposable
        {
            public PreviewPoseGeometry DefaultPose;
            public PreviewPoseGeometry PlayerIdlePose;
            public int LastAccessFrame;

            public void Dispose() => Dispose(permanent: false);

            public void Dispose(bool permanent)
            {
                DefaultPose?.Dispose(permanent);
                PlayerIdlePose?.Dispose(permanent);
                DefaultPose = null;
                PlayerIdlePose = null;
            }
        }

        private sealed class ItemPreviewEffectBindings
        {
            public Effect Effect;
            public EffectTechnique Technique;
            public EffectTechnique FastTechnique;
            public EffectParameter World;
            public EffectParameter WorldViewProjection;
            public EffectParameter EyePosition;
            public EffectParameter DiffuseTexture;
            public EffectParameter Chrome02Texture;
            public EffectParameter Shiny01Texture;
            public EffectParameter Chrome01Texture;
            public EffectParameter ItemOptions;
            public EffectParameter ItemMaterialGroup;
            public EffectParameter ItemMaterialIndex;
            public EffectParameter HighLevelTexturesAvailable;
            public EffectParameter IsExcellent;
            public EffectParameter IsAncient;
            public EffectParameter Time;
            public EffectParameter Alpha;
            public EffectParameter ShadowsEnabled;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static ReferenceComparer<T> Instance { get; } = new();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly struct RenderTargetKey : IEquatable<RenderTargetKey>
        {
            public RenderTargetKey(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }
            public int Height { get; }
            public bool Equals(RenderTargetKey other) => Width == other.Width && Height == other.Height;
            public override bool Equals(object obj) => obj is RenderTargetKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Width, Height);
        }

        private readonly struct PooledRenderTarget
        {
            public PooledRenderTarget(RenderTarget2D target, int returnedFrame)
            {
                Target = target;
                ReturnedFrame = returnedFrame;
            }

            public RenderTarget2D Target { get; }
            public int ReturnedFrame { get; }
        }

        private static readonly Dictionary<string, PreviewCacheEntry> _cache = new();
        private static readonly Dictionary<string, PreviewCacheEntry> _rotatingCache = new();
        private static readonly Dictionary<string, int> _renderFailureRetryFrames = new();
        private static readonly Dictionary<string, BlendState> _previewBlendStateCache = new();
        private static readonly Dictionary<BMD, PreviewModelGeometry> _geometryCache =
            new(ReferenceComparer<BMD>.Instance);
        private static readonly Dictionary<RenderTargetKey, Queue<PooledRenderTarget>> _renderTargetPool = new();

        private static readonly Vector3[] _originalCornersBuffer = new Vector3[8];
        private static readonly Vector3[] _transformedCornersBuffer = new Vector3[8];

        private static ItemPreviewEffectBindings _itemPreviewEffectBindings;
        private static GraphicsDevice _hookedGraphicsDevice;
        private static int _renderBudgetFrame = -1;
        private static int _rendersThisFrame;
        private static int _animatedRendersThisFrame;

        private const int MaxStaticCacheSize = 256;
        private const int MaxPreviewGeometryModels = 192;
        private const int MaxRotatingCacheSize = 64;
        private const long MaxStaticCachePixels = 8L * 1024L * 1024L;
        private const long MaxRotatingCachePixels = 2L * 1024L * 1024L;
        private const int MaxPooledTargetsPerSize = 8;
        private const int RenderTargetReuseDelayFrames = 4;
        private const int MaxPreviewRendersPerFrame = 6;
        private const int MaxAnimatedPreviewRendersPerFrame = 3;
        private const int RenderFailureCooldownFrames = 300;
        private const float AnimatedUpdateInterval = 1f / 23f;

        private static ItemRenderProperties CreateRenderProperties(InventoryItem item)
        {
            if (item == null)
            {
                return ItemRenderProperties.Default;
            }

            var details = item.Details;
            int level = Math.Max(details.Level, item.Level);
            bool isExcellent = details.IsExcellent;
            bool isAncient = details.IsAncient;

            int itemGroup = item.Definition?.Group ?? -1;
            int itemIndex = item.Definition?.Id ?? -1;
            return new ItemRenderProperties(
                level,
                isExcellent,
                isAncient,
                itemGroup,
                itemIndex);
        }

        private static string BuildCacheKey(
            ItemDefinition definition,
            int width,
            int height,
            float rotationAngle,
            in ItemRenderProperties props,
            bool isRotating)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.TexturePath))
                return string.Empty;

            string key = $"{definition.TexturePath}:{width}x{height}";
            if (isRotating)
            {
                // One persistent target per item/size. The previous implementation added the
                // current angle to the key, creating dozens or hundreds of render targets.
                key = $"{key}:rotating";
            }
            else if (rotationAngle != 0f)
            {
                key = $"{key}:angle{rotationAngle:F1}";
            }

            if (props.RequiresDistinctKey)
            {
                key = $"{key}:lvl{props.Level:X2}:ex{(props.IsExcellent ? 1 : 0)}:an{(props.IsAncient ? 1 : 0)}";
                if (props.ItemGroup >= 0 && props.ItemIndex >= 0)
                    key = $"{key}:g{props.ItemGroup}:i{props.ItemIndex}";
            }

            return key;
        }

        private static PreviewCacheEntry GetCacheEntry(string key, bool isRotating)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            var targetCache = isRotating ? _rotatingCache : _cache;
            if (!targetCache.TryGetValue(key, out var entry))
                return null;

            entry.LastAccessFrame = MuGame.FrameIndex;
            return entry;
        }

        private static void StoreCacheEntry(string key, PreviewCacheEntry entry, bool isRotating)
        {
            if (string.IsNullOrEmpty(key) || entry == null)
                return;

            var targetCache = isRotating ? _rotatingCache : _cache;
            if (targetCache.TryGetValue(key, out var existing) && !ReferenceEquals(existing, entry))
                existing.Dispose();

            entry.LastAccessFrame = MuGame.FrameIndex;
            targetCache[key] = entry;
            TrimPreviewCache(
                targetCache,
                isRotating ? MaxRotatingCacheSize : MaxStaticCacheSize,
                isRotating ? MaxRotatingCachePixels : MaxStaticCachePixels);
        }

        private static void TrimPreviewCache(
            Dictionary<string, PreviewCacheEntry> cache,
            int maxEntries,
            long maxPixels)
        {
            long totalPixels = 0;
            foreach (var entry in cache.Values)
            {
                var texture = entry?.Texture;
                if (texture != null && !texture.IsDisposed)
                    totalPixels += (long)texture.Width * texture.Height;
            }

            while (cache.Count > maxEntries || totalPixels > maxPixels)
            {
                string oldestKey = null;
                int oldestFrame = int.MaxValue;

                foreach (var pair in cache)
                {
                    int frame = pair.Value?.LastAccessFrame ?? int.MinValue;
                    if (oldestKey == null || frame < oldestFrame)
                    {
                        oldestKey = pair.Key;
                        oldestFrame = frame;
                    }
                }

                if (oldestKey == null)
                    break;

                if (cache.Remove(oldestKey, out var removed))
                {
                    var texture = removed?.Texture;
                    if (texture != null && !texture.IsDisposed)
                        totalPixels -= (long)texture.Width * texture.Height;
                    removed?.Dispose();
                }
            }
        }

        private static bool TryReserveRenderBudget(bool animated)
        {
            int frame = MuGame.FrameIndex;
            if (_renderBudgetFrame != frame)
            {
                _renderBudgetFrame = frame;
                _rendersThisFrame = 0;
                _animatedRendersThisFrame = 0;
            }

            if (_rendersThisFrame >= MaxPreviewRendersPerFrame)
                return false;

            if (animated && _animatedRendersThisFrame >= MaxAnimatedPreviewRendersPerFrame)
                return false;

            _rendersThisFrame++;
            if (animated)
                _animatedRendersThisFrame++;
            return true;
        }

        private static void ReleaseRenderBudget(bool animated)
        {
            if (_renderBudgetFrame != MuGame.FrameIndex)
                return;

            _rendersThisFrame = Math.Max(0, _rendersThisFrame - 1);
            if (animated)
                _animatedRendersThisFrame = Math.Max(0, _animatedRendersThisFrame - 1);
        }

        private static void EnsureDeviceHooks(GraphicsDevice graphicsDevice)
        {
            if (ReferenceEquals(_hookedGraphicsDevice, graphicsDevice))
                return;

            if (_hookedGraphicsDevice != null)
            {
                _hookedGraphicsDevice.DeviceResetting -= OnGraphicsDeviceResetting;
                _hookedGraphicsDevice.DeviceLost -= OnGraphicsDeviceLost;
            }

            ClearAllCaches(permanent: true);
            _hookedGraphicsDevice = graphicsDevice;

            if (_hookedGraphicsDevice != null)
            {
                _hookedGraphicsDevice.DeviceResetting += OnGraphicsDeviceResetting;
                _hookedGraphicsDevice.DeviceLost += OnGraphicsDeviceLost;
            }
        }

        private static void OnGraphicsDeviceResetting(object sender, EventArgs e)
            => ClearAllCaches(permanent: true);

        private static void OnGraphicsDeviceLost(object sender, EventArgs e)
            => ClearAllCaches(permanent: true);

        public static void ClearCache(bool releaseGpuResources = false)
            => ClearAllCaches(permanent: releaseGpuResources);

        private static void ClearAllCaches(bool permanent)
        {
            foreach (var entry in _cache.Values)
            {
                if (permanent)
                    entry?.DisposePermanently();
                else
                    entry?.Dispose();
            }
            _cache.Clear();

            foreach (var entry in _rotatingCache.Values)
            {
                if (permanent)
                    entry?.DisposePermanently();
                else
                    entry?.Dispose();
            }
            _rotatingCache.Clear();
            _renderFailureRetryFrames.Clear();
            _itemPreviewEffectBindings = null;

            foreach (var model in _geometryCache.Values)
                model?.Dispose(permanent);
            _geometryCache.Clear();

            if (permanent)
            {
                foreach (var queue in _renderTargetPool.Values)
                {
                    while (queue.Count > 0)
                        queue.Dequeue().Target?.Dispose();
                }
                _renderTargetPool.Clear();
            }
        }

        private static RenderTarget2D RentPreviewTarget(GraphicsDevice graphicsDevice, int width, int height)
        {
            var key = new RenderTargetKey(width, height);
            if (_renderTargetPool.TryGetValue(key, out var queue))
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var entry = queue.Dequeue();
                    var target = entry.Target;
                    if (target == null || target.IsDisposed || target.GraphicsDevice != graphicsDevice)
                    {
                        target?.Dispose();
                        continue;
                    }

                    int age = unchecked(MuGame.FrameIndex - entry.ReturnedFrame);
                    if (age >= RenderTargetReuseDelayFrames || age < 0)
                        return target;

                    queue.Enqueue(entry);
                }
            }

            return new RenderTarget2D(
                graphicsDevice,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth24);
        }

        private static void ReturnPreviewTarget(RenderTarget2D target)
        {
            if (target == null || target.IsDisposed)
                return;

            if (_hookedGraphicsDevice == null || target.GraphicsDevice != _hookedGraphicsDevice)
            {
                target.Dispose();
                return;
            }

            var key = new RenderTargetKey(target.Width, target.Height);
            if (!_renderTargetPool.TryGetValue(key, out var queue))
            {
                queue = new Queue<PooledRenderTarget>();
                _renderTargetPool[key] = queue;
            }

            if (queue.Count >= MaxPooledTargetsPerSize)
            {
                target.Dispose();
                return;
            }

            queue.Enqueue(new PooledRenderTarget(target, MuGame.FrameIndex));
        }

        private static ItemPreviewEffectBindings GetItemPreviewEffectBindings()
        {
            var effect = GraphicsManager.Instance.ItemMaterialEffect;
            if (effect == null)
                return null;

            if (_itemPreviewEffectBindings != null &&
                ReferenceEquals(_itemPreviewEffectBindings.Effect, effect) &&
                _itemPreviewEffectBindings.Technique != null)
            {
                return _itemPreviewEffectBindings;
            }

            var technique = FindTechnique(effect, "BasicColorDrawing");
            if (technique == null)
                return null;

            _itemPreviewEffectBindings = new ItemPreviewEffectBindings
            {
                Effect = effect,
                Technique = technique,
                FastTechnique = FindTechnique(effect, "BasicColorDrawing_UpgradeFast"),
                World = effect.Parameters["World"],
                WorldViewProjection = effect.Parameters["WorldViewProjection"],
                EyePosition = effect.Parameters["EyePosition"],
                DiffuseTexture = effect.Parameters["DiffuseTexture"],
                Chrome02Texture = effect.Parameters["Chrome02Texture"],
                Shiny01Texture = effect.Parameters["Shiny01Texture"],
                Chrome01Texture = effect.Parameters["Chrome01Texture"],
                ItemOptions = effect.Parameters["ItemOptions"],
                ItemMaterialGroup = effect.Parameters["ItemMaterialGroup"],
                ItemMaterialIndex = effect.Parameters["ItemMaterialIndex"],
                HighLevelTexturesAvailable = effect.Parameters["HighLevelTexturesAvailable"],
                IsExcellent = effect.Parameters["IsExcellent"],
                IsAncient = effect.Parameters["IsAncient"],
                Time = effect.Parameters["Time"],
                Alpha = effect.Parameters["Alpha"],
                ShadowsEnabled = effect.Parameters["ShadowsEnabled"],
            };

            return _itemPreviewEffectBindings;
        }

        private static PreviewPoseGeometry GetOrCreatePoseGeometry(BMD bmd, ItemDefinition definition)
        {
            if (!_geometryCache.TryGetValue(bmd, out var modelCache))
            {
                modelCache = new PreviewModelGeometry();
                _geometryCache[bmd] = modelCache;
                TrimGeometryCache(bmd);
            }

            modelCache.LastAccessFrame = MuGame.FrameIndex;
            int group = definition?.Group ?? -1;
            bool wantsPlayerPose = group >= 7 && group <= 11 && PlayerIdlePoseProvider.IsLoaded;
            ref PreviewPoseGeometry slot = ref (wantsPlayerPose
                ? ref modelCache.PlayerIdlePose
                : ref modelCache.DefaultPose);

            if (slot != null && slot.IsValid && slot.UsesPlayerIdlePose == wantsPlayerPose)
                return slot;

            slot?.Dispose();
            slot = BuildPoseGeometry(bmd, definition, wantsPlayerPose);
            return slot;
        }

        private static void TrimGeometryCache(BMD protectedModel)
        {
            while (_geometryCache.Count > MaxPreviewGeometryModels)
            {
                BMD oldestModel = null;
                PreviewModelGeometry oldestGeometry = null;
                int oldestFrame = int.MaxValue;

                foreach (var pair in _geometryCache)
                {
                    if (ReferenceEquals(pair.Key, protectedModel))
                        continue;

                    int frame = pair.Value?.LastAccessFrame ?? int.MinValue;
                    if (oldestModel == null || frame < oldestFrame)
                    {
                        oldestModel = pair.Key;
                        oldestGeometry = pair.Value;
                        oldestFrame = frame;
                    }
                }

                if (oldestModel == null)
                    break;

                _geometryCache.Remove(oldestModel);
                oldestGeometry?.Dispose();
            }
        }

        private static PreviewPoseGeometry BuildPoseGeometry(BMD bmd, ItemDefinition definition, bool usesPlayerIdlePose)
        {
            var bones = BuildBoneMatrices(bmd, usesPlayerIdlePose ? definition : null);
            var pose = new PreviewPoseGeometry
            {
                Bones = bones,
                Bounds = ComputeBounds(bmd, bones),
                MeshOrder = BuildMeshRenderOrder(bmd),
                Meshes = new PreviewMeshGeometry[bmd.Meshes.Length],
                UsesPlayerIdlePose = usesPlayerIdlePose,
            };

            for (int meshIndex = 0; meshIndex < bmd.Meshes.Length; meshIndex++)
            {
                DynamicVertexBuffer vertexBuffer = null;
                DynamicIndexBuffer indexBuffer = null;
                BMDLoader.Instance.GetModelBuffers(
                    bmd,
                    meshIndex,
                    Color.White,
                    bones,
                    ref vertexBuffer,
                    ref indexBuffer,
                    skipCache: false);

                pose.Meshes[meshIndex] = new PreviewMeshGeometry
                {
                    VertexBuffer = vertexBuffer,
                    IndexBuffer = indexBuffer,
                    Skip = vertexBuffer == null || indexBuffer == null,
                };
            }

            return pose;
        }

        private static int[] BuildMeshRenderOrder(BMD bmd)
        {
            int meshCount = bmd?.Meshes?.Length ?? 0;
            var result = new int[meshCount];
            int opaqueWrite = 0;
            int transparentWrite = 0;

            for (int i = 0; i < meshCount; i++)
            {
                var mesh = bmd.Meshes[i];
                bool transparent = !string.IsNullOrEmpty(mesh.BlendingMode) &&
                                   !string.Equals(mesh.BlendingMode, "Opaque", StringComparison.OrdinalIgnoreCase);
                if (!transparent)
                    result[opaqueWrite++] = i;
            }

            transparentWrite = opaqueWrite;
            for (int i = 0; i < meshCount; i++)
            {
                var mesh = bmd.Meshes[i];
                bool transparent = !string.IsNullOrEmpty(mesh.BlendingMode) &&
                                   !string.Equals(mesh.BlendingMode, "Opaque", StringComparison.OrdinalIgnoreCase);
                if (transparent)
                    result[transparentWrite++] = i;
            }

            return result;
        }

        private static float ResolveEffectTime(GameTime gameTime)
        {
            if (gameTime != null)
            {
                return (float)gameTime.TotalGameTime.TotalSeconds;
            }

            var muTime = MuGame.Instance?.GameTime;
            if (muTime != null)
            {
                return (float)muTime.TotalGameTime.TotalSeconds;
            }

            return Environment.TickCount * 0.001f;
        }

        public static Texture2D GetPreview(ItemDefinition definition, int width, int height, float rotationAngle = 0f)
        {
            return GetPreviewInternal(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                gameTime: null,
                useCache: true,
                isRotating: false,
                smoothAnimation: false);
        }

        public static Texture2D GetPreview(InventoryItem item, int width, int height, float rotationAngle = 0f)
        {
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                gameTime: null,
                useCache: true,
                isRotating: false,
                smoothAnimation: false);
        }

        public static Texture2D TryGetCachedPreview(ItemDefinition definition, int width, int height, float rotationAngle = 0f)
        {
            var key = BuildCacheKey(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                isRotating: false);
            return GetCacheEntry(key, isRotating: false)?.Texture;
        }

        public static Texture2D TryGetCachedPreview(InventoryItem item, int width, int height, float rotationAngle = 0f)
        {
            var key = BuildCacheKey(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                isRotating: false);
            return GetCacheEntry(key, isRotating: false)?.Texture;
        }

        public static Texture2D GetAnimatedPreview(ItemDefinition definition, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(definition, width, height, 0f);

            float rotationAngle = CalculateCachedRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: false);
        }

        public static Texture2D GetAnimatedPreview(InventoryItem item, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(item, width, height, 0f);

            float rotationAngle = CalculateCachedRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: false);
        }

        public static Texture2D GetSmoothAnimatedPreview(ItemDefinition definition, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(definition, width, height, 0f);

            float rotationAngle = CalculateRawRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: true);
        }

        public static Texture2D GetSmoothAnimatedPreview(InventoryItem item, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(item, width, height, 0f);

            float rotationAngle = CalculateRawRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: true);
        }

        public static Texture2D GetTestRotatingPreview(ItemDefinition definition, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(definition, width, height, 0f);

            float rotationAngle = CalculateCachedRotationAngle(gameTime.TotalGameTime.TotalSeconds, 90f);
            return GetPreviewInternal(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: false);
        }

        public static Texture2D GetTestRotatingPreview(InventoryItem item, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(item, width, height, 0f);

            float rotationAngle = CalculateCachedRotationAngle(gameTime.TotalGameTime.TotalSeconds, 90f);
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: false);
        }

        public static Texture2D GetSmoothRotatingPreview(ItemDefinition definition, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(definition, width, height, 0f);

            float rotationAngle = CalculateRawRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                definition,
                width,
                height,
                rotationAngle,
                ItemRenderProperties.Default,
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: true);
        }

        public static Texture2D GetSmoothRotatingPreview(InventoryItem item, int width, int height, GameTime gameTime)
        {
            if (gameTime == null)
                return GetPreview(item, width, height, 0f);

            float rotationAngle = CalculateRawRotationAngle(gameTime.TotalGameTime.TotalSeconds, 120f);
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                rotationAngle,
                CreateRenderProperties(item),
                gameTime,
                useCache: true,
                isRotating: true,
                smoothAnimation: true);
        }

        public static Texture2D GetMaterialAnimatedPreview(InventoryItem item, int width, int height, GameTime gameTime)
        {
            return GetPreviewInternal(
                item?.Definition,
                width,
                height,
                0f,
                CreateRenderProperties(item),
                gameTime,
                useCache: true,
                isRotating: false,
                smoothAnimation: false);
        }

        private static float CalculateCachedRotationAngle(double totalSeconds, float speedDegreesPerSecond)
        {
            float angle = CalculateRawRotationAngle(totalSeconds, speedDegreesPerSecond);
            return MathF.Round(angle / 5f) * 5f;
        }

        private static float CalculateRawRotationAngle(double totalSeconds, float speedDegreesPerSecond)
        {
            return (float)(totalSeconds * speedDegreesPerSecond) % 360f;
        }

        private static Texture2D GetPreviewInternal(
            ItemDefinition definition,
            int width,
            int height,
            float rotationAngle,
            in ItemRenderProperties props,
            GameTime gameTime,
            bool useCache,
            bool isRotating,
            bool smoothAnimation)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.TexturePath))
                return null;

            // A cache hit must also observe device changes (Android may recreate its
            // GL context when the activity resumes).
            EnsureDeviceHooks(GraphicsManager.Instance.GraphicsDevice);

            string key = BuildCacheKey(definition, width, height, rotationAngle, props, isRotating);
            float now = ResolveEffectTime(gameTime);
            bool requiresAnimation = isRotating ||
                                     (props.ShouldUseItemMaterial && Constants.ENABLE_ITEM_MATERIAL_ANIMATION);

            PreviewCacheEntry entry = useCache ? GetCacheEntry(key, isRotating) : null;
            if (entry?.Texture != null && entry.Texture.IsDisposed)
            {
                var targetCache = isRotating ? _rotatingCache : _cache;
                targetCache.Remove(key);
                entry = null;
            }

            if (entry != null)
                RenderPassProfiler.RecordPreviewCacheHit();
            else
                RenderPassProfiler.RecordPreviewCacheMiss();

            int frame = MuGame.FrameIndex;
            if (_renderFailureRetryFrames.TryGetValue(key, out int retryFrame))
            {
                if (unchecked(frame - retryFrame) < 0)
                    return entry?.Texture;

                _renderFailureRetryFrames.Remove(key);
            }

            if (entry != null)
            {
                if (entry.LastRenderedFrame == frame)
                    return entry.Texture;

                float updateInterval = smoothAnimation ? 0f : AnimatedUpdateInterval;
                if (!requiresAnimation || (updateInterval > 0f && now - entry.LastUpdateTime < updateInterval))
                    return entry.Texture;
            }

            if (!TryReserveRenderBudget(requiresAnimation))
            {
                RenderPassProfiler.RecordPreviewBudgetSkip();
                return entry?.Texture;
            }

            try
            {
                var target = entry?.Texture;
                RenderTarget2D rendered;
                bool didRender;
                var previewStarted = RenderPassProfiler.Start();
                try
                {
                    rendered = Render(
                        definition,
                        width,
                        height,
                        rotationAngle,
                        props,
                        gameTime,
                        target,
                        out didRender);
                }
                finally
                {
                    RenderPassProfiler.AddPreviewRender(previewStarted);
                }

                if (!didRender)
                {
                    ReleaseRenderBudget(requiresAnimation);
                    return entry?.Texture;
                }

                if (rendered == null)
                    return entry?.Texture;

                if (!useCache)
                {
                    return rendered;
                }

                if (entry == null)
                {
                    entry = new PreviewCacheEntry(rendered, now, requiresAnimation);
                    StoreCacheEntry(key, entry, isRotating);
                }
                else
                {
                    entry.UpdateTexture(rendered);
                    entry.LastUpdateTime = now;
                    entry.RequiresAnimation = requiresAnimation;
                    entry.LastRenderedFrame = frame;
                    entry.LastAccessFrame = frame;
                }

                _renderFailureRetryFrames.Remove(key);
                return entry.Texture;
            }
            catch (Exception ex)
            {
                _renderFailureRetryFrames[key] = unchecked(MuGame.FrameIndex + RenderFailureCooldownFrames);
                MuGame.AppLoggerFactory?.CreateLogger("BmdPreviewRenderer").LogWarning(
                    ex, "Item preview failed for {Path}; retrying after {Frames} frames",
                    definition.TexturePath, RenderFailureCooldownFrames);
                return entry?.Texture;
            }
        }

        private static RenderTarget2D Render(
            ItemDefinition def,
            int width,
            int height,
            float rotationAngle,
            in ItemRenderProperties props,
            GameTime gameTime,
            RenderTarget2D target,
            out bool didRender)
        {
            didRender = false;
            RenderTarget2D rt = target;
            bool createdNewTarget = false;
            var gd = GraphicsManager.Instance.GraphicsDevice;
            if (gd == null)
                return target;

            EnsureDeviceHooks(gd);

            RenderTargetBinding[] prevTargets = null;
            BlendState originalBlendState = null;
            DepthStencilState originalDepthStencilState = null;
            RasterizerState originalRasterizerState = null;
            SamplerState originalSamplerState = null;
            Viewport originalViewport = default;
            Rectangle originalScissorRectangle = default;
            bool capturedStates = false;

            try
            {
                var modelTask = BMDLoader.Instance.Prepare(def.TexturePath);
                if (!modelTask.IsCompleted)
                    return target;

                var bmd = modelTask.Result;
                if (bmd == null)
                    throw new InvalidOperationException($"Preview model unavailable: {def.TexturePath}");

                PreviewPoseGeometry pose = GetOrCreatePoseGeometry(bmd, def);
                if (pose == null || !pose.IsValid)
                    return target;

                // A cached BMD can outlive the texture loader's eviction window.
                // Re-prepare evicted textures before drawing, without blocking the UI.
                int drawableMeshes = 0;
                for (int i = 0; i < pose.MeshOrder.Length; i++)
                {
                    int meshIndex = pose.MeshOrder[i];
                    if (pose.Meshes[meshIndex].Skip)
                        continue;
                    string texturePath = BMDLoader.Instance.GetTexturePath(bmd, bmd.Meshes[meshIndex].TexturePath);
                    var textureTask = TextureLoader.Instance.Prepare(texturePath);
                    if (!textureTask.IsCompleted)
                        return target;
                    // Missing optional meshes must not hide the rest of the item.
                    // A decoded texture whose GPU upload failed is a transient error.
                    if (textureTask.GetAwaiter().GetResult() == null)
                        continue;
                    if (TextureLoader.Instance.GetTexture2D(texturePath) == null)
                        throw new InvalidOperationException($"Preview texture unavailable: {texturePath} ({def.TexturePath})");
                    drawableMeshes++;
                }
                if (drawableMeshes == 0)
                    throw new InvalidOperationException($"No drawable preview meshes: {def.TexturePath}");

                if (rt == null || rt.IsDisposed || rt.Width != width || rt.Height != height)
                {
                    rt = RentPreviewTarget(gd, width, height);
                    createdNewTarget = true;
                }

                prevTargets = gd.GetRenderTargets();
                originalBlendState = gd.BlendState;
                originalDepthStencilState = gd.DepthStencilState;
                originalRasterizerState = gd.RasterizerState;
                originalSamplerState = gd.SamplerStates[0];
                originalViewport = gd.Viewport;
                originalScissorRectangle = gd.ScissorRectangle;
                capturedStates = true;

                gd.SetRenderTarget(rt);
                gd.Clear(Color.Transparent);
                gd.BlendState = BlendState.AlphaBlend;
                gd.DepthStencilState = DepthStencilState.Default;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearWrapSamplerState();

                Matrix view = Matrix.CreateLookAt(new Vector3(0, 0, 40f), Vector3.Zero, Vector3.Up);
                Matrix projection = Matrix.CreatePerspectiveFieldOfView(
                    MathHelper.ToRadians(30f),
                    (float)width / height,
                    1f,
                    100f);

                Matrix baseRotation = ItemOrientationHelper.GetInventoryBaseRotation(def);
                Matrix mouseRotation = Matrix.CreateRotationY(MathHelper.ToRadians(rotationAngle));

                BoundingBox originalBounds = pose.Bounds;
                originalBounds.GetCorners(_originalCornersBuffer);
                Vector3 rotatedMin = new(float.MaxValue);
                Vector3 rotatedMax = new(float.MinValue);

                for (int i = 0; i < _originalCornersBuffer.Length; i++)
                {
                    Vector3 rotatedCorner = Vector3.Transform(_originalCornersBuffer[i], baseRotation);
                    rotatedMin = Vector3.Min(rotatedMin, rotatedCorner);
                    rotatedMax = Vector3.Max(rotatedMax, rotatedCorner);
                }

                Vector3 rotatedSize = rotatedMax - rotatedMin;
                float largestDimension = Math.Max(rotatedSize.X, Math.Max(rotatedSize.Y, rotatedSize.Z));
                float scale = largestDimension > 0.0001f ? 15f / largestDimension : 1f;
                Vector3 originalCenter = (originalBounds.Min + originalBounds.Max) * 0.5f;
                Matrix finalRotation = rotationAngle != 0f ? baseRotation * mouseRotation : baseRotation;
                Matrix worldBase = Matrix.CreateScale(scale) *
                                   finalRotation *
                                   Matrix.CreateTranslation(-originalCenter * scale);

                for (int i = 0; i < _originalCornersBuffer.Length; i++)
                    _transformedCornersBuffer[i] = Vector3.Transform(_originalCornersBuffer[i], worldBase);

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                for (int i = 0; i < _transformedCornersBuffer.Length; i++)
                {
                    Vector3 corner = _transformedCornersBuffer[i];
                    minX = Math.Min(minX, corner.X);
                    maxX = Math.Max(maxX, corner.X);
                    minY = Math.Min(minY, corner.Y);
                    maxY = Math.Max(maxY, corner.Y);
                    minZ = Math.Min(minZ, corner.Z);
                    maxZ = Math.Max(maxZ, corner.Z);
                }

                Matrix world = worldBase * Matrix.CreateTranslation(
                    -(minX + maxX) * 0.5f,
                    -(minY + maxY) * 0.5f,
                    -(minZ + maxZ) * 0.5f);
                Matrix worldViewProjection = world * view * projection;
                Vector3 eyePosition = new(0f, 0f, 40f);

                bool useItemMaterial = props.ShouldUseItemMaterial &&
                                       Constants.ENABLE_ITEM_MATERIAL_SHADER &&
                                       GraphicsManager.Instance.ItemMaterialEffect != null;

                if (!useItemMaterial)
                {
                    var effect = GraphicsManager.Instance.BasicEffect3D;
                    Matrix oldV = effect.View;
                    Matrix oldP = effect.Projection;
                    Matrix oldW = effect.World;
                    float oldAlpha = effect.Alpha;
                    Vector3 oldDiffuse = effect.DiffuseColor;
                    bool oldTextureEnabled = effect.TextureEnabled;
                    bool oldVertexColorEnabled = effect.VertexColorEnabled;
                    bool oldLightingEnabled = effect.LightingEnabled;
                    bool oldFogEnabled = effect.FogEnabled;
                    Texture2D oldTexture = effect.Texture;

                    try
                    {
                        // World effects share this BasicEffect. Their alpha, tint and
                        // fog must never become part of a cached inventory thumbnail.
                        effect.Alpha = 1f;
                        effect.DiffuseColor = Vector3.One;
                        effect.TextureEnabled = true;
                        effect.VertexColorEnabled = true;
                        effect.LightingEnabled = false;
                        effect.FogEnabled = false;
                        effect.View = view;
                        effect.Projection = projection;
                        effect.World = world;

                        for (int i = 0; i < pose.MeshOrder.Length; i++)
                        {
                            int meshIndex = pose.MeshOrder[i];
                            RenderMeshWithBlendState(
                                gd,
                                effect,
                                bmd,
                                meshIndex,
                                pose.Meshes[meshIndex]);
                        }
                    }
                    finally
                    {
                        effect.View = oldV;
                        effect.Projection = oldP;
                        effect.World = oldW;
                        effect.Alpha = oldAlpha;
                        effect.DiffuseColor = oldDiffuse;
                        effect.TextureEnabled = oldTextureEnabled;
                        effect.VertexColorEnabled = oldVertexColorEnabled;
                        effect.LightingEnabled = oldLightingEnabled;
                        effect.FogEnabled = oldFogEnabled;
                        effect.Texture = oldTexture;
                    }
                }
                else
                {
                    float shaderTime = ResolveEffectTime(gameTime);
                    ItemPreviewEffectBindings previewBindings = GetItemPreviewEffectBindings();
                    previewBindings?.ShadowsEnabled?.SetValue(0f);
                    try
                    {
                        for (int i = 0; i < pose.MeshOrder.Length; i++)
                        {
                            int meshIndex = pose.MeshOrder[i];
                            RenderMeshWithItemMaterialPreview(
                                gd,
                                bmd,
                                meshIndex,
                                pose.Meshes[meshIndex],
                                world,
                                worldViewProjection,
                                eyePosition,
                                props,
                                shaderTime);
                        }
                    }
                    finally
                    {
                        // ItemMaterialEffect is shared with the world renderer. Restore its
                        // shadow bindings once after the whole preview instead of per mesh.
                        GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(
                            previewBindings?.Effect,
                            force: true);
                    }
                }

                didRender = true;
                return rt;
            }
            catch
            {
                if (createdNewTarget && rt != null && !ReferenceEquals(rt, target))
                {
                    ReturnPreviewTarget(rt);
                    rt = null;
                }

                throw;
            }
            finally
            {
                if (capturedStates)
                {
                    if (prevTargets != null && prevTargets.Length > 0)
                        gd.SetRenderTargets(prevTargets);
                    else
                        gd.SetRenderTarget(null);

                    gd.Viewport = originalViewport;
                    gd.ScissorRectangle = originalScissorRectangle;
                    gd.BlendState = originalBlendState;
                    gd.DepthStencilState = originalDepthStencilState;
                    gd.RasterizerState = originalRasterizerState;
                    gd.SamplerStates[0] = originalSamplerState;
                }
            }
        }

        private static void RenderMeshWithBlendState(
            GraphicsDevice gd,
            BasicEffect effect,
            BMD bmd,
            int meshIndex,
            PreviewMeshGeometry geometry)
        {
            if (geometry == null || geometry.Skip || !geometry.IsValid)
                return;

            var mesh = bmd.Meshes[meshIndex];
            var currentBlendState = gd.BlendState;
            var currentRasterizerState = gd.RasterizerState;

            try
            {
                BlendState customBlendState = GetBlendStateForMesh(mesh);
                if (customBlendState != null)
                    gd.BlendState = customBlendState;

                if (customBlendState != null && customBlendState != BlendState.Opaque)
                    gd.RasterizerState = RasterizerState.CullNone;

                effect.Texture = TextureLoader.Instance.GetTexture2D(
                    BMDLoader.Instance.GetTexturePath(bmd, mesh.TexturePath));
                if (effect.Texture == null)
                    return;

                gd.SetVertexBuffer(geometry.VertexBuffer);
                gd.Indices = geometry.IndexBuffer;

                int primitiveCount = geometry.IndexBuffer.IndexCount / 3;
                effect.CurrentTechnique.Passes[0].Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            }
            finally
            {
                gd.BlendState = currentBlendState;
                gd.RasterizerState = currentRasterizerState;
            }
        }

        private static bool CanUseFastItemPreviewShader(
            ItemPreviewEffectBindings bindings,
            in ItemRenderProperties props)
        {
            if (!Constants.ENABLE_FAST_MATERIAL_SHADERS ||
                bindings?.FastTechnique == null ||
                props.Level < 7 ||
                props.IsExcellent ||
                props.IsAncient)
            {
                return false;
            }

            bool isArmorPart = props.ItemGroup >= 7 && props.ItemGroup <= 11;
            bool usesHighLevelArmor = isArmorPart &&
                                      props.Level >= 11 &&
                                      GraphicsManager.Instance.HasItemUpgradeTextures;
            return !usesHighLevelArmor;
        }

        private static void RenderMeshWithItemMaterialPreview(GraphicsDevice gd,
                                                              BMD bmd,
                                                              int meshIdx,
                                                              PreviewMeshGeometry geometry,
                                                              Matrix world,
                                                              Matrix worldViewProjection,
                                                              Vector3 eyePosition,
                                                              in ItemRenderProperties props,
                                                              float shaderTime)
        {
            var bindings = GetItemPreviewEffectBindings();
            if (bindings == null || geometry == null || geometry.Skip || !geometry.IsValid)
                return;

            var effect = bindings.Effect;
            var mesh = bmd.Meshes[meshIdx];

            var currentBlendState = gd.BlendState;
            var currentRasterizerState = gd.RasterizerState;
            var previousTechnique = effect.CurrentTechnique;

            try
            {
                // Item previews use CPU-skinned VertexPositionColorNormalTexture buffers.
                // Select the compact ordinary-upgrade shader only when no Ancient, Excellent
                // or high-level armor material data is required.
                bool fastShader = CanUseFastItemPreviewShader(bindings, props);
                effect.CurrentTechnique = fastShader ? bindings.FastTechnique : bindings.Technique;
                BlendState customBlendState = GetBlendStateForMesh(mesh);
                if (customBlendState != null)
                {
                    gd.BlendState = customBlendState;
                }

                bool isTwoSided = customBlendState != null && customBlendState != BlendState.Opaque;
                if (isTwoSided)
                {
                    gd.RasterizerState = RasterizerState.CullNone;
                }

                var texturePath = BMDLoader.Instance.GetTexturePath(bmd, mesh.TexturePath);
                if (string.IsNullOrEmpty(texturePath))
                {
                    return;
                }

                var texture = TextureLoader.Instance.GetTexture2D(texturePath);
                if (texture == null)
                {
                    return;
                }

                bindings.World?.SetValue(world);
                bindings.WorldViewProjection?.SetValue(worldViewProjection);
                bindings.DiffuseTexture?.SetValue(texture);

                GraphicsManager graphics = GraphicsManager.Instance;
                if (!fastShader)
                {
                    bindings.EyePosition?.SetValue(eyePosition);
                    Texture2D fallback = graphics.BlackPixel ?? graphics.Pixel;
                    bindings.Chrome02Texture?.SetValue(graphics.ItemChrome02Texture ?? fallback);
                    bindings.Shiny01Texture?.SetValue(graphics.ItemShiny01Texture ?? fallback);
                    bindings.Chrome01Texture?.SetValue(graphics.ItemChrome01Texture ?? fallback);
                    bindings.ItemMaterialGroup?.SetValue(props.ItemGroup);
                    bindings.ItemMaterialIndex?.SetValue(props.ItemIndex);
                    bindings.HighLevelTexturesAvailable?.SetValue(
                        graphics.HasItemUpgradeTextures ? 1f : 0f);
                    bindings.IsExcellent?.SetValue(props.IsExcellent);
                    bindings.IsAncient?.SetValue(props.IsAncient);
                }
                bindings.ItemOptions?.SetValue(props.ItemOptions);
                bindings.Time?.SetValue(shaderTime);
                bindings.Alpha?.SetValue(1f);

                gd.SetVertexBuffer(geometry.VertexBuffer);
                gd.Indices = geometry.IndexBuffer;

                int primitiveCount = geometry.IndexBuffer.IndexCount / 3;
                effect.CurrentTechnique.Passes[0].Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            }
            finally
            {
                effect.CurrentTechnique = previousTechnique;
                gd.BlendState = currentBlendState;
                gd.RasterizerState = currentRasterizerState;
            }
        }

        private static EffectTechnique FindTechnique(Effect effect, string techniqueName)
        {
            if (effect == null || string.IsNullOrEmpty(techniqueName))
            {
                return null;
            }

            foreach (var technique in effect.Techniques)
            {
                if (string.Equals(technique.Name, techniqueName, StringComparison.Ordinal))
                {
                    return technique;
                }
            }

            return null;
        }

        private static BlendState GetBlendStateForMesh(Client.Data.BMD.BMDTextureMesh mesh)
        {
            if (string.IsNullOrEmpty(mesh.BlendingMode))
                return null;

            // Check cache first
            if (_previewBlendStateCache.TryGetValue(mesh.BlendingMode, out var cachedState))
                return cachedState;

            // Use reflection to get BlendState from Blendings class
            try
            {
                var field = typeof(Blendings).GetField(mesh.BlendingMode,
                                                      BindingFlags.Public | BindingFlags.Static);
                if (field != null && field.FieldType == typeof(BlendState))
                {
                    var blendState = (BlendState)field.GetValue(null);
                    _previewBlendStateCache[mesh.BlendingMode] = blendState;
                    return blendState;
                }
            }
            catch (Exception)
            {
                // If reflection fails, cache null to avoid repeated attempts
                _previewBlendStateCache[mesh.BlendingMode] = null;
            }

            return null;
        }

        private static Matrix[] BuildBoneMatrices(Client.Data.BMD.BMD bmd, ItemDefinition definition = null)
        {
            var bones = bmd.Bones;
            var result = new Matrix[bones.Length];

            int group = definition?.Group ?? -1;
            bool isArmor = group >= 7 && group <= 11;
            var playerBones = PlayerIdlePoseProvider.GetIdleBoneMatrices();

            if (isArmor && playerBones != null && playerBones.Length > 0)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (i < playerBones.Length)
                    {
                        result[i] = playerBones[i];
                    }
                    else
                    {
                        result[i] = BuildSingleBoneMatrix(bones[i], result);
                    }
                }
            }
            else
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    result[i] = BuildSingleBoneMatrix(bones[i], result);
                }
            }

            return result;
        }

        private static Matrix BuildSingleBoneMatrix(Client.Data.BMD.BMDTextureBone bone, Matrix[] parentResults)
        {
            Matrix local = Matrix.Identity;

            if (bone?.Matrixes != null && bone.Matrixes.Length > 0)
            {
                var bm = bone.Matrixes[0];
                if (bm.Position?.Length > 0 && bm.Quaternion?.Length > 0)
                {
                    var q = bm.Quaternion[0];
                    local = Matrix.CreateFromQuaternion(new Microsoft.Xna.Framework.Quaternion(q.X, q.Y, q.Z, q.W));
                    var p = bm.Position[0];
                    local.Translation = new Vector3(p.X, p.Y, p.Z);
                }
            }

            if (bone != null && bone.Parent >= 0 && bone.Parent < parentResults.Length)
                return local * parentResults[bone.Parent];

            return local;
        }

        private static BoundingBox ComputeBounds(Client.Data.BMD.BMD bmd, Matrix[] bones)
        {
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);
            foreach (var mesh in bmd.Meshes)
            {
                foreach (var vert in mesh.Vertices)
                {
                    Matrix m = vert.Node < bones.Length ? bones[vert.Node] : Matrix.Identity;
                    Vector3 pos = Vector3.Transform(new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z), m);
                    min = Vector3.Min(min, pos);
                    max = Vector3.Max(max, pos);
                }
            }
            return new BoundingBox(min, max);
        }
    }

    internal static class ItemOrientationHelper
    {
        private const float MinAxisLengthSq = 1e-6f;

        public static Matrix GetInventoryBaseRotation(ItemDefinition definition)
        {
            if (definition == null)
            {
                return MuRotationConverter.ConvertToMonoGame(25f, 45f, 0f);
            }

            short group = (short)definition.Group;
            if (group == 6)
            {
                return MuRotationConverter.ConvertToMonoGame(270f, 270f, 0f);
            }

            if (group == 13 || group == 14)
            {
                return MuRotationConverter.ConvertToMonoGame(270f, 0f, 0f);
            }

            if (group == 0 || group == 1 || group == 2 || group == 3 || group == 5)
            {
                return MuRotationConverter.ConvertToMonoGame(25f, 45f, 0f);
            }

            return MuRotationConverter.ConvertToMonoGame(270f, -10f, 0f);
        }

        public static Quaternion GetInventoryOrientation(ItemDefinition definition)
        {
            Matrix rotation = GetInventoryBaseRotation(definition);
            return Quaternion.CreateFromRotationMatrix(rotation);
        }

        public static Vector3 GetWorldDropEuler(ItemDefinition definition)
        {
            // Get the same MU rotation values used in inventory
            (float muX, float muY, float muZ) = GetMuRotationValues(definition);

            // Apply EXACTLY the same conversion as MuRotationConverter.ConvertToMonoGame
            bool hasLargeAngle = (MathF.Abs(muX) >= 180f || MathF.Abs(muY) >= 180f || MathF.Abs(muZ) >= 180f);

            float monoX = muX;
            float monoY = -muY;
            float monoZ = hasLargeAngle ? muZ : muZ + 180f;

            // Convert to radians (ModelObject.Angle expects radians)
            Vector3 inventoryAngle = new Vector3(
                MathHelper.ToRadians(monoX),
                MathHelper.ToRadians(monoY),
                MathHelper.ToRadians(monoZ)
            );

            // Apply camera space transformation offset
            // This rotates from inventory camera view to world space isometric view
            inventoryAngle.X += -MathHelper.PiOver2; // -90° pitch
            inventoryAngle.Y += MathHelper.PiOver2;  // +90° yaw

            return inventoryAngle;
        }

        /// <summary>
        /// Returns the MU Online rotation values for each item group
        /// These are the same values used in GetInventoryBaseRotation
        /// </summary>
        private static (float muX, float muY, float muZ) GetMuRotationValues(ItemDefinition definition)
        {
            if (definition == null)
            {
                return (25f, 45f, 0f); // Default weapons
            }

            short group = (short)definition.Group;

            // Shields
            if (group == 6)
            {
                return (270f, 270f, 0f);
            }

            // Wings
            if (group == 13 || group == 14)
            {
                return (270f, 0f, 0f);
            }

            // Weapons (swords, axes, maces, spears, bows)
            if (group == 0 || group == 1 || group == 2 || group == 3 || group == 5)
            {
                return (25f, 45f, 0f);
            }

            // Default for other groups
            return (270f, -10f, 0f);
        }

        private static float NormalizeAngle(float angle)
        {
            const float twoPi = MathF.PI * 2f;
            angle %= twoPi;
            if (angle <= -MathF.PI)
            {
                angle += twoPi;
            }
            else if (angle > MathF.PI)
            {
                angle -= twoPi;
            }
            return angle;
        }
    }

    /// <summary>
    /// Smart MU Online to MonoGame rotation converter - Final Version
    /// Based on discovered patterns: Small angles need Z+180°, Large angles don't
    /// </summary>
    public static class MuRotationConverter
    {
        /// <summary>
        /// Converts MU Online rotation to MonoGame with automatic correction detection
        /// </summary>
        /// <param name="muX">MU Online X rotation in degrees</param>
        /// <param name="muY">MU Online Y rotation in degrees</param>
        /// <param name="muZ">MU Online Z rotation in degrees</param>
        /// <returns>MonoGame rotation matrix</returns>
        public static Matrix ConvertToMonoGame(float muX, float muY, float muZ)
        {
            // Rule: If any angle >= 180°, don't add Z flip. Otherwise, add Z+180°
            bool hasLargeAngle = (Math.Abs(muX) >= 180f || Math.Abs(muY) >= 180f || Math.Abs(muZ) >= 180f);

            float monoX = muX;
            float monoY = -muY;
            float monoZ = hasLargeAngle ? muZ : muZ + 180f;

            var m = CreateRotationMatrix(monoX, monoY, monoZ);

            if (muX >= 180f && muY >= 180f && Math.Abs(muZ) < 1f)
                m *= Matrix.CreateRotationY(MathHelper.Pi);

            return m;
        }

        /// <summary>
        /// Converts with explicit control over corrections
        /// </summary>
        public static Matrix ConvertToMonoGame(float muX, float muY, float muZ,
                                             float xCorrection = 0f,
                                             float yCorrection = 0f,
                                             float zCorrection = float.NaN) // NaN = auto-detect
        {
            float monoX = muX + xCorrection;
            float monoY = -muY + yCorrection;

            float monoZ;
            if (float.IsNaN(zCorrection))
            {
                // Auto-detect Z correction
                bool hasLargeAngle = (Math.Abs(muX) >= 180f || Math.Abs(muY) >= 180f || Math.Abs(muZ) >= 180f);
                monoZ = hasLargeAngle ? muZ : muZ + 180f;
            }
            else
            {
                monoZ = muZ + zCorrection;
            }

            return CreateRotationMatrix(monoX, monoY, monoZ);
        }

        /// <summary>
        /// Creates rotation matrix in correct order
        /// </summary>
        private static Matrix CreateRotationMatrix(float x, float y, float z)
        {
            return Matrix.CreateRotationX(MathHelper.ToRadians(x)) *
                   Matrix.CreateRotationY(MathHelper.ToRadians(y)) *
                   Matrix.CreateRotationZ(MathHelper.ToRadians(z));
        }

        /// <summary>
        /// Predefined rotations for verified items
        /// </summary>
        public static class Presets
        {
            /// <summary>
            /// Default MU rotation (0, 0, 0) → MonoGame (0°, 0°, 180°)
            /// </summary>
            public static Matrix Default => ConvertToMonoGame(0f, 0f, 0f);

            /// <summary>
            /// Small Axe (25, 45, 0) → MonoGame (25°, -45°, 180°) ✅ VERIFIED
            /// </summary>
            public static Matrix SmallAxe => ConvertToMonoGame(25f, 45f, 0f);

            /// <summary>
            /// Shield (270, 270, 0) → MonoGame (270°, -270°, 0°) ✅ VERIFIED
            /// </summary>
            public static Matrix Shield => ConvertToMonoGame(270f, 270f, 0f);

            /// <summary>
            /// Common sword rotation (0, 90, 0) → MonoGame (0°, -90°, 180°)
            /// </summary>
            public static Matrix Sword => ConvertToMonoGame(0f, 90f, 0f);
        }

        /// <summary>
        /// Debug method to show conversion logic
        /// </summary>
        public static void DebugConversion(float muX, float muY, float muZ)
        {
            bool hasLargeAngle = (Math.Abs(muX) >= 180f || Math.Abs(muY) >= 180f || Math.Abs(muZ) >= 180f);

            float monoX = muX;
            float monoY = -muY;
            float monoZ = hasLargeAngle ? muZ : muZ + 180f;

            Console.WriteLine($"MU Rotation: ({muX:F0}°, {muY:F0}°, {muZ:F0}°)");
            Console.WriteLine($"Has large angle (≥180°): {hasLargeAngle}");
            Console.WriteLine($"MonoGame: ({monoX:F0}°, {monoY:F0}°, {monoZ:F0}°)");

            if (hasLargeAngle)
                Console.WriteLine("  → No Z correction applied (large angle rule)");
            else
                Console.WriteLine("  → Z+180° correction applied (small angle rule)");
        }

        /// <summary>
        /// Verify known working combinations
        /// </summary>
        public static void VerifyKnownRotations()
        {
            Console.WriteLine("=== VERIFYING KNOWN WORKING ROTATIONS ===");

            Console.WriteLine("\n--- Small Axe (25, 45, 0) ---");
            DebugConversion(25f, 45f, 0f);
            Console.WriteLine("Expected: (25°, -45°, 180°) ✅");

            Console.WriteLine("\n--- Shield (270, 270, 0) ---");
            DebugConversion(270f, 270f, 0f);
            Console.WriteLine("Expected: (270°, -270°, 0°) ✅");

            Console.WriteLine("\n--- Default (0, 0, 0) ---");
            DebugConversion(0f, 0f, 0f);
            Console.WriteLine("Expected: (0°, 0°, 180°)");

            Console.WriteLine("\n--- Sword example (0, 90, 0) ---");
            DebugConversion(0f, 90f, 0f);
            Console.WriteLine("Expected: (0°, -90°, 180°)");
        }
    }

    /// <summary>
    /// Extension methods for easier usage
    /// </summary>
    public static class MuRotationExtensions
    {
        /// <summary>
        /// Convert Vector3 MU rotation to MonoGame matrix
        /// </summary>
        public static Matrix ToMonoGameRotation(this Vector3 muRotation)
        {
            return MuRotationConverter.ConvertToMonoGame(muRotation.X, muRotation.Y, muRotation.Z);
        }

        /// <summary>
        /// Create MU rotation vector
        /// </summary>
        public static Vector3 MuRotation(float x, float y, float z) => new Vector3(x, y, z);
    }

    /// <summary>
    /// Item-specific rotation configurations
    /// </summary>
    public class ItemRotationConfig
    {
        public float MuX { get; set; }
        public float MuY { get; set; }
        public float MuZ { get; set; }
        public string ItemName { get; set; } = "";

        /// <summary>
        /// Convert this config to MonoGame rotation matrix
        /// </summary>
        public Matrix ToMonoGameMatrix()
        {
            return MuRotationConverter.ConvertToMonoGame(MuX, MuY, MuZ);
        }

        /// <summary>
        /// Debug this rotation
        /// </summary>
        public void Debug()
        {
            Console.WriteLine($"--- {ItemName} ({MuX}, {MuY}, {MuZ}) ---");
            MuRotationConverter.DebugConversion(MuX, MuY, MuZ);
        }
    }
}
