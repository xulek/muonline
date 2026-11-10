using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Magic circle on top of the character's pedestal: a soft pool of light, a rune ring and a ray burst turning in
    /// opposite directions, breathing slowly. Drawn additively on the pedestal surface.
    /// </summary>
    internal sealed class PedestalGlow : WorldObject
    {
        private Texture2D _ring;
        private Texture2D _rays;
        private BasicEffect _effect;
        private readonly VertexPositionColorTexture[] _quad = new VertexPositionColorTexture[6];
        private float _time;

        public PedestalGlow(Vector3 center)
        {
            Position = center;
            IsTransparent = true;
            AffectedByTransparency = false;
            BoundingBoxLocal = new BoundingBox(new Vector3(-200f, -200f, -20f), new Vector3(200f, 200f, 800f));
        }

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(forceVisible: true, alwaysUpdate: true);

        public override async Task LoadContent()
        {
            _ring = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Magic_Ground1.OZJ");
            _rays = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Magic_Ground2.OZJ");
            await base.LoadContent();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            _time = (float)(gameTime.TotalGameTime.TotalSeconds % 3600.0);
            if (!Visible)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            _effect ??= new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
            _effect.World = Matrix.Identity;
            _effect.View = Camera.Instance.View;
            _effect.Projection = Camera.Instance.Projection;

            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;
            var previousSampler = gd.SamplerStates[0];
            gd.BlendState = SceneFxTextures.AddPremultiplied;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            float breath = 0.82f + 0.18f * MathF.Sin(_time * 1.6f);
            DrawDisc(gd, SceneFxTextures.Glow, 120f, 0f, new Vector3(0.2f, 0.35f, 0.9f) * 0.3f * breath, 2f);
            DrawDisc(gd, _rays, 118f, -_time * 0.18f, new Vector3(0.62f, 0.4f, 1f) * 0.3f * breath, 3f);
            DrawBeam(gd, 95f, 760f, new Vector3(0.35f, 0.55f, 1f) * 0.22f * breath);
            DrawDisc(gd, _ring, 96f, _time * 0.32f, new Vector3(0.4f, 0.85f, 1f) * (0.8f + 0.2f * breath), 4f);

            gd.BlendState = previousBlend;
            gd.DepthStencilState = previousDepth;
            gd.RasterizerState = previousRasterizer;
            gd.SamplerStates[0] = previousSampler;
        }

        /// <summary>Column of light standing on the pedestal, turned towards the camera around the vertical axis.</summary>
        private void DrawBeam(GraphicsDevice gd, float halfWidth, float height, Vector3 color)
        {
            Vector3 toCamera = Camera.Instance.Position - Position;
            toCamera.Z = 0f;
            if (toCamera.LengthSquared() < 1f)
                return;
            toCamera.Normalize();
            Vector3 side = new Vector3(-toCamera.Y, toCamera.X, 0f) * halfWidth;
            Vector3 up = new(0f, 0f, height);
            Vector3 b = Position - toCamera * 45f + new Vector3(0f, 0f, -10f);   // just behind the character: a back light
            var tint = new Color(color);
            var p0 = new VertexPositionColorTexture(b - side + up, tint, new Vector2(0f, 0f));
            var p1 = new VertexPositionColorTexture(b + side + up, tint, new Vector2(1f, 0f));
            var p2 = new VertexPositionColorTexture(b + side, tint, new Vector2(1f, 1f));
            var p3 = new VertexPositionColorTexture(b - side, tint, new Vector2(0f, 1f));
            _quad[0] = p0; _quad[1] = p1; _quad[2] = p2;
            _quad[3] = p0; _quad[4] = p2; _quad[5] = p3;

            _effect.Texture = SceneFxTextures.Beam;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, _quad, 0, 2);
            }
        }

        private void DrawDisc(GraphicsDevice gd, Texture2D texture, float radius, float angle, Vector3 color, float lift)
        {
            if (texture == null || texture.IsDisposed)
                return;

            Vector3 c = Position + new Vector3(0f, 0f, lift);
            Vector3 u = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0f);
            Vector3 v = new(-u.Y, u.X, 0f);
            var tint = new Color(color);
            var p0 = new VertexPositionColorTexture(c - u - v, tint, new Vector2(0f, 0f));
            var p1 = new VertexPositionColorTexture(c + u - v, tint, new Vector2(1f, 0f));
            var p2 = new VertexPositionColorTexture(c + u + v, tint, new Vector2(1f, 1f));
            var p3 = new VertexPositionColorTexture(c - u + v, tint, new Vector2(0f, 1f));
            _quad[0] = p0; _quad[1] = p1; _quad[2] = p2;
            _quad[3] = p0; _quad[4] = p2; _quad[5] = p3;

            _effect.Texture = texture;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, _quad, 0, 2);
            }
        }

        public override void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            base.Dispose();
        }
    }
}
