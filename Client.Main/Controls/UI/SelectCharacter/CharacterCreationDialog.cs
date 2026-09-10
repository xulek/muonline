using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>
    /// Dialog for creating a new character with class selection.
    /// </summary>
    public class CharacterCreationDialog : UIControl
    {
        private static class Theme
        {
            // Background layers
            public static Color BgDarkest => ModernHudTheme.BgDarkest;
            public static Color BgDark => ModernHudTheme.BgDark;
            public static Color BgMid => ModernHudTheme.BgMid;
            public static Color BgLight => ModernHudTheme.BgLight;
            public static Color BgLighter => ModernHudTheme.BgLighter;

            // Accent - Warm Gold
            public static Color Accent => ModernHudTheme.Accent;
            public static Color AccentBright => ModernHudTheme.AccentBright;
            public static Color AccentDim => ModernHudTheme.AccentDim;
            public static Color AccentGlow => ModernHudTheme.AccentGlow;

            // Secondary accent - Cool Blue
            public static Color Secondary => ModernHudTheme.Secondary;
            public static Color SecondaryBright => ModernHudTheme.SecondaryBright;
            public static Color SecondaryDim => ModernHudTheme.SecondaryDim;

            // Borders
            public static Color BorderOuter => ModernHudTheme.BorderOuter;
            public static Color BorderInner => ModernHudTheme.BorderInner;
            public static Color BorderHighlight => ModernHudTheme.BorderHighlight;

            // Text
            public static Color TextWhite => ModernHudTheme.TextWhite;
            public static Color TextGold => ModernHudTheme.TextGold;
            public static Color TextGray => ModernHudTheme.TextGray;
            public static Color TextDark => ModernHudTheme.TextDark;

            // Status colors
            public static Color Success => ModernHudTheme.Success;
            public static Color Warning => ModernHudTheme.Warning;
            public static Color Danger => ModernHudTheme.Danger;
        }

        private readonly List<CharacterClassInfo> _availableClasses;
        private int _selectedClassIndex = 0;
        
        private LabelControl _titleLabel;
        private LabelControl _nameLabel;
        private LabelControl _classSectionLabel;
        private TextFieldControl _nameInput;
        private LabelControl _classLabel;
        private LabelControl _classDescriptionLabel;
        private ButtonControl _previousClassButton;
        private ButtonControl _nextClassButton;
        private ButtonControl _createButton;
        private ButtonControl _cancelButton;
        
        // Static surface cache for background (following muonline-ui-design pattern)
        private RenderTarget2D _backgroundSurface;
        private bool _surfaceNeedsRedraw = true;
        private double _bringToFrontTimer = 0;
        private const double BRING_TO_FRONT_INTERVAL = 0.1; // Throttle to every 100ms

        private Texture2D _s6PanelFrame;
        private Texture2D _s6NameDescription;
        private Texture2D _s6NameInput;
        private Texture2D _s6NamePlate;
        private Texture2D _s6OkCancel;
        private Texture2D _s6ClassButton;
        private Texture2D _s6DarkLabel;
        private Texture2D[] _s6Spiders = new Texture2D[6];
        private UiThemeId _loadedTheme = (UiThemeId)(-1);

        private static readonly Rectangle ClassicClassDefaultSource = new(0, 0, 152, 30);
        private static readonly Rectangle ClassicClassHoverSource = new(167, 0, 152, 30);
        private static readonly Rectangle ClassicClassSelectedSource = new(333, 0, 152, 30);
        // The current project asset is a 467x80 sheet with two 233x80 button states.
        private static readonly Rectangle ClassicOkSource = new(0, 0, 233, 80);
        private static readonly Rectangle ClassicOkHoverSource = new(234, 0, 233, 80);
        private static readonly Rectangle ClassicOkLegacySource = new(0, 0, 74, 32);
        private static readonly Rectangle ClassicOkLegacyHoverSource = new(74, 0, 75, 32);
        private static readonly Rectangle ClassicPanelSource = new(0, 0, 344, 358);
        private static readonly Rectangle ClassicDescriptionSource = new(0, 0, 581, 366);
        private static readonly Rectangle ClassicNameInputSource = new(0, 0, 220, 58);
        private static readonly Rectangle ClassicNamePlateSource = new(0, 0, 123, 74);
        private static readonly Rectangle ClassicDarkLabelSource = new(0, 0, 220, 46);
        private static readonly Rectangle ClassicSpiderSource = new(0, 0, 512, 512);

        private readonly List<Rectangle> _classicClassRects = new();
        private Rectangle _classicPanelRect;
        private Rectangle _classicDescriptionRect;
        private Rectangle _classicNameInputRect;
        private Rectangle _classicNamePlateRect;
        private Rectangle _classicNameTextRect;
        private Rectangle _classicDescriptionTextRect;
        private Rectangle _classicAttributeLabelRect;
        private Rectangle _classicSpiderRect;
        private Rectangle _classicOkRect;
        private Rectangle _classicCancelRect;
        private int _classicPressedClass = -1;

        private const float ClassicPanelX = 298.5f;
        private const float ClassicPanelY = 120.7f;
        private const float ClassicPanelW = 392.7f;
        private const float ClassicPanelH = 488.2f;
        private const float ClassicClassCenterX = 373.8f;
        private const float ClassicClassTopY = 302.5f;
        private const float ClassicClassW = 177.8f;
        private const float ClassicClassH = 39.4f;
        private const float ClassicClassPitch = 39.4f;
        private const float ClassicDescriptionX = 18.8f;
        private const float ClassicDescriptionY = -181.2f;
        private const float ClassicDescriptionW = 643.7f;
        private const float ClassicDescriptionH = 416.5f;
        private const float ClassicNameInputX = -37.7f;
        private const float ClassicNameInputY = -66.6f;
        private const float ClassicNameInputW = 300f;
        private const float ClassicNameInputH = 24.7f;
        private const float ClassicNamePlateX = -235f;
        private const float ClassicNamePlateY = -59.9f;
        private const float ClassicNamePlateW = 144.1f;
        private const float ClassicNamePlateH = 85.1f;
        private const float ClassicNameTextX = -58.8f;
        private const float ClassicNameTextY = -66.6f;
        private const float ClassicNameTextW = 193.8f;
        private const float ClassicNameTextH = 25.4f;
        private const float ClassicDescriptionTextX = -37.9f;
        private const float ClassicDescriptionTextY = -204.4f;
        private const float ClassicDescriptionTextW = 316.9f;
        private const float ClassicDescriptionTextH = 203f;
        private const float ClassicAttributeLabelX = 212.2f;
        private const float ClassicAttributeLabelY = 134f;
        private const float ClassicAttributeLabelW = 135.5f;
        private const float ClassicAttributeLabelH = 371.1f;
        private const float ClassicSpiderX = 210.7f;
        private const float ClassicSpiderY = 107.8f;
        private const float ClassicSpiderW = 137.8f;
        private const float ClassicSpiderH = 132.2f;
        private const float ClassicOkX = 224.4f;
        private const float ClassicOkY = -66.2f;
        private const float ClassicOkW = 104.4f;
        private const float ClassicOkH = 41.1f;
        private const float ClassicCancelX = 224.3f;
        private const float ClassicCancelY = -112f;
        private const float ClassicCancelW = 104.4f;
        private const float ClassicCancelH = 41.1f;
        private const float ClassicNamePlateTextDX = -5.3f;
        private const float ClassicNamePlateTextDY = 7.5f;
        
        public event EventHandler<(string Name, CharacterClassNumber Class)> CharacterCreateRequested;
        public event EventHandler CancelRequested;
        public event EventHandler SelectionChanged;

        public CharacterClassNumber SelectedClass =>
            _availableClasses[_selectedClassIndex].Class;

        public string SelectedClassPreviewModel =>
            GetPreviewModel(SelectedClass);

        private struct CharacterClassInfo
        {
            public CharacterClassNumber Class;
            public string Name;
            public string Description;
        }

        public CharacterCreationDialog()
        {
            _availableClasses = new List<CharacterClassInfo>
            {
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.DarkWizard, 
                    Name = "Dark Wizard",
                    Description = "Masters of magical destruction.\nHigh magic damage, low defense."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.DarkKnight, 
                    Name = "Dark Knight",
                    Description = "Warriors of strength and honor.\nHigh health and physical damage."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.FairyElf, 
                    Name = "Fairy Elf",
                    Description = "Agile archers and healers.\nHigh agility, support abilities."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.MagicGladiator, 
                    Name = "Magic Gladiator",
                    Description = "Hybrid warriors with magic.\nBalanced melee and magic skills."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.DarkLord, 
                    Name = "Dark Lord",
                    Description = "Commanders with dark powers.\nSummons pets and commands armies."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.Summoner, 
                    Name = "Summoner",
                    Description = "Mystics with curse powers.\nCurses enemies and summons."
                },
                new CharacterClassInfo 
                { 
                    Class = CharacterClassNumber.RageFighter, 
                    Name = "Rage Fighter",
                    Description = "Hand-to-hand combat masters.\nHighest HP, powerful combos."
                }
            };

            AutoViewSize = false;
            ViewSize = new Point(650, 550);
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            Interactive = true;
            
            InitializeControls();
            UpdateClassDisplay();
            ApplyThemeLayout();
            
            BringToFront();
        }

        public override async Task Load()
        {
            await base.Load();
            UiThemeId theme = UiThemeManager.CurrentId;
            if (_loadedTheme == theme)
                return;

            if (theme == UiThemeId.Season6)
            {
                async Task<Texture2D> LoadAsset(UiThemeAsset asset) =>
                    await UiThemeManager.LoadNativeTextureAsync(asset);

                _s6PanelFrame = await LoadAsset(UiThemeAsset.CharacterPanelFrame);
                _s6NameDescription = await LoadAsset(UiThemeAsset.CharacterNameDescription);
                _s6NameInput = await LoadAsset(UiThemeAsset.CharacterNameInput);
                _s6NamePlate = await LoadAsset(UiThemeAsset.CharacterNamePlate);
                _s6OkCancel = await LoadAsset(UiThemeAsset.CharacterOkCancel);
                _s6ClassButton = await LoadAsset(UiThemeAsset.CharacterClassButton);
                _s6DarkLabel = await LoadAsset(UiThemeAsset.CharacterDarkLabel);
                _s6Spiders[0] = await LoadAsset(UiThemeAsset.CharacterSpider01);
                _s6Spiders[1] = await LoadAsset(UiThemeAsset.CharacterSpider02);
                _s6Spiders[2] = await LoadAsset(UiThemeAsset.CharacterSpider03);
                _s6Spiders[3] = await LoadAsset(UiThemeAsset.CharacterSpider04);
                _s6Spiders[4] = await LoadAsset(UiThemeAsset.CharacterSpider05);
                _s6Spiders[5] = await LoadAsset(UiThemeAsset.CharacterSpider06);
            }

            _loadedTheme = theme;
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            _loadedTheme = (UiThemeId)(-1);
            ApplyThemeLayout();
            if (UiThemeManager.CurrentId == UiThemeId.Season6 && Status == GameControlStatus.Ready)
                _ = Load();
        }

        private void ApplyThemeLayout()
        {
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            if (season6)
            {
                AutoViewSize = false;
                Align = ControlAlign.None;
                X = 0;
                Y = 0;
                ViewSize = UiScaler.VirtualSize;
                ControlSize = ViewSize;

                LayoutClassicRects();

                Place(_titleLabel, 0, 24, UiScaler.VirtualSize.X, 28, HorizontalAlign.Center, 16f);
                Place(_nameLabel, _classicNamePlateRect.X, _classicNamePlateRect.Y,
                    _classicNamePlateRect.Width, _classicNamePlateRect.Height,
                    HorizontalAlign.Center, 12f);
                Place(_nameInput, _classicNameInputRect.X, _classicNameInputRect.Y,
                    _classicNameInputRect.Width, _classicNameInputRect.Height);
                Place(_classSectionLabel, _classicClassRects.Count > 0 ? _classicClassRects[0].X : 0,
                    _classicClassRects.Count > 0 ? _classicClassRects[0].Y : 0,
                    _classicClassRects.Count > 0 ? _classicClassRects[0].Width : 1,
                    _classicClassRects.Count > 0 ? _classicClassRects[0].Height : 1,
                    HorizontalAlign.Center, 13f);
                Place(_classLabel, 0, 0, 1, 1, HorizontalAlign.Center, 15f);
                Place(_classDescriptionLabel, _classicDescriptionTextRect.X, _classicDescriptionTextRect.Y,
                    _classicDescriptionTextRect.Width, _classicDescriptionTextRect.Height,
                    HorizontalAlign.Left, 10f);
                Place(_createButton, _classicOkRect.X, _classicOkRect.Y,
                    _classicOkRect.Width, _classicOkRect.Height);
                Place(_cancelButton, _classicCancelRect.X, _classicCancelRect.Y,
                    _classicCancelRect.Width, _classicCancelRect.Height);

                _previousClassButton.Visible = false;
                _previousClassButton.Interactive = false;
                _nextClassButton.Visible = false;
                _nextClassButton.Interactive = false;

                _titleLabel.TextColor = ModernHudTheme.TextWhite;
                _nameLabel.TextColor = ModernHudTheme.TextWhite;
                _classSectionLabel.TextColor = ModernHudTheme.TextWhite;
                _classLabel.TextColor = ModernHudTheme.TextGold;
                _classDescriptionLabel.TextColor = ModernHudTheme.TextGray;
                _nameInput.Skin = TextFieldSkin.Flat;
                _nameInput.TextColor = Color.Transparent;
                _nameInput.BackgroundColor = Color.Transparent;
                _nameInput.BorderColor = Color.Transparent;
                _nameInput.BorderThickness = 0;
                _createButton.BackgroundColor = ModernHudTheme.Success;
                _cancelButton.BackgroundColor = ModernHudTheme.BgLight;
            }
            else
            {
                AutoViewSize = false;
                Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
                ViewSize = new Point(650, 550);
                ControlSize = ViewSize;
                _titleLabel.Align = ControlAlign.Top | ControlAlign.HorizontalCenter;
                _nameLabel.Align = ControlAlign.None;
                _classSectionLabel.Align = ControlAlign.None;
                _classLabel.Align = ControlAlign.HorizontalCenter;
                _classDescriptionLabel.Align = ControlAlign.HorizontalCenter;
                _nameInput.X = 50;
                _nameInput.Y = 100;
                _nameInput.ViewSize = new Point(550, 36);
                _classSectionLabel.X = 50;
                _classSectionLabel.Y = 165;
                _previousClassButton.X = 50;
                _previousClassButton.Y = 200;
                _classLabel.Y = 200;
                _nextClassButton.X = ViewSize.X - 50 - _nextClassButton.ViewSize.X;
                _nextClassButton.Y = 200;
                _classDescriptionLabel.X = 50;
                _classDescriptionLabel.Y = 240;
                _previousClassButton.Visible = true;
                _previousClassButton.Interactive = true;
                _nextClassButton.Visible = true;
                _nextClassButton.Interactive = true;
                _nameInput.Skin = TextFieldSkin.Flat;
                _nameInput.TextColor = Theme.TextWhite;
                _nameInput.BackgroundColor = Theme.BgDark;
                _nameInput.BorderColor = Theme.BorderInner;
                _nameInput.BorderThickness = 1;
                _createButton.X = 50;
                _createButton.Y = 380;
                _createButton.ViewSize = new Point(260, 40);
                _createButton.ControlSize = _createButton.ViewSize;
                _cancelButton.X = 340;
                _cancelButton.Y = 380;
                _cancelButton.ViewSize = new Point(260, 40);
                _cancelButton.ControlSize = _cancelButton.ViewSize;
                _nameInput.ControlSize = _nameInput.ViewSize;
            }
        }

        private static void Place(LabelControl control, int x, int y, int width, int height,
            HorizontalAlign align, float fontSize)
        {
            control.Align = ControlAlign.None;
            control.X = x;
            control.Y = y;
            control.AutoViewSize = false;
            control.ViewSize = new Point(width, height);
            control.TextAlign = align;
            control.FontSize = fontSize;
        }

        private static void Place(GameControl control, int x, int y, int width, int height)
        {
            control.Align = ControlAlign.None;
            control.X = x;
            control.Y = y;
            control.AutoViewSize = false;
            control.ViewSize = new Point(width, height);
            control.ControlSize = control.ViewSize;
        }

        private void InitializeControls()
        {
            // Title
            _titleLabel = new LabelControl
            {
                Text = "CREATE CHARACTER",
                FontSize = 20f,
                TextColor = Theme.TextGold,
                Align = ControlAlign.Top | ControlAlign.HorizontalCenter,
                Margin = new Margin { Top = 10 }
            };
            Controls.Add(_titleLabel);

            // Character name input label
            _nameLabel = new LabelControl
            {
                Text = "Character Name:",
                FontSize = 13f,
                TextColor = Theme.TextWhite,
                X = 50,
                Y = 75
            };
            Controls.Add(_nameLabel);

            // Character name input
            _nameInput = TextFieldControl.Create();
            _nameInput.X = 50;
            _nameInput.Y = 100;
            _nameInput.ViewSize = new Point(550, 36);
            _nameInput.FontSize = 8f;
            _nameInput.Placeholder = "Enter name (3-10 chars)...";
            _nameInput.Skin = TextFieldSkin.Flat;
            _nameInput.TextColor = Theme.TextWhite;
            _nameInput.BackgroundColor = Theme.BgDark;
            _nameInput.BorderColor = Theme.BorderInner;
            _nameInput.BorderThickness = 1;
            _nameInput.ValueChanged += OnNameInputValueChanged;
            Controls.Add(_nameInput);

            // Class selection section
            _classSectionLabel = new LabelControl
            {
                Text = "Select Class:",
                FontSize = 13f,
                TextColor = Theme.TextWhite,
                X = 50,
                Y = 165
            };
            Controls.Add(_classSectionLabel);

            // Class navigation buttons
            _previousClassButton = CreateModernNavigationButton("<");
            _previousClassButton.X = 50;
            _previousClassButton.Y = 200;
            _previousClassButton.Click += (s, e) => ChangeClass(-1);
            Controls.Add(_previousClassButton);

            _nextClassButton = CreateModernNavigationButton(">");
            _nextClassButton.X = ViewSize.X - 50 - _nextClassButton.ViewSize.X;
            _nextClassButton.Y = 200;
            _nextClassButton.Click += (s, e) => ChangeClass(1);
            Controls.Add(_nextClassButton);

            // Class name label
            _classLabel = new LabelControl
            {
                Text = "Dark Wizard",
                FontSize = 18f,
                TextColor = Theme.TextGold,
                Align = ControlAlign.HorizontalCenter,
                Y = 200
            };
            Controls.Add(_classLabel);

            // Class description
            _classDescriptionLabel = new LabelControl
            {
                Text = "",
                FontSize = 12f,
                TextColor = Theme.TextGray,
                Align = ControlAlign.HorizontalCenter,
                Y = 240,
                ViewSize = new Point(550, 100),
                X = 50
            };
            Controls.Add(_classDescriptionLabel);

            // Create button
            _createButton = CreateModernButton("CREATE CHARACTER", Theme.Success);
            _createButton.X = 50;
            _createButton.Y = 380;
            _createButton.ViewSize = new Point(260, 40);
            _createButton.Click += OnCreateButtonClick;
            Controls.Add(_createButton);

            // Cancel button
            _cancelButton = CreateModernButton("CANCEL", Theme.BgLight);
            _cancelButton.X = 340;
            _cancelButton.Y = 380;
            _cancelButton.ViewSize = new Point(260, 40);
            _cancelButton.Click += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_cancelButton);
        }

        private ButtonControl CreateModernNavigationButton(string arrow)
        {
            return new ButtonControl
            {
                Text = arrow,
                FontSize = 32f,
                AutoViewSize = false,
                ViewSize = new Point(60, 60),
                BackgroundColor = Theme.BgMid,
                HoverBackgroundColor = Theme.BgLight,
                PressedBackgroundColor = Theme.BgDark,
                TextColor = Theme.Accent,
                HoverTextColor = Theme.AccentBright,
                DisabledTextColor = Theme.TextDark,
                Interactive = true,
                BorderThickness = 2,
                BorderColor = Theme.BorderInner
            };
        }

        private ButtonControl CreateModernButton(string text, Color baseColor)
        {
            return new ButtonControl
            {
                Text = text,
                FontSize = 13f,
                AutoViewSize = false,
                BackgroundColor = baseColor,
                HoverBackgroundColor = Color.Lerp(baseColor, Color.White, 0.2f),
                PressedBackgroundColor = Color.Lerp(baseColor, Color.Black, 0.2f),
                TextColor = Theme.TextWhite,
                HoverTextColor = Theme.TextWhite,
                DisabledTextColor = Theme.TextDark,
                Interactive = true,
                BorderThickness = 1,
                BorderColor = Theme.BorderInner
            };
        }

        private void ChangeClass(int direction)
        {
            _selectedClassIndex = (_selectedClassIndex + direction + _availableClasses.Count) % _availableClasses.Count;
            UpdateClassDisplay();
        }

        private static string GetPreviewModel(CharacterClassNumber characterClass) => characterClass switch
        {
            CharacterClassNumber.DarkKnight => "NewFace01",
            CharacterClassNumber.DarkWizard => "NewFace02",
            CharacterClassNumber.FairyElf => "NewFace03",
            CharacterClassNumber.MagicGladiator => "NewFace04",
            CharacterClassNumber.DarkLord => "NewFace05",
            CharacterClassNumber.Summoner => "NewFace06",
            _ => null
        };

        private void UpdateClassDisplay()
        {
            var selectedClass = _availableClasses[_selectedClassIndex];
            _classLabel.Text = selectedClass.Name;
            _classDescriptionLabel.Text = selectedClass.Description;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnNameInputValueChanged(object sender, EventArgs e)
        {
            string value = _nameInput.Value;
            var sanitized = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length && sanitized.Length < 10; i++)
            {
                if (char.IsLetterOrDigit(value[i]))
                    sanitized.Append(value[i]);
            }

            string sanitizedValue = sanitized.ToString();
            if (!string.Equals(value, sanitizedValue, StringComparison.Ordinal))
                _nameInput.Value = sanitizedValue;
        }

        private void OnCreateButtonClick(object sender, EventArgs e)
        {
            string characterName = _nameInput?.Value?.Trim() ?? "";
            
            if (string.IsNullOrWhiteSpace(characterName))
            {
                MessageWindow.Show("Please enter a character name.");
                return;
            }

            if (characterName.Length < 3)
            {
                MessageWindow.Show("Character name must be at least 3 characters long.");
                return;
            }

            if (characterName.Length > 10)
            {
                MessageWindow.Show("Character name must be 10 characters or less.");
                return;
            }

            var selectedClass = _availableClasses[_selectedClassIndex];
            CharacterCreateRequested?.Invoke(this, (characterName, selectedClass.Class));
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (UiThemeManager.CurrentId == UiThemeId.Classic && Visible)
                UpdateClassicClassInteraction();
            
            _bringToFrontTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_bringToFrontTimer >= BRING_TO_FRONT_INTERVAL && Parent != null)
            {
                _bringToFrontTimer = 0;
                BringToFront();
            }
        }

        private void UpdateClassicClassInteraction()
        {
            if (_classicClassRects.Count == 0)
                return;

            MouseState current = MuGame.Instance.UiMouseState;
            MouseState previous = MuGame.Instance.PrevUiMouseState;
            Point mouse = current.Position;
            bool pressed = current.LeftButton == ButtonState.Pressed;
            bool wasPressed = previous.LeftButton == ButtonState.Pressed;

            if (pressed && !wasPressed)
            {
                _classicPressedClass = _classicClassRects.FindIndex(rect => rect.Contains(mouse));
            }
            else if (!pressed && wasPressed)
            {
                if (_classicPressedClass >= 0 &&
                    _classicPressedClass < _classicClassRects.Count &&
                    _classicClassRects[_classicPressedClass].Contains(mouse))
                {
                    if (_selectedClassIndex != _classicPressedClass)
                    {
                        _selectedClassIndex = _classicPressedClass;
                        UpdateClassDisplay();
                    }
                }

                _classicPressedClass = -1;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (UiThemeManager.CurrentId == UiThemeId.Season6)
            {
                DrawClassicCreationSurface();
                return;
            }

            DrawCachedBackground();
            
            base.Draw(gameTime);
        }

        private static float ClassicScale => UiScaler.VirtualSize.Y / 750f;

        private static Vector2 ClassicPoint(float x, float y)
        {
            float scale = ClassicScale;
            return new(
                UiScaler.VirtualSize.X / 2f + x * scale,
                UiScaler.VirtualSize.Y / 2f - y * scale);
        }

        private static Rectangle ClassicRect(float x, float y, float width, float height)
        {
            Vector2 center = ClassicPoint(x, y);
            float scale = ClassicScale;
            return new Rectangle(
                (int)MathF.Round(center.X - width * scale / 2f),
                (int)MathF.Round(center.Y - height * scale / 2f),
                Math.Max(1, (int)MathF.Round(width * scale)),
                Math.Max(1, (int)MathF.Round(height * scale)));
        }

        private void LayoutClassicRects()
        {
            _classicPanelRect = ClassicRect(ClassicPanelX, ClassicPanelY, ClassicPanelW, ClassicPanelH);
            _classicDescriptionRect = ClassicRect(
                ClassicDescriptionX, ClassicDescriptionY, ClassicDescriptionW, ClassicDescriptionH);
            _classicNameInputRect = ClassicRect(
                ClassicNameInputX, ClassicNameInputY, ClassicNameInputW, ClassicNameInputH);
            _classicNamePlateRect = ClassicRect(
                ClassicNamePlateX, ClassicNamePlateY, ClassicNamePlateW, ClassicNamePlateH);
            _classicNameTextRect = ClassicRect(
                ClassicNameTextX, ClassicNameTextY, ClassicNameTextW, ClassicNameTextH);
            _classicDescriptionTextRect = ClassicRect(
                ClassicDescriptionTextX, ClassicDescriptionTextY,
                ClassicDescriptionTextW, ClassicDescriptionTextH);
            _classicAttributeLabelRect = ClassicRect(
                ClassicAttributeLabelX, ClassicAttributeLabelY,
                ClassicAttributeLabelW, ClassicAttributeLabelH);
            _classicSpiderRect = ClassicRect(ClassicSpiderX, ClassicSpiderY, ClassicSpiderW, ClassicSpiderH);
            _classicOkRect = ClassicRect(ClassicOkX, ClassicOkY, ClassicOkW, ClassicOkH);
            _classicCancelRect = ClassicRect(
                ClassicCancelX, ClassicCancelY, ClassicCancelW, ClassicCancelH);

            _classicClassRects.Clear();
            for (int i = 0; i < _availableClasses.Count; i++)
            {
                float y = ClassicClassTopY - ClassicClassPitch * i;
                _classicClassRects.Add(ClassicRect(
                    ClassicClassCenterX, y, ClassicClassW, ClassicClassH));
            }
        }

        private void DrawClassicCreationSurface()
        {
            SpriteBatch sprite = GraphicsManager.Instance.Sprite;
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            SpriteFont font = GraphicsManager.Instance.Font;
            if (sprite == null || pixel == null || font == null)
                return;

            using var scope = new SpriteBatchScope(
                sprite, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform);

            DrawPiece(sprite, pixel, _s6PanelFrame, _classicPanelRect, ClassicPanelSource);
            DrawPiece(sprite, pixel, _s6DarkLabel, _classicAttributeLabelRect, ClassicDarkLabelSource);

            Texture2D spider = GetSelectedSpiderTexture();
            if (spider != null && !spider.IsDisposed)
                DrawPiece(sprite, pixel, spider, _classicSpiderRect, ClassicSpiderSource);

            Point mouse = MuGame.Instance.UiMouseState.Position;
            for (int i = 0; i < _classicClassRects.Count; i++)
            {
                Rectangle rect = _classicClassRects[i];
                bool selected = i == _selectedClassIndex;
                bool hover = rect.Contains(mouse) || _classicPressedClass == i;
                Rectangle source = selected
                    ? ClassicClassSelectedSource
                    : hover ? ClassicClassHoverSource : ClassicClassDefaultSource;
                DrawPieceAspectFit(sprite, pixel, _s6ClassButton, rect, source);
                DrawCentered(sprite, font, _availableClasses[i].Name, rect,
                    selected ? ModernHudTheme.TextGold : ModernHudTheme.TextWhite, 0.42f);

            }

            DrawPiece(sprite, pixel, _s6NameDescription, _classicDescriptionRect,
                ClassicDescriptionSource);
            DrawPiece(sprite, pixel, _s6DarkLabel,
                ClassicRect(13.2f, -226.6f, 467f, 293f), ClassicDarkLabelSource);
            DrawClassicDescription(sprite, font);

            DrawPiece(sprite, pixel, _s6NameInput, _classicNameInputRect, ClassicNameInputSource);
            DrawPiece(sprite, pixel, _s6NamePlate, _classicNamePlateRect, ClassicNamePlateSource);
            DrawCentered(sprite, font, "Name", OffsetRect(
                _classicNamePlateRect, ClassicNamePlateTextDX, ClassicNamePlateTextDY),
                ModernHudTheme.TextWhite, 0.42f);
            DrawString(sprite, font, _nameInput.Value, _classicNameTextRect,
                ModernHudTheme.TextWhite, 0.45f);

            DrawClassicButton(sprite, pixel, font, _classicOkRect, _createButton.Text,
                _createButton.IsMouseOver || _createButton.IsMousePressed);
            DrawClassicButton(sprite, pixel, font, _classicCancelRect, _cancelButton.Text,
                _cancelButton.IsMouseOver || _cancelButton.IsMousePressed);

            DrawCentered(sprite, font, _titleLabel.Text,
                new Rectangle(0, 18, UiScaler.VirtualSize.X, 34),
                ModernHudTheme.TextWhite, 0.58f);
        }

        private static Rectangle OffsetRect(Rectangle rect, float dx, float dy)
        {
            int scaleX = (int)MathF.Round(dx * ClassicScale);
            int scaleY = (int)MathF.Round(dy * ClassicScale);
            return new Rectangle(rect.X + scaleX, rect.Y + scaleY, rect.Width, rect.Height);
        }

        private void DrawClassicButton(SpriteBatch sprite, Texture2D pixel, SpriteFont font,
            Rectangle rect, string text, bool hover)
        {
            Rectangle source = GetOkCancelSource(_s6OkCancel, hover);
            DrawPieceAspectFit(sprite, pixel, _s6OkCancel, rect, source);
            DrawCenteredFit(sprite, font, text, rect, ModernHudTheme.TextWhite, 0.42f);
        }

        private static Rectangle GetOkCancelSource(Texture2D texture, bool hover)
        {
            // OZT textures are uploaded at the next power-of-two size. The bundled
            // project sheet is 467x80 (512x128 on the GPU); older data uses 149x32.
            bool isWideSheet = texture != null && (texture.Width >= 512 || texture.Height >= 128);
            if (isWideSheet)
                return hover ? ClassicOkHoverSource : ClassicOkSource;

            return hover ? ClassicOkLegacyHoverSource : ClassicOkLegacySource;
        }

        private static void DrawCenteredFit(SpriteBatch sprite, SpriteFont font, string text,
            Rectangle rect, Color color, float maximumScale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            Vector2 measured = font.MeasureString(text);
            float scale = maximumScale;
            if (measured.X * scale > rect.Width - 8)
                scale = Math.Max(0.2f, (rect.Width - 8f) / measured.X);

            DrawCentered(sprite, font, text, rect, color, scale);
        }

        private Texture2D GetSelectedSpiderTexture()
        {
            int spiderIndex = SelectedClass switch
            {
                CharacterClassNumber.DarkKnight => 0,
                CharacterClassNumber.DarkWizard => 1,
                CharacterClassNumber.FairyElf => 2,
                CharacterClassNumber.MagicGladiator => 3,
                CharacterClassNumber.DarkLord => 4,
                CharacterClassNumber.Summoner => 5,
                _ => -1
            };

            return spiderIndex >= 0 && spiderIndex < _s6Spiders.Length
                ? _s6Spiders[spiderIndex]
                : null;
        }

        private static void DrawPieceAspectFit(SpriteBatch sprite, Texture2D pixel, Texture2D texture,
            Rectangle destination, Rectangle source, Color? tint = null)
        {
            if (texture == null || texture.IsDisposed || source.Width <= 0 || source.Height <= 0)
            {
                DrawPiece(sprite, pixel, texture, destination, source, tint);
                return;
            }

            float sourceAspect = source.Width / (float)source.Height;
            float destinationAspect = destination.Width / (float)destination.Height;
            Rectangle fitted = destination;

            if (destinationAspect > sourceAspect)
            {
                fitted.Width = Math.Max(1, (int)MathF.Round(destination.Height * sourceAspect));
                fitted.X = destination.Center.X - fitted.Width / 2;
            }
            else if (destinationAspect < sourceAspect)
            {
                fitted.Height = Math.Max(1, (int)MathF.Round(destination.Width / sourceAspect));
                fitted.Y = destination.Center.Y - fitted.Height / 2;
            }

            DrawPiece(sprite, pixel, texture, fitted, source, tint);
        }

        private static void DrawPiece(SpriteBatch sprite, Texture2D pixel, Texture2D texture,
            Rectangle destination, Rectangle source, Color? tint = null)
        {
            if (texture != null && !texture.IsDisposed)
                sprite.Draw(texture, destination, source, tint ?? Color.White);
            else
            {
                sprite.Draw(pixel, destination, ModernHudTheme.BgDark);
                sprite.Draw(pixel, new Rectangle(destination.X, destination.Y, destination.Width, 1), ModernHudTheme.BorderInner);
                sprite.Draw(pixel, new Rectangle(destination.X, destination.Bottom - 1, destination.Width, 1), ModernHudTheme.BorderOuter);
            }
        }

        private static void DrawCentered(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect,
            Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;
            Vector2 size = font.MeasureString(text) * scale;
            Vector2 position = new(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
            sprite.DrawString(font, text, position + Vector2.One, Color.Black * 0.65f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawClassicDescription(SpriteBatch sprite, SpriteFont font)
        {
            string text = _classDescriptionLabel.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            float scale = 0.36f;
            float lineHeight = font.LineSpacing * scale * 1.1f;
            float y = _classicDescriptionTextRect.Y;
            for (int i = 0; i < lines.Length; i++)
            {
                if (y + lineHeight > _classicDescriptionTextRect.Bottom)
                    break;
                DrawString(sprite, font, lines[i],
                    new Rectangle(_classicDescriptionTextRect.X, (int)y,
                        _classicDescriptionTextRect.Width, (int)lineHeight),
                    ModernHudTheme.TextWhite, scale);
                y += lineHeight;
            }
        }

        private static void DrawString(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect,
            Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            Vector2 position = new(rect.X, rect.Y + 2);
            sprite.DrawString(font, text, position + Vector2.One, Color.Black * 0.65f,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale,
                SpriteEffects.None, 0f);
        }

        private void DrawCachedBackground()
        {
            var device = MuGame.Instance.GraphicsDevice;
            
            if (_backgroundSurface == null || _surfaceNeedsRedraw || 
                _backgroundSurface.Width != ViewSize.X || _backgroundSurface.Height != ViewSize.Y)
            {
                Client.Main.Graphics.UiRenderTargetPool.Return(_backgroundSurface);
                _backgroundSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(device, ViewSize.X, ViewSize.Y);
                
                // Render background to surface
                var oldTargets = device.GetRenderTargets();
                device.SetRenderTarget(_backgroundSurface);
                device.Clear(Color.Transparent);
                
                using var batch = new SpriteBatch(device);
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                
                var pixel = UiDrawHelper.GetPixelTexture(device);
                var dialogRect = new Rectangle(0, 0, ViewSize.X, ViewSize.Y);
                
                // Main panel background with gradient
                UiDrawHelper.DrawVerticalGradient(batch, dialogRect, Theme.BgMid, Theme.BgDark);
                
                // Outer border
                batch.Draw(pixel, new Rectangle(0, 0, dialogRect.Width, 1), Theme.BorderOuter);
                batch.Draw(pixel, new Rectangle(0, dialogRect.Height - 1, dialogRect.Width, 1), Theme.BorderOuter);
                batch.Draw(pixel, new Rectangle(0, 0, 1, dialogRect.Height), Theme.BorderOuter);
                batch.Draw(pixel, new Rectangle(dialogRect.Width - 1, 0, 1, dialogRect.Height), Theme.BorderOuter);
                
                // Header section
                var headerRect = new Rectangle(0, 0, dialogRect.Width, 50);
                UiDrawHelper.DrawHorizontalGradient(batch, headerRect, Theme.BgLighter, Theme.BgMid);
                UiDrawHelper.DrawCornerAccents(batch, headerRect, Theme.Accent, 12, 2);
                
                // Header separator
                batch.Draw(pixel, new Rectangle(0, headerRect.Bottom - 1, headerRect.Width, 1), Theme.BorderInner);
                batch.Draw(pixel, new Rectangle(0, headerRect.Bottom - 2, headerRect.Width, 1), Theme.Accent * 0.3f);
                
                batch.End();
                
                device.SetRenderTargets(oldTargets);
                _surfaceNeedsRedraw = false;
            }
            
            // Draw cached surface
            using var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                UiScaler.SpriteTransform
            );
            
            GraphicsManager.Instance.Sprite.Draw(
                _backgroundSurface,
                new Rectangle(DisplayPosition.X, DisplayPosition.Y, ViewSize.X, ViewSize.Y),
                Color.White
            );
        }

        public override void Dispose()
        {
            Client.Main.Graphics.UiRenderTargetPool.Return(_backgroundSurface);
            _backgroundSurface = null;
            base.Dispose();
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _surfaceNeedsRedraw = true;
        }
    }
}
