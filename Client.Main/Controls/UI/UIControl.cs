using Client.Main;
using Client.Main.Models;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI
{
    public abstract class UIControl : GameControl
    {
        protected override MouseState CurrentMouseState => MuGame.Instance.UiMouseState;
        protected override MouseState PreviousMouseState => MuGame.Instance.PrevUiMouseState;

        public UIControl()
        {
            UiThemeManager.ThemeChanged += HandleThemeChanged;
        }

        private void HandleThemeChanged(object sender, UiThemeChangedEventArgs e)
        {
            if (Status == GameControlStatus.Disposed)
                return;

            MarkLayoutDirty();
            OnThemeChanged(e);
        }

        protected virtual void OnThemeChanged(UiThemeChangedEventArgs e)
        {
        }

        public override void Dispose()
        {
            UiThemeManager.ThemeChanged -= HandleThemeChanged;
            base.Dispose();
        }
    }
}
