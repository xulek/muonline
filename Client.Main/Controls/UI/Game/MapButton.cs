﻿using Client.Main.Controllers;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI.Game
{
    public class MapButton<TWorld> : UIControl where TWorld : WalkableWorldControl, new()
    {
        public new string Name { get; set; }
        public Color TextColor { get; set; } = Color.White;
        public float FontSize { get; set; } = 10f;

        public MapButton()
        {
            ViewSize = new Point(190, 20);
            AutoViewSize = false;
            Interactive = true;
        }

        public override async Task Load()
        {
            await base.Load();

            Click += MapButton_Click;
        }

        private void MapButton_Click(object sender, System.EventArgs e)
        {
            if (Root is GameScene gameScene)
            {
                _ = gameScene.ChangeMap<TWorld>();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            float scaleFactor = FontSize / Constants.BASE_FONT_SIZE;

            GraphicsManager.Instance.Sprite.Begin();
            GraphicsManager.Instance.Sprite.DrawString(
                GraphicsManager.Instance.Font,
                Name,
                new Vector2(DisplayRectangle.X + 5, DisplayRectangle.Y + 5),
                TextColor,
                0f,
                Vector2.Zero,
                scaleFactor,
                SpriteEffects.None,
                0
            );
            GraphicsManager.Instance.Sprite.End();

            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Scene.MouseControl == this)
            {
                BackgroundColor = Color.Yellow;
                TextColor = Color.Black;
            }
            else
            {
                BackgroundColor = Color.Transparent;
                TextColor = Color.White;
            }
        }
    }
}