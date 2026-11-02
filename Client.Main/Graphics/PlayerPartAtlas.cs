using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Shared texture atlas for merged player meshes. Every texture gets a power-of-two cell aligned to its
    /// size (buddy allocation), so the box-filtered mip chain of the atlas never mixes different textures.
    /// Textures are requested while drawing and copied into the atlas at the start of the next frame,
    /// outside of the scene render target.
    /// </summary>
    internal static class PlayerPartAtlas
    {
        public const int PageSize = 2048;
        private const int MaxPages = 4;
        private const int MinCell = 16;
        private const int MaxTextureSize = 1024;

        private sealed class Page
        {
            public RenderTarget2D Target;
            // Free cells per level: level 0 = whole page, level n = PageSize >> n.
            public readonly List<Point>[] FreeCells = CreateFreeLists();
        }

        private readonly struct Entry
        {
            public Entry(int page, Vector4 rect) { Page = page; Rect = rect; }
            public int Page { get; }
            public Vector4 Rect { get; }
        }

        private static readonly List<Page> Pages = new();
        private static readonly Dictionary<Texture2D, Entry> Entries = new(ReferenceEqualityComparer.Instance);
        private static readonly HashSet<Texture2D> Pending = new(ReferenceEqualityComparer.Instance);
        private static readonly HashSet<Texture2D> Rejected = new(ReferenceEqualityComparer.Instance);
        private static SpriteBatch _spriteBatch;

        private static List<Point>[] CreateFreeLists()
        {
            int levels = 0;
            for (int size = PageSize; size >= MinCell; size >>= 1)
                levels++;
            var lists = new List<Point>[levels];
            for (int i = 0; i < levels; i++)
                lists[i] = new List<Point>();
            lists[0].Add(Point.Zero);
            return lists;
        }

        /// <summary>
        /// Returns the atlas page and tile of a texture, or requests it for the next frame. Textures larger than
        /// 1024 texels or rejected earlier never become available (the caller draws normally).
        /// </summary>
        public static bool TryGet(Texture2D texture, out int page, out Vector4 rect)
        {
            page = -1;
            rect = default;
            if (texture == null || texture.IsDisposed || Rejected.Contains(texture))
                return false;

            if (Entries.TryGetValue(texture, out Entry entry) &&
                entry.Page < Pages.Count && Pages[entry.Page].Target is { IsDisposed: false })
            {
                page = entry.Page;
                rect = entry.Rect;
                return true;
            }

            if (texture.Width > MaxTextureSize || texture.Height > MaxTextureSize)
            {
                Rejected.Add(texture);
                return false;
            }

            Pending.Add(texture);
            return false;
        }

        public static Texture2D GetPageTexture(int page) =>
            (uint)page < (uint)Pages.Count ? Pages[page].Target : null;

        /// <summary>Copies requested textures into the atlas; call outside of any scene render target.</summary>
        public static void ProcessPending(GraphicsDevice gd)
        {
            if (Pending.Count == 0 || gd == null)
                return;

            var previousTargets = gd.GetRenderTargets();
            var previousViewport = gd.Viewport;
            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;
            var previousSampler = gd.SamplerStates[0];
            try
            {
                _spriteBatch ??= new SpriteBatch(gd);
                var byPage = new Dictionary<int, List<(Texture2D Texture, Point Cell)>>();
                foreach (Texture2D texture in Pending)
                {
                    if (texture == null || texture.IsDisposed || Entries.ContainsKey(texture))
                        continue;

                    int cellSize = Math.Max(MinCell, NextPowerOfTwo(Math.Max(texture.Width, texture.Height)));
                    if (!TryAllocate(gd, cellSize, out int page, out Point cell))
                    {
                        Rejected.Add(texture);
                        continue;
                    }

                    var rect = new Vector4(
                        cell.X / (float)PageSize,
                        cell.Y / (float)PageSize,
                        texture.Width / (float)PageSize,
                        texture.Height / (float)PageSize);
                    Entries[texture] = new Entry(page, rect);
                    if (!byPage.TryGetValue(page, out var list))
                        byPage[page] = list = new List<(Texture2D, Point)>();
                    list.Add((texture, cell));
                }

                foreach (var (page, list) in byPage)
                {
                    gd.SetRenderTarget(Pages[page].Target);
                    _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
                        DepthStencilState.None, RasterizerState.CullNone);
                    foreach (var (texture, cell) in list)
                        _spriteBatch.Draw(texture, new Rectangle(cell.X, cell.Y, texture.Width, texture.Height), Color.White);
                    _spriteBatch.End();
                }
            }
            finally
            {
                Pending.Clear();
                if (previousTargets != null && previousTargets.Length > 0)
                    gd.SetRenderTargets(previousTargets);
                else
                    gd.SetRenderTarget(null);
                gd.Viewport = previousViewport;
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
            }
        }

        private static bool TryAllocate(GraphicsDevice gd, int cellSize, out int page, out Point cell)
        {
            int level = 0;
            for (int size = PageSize; size > cellSize; size >>= 1)
                level++;

            for (page = 0; page < Pages.Count; page++)
            {
                if (TryAllocateInPage(Pages[page], level, out cell))
                    return true;
            }

            if (Pages.Count < MaxPages)
            {
                var newPage = new Page
                {
                    // Mip maps are generated when the target is unbound; contents are preserved between updates.
                    Target = new RenderTarget2D(gd, PageSize, PageSize, true, SurfaceFormat.Color,
                        DepthFormat.None, 0, RenderTargetUsage.PreserveContents)
                };
                Pages.Add(newPage);
                page = Pages.Count - 1;
                if (TryAllocateInPage(newPage, level, out cell))
                    return true;
            }

            page = -1;
            cell = Point.Zero;
            return false;
        }

        private static bool TryAllocateInPage(Page page, int level, out Point cell)
        {
            cell = Point.Zero;
            int source = level;
            while (source >= 0 && page.FreeCells[source].Count == 0)
                source--;
            if (source < 0)
                return false;

            var list = page.FreeCells[source];
            Point origin = list[^1];
            list.RemoveAt(list.Count - 1);
            // Split down to the requested level; keep the other three quadrants free.
            for (int l = source; l < level; l++)
            {
                int half = PageSize >> (l + 1);
                page.FreeCells[l + 1].Add(new Point(origin.X + half, origin.Y));
                page.FreeCells[l + 1].Add(new Point(origin.X, origin.Y + half));
                page.FreeCells[l + 1].Add(new Point(origin.X + half, origin.Y + half));
            }

            cell = origin;
            return true;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result <<= 1;
            return result;
        }
    }
}
