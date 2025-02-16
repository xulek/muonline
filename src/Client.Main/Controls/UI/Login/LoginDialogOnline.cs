using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controls.UI.Login
{
    public class LoginDialogOnline : PopupFieldDialog
    {
        private TextureControl _line1;
        private TextureControl _line2;
        private TextFieldControl _userInput;
        private LabelControl _serverNameLabel;
        private TextFieldControl _passwordInput;

        // Event raised when the login button is clicked
        public event EventHandler LoginButtonClicked;

        public string ServerName
        {
            get => _serverNameLabel.Text;
            set => _serverNameLabel.Text = value;
        }

        // Properties to retrieve user input
        public string Username => _userInput.Value;
        public string Password => _passwordInput.Value;

        public LoginDialogOnline()
        {
            ControlSize = new Point(300, 200);
            InitializeControls();
            _userInput.OnFocus();
        }

        private void InitializeControls()
        {
            // Create and add title label
            var titleLabel = new LabelControl
            {
                Text = "MU Online",
                Align = ControlAlign.HorizontalCenter,
                Y = 15,
                FontSize = 12
            };
            Controls.Add(titleLabel);

            // Create and add first separator line
            _line1 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 40,
                AutoViewSize = false
            };
            Controls.Add(_line1);

            // Create and add server name label
            _serverNameLabel = new LabelControl
            {
                Text = "OpenMU Server 1",
                Align = ControlAlign.HorizontalCenter,
                Y = 55,
                FontSize = 12,
                TextColor = new Color(241, 188, 37)
            };
            Controls.Add(_serverNameLabel);

            // Create and add user label
            var userLabel = new LabelControl
            {
                Text = "User",
                Y = 90,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            };
            Controls.Add(userLabel);

            // Create and add password label
            var passwordLabel = new LabelControl
            {
                Text = "Password",
                Y = 120,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            };
            Controls.Add(passwordLabel);

            // Create and add second separator line
            _line2 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 150,
                AutoViewSize = false,
                Alpha = 0.7f
            };
            Controls.Add(_line2);

            // Create and add text fields
            _userInput = new TextFieldControl { X = 100, Y = 87 };
            Controls.Add(_userInput);

            _passwordInput = new TextFieldControl { X = 100, Y = 117, MaskValue = true };
            Controls.Add(_passwordInput);

            // Create and add login button
            var button = new OkButton
            {
                Y = 160,
                Align = ControlAlign.HorizontalCenter
            };
            button.Click += OnLoginClick;
            Controls.Add(button);
        }

        // Method triggered when the login button is clicked
        private void OnLoginClick(object sender, EventArgs e)
        {
            // Raise the event to notify that the login button was clicked
            LoginButtonClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnScreenSizeChanged()
        {
            _line1.ViewSize = new Point(DisplaySize.X - 20, 8);
            _line2.ViewSize = new Point(DisplaySize.X - 20, 5);
            base.OnScreenSizeChanged();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            HandleTabNavigation();
        }

        private void HandleTabNavigation()
        {
            // Switch focus between text fields on Tab key press
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
        }
    }
}
