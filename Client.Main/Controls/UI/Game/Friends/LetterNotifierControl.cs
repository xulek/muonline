#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Unread letter indicator. Like the friend button of the original main frame
    /// (NewUIMainFrameWindow.cpp RenderFriendButtonState) it blinks after a new letter arrived
    /// (SetNewMailAlert) and stays lit while unread letters exist. A click opens the letter box.
    /// </summary>
    public sealed class LetterNotifierControl : UIControl
    {
        private const int Size = 34;
        private readonly FriendManager _friends;
        private bool _alert;
        private bool _hovered;

        public LetterNotifierControl(FriendManager friends)
        {
            _friends = friends;
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(Size + 10, Size);
            Align = ControlAlign.Top | ControlAlign.Right;
            Margin = new Margin { Top = 220, Right = 12 };
            _friends.NewLetterReceived += OnNewLetter;
        }

        public override void Dispose()
        {
            _friends.NewLetterReceived -= OnNewLetter;
            base.Dispose();
        }

        private void OnNewLetter(LetterEntry letter) => _alert = true;

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            int unread = _friends.UnreadLetterCount;
            if (unread == 0) _alert = false;
            Visible = unread > 0;
            if (!Visible) return;

            var mouse = MuGame.Instance.UiMouseState;
            _hovered = DisplayRectangle.Contains(mouse.Position);
            if (_hovered && mouse.LeftButton == ButtonState.Pressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released)
            {
                Scene?.SetMouseInputConsumed();
                _alert = false;
                (Scene as Scenes.GameScene)?.FriendList?.ShowLetters();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            // Blink cycle of the original: lit for 12 of 24 ticks.
            bool lit = !_alert || (int)(gameTime.TotalGameTime.TotalSeconds * 4) % 2 == 0;
            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                var box = new Rectangle(DisplayRectangle.X, DisplayRectangle.Y, Size, Size);
                PlayerShopUi.DrawPanel(sb, box, _hovered ? ModernHudTheme.BgLight : ModernHudTheme.BgDark, Alpha);
                Color ink = (lit ? ModernHudTheme.TextGold : ModernHudTheme.TextGray) * Alpha;

                // Envelope: frame plus the two flap lines.
                var env = new Rectangle(box.X + 7, box.Y + 10, box.Width - 14, box.Height - 20);
                Client.Main.Controls.UI.Common.UiDrawHelper.DrawBorder(sb, env, ink);
                int half = env.Width / 2;
                for (int i = 0; i <= half; i++)
                {
                    int y = env.Y + i * (env.Height - 2) / half;
                    sb.Draw(pixel, new Rectangle(env.X + i, y, 1, 1), ink);
                    sb.Draw(pixel, new Rectangle(env.Right - 1 - i, y, 1, 1), ink);
                }

                string count = _friends.UnreadLetterCount.ToString();
                var badge = new Rectangle(box.Right - 6, box.Y - 2, Math.Max(14, (int)PlayerShopUi.Measure(count, 10f).X + 6), 14);
                sb.Draw(pixel, badge, ModernHudTheme.Danger * Alpha);
                PlayerShopUi.DrawTextCentered(sb, count, badge, ModernHudTheme.TextWhite, 10f, Alpha);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }
    }
}
