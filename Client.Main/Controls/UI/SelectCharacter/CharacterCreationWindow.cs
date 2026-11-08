using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>
    /// Modern character creation: class emblems (the selected one on a rotating rune sigil), class name and
    /// description, the name field with inline validation, Cancel / Create. Takes the roster's place on the right
    /// while the scene shows a 3D preview of the selected class. Enter creates, Escape cancels.
    /// </summary>
    public sealed class CharacterCreationWindow : OrnateWindowControl
    {
        private const int WindowWidth = 380, WindowHeight = 560;
        private const int EmblemSize = 64, EmblemGap = 18, EmblemRowTop = 98, EmblemRowStep = 82;
        private const int FieldX = 28, FieldY = 418, FieldHeight = 38;
        private const int HitCreate = FirstHitId, HitCancel = FirstHitId + 1, HitClass = FirstHitId + 10;

        private readonly TextFieldControl _nameField;
        private int _selected;
        private string _error;
        private float _time;

        public CharacterCreationWindow() : base(new Point(WindowWidth, WindowHeight))
        {
            Title = "CREATE CHARACTER";
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 34 };

            _nameField = TextFieldControl.Create();
            OrnateWidgets.StyleTextField(_nameField, 14f);
            _nameField.AutoViewSize = false;
            _nameField.X = FieldX + 10;
            _nameField.Y = FieldY + 1;
            _nameField.ViewSize = new Point(WindowWidth - FieldX * 2 - 20, FieldHeight - 2);
            _nameField.ControlSize = _nameField.ViewSize;
            _nameField.ValueChanged += OnNameChanged;
            _nameField.EnterKeyPressed += (_, _) => TryCreate();
            Controls.Add(_nameField);
        }

        public event EventHandler<(string Name, CharacterClassNumber Class)> CreateRequested;
        public event EventHandler CancelRequested;

        /// <summary>Raised when the selected class changes (the scene updates the 3D preview).</summary>
        public event EventHandler SelectionChanged;

        public CharacterClasses.Info SelectedClass => CharacterClasses.Creatable[_selected];

        /// <summary>Gives keyboard focus to the name field; call once the window is shown.</summary>
        public void FocusName() => _nameField.Focus();

        private static readonly string NamePlaceholder =
            $"{CharacterClasses.MinNameLength}-{CharacterClasses.MaxNameLength} letters or digits";

        protected override IEnumerable<string> PreloadPaths =>
            CharacterClasses.Creatable.Select(c => "Icons/" + c.Emblem).Append("Controls/loading_sigil");

        // --- Layout -------------------------------------------------------------------------------------

        private Rectangle EmblemBounds(int index)
        {
            int perRow = 4;
            int row = index / perRow;
            int inRow = row == 0 ? perRow : CharacterClasses.Creatable.Count - perRow;
            int col = index % perRow;
            int rowWidth = inRow * EmblemSize + (inRow - 1) * EmblemGap;
            int x = (WindowWidth - rowWidth) / 2 + col * (EmblemSize + EmblemGap);
            return At(x, EmblemRowTop + row * EmblemRowStep, EmblemSize, EmblemSize);
        }

        private Rectangle FieldBounds => At(FieldX, FieldY, WindowWidth - FieldX * 2, FieldHeight);
        private Rectangle CancelBounds => At(FieldX, WindowHeight - 76, 130, 40);
        private Rectangle CreateBounds => At(WindowWidth - FieldX - 182, WindowHeight - 80, 182, 46);

        // --- Input --------------------------------------------------------------------------------------

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _time += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            KeyboardState keys = MuGame.Instance.Keyboard;
            if (keys.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override int HitTest(Point position)
        {
            if (CreateBounds.Contains(position)) return HitCreate;
            if (CancelBounds.Contains(position)) return HitCancel;
            for (int i = 0; i < CharacterClasses.Creatable.Count; i++)
                if (EmblemBounds(i).Contains(position))
                    return HitClass + i;
            return HitNone;
        }

        protected override void OnActivate(int hit)
        {
            if (hit == HitCreate)
            {
                PlayClickSound();
                TryCreate();
            }
            else if (hit == HitCancel)
            {
                PlayClickSound();
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (hit >= HitClass && hit - HitClass != _selected)
            {
                PlayClickSound();
                _selected = hit - HitClass;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnNameChanged(object sender, EventArgs e)
        {
            string sanitized = CharacterClasses.SanitizeName(_nameField.Value);
            if (!string.Equals(sanitized, _nameField.Value, StringComparison.Ordinal))
                _nameField.Value = sanitized;
            _error = null;
        }

        private void TryCreate()
        {
            string name = _nameField.Value?.Trim() ?? string.Empty;
            _error = CharacterClasses.ValidateName(name);
            if (_error == null)
                CreateRequested?.Invoke(this, (name, SelectedClass.Class));
        }

        // --- Drawing ------------------------------------------------------------------------------------

        protected override void DrawContent(SpriteBatch sb, float a)
        {
            OrnateWidgets.SectionHeader(sb, At(24, 58, WindowWidth - 48, 30), "Class", a);
            for (int i = 0; i < CharacterClasses.Creatable.Count; i++)
                DrawEmblem(sb, i, a);

            CharacterClasses.Info info = SelectedClass;
            OrnateDraw.TextCentered(sb, info.Name, At(0, 262, WindowWidth, 30), 20f, OrnateStyle.Gold, a, bold: true);
            string[] lines = info.Description.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                OrnateDraw.TextCentered(sb, lines[i], At(20, 296 + i * 20, WindowWidth - 40, 20), 12.5f, OrnateStyle.TextMuted, a);

            OrnateWidgets.SectionHeader(sb, At(24, 378, WindowWidth - 48, 30), "Name", a);
            OrnateWidgets.InputFrame(sb, FieldBounds, _nameField.IsFocused, _error != null, a);
            if (string.IsNullOrEmpty(_nameField.Value))
                OrnateDraw.TextLeft(sb, NamePlaceholder, new Rectangle(FieldBounds.X + 20, FieldBounds.Y, FieldBounds.Width - 32, FieldBounds.Height), 13f, OrnateStyle.TextDisabled, a);
            if (_error != null)
                OrnateDraw.TextLeft(sb, _error, At(FieldX + 2, FieldY + FieldHeight + 4, WindowWidth - FieldX * 2, 20), 12f, OrnateStyle.Danger, a);

            OrnateWidgets.Button(sb, CancelBounds, "Cancel", new WidgetState(Clicks.IsHover(HitCancel), Clicks.IsPressed(HitCancel)), a);
            OrnateWidgets.PrimaryButton(sb, CreateBounds, "CREATE", new WidgetState(Clicks.IsHover(HitCreate), Clicks.IsPressed(HitCreate)), a);
        }

        private void DrawEmblem(SpriteBatch sb, int index, float a)
        {
            Rectangle rect = EmblemBounds(index);
            bool selected = index == _selected;
            bool hover = Clicks.IsHover(HitClass + index);

            if (selected)
            {
                Texture2D sigil = OrnateAssets.LoadingSigil;
                if (sigil != null)
                {
                    var origin = new Vector2(sigil.Width / 2f, sigil.Height / 2f);
                    float scale = (EmblemSize + 30f) / sigil.Width;
                    sb.Draw(sigil, rect.Center.ToVector2(), null, Color.White * (0.9f * a), _time * 0.5f, origin, scale, SpriteEffects.None, 0f);
                }
            }

            int grow = selected ? 4 : hover ? 2 : 0;
            rect.Inflate(grow, grow);
            float alpha = selected || hover ? 1f : 0.62f;
            OrnateDraw.Icon(sb, CharacterClasses.Creatable[index].Emblem, rect, alpha * a);
        }
    }
}
