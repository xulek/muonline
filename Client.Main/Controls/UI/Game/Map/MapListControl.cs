using Client.Main.Models;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Worlds;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.Map
{
    public class MapListControl : UIControl
    {
        private const int InnerPadding = 10;
        private const int ButtonSpacing = 0;
        private const float ButtonFontSize = 10f;

        private bool IsSeason6 => UiThemeManager.CurrentId == UiThemeId.Season6;

        public MapListControl()
        {
            // Set the control alignment.
            Align = ControlAlign.Top | ControlAlign.Left;
            Margin = IsSeason6 ? new Margin { Top = 14, Left = 14 } : new Margin { Top = 10, Left = 10 };

            // Set a semi-transparent background and border.
            BackgroundColor = IsSeason6 ? ModernHudTheme.BgDark * 0.96f : Color.Black * 0.6f;
            BorderColor = IsSeason6 ? ModernHudTheme.BorderInner : Color.Gray;
            BorderThickness = 1;

            // Set a fixed size for the container.
            ControlSize = IsSeason6 ? new Point(300, 650) : new Point(220, 650);
            // Explicitly set ViewSize so the background is drawn correctly.
            ViewSize = ControlSize;
            AutoViewSize = false;

            // Add map buttons.
            AddButtons();
            ApplyThemeLayout();
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private void ApplyThemeLayout()
        {
            int padding = IsSeason6 ? 14 : InnerPadding;
            int height = IsSeason6 ? 24 : 20;
            ControlSize = IsSeason6 ? new Point(300, 650) : new Point(220, 650);
            ViewSize = ControlSize;
            Margin = IsSeason6 ? new Margin { Top = 14, Left = 14 } : new Margin { Top = 10, Left = 10 };
            BackgroundColor = IsSeason6 ? ModernHudTheme.BgDark * 0.96f : Color.Black * 0.6f;
            BorderColor = IsSeason6 ? ModernHudTheme.BorderInner : Color.Gray;
            int width = ControlSize.X - padding * 2;
            foreach (var child in Controls)
            {
                child.ViewSize = new Point(width, height);
                child.ControlSize = child.ViewSize;
            }
        }

        /// <summary>
        /// Adds map buttons to the control.
        /// </summary>
        private void AddButtons()
        {
            int buttonWidth = ControlSize.X - 2 * InnerPadding;
            int buttonHeight = 20;

            Controls.Add(new MapButton<LorenciaWorld>
            {
                Name = "Lorencia",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<NoriaWorld>
            {
                Name = "Noria",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<ElvelandWorld>
            {
                Name = "Elveland",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DeviasWorld>
            {
                Name = "Devias",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DungeonWorld>
            {
                Name = "Dungeon",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<AtlansWorld>
            {
                Name = "Atlans",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<LostTowerWorld>
            {
                Name = "Lost Tower",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<IcarusWorld>
            {
                Name = "Icarus",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<UrukMountainWorld>
            {
                Name = "Uruk Mountain",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<StadiumWorld>
            {
                Name = "Arena",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<TarkanWorld>
            {
                Name = "Tarkan",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DevilSquareWorld>
            {
                Name = "Devil Square",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<ValleyOfLorenWorld>
            {
                Name = "Valley Of Loren",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<LandOfTrialsWorld>
            {
                Name = "Land Of Trials",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<AidaWorld>
            {
                Name = "Aida",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<CrywolfWorld>
            {
                Name = "Cry Wolf",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<KanturuWorld>
            {
                Name = "Kanturu (RUINS)",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<KanturuRemainWorld>
            {
                Name = "Kanturu Remain (RELICS)",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<RefineTowerWorld>
            {
                Name = "Refine Tower",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<SilentMapWorld>
            {
                Name = "Silent Map",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<BalgassBarracksWorld>
            {
                Name = "Barracks",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<BalgassRefugeWorld>
            {
                Name = "Refuge",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<IllusionTempleWorld>
            {
                Name = "Illusion Temple",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<SwampOfPeaceWorld>
            {
                Name = "Swamp of Peace",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<RaklionWorld>
            {
                Name = "Raklion",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<RaklionBossWorld>
            {
                Name = "Raklion Boss",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<SantaVillageWorld>
            {
                Name = "Santa Village",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<VulcanusWorld>
            {
                Name = "Vulcanus",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DuelArenaWorld>
            {
                Name = "Duel Arena",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DoppelgangerIceWorld>
            {
                Name = "Doppelganger Ice Zone",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
            Controls.Add(new MapButton<DoppelgangerIceNewWorld>
            {
                Name = "Doppelganger Ice Zone new",
                ControlSize = new Point(buttonWidth, buttonHeight),
                FontSize = ButtonFontSize
            });
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            ArrangeMapButtons();
        }

        /// <summary>
        /// Arranges the map buttons in a vertical column with the specified inner padding.
        /// </summary>
        private void ArrangeMapButtons()
        {
            int padding = IsSeason6 ? 14 : InnerPadding;
            int spacing = IsSeason6 ? 2 : ButtonSpacing;
            int currentY = padding;

            foreach (var child in Controls)
            {
                child.X = padding;
                child.Y = currentY;
                currentY += child.ControlSize.Y + spacing;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }
    }
}
