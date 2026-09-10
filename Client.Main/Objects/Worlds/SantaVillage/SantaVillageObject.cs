#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SantaVillage
{
    /// <summary>
    /// GMSantaTown.cpp map objects: Types 26/27/28 hidden lamp meshes with red/blue/cyan
    /// BITMAP_LIGHT glow sprites (fLumi=(sin(WT*0.005)+1)*0.1+0.9, scale 8*Scale), and the
    /// Type 16 tree whose second mesh renders as teal chrome (BodyLight 0, 0.5, 0.5).
    /// </summary>
    public class SantaVillageObject : MapTileObject
    {
        private const string ChromeTexturePath = "Effect/Chrome01.jpg";

        private Texture2D? _chromeTexture;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        protected override bool ForceTwoSidedMeshes => Type == 16;

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (Type == 16)
                _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture(ChromeTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            switch (Type)
            {
                case 26 or 27 or 28:
                    HiddenMesh = -2;
                    break;
            }

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
                return;

            base.Draw(gameTime);

            if (Type == 16 && _chromeTexture != null && Model?.Meshes != null && Model.Meshes.Length > 2)
                DrawTreeChromePass();
        }

        private void DrawTreeChromePass()
        {
            var prevBlend = GraphicsDevice.BlendState;
            try
            {
                // Mesh 2 RENDER_CHROME|BRIGHT with BodyLight(0, 0.5, 0.5) teal tint
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                const int meshIndex = 2;
                Texture2D originalTexture = GetMeshTexture(meshIndex);
                try
                {
                    SetMeshTextureOverride(meshIndex, _chromeTexture);
                    DrawMesh(meshIndex);
                }
                finally
                {
                    if (originalTexture != null)
                        SetMeshTextureOverride(meshIndex, originalTexture);
                    else
                        ClearMeshTextureOverride(meshIndex);
                }
            }
            finally
            {
                GraphicsDevice.BlendState = prevBlend;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            float luminosity = (MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * 0.005f) + 1f) * 0.1f + 0.9f;

            Vector3? glowColor = Type switch
            {
                26 => new Vector3(luminosity * 0.8f, luminosity * 0.2f, 0f),
                27 => new Vector3(0f, luminosity * 0.2f, luminosity * 0.8f),
                28 => new Vector3(0f, luminosity * 0.6f, luminosity * 0.6f),
                _ => null
            };

            if (!glowColor.HasValue || !Visible || Status != GameControlStatus.Ready ||
                Camera.Instance == null || World == null)
            {
                return;
            }

            var device = Controllers.GraphicsManager.Instance.GraphicsDevice;
            var camera = Camera.Instance;
            var spriteBatch = Controllers.GraphicsManager.Instance.Sprite;
            if (device == null || spriteBatch == null)
                return;

            Vector3 projected = device.Viewport.Project(
                WorldPosition.Translation,
                camera.Projection,
                camera.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            Vector3 edge = device.Viewport.Project(
                WorldPosition.Translation + camera.Right * (8f * Scale),
                camera.Projection,
                camera.View,
                Matrix.Identity);
            float radiusPx = MathF.Abs(edge.X - projected.X);

            void drawGlow()
            {
                spriteBatch.Draw(
                    Controllers.GraphicsManager.Instance.Pixel,
                    new Vector2(projected.X, projected.Y),
                    null,
                    new Color(glowColor.Value.X, glowColor.Value.Y, glowColor.Value.Z, 0.9f),
                    0f,
                    Vector2.Zero,
                    radiusPx,
                    SpriteEffects.None,
                    projected.Z);
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    drawGlow();
                }
            }
            else
            {
                drawGlow();
            }
        }
    }
}



