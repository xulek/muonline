#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects.Cloth
{
    /// <summary>Collision sphere of a cloth: AddCollisionSphere(x, y, z, radius, bone), centre in bone space.</summary>
    public readonly record struct ClothCollisionSphere(Vector3 Center, float Radius, int Bone);

    /// <summary>
    /// Arguments of one CPhysicsCloth::Create call plus its AddCollisionSphere / SetWindMinMax calls.
    /// Texture paths are the files behind iTexFront / iTexBack.
    /// </summary>
    public sealed class ClothDefinition
    {
        public int Bone { get; init; }
        public Vector3 Offset { get; init; }
        public int NumHor { get; init; }
        public int NumVer { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public PhysicsClothType Type { get; init; }
        public string FrontTexture { get; init; } = string.Empty;
        public string? BackTexture { get; init; }
        public ClothCollisionSphere[] Spheres { get; init; } = Array.Empty<ClothCollisionSphere>();
        public byte WindMin { get; init; } = 1;
        public byte WindMax { get; init; } = 1;
    }

    /// <summary>
    /// The m_pCloth array of one object: creates, moves and renders its cloths as RenderCharacter does
    /// (ZzzCharacter.cpp:8630-8648 monsters, 9734-9795 players): Create on first use, Move2 every frame,
    /// DeleteCloth of the whole set when one tears (created again next frame), then CPhysicsCloth::Render
    /// (PhysicsManager.cpp:761-874).
    /// Not ported: PCT_MASK_LIGHT sprites (Dark Lord hair only) and the cloth shadow (ZzzBMD.cpp:2299).
    /// </summary>
    public sealed class SourceClothSet
    {
        private const int AlphaTestReference = 63; // glAlphaFunc(GL_GREATER, 0.25f), ZzzOpenglUtil.cpp:637

        private static readonly RasterizerState CullOriginalBack = new()
        {
            CullMode = CullMode.CullClockwiseFace,
            Name = "SourceCloth.CullBack",
        };

        private sealed class Entry
        {
            public Entry(ClothDefinition definition) => Definition = definition;

            public readonly ClothDefinition Definition;
            public readonly SourcePhysicsCloth Cloth = new();
            public Texture2D? Front;
            public Texture2D? Back;
            public VertexPositionColorTexture[] Vertices = Array.Empty<VertexPositionColorTexture>();
            public short[] FrontIndices = Array.Empty<short>();
            public short[] BackIndices = Array.Empty<short>();
        }

        private readonly List<Entry> _entries = new(2);
        private double _lastMoveTime = double.NaN;

        public SourceClothSet(params ClothDefinition[] definitions)
        {
            for (int i = 0; i < definitions.Length; i++)
                _entries.Add(new Entry(definitions[i]));
        }

        public int Count => _entries.Count;
        public bool HasTextures { get; private set; }

        public async Task LoadAsync()
        {
            bool any = false;
            foreach (Entry entry in _entries)
            {
                entry.Front = await TryLoad(entry.Definition.FrontTexture);
                entry.Back = string.IsNullOrEmpty(entry.Definition.BackTexture) ||
                             entry.Definition.BackTexture == entry.Definition.FrontTexture
                    ? entry.Front
                    : await TryLoad(entry.Definition.BackTexture);
                any |= entry.Front != null;
            }
            HasTextures = any;
        }

        /// <summary>Use already loaded textures instead of <see cref="LoadAsync"/>.</summary>
        public void SetTextures(int index, Texture2D? front, Texture2D? back)
        {
            Entry entry = _entries[index];
            entry.Front = front;
            entry.Back = back ?? front;
            HasTextures = false;
            foreach (Entry e in _entries)
                HasTextures |= e.Front != null;
        }

        private static async Task<Texture2D?> TryLoad(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                return await TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>DeleteCloth (ZzzCharacter.cpp:2904): drops every cloth; they are created again on the next move.</summary>
        public void Reset()
        {
            foreach (Entry entry in _entries)
                entry.Cloth.Destroy();
            _lastMoveTime = double.NaN;
        }

        /// <summary>
        /// Create (if needed) and Move2(time, 5) every cloth once per frame; extra calls in the same frame do nothing.
        /// <paramref name="time"/> is 0.005, or 0 while the owner is stunned / asleep (ZzzCharacter.cpp:9775-9780).
        /// </summary>
        public void Move(ModelObject owner, float time = 0.005f)
        {
            double now = FPSCounter.Instance.WorldTime;
            if (now == _lastMoveTime)
                return;
            _lastMoveTime = now;

            foreach (Entry entry in _entries)
            {
                if (!entry.Cloth.IsCreated && !CreateCloth(owner, entry))
                    continue;

                if (!entry.Cloth.Move2(owner, time, 5))
                {
                    Reset();
                    _lastMoveTime = now;
                    return;
                }
            }
        }

        private static bool CreateCloth(ModelObject owner, Entry entry)
        {
            ClothDefinition d = entry.Definition;
            SourcePhysicsCloth cloth = entry.Cloth;
            if (!cloth.Create(owner, d.Bone, d.Offset.X, d.Offset.Y, d.Offset.Z, d.NumHor, d.NumVer, d.Width, d.Height, d.Type))
                return false;

            // Spheres are added once per Create, like the original where each Create follows a fresh cloth.
            cloth.ClearCollisionSpheres();
            foreach (ClothCollisionSphere sphere in d.Spheres)
                cloth.AddCollisionSphere(sphere.Center.X, sphere.Center.Y, sphere.Center.Z, sphere.Radius, sphere.Bone);
            cloth.SetWindMinMax(d.WindMin, d.WindMax);

            if (entry.Vertices.Length != cloth.VertexCount)
            {
                entry.Vertices = new VertexPositionColorTexture[cloth.VertexCount];
                BuildIndices(cloth.HorizontalCount, cloth.VerticalCount, out entry.FrontIndices, out entry.BackIndices);
            }
            return true;
        }

        // RenderFace (PhysicsManager.cpp:832-866): front quads (i,j)(i+1,j)(i+1,j+1)(i,j+1), back quads reversed.
        private static void BuildIndices(int numHor, int numVer, out short[] front, out short[] back)
        {
            int quads = (numHor - 1) * (numVer - 1);
            front = new short[quads * 6];
            back = new short[quads * 6];
            int k = 0;
            for (int j = 0; j < numVer - 1; ++j)
            {
                for (int i = 0; i < numHor - 1; ++i)
                {
                    short a = (short)(numHor * j + i);
                    short b = (short)(a + 1);
                    short c = (short)(a + 1 + numHor);
                    short d = (short)(a + numHor);
                    front[k] = a; front[k + 1] = b; front[k + 2] = c;
                    front[k + 3] = a; front[k + 4] = c; front[k + 5] = d;
                    back[k] = a; back[k + 1] = d; back[k + 2] = c;
                    back[k + 3] = a; back[k + 4] = c; back[k + 5] = b;
                    k += 6;
                }
            }
        }

        /// <summary>CPhysicsCloth::Render(pvColor): colour is glColor3fv(pvColor) or white, unlit.</summary>
        public void Draw(GraphicsDevice gd, Vector3 color, float alpha)
        {
            if (alpha <= 0.001f)
                return;

            AlphaTestEffect effect = GraphicsManager.Instance.AlphaTestEffect3D;
            if (effect == null)
                return;

            BlendState previousBlend = gd.BlendState;
            DepthStencilState previousDepth = gd.DepthStencilState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];
            Matrix previousWorld = effect.World;
            Matrix previousView = effect.View;
            Matrix previousProjection = effect.Projection;
            Texture2D previousTexture = effect.Texture;
            Vector3 previousDiffuse = effect.DiffuseColor;
            float previousAlpha = effect.Alpha;
            bool previousVertexColor = effect.VertexColorEnabled;
            CompareFunction previousFunction = effect.AlphaFunction;
            int previousReference = effect.ReferenceAlpha;

            try
            {
                effect.World = Matrix.Identity;
                effect.View = Camera.Instance.View;
                effect.Projection = Camera.Instance.Projection;
                effect.DiffuseColor = color;
                effect.Alpha = alpha;
                effect.VertexColorEnabled = false;
                gd.SamplerStates[0] = SamplerState.LinearWrap;

                foreach (Entry entry in _entries)
                {
                    SourcePhysicsCloth cloth = entry.Cloth;
                    if (!cloth.IsCreated || entry.Front == null)
                        continue;

                    FillVertices(entry);
                    PhysicsClothType draw = cloth.Type & PhysicsClothType.MaskDraw;
                    switch (draw)
                    {
                        case PhysicsClothType.DrawAlpha: // EnableAlphaTest: SRC_ALPHA / 1-SRC_ALPHA, alpha > 0.25, no culling
                            gd.BlendState = BlendState.NonPremultiplied;
                            gd.DepthStencilState = DepthStencilState.Default;
                            gd.RasterizerState = RasterizerState.CullNone;
                            effect.AlphaFunction = CompareFunction.Greater;
                            effect.ReferenceAlpha = AlphaTestReference;
                            break;
                        case PhysicsClothType.DrawBlend: // EnableAlphaBlend: ONE / ONE, no depth write, no culling
                            gd.BlendState = Blendings.OneOneAdditive;
                            gd.DepthStencilState = DepthStencilState.DepthRead;
                            gd.RasterizerState = RasterizerState.CullNone;
                            effect.AlphaFunction = CompareFunction.Always;
                            effect.ReferenceAlpha = 0;
                            break;
                        default: // DisableAlphaBlend: opaque, back faces culled
                            gd.BlendState = alpha < 0.999f ? BlendState.NonPremultiplied : BlendState.Opaque;
                            gd.DepthStencilState = DepthStencilState.Default;
                            gd.RasterizerState = CullOriginalBack;
                            effect.AlphaFunction = CompareFunction.Always;
                            effect.ReferenceAlpha = 0;
                            break;
                    }

                    DrawFace(gd, effect, entry, entry.Front, entry.FrontIndices);
                    // PCT_MASK_LIGHT is not ported, so the back face is always drawn (PhysicsManager.cpp:822-825).
                    if (entry.Back != null)
                        DrawFace(gd, effect, entry, entry.Back, entry.BackIndices);
                }
            }
            finally
            {
                effect.World = previousWorld;
                effect.View = previousView;
                effect.Projection = previousProjection;
                effect.Texture = previousTexture;
                effect.DiffuseColor = previousDiffuse;
                effect.Alpha = previousAlpha;
                effect.VertexColorEnabled = previousVertexColor;
                effect.AlphaFunction = previousFunction;
                effect.ReferenceAlpha = previousReference;
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
            }
        }

        // RenderVertex (PhysicsManager.cpp:868-874): u = x / (NumHor - 1), v = min(0.99, y / (NumVer - 1)).
        private static void FillVertices(Entry entry)
        {
            SourcePhysicsCloth cloth = entry.Cloth;
            ReadOnlySpan<Vector3> positions = cloth.Positions;
            int numHor = cloth.HorizontalCount;
            int numVer = cloth.VerticalCount;
            for (int j = 0; j < numVer; ++j)
            {
                float v = MathF.Min(0.99f, j / (float)(numVer - 1));
                for (int i = 0; i < numHor; ++i)
                {
                    int index = numHor * j + i;
                    entry.Vertices[index] = new VertexPositionColorTexture(positions[index], Color.White, new Vector2(i / (float)(numHor - 1), v));
                }
            }
        }

        private static void DrawFace(GraphicsDevice gd, AlphaTestEffect effect, Entry entry, Texture2D texture, short[] indices)
        {
            effect.Texture = texture;
            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, entry.Vertices, 0, entry.Vertices.Length, indices, 0, indices.Length / 3);
            }
        }
    }
}
