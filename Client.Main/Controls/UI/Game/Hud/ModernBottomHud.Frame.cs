#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Ornate;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Hud
{
    // Ornate frame of the Modern HUD:
    //
    //   [PARTY][GUILD][FRIENDS]  (HP orb)=[ SD gauge | AG gauge ]=(MP orb)  [MENU][CHAR][INV]
    //                                    [Q][W][E]  [1] ... [0]
    //                                    [========= EXP =========]
    //
    // The orbs sit in winged rings at both ends of the slot bar; everything is centred at the bottom of the screen.
    public sealed partial class ModernBottomHud
    {
        private const int SlotSize = 40, SlotGap = 3, PotionGroupGap = 10;
        private const int OrbDiameter = 112;
        private const int BarBandHeight = 80, BarEndOverlap = 70;
        private const int ExpHeight = 12, BottomMargin = 2;
        private const int MenuButtonSize = 42, MenuButtonGap = 4, TrayPadding = 18, TrayBandHeight = 58;

        // Measured on Hud/bar (1024x142): rails band, plain stretch segment and the centre gem ornament.
        private static readonly PlateLayout BarLayout = new(0.275f, 0.915f, 0.06f, 0.31f, 0.40f, 0.41f, 0.59f);
        private const float BarChannelTop = 0.154f, BarChannelBottom = 0.835f; // of the band height

        // Measured on Hud/orb_ring (384x351): centre of the round opening and its diameter (fractions of the width).
        private static readonly Vector3 OrbRingHole = new(0.621f, 0.463f, 0.625f);

        private static readonly string[] MenuIcons = { "settings", "character", "inventory", "party", "guild", "friends" };
        private static readonly string[] MenuHints = { "Menu (Esc)", "Character (C)", "Inventory (I)", "Party", "Guild (G)", "Friends (F)" };

        private static readonly Color SdFill = new(236, 204, 96), SdFillDark = new(120, 92, 30);
        private static readonly Color AgFill = new(176, 120, 236), AgFillDark = new(70, 36, 112);

        private Rectangle _leftTrayRect, _rightTrayRect;
        private Rectangle _bandRect, _hpOrbRect, _mpOrbRect, _hpRingRect, _mpRingRect;
        private float _trailHpPct, _trailMpPct;
        private double _hpLostAt, _mpLostAt;

        private static readonly string[] FramePreload =
        {
            "Hud/bar", "Hud/orb_ring", "Hud/slot", "Hud/slot_active", HudOrb.NoiseTexture,
            "Emblems/settings", "Emblems/character", "Emblems/inventory", "Emblems/party", "Emblems/guild", "Emblems/friends",
        };

        private void RefreshLayout()
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (virtualSize == _lastVirtualSize)
                return;

            _lastVirtualSize = virtualSize;
            _ = OrnateAssets.PreloadAsync(FramePreload);

            int vw = virtualSize.X;
            int vh = virtualSize.Y;
            _slotFontScale = 0.36f;

            // Slot row, centred.
            int slotsWidth = SlotCount * SlotSize + (SlotCount - 1) * SlotGap + PotionGroupGap;
            int slotsLeft = (vw - slotsWidth) / 2;

            // Bar band around the slots; its ends disappear under the orbs.
            int expTop = vh - BottomMargin - ExpHeight;
            int bandTop = expTop - 2 - BarBandHeight;
            _bandRect = new Rectangle(slotsLeft - BarEndOverlap, bandTop, slotsWidth + BarEndOverlap * 2, BarBandHeight);
            _panelRect = _bandRect;

            int channelTop = bandTop + (int)(BarBandHeight * BarChannelTop);
            int channelBottom = bandTop + (int)(BarBandHeight * BarChannelBottom);

            // SD / AG gauges on top of the channel, slots under them.
            const int gaugeHeight = 7;
            int gaugeY = channelTop + 2;
            int half = slotsWidth / 2;
            _sdBarRect = new Rectangle(slotsLeft + 2, gaugeY, half - 8, gaugeHeight);
            _agBarRect = new Rectangle(slotsLeft + half + 6, gaugeY, half - 8, gaugeHeight);

            int slotY = Math.Max(gaugeY + gaugeHeight + 3, channelBottom - SlotSize - 1);
            _slotRects = new Rectangle[SlotCount];
            int sx = slotsLeft;
            for (int i = 0; i < SlotCount; i++)
            {
                _slotRects[i] = new Rectangle(sx, slotY, SlotSize, SlotSize);
                sx += SlotSize + SlotGap;
                if (i == PotionSlotCount - 1)
                    sx += PotionGroupGap;
            }

            // Orbs: rings stand on the bottom edge; the opening centres the orb.
            float ringScale = OrbDiameter / (OrbRingHole.Z * 384f);
            int ringW = (int)MathF.Round(384f * ringScale), ringH = (int)MathF.Round(351f * ringScale);
            int ringTop = vh - ringH + 2;
            int orbCenterY = ringTop + (int)MathF.Round(OrbRingHole.Y * ringH);
            int hpCenterX = _bandRect.X + 12, mpCenterX = _bandRect.Right - 12;

            _hpOrbRect = new Rectangle(hpCenterX - OrbDiameter / 2, orbCenterY - OrbDiameter / 2, OrbDiameter, OrbDiameter);
            _mpOrbRect = new Rectangle(mpCenterX - OrbDiameter / 2, orbCenterY - OrbDiameter / 2, OrbDiameter, OrbDiameter);
            _hpRingRect = new Rectangle(hpCenterX - (int)MathF.Round(OrbRingHole.X * ringW), ringTop, ringW, ringH);
            _mpRingRect = new Rectangle(mpCenterX - (ringW - (int)MathF.Round(OrbRingHole.X * ringW)), ringTop, ringW, ringH);
            _hpBarRect = _hpOrbRect;
            _mpBarRect = _mpOrbRect;

            _expBarRect = new Rectangle(_hpOrbRect.Center.X + 20, expTop, _mpOrbRect.Center.X - _hpOrbRect.Center.X - 40, ExpHeight);

            // Menu medallions on short bars beside the rings: Party, Guild, Friends left; Menu, Character, Inventory right.
            _btnRects = new Rectangle[ButtonLabels.Length];
            int trayWidth = MenuButtonSize * 3 + MenuButtonGap * 2 + TrayPadding * 2;
            int trayTop = _bandRect.Bottom - TrayBandHeight;
            // On narrow screens the trays slide under the ring wings instead of leaving the screen.
            _leftTrayRect = new Rectangle(Math.Max(4, _hpRingRect.X - 4 - trayWidth), trayTop, trayWidth, TrayBandHeight);
            _rightTrayRect = new Rectangle(Math.Min(vw - 4 - trayWidth, _mpRingRect.Right + 4), trayTop, trayWidth, TrayBandHeight);
            int buttonsY = trayTop + (int)(TrayBandHeight * (BarChannelTop + BarChannelBottom) / 2f) - MenuButtonSize / 2;
            for (int i = 0; i < 3; i++)
            {
                int offset = TrayPadding + i * (MenuButtonSize + MenuButtonGap);
                _btnRects[3 + i] = new Rectangle(_leftTrayRect.X + offset, buttonsY, MenuButtonSize, MenuButtonSize);
                _btnRects[i] = new Rectangle(_rightTrayRect.X + offset, buttonsY, MenuButtonSize, MenuButtonSize);
            }

            int top = Math.Min(_hpRingRect.Y, bandTop - 30);
            X = 0;
            Y = top;
            ControlSize = new Point(vw, vh - top);
            ViewSize = ControlSize;
        }

        /// <summary>True where the HUD art covers the point (clicks there do not reach the world).</summary>
        private bool IsOverFrame(Point p) =>
            _bandRect.Contains(p) || _expBarRect.Contains(p) || _leftTrayRect.Contains(p) || _rightTrayRect.Contains(p) ||
            InCircle(_hpOrbRect, p) || InCircle(_mpOrbRect, p);

        private static bool InCircle(Rectangle r, Point p)
        {
            float dx = p.X - r.Center.X, dy = p.Y - r.Center.Y, radius = r.Width / 2f;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>Recently lost HP/MP stays visible as a pale band that drains after a short delay.</summary>
        private void UpdateOrbTrails(float dt)
        {
            static float Step(float trail, float target, ref double lostAt, double now, float dt)
            {
                if (target >= trail)
                    return target;
                if (lostAt <= 0)
                    lostAt = now;
                if (now - lostAt < 0.6)
                    return trail;
                float next = trail - Math.Max(0.18f, (trail - target) * 1.4f) * dt;
                if (next <= target)
                {
                    lostAt = 0;
                    return target;
                }
                return next;
            }

            if (_displayHpPct > _trailHpPct - 0.001f)
                _hpLostAt = 0;
            if (_displayMpPct > _trailMpPct - 0.001f)
                _mpLostAt = 0;
            _trailHpPct = Step(_trailHpPct, _displayHpPct, ref _hpLostAt, _totalTime, dt);
            _trailMpPct = Step(_trailMpPct, _displayMpPct, ref _mpLostAt, _totalTime, dt);
        }

        private void DrawFrame(SpriteBatch sb)
        {
            float alpha = Alpha;
            float time = (float)_totalTime;

            DrawShade(sb, alpha);

            // Bar and its channel.
            Texture2D? bar = OrnateAssets.Get("Hud/bar");
            if (bar != null)
                OrnateDraw.Plate(sb, bar, BarLayout, _bandRect, false, Color.White * alpha, overhang: 1f);
            else
                OrnateDraw.Inset(sb, _bandRect, alpha, 0.8f);

            DrawMenuButtons(sb, alpha); // before the rings, which may overlap the trays on narrow screens
            DrawGauge(sb, _sdBarRect, _displaySdPct, SdFillDark, SdFill, time, alpha);
            DrawGauge(sb, _agBarRect, _displayAgPct, AgFillDark, AgFill, time, alpha);
            DrawQuickSlots(sb, GraphicsManager.Instance.Pixel);
            DrawExp(sb, time, alpha);

            // Orbs, then their rings on top (the ring's lip hides the orb edge).
            float hpPulse = _targetHpPct < 0.25f && _state.MaximumHealth > 0 ? 0.22f * (0.5f + 0.5f * MathF.Sin(time * 6.5f)) : 0f;
            float mpPulse = _targetMpPct < 0.15f && _state.MaximumMana > 0 ? 0.15f * (0.5f + 0.5f * MathF.Sin(time * 5f)) : 0f;
            HudOrb.Draw(sb, _hpOrbRect, _displayHpPct, _trailHpPct, HudOrb.Health, hpPulse, time, alpha);
            HudOrb.Draw(sb, _mpOrbRect, _displayMpPct, _trailMpPct, HudOrb.Mana, mpPulse, time + 17f, alpha);
            DrawRing(sb, _hpRingRect, false, hpPulse, alpha);
            DrawRing(sb, _mpRingRect, true, 0f, alpha);

            if (_state.MaximumHealth > 0)
                DrawOrbText(sb, _hpOrbRect, _healthText, _healthMaxText, alpha);
            if (_state.MaximumMana > 0)
                DrawOrbText(sb, _mpOrbRect, _manaText, _manaMaxText, alpha);

            DrawHoverHint(sb, alpha);
        }

        /// <summary>
        /// Soft shade under the whole HUD so it reads over bright ground: darker toward the bottom edge and fading out
        /// sideways past the trays, so it has no visible edge.
        /// </summary>
        private void DrawShade(SpriteBatch sb, float alpha)
        {
            const int fade = 90, strips = 18;
            int vw = UiScaler.VirtualSize.X, vh = UiScaler.VirtualSize.Y;
            int top = _bandRect.Y - 16;
            int left = _leftTrayRect.X + 30, right = _rightTrayRect.Right - 30;
            Color dark = Color.Black * (0.5f * alpha);

            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(left, top, right - left, vh - top), Color.Transparent, dark, 16);
            int stripWidth = (int)MathF.Ceiling(fade / (float)strips);
            for (int i = 0; i < strips; i++)
            {
                float t = 1f - (i + 0.5f) / strips;      // 1 next to the centre part, 0 at the outer end
                Color c = dark * (t * t);
                int offset = i * stripWidth;
                var l = new Rectangle(left - offset - stripWidth, top, stripWidth, vh - top);
                var r = new Rectangle(right + offset, top, stripWidth, vh - top);
                if (l.Right > 0)
                    UiDrawHelper.DrawVerticalGradient(sb, l, Color.Transparent, c, 8);
                if (r.X < vw)
                    UiDrawHelper.DrawVerticalGradient(sb, r, Color.Transparent, c, 8);
            }
        }

        private static void DrawRing(SpriteBatch sb, Rectangle rect, bool mirrored, float warning, float alpha)
        {
            Texture2D? ring = OrnateAssets.Get("Hud/orb_ring");
            if (ring == null)
                return;

            Color tint = Color.Lerp(Color.White, new Color(255, 150, 140), Math.Min(1f, warning * 3f));
            sb.Draw(ring, rect, null, tint * alpha, 0f, Vector2.Zero, mirrored ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        private static void DrawOrbText(SpriteBatch sb, Rectangle orb, string value, string max, float alpha)
        {
            Vector2 valueSize = OrnateDraw.Measure(value, 16f);
            Vector2 maxSize = OrnateDraw.Measure(max, 11f);
            float y = orb.Center.Y - (valueSize.Y + maxSize.Y - 4f) / 2f + 4f;
            OrnateDraw.Text(sb, value, new Vector2(orb.Center.X - valueSize.X / 2f, y), 16f, Color.White, alpha, bold: true);
            OrnateDraw.Text(sb, max, new Vector2(orb.Center.X - maxSize.X / 2f, y + valueSize.Y - 4f), 11f, new Color(225, 215, 200), alpha);
        }

        /// <summary>Slim gauge (SD, AG): dark track with a bronze rim, gradient fill and a slow sheen.</summary>
        private static void DrawGauge(SpriteBatch sb, Rectangle rect, float pct, Color dark, Color bright, float time, float alpha)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            sb.Draw(pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), new Color(110, 84, 46) * (0.9f * alpha));
            sb.Draw(pixel, rect, new Color(8, 7, 6) * alpha);

            int fill = (int)MathF.Round(rect.Width * MathHelper.Clamp(pct, 0f, 1f));
            if (fill <= 0)
                return;

            var filled = new Rectangle(rect.X, rect.Y, fill, rect.Height);
            UiDrawHelper.DrawVerticalGradient(sb, filled, bright * alpha, dark * alpha, 8);
            sb.Draw(pixel, new Rectangle(filled.X, filled.Y, filled.Width, 1), Color.White * (0.35f * alpha));

            // Sheen sweeping along the filled part.
            float phase = time * 0.35f % 1.6f - 0.3f;
            int sheenX = rect.X + (int)(phase * rect.Width);
            var sheen = Rectangle.Intersect(new Rectangle(sheenX, rect.Y, 24, rect.Height), filled);
            if (sheen.Width > 0)
                UiDrawHelper.DrawHorizontalGradient(sb, sheen, Color.Transparent, Color.White * (0.35f * alpha), 8);
        }

        private double ExperiencePercent()
        {
            if (_state.ExperienceForNextLevel == 0)
                return 0;

            ushort level = _state.Level;
            ulong previous = level > 1 ? (ulong)((level - 1 + 9) * (level - 1) * (level - 1) * 10) : 0;
            ulong inLevel = _state.Experience >= previous ? _state.Experience - previous : 0;
            ulong needed = _state.ExperienceForNextLevel >= previous ? _state.ExperienceForNextLevel - previous : 1;
            return needed > 0 ? inLevel / (double)needed * 100.0 : 0.0;
        }

        private void DrawExp(SpriteBatch sb, float time, float alpha)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            Rectangle rect = _expBarRect;
            sb.Draw(pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black * (0.85f * alpha));
            UiDrawHelper.DrawBorder(sb, rect, new Color(110, 84, 46) * alpha);
            Rectangle track = new(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
            sb.Draw(pixel, track, new Color(10, 9, 8) * alpha);

            double percent = ExperiencePercent();
            int fill = (int)(track.Width * MathHelper.Clamp((float)(percent / 100.0), 0f, 1f));
            if (fill > 0)
            {
                var filled = new Rectangle(track.X, track.Y, fill, track.Height);
                UiDrawHelper.DrawVerticalGradient(sb, filled, new Color(250, 214, 120) * alpha, new Color(132, 92, 30) * alpha, 8);
                sb.Draw(pixel, new Rectangle(filled.Right - 2, filled.Y, 2, filled.Height), new Color(255, 240, 190) * (0.8f * alpha));
                float phase = time * 0.18f % 1.4f - 0.2f;
                var sheen = Rectangle.Intersect(new Rectangle(track.X + (int)(phase * track.Width), track.Y, 40, track.Height), filled);
                if (sheen.Width > 0)
                    UiDrawHelper.DrawHorizontalGradient(sb, sheen, Color.Transparent, Color.White * (0.3f * alpha), 8);
            }

            for (int i = 1; i < 10; i++)
            {
                int x = track.X + track.Width * i / 10;
                sb.Draw(pixel, new Rectangle(x, track.Y, 1, track.Height), Color.Black * (0.45f * alpha));
            }

            string text = $"Lv. {_state.Level}   {percent:F1}%";
            OrnateDraw.TextCentered(sb, text, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height + 1), 10f, new Color(255, 236, 190), alpha);
        }

        private void DrawQuickSlots(SpriteBatch sb, Texture2D pixel)
        {
            float alpha = Alpha;
            Texture2D? frame = OrnateAssets.Get("Hud/slot");
            Texture2D? activeFrame = OrnateAssets.Get("Hud/slot_active");

            for (int i = 0; i < _slotRects.Length; i++)
            {
                Rectangle rect = _slotRects[i];
                bool active = i == _activeSkillSlot;
                bool hover = i == _hoveredSlot;
                var inner = new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);

                UiDrawHelper.DrawVerticalGradient(sb, inner, new Color(30, 26, 22) * alpha, new Color(10, 9, 8) * alpha, 12);
                if (hover)
                    sb.Draw(pixel, inner, new Color(255, 214, 128) * (0.08f * alpha));

                if (i >= PotionSlotCount && _slotSkills[i] != null)
                    DrawSkillIcon(sb, inner, _slotSkills[i]!);
                if (i < PotionSlotCount)
                    DrawPotionSlotContent(sb, pixel, inner, i);

                Texture2D? texture = active ? activeFrame ?? frame : frame;
                if (texture != null)
                {
                    Color tint = active ? Color.White : hover ? new Color(255, 240, 210) : new Color(200, 190, 175);
                    sb.Draw(texture, new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), tint * alpha);
                }
                else
                {
                    UiDrawHelper.DrawBorder(sb, rect, (active ? OrnateStyle.Gold : OrnateStyle.Bronze) * alpha);
                }

                if (active)
                {
                    float pulse = 0.12f + 0.08f * MathF.Sin((float)_totalTime * 3f);
                    UiDrawHelper.DrawBorder(sb, inner, OrnateStyle.Gold * (pulse * 3f * alpha));
                }

                Color keyColor = active ? OrnateStyle.Gold : hover ? Color.White : new Color(200, 190, 175);
                OrnateDraw.Text(sb, SlotKeyLabels[i], new Vector2(rect.X + 4, rect.Y + 2), 10f, keyColor, alpha, bold: true);
            }
        }

        private void DrawMenuButtons(SpriteBatch sb, float alpha)
        {
            Texture2D? bar = OrnateAssets.Get("Hud/bar");
            foreach (Rectangle tray in new[] { _leftTrayRect, _rightTrayRect })
            {
                if (bar != null)
                    OrnateDraw.Plate(sb, bar, BarLayout, tray, false, Color.White * alpha, overhang: 1f);
                else
                    OrnateDraw.Inset(sb, tray, alpha, 0.8f);
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                Rectangle rect = _btnRects[i];
                bool hover = i == _hoveredButton;
                if (hover)
                {
                    // Warm light behind the medallion, then the medallion slightly enlarged.
                    Rectangle glow = rect;
                    glow.Inflate(6, 6);
                    UiDrawHelper.DrawVerticalGradient(sb, glow, new Color(255, 190, 90) * (0.22f * alpha), new Color(255, 150, 60) * (0.05f * alpha), 8);
                    rect.Inflate(3, 3);
                }

                // The tray is the frame: draw the medallion's inside only, so the motif uses the whole button.
                Texture2D? emblem = OrnateAssets.Emblem(MenuIcons[i]);
                if (emblem != null)
                {
                    sb.Draw(emblem, rect, (hover ? Color.White : new Color(225, 218, 205)) * alpha);
                }
                else
                {
                    OrnateDraw.Icon(sb, MenuIcons[i], rect, alpha);
                }
            }
        }

        private void DrawHoverHint(SpriteBatch sb, float alpha)
        {
            Point mouse = PointerForHitTest(MuGame.Instance.UiMouseState.Position);
            if (_hoveredButton >= 0)
            {
                OrnateItemUi.Hint(sb, _btnRects[_hoveredButton], MenuHints[_hoveredButton], alpha);
                return;
            }

            if (_sdBarRect.Contains(mouse) && _state.MaximumShield > 0)
                OrnateItemUi.Hint(sb, _sdBarRect, $"Shield (SD)  {_state.CurrentShield} / {_state.MaximumShield}", alpha, SdFill);
            else if (_agBarRect.Contains(mouse) && _state.MaximumAbility > 0)
                OrnateItemUi.Hint(sb, _agBarRect, $"Ability (AG)  {_state.CurrentAbility} / {_state.MaximumAbility}", alpha, AgFill);
            else if (InCircle(_hpOrbRect, mouse))
                OrnateItemUi.Hint(sb, _hpOrbRect, $"Health  {_state.CurrentHealth} / {_state.MaximumHealth}", alpha, new Color(255, 150, 130));
            else if (InCircle(_mpOrbRect, mouse))
                OrnateItemUi.Hint(sb, _mpOrbRect, $"Mana  {_state.CurrentMana} / {_state.MaximumMana}", alpha, new Color(150, 190, 255));
            else if (_expBarRect.Contains(mouse))
                OrnateItemUi.Hint(sb, _expBarRect, $"Experience  {ExperiencePercent():F2}%  (Level {_state.Level})", alpha, OrnateStyle.Gold);
        }
    }
}
