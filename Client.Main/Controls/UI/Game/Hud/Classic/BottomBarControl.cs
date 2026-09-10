using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Bottom HUD bar based on the MU Immortal main menu layout:
    /// footer plate, full-width EXP bar, HP/MP crystals, and five consumable slots.
    /// Uses the extracted atlas artwork where available.
    /// </summary>
    public sealed class BottomBarControl : UIControl
    {
        private const string FooterPath = "Interface/DH/mi_footer.OZP";
        private const string ExpBgPath = "Interface/DH/mi_exp_bg.OZP";
        private const string ExpFillPath = "Interface/DH/mi_exp_fill.OZP";
        private const string HpCrystalPath = "Interface/DH/mi_hp_crystal.OZP";
        private const string HpCrystalGreyPath = "Interface/DH/mi_hp_crystal_grey.OZP";
        private const string MpCrystalPath = "Interface/DH/mi_mp_crystal.OZP";
        private const string MpCrystalGreyPath = "Interface/DH/mi_mp_crystal_grey.OZP";
        private const string SlotPath = "Interface/DH/mi_slot.OZP";
        private const string ClawLeftPath = "Interface/DH/mi_claw_left.OZP";
        private const string ClawRightPath = "Interface/DH/mi_claw_right.OZP";
        // SD/AG bars: the left bar is green with a right ornament; the right bar is
        // wine-colored with a left ornament.
        private const string BarLeftPath = "Interface/DH/mi_bar_left.OZP";
        private const string BarRightPath = "Interface/DH/mi_bar_right.OZP";
        // Full variants generated from the artwork; the trough is brightened while
        // cantos diagonais e sombreado). O preenchimento = recorte destas.
        private const string BarLeftFullPath = "Interface/DH/mi_bar_left_full.OZP";
        private const string BarRightFullPath = "Interface/DH/mi_bar_right_full.OZP";
        // End gradients from the artwork, drawn over the bar.
        private const string BarFadeLeftPath = "Interface/DH/mi_bar_fade_left.OZP";
        private const string BarFadeRightPath = "Interface/DH/mi_bar_fade_right.OZP";
        // Thin strips drawn last to hide gradient spill.
        private const string BarOverlayLeftPath = "Interface/DH/mi_bar_overlay_left.OZP";
        private const string BarOverlayRightPath = "Interface/DH/mi_bar_overlay_right.OZP";

        // Dimensions are driven by BottomBarLayout.
        private static int CrystalW => (int)BottomBarLayout.CrystalW;
        private static int CrystalH => (int)BottomBarLayout.CrystalH;
        private static int SlotSize => (int)BottomBarLayout.SlotSize;
        private static int SlotGap => (int)BottomBarLayout.SlotGap;
        private static int SlotCount => BottomBarLayout.SlotCount;
        private static int ExpH => (int)BottomBarLayout.ExpH;

        private Texture2D _texFooter, _texExpBg, _texExpFill;
        private Texture2D _texHpCrystal, _texHpGrey, _texMpCrystal, _texMpGrey, _texSlot;
        private Texture2D _texBarLeft, _texBarRight, _texBarLeftFull, _texBarRightFull;
        private Texture2D _texBarFadeLeft, _texBarFadeRight;
        private Texture2D _texBarOverlayLeft, _texBarOverlayRight;
        // CPU-mirrored copies of the right-side artwork. The right bar uses these with
        // FlipHorizontally so PointClamp sampling remains symmetrical while preserving
        // the right-side wine fill and the left-side gold fill.
        private Texture2D _texBarRightA, _texBarRightFullA, _texBarOverlayRightA;
        private Texture2D _texClawLeft, _texClawRight;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);

        private static float HudTextWidth(SpriteFont font, string text, float scale)
        {
            return font.MeasureString(text).X * scale;
        }

        private static void DrawHudText(SpriteBatch sb, SpriteFont font, string text,
            Vector2 position, float scale, Color color)
        {
            sb.DrawString(font, text, position + Vector2.One, Color.Black * 0.7f,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, text, position, color,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private readonly CharacterState _state;
        // Dono do ESTADO dos slots de poção (no mobile o ModernBottomHud é headless — não
        // desenha, mas guarda/persiste as atribuições feitas na tela Potion Imprint).
        private readonly ModernBottomHud _hud;

        public BottomBarControl(CharacterState state, ModernBottomHud hud = null)
        {
            _state = state;
            _hud = hud;
            Interactive = true;
            AutoViewSize = false;
            // Faixa full-width na base.
            ControlSize = new Point(UiScaler.VirtualSize.X, 110);
            ViewSize = ControlSize;
            X = 0;
            Y = UiScaler.VirtualSize.Y - 110;
        }

        public override bool OnClick()
        {
            base.OnClick();
            return true;
        }

        public override async Task Load()
        {
            await base.Load();
            if (_loadedTheme == UiThemeManager.CurrentId)
                return;
            async Task<Texture2D> L(string p) { try { return await UiThemeManager.LoadThemeTextureAsync(p); } catch { return null; } }
            _texFooter = await L(FooterPath);
            _texExpBg = await L(ExpBgPath);
            _texExpFill = await L(ExpFillPath);
            _texBarLeft = await L(BarLeftPath);
            _texBarRight = await L(BarRightPath);
            _texBarLeftFull = await L(BarLeftFullPath);
            _texBarRightFull = await L(BarRightFullPath);
            _texBarOverlayLeft = await L(BarOverlayLeftPath);
            _texBarOverlayRight = await L(BarOverlayRightPath);
            _texBarFadeLeft = await L(BarFadeLeftPath);
            _texBarFadeRight = await L(BarFadeRightPath);
            _texHpCrystal = await L(HpCrystalPath);
            _texHpGrey = await L(HpCrystalGreyPath);
            _texMpCrystal = await L(MpCrystalPath);
            _texMpGrey = await L(MpCrystalGreyPath);
            _texSlot = await L(SlotPath);
            _texClawLeft = await L(ClawLeftPath);
            _texClawRight = await L(ClawRightPath);
            DisposeMirroredTextures();
            _texBarRightA = FlipHorizontal(_texBarRight);
            _texBarRightFullA = FlipHorizontal(_texBarRightFull);
            _texBarOverlayRightA = FlipHorizontal(_texBarOverlayRight);
            _loadedTheme = UiThemeManager.CurrentId;
        }

        public override void Dispose()
        {
            DisposeMirroredTextures();
            base.Dispose();
        }

        private void DisposeMirroredTextures()
        {
            _texBarRightA?.Dispose();
            _texBarRightFullA?.Dispose();
            _texBarOverlayRightA?.Dispose();
            _texBarRightA = null;
            _texBarRightFullA = null;
            _texBarOverlayRightA = null;
        }

        // Espelho horizontal exato em CPU (uma vez, no Load).
        private static Texture2D FlipHorizontal(Texture2D src)
        {
            if (src == null || src.IsDisposed) return null;
            var data = new Color[src.Width * src.Height];
            src.GetData(data);
            var flipped = new Color[data.Length];
            for (int y = 0; y < src.Height; y++)
            {
                int row = y * src.Width;
                for (int x = 0; x < src.Width; x++)
                    flipped[row + x] = data[row + (src.Width - 1 - x)];
            }
            var tex = new Texture2D(src.GraphicsDevice, src.Width, src.Height);
            tex.SetData(flipped);
            return tex;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || _state == null) return;
            var sb = GraphicsManager.Instance?.Sprite;
            var pixel = GraphicsManager.Instance?.Pixel;
            var font = GraphicsManager.Instance?.Font;
            if (sb == null || pixel == null) return;

            int scrW = UiScaler.VirtualSize.X;
            int scrH = UiScaler.VirtualSize.Y;

            // HUD reformulado (mock do usuário): placa escura CURTA atrás do cluster
            // (as pontas cortadas terminam ATRÁS dos cristais de HP/MP, que cobrem);
            // tudo colado na base; EXP = barra de progresso de verdade (trilho visível
            // quando vazia, preenchimento dourado sólido) na largura dos 5 slots.

            // ── layout do cluster central (tudo relativo à BASE da tela) ─
            int slotsW = SlotCount * SlotSize + (SlotCount - 1) * SlotGap;
            int sx = (scrW - slotsW) / 2;
            int expW = BottomBarLayout.ExpW > 0 ? (int)BottomBarLayout.ExpW : slotsW;
            var expRect = new Rectangle(sx + (slotsW - expW) / 2,
                                        scrH - (int)BottomBarLayout.ExpBottom - ExpH, expW, ExpH);
            int sy = scrH - (int)BottomBarLayout.SlotsBottom - SlotSize;       // topo dos slots
            int crystalY = scrH - (int)BottomBarLayout.CrystalBottom - CrystalH;
            int cgap = (int)BottomBarLayout.CrystalGap;
            var hpRect = new Rectangle(sx - CrystalW - cgap, crystalY, CrystalW, CrystalH);
            var mpRectPlate = new Rectangle(sx + slotsW + cgap, crystalY, CrystalW, CrystalH);

            // ── placa de fundo curta (do meio do HP ao meio do MP) ──────
            if (BottomBarLayout.PlateEnabled)
            {
                int inset = (int)BottomBarLayout.PlateInset;
                int plateTop = sy - (int)BottomBarLayout.PlateTopPad;
                var plate = new Rectangle(hpRect.Center.X + inset, plateTop,
                                          (mpRectPlate.Center.X - inset) - (hpRect.Center.X + inset),
                                          scrH - plateTop);
                if (_texFooter != null && !_texFooter.IsDisposed)
                    sb.Draw(_texFooter, plate, Color.White);
                else
                    sb.Draw(pixel, plate, new Color(14, 13, 11) * 0.92f);
            }

            // ── EXP: barra de progresso (trilho + fill dourado uniforme) ─
            float expRate = _state.ExperienceForNextLevel > 0
                ? MathHelper.Clamp((float)(_state.Experience / (double)_state.ExperienceForNextLevel), 0f, 1f) : 0f;
            // Depois do 400 o char upa MASTER LEVEL: a barra e o % passam a contar o Master EXP.
            bool masterMode = _state.Level >= 400;
            if (masterMode && _state.MasterExperienceForNextLevel > 0)
                expRate = MathHelper.Clamp(
                    (float)(_state.MasterExperience / (double)_state.MasterExperienceForNextLevel), 0f, 1f);
            // trilho (estado vazio): interior cinza-escuro com borda visível
            if (BottomBarLayout.ExpEnabled)
            {
            sb.Draw(pixel, expRect, new Color(88, 84, 70));                       // borda
            sb.Draw(pixel, new Rectangle(expRect.X + 1, expRect.Y + 1, expRect.Width - 2, expRect.Height - 2),
                new Color(38, 36, 30));                                           // interior
            int expFillW = (int)((expRect.Width - 2) * expRate);
            if (expFillW > 0)
            {
                // preenchimento: OURO ENVELHECIDO/bronze combinando com os ícones do HUD,
                // com BRILHO — miolo mais luminoso + linha de highlight especular no topo
                // (metal polido), pra não ficar apagado.
                var fill = new Rectangle(expRect.X + 1, expRect.Y + 1, expFillW, expRect.Height - 2);
                sb.Draw(pixel, fill, new Color(74, 54, 32));                      // contorno (bronze escuro)
                if (fill.Width > 2 && fill.Height > 2)
                {
                    var core = new Rectangle(fill.X + 1, fill.Y + 1, fill.Width - 2, fill.Height - 2);
                    sb.Draw(pixel, core, new Color(198, 152, 86));                // miolo (ouro mais luminoso)
                    // brilho especular: faixa clara na metade de cima (aspecto polido).
                    sb.Draw(pixel, new Rectangle(core.X, core.Y, core.Width, Math.Max(1, core.Height / 2)),
                        new Color(245, 205, 120) * 0.7f);
                }
            }
            }

            // Level and experience text in the lower-left corner. Use the same
            // resolution-independent SpriteFont path as the rest of the UI.
            {
                // Before level 400: "Lv.299" / "EXP 52.4%".
                // At master level: "Lv.400  ML.95" / "Master EXP 41.8%".
                string nv = $"Lv.{_state.Level}";
                string ml = masterMode ? $"ML.{_state.MasterLevel}" : null;
                string expLbl = masterMode ? "Master EXP " : "EXP ";
                string pct = $"{expRate * 100f:0.0}%";
                SpriteFont levelFont = GraphicsManager.GetUiFont(BottomBarLayout.LevelFont, out float levelScale);
                SpriteFont expFont = GraphicsManager.GetUiFont(BottomBarLayout.ExpTextFont, out float expScale);
                if (levelFont == null || expFont == null)
                    return;

                float line2Y = scrH - BottomBarLayout.LevelTextBottom;
                float line1Y = line2Y - expFont.LineSpacing * expScale - 4;
                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);
                float x = BottomBarLayout.LevelTextX;
                float levelY = line1Y - levelFont.MeasureString(nv).Y * levelScale;
                DrawHudText(sb, levelFont, nv, new Vector2(x, levelY), levelScale, Color.White);
                if (ml != null)
                {
                    x += HudTextWidth(levelFont, nv, levelScale) + 14;
                    DrawHudText(sb, levelFont, ml, new Vector2(x, levelY), levelScale, Color.White);
                }
                x = BottomBarLayout.LevelTextX;
                float expY = line2Y - expFont.MeasureString(expLbl).Y * expScale;
                DrawHudText(sb, expFont, expLbl, new Vector2(x, expY), expScale, new Color(190, 190, 190));
                x += HudTextWidth(expFont, expLbl, expScale);
                DrawHudText(sb, expFont, pct, new Vector2(x, expY), expScale, new Color(198, 152, 86));
                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, UiScaler.SpriteTransform);
            }

            // Slots de poção: a arte mi_slot (119x115) é reduzida p/ SlotSize (~49). Com
            // PointClamp o downscale serrilha a borda de forma DEPENDENTE da resolução física
            // (BlueStacks ficava com borda dupla/grossa; emulador limpo). LinearClamp suaviza
            // e dá borda idêntica em qualquer device.
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);
            for (int i = 0; i < SlotCount; i++)
            {
                var slotRect = new Rectangle(sx + i * (SlotSize + SlotGap), sy, SlotSize, SlotSize);
                if (_texSlot != null && !_texSlot.IsDisposed)
                    sb.Draw(_texSlot, slotRect, Color.White);
                else
                {
                    sb.Draw(pixel, slotRect, new Color(20, 22, 28) * 0.85f);
                    sb.Draw(pixel, new Rectangle(slotRect.X, slotRect.Y, slotRect.Width, 1), new Color(120, 100, 60));
                }

                // Poção atribuída (tela Potion Imprint, estado no ModernBottomHud headless):
                // ícone (preview BMD pré-gerado no Update do hud) + contagem de estoque.
                var assign = _hud?.GetItemAssignmentAt(i);
                if (assign.HasValue)
                {
                    var icon = _hud.GetItemIconByKey(assign.Value.Group, assign.Value.Id);
                    int count = _hud.CountItemInInventory(assign.Value.Group, assign.Value.Id);
                    if (icon != null && !icon.IsDisposed)
                    {
                        int pad = Math.Max(2, SlotSize / 10);
                        var iconRect = new Rectangle(slotRect.X + pad, slotRect.Y + pad,
                            slotRect.Width - pad * 2, slotRect.Height - pad * 2);
                        // Sem estoque no inventário: ícone apagado (cinza).
                        sb.Draw(icon, iconRect, count > 0 ? Color.White : new Color(90, 90, 90));
                    }
                    if (font != null && count > 0)
                    {
                        string cnt = count.ToString();
                        const float csc = 0.36f;
                        var csz = font.MeasureString(cnt) * csc;
                        var cpos = new Vector2(slotRect.Right - csz.X - 3, slotRect.Bottom - csz.Y - 2);
                        DrawOutlined(sb, font, cnt, cpos, csc, Color.White, 1);
                    }
                }
            }
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, UiScaler.SpriteTransform);

            // ── Barras SD/AG (arte do usuário) ───────────────────────────
            // Ficam ACIMA dos slots, encaixadas entre os cristais: a esquerda (verde,
            // ornamento à direita) nasce atrás do cristal de HP; a direita (vinho,
            // ornamento à esquerda) nasce atrás do cristal de MP. O X de início entra
            // alguns px POR BAIXO do cristal — efeito de "sair de trás" dele.
            DrawSideBars(sb, hpRect, mpRectPlate, sy);

            // ── Cristal de HP (à ESQUERDA dos slots, envolto pela garra) ─
            float hpRate = _state.MaximumHealth > 0
                ? MathHelper.Clamp(_state.CurrentHealth / (float)_state.MaximumHealth, 0f, 1f) : 0f;
            DrawCrystal(sb, hpRect, _texHpGrey, _texHpCrystal, hpRate);
            DrawClaw(sb, _texClawLeft, hpRect, +(int)BottomBarLayout.ClawNudge);
            DrawCrystalNum(sb, font, hpRect, _state.CurrentHealth);

            // ── Cristal de MP (à DIREITA dos slots, envolto pela garra) ──
            // MESMO rect do layout (CrystalGap) — o "+6" hardcoded ignorava o gap do
            // editor e o cristal do jogo não batia com o hud-edit-bottombar.
            float mpRate = _state.MaximumMana > 0
                ? MathHelper.Clamp(_state.CurrentMana / (float)_state.MaximumMana, 0f, 1f) : 0f;
            DrawCrystal(sb, mpRectPlate, _texMpGrey, _texMpCrystal, mpRate);
            DrawClaw(sb, _texClawRight, mpRectPlate, -(int)BottomBarLayout.ClawNudge);
            DrawCrystalNum(sb, font, mpRectPlate, _state.CurrentMana);

            base.Draw(gameTime);
        }

        // Barras laterais (arte SD/AG). Tamanho FIXO pelo layout — esticar até o centro
        // distorcia o ornamento. Cada uma nasce por baixo do cristal (SideBarOverlap).
        private void DrawSideBars(SpriteBatch sb, Rectangle hpRect, Rectangle mpRect, int slotsTopY)
        {
            if (!BottomBarLayout.SideBarsEnabled) return;

            int scrH = UiScaler.VirtualSize.Y;
            int h = (int)BottomBarLayout.SideBarH;
            int w = (int)BottomBarLayout.SideBarW;
            int overlap = (int)BottomBarLayout.SideBarOverlap;
            int y = scrH - (int)BottomBarLayout.SideBarBottom - h;
            if (w < 8 || h < 4) return;

            var font = GraphicsManager.Instance.Font;

            int barLeftX = hpRect.Right - overlap + (int)BottomBarLayout.SideBarLeftX;
            int barRightX = mpRect.X + overlap - w + (int)BottomBarLayout.SideBarRightX;

            // ESQUERDA (verde, ornamento à direita): nasce dentro do cristal de HP.
            if (_texBarLeft != null && !_texBarLeft.IsDisposed)
            {
                int x0 = barLeftX;
                var rect = new Rectangle(x0, y, w, h);
                sb.Draw(_texBarLeft, rect, Color.White);   // calha vazia (escura)
                // Preenchimento = RECORTE da arte cheia, crescendo da ESQUERDA.
                DrawSideBarFill(sb, _texBarLeftFull, rect, BottomBarLayout.SideBarLeftFill,
                                useLeftTrough: true,
                                growFromLeft: BottomBarLayout.SideBarFillFromHeadL,
                                offsetX: BottomBarLayout.SideBarFillOffsetL);
                DrawFadesLeftBar(sb, rect);    // degradês A e B
                DrawSideBarText(sb, font, rect, _state.CurrentShield, _state.MaximumShield,
                                BottomBarLayout.SideBarTextLeftX, BottomBarLayout.SideBarTextLeftOverride);
            }

            // DIREITA (vinho, ornamento à esquerda): termina dentro do cristal de MP.
            // SIMETRIA EXATA: desenha a ARTE DA ESQUERDA espelhada (FlipHorizontally) em
            // vez da arte da direita. As artes são espelhos pixel-perfeitos, MAS o
            // downscale PointClamp amostra as duas na MESMA grade esq→dir — arte
            // espelhada amostrada na mesma grade NÃO sai espelhada (±1px por borda).
            // Amostrar a MESMA arte e espelhar o RESULTADO garante o espelho.
            if (_texBarRightA != null && !_texBarRightA.IsDisposed)
            {
                int x1 = barRightX;
                var rect = new Rectangle(x1, y, w, h);
                sb.Draw(_texBarRightA, rect, null, Color.White, 0f, Vector2.Zero,
                        SpriteEffects.FlipHorizontally, 0f);   // calha vazia (escura)
                // Preenchimento = RECORTE da arte cheia DIREITA alinhada (cor própria),
                // com src espelhado + flip => amostragem idêntica à da esquerda.
                DrawSideBarFill(sb, _texBarRightFullA, rect, BottomBarLayout.SideBarRightFill,
                                useLeftTrough: false,
                                // a arte da direita é espelhada: "cabeça" lá é a ponta DIREITA
                                growFromLeft: !BottomBarLayout.SideBarFillFromHeadR,
                                offsetX: BottomBarLayout.SideBarFillOffsetR,
                                mirrorSampling: true);
                DrawFadesRightBar(sb, rect);   // degradês C e D
                DrawSideBarText(sb, font, rect, _state.CurrentAbility, _state.MaximumAbility,
                                BottomBarLayout.SideBarTextRightX, BottomBarLayout.SideBarTextRightOverride);
            }

            // SOBREPOSIÇÃO por último: tapa o que os degradês vazam para fora da calha.
            if (BottomBarLayout.OverlayLEnabled)
                DrawOverlay(sb, _texBarOverlayLeft, barLeftX,
                            BottomBarLayout.OverlayLX, BottomBarLayout.OverlayLBottom,
                            BottomBarLayout.OverlayLW, BottomBarLayout.OverlayLH,
                            BottomBarLayout.OverlayLFlip);
            if (BottomBarLayout.OverlayREnabled)
                // Cópia ALINHADA da arte direita com flip invertido: mesma amostragem
                // da tira esquerda (simetria exata) mantendo a arte própria do lado.
                DrawOverlay(sb, _texBarOverlayRightA ?? _texBarOverlayRight, barRightX,
                            BottomBarLayout.OverlayRX, BottomBarLayout.OverlayRBottom,
                            BottomBarLayout.OverlayRW, BottomBarLayout.OverlayRH,
                            !BottomBarLayout.OverlayRFlip);
        }

        // Tira fina sobre a barra. X é relativo à barra; Bottom é medido da BASE da
        // tela (igual às demais peças do HUD, para bater com o editor).
        private void DrawOverlay(SpriteBatch sb, Texture2D tex, int barX,
                                 float offsetX, float bottom, float wCfg, float hCfg, bool flip)
        {
            if (tex == null || tex.IsDisposed) return;
            float fw = wCfg > 0f ? wCfg : tex.Width + wCfg;
            float fh = hCfg > 0f ? hCfg : tex.Height + hCfg;
            int w = (int)MathF.Round(fw), h = (int)MathF.Round(fh);
            if (w < 1 || h < 1) return;
            int x = (int)MathF.Round(barX + offsetX);
            int y = UiScaler.VirtualSize.Y - (int)MathF.Round(bottom) - h;
            sb.Draw(tex, new Rectangle(x, y, w, h), null, Color.White * Alpha, 0f,
                    Vector2.Zero, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        // Preenchimento: recorta a arte CHEIA (mesma peça, calha clareada) na proporção
        // da taxa. Como é a própria arte, os cantos diagonais, o sombreado interno e os
        // tons claro/escuro são preservados — nada de retângulo de cor por cima.
        // mirrorSampling: barra DIREITA — amostra a arte da ESQUERDA (src espelhado no
        // espaço da textura) e desenha com FlipHorizontally, para a amostragem ser
        // idêntica à da barra esquerda (simetria exata sob PointClamp).
        private void DrawSideBarFill(SpriteBatch sb, Texture2D full, Rectangle bar, float rate,
                                     bool useLeftTrough, bool growFromLeft, float offsetX,
                                     bool mirrorSampling = false)
        {
            if (full == null || full.IsDisposed) return;
            rate = MathHelper.Clamp(rate, 0f, 1f);
            if (rate <= 0f) return;

            int ox = (int)MathF.Round(offsetX);

            // Cheia: desenha inteira (evita erro de arredondamento na borda).
            // Faixa VERTICAL da calha — o editor pinta só ela; a barra inteira
            // estourava o ornamento superior/inferior e não batia com o layout.
            int fillY = bar.Y + (int)MathF.Round(bar.Height * BottomBarLayout.SideBarTroughTop);
            int fillH = (int)MathF.Round(bar.Height * (BottomBarLayout.SideBarTroughBottom
                                                       - BottomBarLayout.SideBarTroughTop));
            if (fillH < 1) return;

            // Recorte vertical correspondente na textura.
            int srcY = (int)MathF.Round(full.Height * BottomBarLayout.SideBarTroughTop);
            int srcH = (int)MathF.Round(full.Height * (BottomBarLayout.SideBarTroughBottom
                                                       - BottomBarLayout.SideBarTroughTop));
            if (srcH < 1) return;

            // (Sem caso especial para rate=1: o caminho geral com a CALHA cobre o cheio.
            // Desenhar a barra INTEIRA no 100% divergia do editor, que pinta só a calha.)

            // O recorte segue a CALHA (área pintada), não a largura total da barra —
            // senão o preenchimento parece começar/terminar no meio da arte.
            // As calhas são MEDIDAS na arte e diferem por lado (a direita é espelhada):
            // esquerda 0.01..0.74, direita 0.26..0.99 — mesma largura, posições distintas.
            float tL = useLeftTrough ? BottomBarLayout.SideBarTroughL : BottomBarLayout.SideBarTroughRL;
            float tR = useLeftTrough ? BottomBarLayout.SideBarTroughR : BottomBarLayout.SideBarTroughRR;

            if (mirrorSampling)
            {
                // SIMETRIA EXATA: calcula TUDO no espaço ALINHADO (orientação da barra
                // esquerda, calha 1-tR..1-tL, offset/crescimento invertidos) com as
                // MESMAS fórmulas e truncamentos da esquerda, e espelha só o RETÂNGULO
                // final. Truncar (int) independente nos dois lados puxava ambos para
                // baixo (mesma direção) e desalinhava o par em 1px.
                tL = 1f - (useLeftTrough ? BottomBarLayout.SideBarTroughR : BottomBarLayout.SideBarTroughRR);
                tR = 1f - (useLeftTrough ? BottomBarLayout.SideBarTroughL : BottomBarLayout.SideBarTroughRL);
                growFromLeft = !growFromLeft;
                ox = -ox;
            }

            // limites da calha em pixels de TEXTURA e de TELA
            float srcL = full.Width * tL, srcR = full.Width * tR;
            float dstL = bar.Width * tL, dstR = bar.Width * tR;
            float srcSpan = srcR - srcL, dstSpan = dstR - dstL;
            if (srcSpan < 1f || dstSpan < 1f) return;

            int sw = (int)(srcSpan * rate);
            int dw = (int)(dstSpan * rate);
            if (sw < 1 || dw < 1) return;

            // Cresce a partir do lado do cristal: esquerda→direita na barra esquerda,
            // direita→esquerda na barra direita.
            var src = growFromLeft
                ? new Rectangle((int)srcL, srcY, sw, srcH)
                : new Rectangle((int)(srcR - sw), srcY, sw, srcH);
            int dstLocalX = growFromLeft ? (int)dstL + ox : (int)(dstR - dw) + ox;

            if (mirrorSampling)
            {
                // espelha o retângulo de destino dentro da barra; o src fica no espaço
                // alinhado (a textura *A já está espelhada) e o Flip devolve a orientação.
                var dstM = new Rectangle(bar.X + (bar.Width - dstLocalX - dw), fillY, dw, fillH);
                sb.Draw(full, dstM, src, Color.White, 0f, Vector2.Zero,
                        SpriteEffects.FlipHorizontally, 0f);
                return;
            }

            sb.Draw(full, new Rectangle(bar.X + dstLocalX, fillY, dw, fillH), src, Color.White);
        }

        // Degradês das extremidades: DOIS por barra (ponta esquerda e ponta direita),
        // desenhados POR CIMA. Cada um com posição, tamanho e flip próprios.
        // Degradês da barra ESQUERDA (A = ponta esq, B = ponta dir).
        private void DrawFadesLeftBar(SpriteBatch sb, Rectangle bar)
        {
            if (!BottomBarLayout.SideBarFadeEnabled) return;
            if (BottomBarLayout.FadeAOn)
                DrawOneFade(sb, _texBarFadeLeft, bar, true,
                            BottomBarLayout.FadeAW, BottomBarLayout.FadeAH,
                            BottomBarLayout.FadeAX, BottomBarLayout.FadeAY, BottomBarLayout.FadeAFlip);
            if (BottomBarLayout.FadeBOn)
                DrawOneFade(sb, _texBarFadeRight, bar, false,
                            BottomBarLayout.FadeBW, BottomBarLayout.FadeBH,
                            BottomBarLayout.FadeBX, BottomBarLayout.FadeBY, BottomBarLayout.FadeBFlip);
        }

        // Degradês da barra DIREITA (C = ponta esq, D = ponta dir).
        private void DrawFadesRightBar(SpriteBatch sb, Rectangle bar)
        {
            if (!BottomBarLayout.SideBarFadeEnabled) return;
            if (BottomBarLayout.FadeCOn)
                DrawOneFade(sb, _texBarFadeLeft, bar, true,
                            BottomBarLayout.FadeCW, BottomBarLayout.FadeCH,
                            BottomBarLayout.FadeCX, BottomBarLayout.FadeCY, BottomBarLayout.FadeCFlip);
            if (BottomBarLayout.FadeDOn)
                DrawOneFade(sb, _texBarFadeRight, bar, false,
                            BottomBarLayout.FadeDW, BottomBarLayout.FadeDH,
                            BottomBarLayout.FadeDX, BottomBarLayout.FadeDY, BottomBarLayout.FadeDFlip);
        }

        private void DrawOneFade(SpriteBatch sb, Texture2D fade, Rectangle bar, bool atLeftEdge,
                                 float wCfg, float hCfg, float offsetX, float offsetY, bool flip)
        {
            if (fade == null || fade.IsDisposed) return;

            // Largura/altura INDEPENDENTES por degradê:
            //   0        = automático (altura da barra / proporção nativa da arte)
            //   positivo = tamanho absoluto em px
            //   NEGATIVO = reduz N px do tamanho automático (ex.: -12 encolhe 12px)
            // Contas em FLOAT (o editor aceita decimais, ex.: -11,9) e só arredonda
            // no fim — truncar antes perdia a fração e o ajuste não refletia.
            float fh, fyTop;

            if (BottomBarLayout.FadeSnapToTrough)
            {
                // Encaixa EXATAMENTE na calha (área interna, sem a borda dourada).
                // A calha não cai em pixel inteiro; por isso a conta é feita em float
                // e só o retângulo final é arredondado.
                fyTop = bar.Y + bar.Height * BottomBarLayout.FadeTroughTop + offsetY;
                float bottom = bar.Y + bar.Height * BottomBarLayout.FadeTroughBottom;
                fh = bottom - fyTop + offsetY;
                if (hCfg != 0f) fh += hCfg;      // ajuste fino opcional
            }
            else
            {
                float autoH = bar.Height;
                fh = hCfg > 0f ? hCfg : autoH + hCfg;
                fyTop = bar.Y + offsetY;
            }

            float autoW = fh * (fade.Width / (float)fade.Height);
            float fw = wCfg > 0f ? wCfg : autoW + wCfg;          // wCfg<0 subtrai

            int w = (int)MathF.Round(fw);
            int h = (int)MathF.Round(fh);
            if (w < 1 || h < 1) return;

            int x = (int)MathF.Round(atLeftEdge ? bar.X + offsetX : bar.Right - fw - offsetX);
            int y = (int)MathF.Round(fyTop);

            sb.Draw(fade, new Rectangle(x, y, w, h), null, Color.White * Alpha, 0f,
                    Vector2.Zero, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        // Valor "atual / máximo" centralizado sobre a barra.
        // Sem lógica ligada ainda: se o servidor não manda o recurso (max = 0), mostra
        // o mesmo valor dos dois lados (estado CHEIO), como pedido.
        private void DrawSideBarText(SpriteBatch sb, SpriteFont font, Rectangle bar, uint cur, uint max,
                                     float offsetX = 0f, string overrideText = null)
        {
            if (!BottomBarLayout.SideBarTextEnabled || font == null) return;
            // Texto fixo do layout (para bater com o editor); senão, valor real.
            string s = string.IsNullOrEmpty(overrideText) ? $"{cur} / {max}" : overrideText;
            float sc = BottomBarLayout.SideBarTextFont / 25f;

            // Centraliza pela CAIXA DE TINTA dos dígitos. ATENÇÃO: Cropping/BoundsInTexture
            // já estão em pixels DA TEXTURA da fonte; ao desenhar com escala `sc` a tinta
            // também escala — por isso inkTop e inkH entram AMBOS multiplicados por sc
            // (o erro anterior era escalar só um lado, jogando o texto fora do centro).
            var size = font.MeasureString(s) * sc;
            // Métrica MEDIDA da fonte do jogo (Arial 25): a tinta dos dígitos ocupa
            // y 4.62..22.88 dentro da linha. Usamos valores fixos porque GetGlyphs
            // devolve coordenadas da textura da fonte, não da linha de texto.
            const float INK_TOP = 4.62f, INK_H = 18.25f;
            float y = bar.Y + (bar.Height - INK_H * sc) / 2f - INK_TOP * sc;
            // offsetX = ajuste POR LADO (permite empurrar cada texto pra fora do centro);
            // SideBarTextOffsetX continua valendo como ajuste global dos dois.
            var pos = new Vector2(bar.X + (bar.Width - size.X) / 2f
                                        + BottomBarLayout.SideBarTextOffsetX + offsetX,
                                  y + BottomBarLayout.SideBarTextOffsetY);

            // Destaque igual à referência: sombra escura curta (sem contorno grosso).
            float shadow = MathHelper.Clamp(BottomBarLayout.SideBarTextShadow, 0f, 1f);
            sb.DrawString(font, s, pos + new Vector2(1, 1), Color.Black * shadow,
                          0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            sb.DrawString(font, s, pos + new Vector2(0, 1), Color.Black * (shadow * 0.7f),
                          0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            sb.DrawString(font, s, pos, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
        }

        private void DrawCrystal(SpriteBatch sb, Rectangle rect, Texture2D grey, Texture2D bright, float rate)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            // Base cinza (cristal vazio).
            if (grey != null && !grey.IsDisposed)
                sb.Draw(grey, rect, Color.White);

            // Preenchimento brilhante recortado de baixo pra cima pela taxa.
            if (bright != null && !bright.IsDisposed && rate > 0f)
            {
                int fillH = (int)(rect.Height * rate);
                int srcH = (int)(bright.Height * rate);
                var src = new Rectangle(0, bright.Height - srcH, bright.Width, srcH);
                var dst = new Rectangle(rect.X, rect.Bottom - fillH, rect.Width, fillH);
                sb.Draw(bright, dst, src, Color.White);
            }
            else if (bright == null && rate > 0f)
            {
                int fillH = (int)(rect.Height * rate);
                sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - fillH, rect.Width, fillH), new Color(200, 40, 40));
            }
        }

        private void DrawClaw(SpriteBatch sb, Texture2D claw, Rectangle crystalRect, int nudgeX)
        {
            if (claw == null || claw.IsDisposed) return;
            // Tamanho ABSOLUTO do layout (peça editável no editor). nudgeX puxa a garra
            // pra DENTRO (esq do HP → direita; dir do MP → esquerda).
            int w = (int)BottomBarLayout.ClawW;
            int h = (int)BottomBarLayout.ClawH;
            int cx = crystalRect.X + (crystalRect.Width - w) / 2 + nudgeX;
            // colada embaixo (profundidade correta) + ajuste Y do editor
            int cy = crystalRect.Bottom - h + (int)BottomBarLayout.ClawOffsetY;
            var r = new Rectangle(cx, cy, w, h);
            sb.Draw(claw, r, Color.White);
        }

        private void DrawCrystalNum(SpriteBatch sb, SpriteFont font, Rectangle rect, uint value)
        {
            if (font == null) return;
            string s = value.ToString();
            float sc = BottomBarLayout.CrystalNumFont / 25f;
            var size = font.MeasureString(s) * sc;
            var pos = new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Bottom - size.Y);
            DrawOutlined(sb, font, s, pos, sc, Color.White, 2);
        }

        // Desenha texto com contorno preto em 8 direções (bem legível sobre qualquer fundo).
        private static void DrawOutlined(SpriteBatch sb, SpriteFont font, string text,
            Vector2 pos, float scale, Color color, int thickness)
        {
            var outline = Color.Black;
            for (int dx = -thickness; dx <= thickness; dx++)
                for (int dy = -thickness; dy <= thickness; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    sb.DrawString(font, text, pos + new Vector2(dx, dy), outline,
                        0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
