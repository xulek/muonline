using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Skill Imprint visual shell based on the official MU interface. The layout is exported
    /// by hud-edit-imprint through <see cref="ImprintLayout"/>. It has no server-side logic;
    /// the Imprint mechanic is not present in the Season 6 protocol.
    /// </summary>
    public sealed class SkillImprintControl : UIControl
    {
        private const string Dir = "Interface/Imprint/";
        private const string OkCancelTex = "Interface/CharCreate/ok_cancel.OZT"; // Standard OK/Cancel button.

        private Texture2D _texPanel, _texSlot, _texTab, _texOkCancel;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);
        private Texture2D _texPgLeft, _texPgRight, _texSideArrow, _texExpandPanel, _texSkillBg;
        private Texture2D _texRect, _texDarkCard, _texExpBorder;
        private Texture2D _texReset, _texResetText, _texClose, _texCloseHover, _texAttackRing, _texAttackIcon, _texAro;
        private Texture2D _texHoleGlow;   // Glow drawn over the skill, matching the hotbar effect.

        // Calculated rectangles in virtual screen coordinates.
        private Rectangle _panelRect, _titleRect, _closeRect, _tabBasicRect, _tabSpecialRect;
        private Rectangle _arrowLRect, _arrowRRect, _pageFieldRect, _sideBtnRect;
        private Rectangle _expandPanelRect, _skillBgRect, _title2Rect, _saveRect, _cancelRect;
        private Rectangle _rect1Rect, _rect2Rect, _rect3Rect, _title3Rect, _darkCardRect;
        private Rectangle _expBorder1Rect, _expBorder2Rect, _sideBtn2Rect;
        private Rectangle _resetRect, _resetTextRect, _attackBtnRect, _layout1Rect, _layout2Rect, _setTab1Rect, _setTab2Rect, _aroRect, _aro1Rect;
        private readonly System.Collections.Generic.List<Rectangle> _gridRects = new();
        private readonly System.Collections.Generic.List<Rectangle> _bottomRects = new();
        private readonly System.Collections.Generic.List<bool> _bottomEnabled = new();
        private readonly System.Collections.Generic.List<Rectangle> _set1Rects = new();
        private Rectangle _darkCard2Rect, _darkCard3Rect;
        private Rectangle _titleBarMainRect, _titleBarInfoRect;   // barras vermelhas (zonas de arraste)
        private bool _tabSpecial;

        // ── Dados de skill (migrado do SkillPanelControl) ───────────────────────────
        // Fonte: CharacterState (skills aprendidas) + ClassSkillRoster (roster da classe).
        // Bloqueadas = roster não aprendido → ícone "apagado" (newui_non_skill*).
        private readonly CharacterState _state;
        private readonly ModernBottomHud _hud;   // dono dos slots de ataque (hotbar real)
        private int _page;                 // página atual da grade Basic/Special
        private int _pageCount = 1;
        private ushort _selectedSkillId;   // skill selecionada (Skill Info mostra ela)
        // Hit-test dos slots da grade: (rect, skillId) do que está desenhado nesta página.
        private readonly List<(Rectangle Rect, ushort SkillId)> _gridHits = new();

        // ── Drag de skill (grade → slot de ataque) ──────────────────────────────────
        // Set 2 (7 slots) mapeia nos índices 0..6 do hotbar do ModernBottomHud (os mesmos
        // que o arco de ataque usa). Arrastar uma skill aprendida pra um slot equipa-a lá.
        private bool _dragSkill;           // arrastando uma skill da grade?
        private ushort _dragSkillId;       // skill sendo arrastada
        private Point _dragSkillPos;       // posição atual do cursor (pra desenhar o ícone)
        private Point _dragSkillStart;     // onde começou (pra distinguir clique de arraste)

        // Local draft: changes are applied to the combat hotbar only after SAVE.
        // Editing (drag/remove/reset/tab changes) updates these copies only. Cancel/Close
        // discards the draft and reloads the current hotbar state.
        private readonly ushort?[][] _draftSets = { new ushort?[4], new ushort?[7] };
        private int _draftActiveSet = 1;   // Set that becomes active after saving.
        private bool _wasVisible;          // Detects opening the screen to load the draft.

        // Two-panel behavior: both panels start open and adjacent. While Skill Info is open,
        // only its close edge is visible; otherwise the first panel's edge reopens Skill Info.
        private bool _expandOpen = true;
        private bool _closeHover;
        private bool _pgLeftHover, _pgRightHover;   // Pagination arrow hover state.
        private bool _resetHover;   // Reset button hover state.
        private bool _set2Selected;   // False = Set 1, true = Set 2.

        // Each panel has its own virtual-pixel offset. Dragging starts on its red title bar.
        // _mainDX/DY belongs to Skill Imprint; _infoDX/DY belongs to Skill Info.
        private int _mainDX, _mainDY, _infoDX, _infoDY;
        private enum DragTarget { None, Main, Info }
        private DragTarget _dragging = DragTarget.None;
        private Point _dragGrab;   // Pointer position when dragging starts.
        private int _dragBaseX, _dragBaseY;   // offset da janela no início do arraste

        public SkillImprintControl(CharacterState state = null, ModernBottomHud hud = null)
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
            _texTab = await L(Dir + "imprint_tab.OZP");
            _texPgLeft = await L(Dir + "imprint_pg_left.OZP");
            _texPgRight = await L(Dir + "imprint_pg_right.OZP");
            _texSideArrow = await L(Dir + "imprint_side_arrow.OZP");
            _texExpandPanel = await L(Dir + "imprint_expand_panel.OZP");
            _texSkillBg = await L(Dir + "imprint_skill_bg.OZP");
            _texRect = await L(Dir + "imprint_rect.OZP");
            _texDarkCard = await L(Dir + "imprint_dark_card.OZP");
            _texExpBorder = await L(Dir + "imprint_exp_border.OZP");
            _texAro = await L(Dir + "imprint_aro.OZP");
            _texReset = await L(Dir + "imprint_reset.OZP");
            _texResetText = await L(Dir + "imprint_reset_text.OZP");
            _texClose = await L(Dir + "imprint_close.OZP");
            _texCloseHover = await L(Dir + "imprint_close_hover.OZP");
            _texAttackRing = await L("Interface/SkillWin/attack_ring.OZP");
            _texAttackIcon = await L("Interface/SkillWin/attack_icon.OZP");
            _texHoleGlow = await L("Interface/SkillWin/hole_glow.OZP");   // brilho por cima da skill (igual hotbar)
            _texOkCancel = await L(OkCancelTex);

            // Atlas de ícones de skill: normal + variante "apagada" (newui_non_skill*), pra
            // pré-decodificar antes do primeiro desenho (evita slot vazio no 1º frame).
            foreach (var path in SkillIconAtlas.TexturePaths)
            {
                await L(path);
                await L(SkillIconRenderer.DisabledVariant(path));
            }

            // Abre mostrando o set que está ativo (persistido).
            if (_hud != null) _set2Selected = _hud.ActiveSet == 1;
            _loadedTheme = UiThemeManager.CurrentId;
        }

        public void Toggle()
        {
            Visible = !Visible;
            // Ao ABRIR a tela, as 2 janelas nascem sempre abertas (coladas).
            if (Visible) _expandOpen = true;
        }

        // Escala responsiva: a altura EFETIVA do painel = min(altura da tela, PanelMaxH=600).
        // Em telas baixas (ex.: 300px) o painel encolhe pra caber; em telas altas trava em 600.
        // Tudo (largura + posições dos elementos) escala pelo mesmo fator, mantendo a proporção.
        private float _scale = 1f;
        private int _panelOX, _panelOY, _panelW, _panelH;

        private void ComputeScale()
        {
            // Panels use the full virtual UI height. UiScaler maps that virtual space to the
            // physical display while preserving the same scale for widths and typography.
            int targetH = UiScaler.VirtualSize.Y;
            _scale = targetH / (float)ImprintLayout.PanelH;
            _panelH = targetH;
            _panelW = (int)(ImprintLayout.PanelW * _scale);
            _panelOX = (UiScaler.VirtualSize.X - _panelW) / 2 + (int)(ImprintLayout.PanelX * _scale);
            _panelOY = (UiScaler.VirtualSize.Y - _panelH) / 2 + (int)(ImprintLayout.PanelY * _scale);
        }

        // Rect de um elemento da JANELA 1 (Skill Imprint): coords do editor × escala + origem
        // + deslocamento de arraste da janela 1.
        private Rectangle R(float x, float y, float w, float h)
            => new(_panelOX + _mainDX + (int)(x * _scale), _panelOY + _mainDY + (int)(y * _scale),
                   (int)(w * _scale), (int)(h * _scale));

        // Rect de um elemento da JANELA 2 (Skill Info): mesma origem, mas com o deslocamento
        // de arraste PRÓPRIO da janela 2 (assim ela se move independente da janela 1).
        private Rectangle RI(float x, float y, float w, float h)
            => new(_panelOX + _infoDX + (int)(x * _scale), _panelOY + _infoDY + (int)(y * _scale),
                   (int)(w * _scale), (int)(h * _scale));

        private void Layout()
        {
            ComputeScale();
            // Painel principal já com o deslocamento de arraste da janela 1.
            _panelRect = new Rectangle(_panelOX + _mainDX, _panelOY + _mainDY, _panelW, _panelH);
            _titleRect = R(ImprintLayout.TitleX, ImprintLayout.TitleY, ImprintLayout.TitleW, ImprintLayout.TitleH);
            _closeRect = R(ImprintLayout.CloseX, ImprintLayout.CloseY, ImprintLayout.CloseW, ImprintLayout.CloseH);
            _tabBasicRect = R(ImprintLayout.TabBasicX, ImprintLayout.TabBasicY, ImprintLayout.TabBasicW, ImprintLayout.TabBasicH);
            _tabSpecialRect = R(ImprintLayout.TabSpecialX, ImprintLayout.TabSpecialY, ImprintLayout.TabSpecialW, ImprintLayout.TabSpecialH);
            _arrowLRect = R(ImprintLayout.ArrowLX, ImprintLayout.ArrowLY, ImprintLayout.ArrowLW, ImprintLayout.ArrowLH);
            _arrowRRect = R(ImprintLayout.ArrowRX, ImprintLayout.ArrowRY, ImprintLayout.ArrowRW, ImprintLayout.ArrowRH);
            _pageFieldRect = R(ImprintLayout.PageFieldX, ImprintLayout.PageFieldY, ImprintLayout.PageFieldW, ImprintLayout.PageFieldH);
            _sideBtnRect = R(ImprintLayout.SideBtnX, ImprintLayout.SideBtnY, ImprintLayout.SideBtnW, ImprintLayout.SideBtnH);
            // SideBtn2 = borda direita da JANELA 2 (fecha a 2ª) → segue a janela Info.
            _sideBtn2Rect = RI(ImprintLayout.SideBtn2X, ImprintLayout.SideBtn2Y, ImprintLayout.SideBtn2W, ImprintLayout.SideBtn2H);
            // ── Elementos da JANELA 2 (Skill Info): usam RI (deslocamento próprio) ──
            _expandPanelRect = RI(ImprintLayout.ExpandPanelX, ImprintLayout.ExpandPanelY, ImprintLayout.ExpandPanelW, ImprintLayout.ExpandPanelH);
            _skillBgRect = RI(ImprintLayout.SkillBgX, ImprintLayout.SkillBgY, ImprintLayout.SkillBgW, ImprintLayout.SkillBgH);
            _title2Rect = RI(ImprintLayout.Title2X, ImprintLayout.Title2Y, ImprintLayout.Title2W, ImprintLayout.Title2H);
            _rect1Rect = RI(ImprintLayout.Rect1X, ImprintLayout.Rect1Y, ImprintLayout.Rect1W, ImprintLayout.Rect1H);
            _rect2Rect = R(ImprintLayout.Rect2X, ImprintLayout.Rect2Y, ImprintLayout.Rect2W, ImprintLayout.Rect2H);
            _rect3Rect = R(ImprintLayout.Rect3X, ImprintLayout.Rect3Y, ImprintLayout.Rect3W, ImprintLayout.Rect3H);
            _title3Rect = RI(ImprintLayout.Title3X, ImprintLayout.Title3Y, ImprintLayout.Title3W, ImprintLayout.Title3H);
            _darkCardRect = RI(ImprintLayout.DarkCardX, ImprintLayout.DarkCardY, ImprintLayout.DarkCardW, ImprintLayout.DarkCardH);
            // ExpBorder1/SideBtn = borda direita da JANELA 1 (reabre a 2ª) → seguem a janela 1 (R).
            _expBorder1Rect = R(ImprintLayout.ExpBorder1X, ImprintLayout.ExpBorder1Y, ImprintLayout.ExpBorder1W, ImprintLayout.ExpBorder1H);
            _expBorder2Rect = RI(ImprintLayout.ExpBorder2X, ImprintLayout.ExpBorder2Y, ImprintLayout.ExpBorder2W, ImprintLayout.ExpBorder2H);
            _resetRect = R(ImprintLayout.ResetX, ImprintLayout.ResetY, ImprintLayout.ResetW, ImprintLayout.ResetH);
            _resetTextRect = R(ImprintLayout.ResetTextX, ImprintLayout.ResetTextY, ImprintLayout.ResetTextW, ImprintLayout.ResetTextH);
            _attackBtnRect = R(ImprintLayout.AttackBtnX, ImprintLayout.AttackBtnY, ImprintLayout.AttackBtnW, ImprintLayout.AttackBtnH);
            _layout1Rect = R(ImprintLayout.Layout1X, ImprintLayout.Layout1Y, ImprintLayout.Layout1W, ImprintLayout.Layout1H);
            _layout2Rect = R(ImprintLayout.Layout2X, ImprintLayout.Layout2Y, ImprintLayout.Layout2W, ImprintLayout.Layout2H);
            _setTab1Rect = R(ImprintLayout.SetTab1X, ImprintLayout.SetTab1Y, ImprintLayout.SetTab1W, ImprintLayout.SetTab1H);
            _setTab2Rect = R(ImprintLayout.SetTab2X, ImprintLayout.SetTab2Y, ImprintLayout.SetTab2W, ImprintLayout.SetTab2H);
            _aroRect = R(ImprintLayout.AroBgX, ImprintLayout.AroBgY, ImprintLayout.AroBgW, ImprintLayout.AroBgH);
            _aro1Rect = R(ImprintLayout.AroBg1X, ImprintLayout.AroBg1Y, ImprintLayout.AroBg1W, ImprintLayout.AroBg1H);
            _saveRect = R(ImprintLayout.SaveX, ImprintLayout.SaveY, ImprintLayout.SaveW, ImprintLayout.SaveH);
            _cancelRect = R(ImprintLayout.CancelX, ImprintLayout.CancelY, ImprintLayout.CancelW, ImprintLayout.CancelH);

            // Grade de slots (guia = GridX/Y, passo = W/H + Gap).
            _gridRects.Clear();
            int stepX = (int)ImprintLayout.GridW + ImprintLayout.GridGap;
            int stepY = (int)ImprintLayout.GridH + ImprintLayout.GridGap;
            for (int r = 0; r < ImprintLayout.GridRows; r++)
                for (int c = 0; c < ImprintLayout.GridCols; c++)
                    _gridRects.Add(R(ImprintLayout.GridX + c * stepX, ImprintLayout.GridY + r * stepY, ImprintLayout.GridW, ImprintLayout.GridH));

            // 7 slots SOLTOS da linha inferior (cada um com posição própria do editor).
            _bottomRects.Clear();
            _bottomEnabled.Clear();
            AddSlot(ImprintLayout.Slot1Enabled, ImprintLayout.Slot1X, ImprintLayout.Slot1Y, ImprintLayout.Slot1W, ImprintLayout.Slot1H);
            AddSlot(ImprintLayout.Slot2Enabled, ImprintLayout.Slot2X, ImprintLayout.Slot2Y, ImprintLayout.Slot2W, ImprintLayout.Slot2H);
            AddSlot(ImprintLayout.Slot3Enabled, ImprintLayout.Slot3X, ImprintLayout.Slot3Y, ImprintLayout.Slot3W, ImprintLayout.Slot3H);
            AddSlot(ImprintLayout.Slot4Enabled, ImprintLayout.Slot4X, ImprintLayout.Slot4Y, ImprintLayout.Slot4W, ImprintLayout.Slot4H);
            AddSlot(ImprintLayout.Slot5Enabled, ImprintLayout.Slot5X, ImprintLayout.Slot5Y, ImprintLayout.Slot5W, ImprintLayout.Slot5H);
            AddSlot(ImprintLayout.Slot6Enabled, ImprintLayout.Slot6X, ImprintLayout.Slot6Y, ImprintLayout.Slot6W, ImprintLayout.Slot6H);
            AddSlot(ImprintLayout.Slot7Enabled, ImprintLayout.Slot7X, ImprintLayout.Slot7Y, ImprintLayout.Slot7W, ImprintLayout.Slot7H);

            void AddSlot(bool on, float x, float y, float w, float h)
            {
                if (!on) return;
                _bottomRects.Add(R(x, y, w, h));
            }

            // Set 1: 4 slots.
            _set1Rects.Clear();
            if (ImprintLayout.Set1Slot1Enabled) _set1Rects.Add(R(ImprintLayout.Set1Slot1X, ImprintLayout.Set1Slot1Y, ImprintLayout.Set1Slot1W, ImprintLayout.Set1Slot1H));
            if (ImprintLayout.Set1Slot2Enabled) _set1Rects.Add(R(ImprintLayout.Set1Slot2X, ImprintLayout.Set1Slot2Y, ImprintLayout.Set1Slot2W, ImprintLayout.Set1Slot2H));
            if (ImprintLayout.Set1Slot3Enabled) _set1Rects.Add(R(ImprintLayout.Set1Slot3X, ImprintLayout.Set1Slot3Y, ImprintLayout.Set1Slot3W, ImprintLayout.Set1Slot3H));
            if (ImprintLayout.Set1Slot4Enabled) _set1Rects.Add(R(ImprintLayout.Set1Slot4X, ImprintLayout.Set1Slot4Y, ImprintLayout.Set1Slot4W, ImprintLayout.Set1Slot4H));
            _darkCard2Rect = R(ImprintLayout.DarkCard2X, ImprintLayout.DarkCard2Y, ImprintLayout.DarkCard2W, ImprintLayout.DarkCard2H);
            _darkCard3Rect = R(ImprintLayout.DarkCard3X, ImprintLayout.DarkCard3Y, ImprintLayout.DarkCard3W, ImprintLayout.DarkCard3H);

            // Barras de título (faixa vermelha no topo de cada janela) = zonas de arraste.
            // A barra ocupa a largura do painel na altura do título (~Y 30..72 no editor).
            const float BarTop = 30f, BarBot = 74f;
            _titleBarMainRect = new Rectangle(
                _panelRect.X, _panelOY + _mainDY + (int)(BarTop * _scale),
                _panelW, (int)((BarBot - BarTop) * _scale));
            // A barra da janela Info acompanha o painel expandido (mesma faixa de topo).
            _titleBarInfoRect = new Rectangle(
                _expandPanelRect.X, _panelOY + _infoDY + (int)(BarTop * _scale),
                _expandPanelRect.Width, (int)((BarBot - BarTop) * _scale));
        }

        // Skills a exibir na grade, já filtradas pela aba ativa (Basic/Special) e ordenadas
        // como o SkillPanelControl: roster da classe primeiro (aprendidas coloridas, o resto
        // apagado+cadeado), depois aprendidas fora do roster em ordem alfabética.
        //   Basic   = skills NÃO-master.
        //   Special = skills master (SkillUseType.MasterActive).
        private List<(ushort Id, SkillEntryState Entry)> BuildEntries()
        {
            var result = new List<(ushort, SkillEntryState)>();
            if (_state == null) return result;

            var learned = _state.GetSkills()?.ToList() ?? new List<SkillEntryState>();
            var learnedById = new Dictionary<ushort, SkillEntryState>();
            foreach (var s in learned) learnedById.TryAdd(s.SkillId, s);

            var roster = ClassSkillRoster.For(_state.Class);
            var all = new List<(ushort Id, SkillEntryState Entry)>();
            foreach (var id in roster)
                all.Add((id, learnedById.GetValueOrDefault(id)));
            foreach (var s in learned
                         .Where(s => Array.IndexOf(roster, s.SkillId) < 0)
                         .OrderBy(s => SkillDatabase.GetSkillName(s.SkillId)))
                all.Add((s.SkillId, s));

            // Filtro da aba: master vai pra Special, o resto pra Basic.
            foreach (var e in all)
                if (IsMasterSkill(e.Id) == _tabSpecial)
                    result.Add(e);
            return result;
        }

        private static bool IsMasterSkill(ushort skillId)
        {
            var def = SkillDatabase.GetSkillDefinition(skillId);
            return def != null && def.SkillUseType == (byte)Client.Data.BMD.SkillUseType.MasterActive;
        }

        // Descrições de fallback (inglês) pras skills utilitárias/buff que o arquivo de
        // tooltips do RealMU não traz texto. IDs = SkillNumber do OpenMU (S6).
        private static string FallbackDescription(ushort skillId) => skillId switch
        {
            18  => "Temporarily increases your defense.",
            67  => "Stuns the target, preventing it from moving for a short time.",
            68  => "Removes the stun effect.",
            69  => "Temporarily increases the mana of you and your party members.",
            70  => "Turns you invisible to monsters for a short time.",
            71  => "Removes the invisibility effect.",
            210 => "Party buff that grants a protective shield.",
            211 => "Restricts the target's movement.",
            212 => "Reveals and pursues the target.",
            213 => "Burns the target's shield with holy fire.",
            _   => null,
        };

        // Troca só a ABA mostrada. NÃO aplica nada no hotbar — o rascunho lembra qual set
        // ficará ativo ao salvar (_draftActiveSet); a hotbar só muda no Save.
        private void SelectSet(bool set2)
        {
            _set2Selected = set2;
            _draftActiveSet = set2 ? 1 : 0;
        }

        // Grava uma skill num slot do rascunho (dedupe dentro do mesmo set).
        private void DraftSet(int set, int idx, ushort skillId)
        {
            if (set < 0 || set >= _draftSets.Length) return;
            var arr = _draftSets[set];
            if (idx < 0 || idx >= arr.Length) return;
            for (int i = 0; i < arr.Length; i++)
                if (i != idx && arr[i] == skillId) arr[i] = null;
            arr[idx] = skillId;
        }

        // Copia o estado atual do hud pro rascunho (ao abrir a tela / cancelar).
        private void LoadDraftFromHud()
        {
            for (int set = 0; set < 2; set++)
            {
                int n = _draftSets[set].Length;
                for (int i = 0; i < n; i++)
                    _draftSets[set][i] = _hud?.GetSetSkillAt(set, i)?.SkillId;
            }
            _draftActiveSet = _hud?.ActiveSet ?? 1;
            _set2Selected = _draftActiveSet == 1;
        }

        // Empurra o rascunho pro hud (SAVE): grava os dois sets + ativa o set escolhido.
        // Só aqui a hotbar de combate é realmente alterada.
        private void CommitDraftToHud()
        {
            if (_hud == null) return;
            for (int set = 0; set < 2; set++)
            {
                _hud.ClearSet(set);
                var arr = _draftSets[set];
                for (int i = 0; i < arr.Length; i++)
                {
                    ushort? id = arr[i];
                    if (!id.HasValue) continue;
                    var entry = _state?.GetSkills()?.FirstOrDefault(s => s.SkillId == id.Value);
                    if (entry != null) _hud.SetSetSkillAt(set, i, entry);
                }
            }
            _hud.ActivateSet(_draftActiveSet);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Abriu agora? Carrega o rascunho do estado atual do hud (edição parte do salvo).
            if (Visible && !_wasVisible) LoadDraftFromHud();
            _wasVisible = Visible;

            if (!Visible) return;
            Layout();

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            var p = new Point(mouse.Position.X, mouse.Position.Y);

            bool down = mouse.LeftButton == ButtonState.Pressed;
            bool wasDown = prev.LeftButton == ButtonState.Pressed;

            // ── Drag & drop das janelas pela barra de título ──────────────────────────
            // Em andamento: segue o mouse enquanto o botão continua pressionado.
            if (_dragging != DragTarget.None)
            {
                if (down)
                {
                    int dx = p.X - _dragGrab.X;
                    int dy = p.Y - _dragGrab.Y;
                    if (_dragging == DragTarget.Main) { _mainDX = _dragBaseX + dx; _mainDY = _dragBaseY + dy; }
                    else { _infoDX = _dragBaseX + dx; _infoDY = _dragBaseY + dy; }
                    Layout();   // recomputa rects com o novo offset já neste frame
                    Scene?.SetMouseInputConsumed();
                    return;
                }
                _dragging = DragTarget.None;   // soltou
            }

            // Início do arraste: clicou (borda de descida) numa barra de título, fora do close.
            if (down && !wasDown && !(_closeRect.Contains(p) && ImprintLayout.CloseEnabled))
            {
                if (_expandOpen && _titleBarInfoRect.Contains(p))
                {
                    _dragging = DragTarget.Info; _dragGrab = p; _dragBaseX = _infoDX; _dragBaseY = _infoDY;
                    Scene?.SetMouseInputConsumed();
                    return;
                }
                if (_titleBarMainRect.Contains(p))
                {
                    _dragging = DragTarget.Main; _dragGrab = p; _dragBaseX = _mainDX; _dragBaseY = _mainDY;
                    Scene?.SetMouseInputConsumed();
                    return;
                }
            }

            // ── Drag de skill (grade → slot de ataque do Set 2) ───────────────────────
            // Em andamento: segue o cursor; ao soltar, equipa no slot do Set 2 sob o mouse.
            if (_dragSkill)
            {
                _dragSkillPos = p;
                if (down) { Scene?.SetMouseInputConsumed(); return; }

                // Soltou: se caiu num slot do set MOSTRADO, grava no RASCUNHO (não no hotbar).
                if (_dragSkillId != 0)
                {
                    int set = _set2Selected ? 1 : 0;
                    var slots = _set2Selected ? _bottomRects : _set1Rects;
                    for (int i = 0; i < slots.Count; i++)
                        if (slots[i].Contains(p)) { DraftSet(set, i, _dragSkillId); break; }
                }
                _dragSkill = false;
                Scene?.SetMouseInputConsumed();
                return;
            }

            // Início do drag: pressionou sobre um slot da grade com skill APRENDIDA e moveu.
            if (down && !wasDown)
            {
                foreach (var hit in _gridHits)
                {
                    if (!hit.Rect.Contains(p)) continue;
                    bool learned = _state?.GetSkills()?.Any(s => s.SkillId == hit.SkillId) == true;
                    if (learned) { _dragSkillId = hit.SkillId; _dragSkillStart = p; _dragSkillPos = p; }
                    break;
                }
            }
            // Promove pra drag quando o cursor se afasta o suficiente do ponto inicial.
            if (down && _dragSkillId != 0 && !_dragSkill)
            {
                int dxs = p.X - _dragSkillStart.X, dys = p.Y - _dragSkillStart.Y;
                if (dxs * dxs + dys * dys > 64) _dragSkill = true;   // >8px = arraste
            }
            if (!down) _dragSkillId = 0;   // soltou sem virar drag → cancela candidato

            // Hover do botão de fechar (troca pra arte hover-selected).
            _closeHover = ImprintLayout.CloseEnabled && _closeRect.Contains(p);
            _pgLeftHover = ImprintLayout.ArrowLEnabled && _arrowLRect.Contains(p);
            _pgRightHover = ImprintLayout.ArrowREnabled && _arrowRRect.Contains(p);
            _resetHover = ImprintLayout.ResetEnabled && _resetRect.Contains(p);

            bool click = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
            if (!click) return;

            // SAVE: confirma — só AQUI o rascunho é aplicado no hotbar de combate.
            if (ImprintLayout.SaveEnabled && _saveRect.Contains(p)) { CommitDraftToHud(); Scene?.SetMouseInputConsumed(); Visible = false; return; }
            // Close/Cancel: descarta o rascunho (recarrega do hud) e fecha — hotbar intacta.
            if (ImprintLayout.CloseEnabled && _closeRect.Contains(p)) { LoadDraftFromHud(); Scene?.SetMouseInputConsumed(); Visible = false; return; }
            if (ImprintLayout.CancelEnabled && _cancelRect.Contains(p)) { LoadDraftFromHud(); Scene?.SetMouseInputConsumed(); Visible = false; return; }
            // Abas Set 1/Set 2 alternam o set MOSTRADO na tela e o ATIVO (layout da hotbar 4/7).
            if (ImprintLayout.SetTab1Enabled && _setTab1Rect.Contains(p)) { SelectSet(false); Scene?.SetMouseInputConsumed(); return; }
            if (ImprintLayout.SetTab2Enabled && _setTab2Rect.Contains(p)) { SelectSet(true); Scene?.SetMouseInputConsumed(); return; }
            // Troca de aba: volta pra página 1 (o conteúdo muda).
            if (ImprintLayout.TabBasicEnabled && _tabBasicRect.Contains(p)) { _tabSpecial = false; _page = 0; Scene?.SetMouseInputConsumed(); return; }
            if (ImprintLayout.TabSpecialEnabled && _tabSpecialRect.Contains(p)) { _tabSpecial = true; _page = 0; Scene?.SetMouseInputConsumed(); return; }

            // Paginação da grade (setas laranja ◀ ▶).
            if (ImprintLayout.ArrowLEnabled && _arrowLRect.Contains(p)) { _page = Math.Max(0, _page - 1); Scene?.SetMouseInputConsumed(); return; }
            if (ImprintLayout.ArrowREnabled && _arrowRRect.Contains(p)) { _page = Math.Min(_pageCount - 1, _page + 1); Scene?.SetMouseInputConsumed(); return; }

            // Reset: limpa todos os slots do set MOSTRADO (só no rascunho).
            if (ImprintLayout.ResetEnabled && _resetRect.Contains(p))
            {
                Array.Clear(_draftSets[_set2Selected ? 1 : 0]);
                Scene?.SetMouseInputConsumed();
                return;
            }
            // Clique num slot com skill do set mostrado: remove (só no rascunho).
            {
                int set = _set2Selected ? 1 : 0;
                var slots = _set2Selected ? _bottomRects : _set1Rects;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!slots[i].Contains(p))
                        continue;

                    if (_draftSets[set][i].HasValue)
                    {
                        _draftSets[set][i] = null;
                    }
                    else if (_selectedSkillId != 0 &&
                             _state?.GetSkills()?.Any(s => s.SkillId == _selectedSkillId) == true)
                    {
                        _draftSets[set][i] = _selectedSkillId;
                    }

                    // Consume empty-slot clicks even without a valid selection.
                    Scene?.SetMouseInputConsumed();
                    return;
                }
            }

            // Clique num slot da grade: seleciona a skill (Skill Info mostra ela).
            foreach (var hit in _gridHits)
                if (hit.Rect.Contains(p)) { _selectedSkillId = hit.SkillId; Scene?.SetMouseInputConsumed(); return; }
            // Seta da 2ª janela (visível só quando ela está ABERTA): FECHA a 2ª.
            if (_expandOpen && ImprintLayout.SideBtn2Enabled && _sideBtn2Rect.Contains(p))
            {
                _expandOpen = false;
                Scene?.SetMouseInputConsumed();
                return;
            }
            // Seta da 1ª janela (visível só quando a 2ª está FECHADA): REABRE a 2ª.
            if (!_expandOpen && ImprintLayout.SideBtnEnabled && _sideBtnRect.Contains(p))
            {
                _expandOpen = true;
                Scene?.SetMouseInputConsumed();
                return;
            }
            if (_panelRect.Contains(p) || (_expandOpen && _expandPanelRect.Contains(p)))
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
            if (ImprintLayout.PanelEnabled)
            {
                if (Tex(_texPanel)) sb.Draw(_texPanel, _panelRect, Color.White);
                else sb.Draw(pixel, _panelRect, new Color(24, 20, 18) * 0.97f);
            }

            // Dark cards extras (opcionais).
            if (ImprintLayout.DarkCard2Enabled && Tex(_texDarkCard)) sb.Draw(_texDarkCard, _darkCard2Rect, Color.White);
            if (ImprintLayout.DarkCard3Enabled && Tex(_texDarkCard)) sb.Draw(_texDarkCard, _darkCard3Rect, Color.White);

            // Grade de skills de cima: casca (soquete) + ícone da skill do roster da classe.
            // Aprendida = ícone colorido; bloqueada = ícone apagado (newui_non_skill*).
            DrawSkillGrid(sb, pixel, font);
            // Linha inferior.
            // Aro ornamentado atrás dos slots — o do Set ativo (Set 1 e Set 2 têm o seu).
            if (_set2Selected) { if (ImprintLayout.AroBgEnabled && Tex(_texAro)) sb.Draw(_texAro, _aroRect, Color.White); }
            else { if (ImprintLayout.AroBg1Enabled && Tex(_texAro)) sb.Draw(_texAro, _aro1Rect, Color.White); }
            // Set 2 = 7 slots (arco); Set 1 = 4 slots. Alterna pela aba Set 1/Set 2.
            // Refletem o RASCUNHO (edição em andamento), não o hotbar — só o Save aplica.
            int shownSet = _set2Selected ? 1 : 0;
            var draft = _draftSets[shownSet];
            var activeSlots = _set2Selected ? _bottomRects : _set1Rects;
            for (int i = 0; i < activeSlots.Count; i++)
            {
                var r = activeSlots[i];
                DrawSlot(sb, pixel, r);
                ushort? id = i < draft.Length ? draft[i] : null;
                if (id.HasValue)
                {
                    var ic = ShrinkToSquare(r, 0.98f);
                    var cut = SkillIconRenderer.GetCircleIconTexture(id.Value);
                    if (cut != null) sb.Draw(cut, ic, Color.White);
                    else SkillIconRenderer.DrawSkillCircle(sb, id.Value, ic, Color.White, 2f);
                    if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(r, 0.90f), Color.White);
                }
            }
            // Botão de ATAQUE junto aos 7 slots (mesmo estilo, ajuda o player).
            if (ImprintLayout.AttackBtnEnabled)
            {
                // Botão de ataque COMPLETO (Set 1 e Set 2) como no HUD: aro/disco (attack_ring) + espada
                // (attack_icon) por cima, centralizada. Antes só desenhava o disco = slot cinza.
                var r = _attackBtnRect;
                if (Tex(_texAttackRing)) sb.Draw(_texAttackRing, r, Color.White);
                if (Tex(_texAttackIcon))
                {
                    var mid = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                    int iw = (int)(r.Width * 84f / 142f);
                    int ih = (int)(r.Height * 94f / 133f);
                    sb.Draw(_texAttackIcon, new Rectangle((int)(mid.X - iw / 2f), (int)(mid.Y - ih / 2f), iw, ih), Color.White);
                }
            }
            // Botão RESET (seta circular). Hover = glow bem visível (desenha 2x pra somar luz).
            if (ImprintLayout.ResetEnabled && Tex(_texReset))
                DrawHoverable(sb, _texReset, _resetRect, _resetHover);
            if (ImprintLayout.ResetTextEnabled && Tex(_texResetText))
                sb.Draw(_texResetText, _resetTextRect, Color.White);
            // (Setas laterais de troca de set REMOVIDAS — só as abas Set 1/Set 2 alternam.)
            // Abas Set 1 / Set 2 (arte de aba vermelha + texto, ativa destacada).
            if (ImprintLayout.SetTab1Enabled)
                DrawTab(sb, pixel, font, _setTab1Rect, "Set 1", !_set2Selected, ImprintLayout.SetTab1Font);
            if (ImprintLayout.SetTab2Enabled)
                DrawTab(sb, pixel, font, _setTab2Rect, "Set 2", _set2Selected, ImprintLayout.SetTab2Font);

            // Molduras da 1ª janela (Rect2/Rect3) — sempre visíveis, não dependem da 2ª.
            if (ImprintLayout.Rect2Enabled && Tex(_texRect)) Draw9Slice(sb, _texRect, _rect2Rect);
            if (ImprintLayout.Rect3Enabled && Tex(_texRect)) Draw9Slice(sb, _texRect, _rect3Rect);

            // 2ª JANELA (Skill Info): nasce ABERTA, colada na 1ª. Fecha/reabre pelas setas.
            if (_expandOpen && ImprintLayout.ExpandPanelEnabled)
            {
                if (Tex(_texExpandPanel)) sb.Draw(_texExpandPanel, _expandPanelRect, Color.White);
                if (ImprintLayout.DarkCardEnabled && Tex(_texDarkCard)) sb.Draw(_texDarkCard, _darkCardRect, Color.White);
                if (ImprintLayout.SkillBgEnabled && Tex(_texSkillBg)) sb.Draw(_texSkillBg, _skillBgRect, Color.White);
                if (ImprintLayout.Rect1Enabled && Tex(_texRect)) Draw9Slice(sb, _texRect, _rect1Rect);
                // Borda da 2ª janela: só ela aparece enquanto as duas estão abertas.
                if (ImprintLayout.ExpBorder2Enabled && Tex(_texExpBorder)) sb.Draw(_texExpBorder, _expBorder2Rect, Color.White);
                if (ImprintLayout.Title2Enabled)
                    DrawCenteredText(sb, font, "Skill Info", _title2Rect, new Color(230, 200, 120), ImprintLayout.Title2Font / 25f * _scale);
                if (ImprintLayout.Title3Enabled)
                    DrawCenteredText(sb, font, "Description", _title3Rect, new Color(230, 200, 120), ImprintLayout.Title3Font / 25f * _scale);

                // Conteúdo do Skill Info: ícone grande + nome (área _skillBgRect), e a
                // descrição (tooltip) na área "Description" (_rect1Rect).
                DrawSkillInfo(sb, font);
            }
            else
            {
                // 2ª fechada: a borda da 1ª janela aparece (ela virou a última da direita).
                if (ImprintLayout.ExpBorder1Enabled && Tex(_texExpBorder)) sb.Draw(_texExpBorder, _expBorder1Rect, Color.White);
            }

            // Setas de paginação (caixas laranja) + campo do número da página.
            // Hover = clareamento leve (mesmo padrão do OkCancelButton).
            if (ImprintLayout.ArrowLEnabled && Tex(_texPgLeft)) DrawHoverable(sb, _texPgLeft, _arrowLRect, _pgLeftHover);
            if (ImprintLayout.ArrowREnabled && Tex(_texPgRight)) DrawHoverable(sb, _texPgRight, _arrowRRect, _pgRightHover);
            if (ImprintLayout.PageFieldEnabled)
                DrawCenteredText(sb, font, (_page + 1).ToString(), _pageFieldRect, new Color(230, 200, 120), ImprintLayout.PageFieldFont / 25f * _scale);

            // Setas laterais: só UMA aparece por vez — a da janela mais à direita.
            //   2ª aberta  → seta da 2ª (fecha a 2ª). A da 1ª ficaria por cima dela.
            //   2ª fechada → seta da 1ª (reabre a 2ª).
            if (_expandOpen)
            {
                if (ImprintLayout.SideBtn2Enabled && Tex(_texSideArrow))
                    sb.Draw(_texSideArrow, _sideBtn2Rect, Color.White);
            }
            else
            {
                if (ImprintLayout.SideBtnEnabled && Tex(_texSideArrow))
                    sb.Draw(_texSideArrow, _sideBtnRect, Color.White);
            }

            // Abas.
            if (ImprintLayout.TabBasicEnabled) DrawTab(sb, pixel, font, _tabBasicRect, "Basic Skill", !_tabSpecial, ImprintLayout.TabBasicFont);
            if (ImprintLayout.TabSpecialEnabled) DrawTab(sb, pixel, font, _tabSpecialRect, "Special Skill", _tabSpecial, ImprintLayout.TabSpecialFont);

            // Título: só o TEXTO (a barra vermelha já vem no painel-template).
            if (ImprintLayout.TitleEnabled)
                DrawCenteredText(sb, font, "Skill Imprint", _titleRect, new Color(230, 200, 120), ImprintLayout.TitleFont / 25f * _scale);

            // Botão de fechar (arte). Troca pra variação hover quando o mouse está em cima.
            if (ImprintLayout.CloseEnabled)
            {
                var closeTex = _closeHover ? _texCloseHover : _texClose;
                if (Tex(closeTex)) sb.Draw(closeTex, _closeRect, Color.White);
                else DrawCenteredText(sb, font, "X", _closeRect, new Color(220, 90, 70), ImprintLayout.CloseFont / 25f * _scale);
            }

            // Botões Save/Cancel (arte padrão ok_cancel).
            if (ImprintLayout.SaveEnabled) DrawOkButton(sb, font, _saveRect, "Save", ImprintLayout.SaveFont, false);
            if (ImprintLayout.CancelEnabled) DrawOkButton(sb, font, _cancelRect, "Cancel", ImprintLayout.CancelFont, true);

            // Ícone "fantasma" que segue o cursor enquanto arrasta uma skill pro slot.
            if (_dragSkill && _dragSkillId != 0)
            {
                int side = (int)(48 * _scale);
                var gr = new Rectangle(_dragSkillPos.X - side / 2, _dragSkillPos.Y - side / 2, side, side);
                var cut = SkillIconRenderer.GetCircleIconTexture(_dragSkillId);
                if (cut != null) sb.Draw(cut, gr, Color.White * 0.9f);
                if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(gr, 0.90f), Color.White * 0.9f);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, UiScaler.SpriteTransform);

            base.Draw(gameTime);
        }

        private static bool Tex(Texture2D t) => t != null && !t.IsDisposed;

        // Desenha uma arte com efeito de HOVER visível: normal sempre; em hover, uma
        // segunda passada ADDITIVE por cima soma luz (brilho real, não só tint claro).
        private void DrawHoverable(SpriteBatch sb, Texture2D tex, Rectangle rect, bool hover)
        {
            sb.Draw(tex, rect, Color.White);
            if (!hover) return;
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);
            sb.Draw(tex, rect, new Color(120, 110, 80));   // brilho quente somado
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);
        }

        // Desenha uma moldura em 9-slice: os 4 CANTOS ficam em tamanho FIXO e só as bordas/
        // centro são esticados. Assim a espessura da borda é CONSTANTE em qualquer tamanho
        // (sem isso, esticar a arte 254×258 num rect baixo afinava a borda — feedback do
        // usuário: a moldura dos 5 slots ficava fina/diferente das grandes).
        private void Draw9Slice(SpriteBatch sb, Texture2D tex, Rectangle dst)
        {
            int tw = tex.Width, th = tex.Height;
            // A arte é uma TILE 9-slice mínima (14×14, borda 5px). Canto fonte = borda = 5px.
            // A espessura no destino é FIXA (o canto não estica junto com o eixo longo), então
            // a borda fica com a MESMA grossura em todos os lados e em qualquer tamanho.
            int m = System.Math.Min(5, System.Math.Min(tw, th) / 2 - 1);   // canto fonte (= borda da tile)
            if (m < 1) { sb.Draw(tex, dst, Color.White); return; }
            // Espessura destino: acompanha o zoom do painel (escala vertical), mesma nos 4 lados.
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

        // Grade Basic/Special: soquete + ícone de cada skill do roster (paginado). Preenche
        // _gridHits pro clique de seleção. Portado do SkillPanelControl.DrawSkillGrid, mas
        // usando os rects fixos da grade do editor (_gridRects) em vez de layout próprio.
        private void DrawSkillGrid(SpriteBatch sb, Texture2D pixel, SpriteFont font)
        {
            _gridHits.Clear();

            var entries = BuildEntries();
            int pageSize = Math.Max(1, _gridRects.Count);
            _pageCount = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
            _page = Math.Clamp(_page, 0, _pageCount - 1);

            // Seleção inicial: 1ª skill da 1ª página (como o mobile auto-seleciona).
            if (_selectedSkillId == 0 && entries.Count > 0)
                _selectedSkillId = entries[0].Id;

            int start = _page * pageSize;
            for (int cell = 0; cell < _gridRects.Count; cell++)
            {
                var r = _gridRects[cell];
                DrawSlot(sb, pixel, r);   // soquete sempre (vazio nos slots sem skill)

                int idx = start + cell;
                if (idx >= entries.Count) continue;

                var (skillId, entry) = entries[idx];
                bool isLearned = entry != null;

                // Ícone circular pré-cortado; bloqueada usa o atlas "apagado".
                // 0.98 = preenche quase todo o círculo do soquete (o corte já é redondo com
                // borda AA, então pode encostar na moldura sem lacuna).
                var iconRect = ShrinkToSquare(r, 0.98f);
                var cut = SkillIconRenderer.GetCircleIconTexture(skillId, disabled: !isLearned);
                if (cut != null)
                    sb.Draw(cut, iconRect, Color.White);
                else
                    SkillIconRenderer.DrawSkillCircle(sb, skillId, iconRect, isLearned ? Color.White : Color.Gray, 2f);

                // Brilho por cima (mesmo efeito da hotbar): glow 0.90 do rect do soquete.
                if (Tex(_texHoleGlow))
                    sb.Draw(_texHoleGlow, ShrinkToSquare(r, 0.90f), Color.White);

                // Destaque da selecionada: aro branco fino em volta do soquete.
                if (skillId == _selectedSkillId)
                    DrawBorder(sb, pixel, r, new Color(235, 210, 150));

                _gridHits.Add((r, skillId));
            }
        }

        // Retângulo quadrado centrado em r, com fator de encolhimento (pro ícone ficar
        // concêntrico dentro do círculo do soquete).
        private static Rectangle ShrinkToSquare(Rectangle r, float f)
        {
            int side = (int)(Math.Min(r.Width, r.Height) * f);
            return new Rectangle(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2, side, side);
        }

        // Conteúdo do Skill Info (janela 2): ícone grande + nome (área _skillBgRect) e a
        // descrição/tooltip com quebra de linha (área "Description" = _rect1Rect).
        private void DrawSkillInfo(SpriteBatch sb, SpriteFont font)
        {
            if (font == null) return;
            ushort selId = _selectedSkillId;
            if (selId == 0) return;

            // Está aprendida? (define o ícone colorido vs apagado)
            var learned = _state?.GetSkills()?.FirstOrDefault(s => s.SkillId == selId);
            bool isLearned = learned != null;

            // Ícone grande CENTRADO no aro decorativo (_skillBgRect). O corte já é redondo;
            // fica concêntrico ao círculo interno do aro.
            var bg = _skillBgRect;
            int iconSide = (int)(Math.Min(bg.Width, bg.Height) * 0.60f);
            var iconRect = new Rectangle(
                bg.X + (bg.Width - iconSide) / 2,
                bg.Y + (bg.Height - iconSide) / 2,
                iconSide, iconSide);
            var cut = SkillIconRenderer.GetCircleIconTexture(selId, disabled: !isLearned);
            if (cut != null) sb.Draw(cut, iconRect, Color.White);
            else SkillIconRenderer.DrawSkillCircle(sb, selId, iconRect, isLearned ? Color.White : Color.Gray, 2f);
            // hole_glow por cima (mesmo efeito dos outros 3 lugares).
            if (Tex(_texHoleGlow)) sb.Draw(_texHoleGlow, ShrinkToSquare(iconRect, 0.92f), Color.White);

            // Nome logo abaixo do aro (centrado na largura do aro).
            string name = SkillDatabase.GetSkillName(selId);
            float nsc = 15f / 25f * _scale;
            var nsz = font.MeasureString(name) * nsc;
            var npos = new Vector2(bg.X + (bg.Width - nsz.X) / 2f, bg.Bottom - nsz.Y - 8 * _scale);
            sb.DrawString(font, name, npos + new Vector2(1, 1), Color.Black * 0.6f, 0f, Vector2.Zero, nsc, SpriteEffects.None, 0f);
            sb.DrawString(font, name, npos, new Color(230, 225, 215), 0f, Vector2.Zero, nsc, SpriteEffects.None, 0f);

            // Descrição (tooltip) com word-wrap dentro da área "Description".
            // O Data do RealMU (skilltooltiptext.bmd / Lang.mpr) já vem em INGLÊS, então
            // usamos o texto CRU do tooltip — sem a camada de tradução KO→EN (Resolve),
            // que só serve pra Data coreano.
            string desc = null;
            // Fallback: buffs/utilitárias que o skilltooltiptext.bmd não cobre ficariam sem
            // descrição. Texto oficial curto em inglês pra essas.
            if (string.IsNullOrWhiteSpace(desc)) desc = FallbackDescription(selId);
            if (string.IsNullOrWhiteSpace(desc)) return;
            // Remove marcadores {valor} do template (a versão rica com cores fica pra depois).
            desc = desc.Replace("{", string.Empty).Replace("}", string.Empty);

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
