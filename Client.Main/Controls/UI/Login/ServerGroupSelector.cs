using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI.Login
{
    public class ServerGroupSelector : UIControl
    {
        private List<ServerGroupButton> _buttons = [];
        private readonly SpriteControl _decoration;

        public int ActiveIndex { get; set; } = -1;
        public bool IsEventList { get; set; }
        public SpriteControl IndicatorActive { get; }

        public event EventHandler SelectedIndexChanged;

        public ServerGroupSelector(bool eventList)
        {
            IsEventList = eventList;
            Visible = false;

            Controls.Add(_decoration = new SpriteControl
            {
                TexturePath = "Interface/server_deco_all.tga",
                TileWidth = 67,
                TileHeight = 97,
                TileX = eventList ? 1 : 0,
                X = eventList ? 70 : 0,
                BlendState = Blendings.Alpha,
            });

            Controls.Add(IndicatorActive = new SpriteControl
            {
                X = eventList ? 0 : 114,
                Y = 15,
                TexturePath = "Interface/server_deco_all.tga",
                TileOffset = new Point(136, 0),
                TileWidth = 23,
                TileHeight = 29,
                TileY = eventList ? 1 : 0,
                Visible = false,
                BlendState = Blendings.Alpha,
            });

            ApplyThemeLayout();
        }

        public void AddServer(byte index, string name)
        {
            if (!Visible) Visible = true;

            var button = new ServerGroupButton
            {
                Index = index,
                Name = name,
            };

            button.Click += ServerGroupButton_Click;

            _buttons.Add(button);

            Controls.Add(button);

            ApplyThemeLayout();

            IndicatorActive.BringToFront();
        }

        public void UnselectServer()
        {
            if (ActiveIndex >= 0)
            {
                _buttons[ActiveIndex].Selected = false;
                ActiveIndex = -1;
                IndicatorActive.Visible = false;
            }
        }

        private void ServerGroupButton_Click(object sender, EventArgs e)
        {
            if (ActiveIndex >= 0)
                _buttons[ActiveIndex].Selected = false;

            var button = (ServerGroupButton)sender;
            button.Selected = true;
            ActiveIndex = button.Index;

            IndicatorActive.Visible = true;
            IndicatorActive.Y = 17 + ActiveIndex * 27;

            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private void ApplyThemeLayout()
        {
            bool season6 = !LoginUiTheme.UseModernLayout;
            AutoViewSize = !season6;

            if (season6)
            {
                ViewSize = new Point(236, 88);
                BackgroundColor = Color.Transparent;
                BorderThickness = 0;
                _decoration.Visible = false;
                IndicatorActive.Visible = false;

                for (int i = 0; i < _buttons.Count; i++)
                {
                    ServerGroupButton button = _buttons[i];
                    button.X = 10;
                    button.Y = 38 + i * 34;
                    button.ViewSize = new Point(216, 30);
                }
            }
            else
            {
                _decoration.Visible = true;
                IndicatorActive.Visible = ActiveIndex >= 0;

                for (int i = 0; i < _buttons.Count; i++)
                {
                    ServerGroupButton button = _buttons[i];
                    button.X = IsEventList ? 23 : 8;
                    button.Y = 19 + button.Index * 27;
                    button.ViewSize = new Point(110, 26);
                }
            }

            MarkLayoutDirty();
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
            UiDrawHelper.DrawPanel(sprite, rect, ModernHudTheme.BgDark * 0.94f,
                ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                ModernHudTheme.BorderHighlight, withGlow: true,
                glowColor: ModernHudTheme.AccentGlow * 0.35f);

            string title = IsEventList ? "EVENT SERVERS" : "SERVERS";
            SpriteFont font = GraphicsManager.GetUiFont(12f, out float scale) ?? GraphicsManager.Instance.Font;
            if (font != null)
            {
                Vector2 size = font.MeasureString(title) * scale;
                sprite.DrawString(font, title,
                    new Vector2(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + 12),
                    ModernHudTheme.TextGold, 0f, Vector2.Zero, scale,
                    SpriteEffects.None, 0f);
            }

            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Draw(gameTime);
        }
    }
}
