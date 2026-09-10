using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Login
{
    public class ServerGroupButton : SpriteControl
    {
        private readonly LabelControl _label;

        public byte Index { get; set; }
        public new string Name { get => _label.Text; set => _label.Text = value; }
        public bool Selected { get; set; }

        public ServerGroupButton()
        {
            Controls.Add(_label = new LabelControl
            {
                Align = ControlAlign.HorizontalCenter,
                Y = 6
            });

            Interactive = true;
            TileWidth = 110;
            TileHeight = 26;
            TileY = 0;
            BlendState = Blendings.Alpha;
            TexturePath = "Interface/cha_bt.tga";
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            _label.TextColor = ModernHudTheme.TextWhite;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Scene == null)
            {
                TileY = 0;
                return;
            }

            if (Selected)
                TileY = 3;
            else if (IsMouseOver && IsMousePressed) // check IsMousePressed for click visual
                TileY = 2;
            else if (IsMouseOver) // hover state
                TileY = 1;
            else
                TileY = 0;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            if (LoginUiTheme.UseModernLayout)
            {
                base.Draw(gameTime);
                return;
            }

            SpriteBatch sprite = GraphicsManager.Instance.Sprite;
            Rectangle rect = DisplayRectangle;
            Color background = Selected
                ? ModernHudTheme.SlotSelected
                : IsMouseOver ? ModernHudTheme.SlotHover : ModernHudTheme.SlotBg;
            UiDrawHelper.DrawPanel(sprite, rect, background,
                ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                IsMouseOver || Selected ? ModernHudTheme.Accent : ModernHudTheme.BorderHighlight);
            _label.Draw(gameTime);
        }
    }
}
