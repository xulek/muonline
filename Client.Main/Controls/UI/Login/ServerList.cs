using Client.Main.Core.Models;
using Client.Main.Models;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Graphics;
using Client.Main.Controllers;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI.Login
{
    public class ServerSelectEventArgs : EventArgs
    {
        public byte Index { get; set; }
        public string Name { get; set; }
    }

    public class ServerList : UIControl
    {
        private readonly List<ServerButton> _serverButtons = new();

        public event EventHandler<ServerSelectEventArgs> ServerClick;

        public void AddServer(byte index, string name, byte gauge)
        {
            bool season6 = !LoginUiTheme.UseModernLayout;
            var button = new ServerButton
            {
                Index = index,
                Name = name,
                X = season6 ? 16 : 0,
                Y = season6 ? 36 + _serverButtons.Count * 38 : index * 26,
                Gauge = gauge
            };
            button.Click += (s, e) => ServerClick?.Invoke(this, new ServerSelectEventArgs { Index = index, Name = name });
            _serverButtons.Add(button);
            Controls.Add(button);
            ApplyThemeLayout();
        }

        public void Clear()
        {
            _serverButtons.Clear();
            Controls.Clear();
            ApplyThemeLayout();
        }

        public void SetServers(IReadOnlyList<ServerInfo> servers)
        {
            Clear();
            if (servers == null)
            {
                return;
            }

            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                byte gauge = server.LoadPercentage;
                AddServer((byte)server.ServerId, server.ServerName ?? $"Server {server.ServerId}", gauge);
            }
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
                ViewSize = new Point(320, Math.Max(82, 48 + _serverButtons.Count * 38));
                BackgroundColor = Color.Transparent;
                BorderThickness = 0;
            }

            for (int i = 0; i < _serverButtons.Count; i++)
            {
                ServerButton button = _serverButtons[i];
                button.X = season6 ? 16 : 0;
                button.Y = season6 ? 36 + i * 38 : button.Index * 26;
                button.ViewSize = season6 ? new Point(288, 32) : new Point(192, 26);
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
            UiDrawHelper.DrawPanel(sprite, rect, ModernHudTheme.BgDark * 0.96f,
                ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                ModernHudTheme.BorderHighlight, withGlow: true,
                glowColor: ModernHudTheme.AccentGlow * 0.35f);

            SpriteFont font = GraphicsManager.GetUiFont(13f, out float scale) ?? GraphicsManager.Instance.Font;
            if (font != null)
            {
                const string title = "SELECT SERVER";
                Vector2 titleSize = font.MeasureString(title) * scale;
                sprite.DrawString(font, title,
                    new Vector2(rect.X + (rect.Width - titleSize.X) * 0.5f, rect.Y + 12),
                    ModernHudTheme.TextGold, 0f, Vector2.Zero, scale,
                    SpriteEffects.None, 0f);
            }

            for (int i = 0; i < _serverButtons.Count; i++)
                _serverButtons[i].Draw(gameTime);
        }
    }
}
