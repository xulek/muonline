using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Loading
{
    /// <summary>
    /// Full-screen loading screen: destination illustration with a slow zoom, vignette, map name, ornate progress bar
    /// with eased fill, rotating rune sigil with the percentage, optional download statistics and rotating tips.
    /// Scenes draw it directly while they are loading (it opens its own sprite batch), so it does not depend on the
    /// control tree being initialised or updated.
    /// </summary>
    public sealed class LoadingScreenView : UIControl
    {
        private const float TipSeconds = 7f;
        private const float FadeSeconds = 0.45f;

        private float _progress;
        private float _shownProgress;
        private float _time;
        private int _firstTip;
        private string _art;

        public LoadingScreenView()
        {
            AutoViewSize = false;
            Interactive = false;
            _firstTip = Random.Shared.Next(LoadingArt.Tips.Count);
        }

        public float Progress
        {
            get => _progress;
            set => _progress = MathHelper.Clamp(value, 0f, 1f);
        }

        public string StatusText { get; set; } = string.Empty;

        /// <summary>Large caption at the bottom left (destination map name); hidden when empty.</summary>
        public string Title { get; set; }

        /// <summary>Small caption above <see cref="Title"/>.</summary>
        public string Subtitle { get; set; } = "Entering";

        public bool ShowStats { get; set; }
        public string SizeText { get; set; } = string.Empty;
        public string SpeedText { get; set; } = string.Empty;
        public string EtaText { get; set; } = string.Empty;

        /// <summary>Shown when no illustration is chosen or it is still loading (e.g. the client's key art).</summary>
        public Texture2D FallbackTexture { get; set; }

        /// <summary>Illustration key under <c>Ornate/Loading/</c>; see <see cref="LoadingArt"/>.</summary>
        public string Art
        {
            get => _art;
            set
            {
                _art = value;
                if (!string.IsNullOrEmpty(value))
                    _ = OrnateAssets.Get("Loading/" + value); // start decoding right away
            }
        }

        /// <summary>
        /// Decodes the loading kit (bar frame, sigil) and optionally an illustration. Await it before heavy loading
        /// starts: texture decoding shares a bounded queue with world assets, so a lazily requested illustration
        /// would otherwise appear only after most of the world has loaded.
        /// </summary>
        public static Task PreloadAsync(string art = null)
        {
            var paths = new List<string> { "Controls/progress_frame", "Controls/loading_sigil" };
            if (!string.IsNullOrEmpty(art))
                paths.Add("Loading/" + art);
            return OrnateAssets.PreloadAsync(paths);
        }

        /// <summary>Shows the illustration and name of <paramref name="worldType"/> and restarts the intro.</summary>
        public void SetDestination(Type worldType)
        {
            Art = LoadingArt.ForWorld(worldType);
            Title = LoadingArt.DisplayName(worldType);
            Restart();
        }

        /// <summary>Fades in again from black and resets the eased bar (e.g. for the next map change).</summary>
        public void Restart()
        {
            _time = 0f;
            _shownProgress = 0f;
            _progress = 0f;
            _firstTip = (_firstTip + 1) % LoadingArt.Tips.Count;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _time += dt;
            _shownProgress = _progress < _shownProgress
                ? _progress
                : _shownProgress + (_progress - _shownProgress) * Math.Min(1f, dt * 6f);

            SpriteBatch sb = GraphicsManager.Instance.Sprite;
            using var scope = new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform);

            Point screen = UiScaler.VirtualSize;
            float intro = MathHelper.Clamp(_time / FadeSeconds, 0f, 1f);

            DrawBackdrop(sb, screen, dt);
            DrawVignette(sb, screen);

            float ui = intro;
            int barY = screen.Y - 88;
            DrawTitle(sb, barY, ui);
            DrawSigil(sb, screen, barY, ui);
            DrawBar(sb, screen, barY, ui);
            DrawFooter(sb, screen, barY, ui);

            if (intro < 1f)
                sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(0, 0, screen.X, screen.Y), Color.Black * (1f - intro));
        }

        // --- Layers -------------------------------------------------------------------------------------

        // The slow push-in on an illustration runs on one clock shared by every loading view: the login, character
        // and game scenes each have their own view, and restarting the zoom at a scene change made the picture jump
        // by several pixels at the end of loading.
        private static Texture2D s_zoomTexture;
        private static float s_zoomTime;
        private static int s_zoomFrame = int.MinValue;

        private void DrawBackdrop(SpriteBatch sb, Point screen, float dt)
        {
            Texture2D art = string.IsNullOrEmpty(_art) ? null : OrnateAssets.Get("Loading/" + _art);
            Texture2D texture = art ?? (FallbackTexture is { IsDisposed: false } fallback ? fallback : null);
            if (texture == null)
            {
                sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(0, 0, screen.X, screen.Y), new Color(10, 10, 16));
                return;
            }

            if (!ReferenceEquals(texture, s_zoomTexture))
            {
                s_zoomTexture = texture;
                s_zoomTime = 0f;
            }
            else if (s_zoomFrame != MuGame.FrameIndex)
            {
                s_zoomTime += Math.Min(dt, 0.1f);   // a long loading hitch must not jump the zoom either
            }
            s_zoomFrame = MuGame.FrameIndex;

            // Cover the screen keeping the aspect, with a slow push-in on illustrations; position and size stay
            // fractional so the picture glides instead of stepping a pixel at a time.
            float zoom = art != null ? 1f + 0.06f * MathHelper.SmoothStep(0f, 1f, Math.Min(s_zoomTime / 40f, 1f)) : 1f;
            float scale = Math.Max(screen.X / (float)texture.Width, screen.Y / (float)texture.Height) * zoom;
            var size = new Vector2(texture.Width, texture.Height) * scale;
            var position = (new Vector2(screen.X, screen.Y) - size) * 0.5f;
            sb.Draw(texture, position, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static void DrawVignette(SpriteBatch sb, Point screen)
        {
            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(0, 0, screen.X, 150), Color.Black * 0.55f, Color.Transparent, 24);
            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(0, screen.Y - 300, screen.X, 300), Color.Transparent, Color.Black * 0.92f, 32);
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(0, 0, 140, screen.Y), Color.Black * 0.35f, Color.Transparent, 16);
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(screen.X - 140, 0, 140, screen.Y), Color.Transparent, Color.Black * 0.35f, 16);
        }

        private void DrawTitle(SpriteBatch sb, int barY, float a)
        {
            if (string.IsNullOrEmpty(Title))
                return;

            // Slides up slightly while fading in.
            float rise = (1f - a) * 12f;
            int x = 64;
            int y = barY - 116 + (int)rise;
            OrnateDraw.Text(sb, Subtitle?.ToUpperInvariant(), new Vector2(x + 2, y), 12.5f, OrnateStyle.GoldDim, a);
            OrnateDraw.Text(sb, Title, new Vector2(x, y + 16), 32f, OrnateStyle.Gold, a, bold: true);
            float titleWidth = OrnateDraw.Measure(Title, 32f).X;
            OrnateDraw.Divider(sb, new Rectangle(x - 30, y + 54, (int)titleWidth + 120, 22), a);
        }

        // Opening in the middle of the sigil art, as a fraction of its size (measured on loading_sigil.webp).
        private const float SigilHole = 0.41f;
        private const int ProgressDots = 40;

        private void DrawSigil(SpriteBatch sb, Point screen, int barY, float a)
        {
            const int size = 112;
            var center = new Vector2(screen.X - 64 - size / 2f, barY - 66);
            float hole = size * SigilHole * 0.5f;   // radius of the opening
            Texture2D sigil = OrnateAssets.LoadingSigil;
            if (sigil != null)
            {
                float pulse = 0.85f + 0.15f * MathF.Sin(_time * 2.4f);
                var origin = new Vector2(sigil.Width / 2f, sigil.Height / 2f);
                sb.Draw(sigil, center, null, Color.White * (a * pulse), _time * 0.55f, origin, size / (float)sigil.Width, SpriteEffects.None, 0f);
            }

            // Dark well in the opening so the number reads on any background.
            Texture2D glow = Client.Main.Objects.Effects.Scene.SceneFxTextures.Glow;
            float wellScale = hole * 2.6f / glow.Width;
            sb.Draw(glow, center, null, Color.Black * (0.85f * a), 0f, new Vector2(glow.Width, glow.Height) * 0.5f, wellScale, SpriteEffects.None, 0f);

            // Progress around the inside of the ring: dots light up clockwise from the top.
            float dotRadius = hole - 3.5f;
            int lit = (int)MathF.Round(_shownProgress * ProgressDots);
            var dotOrigin = new Vector2(glow.Width, glow.Height) * 0.5f;
            for (int i = 0; i < ProgressDots; i++)
            {
                float angle = i / (float)ProgressDots * MathHelper.TwoPi - MathHelper.PiOver2;
                var at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dotRadius;
                bool on = i < lit;
                // The newest lit dot glows a little brighter.
                float head = on && i == lit - 1 ? 1.4f : 1f;
                Color color = on ? new Color(255, 214, 130) * (a * head) : new Color(150, 100, 60) * (0.45f * a);
                sb.Draw(glow, at, null, color, 0f, dotOrigin, (on ? 7.5f : 4.5f) * head / glow.Width, SpriteEffects.None, 0f);
            }

            // Number with a small percent sign beside it, centred together inside the opening.
            string number = MathF.Round(_shownProgress * 100f).ToString();
            const float numberSize = 15f, signSize = 9f;
            Vector2 numberMeasure = OrnateDraw.Measure(number, numberSize) * new Vector2(1.06f, 1f);   // bold is a little wider
            Vector2 signMeasure = OrnateDraw.Measure("%", signSize);
            float width = numberMeasure.X + 1f + signMeasure.X;
            var numberAt = new Vector2(center.X - width / 2f, center.Y - numberMeasure.Y / 2f);
            OrnateDraw.Text(sb, number, numberAt, numberSize, new Color(255, 238, 205), a, bold: true);
            OrnateDraw.Text(sb, "%", new Vector2(numberAt.X + numberMeasure.X + 1f, numberAt.Y + numberMeasure.Y * 0.12f), signSize, OrnateStyle.Gold, a);
        }

        private void DrawBar(SpriteBatch sb, Point screen, int barY, float a)
        {
            var plate = new Rectangle(64, barY, screen.X - 128, 16);
            Texture2D frame = OrnateAssets.ProgressFrame;

            OrnateDraw.Text(sb, StatusText, new Vector2(plate.X + 6, plate.Y - 30), 13.5f, OrnateStyle.TextLight, a);

            Rectangle channel = plate;
            if (frame != null)
            {
                float texScale = OrnateDraw.Measure(frame, OrnateStyle.ProgressFrame, plate).Scale;
                int inset = (int)MathF.Round(OrnateStyle.ProgressChannelInset * frame.Width * texScale) + 1;
                channel = new Rectangle(plate.X + inset, plate.Y + 1, plate.Width - inset * 2, plate.Height - 2);
                OrnateDraw.Plate(sb, frame, OrnateStyle.ProgressFrame, plate, false, Color.White * a, overhang: 1f);
            }
            else
            {
                sb.Draw(GraphicsManager.Instance.Pixel, plate, new Color(8, 8, 10) * a);
                UiDrawHelper.DrawBorder(sb, plate, OrnateStyle.Bronze * a);
            }

            int fill = (int)MathF.Round(channel.Width * _shownProgress);
            if (fill <= 0)
                return;

            var filled = new Rectangle(channel.X, channel.Y, fill, channel.Height);
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            UiDrawHelper.DrawVerticalGradient(sb, filled, new Color(255, 218, 128) * a, new Color(168, 82, 18) * a, 8);
            sb.Draw(pixel, new Rectangle(filled.X, filled.Y, filled.Width, 1), new Color(255, 248, 210) * (0.8f * a));

            // Shimmer travelling along the filled part.
            const int shimmerWidth = 90;
            float travel = (_time * 260f) % (fill + shimmerWidth) - shimmerWidth;
            var shimmer = Rectangle.Intersect(new Rectangle(filled.X + (int)travel, filled.Y, shimmerWidth, filled.Height), filled);
            if (shimmer.Width >= 4)
            {
                int half = shimmer.Width / 2;
                UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(shimmer.X, shimmer.Y, half, shimmer.Height), Color.Transparent, Color.White * (0.28f * a), 8);
                UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(shimmer.X + half, shimmer.Y, shimmer.Width - half, shimmer.Height), Color.White * (0.28f * a), Color.Transparent, 8);
            }

            // Glowing head.
            Color glow = new(255, 170, 60);
            for (int i = 6; i >= 2; i -= 2)
                sb.Draw(pixel, new Rectangle(filled.Right - 2 - i, filled.Y - i, 4 + i * 2, filled.Height + i * 2), glow * (0.12f * a));
            sb.Draw(pixel, new Rectangle(filled.Right - 2, filled.Y, 2, filled.Height), new Color(255, 245, 210) * a);
        }

        private void DrawFooter(SpriteBatch sb, Point screen, int barY, float a)
        {
            int y = barY + 30;
            var row = new Rectangle(64, y, screen.X - 128, 20);

            if (ShowStats && !string.IsNullOrEmpty(SizeText))
            {
                OrnateDraw.TextLeft(sb, SizeText, row, 12.5f, OrnateStyle.TextMuted, a);
                OrnateDraw.TextCentered(sb, SpeedText, row, 12.5f, new Color(140, 190, 240), a);
                OrnateDraw.TextRight(sb, EtaText, row, 12.5f, OrnateStyle.TextMuted, a);
                row.Y += 24;
            }

            // Tip with a cross-fade at each change.
            float phase = _time % TipSeconds;
            float tipAlpha = MathHelper.Clamp(Math.Min(phase, TipSeconds - phase) / 0.5f, 0f, 1f) * a;
            string tip = LoadingArt.Tips[(_firstTip + (int)(_time / TipSeconds)) % LoadingArt.Tips.Count];
            Vector2 tipSize = OrnateDraw.Measure(tip, 13f);
            Vector2 labelSize = OrnateDraw.Measure("TIP", 13f);
            float start = row.Center.X - (tipSize.X + labelSize.X + 10) / 2f;
            OrnateDraw.Text(sb, "TIP", new Vector2(start, row.Y), 13f, OrnateStyle.Gold, tipAlpha, bold: true);
            OrnateDraw.Text(sb, tip, new Vector2(start + labelSize.X + 10, row.Y), 13f, OrnateStyle.TextLight, tipAlpha);
        }
    }
}
