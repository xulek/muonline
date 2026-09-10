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
    /// SourceMain5.2 InsertBuffPhysicalEffect eDeBuff_Stun: three MODEL_SPEARSKILL
    /// rings spinning above the stunned character's head
    /// (CreateJoint(MODEL_SPEARSKILL, ..., sub8, owner, 30)).
    /// Approximated with counter-rotating horizontal ring sprites.
    /// </summary>
    public sealed class StunRingsEffect : EffectObject
    {
        private const string RingTexturePath = "Effect/ring.jpg";
        private const int RingCount = 3;
        private const float HeightOffset = 150f;
        private const float RingSpacing = 14f;

        private Texture2D? _ringTexture;
        private readonly WalkerObject _owner;

        public StunRingsEffect(WalkerObject owner)
        {
            _owner = owner;
            Position = new Vector3(0f, 0f, HeightOffset);

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-80f, -80f, -40f),
                new Vector3(80f, 80f, 60f));
        }

        public override async Task Load()
        {
            await base.Load();
            _ringTexture = await TextureLoader.Instance.PrepareAndGetTexture(RingTexturePath);
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

            if (!Visible || _ringTexture == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.DepthRead))
                    DrawRings(gameTime);
            }
            else
                DrawRings(gameTime);
        }

        private void DrawRings(GameTime gameTime)
        {
            var spriteBatch = GraphicsManager.Instance.Sprite;
            float time = (float)gameTime.TotalGameTime.TotalSeconds;
            Vector3 center = WorldPosition.Translation;
            Color tint = new Color(160, 200, 255, 255) * 0.75f;

            for (int i = 0; i < RingCount; i++)
            {
                // Rings spin in alternating directions and bob slightly.
                float direction = i % 2 == 0 ? 1f : -1f;
                float rotation = time * 2.4f * direction + i * 1.1f;
                float bobZ = MathF.Sin(time * 4f + i) * RingSpacing * 0.35f;
                float scale = (34f - i * RingSpacing) * ComputeScreenScale(center);

                Vector3 ringCenter = center + new Vector3(0f, 0f, -i * RingSpacing + bobZ);
                if (!TryProject(ringCenter, out var screen))
                    continue;

                spriteBatch.Draw(
                    _ringTexture,
                    screen,
                    null,
                    tint,
                    rotation,
                    new Vector2(_ringTexture.Width * 0.5f, _ringTexture.Height * 0.5f),
                    scale,
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
