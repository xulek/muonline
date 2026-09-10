using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.Party
{
    public class PartyMemberControl : UIControl
    {
        private readonly LabelControl _nameLabel;
        private readonly LabelControl _infoLabel;
        
        private readonly LabelControl _healthPercentLabel;
        private readonly ButtonControl _leaveButton;

        private readonly ColorBarControl[] _healthSegments;

        private bool IsSeason6 => UiThemeManager.CurrentId == UiThemeId.Season6;

        public PartyMemberInfo MemberInfo { get; private set; }
        public bool IsCurrentPlayer { get; private set; }

        public PartyMemberControl()
        {
            AutoViewSize = false;
            ViewSize = IsSeason6 ? new Point(240, 42) : new Point(160, 48);
            BackgroundColor = IsSeason6 ? ModernHudTheme.BgDark * 0.94f : new Color(15, 15, 25) * 0.9f;
            BorderColor = IsSeason6 ? ModernHudTheme.BorderInner : new Color(100, 150, 200) * 0.8f;
            BorderThickness = 1;

            _nameLabel = new LabelControl
            {
                X = 6,
                Y = IsSeason6 ? 3 : 4,
                FontSize = IsSeason6 ? 10f : 11f,
                TextColor = IsSeason6 ? ModernHudTheme.TextWhite : Color.White,
                IsBold = true,
                HasShadow = true,
                ShadowColor = Color.Black,
                ShadowOffset = new Vector2(1, 1),
                ShadowOpacity = 0.8f
            };

            _healthSegments = new ColorBarControl[4];
            int segmentWidth = IsSeason6 ? 42 : 30;
            int segmentSpacing = 2;

            for (int i = 0; i < 4; i++)
            {
                _healthSegments[i] = new ColorBarControl
                {
                    X = 6 + i * (segmentWidth + segmentSpacing),
                    Y = IsSeason6 ? 18 : 20,
                    ViewSize = new Point(segmentWidth, 7),
                    BackgroundColor = IsSeason6 ? ModernHudTheme.SlotBg : new Color(60, 20, 20) * 0.8f,
                    FillColor = GetHealthSegmentColor(i),
                    BorderColor = IsSeason6 ? ModernHudTheme.BorderOuter : Color.Black * 0.8f,
                    BorderThickness = 1
                };
                Controls.Add(_healthSegments[i]);
            }

            _healthPercentLabel = new LabelControl
            {
                X = IsSeason6 ? ViewSize.X - 34 : 4 * (segmentWidth + segmentSpacing) + 5,
                Y = IsSeason6 ? 17 : 18,
                FontSize = IsSeason6 ? 8f : 8f,
                TextColor = IsSeason6 ? ModernHudTheme.Warning : Color.Yellow,
                IsBold = true,
                HasShadow = true,
                ShadowColor = Color.Black,
                ShadowOffset = new Vector2(1, 1),
                ShadowOpacity = 0.8f
            };

            _infoLabel = new LabelControl
            {
                X = 6,
                Y = IsSeason6 ? 28 : 28,
                FontSize = IsSeason6 ? 8f : 9f,
                TextColor = IsSeason6 ? ModernHudTheme.TextGray : Color.LightBlue,
                HasShadow = true,
                ShadowColor = Color.Black,
                ShadowOffset = new Vector2(1, 1),
                ShadowOpacity = 0.7f
            };

            _leaveButton = new ButtonControl
            {
                X = ViewSize.X - (IsSeason6 ? 20 : 18),
                Y = 3,
                ViewSize = IsSeason6 ? new Point(17, 17) : new Point(15, 15),
                Text = "×",
                FontSize = 12f,
                TextColor = Color.White,
                BackgroundColor = IsSeason6 ? ModernHudTheme.Danger * 0.75f : new Color(150, 50, 50) * 0.8f,
                BorderColor = IsSeason6 ? ModernHudTheme.Danger : new Color(200, 100, 100),
                BorderThickness = 1,
                Visible = false,
            };
            _leaveButton.Click += OnLeaveButtonClick;

            Controls.Add(_nameLabel);
            Controls.Add(_infoLabel);
            Controls.Add(_healthPercentLabel);
            Controls.Add(_leaveButton);
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            int segmentWidth = season6 ? 42 : 30;
            int segmentSpacing = 2;
            ViewSize = season6 ? new Point(240, 42) : new Point(160, 48);
            BackgroundColor = season6 ? ModernHudTheme.BgDark * 0.94f : new Color(15, 15, 25) * 0.9f;
            BorderColor = season6 ? ModernHudTheme.BorderInner : new Color(100, 150, 200) * 0.8f;
            _nameLabel.Y = season6 ? 3 : 4;
            _nameLabel.FontSize = season6 ? 10f : 11f;
            _nameLabel.TextColor = season6 ? ModernHudTheme.TextWhite : Color.White;
            for (int i = 0; i < _healthSegments.Length; i++)
            {
                _healthSegments[i].X = 6 + i * (segmentWidth + segmentSpacing);
                _healthSegments[i].Y = season6 ? 18 : 20;
                _healthSegments[i].ViewSize = new Point(segmentWidth, 7);
                _healthSegments[i].BackgroundColor = season6 ? ModernHudTheme.SlotBg : new Color(60, 20, 20) * 0.8f;
                _healthSegments[i].BorderColor = season6 ? ModernHudTheme.BorderOuter : Color.Black * 0.8f;
            }
            _healthPercentLabel.X = season6 ? ViewSize.X - 34 : 4 * (segmentWidth + segmentSpacing) + 5;
            _healthPercentLabel.Y = season6 ? 17 : 18;
            _healthPercentLabel.TextColor = season6 ? ModernHudTheme.Warning : Color.Yellow;
            _infoLabel.FontSize = season6 ? 8f : 9f;
            _infoLabel.TextColor = season6 ? ModernHudTheme.TextGray : Color.LightBlue;
            _leaveButton.X = ViewSize.X - (season6 ? 20 : 18);
            _leaveButton.ViewSize = season6 ? new Point(17, 17) : new Point(15, 15);
            _leaveButton.BackgroundColor = season6 ? ModernHudTheme.Danger * 0.75f : new Color(150, 50, 50) * 0.8f;
            _leaveButton.BorderColor = season6 ? ModernHudTheme.Danger : new Color(200, 100, 100);
        }

        private Color GetHealthSegmentColor(int segmentIndex)
        {
            switch (segmentIndex)
            {
                case 0: return new Color(220, 20, 20);
                case 1: return new Color(220, 20, 20);
                case 2: return new Color(220, 20, 20);
                case 3: return new Color(220, 20, 20);
                default: return Color.Red;
            }
        }

        public void UpdateData(PartyMemberInfo memberInfo, bool isCurrentPlayer = false)
        {
            MemberInfo = memberInfo;
            IsCurrentPlayer = isCurrentPlayer;

            _nameLabel.Text = memberInfo.Name;

            _healthPercentLabel.Text = $"{(int)(memberInfo.HealthPercentage * 100)}%";

            if (memberInfo.HealthPercentage <= 0.25f)
                _healthPercentLabel.TextColor = Color.Red;
            else if (memberInfo.HealthPercentage <= 0.5f)
                _healthPercentLabel.TextColor = Color.Orange;
            else
                _healthPercentLabel.TextColor = Color.LimeGreen;

            if (memberInfo.HealthPercentage <= 0.25f)
                _nameLabel.TextColor = Color.Red;
            else if (memberInfo.HealthPercentage <= 0.5f)
                _nameLabel.TextColor = Color.Orange;
            else
                _nameLabel.TextColor = Color.White;

            UpdateHealthSegments(memberInfo.HealthPercentage);

            string mapName = MapDatabase.GetMapName(memberInfo.MapId);
            _infoLabel.Text = $"{mapName} ({memberInfo.PositionX}, {memberInfo.PositionY})";

            _leaveButton.Visible = isCurrentPlayer;

            if (isCurrentPlayer)
            {
                BorderColor = IsSeason6 ? ModernHudTheme.Success : new Color(150, 200, 100) * 0.9f; // Zielona ramka
                BackgroundColor = IsSeason6 ? ModernHudTheme.Success * 0.16f : new Color(15, 25, 15) * 0.9f; // Lekko zielone tło
            }
            else
            {
                BorderColor = IsSeason6 ? ModernHudTheme.BorderInner : new Color(100, 150, 200) * 0.8f; // Standardowa ramka
                BackgroundColor = IsSeason6 ? ModernHudTheme.BgDark * 0.94f : new Color(15, 15, 25) * 0.9f; // Standardowe tło
            }
        }

        private void UpdateHealthSegments(float healthPercentage)
        {
            float segmentSize = 0.25f;

            for (int i = 0; i < 4; i++)
            {
                float segmentStart = i * segmentSize;
                float segmentEnd = (i + 1) * segmentSize;

                if (healthPercentage <= segmentStart)
                {
                    _healthSegments[i].Percentage = 0f;
                }
                else if (healthPercentage >= segmentEnd)
                {
                    _healthSegments[i].Percentage = 1f;
                }
                else
                {
                    float segmentFill = (healthPercentage - segmentStart) / segmentSize;
                    _healthSegments[i].Percentage = segmentFill;
                }

                if (healthPercentage <= 0.25f && i == 0)
                {
                    _healthSegments[i].Alpha = 0.5f + (float)(System.Math.Sin(System.DateTime.Now.Millisecond * 0.01) * 0.3);
                }
                else
                {
                    _healthSegments[i].Alpha = 1f;
                }
            }
        }

        private void OnLeaveButtonClick(object sender, System.EventArgs e)
        {
            if (IsCurrentPlayer && MemberInfo != null)
            {
                var characterService = MuGame.Network?.GetCharacterService();
                _ = characterService?.SendPartyKickRequestAsync(MemberInfo.Index);
            }
        }
    }
}
