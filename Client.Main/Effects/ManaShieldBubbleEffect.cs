#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Effects
{
    /// <summary>
    /// SourceMain5.2 InsertBuffPhysicalEffect eBuff_WizDefense: soul barrier /
    /// mana shield bubble orbiting the protected character
    /// (CreateJoint(MODEL_SPEARSKILL, ..., sub0, owner, 50)).
    /// Approximated with a pulsing translucent shell: horizontal ring + two
    /// crossed vertical light columns around the owner.
    /// </summary>
    public sealed class ManaShieldBubbleEffect : EffectObject
    {
        private const string RingTexturePath = "Effect/ring.jpg";
        private const string ColumnTexturePath = "Effect/Shiny01.jpg";

        private const float BaseRadius = 62f;
        private const float HeightOffset = 65f;
        private const float PulseSpeed = 2.2f;

        private Texture2D? _ringTexture;
        private Texture2D? _columnTexture;
        private readonly WalkerObject _owner;

        public ManaShieldBubbleEffect(WalkerObject owner)
        {
            _owner = owner;
            Position = new Vector3(0f, 0f, HeightOffset);

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-120f, -120f, -140f),
                new Vector3(120f, 120f, 160f));
        }

        public override async Task Load()
        {
            await base.Load();

            _ringTexture = await TextureLoader.Instance.PrepareAndGetTexture(RingTexturePath);
            _columnTexture = await TextureLoader.Instance.PrepareAndGetTexture(ColumnTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_owner.Status == GameControlStatus.Disposed || _owner.Hidden)
            {
                RemoveSelf();
                return;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (!Visible || _ringTexture == null || _columnTexture == null)
                return;

            float time = (float)gameTime.TotalGameTime.TotalSeconds;
            float pulse = 0.42f + MathF.Sin(time * PulseSpeed) * 0.16f;
            Color tint = new Color(70, 130, 255, 255) * pulse;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.DepthRead))
                    DrawShell(time, tint, pulse);
            }
            else
                DrawShell(time, tint, pulse);
        }

        private void DrawShell(float time, Color tint, float pulse)
        {
            var spriteBatch = GraphicsManager.Instance.Sprite;

            // Horizontal ring at torso height, slowly rotating around the owner.
            float ringRotation = time * 0.9f;
            float ringScale = BaseRadius * pulse * ComputeScreenScale(WorldPosition.Translation);

            Vector3 ringCenter = WorldPosition.Translation;
            if (TryProject(ringCenter, out var screen))
            {
                spriteBatch.Draw(
                    _ringTexture,
                    screen,
                    null,
                    tint,
                    ringRotation,
                    new Vector2(_ringTexture.Width * 0.5f, _ringTexture.Height * 0.5f),
                    ringScale,
                    SpriteEffects.None,
                    0.5f);
            }

            // Two crossed vertical columns forming the dome silhouette.
            for (int i = 0; i < 2; i++)
            {
                float angle = time * 0.6f + i * MathHelper.PiOver2;
                Vector3 offset = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * BaseRadius * 0.72f;
                if (!TryProject(ringCenter + offset, out var columnScreen))
                    continue;

                float columnScale = BaseRadius * 1.35f * ComputeScreenScale(ringCenter + offset);
                spriteBatch.Draw(
                    _columnTexture,
                    columnScreen,
                    null,
                    tint * 0.85f,
                    0f,
                    new Vector2(_columnTexture.Width * 0.5f, _columnTexture.Height * 0.5f),
                    columnScale,
                    SpriteEffects.None,
                    0.5f);
            }
        }

        private static bool TryProject(Vector3 worldPos, out Vector2 screen)
        {
            var camera = Camera.Instance;
            if (camera == null)
            {
                screen = default;
                return false;
            }

            Vector3 projected = GraphicsManager.Instance.GraphicsDevice.Viewport.Project(
                worldPos, camera.Projection, camera.View, Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
            {
                screen = default;
                return false;
            }

            screen = new Vector2(projected.X, projected.Y);
            return true;
        }

        private static float ComputeScreenScale(Vector3 worldPos)
        {
            float distance = Vector3.Distance(Camera.Instance.Position, worldPos);
            return 1f / (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) * Constants.RENDER_SCALE;
        }

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }
    }
}
