using Client.Main.Controllers;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Map
{
    /// <summary>Banner with the map name shown for a few seconds after entering a map (Ornate kit, Frame/map_banner).</summary>
    public class MapNameControl : GameControl
    {
        private const float HoldSeconds = 5f, FadeSeconds = 2f;
        // The name goes on the dark plate: inner width and vertical centre as fractions of the banner art.
        private const float PlateTextWidth = 0.70f, PlateCenterY = 0.56f;
        private const int TopY = 64, MinWidth = 460;

        private float _displayTimer;
        private string _text = string.Empty;

        public MapNameControl()
        {
            AutoViewSize = false;
            ViewSize = new Point(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
            Interactive = false;
            Visible = true;
            Alpha = 0f;
        }

        public string LabelText
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        public void ShowMapName(string mapName)
        {
            _displayTimer = 0f;
            Alpha = 1f;
            Visible = true;
            _text = mapName ?? string.Empty;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _displayTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
            Alpha = _displayTimer <= HoldSeconds
                ? 1f
                : MathHelper.SmoothStep(1f, 0f, MathHelper.Clamp((_displayTimer - HoldSeconds) / FadeSeconds, 0f, 1f));
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || Alpha <= 0.01f || _text.Length == 0)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                       transform: UiScaler.SpriteTransform))
            {
                Texture2D art = OrnateAssets.Get("Frame/map_banner");
                float fontSize = 26f;
                float textWidth = OrnateDraw.Measure(_text, fontSize).X;
                int width = art != null ? (int)MathF.Max(MinWidth, (textWidth + 48f) / PlateTextWidth) : (int)textWidth + 80;
                int height = art != null ? (int)(width * art.Height / (float)art.Width) : 56;
                var banner = new Rectangle((UiScaler.VirtualSize.X - width) / 2, TopY, width, height);

                if (art != null)
                    sb.Draw(art, banner, Color.White * Alpha);

                var textArea = new Rectangle(banner.X, (int)(banner.Y + banner.Height * PlateCenterY) - 20, banner.Width, 40);
                OrnateDraw.TextCentered(sb, _text, textArea, fontSize, OrnateStyle.Gold, Alpha, bold: true);
            }
        }
    }
}
