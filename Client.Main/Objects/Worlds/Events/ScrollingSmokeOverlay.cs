#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Events
{
    /// <summary>
    /// Generalized recreation of SourceMain5.2 RenderBaseSmoke / RenderOutSides fullscreen
    /// smoke layers (Crywolf 1st, BattleCastle, Swamp of Quiet, Raklion, Karutan...).
    /// Each layer is a resolution-proportional RenderBitmapUV sheared quad with its own
    /// texture, tint, scroll speed and tile density; per-layer blend mode replicates the
    /// EnableAlphaTest / EnableAlphaBlend (additive) sequence.
    /// </summary>
    public sealed class ScrollingSmokeOverlay : IDisposable
    {
        public enum LayerBlend
        {
            /// <summary>GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA (EnableAlphaTest).</summary>
            Normal,
            /// <summary>GL_ONE, GL_ONE (EnableAlphaBlend).</summary>
            Additive
        }

        public sealed class Layer
        {
            public string TexturePath = string.Empty;
            public Color Tint = new(0.4f, 0.4f, 0.45f);
            public float USpeed;
            public float VSpeed;
            public float TileU;
            public float TileV;
            public LayerBlend Blend = LayerBlend.Additive;

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812")]
            internal readonly VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[6];
            internal Texture2D? Texture;
        }

        private readonly Layer[] _layers;
        private BasicEffect? _effect;

        public ScrollingSmokeOverlay(params Layer[] layers)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        }

        /// <summary>
        /// Standard two-layer haze: Map_Smoke2.tga normal-blended slow layer over an
        /// additive Map_Smoke1.jpg layer (source CHROME+3 / CHROME+2 pairing).
        /// </summary>
        public static ScrollingSmokeOverlay CreateStandard(Color tint)
        {
            return new ScrollingSmokeOverlay(
                new Layer
                {
                    TexturePath = "Effect/Map_Smoke2.tga",
                    Tint = tint,
                    USpeed = 0.0005f,
                    TileU = 3f,
                    TileV = 2f,
                    Blend = LayerBlend.Normal
                },
                new Layer
                {
                    TexturePath = "Effect/Map_Smoke1.jpg",
                    Tint = tint,
                    USpeed = 0.0002f,
                    TileU = 0.3f,
                    TileV = 0.3f,
                    Blend = LayerBlend.Additive
                });
        }

        public async Task Load()
        {
            foreach (Layer layer in _layers)
                layer.Texture = await TextureLoader.Instance.PrepareAndGetTexture(layer.TexturePath);

            var gd = GraphicsManager.Instance.GraphicsDevice;
            if (gd != null)
            {
                _effect = new BasicEffect(gd)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    World = Matrix.Identity,
                    View = Matrix.Identity
                };
            }
        }

        public void DrawOverlay(GameTime gameTime)
        {
            if (_effect == null || _layers.Length == 0)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            if (gd == null)
                return;

            var vp = gd.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0)
                return;

            _effect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);

            // WindX = (float)((int)WorldTime % 100000) * speed
            float windTime = ((float)((long)gameTime.TotalGameTime.TotalMilliseconds % 100000L));
            float scaleX = vp.Width / 640f;
            float scaleY = vp.Height / 480f;

            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRast = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            try
            {
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;

                foreach (Layer layer in _layers)
                {
                    if (layer.Texture == null)
                        continue;

                    gd.BlendState = layer.Blend == LayerBlend.Normal
                        ? BlendState.NonPremultiplied
                        : BlendState.Additive;

                    BuildShearedQuad(
                        layer.Vertices,
                        windTime * layer.USpeed,
                        windTime * layer.VSpeed,
                        layer.TileU * scaleX,
                        layer.TileV * scaleY,
                        layer.Tint);

                    _effect.Texture = layer.Texture;
                    foreach (var pass in _effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.SamplerStates[0] = SamplerState.LinearWrap;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, layer.Vertices, 0, 2);
                    }
                }
            }
            finally
            {
                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepth;
                gd.RasterizerState = prevRast;
                gd.SamplerStates[0] = prevSampler;
            }
        }

        /// <summary>
        /// SourceMain5.2 RenderBitmapUV shear mapping:
        /// c[0]=(u, v+vHeight*0.25) c[1]=(u, v+vHeight*0.75) c[2]=(u+uWidth, v+vHeight) c[3]=(u+uWidth, v).
        /// </summary>
        private static void BuildShearedQuad(
            VertexPositionColorTexture[] verts,
            float u, float v, float uWidth, float vHeight,
            Color color)
        {
            Vector3 tl = new(0f, 0f, 0f);
            Vector3 bl = new(0f, 1f, 0f);
            Vector3 br = new(1f, 1f, 0f);
            Vector3 tr = new(1f, 0f, 0f);

            Vector2 uvTL = new(u, v + vHeight * 0.25f);
            Vector2 uvBL = new(u, v + vHeight * 0.75f);
            Vector2 uvBR = new(u + uWidth, v + vHeight);
            Vector2 uvTR = new(u + uWidth, v);

            verts[0] = new(tl, color, uvTL);
            verts[1] = new(tr, color, uvTR);
            verts[2] = new(br, color, uvBR);
            verts[3] = new(tl, color, uvTL);
            verts[4] = new(br, color, uvBR);
            verts[5] = new(bl, color, uvBL);
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
        }
    }
}
