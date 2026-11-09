namespace Client.Main.Controls.UI
{
    /// <summary>
    /// A control that reads typed keys itself (an amount field drawn by a window, not a <see cref="TextFieldControl"/>).
    /// While <see cref="IsCapturingKeyboard"/> is true, game hotkeys and hotbar keys ignore the keyboard
    /// (see <see cref="Scenes.BaseScene.IsTextInputActive"/>).
    /// </summary>
    public interface IKeyboardCapture
    {
        bool IsCapturingKeyboard { get; }
    }
}
