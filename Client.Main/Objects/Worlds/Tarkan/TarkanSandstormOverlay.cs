using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Exact implementation of SourceMain5.2 RenderOutSides() and RenderBitmapUV() for WD_8TARKAN.
    /// Renders two scrolling, diagonally sheared texture layers (sand01.jpg and sand02.jpg)
    /// in additive blend mode with resolution-proportional UV density and natural desert atmosphere.
    /// </summary>
    public sealed class TarkanSandstormOverlay : IDisposable
    {
        private const string Sand01Path = "Object9/sand01.jpg";
        private const string Sand02Path = "Object9/sand02.jpg";

        private Texture2D _sand01Texture;
        private Texture2D _sand02Texture;
        private BasicEffect _effect;

        private readonly VertexPositionColorTexture[] _layer1Vertices = new VertexPositionColorTexture[6];
        private readonly VertexPositionColorTexture[] _layer2Vertices = new VertexPositionColorTexture[6];

        // Atmospheric, natural desert haze colors (balanced so additive blend doesn't bleach the screen)
        public Color Layer1Color { get; set; } = new Color(0.12f, 0.11f, 0.08f, 1.0f); // Soft ambient dust clouds
        public Color Layer2Color { get; set; } = new Color(0.16f, 0.14f, 0.10f, 1.0f); // Fine fast wind sand grains

        public async Task Load()
        {
            _sand01Texture = await TextureLoader.Instance.PrepareAndGetTexture(Sand01Path);
            _sand02Texture = await TextureLoader.Instance.PrepareAndGetTexture(Sand02Path);

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
            if (_sand01Texture == null || _sand02Texture == null || _effect == null)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            if (gd == null)
                return;

            var vp = gd.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0)
                return;

            float screenW = vp.Width;
            float screenH = vp.Height;

            _effect.Projection = Matrix.CreateOrthographicOffCenter(0, screenW, screenH, 0, 0, 1);

            double totalMs = gameTime.TotalGameTime.TotalMilliseconds;
            // SourceMain5.2:
            // float WindX = (float)((int)WorldTime % 100000) * 0.0002f;
            // float WindX2 = (float)((int)WorldTime % 100000) * 0.001f;
            float windX1 = (float)((int)totalMs % 100000) * 0.0002f;
            float windX2 = (float)((int)totalMs % 100000) * 0.001f;

            // Resolution-proportional UV density matching original 640x480 scale:
            // Prevents textures from stretching into giant blurry blobs on modern high-res monitors
            float scaleX = screenW / 640.0f;
            float scaleY = screenH / 480.0f;

            float uWidth1 = 0.3f * scaleX;
            float vHeight1 = 0.3f * scaleY;

            float uWidth2 = 3.0f * scaleX;
            float vHeight2 = 2.0f * scaleY;

            // Layer 1 (ambient sand clouds, sand01.jpg)
            BuildShearedQuad(
                _layer1Vertices,
                0f, 0f, screenW, screenH,
                windX1, 0f, uWidth1, vHeight1,
                Layer1Color);

            // Layer 2 (fast wind sand streaks, sand02.jpg)
            BuildShearedQuad(
                _layer2Vertices,
                0f, 0f, screenW, screenH,
                windX2, 0f, uWidth2, vHeight2,
                Layer2Color);

            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRast = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            try
            {
                // SourceMain: EnableAlphaBlend() -> glBlendFunc(GL_ONE, GL_ONE), DisableDepthMask(), DisableCullFace()
                gd.BlendState = BlendState.Additive;
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;

                // Draw layer 1 (sand01.jpg)
                _effect.Texture = _sand01Texture;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    // Set LinearWrap AFTER pass.Apply() to prevent BasicEffect from resetting it to LinearClamp
                    gd.SamplerStates[0] = SamplerState.LinearWrap;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList, _layer1Vertices, 0, 2);
                }

                // Draw layer 2 (sand02.jpg)
                _effect.Texture = _sand02Texture;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.SamplerStates[0] = SamplerState.LinearWrap;
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList, _layer2Vertices, 0, 2);
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
        /// Recreates SourceMain5.2 RenderBitmapUV shear mapping:
        /// c[0] = (u, v + vHeight * 0.25f)
        /// c[1] = (u, v + vHeight * 0.75f)
        /// c[2] = (u + uWidth, v + vHeight)
        /// c[3] = (u + uWidth, v)
        /// </summary>
        private static void BuildShearedQuad(
            VertexPositionColorTexture[] verts,
            float x, float y, float w, float h,
            float u, float v, float uWidth, float vHeight,
            Color color)
        {
            Vector3 tl = new Vector3(x, y, 0f);
            Vector3 bl = new Vector3(x, y + h, 0f);
            Vector3 br = new Vector3(x + w, y + h, 0f);
            Vector3 tr = new Vector3(x + w, y, 0f);

            Vector2 uvTL = new Vector2(u, v + vHeight * 0.25f);
            Vector2 uvBL = new Vector2(u, v + vHeight * 0.75f);
            Vector2 uvBR = new Vector2(u + uWidth, v + vHeight);
            Vector2 uvTR = new Vector2(u + uWidth, v);

            // Triangle 1: TL, TR, BR
            verts[0] = new VertexPositionColorTexture(tl, color, uvTL);
            verts[1] = new VertexPositionColorTexture(tr, color, uvTR);
            verts[2] = new VertexPositionColorTexture(br, color, uvBR);

            // Triangle 2: TL, BR, BL
            verts[3] = new VertexPositionColorTexture(tl, color, uvTL);
            verts[4] = new VertexPositionColorTexture(br, color, uvBR);
            verts[5] = new VertexPositionColorTexture(bl, color, uvBL);
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
        }
    }
}
