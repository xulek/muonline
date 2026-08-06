using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Potion Imprint configures the hotbar potion and consumable slots. It reuses the
    /// modern visual shell through <see cref="ImprintLayout"/>. The upper grid shows
    /// available inventory consumables; the lower row contains five potion slots.
    /// Dragging a consumable to a slot equips it and persists it through ModernBottomHud.
    /// </summary>
    public sealed class PotionImprintControl : UIControl
    {
        private const string Dir = "Interface/Imprint/";
        private const string OkCancelTex = "Interface/CharCreate/ok_cancel.OZT"; // Standard OK/Cancel button.

        private Texture2D _texPanel, _texSlot, _texTab, _texOkCancel;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);
        private Texture2D _texPotionSlot;   // Artwork for the five potion slots (mi_slot.OZP).
        private Texture2D _texSlotInv;      // quadradinho do inventário (grade de disponíveis)
        // Grade da Potion Imprint: fileiras PRÓPRIAS (independentes da Skill Imprint, que
        // compartilha o ImprintLayout). Ajuste pelo editor hud-edit-potion (você escolhe 3/4/5).
        
        

        private Texture2D _texReset, _texResetText;   // botão Reset (ícone + texto)
        private Rectangle _resetRect, _resetTextRect, _infoTextRect;
        private bool _resetHover;
        private Texture2D _texSideArrow, _texExpandPanel, _texSkillBg;
        private Texture2D _texRect, _texDarkCard, _texExpBorder;
        private Texture2D _texClose, _texCloseHover, _texAro;
        private Texture2D _texHoleGlow;   // brilho por cima do ícone (mesmo efeito da hotbar)

        // Retângulos calculados (em coords de tela virtual).
        private Rectangle _panelRect, _titleRect, _closeRect, _tabBasicRect;
        private Rectangle _skillBgRect, _rect1Rect, _rect2Rect, _rect3Rect, _saveRect, _cancelRect;
        private readonly System.Collections.Generic.List<Rectangle> _gridRects = new();
        private readonly System.Collections.Generic.List<Rectangle> _bottomRects = new();
        private Rectangle _darkCard2Rect, _darkCard3Rect;
        private Rectangle _titleBarMainRect;   // barra de título (zona de arraste)

        // ── Dados de consumível ─────────────────────────────────────────────────────
        // Fonte: ModernBottomHud (candidatos do inventário + slots de poção do hotbar).
        private readonly CharacterState _state;
        private readonly ModernBottomHud _hud;   // dono dos slots de poção (hotbar real)
        private (byte Group, int Id)? _selected;  // consumível selecionado (painel Info mostra ele)
        // Hit-test dos slots da grade: (rect, group, id) do que está desenhado.
        private readonly List<(Rectangle Rect, byte Group, int Id)> _gridHits = new();

        // ── Drag de consumível (grade → slot de poção) ──────────────────────────────
        private bool _dragItem;                  // arrastando um consumível da grade?
        private (byte Group, int Id) _dragKey;   // consumível sendo arrastado
        private bool _dragActive;                // há um candidato de drag válido?
        private Point _dragPos;                  // posição atual do cursor (pra desenhar o ícone)
        private Point _dragStart;                // onde começou (pra distinguir clique de arraste)

        // ── Lógica das 2 janelas ────────────────────────────────────────────────────
        // Nascem AMBAS abertas e coladas. Enquanto a 2ª (Potion Info) está aberta:
        //   • a borda/seta da 1ª janela (ExpBorder1/SideBtn1) fica OCULTA;
        //   • só a borda/seta da 2ª (ExpBorder2/SideBtn2) aparece — é por onde se fecha.
        private bool _closeHover;

        // ── Drag & drop das 2 janelas ──────────────────────────────────────────────
        //   _mainDX/DY = janela 1 (Potion Imprint) | _infoDX/DY = janela 2 (Potion Info)
        private int _mainDX, _mainDY;
        private enum DragTarget { None, Main }
        private DragTarget _dragging = DragTarget.None;
        private Point _dragGrab;   // posição do mouse no momento do clique (pra delta)
        private int _dragBaseX, _dragBaseY;   // offset da janela no início do arraste

        public PotionImprintControl(CharacterState state = null, ModernBottomHud hud = null)
        {
            _state = state;
            _hud = hud;
            Interactive = true;
            AutoViewSize = false;
            ControlSize = new Point(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
            ViewSize = ControlSize;
            X = 0; Y = 0;
            Visible = false;
        }

        public override async Task Load()
        {
            await base.Load();
            if (_loadedTheme == UiThemeManager.CurrentId)
                return;
            async Task<Texture2D> L(string p) { try { return await UiThemeManager.LoadThemeTextureAsync(p); } catch { return null; } }
            _texPanel = await L(Dir + "imprint_panel.OZP");
            _texSlot = await L(Dir + "imprint_slot.OZP");
            _texPotionSlot = await L("Interface/DH/mi_slot.OZP");   // arte de slot de poção
            _texReset = await L(Dir + "imprint_reset.OZP");
            _texResetText = await L(Dir + "imprint_reset_text.OZP");
            _texSlotInv = await L(Dir + "imprint_grid_row.OZP");   // fileira de 5 quadradinhos (grade)
            _texTab = await L(Dir + "imprint_tab.OZP");
            _texSideArrow = await L(Dir + "imprint_side_arrow.OZP");
            _texExpandPanel = await L(Dir + "imprint_expand_panel.OZP");
            _texSkillBg = await L(Dir + "imprint_skill_bg.OZP");
            _texRect = await L(Dir + "imprint_rect.OZP");
            _texDarkCard = await L(Dir + "imprint_dark_card.OZP");
            _texExpBorder = await L(Dir + "imprint_exp_border.OZP");
            _texAro = await L(Dir + "imprint_aro.OZP");
            _texClose = await L(Dir + "imprint_close.OZP");
            _texCloseHover = await L(Dir + "imprint_close_hover.OZP");
            _texHoleGlow = await L("Interface/SkillWin/hole_glow.OZP");   // brilho por cima do ícone (igual hotbar)
            _texOkCancel = await L(OkCancelTex);
            _loadedTheme = UiThemeManager.CurrentId;
        }

        public void Toggle()
        {
            Visible = !Visible;
            // Ao ABRIR a tela, as 2 janelas nascem sempre abertas (coladas); recarrega dados.
            if (Visible)
            {
                _selected = null;
            }
        }

        // Escala responsiva: a altura EFETIVA do painel = 100% da altura da tela virtual.
        private float _scale = 1f;
        private int _panelOX, _panelOY, _panelW, _panelH;

        private void ComputeScale()
        {
            int targetH = UiScaler.VirtualSize.Y;                   // 100% of the virtual UI height
            _scale = targetH / (float)PotionLayout.PanelH;
            _panelH = targetH;
            _panelW = (int)(PotionLayout.PanelW * _scale);
            _panelOX = (UiScaler.VirtualSize.X - _panelW) / 2 + (int)(PotionLayout.PanelX * _scale);
            _panelOY = (UiScaler.VirtualSize.Y - _panelH) / 2 + (int)(PotionLayout.PanelY * _scale);
        }

        // Rect de um elemento da JANELA 1 (Potion Imprint).
        private Rectangle R(float x, float y, float w, float h)
            => new(_panelOX + _mainDX + (int)(x * _scale), _panelOY + _mainDY + (int)(y * _scale),
                   (int)(w * _scale), (int)(h * _scale));

        private void Layout()
        {
            ComputeScale();
            // Painel principal já com o deslocamento de arraste da janela 1.
            _panelRect = new Rectangle(_panelOX + _mainDX, _panelOY + _mainDY, _panelW, _panelH);
            _titleRect = R(PotionLayout.TitleX, PotionLayout.TitleY, PotionLayout.TitleW, PotionLayout.TitleH);
            _closeRect = R(PotionLayout.CloseX, PotionLayout.CloseY, PotionLayout.CloseW, PotionLayout.CloseH);
            _tabBasicRect = R(PotionLayout.TabBasicX, PotionLayout.TabBasicY, PotionLayout.TabBasicW, PotionLayout.TabBasicH);
            // Molduras da janela principal (as únicas usadas pela Potion Imprint).
            _rect2Rect = R(PotionLayout.Rect2X, PotionLayout.Rect2Y, PotionLayout.Rect2W, PotionLayout.Rect2H);
            _rect3Rect = R(PotionLayout.Rect3X, PotionLayout.Rect3Y, PotionLayout.Rect3W, PotionLayout.Rect3H);
            _saveRect = R(PotionLayout.SaveX, PotionLayout.SaveY, PotionLayout.SaveW, PotionLayout.SaveH);
            _cancelRect = R(PotionLayout.CancelX, PotionLayout.CancelY, PotionLayout.CancelW, PotionLayout.CancelH);
            _resetRect = R(PotionLayout.ResetX, PotionLayout.ResetY, PotionLayout.ResetW, PotionLayout.ResetH);
            _resetTextRect = R(PotionLayout.ResetTextX, PotionLayout.ResetTextY, PotionLayout.ResetTextW, PotionLayout.ResetTextH);
            _infoTextRect = R(PotionLayout.InfoTextX, PotionLayout.InfoTextY, PotionLayout.InfoTextW, PotionLayout.InfoTextH);
            _skillBgRect = _panelRect;
            _rect1Rect = _panelRect;

            // Grade de slots (guia = GridX/Y, passo = W/H + Gap).
            _gridRects.Clear();
            int stepX = (int)PotionLayout.GridW + PotionLayout.GridGap;
            int stepY = (int)PotionLayout.GridH + PotionLayout.GridGap;
            for (int r = 0; r < PotionLayout.GridRows; r++)
                for (int c = 0; c < PotionLayout.GridCols; c++)
                    _gridRects.Add(R(PotionLayout.GridX + c * stepX, PotionLayout.GridY + r * stepY, PotionLayout.GridW, PotionLayout.GridH));

            // 7 slots SOLTOS da linha inferior — usaremos só os 5 primeiros como slots de poção.
            _bottomRects.Clear();
            AddSlot(PotionLayout.Slot1Enabled, PotionLayout.Slot1X, PotionLayout.Slot1Y, PotionLayout.Slot1W, PotionLayout.Slot1H);
            AddSlot(PotionLayout.Slot2Enabled, PotionLayout.Slot2X, PotionLayout.Slot2Y, PotionLayout.Slot2W, PotionLayout.Slot2H);
            AddSlot(PotionLayout.Slot3Enabled, PotionLayout.Slot3X, PotionLayout.Slot3Y, PotionLayout.Slot3W, PotionLayout.Slot3H);
            AddSlot(PotionLayout.Slot4Enabled, PotionLayout.Slot4X, PotionLayout.Slot4Y, PotionLayout.Slot4W, PotionLayout.Slot4H);
            AddSlot(PotionLayout.Slot5Enabled, PotionLayout.Slot5X, PotionLayout.Slot5Y, PotionLayout.Slot5W, PotionLayout.Slot5H);

            void AddSlot(bool on, float x, float y, float w, float h)
            {
                if (!on) return;
                _bottomRects.Add(R(x, y, w, h));
            }

            // Ordem VISUAL: o índice do slot (= índice na hotbar) segue a posição na tela,
            // esquerda→direita — mesmo que os SlotN sejam arrastados fora de ordem no editor.
            _bottomRects.Sort((a, b) => a.X.CompareTo(b.X));

            _darkCard2Rect = R(PotionLayout.DarkCard2X, PotionLayout.DarkCard2Y, PotionLayout.DarkCard2W, PotionLayout.DarkCard2H);
            _darkCard3Rect = R(PotionLayout.DarkCard3X, PotionLayout.DarkCard3Y, PotionLayout.DarkCard3W, PotionLayout.DarkCard3H);

            // Barras de título (faixa vermelha no topo de cada janela) = zonas de arraste.
            const float BarTop = 30f, BarBot = 74f;
            _titleBarMainRect = new Rectangle(
                _panelRect.X, _panelRect.Y + (int)(BarTop * _scale),
                _panelRect.Width, (int)((BarBot - BarTop) * _scale));
        }

        // Número de slots de poção = os 5 primeiros da linha inferior (limitado ao hotbar).
        private int PotionSlotCount => Math.Min(ModernBottomHud.ItemHotbarSlotCount, _bottomRects.Count);

        // Candidatos de consumível do inventário (o que a grade mostra).
        private IReadOnlyList<(byte Group, int Id, string Name, string TexturePath, int Count)> Candidates()
            => _hud?.GetConsumableCandidates()
               ?? (IReadOnlyList<(byte, int, string, string, int)>)Array.Empty<(byte, int, string, string, int)>();

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible) return;
            Layout();

            // Ícones BMD dos candidatos: pré-gera os previews AQUI (fora do SpriteBatch).
            // GetItemIconByKey só devolve preview já cacheado — sem isso a grade fica sem ícone.
            _hud?.EnsureConsumableIconsCached();

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            var p = new Point(mouse.Position.X, mouse.Position.Y);

            bool down = mouse.LeftButton == ButtonState.Pressed;
            bool wasDown = prev.LeftButton == ButtonState.Pressed;

            // ── Drag & drop das janelas pela barra de título ──────────────────────────
            if (_dragging != DragTarget.None)
            {
                if (down)
                {
                    int dx = p.X - _dragGrab.X;
                    int dy = p.Y - _dragGrab.Y;
                    if (_dragging == DragTarget.Main) { _mainDX = _dragBaseX + dx; _mainDY = _dragBaseY + dy; }
                    _mainDX = _dragBaseX + dx; _mainDY = _dragBaseY + dy;
                    Layout();   // recomputa rects com o novo offset já neste frame
                    Scene?.SetMouseInputConsumed();
                    return;
                }
                _dragging = DragTarget.None;   // soltou
            }

            // Início do arraste: clicou numa barra de título, fora do close.
            if (down && !wasDown && !(_closeRect.Contains(p) && PotionLayout.CloseEnabled))
            {
                if (_titleBarMainRect.Contains(p))
                {
                    _dragging = DragTarget.Main; _dragGrab = p; _dragBaseX = _mainDX; _dragBaseY = _mainDY;
                    Scene?.SetMouseInputConsumed();
                    return;
                }
            }

            // ── Drag de consumível (grade → slot de poção) ────────────────────────────
            if (_dragItem)
            {
                _dragPos = p;
                if (down) { Scene?.SetMouseInputConsumed(); return; }

                // Soltou: se caiu num slot de poção, equipa via o hud (persiste automaticamente).
                if (_dragActive && _hud != null)
                {
                    int n = PotionSlotCount;
                    for (int i = 0; i < n; i++)
                        if (_bottomRects[i].Contains(p)) { _hud.SetItemAssignmentAt(i, (_dragKey.Group, _dragKey.Id)); break; }
                }
                _dragItem = false;
                _dragActive = false;
                Scene?.SetMouseInputConsumed();
                return;
            }

            // Início do drag: pressionou sobre um slot da grade com consumível e moveu.
            if (down && !wasDown)
            {
                foreach (var hit in _gridHits)
                {
                    if (!hit.Rect.Contains(p)) continue;
                    _dragKey = (hit.Group, hit.Id);
                    _dragActive = true;
                    _dragStart = p;
                    _dragPos = p;
                    break;
                }
            }
            // Promove pra drag quando o cursor se afasta o suficiente do ponto inicial.
            if (down && _dragActive && !_dragItem)
            {
                int dxs = p.X - _dragStart.X, dys = p.Y - _dragStart.Y;
                if (dxs * dxs + dys * dys > 64) _dragItem = true;   // >8px = arraste
            }
            if (!down) _dragActive = false;   // soltou sem virar drag → cancela candidato

            // Hover do botão de fechar e do Reset (troca/clareia a arte).
            _closeHover = PotionLayout.CloseEnabled && _closeRect.Contains(p);
            _resetHover = PotionLayout.ResetEnabled && _resetRect.Contains(p);

            bool click = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
            if (!click) return;

            // SAVE / Close / Cancel: nada de rascunho — as poções já persistem. Só fecha.
            if (PotionLayout.SaveEnabled && _saveRect.Contains(p)) { Scene?.SetMouseInputConsumed(); Visible = false; return; }
            if (PotionLayout.CloseEnabled && _closeRect.Contains(p)) { Scene?.SetMouseInputConsumed(); Visible = false; return; }
            if (PotionLayout.CancelEnabled && _cancelRect.Contains(p)) { Scene?.SetMouseInputConsumed(); Visible = false; return; }

            // RESET: limpa os 5 slots de poção (aplica direto no hud).
            if (PotionLayout.ResetEnabled && _resetRect.Contains(p) && _hud != null)
            {
                for (int i = 0; i < PotionSlotCount; i++) _hud.SetItemAssignmentAt(i, null);
                Scene?.SetMouseInputConsumed();
                return;
            }

            // Clique num slot de poção com atribuição: remove (aplica direto no hud).
            if (_hud != null)
            {
                int n = PotionSlotCount;
                for (int i = 0; i < n; i++)
                    if (_bottomRects[i].Contains(p) && _hud.GetItemAssignmentAt(i).HasValue)
                    { _hud.SetItemAssignmentAt(i, null); Scene?.SetMouseInputConsumed(); return; }
            }

            // Clique num slot da grade: seleciona o consumível (Potion Info mostra ele).
            foreach (var hit in _gridHits)
                if (hit.Rect.Contains(p)) { _selected = (hit.Group, hit.Id); Scene?.SetMouseInputConsumed(); return; }

            // (Sem janela 2 / setas laterais na Potion Imprint.)
            if (_panelRect.Contains(p))
                Scene?.SetMouseInputConsumed(); // não vaza clique pro mundo
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;
            var sb = GraphicsManager.Instance?.Sprite;
            var pixel = GraphicsManager.Instance?.Pixel;
            var font = GraphicsManager.Instance?.Font;
            if (sb == null || pixel == null) return;

            Layout();

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);

            // Painel de fundo (principal).
            if (PotionLayout.PanelEnabled)
            {
                if (Tex(_texPanel)) sb.Draw(_texPanel, _panelRect, Color.White);
                else sb.Draw(pixel, _panelRect, new Color(24, 20, 18) * 0.97f);
            }

            // Molduras (Rect2/Rect3) POR BAIXO de tudo — mesma ordem do editor. Antes eram
            // desenhadas por último e a borda cobria a contagem da última coluna da grade.
            if (PotionLayout.Rect2Enabled && Tex(_texRect)) Draw9Slice(sb, _texRect, _rect2Rect);
            if (PotionLayout.Rect3Enabled && Tex(_texRect)) Draw9Slice(sb, _texRect, _rect3Rect);

            // Dark cards extras (opcionais).
            if (PotionLayout.DarkCard2Enabled && Tex(_texDarkCard)) sb.Draw(_texDarkCard, _darkCard2Rect, Color.White);
            if (PotionLayout.DarkCard3Enabled && Tex(_texDarkCard)) sb.Draw(_texDarkCard, _darkCard3Rect, Color.White);

            // Grade de cima: casca (soquete) + ícone do consumível disponível no inventário.
            DrawItemGrid(sb, pixel, font);

            // (Aro ornamentado OCULTADO no editor — Potion Imprint não usa.)

            // Linha inferior: 5 slots de poção. Desenham a atribuição atual lida do hud.
            int slots = PotionSlotCount;
            for (int i = 0; i < slots; i++)
            {
                var r = _bottomRects[i];
                // Slot de poção usa a arte própria (mi_slot.OZP), não o soquete genérico.
                if (Tex(_texPotionSlot)) sb.Draw(_texPotionSlot, r, Color.White);
                else DrawSlot(sb, pixel, r);
                var assign = _hud?.GetItemAssignmentAt(i);
                if (assign.HasValue)
                {
                    var ic = ShrinkToSquare(r, 0.98f);
                    var icon = _hud?.GetItemIconByKey(assign.Value.Group, assign.Value.Id);
                    if (Tex(icon)) sb.Draw(icon, ic, Color.White);
                    if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(r, 0.90f), Color.White);
                }
            }

            // A janela 2 (Potion Info / painel expandido) foi OCULTADA no editor: a Potion
            // Imprint usa só o painel principal (grade + 5 slots + Save/Cancel). Como o
            // ImprintLayout é compartilhado com a Skill Imprint, não desligamos os flags
            // globais — a Potion simplesmente NÃO desenha esses elementos.

            // (Sem setas laterais: a janela 2 foi removida da Potion Imprint.)

            // Aba única (esquerda), renomeada.
            if (PotionLayout.TabBasicEnabled) DrawTab(sb, pixel, font, _tabBasicRect, "Potion Available", true, PotionLayout.TabBasicFont);

            // Título: só o TEXTO (a barra vermelha já vem no painel-template).
            if (PotionLayout.TitleEnabled)
                DrawCenteredText(sb, font, "Potion Imprint", _titleRect, new Color(230, 200, 120), PotionLayout.TitleFont / 25f * _scale);

            // Botão de fechar (arte). Troca pra variação hover quando o mouse está em cima.
            if (PotionLayout.CloseEnabled)
            {
                var closeTex = _closeHover ? _texCloseHover : _texClose;
                if (Tex(closeTex)) sb.Draw(closeTex, _closeRect, Color.White);
                else DrawCenteredText(sb, font, "X", _closeRect, new Color(220, 90, 70), PotionLayout.CloseFont / 25f * _scale);
            }

            // Botão RESET (ícone + texto). Hover = clareamento leve.
            if (PotionLayout.ResetEnabled && Tex(_texReset))
                sb.Draw(_texReset, _resetRect, _resetHover ? new Color(255, 245, 220) : Color.White);
            if (PotionLayout.ResetTextEnabled && Tex(_texResetText))
                sb.Draw(_texResetText, _resetTextRect, Color.White);

            // Mensagem informativa (multilinha, esquerda/topo, quebra pela largura do rect —
            // mesmo comportamento do editor). Não clipa: a altura do rect é só guia de layout.
            if (PotionLayout.InfoTextEnabled && font != null && !string.IsNullOrEmpty(PotionLayout.InfoTextMessage))
            {
                float isc = PotionLayout.InfoTextFont / 25f * _scale;
                float ilineH = font.MeasureString("A").Y * isc * 1.2f;
                float iy = _infoTextRect.Y;
                foreach (var line in WrapText(font, PotionLayout.InfoTextMessage, isc, _infoTextRect.Width))
                {
                    sb.DrawString(font, line, new Vector2(_infoTextRect.X + 1, iy + 1), Color.Black * 0.6f, 0f, Vector2.Zero, isc, SpriteEffects.None, 0f);
                    sb.DrawString(font, line, new Vector2(_infoTextRect.X, iy), new Color(225, 220, 210), 0f, Vector2.Zero, isc, SpriteEffects.None, 0f);
                    iy += ilineH;
                }
            }

            // Botões Save/Cancel (arte padrão ok_cancel). Ambos apenas fecham a tela.
            if (PotionLayout.SaveEnabled) DrawOkButton(sb, font, _saveRect, "Save", PotionLayout.SaveFont, false);
            if (PotionLayout.CancelEnabled) DrawOkButton(sb, font, _cancelRect, "Cancel", PotionLayout.CancelFont, true);

            // Ícone "fantasma" que segue o cursor enquanto arrasta um consumível pro slot.
            if (_dragItem && _dragActive)
            {
                int side = (int)(48 * _scale);
                var gr = new Rectangle(_dragPos.X - side / 2, _dragPos.Y - side / 2, side, side);
                var icon = _hud?.GetItemIconByKey(_dragKey.Group, _dragKey.Id);
                if (Tex(icon)) sb.Draw(icon, gr, Color.White * 0.9f);
                if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(gr, 0.90f), Color.White * 0.9f);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, UiScaler.SpriteTransform);

            base.Draw(gameTime);
        }

        private static bool Tex(Texture2D t) => t != null && !t.IsDisposed;

        // Desenha uma moldura em 9-slice: os 4 CANTOS ficam em tamanho FIXO e só as bordas/
        // centro são esticados.
        private void Draw9Slice(SpriteBatch sb, Texture2D tex, Rectangle dst)
        {
            int tw = tex.Width, th = tex.Height;
            int m = System.Math.Min(5, System.Math.Min(tw, th) / 2 - 1);   // canto fonte (= borda da tile)
            if (m < 1) { sb.Draw(tex, dst, Color.White); return; }
            int md = System.Math.Max(2, (int)System.Math.Round(m * _scale));
            md = System.Math.Min(md, System.Math.Min(dst.Width, dst.Height) / 2 - 1);
            if (md < 1 || dst.Width < 4 || dst.Height < 4) { sb.Draw(tex, dst, Color.White); return; }

            int cx = tw - 2 * m, cy = th - 2 * m;               // centro fonte
            int dcx = dst.Width - 2 * md, dcy = dst.Height - 2 * md;

            // Cantos (tamanho FIXO md × md).
            sb.Draw(tex, new Rectangle(dst.X, dst.Y, md, md), new Rectangle(0, 0, m, m), Color.White);
            sb.Draw(tex, new Rectangle(dst.Right - md, dst.Y, md, md), new Rectangle(tw - m, 0, m, m), Color.White);
            sb.Draw(tex, new Rectangle(dst.X, dst.Bottom - md, md, md), new Rectangle(0, th - m, m, m), Color.White);
            sb.Draw(tex, new Rectangle(dst.Right - md, dst.Bottom - md, md, md), new Rectangle(tw - m, th - m, m, m), Color.White);
            // Bordas (só o eixo longo estica; a espessura md é fixa).
            sb.Draw(tex, new Rectangle(dst.X + md, dst.Y, dcx, md), new Rectangle(m, 0, cx, m), Color.White);
            sb.Draw(tex, new Rectangle(dst.X + md, dst.Bottom - md, dcx, md), new Rectangle(m, th - m, cx, m), Color.White);
            sb.Draw(tex, new Rectangle(dst.X, dst.Y + md, md, dcy), new Rectangle(0, m, m, cy), Color.White);
            sb.Draw(tex, new Rectangle(dst.Right - md, dst.Y + md, md, dcy), new Rectangle(tw - m, m, m, cy), Color.White);
            // Centro (interior transparente/textura).
            sb.Draw(tex, new Rectangle(dst.X + md, dst.Y + md, dcx, dcy), new Rectangle(m, m, cx, cy), Color.White);
        }

        private void DrawSlot(SpriteBatch sb, Texture2D pixel, Rectangle r)
        {
            if (Tex(_texSlot)) sb.Draw(_texSlot, r, Color.White);
            else { sb.Draw(pixel, r, new Color(40, 36, 32)); DrawBorder(sb, pixel, r, new Color(80, 72, 60)); }
        }

        // Grade de consumíveis disponíveis: soquete + ícone + contagem. Preenche _gridHits.
        private void DrawItemGrid(SpriteBatch sb, Texture2D pixel, SpriteFont font)
        {
            _gridHits.Clear();

            var candidates = Candidates();
            int count = Math.Min(candidates.Count, _gridRects.Count);

            // Fundo da grade: a arte slot-potion-inv (fileira de 5 quadradinhos) desenhada
            // UMA vez por LINHA, cobrindo as 5 células daquela linha (não célula a célula).
            if (Tex(_texSlotInv))
            {
                int cols = PotionLayout.GridCols;
                for (int row = 0; row < PotionLayout.GridRows; row++)
                {
                    int first = row * cols;
                    int lastInRow = Math.Min(first + cols - 1, _gridRects.Count - 1);
                    if (first >= _gridRects.Count) break;
                    var a = _gridRects[first];
                    var b = _gridRects[lastInRow];
                    var rowRect = new Rectangle(a.X, a.Y, b.Right - a.X, a.Height);
                    sb.Draw(_texSlotInv, rowRect, Color.White);
                }
            }

            for (int cell = 0; cell < _gridRects.Count; cell++)
            {
                var r = _gridRects[cell];

                if (cell >= count) continue;

                var cand = candidates[cell];
                var iconRect = ShrinkToSquare(r, 0.98f);
                var icon = _hud?.GetItemIconByKey(cand.Group, cand.Id);
                if (Tex(icon)) sb.Draw(icon, iconRect, Color.White);
                // (Sem hole_glow na grade de poções: o glow é redondo e não combina com os
                //  quadradinhos — era o "círculo" que aparecia por cima de cada célula.)

                // Contagem de estoque no canto inferior-direito.
                if (font != null && cand.Count > 1)
                {
                    string cnt = cand.Count.ToString();
                    float csc = 10f / 25f * _scale;
                    var csz = font.MeasureString(cnt) * csc;
                    var cpos = new Vector2(r.Right - csz.X - 2 * _scale, r.Bottom - csz.Y - 1 * _scale);
                    sb.DrawString(font, cnt, cpos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, csc, SpriteEffects.None, 0f);
                    sb.DrawString(font, cnt, cpos, new Color(240, 235, 210), 0f, Vector2.Zero, csc, SpriteEffects.None, 0f);
                }

                // Destaque do selecionado: aro claro em volta do soquete.
                if (_selected.HasValue && _selected.Value.Group == cand.Group && _selected.Value.Id == cand.Id)
                    DrawBorder(sb, pixel, r, new Color(235, 210, 150));

                _gridHits.Add((r, cand.Group, cand.Id));
            }
        }

        // Retângulo quadrado centrado em r, com fator de encolhimento.
        private static Rectangle ShrinkToSquare(Rectangle r, float f)
        {
            int side = (int)(Math.Min(r.Width, r.Height) * f);
            return new Rectangle(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2, side, side);
        }

        // Conteúdo do Potion Info (janela 2): ícone grande + nome (área _skillBgRect) e a
        // contagem no inventário (área "Description" = _rect1Rect).
        private void DrawItemInfo(SpriteBatch sb, SpriteFont font)
        {
            if (font == null || !_selected.HasValue) return;

            var sel = _selected.Value;
            var candidates = Candidates();
            string name = null;
            foreach (var c in candidates)
                if (c.Group == sel.Group && c.Id == sel.Id) { name = c.Name; break; }
            if (string.IsNullOrEmpty(name)) name = $"Item {sel.Group}:{sel.Id}";

            // Ícone grande CENTRADO no aro decorativo (_skillBgRect).
            var bg = _skillBgRect;
            int iconSide = (int)(Math.Min(bg.Width, bg.Height) * 0.60f);
            var iconRect = new Rectangle(
                bg.X + (bg.Width - iconSide) / 2,
                bg.Y + (bg.Height - iconSide) / 2,
                iconSide, iconSide);
            var icon = _hud?.GetItemIconByKey(sel.Group, sel.Id);
            if (Tex(icon)) sb.Draw(icon, iconRect, Color.White);
            // hole_glow por cima.
            if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(iconRect, 0.92f), Color.White);

            // Nome logo abaixo do aro (centrado na largura do aro).
            float nsc = 15f / 25f * _scale;
            var nsz = font.MeasureString(name) * nsc;
            var npos = new Vector2(bg.X + (bg.Width - nsz.X) / 2f, bg.Bottom - nsz.Y - 8 * _scale);
            sb.DrawString(font, name, npos + new Vector2(1, 1), Color.Black * 0.6f, 0f, Vector2.Zero, nsc, SpriteEffects.None, 0f);
            sb.DrawString(font, name, npos, new Color(230, 225, 215), 0f, Vector2.Zero, nsc, SpriteEffects.None, 0f);

            // "Descrição": nome + contagem no inventário (não há tooltip de item disponível).
            int invCount = _hud?.CountItemInInventory(sel.Group, sel.Id) ?? 0;
            string desc = $"{name}\nIn inventory: {invCount}";

            var box = _rect1Rect;
            int pad = (int)(10 * _scale);
            var inner = new Rectangle(box.X + pad, box.Y + pad, box.Width - 2 * pad, box.Height - 2 * pad);
            float dsc = 13f / 25f * _scale;
            float lineH = font.MeasureString("A").Y * dsc * 1.05f;
            float y = inner.Y;
            foreach (var line in WrapText(font, desc, dsc, inner.Width))
            {
                if (y + lineH > inner.Bottom) break;   // não vaza da caixa
                sb.DrawString(font, line, new Vector2(inner.X + 1, y + 1), Color.Black * 0.6f, 0f, Vector2.Zero, dsc, SpriteEffects.None, 0f);
                sb.DrawString(font, line, new Vector2(inner.X, y), new Color(210, 205, 195), 0f, Vector2.Zero, dsc, SpriteEffects.None, 0f);
                y += lineH;
            }
        }

        // Quebra de texto em linhas que cabem em maxWidth (px de tela), por palavra.
        private static IEnumerable<string> WrapText(SpriteFont font, string text, float scale, float maxWidth)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string cur = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    string test = cur.Length == 0 ? word : cur + " " + word;
                    if (font.MeasureString(test).X * scale > maxWidth && cur.Length > 0)
                    {
                        lines.Add(cur);
                        cur = word;
                    }
                    else cur = test;
                }
                lines.Add(cur);
            }
            return lines;
        }

        private void DrawTab(SpriteBatch sb, Texture2D pixel, SpriteFont font, Rectangle r, string label, bool active, float fontSize)
        {
            if (Tex(_texTab)) sb.Draw(_texTab, r, active ? Color.White : Color.White * 0.55f);
            else { sb.Draw(pixel, r, active ? new Color(60, 52, 44) : new Color(34, 30, 26)); DrawBorder(sb, pixel, r, new Color(90, 78, 62)); }
            DrawCenteredText(sb, font, label, r, active ? new Color(235, 210, 150) : new Color(150, 140, 120), fontSize / 25f * _scale);
        }

        // Botões Save/Cancel: folha ok_cancel-3 (467x80) com 2 CORES — Save verde (esq 0..232)
        // e Cancel vermelho (dir 233..466). isCancel escolhe a metade.
        private void DrawOkButton(SpriteBatch sb, SpriteFont font, Rectangle r, string label, float fontSize, bool isCancel)
        {
            if (Tex(_texOkCancel))
            {
                var src = isCancel ? new Rectangle(233, 0, 234, 80) : new Rectangle(0, 0, 233, 80);
                sb.Draw(_texOkCancel, r, src, Color.White);
            }
            DrawCenteredText(sb, font, label, r, new Color(230, 225, 215), fontSize / 25f * _scale);
        }

        private static void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle r, Color c)
        {
            sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
            sb.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
        }

        private static void DrawCenteredText(SpriteBatch sb, SpriteFont font, string text, Rectangle r, Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            var size = font.MeasureString(text) * scale;
            var pos = new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f);
            sb.DrawString(font, text, pos + new Vector2(1, 1), Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
