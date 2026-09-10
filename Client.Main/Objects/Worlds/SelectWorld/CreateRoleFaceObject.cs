using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Displays the selected character class on the Classic character-creation screen.
    /// The object is kept in the selection world so it uses the normal model renderer.
    /// </summary>
    public sealed class CreateRoleFaceObject : ModelObject
    {
        private const float TargetHeight = 200f;
        private const float FallbackScale = 845f;
        private const float HeadRoom = 170f;
        private const float CameraAngle = 90f;
        private const float AnimationFps = 25f;
        private const int WindowMargin = 18;

        private string _modelName;
        private float _modelMinZ;
        private float _modelMaxZ;

        protected override bool RequiresPerFrameAnimation => true;

        public CreateRoleFaceObject()
        {
            Interactive = false;
            LightEnabled = false;
            RenderShadow = false;
            UseSunLight = false;
            AnimationSpeed = AnimationFps;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            DrawCreationWindowBackdrop();
            base.Draw(gameTime);
        }

        public async Task SetClass(string modelName, Vector3 anchor)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                Hidden = true;
                return;
            }

            bool needsReload = !string.Equals(_modelName, modelName, StringComparison.OrdinalIgnoreCase) ||
                               Status != GameControlStatus.Ready;
            if (needsReload)
            {
                Hidden = true;

                // Reset the object before assigning Model. ModelObject automatically starts
                // a background reload only for Ready/Error objects; keeping it NonInitialized
                // gives this method one explicit load path and prevents duplicate buffers.
                Status = GameControlStatus.NonInitialized;
                Model = await BMDLoader.Instance.Prepare($"Logo/{modelName}.bmd");
                _modelName = modelName;
                await Load();

                if (Status != GameControlStatus.Ready)
                    return;

                (_modelMinZ, _modelMaxZ) = MeasureModelZ();
            }

            Angle = new Vector3(
                0f,
                0f,
                MathHelper.ToRadians(CameraAngle));

            float rawHeight = _modelMaxZ - _modelMinZ;
            Scale = rawHeight > 0.0001f ? TargetHeight / rawHeight : FallbackScale;
            Position = anchor + Vector3.UnitZ * (HeadRoom - _modelMaxZ * Scale);
        }

        private static void DrawCreationWindowBackdrop()
        {
            SpriteBatch sprite = GraphicsManager.Instance?.Sprite;
            Texture2D pixel = GraphicsManager.Instance?.Pixel;
            if (sprite == null || pixel == null || pixel.IsDisposed)
                return;

            Point virtualSize = UiScaler.VirtualSize;
            Rectangle window = new(
                WindowMargin,
                WindowMargin,
                Math.Max(1, virtualSize.X - WindowMargin * 2),
                Math.Max(1, virtualSize.Y - WindowMargin * 2));

            using var scope = new SpriteBatchScope(
                sprite,
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                UiScaler.SpriteTransform);

            sprite.Draw(pixel, window, Color.Black);

            Color outerBorder = new(63, 82, 106, 255);
            Color innerBorder = new(18, 25, 35, 255);
            DrawHorizontalBorder(sprite, pixel, window.X, window.Y, window.Width, outerBorder);
            DrawHorizontalBorder(sprite, pixel, window.X, window.Bottom - 2, window.Width, outerBorder);
            DrawVerticalBorder(sprite, pixel, window.X, window.Y, window.Height, outerBorder);
            DrawVerticalBorder(sprite, pixel, window.Right - 2, window.Y, window.Height, outerBorder);

            Rectangle inner = new(window.X + 2, window.Y + 2, window.Width - 4, window.Height - 4);
            DrawHorizontalBorder(sprite, pixel, inner.X, inner.Y, inner.Width, innerBorder);
            DrawHorizontalBorder(sprite, pixel, inner.X, inner.Bottom - 1, inner.Width, innerBorder);
            DrawVerticalBorder(sprite, pixel, inner.X, inner.Y, inner.Height, innerBorder);
            DrawVerticalBorder(sprite, pixel, inner.Right - 1, inner.Y, inner.Height, innerBorder);
        }

        private static void DrawHorizontalBorder(
            SpriteBatch sprite, Texture2D pixel, int x, int y, int width, Color color)
        {
            sprite.Draw(pixel, new Rectangle(x, y, Math.Max(1, width), 1), color);
        }

        private static void DrawVerticalBorder(
            SpriteBatch sprite, Texture2D pixel, int x, int y, int height, Color color)
        {
            sprite.Draw(pixel, new Rectangle(x, y, 1, Math.Max(1, height)), color);
        }

        private (float Min, float Max) MeasureModelZ()
        {
            if (Model?.Meshes == null || BoneTransform == null)
                return (0f, 0f);

            float min = float.MaxValue;
            float max = float.MinValue;

            foreach (var mesh in Model.Meshes)
            {
                if (mesh.Vertices == null)
                    continue;

                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.Node < 0 || vertex.Node >= BoneTransform.Length)
                        continue;

                    float z = Vector3.Transform(vertex.Position, BoneTransform[vertex.Node]).Z;
                    min = MathF.Min(min, z);
                    max = MathF.Max(max, z);
                }
            }

            return max > min ? (min, max) : (0f, 0f);
        }
    }
}
