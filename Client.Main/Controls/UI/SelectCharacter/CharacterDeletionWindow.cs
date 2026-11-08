using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>
    /// Modern confirmation for deleting a character: modal window with the character's card, a warning and the
    /// account security code (masked). Enter confirms, Escape cancels.
    /// </summary>
    public sealed class CharacterDeletionWindow : OrnateWindowControl
    {
        private const int WindowWidth = 440, WindowHeight = 340;
        private const int FieldX = 30, FieldY = 206, FieldHeight = 38;
        private const int HitDelete = FirstHitId, HitCancel = FirstHitId + 1;

        private readonly CharacterRosterWindow.Slot _character;
        private readonly TextFieldControl _codeField;

        public CharacterDeletionWindow(CharacterRosterWindow.Slot character) : base(new Point(WindowWidth, WindowHeight))
        {
            _character = character;
            Title = "DELETE CHARACTER";
            ModalDim = 0.55f;

            _codeField = TextFieldControl.Create();
            OrnateWidgets.StyleTextField(_codeField, 14f);
            _codeField.MaskValue = true;
            _codeField.AutoViewSize = false;
            _codeField.X = FieldX + 10;
            _codeField.Y = FieldY + 1;
            _codeField.ViewSize = new Point(WindowWidth - FieldX * 2 - 20, FieldHeight - 2);
            _codeField.ControlSize = _codeField.ViewSize;
            _codeField.EnterKeyPressed += (_, _) => Confirm();
            Controls.Add(_codeField);
        }

        public event EventHandler<string> DeleteConfirmed;
        public event EventHandler CancelRequested;

        protected override IEnumerable<string> PreloadPaths => new[] { "Emblems/" + CharacterClasses.EmblemFor(_character.Class) };

        public void FocusCode() => _codeField.Focus();

        private Rectangle CardBounds => At(36, 80, WindowWidth - 72, 50);
        private Rectangle FieldBounds => At(FieldX, FieldY, WindowWidth - FieldX * 2, FieldHeight);
        private Rectangle CancelBounds => At(FieldX, WindowHeight - 74, 150, 40);
        private Rectangle DeleteBounds => At(WindowWidth - FieldX - 182, WindowHeight - 78, 182, 46);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            KeyboardState keys = MuGame.Instance.Keyboard;
            if (keys.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override int HitTest(Point position) =>
            DeleteBounds.Contains(position) ? HitDelete : CancelBounds.Contains(position) ? HitCancel : HitNone;

        protected override void OnActivate(int hit)
        {
            PlayClickSound();
            if (hit == HitDelete)
                Confirm();
            else if (hit == HitCancel)
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Confirm() => DeleteConfirmed?.Invoke(this, _codeField.Value?.Trim() ?? string.Empty);

        protected override void DrawContent(SpriteBatch sb, float a)
        {
            Rectangle card = CardBounds;
            Rectangle socket = OrnateWidgets.Card(sb, card, new WidgetState(Selected: true), a);
            OrnateWidgets.SocketEmblem(sb, CharacterClasses.EmblemFor(_character.Class), socket, a);
            var text = new Rectangle(socket.Right + 14, card.Y, card.Right - socket.Right - 26, card.Height);
            OrnateDraw.TextLeft(sb, _character.Name, new Rectangle(text.X, card.Y + 4, text.Width, card.Height / 2 - 2), 16f, OrnateStyle.Gold, a, bold: true);
            OrnateDraw.TextLeft(sb, CharacterClassDatabase.GetClassName(_character.Class), new Rectangle(text.X, card.Center.Y, text.Width, card.Height / 2 - 4), 12f, OrnateStyle.GoldDim, a);
            OrnateDraw.TextRight(sb, $"Lv. {_character.Level}", new Rectangle(text.X, card.Y, text.Width - 6, card.Height), 14f, Color.White, a, bold: true);

            OrnateDraw.TextCentered(sb, "This character will be deleted permanently.", At(0, 150, WindowWidth, 20), 13f, OrnateStyle.Danger, a);
            OrnateDraw.TextCentered(sb, "Enter your account security code to confirm.", At(0, 172, WindowWidth, 20), 12.5f, OrnateStyle.TextMuted, a);
            OrnateWidgets.InputFrame(sb, FieldBounds, _codeField.IsFocused, false, a);
            if (string.IsNullOrEmpty(_codeField.Value))
                OrnateDraw.TextLeft(sb, "Security code", new Rectangle(FieldBounds.X + 20, FieldBounds.Y, FieldBounds.Width - 32, FieldBounds.Height), 13f, OrnateStyle.TextDisabled, a);

            OrnateWidgets.Button(sb, CancelBounds, "Cancel", new WidgetState(Clicks.IsHover(HitCancel), Clicks.IsPressed(HitCancel)), a);
            OrnateWidgets.PrimaryButton(sb, DeleteBounds, "DELETE", new WidgetState(Clicks.IsHover(HitDelete), Clicks.IsPressed(HitDelete)), a);
        }
    }
}
