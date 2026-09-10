using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Client.Main.Controls.UI
{
    public abstract class PopupFieldDialog : DialogControl, IUiTexturePreloadable
    {
        private Texture2D _cornerTopLeftTexture;
        private Texture2D _topLineTexture;
        private Texture2D _cornerTopRightTexture;
        private Texture2D _leftLineTexture;
        private Texture2D _backgroundTexture;
        private Texture2D _rightLineTexture;
        private Texture2D _cornerBottomLeftTexture;
        private Texture2D _bottomLineTexture;
        private Texture2D _cornerBottomRightTexture;
        private bool _useFallbackFrame;
        private Task _classicFrameLoadTask;
        private readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<PopupFieldDialog>();
        private static readonly string[] s_popupTextureSuffixes =
        {
            "01","02","03","04","05","06","07","08","09"
        };

        public override async Task Load()
        {
            await base.Load();

            if (UiThemeManager.CurrentId == UiThemeId.Modern)
                await EnsureClassicFrameLoadedAsync();
            else
                _useFallbackFrame = true;
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            if (UiThemeManager.CurrentId == UiThemeId.Modern)
            {
                _useFallbackFrame = true;
                if (Status == GameControlStatus.Ready)
                    _ = EnsureClassicFrameLoadedAsync();
            }
        }

        private Task EnsureClassicFrameLoadedAsync()
        {
            return _classicFrameLoadTask ??= LoadClassicFrameAsync();
        }

        private async Task LoadClassicFrameAsync()
        {
            async Task<Texture2D> Load(string path)
            {
                try
                {
                    return await UiThemeManager.LoadThemeTextureAsync(path);
                }
                catch
                {
                    return null;
                }
            }

            _cornerTopLeftTexture = await Load("Interface/GFx/popupfield01.ozd");
            _topLineTexture = await Load("Interface/GFx/popupfield02.ozd");
            _cornerTopRightTexture = await Load("Interface/GFx/popupfield03.ozd");
            _leftLineTexture = await Load("Interface/GFx/popupfield04.ozd");
            _backgroundTexture = await Load("Interface/GFx/popupfield05.ozd");
            _rightLineTexture = await Load("Interface/GFx/popupfield06.ozd");
            _cornerBottomLeftTexture = await Load("Interface/GFx/popupfield07.ozd");
            _bottomLineTexture = await Load("Interface/GFx/popupfield08.ozd");
            _cornerBottomRightTexture = await Load("Interface/GFx/popupfield09.ozd");

            _useFallbackFrame = _cornerTopLeftTexture == null || _topLineTexture == null ||
                                _cornerTopRightTexture == null || _leftLineTexture == null ||
                                _backgroundTexture == null || _rightLineTexture == null ||
                                _cornerBottomLeftTexture == null || _bottomLineTexture == null ||
                                _cornerBottomRightTexture == null;
            if (_useFallbackFrame)
                _logger?.LogWarning("PopupFieldDialog frame textures missing. Using fallback flat background.");
        }

        public IEnumerable<string> GetPreloadTexturePaths()
        {
            if (UiThemeManager.CurrentId != UiThemeId.Modern)
                yield break;

            const string basePath = "Interface/GFx/popupfield";
            for (int i = 0; i < s_popupTextureSuffixes.Length; i++)
            {
                yield return basePath + s_popupTextureSuffixes[i] + ".ozd";
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sprite = GraphicsManager.Instance.Sprite;
            var rect = DisplayRectangle;

            if (UiThemeManager.CurrentId == UiThemeId.Classic)
            {
                UiDrawHelper.DrawPanel(
                    sprite,
                    rect,
                    ModernHudTheme.BgDark * 0.98f,
                    ModernHudTheme.BorderInner,
                    ModernHudTheme.BorderOuter,
                    ModernHudTheme.BorderHighlight,
                    withGlow: true,
                    glowColor: ModernHudTheme.AccentGlow * 0.4f);
            }
            else if (_useFallbackFrame)
            {
                var bgColor = new Color(0, 0, 0, 200);
                sprite.Draw(GraphicsManager.Instance.Pixel, rect, bgColor);

                var borderColor = new Color(255, 255, 255, 160);
                sprite.Draw(GraphicsManager.Instance.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), borderColor);
                sprite.Draw(GraphicsManager.Instance.Pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), borderColor);
                sprite.Draw(GraphicsManager.Instance.Pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), borderColor);
                sprite.Draw(GraphicsManager.Instance.Pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), borderColor);
            }
            else
            {
                sprite.Draw(_cornerTopLeftTexture, new Rectangle(rect.X, rect.Y, _cornerTopLeftTexture.Width, _cornerTopLeftTexture.Height), Color.White);
                sprite.Draw(_cornerTopRightTexture, new Rectangle(rect.X + rect.Width - _cornerTopRightTexture.Width, rect.Y, _cornerTopRightTexture.Width, _cornerTopRightTexture.Height), Color.White);
                sprite.Draw(_cornerBottomLeftTexture, new Rectangle(rect.X, rect.Y + rect.Height - _cornerBottomLeftTexture.Height, _cornerBottomLeftTexture.Width, _cornerBottomLeftTexture.Height), Color.White);
                sprite.Draw(_cornerBottomRightTexture, new Rectangle(rect.X + rect.Width - _cornerBottomRightTexture.Width, rect.Y + rect.Height - _cornerBottomRightTexture.Height, _cornerBottomRightTexture.Width, _cornerBottomRightTexture.Height), Color.White);

                sprite.Draw(_topLineTexture, new Rectangle(rect.X + _cornerTopLeftTexture.Width, rect.Y, rect.Width - _cornerTopLeftTexture.Width - _cornerTopRightTexture.Width, _topLineTexture.Height), Color.White);
                sprite.Draw(_bottomLineTexture, new Rectangle(rect.X + _cornerBottomLeftTexture.Width, rect.Y + rect.Height - _bottomLineTexture.Height, rect.Width - _cornerBottomLeftTexture.Width - _cornerBottomRightTexture.Width, _bottomLineTexture.Height), Color.White);
                sprite.Draw(_leftLineTexture, new Rectangle(rect.X, rect.Y + _cornerTopLeftTexture.Height, _leftLineTexture.Width, rect.Height - _cornerTopLeftTexture.Height - _cornerBottomLeftTexture.Height), Color.White);
                sprite.Draw(_rightLineTexture, new Rectangle(rect.X + rect.Width - _rightLineTexture.Width, rect.Y + _cornerTopRightTexture.Height, _rightLineTexture.Width, rect.Height - _cornerTopRightTexture.Height - _cornerBottomRightTexture.Height), Color.White);

                sprite.Draw(_backgroundTexture,
                            new Rectangle(
                                rect.X + _leftLineTexture.Width,
                                rect.Y + _topLineTexture.Height,
                                rect.Width - _leftLineTexture.Width - _rightLineTexture.Width,
                                rect.Height - _topLineTexture.Height - _bottomLineTexture.Height),
                            Color.White);
            }

            base.Draw(gameTime);
        }
    }
}
