using Client.Main.Models;
using Client.Main.Controls.UI.Common;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controls.UI.Login
{
    public class LoginDialog : PopupFieldDialog
    {
        // Fields
        private readonly TextureControl _line1;
        private readonly TextureControl _line2;
        private readonly LabelControl _titleLabel;
        private readonly LabelControl _userLabel;
        private readonly LabelControl _passwordLabel;
        private readonly TextFieldControl _userInput;
        private readonly LabelControl _serverNameLabel;
        private readonly TextFieldControl _passwordInput;
        private readonly OkButton _okButton;
        private readonly ButtonControl _classicOkButton;
        private readonly ButtonControl _classicCancelButton;

        // Properties
        public string ServerName
        {
            get => _serverNameLabel.Text;
            set => _serverNameLabel.Text = value;
        }

        /// <summary>
        /// Gets the username entered in the text field.
        /// </summary>
        public string Username => _userInput.Value;

        /// <summary>
        /// Gets the password entered in the text field.
        /// </summary>
        public string Password => _passwordInput.Value;

        // Events
        /// <summary>
        /// Invoked when the user confirms login (clicks OK or presses Enter in the password field).
        /// </summary>
        public event EventHandler LoginAttempt;
        public event EventHandler CancelAttempt;

        // Constructors
        public LoginDialog()
        {
            ControlSize = new Point(300, 200);
            Interactive = true;

            Controls.Add(_titleLabel = new LabelControl
            {
                Text = "MU Online",
                Align = ControlAlign.HorizontalCenter,
                Y = 15,
                FontSize = 12
            });

            Controls.Add(_line1 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 40,
                AutoViewSize = false
            });

            Controls.Add(_serverNameLabel = new LabelControl
            {
                Text = "OpenMU Server 1",
                Align = ControlAlign.HorizontalCenter,
                Y = 55,
                FontSize = 12,
                TextColor = ModernHudTheme.TextGold
            });

            Controls.Add(_userLabel = new LabelControl
            {
                Text = "User",
                Y = 90,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            });

            Controls.Add(_passwordLabel = new LabelControl
            {
                Text = "Password",
                Y = 120,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            });

            Controls.Add(_line2 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 150,
                AutoViewSize = false,
                Alpha = 0.7f
            });

            _userInput = TextFieldControl.Create();
            _userInput.X = 100;
            _userInput.Y = 87;
            _userInput.Skin = TextFieldSkin.NineSlice;

            _passwordInput = TextFieldControl.Create();
            _passwordInput.X = 100;
            _passwordInput.Y = 117;
            _passwordInput.MaskValue = true;
            _passwordInput.Skin = TextFieldSkin.NineSlice;

            _passwordInput.ValueChanged += PasswordInput_EnterPressed; // Use dedicated method
            Controls.Add(_userInput);
            Controls.Add(_passwordInput);

            _userInput.Click += (s, e) => { _userInput.OnFocus(); _passwordInput.OnBlur(); };
            _passwordInput.Click += (s, e) => { _passwordInput.OnFocus(); _userInput.OnBlur(); };

            _okButton = new OkButton
            {
                Y = 160,
                Align = ControlAlign.HorizontalCenter
            };
            _okButton.Click += OkButton_Click; // Use dedicated method
            Controls.Add(_okButton);

            _classicOkButton = CreateClassicButton("OK", ModernHudTheme.Success);
            _classicOkButton.Click += OkButton_Click;
            Controls.Add(_classicOkButton);

            _classicCancelButton = CreateClassicButton("CANCEL", ModernHudTheme.BgLight);
            _classicCancelButton.Click += CancelButton_Click;
            Controls.Add(_classicCancelButton);

            ApplyThemeLayout();
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private static ButtonControl CreateClassicButton(string text, Color color) => new()
        {
            Text = text,
            AutoViewSize = false,
            FontSize = 12f,
            BackgroundColor = color,
            HoverBackgroundColor = Color.Lerp(color, Color.White, 0.15f),
            PressedBackgroundColor = Color.Lerp(color, Color.Black, 0.15f),
            TextColor = ModernHudTheme.TextWhite,
            HoverTextColor = ModernHudTheme.TextWhite,
            BorderColor = ModernHudTheme.BorderInner,
            BorderThickness = 1,
            Interactive = true
        };

        private void ApplyThemeLayout()
        {
            bool classic = !LoginUiTheme.UseModernLayout;
            ControlSize = classic
                ? new Point(LoginLayout.PanelWidth, LoginLayout.PanelHeight)
                : new Point(300, 200);

            _serverNameLabel.TextColor = classic ? ModernHudTheme.TextGold : new Color(241, 188, 37);
            _titleLabel.TextColor = classic ? ModernHudTheme.TextWhite : Color.WhiteSmoke;
            _userLabel.TextColor = classic ? ModernHudTheme.TextWhite : Color.WhiteSmoke;
            _passwordLabel.TextColor = classic ? ModernHudTheme.TextWhite : Color.WhiteSmoke;
            _userInput.TextColor = classic ? ModernHudTheme.TextWhite : Color.White;
            _passwordInput.TextColor = classic ? ModernHudTheme.TextWhite : Color.White;
            _userInput.Interactive = true;
            _passwordInput.Interactive = true;

            if (classic)
            {
                _line1.X = 10;
                _line1.Y = LoginLayout.LineTopY;
                _line2.X = 10;
                _line2.Y = LoginLayout.LineBottomY;
                _userLabel.X = LoginLayout.UserLabelX;
                _userLabel.Y = LoginLayout.UserLabelY;
                _passwordLabel.X = LoginLayout.PasswordLabelX;
                _passwordLabel.Y = LoginLayout.PasswordLabelY;
                _userInput.X = LoginLayout.UserInputX;
                _userInput.Y = LoginLayout.UserInputY;
                _passwordInput.X = LoginLayout.PasswordInputX;
                _passwordInput.Y = LoginLayout.PasswordInputY;
                _userInput.ViewSize = new Point(LoginLayout.InputWidth, LoginLayout.InputHeight);
                _passwordInput.ViewSize = new Point(LoginLayout.InputWidth, LoginLayout.InputHeight);
                _userInput.Skin = TextFieldSkin.Flat;
                _passwordInput.Skin = TextFieldSkin.Flat;
                _userInput.BackgroundColor = new Color(12, 12, 16, 235);
                _passwordInput.BackgroundColor = new Color(12, 12, 16, 235);
                _userInput.BorderColor = ModernHudTheme.BorderInner;
                _passwordInput.BorderColor = ModernHudTheme.BorderInner;
                _userInput.BorderThickness = 1;
                _passwordInput.BorderThickness = 1;
                _classicOkButton.X = LoginLayout.OkX;
                _classicOkButton.Y = LoginLayout.OkY;
                _classicOkButton.ViewSize = new Point(LoginLayout.OkWidth, LoginLayout.OkHeight);
                _classicCancelButton.X = LoginLayout.CancelX;
                _classicCancelButton.Y = LoginLayout.CancelY;
                _classicCancelButton.ViewSize = new Point(LoginLayout.CancelWidth, LoginLayout.CancelHeight);
            }
            else
            {
                _line1.X = 10;
                _line1.Y = 40;
                _line2.X = 10;
                _line2.Y = 150;
                _userLabel.X = 20;
                _userLabel.Y = 90;
                _passwordLabel.X = 20;
                _passwordLabel.Y = 120;
                _userInput.X = 100;
                _userInput.Y = 87;
                _passwordInput.X = 100;
                _passwordInput.Y = 117;
                _userInput.ViewSize = new Point(176, 14);
                _passwordInput.ViewSize = new Point(176, 14);
                _userInput.Skin = TextFieldSkin.NineSlice;
                _passwordInput.Skin = TextFieldSkin.NineSlice;
            }

            _okButton.Visible = !classic;
            _okButton.Interactive = !classic;
            _classicOkButton.Visible = classic;
            _classicOkButton.Interactive = classic;
            _classicCancelButton.Visible = classic;
            _classicCancelButton.Interactive = classic;
            _line1.Visible = !classic;
            _line2.Visible = !classic;
            _line1.ViewSize = new Point(Math.Max(1, ControlSize.X - 20), 8);
            _line2.ViewSize = new Point(Math.Max(1, ControlSize.X - 20), 5);
        }

        public override void Draw(GameTime gameTime)
        {
            if (LoginUiTheme.UseModernLayout)
            {
                base.Draw(gameTime);
                return;
            }

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            SpriteBatch sprite = GraphicsManager.Instance.Sprite;
            Rectangle rect = DisplayRectangle;
            UiDrawHelper.DrawPanel(sprite, rect, ModernHudTheme.BgDark * 0.98f,
                ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                ModernHudTheme.BorderHighlight, withGlow: true,
                glowColor: ModernHudTheme.AccentGlow * 0.45f);

            Rectangle header = new(rect.X + 10, rect.Y + 39, rect.Width - 20, 1);
            sprite.Draw(GraphicsManager.Instance.Pixel, header, ModernHudTheme.Accent * 0.7f);
            Rectangle separator = new(rect.X + 10, rect.Y + LoginLayout.LineBottomY, rect.Width - 20, 1);
            sprite.Draw(GraphicsManager.Instance.Pixel, separator, ModernHudTheme.BorderInner * 0.8f);

            _titleLabel.Draw(gameTime);
            _serverNameLabel.Draw(gameTime);
            _userLabel.Draw(gameTime);
            _passwordLabel.Draw(gameTime);
            _userInput.Draw(gameTime);
            _passwordInput.Draw(gameTime);
            _classicOkButton.Draw(gameTime);
            _classicCancelButton.Draw(gameTime);
        }

        // Public Methods
        /// <summary>
        /// Sets focus on the username field (called from the scene).
        /// </summary>
        public void FocusUsername()
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                Scene?.FocusControlIfInteractive(_userInput);
                _userInput?.OnFocus();
                _passwordInput?.OnBlur();
            });
        }

        public override void Update(GameTime gameTime)
        {
            // Handle Tab key to switch focus between input fields
            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Tab) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Tab))
            {
                if (_userInput.IsFocused)
                {
                    _userInput.OnBlur();
                    _passwordInput.OnFocus();
                }
                else if (_passwordInput.IsFocused)
                {
                    _passwordInput.OnBlur();
                    _userInput.OnFocus();
                }
            }
            base.Update(gameTime);
        }

        // Protected Methods
        protected override void OnScreenSizeChanged()
        {
            ApplyThemeLayout();
            base.OnScreenSizeChanged();
        }

        // Private Methods
        // Method called after clicking the OK button
        private void OkButton_Click(object sender, EventArgs e)
        {
            AttemptLogin();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            _userInput.OnBlur();
            _passwordInput.OnBlur();
            CancelAttempt?.Invoke(this, EventArgs.Empty);
        }

        // Method called after pressing Enter in the password field
        private void PasswordInput_EnterPressed(object sender, EventArgs e)
        {
            // ValueChanged is also invoked on text change,
            // so we check if Enter was just pressed.
            bool enterPressed = MuGame.Instance.Keyboard.IsKeyDown(Keys.Enter) &&
                                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Enter);

            if (enterPressed)
            {
                AttemptLogin();
            }
        }

        // Invokes the LoginAttempt event
        private void AttemptLogin()
        {
            // Blur fields to hide soft keyboard (especially on mobile) after submitting.
            _userInput.OnBlur();
            _passwordInput.OnBlur();
            if (Scene != null && (Scene.FocusControl == _userInput || Scene.FocusControl == _passwordInput))
            {
                Scene.FocusControl = null; // keep focus cleared so keyboard stays hidden
            }

            LoginAttempt?.Invoke(this, EventArgs.Empty);
        }
    }
}
