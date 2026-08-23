using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements glowing pulsing light balls in Tarkan (Objects 63 and 64).
    /// Object 63: Cyan/white glow.
    /// Object 64: Red/orange glow.
    /// SourceMain5.2 ZzzObject.cpp cases 63, 64.
    /// </summary>
    public class TarkanLightGlowObject : ModelObject
    {
        private Texture2D _impactTexture;
        private Vector2 _texCenter;

        public TarkanLightGlowObject()
        {
            HiddenMesh = -2;
            Hidden = false;
            IsTransparent = true;
            AffectedByTransparency = true;
        }

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object9/Object{idx}.bmd");
            _impactTexture = await TextureLoader.Instance.PrepareAndGetTexture("Object9/Impack03.jpg");
            if (_impactTexture != null)
                _texCenter = new Vector2(_impactTexture.Width * 0.5f, _impactTexture.Height * 0.5f);

            await base.Load();
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_impactTexture == null || Hidden)
            {
                base.DrawAfter(gameTime);
                return;
            }

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var camera = Camera.Instance;
            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (gd == null || camera == null || spriteBatch == null)
            {
                base.DrawAfter(gameTime);
                return;
            }

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float luminosity = MathF.Sin((totalMs + Angle.Z * 5f) * 0.002f) * 0.3f + 0.7f;
            float scale = luminosity * 1.5f;

            Color glowColor;
            if (Type == 64)
            {
                // Red/orange
                glowColor = new Color(luminosity, luminosity * 0.32f, luminosity * 0.32f, 1f);
            }
            else
            {
                // Cyan/white (Type 63)
                glowColor = new Color(luminosity / 1.7f, luminosity, luminosity, 1f);
            }

            // Bone 2 position or object position
            Vector3 worldPos = WorldPosition.Translation;
            var bones = GetBoneTransforms();
            if (bones != null && bones.Length > 2)
            {
                worldPos = Vector3.Transform(bones[2].Translation, WorldPosition);
            }

            Vector4 clipPos = Vector4.Transform(worldPos, camera.ViewProjection);
            if (clipPos.W <= 0.001f)
            {
                base.DrawAfter(gameTime);
                return;
            }

            float invW = 1f / clipPos.W;
            var vp = gd.Viewport;
            Vector2 screenPos = new Vector2(
                (clipPos.X * invW * 0.5f + 0.5f) * vp.Width,
                (0.5f - clipPos.Y * invW * 0.5f) * vp.Height);
            float depth = clipPos.Z * invW;

            if (depth < 0f || depth > 1f)
            {
                base.DrawAfter(gameTime);
                return;
            }

            float distScale = MathHelper.Clamp(1200f / clipPos.W, 0.2f, 4f);
            float spriteScale = scale * distScale * Constants.RENDER_SCALE;

            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone);

            spriteBatch.Draw(_impactTexture, screenPos, null, glowColor, 0f, _texCenter, spriteScale, SpriteEffects.None, depth);

            spriteBatch.End();

            base.DrawAfter(gameTime);
        }
    }
}
