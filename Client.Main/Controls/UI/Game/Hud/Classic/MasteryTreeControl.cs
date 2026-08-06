using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Mastery window using the official screen layout: one window composed from the
    /// official mastery artwork, three columns (common, specialization I, specialization II),
    /// circular nodes, and prerequisite connections.
    ///
    /// ServerMasterSkills defines the tree; learned levels come from CharacterState
    /// (MasterSkillList/Update). Clicking a node sends AddMasterSkillPoint; the server
    /// validates points, rank, and the level-10 prerequisite before replying.
    ///
    /// The window opens with the A key or the Allocate tree icon in the menu fold-out.
    /// Geometry is driven by MasteryLayout.
    /// </summary>
    public sealed class MasteryTreeControl : UIControl
    {
        public static MasteryTreeControl Instance { get; private set; }

        private const string BgPath = "Interface/Mastery/mastery_bg.OZP";
        private const string SlotPath = "Interface/Imprint/imprint_slot.OZP";
        private const string GlowPath = "Interface/SkillWin/hole_glow.OZP";
        private const string ClosePath = "Interface/Imprint/imprint_close.OZP";
        private const string CloseHoverPath = "Interface/Imprint/imprint_close_hover.OZP";

        private readonly CharacterState _state;

        private Texture2D _texBg, _texSlot, _texGlow, _texClose, _texCloseHover;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);

        // Responsive scale: fit the 1024x1023 artwork into min(screen height, PanelMaxH)
        // and keep it centered.
        private float _scale = 1f;
        private int _ox, _oy;

        private sealed class Node
        {
            public ServerMasterSkills.Entry Entry;
            public int SubCol;
            public Rectangle Rect;      // Socket rectangle in screen coordinates.
        }

        private readonly List<Node> _nodes = new();
        private byte _builtClass = 0xFF;
        private ushort _hoverSkill;
        private Rectangle _closeRect;
        private bool _closeHover;

        // Window dragging uses the title bar; the user offset is added to the centered panel.
        private bool _dragging;
        private Point _dragLast;
        private int _userOX, _userOY;

        public MasteryTreeControl(CharacterState state)
        {
            _state = state;
            Instance = this;
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
            _texBg = await L(BgPath);
            _texSlot = await L(SlotPath);
            _texGlow = await L(GlowPath);
            _texClose = await L(ClosePath);
            _texCloseHover = await L(CloseHoverPath);

            // Atlas dos ícones (normal + apagado) pré-decodificado, como no Imprint.
            foreach (var path in SkillIconAtlas.TexturePaths)
            {
                await L(path);
                await L(SkillIconRenderer.DisabledVariant(path));
            }
            _loadedTheme = UiThemeManager.CurrentId;
        }

        public void Toggle()
        {
            Visible = !Visible;
            if (Visible)
            {
                BringToFront();
                SoundController.Instance.PlayBuffer("Sound/iCreateWindow.wav");
            }
        }

        // Current class tree. A child inherits its parent's column for vertical links;
        // same-rank dependencies use the adjacent column. Nodes without a parent use the
        // first free column in their rank. Allocation is deterministic by Number.
        private void EnsureTree()
        {
            byte masterClass = ServerMasterSkills.ToMasterClass((byte)_state.Class);
            if (masterClass == _builtClass) return;
            _builtClass = masterClass;
            _nodes.Clear();

            var byNumber = new Dictionary<ushort, Node>();
            var occupied = new HashSet<(byte Root, byte Rank, int Sub)>();

            foreach (var e in ServerMasterSkills.All.OrderBy(e => e.Number))
            {
                if (Array.IndexOf(e.Classes, masterClass) < 0) continue;

                int sub = 0;
                if (e.Required != 0 && byNumber.TryGetValue(e.Required, out var parent)
                    && parent.Entry.Root == e.Root)
                {
                    sub = e.Rank > parent.Entry.Rank ? parent.SubCol : parent.SubCol + 1;
                }
                while (occupied.Contains((e.Root, e.Rank, sub)))
                    sub++;
                sub = Math.Min(sub, MasteryLayout.SubCols - 1);
                while (occupied.Contains((e.Root, e.Rank, sub)))
                    sub++;   // Last resort; should not be reached.

                occupied.Add((e.Root, e.Rank, sub));
                var node = new Node { Entry = e, SubCol = sub };
                _nodes.Add(node);
                byNumber[e.Number] = node;
            }
        }

        // ── Geometria ────────────────────────────────────────────────────
        private void ComputeScale()
        {
            int scrW = UiScaler.VirtualSize.X, scrH = UiScaler.VirtualSize.Y;
            // Project standard: the panel uses the full virtual height (720), and UiScaler
            // maps it to the physical screen so the window scales with the other UI.
            _scale = scrH / MasteryLayout.ArtH;
            int panelW = (int)(MasteryLayout.ArtW * _scale);
            int panelH = (int)(MasteryLayout.ArtH * _scale);
            // Centered plus drag offset, clamped inside the screen.
            _ox = Math.Clamp((scrW - panelW) / 2 + _userOX, 0, Math.Max(0, scrW - panelW));
            _oy = Math.Clamp((scrH - panelH) / 2 + _userOY, 0, Math.Max(0, scrH - panelH));
            // Recalculate the clamped offset to avoid accumulating drag outside the screen.
            _userOX = _ox - (scrW - panelW) / 2;
            _userOY = _oy - (scrH - panelH) / 2;
        }

        private Rectangle R(float x, float y, float w, float h) =>
            new((int)(_ox + x * _scale), (int)(_oy + y * _scale),
                (int)(w * _scale), (int)(h * _scale));

        private float ColX(byte root) => root switch
        {
            0 => MasteryLayout.Col1X,
            1 => MasteryLayout.Col2X,
            _ => MasteryLayout.Col3X,
        };

            // Node center in artwork coordinates.
        private Vector2 NodeCenterArt(Node n)
        {
            float colX = ColX(n.Entry.Root);
            float usable = MasteryLayout.ColW - MasteryLayout.SubColPad * 2f;
            float subW = usable / MasteryLayout.SubCols;
            float cx = colX + MasteryLayout.SubColPad + subW * (n.SubCol + 0.5f);
            float cy = MasteryLayout.FirstRowCY + (n.Entry.Rank - 1) * MasteryLayout.RowH;
            return new Vector2(cx, cy);
        }

        private Rectangle NodeRect(Node n)
        {
            var c = NodeCenterArt(n);
            float half = MasteryLayout.NodeSize / 2f;
            return R(c.X - half, c.Y - half, MasteryLayout.NodeSize, MasteryLayout.NodeSize);
        }

        // Input.
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;

            EnsureTree();
            ComputeScale();

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            var p = mouse.Position;
            bool down = mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
            bool click = down
                         && prev.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released;

            _closeRect = R(MasteryLayout.CloseX, MasteryLayout.CloseY,
                           MasteryLayout.CloseSize, MasteryLayout.CloseSize);
            _closeHover = _closeRect.Contains(p);

            // Drag from the title bar area above the columns, excluding the close button.
            var titleRect = R(0, 0, MasteryLayout.ArtW, MasteryLayout.DragBarH);
            if (_dragging)
            {
                if (!down) _dragging = false;
                else
                {
                    _userOX += p.X - _dragLast.X;
                    _userOY += p.Y - _dragLast.Y;
                    _dragLast = p;
                    ComputeScale();
                    Scene?.SetMouseInputConsumed();
                    return;   // Do not process nodes or close while dragging.
                }
            }
            else if (click && titleRect.Contains(p) && !_closeHover)
            {
                _dragging = true;
                _dragLast = p;
                Scene?.SetMouseInputConsumed();
                return;
            }

            _hoverSkill = 0;
            foreach (var n in _nodes)
            {
                n.Rect = NodeRect(n);
                if (n.Rect.Contains(p)) _hoverSkill = n.Entry.Number;
            }

            if (!click) return;

            if (_closeHover)
            {
                Visible = false;
                Scene?.SetMouseInputConsumed();
                return;
            }

            var panelRect = R(0, 0, MasteryLayout.ArtW, MasteryLayout.ArtH);
            if (!panelRect.Contains(p)) return;   // Click outside: let the world receive it.

            Scene?.SetMouseInputConsumed();

            if (_hoverSkill != 0)
                TryLearn(_hoverSkill);
        }

        private void TryLearn(ushort skillId)
        {
            if (_state.MasterLevelUpPoints == 0)
            {
                AddChat("No master points available.");
                return;
            }

            var node = _nodes.FirstOrDefault(n => n.Entry.Number == skillId);
            if (node == null) return;

            int level = GetLevel(skillId);
            if (level >= node.Entry.MaxLevel)
            {
                AddChat("This skill is already at maximum level.");
                return;
            }
            if (node.Entry.Required != 0 && GetLevel(node.Entry.Required) < 10)
            {
                AddChat($"Requires {ServerMasterSkills.All.First(e => e.Number == node.Entry.Required).Name} at level 10.");
                return;
            }

            // The server performs authoritative validation; this local gate is UX only.
            var svc = MuGame.Network?.GetCharacterService();
            if (svc == null) return;
            // Older protocol branches in this client do not expose the master-skill request
            // yet. Use a late-bound call so the Classic window remains compatible with heads
            // that add the request without inventing a normal-skill packet here.
            var method = svc.GetType().GetMethod("SendAddMasterSkillPointRequestAsync");
            if (method?.Invoke(svc, new object[] { skillId }) is Task request)
                _ = request;
            else
                AddChat("Master skill allocation is not available on this protocol.");
        }

        private static void AddChat(string msg)
        {
            var scene = MuGame.Instance?.ActiveScene as Scenes.GameScene;
            scene?.ChatLog?.AddMessage("System", msg, Models.MessageType.System);
        }

        private int GetLevel(ushort skillId) =>
            _state.GetSkills()?.FirstOrDefault(s => s.SkillId == skillId)?.SkillLevel ?? 0;

        // Draw.
        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;
            var sb = GraphicsManager.Instance?.Sprite;
            var pixel = GraphicsManager.Instance?.Pixel;
            var font = GraphicsManager.Instance?.Font;
            if (sb == null || pixel == null || font == null) return;

            EnsureTree();
            ComputeScale();

            // Batch próprio LinearClamp (UI reescalada; PointClamp serrilha) — padrão §4.
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                     null, null, null, UiScaler.SpriteTransform);

            var panel = R(0, 0, MasteryLayout.ArtW, MasteryLayout.ArtH);
            if (_texBg != null && !_texBg.IsDisposed)
                sb.Draw(_texBg, panel, Color.White);
            else
                sb.Draw(pixel, panel, ModernHudTheme.BgDark * 0.96f);

            DrawHeader(sb, font);
            DrawLinks(sb, pixel);
            DrawNodes(sb, font);
            DrawClose(sb);
            DrawTooltip(sb, font, pixel);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                     null, null, null, UiScaler.SpriteTransform);

            base.Draw(gameTime);
        }

        private void DrawCentered(SpriteBatch sb, SpriteFont font, string text, float cxArt, float cyArt,
                                  float fontPt, Color color, float shadow = 0.7f)
        {
            float sc = fontPt / 25f * _scale;
            var size = font.MeasureString(text) * sc;
            var pos = new Vector2(_ox + cxArt * _scale - size.X / 2f, _oy + cyArt * _scale - size.Y / 2f);
            if (shadow > 0f)
                sb.DrawString(font, text, pos + new Vector2(1, 1), Color.Black * shadow, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
        }

        private void DrawHeader(SpriteBatch sb, SpriteFont font)
        {
            var gold = ModernHudTheme.TextGold;
            var white = ModernHudTheme.TextWhite;

            string cls = CharacterClassDatabase.GetClassName(_state.Class);
            DrawCentered(sb, font, cls, MasteryLayout.ClassNameCX, MasteryLayout.TitleTextY,
                         MasteryLayout.TitleFont, gold);

            float expPct = _state.MasterExperienceForNextLevel > 0
                ? MathHelper.Clamp((float)(_state.MasterExperience / (double)_state.MasterExperienceForNextLevel) * 100f, 0f, 100f)
                : 0f;
            DrawCentered(sb, font, $"Level: {_state.MasterLevel}", MasteryLayout.Field1CX,
                         MasteryLayout.FieldTextY, MasteryLayout.FieldFont, white);
            DrawCentered(sb, font, $"Points: {_state.MasterLevelUpPoints}", MasteryLayout.Field2CX,
                         MasteryLayout.FieldTextY, MasteryLayout.FieldFont, white);
            DrawCentered(sb, font, $"EXP: {expPct:0.00}%", MasteryLayout.Field3CX,
                         MasteryLayout.FieldTextY, MasteryLayout.FieldFont, white);

            DrawCentered(sb, font, MasteryLayout.ColTitleLeft,
                         MasteryLayout.Col1X + MasteryLayout.ColW / 2f, MasteryLayout.ColHeaderY,
                         MasteryLayout.ColHeaderFont, white);
            DrawCentered(sb, font, MasteryLayout.ColTitleMiddle,
                         MasteryLayout.Col2X + MasteryLayout.ColW / 2f, MasteryLayout.ColHeaderY,
                         MasteryLayout.ColHeaderFont, white);
            DrawCentered(sb, font, MasteryLayout.ColTitleRight,
                         MasteryLayout.Col3X + MasteryLayout.ColW / 2f, MasteryLayout.ColHeaderY,
                         MasteryLayout.ColHeaderFont, white);
        }

            // Prerequisite connections: gold parent-to-child lines, vertical in the same
            // sub-column, horizontal in the same rank, or L-shaped otherwise.
        private void DrawLinks(SpriteBatch sb, Texture2D pixel)
        {
            var gold = ModernHudTheme.Accent * 0.9f;
            int w = Math.Max(1, (int)(MasteryLayout.LinkWidth * _scale));
            float half = MasteryLayout.NodeSize / 2f;

            foreach (var n in _nodes)
            {
                if (n.Entry.Required == 0) continue;
                var parent = _nodes.FirstOrDefault(x => x.Entry.Number == n.Entry.Required);
                if (parent == null || parent.Entry.Root != n.Entry.Root) continue;

                var a = NodeCenterArt(parent);
                var b = NodeCenterArt(n);

                if (n.SubCol == parent.SubCol)
                {
                    // vertical: do pé do pai ao topo do filho
                    var r = R(a.X - MasteryLayout.LinkWidth / 2f, a.Y + half,
                              MasteryLayout.LinkWidth, (b.Y - half) - (a.Y + half));
                    if (r.Height > 0) sb.Draw(pixel, r, gold);
                }
                else if (n.Entry.Rank == parent.Entry.Rank)
                {
                    // horizontal: borda a borda
                    float x0 = Math.Min(a.X, b.X) + half, x1 = Math.Max(a.X, b.X) - half;
                    var r = R(x0, a.Y - MasteryLayout.LinkWidth / 2f, x1 - x0, MasteryLayout.LinkWidth);
                    if (r.Width > 0) sb.Draw(pixel, r, gold);
                }
                else
                {
                    // L: desce do pai até a linha do filho, depois vai até a borda dele
                    var rv = R(a.X - MasteryLayout.LinkWidth / 2f, a.Y + half,
                               MasteryLayout.LinkWidth, (b.Y - a.Y) - half);
                    if (rv.Height > 0) sb.Draw(pixel, rv, gold);
                    float hx0 = Math.Min(a.X, b.X - half), hx1 = Math.Max(a.X, b.X - half);
                    if (b.X < a.X) { hx0 = b.X + half; hx1 = a.X; }
                    var rh = R(hx0, b.Y - MasteryLayout.LinkWidth / 2f, hx1 - hx0, MasteryLayout.LinkWidth);
                    if (rh.Width > 0) sb.Draw(pixel, rh, gold);
                }
            }
        }

        private void DrawNodes(SpriteBatch sb, SpriteFont font)
        {
            var gold = ModernHudTheme.TextGold;
            var grey = ModernHudTheme.TextGray;

            foreach (var n in _nodes)
            {
                var r = n.Rect = NodeRect(n);

                if (_texSlot != null && !_texSlot.IsDisposed)
                    sb.Draw(_texSlot, r, Color.White);

                int level = GetLevel(n.Entry.Number);
                bool learned = level > 0;
                bool unlocked = n.Entry.Required == 0 || GetLevel(n.Entry.Required) >= 10;

                // Ícone circular HD (mesma pilha do Imprint): pré-cortado; bloqueada = atlas apagado.
                var iconRect = ShrinkToSquare(r, 0.98f);
                var cut = SkillIconRenderer.GetCircleIconTexture(n.Entry.Number, disabled: !learned);
                var tint = learned ? Color.White : (unlocked ? Color.White : ModernHudTheme.TextDark);
                if (cut != null)
                    sb.Draw(cut, iconRect, tint);
                else
                    SkillIconRenderer.DrawSkillCircle(sb, n.Entry.Number, iconRect,
                        learned ? ModernHudTheme.TextWhite : ModernHudTheme.TextGray, 2f);

                if (_texGlow != null && !_texGlow.IsDisposed)
                    sb.Draw(_texGlow, ShrinkToSquare(r, 0.90f), Color.White);

                // Destaque de hover: aro fino dourado.
                if (n.Entry.Number == _hoverSkill)
                    DrawBorder(sb, GraphicsManager.Instance.Pixel, r, gold);

                // Contagem "N" no canto inf-dir do nó (como a referência).
                string cnt = level.ToString();
                float sc = MasteryLayout.CountFont / 25f * _scale;
                var pos = new Vector2(r.Right + MasteryLayout.CountDX * _scale,
                                      r.Bottom - MasteryLayout.CountDY * _scale);
                sb.DrawString(font, cnt, pos + Vector2.One, Color.Black * 0.8f, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                sb.DrawString(font, cnt, pos, learned ? gold : grey, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            }
        }

        private void DrawClose(SpriteBatch sb)
        {
            var tex = _closeHover && _texCloseHover != null ? _texCloseHover : _texClose;
            if (tex != null && !tex.IsDisposed)
                sb.Draw(tex, _closeRect, Color.White);
        }

        private void DrawTooltip(SpriteBatch sb, SpriteFont font, Texture2D pixel)
        {
            if (_hoverSkill == 0) return;
            var node = _nodes.FirstOrDefault(n => n.Entry.Number == _hoverSkill);
            if (node == null) return;

            int level = GetLevel(node.Entry.Number);
            var lines = new List<(string Text, Color Color)>
            {
                (node.Entry.Name, ModernHudTheme.TextGold),
                ($"Level: {level} / {node.Entry.MaxLevel}", ModernHudTheme.TextWhite),
            };
            if (node.Entry.Required != 0)
            {
                string reqName = ServerMasterSkills.All.First(e => e.Number == node.Entry.Required).Name;
                bool ok = GetLevel(node.Entry.Required) >= 10;
                lines.Add(($"Requires: {reqName} Lv.10", ok ? ModernHudTheme.Success : ModernHudTheme.Danger));
            }
            if (_state.MasterLevelUpPoints > 0 && level < node.Entry.MaxLevel)
                lines.Add(("Click to learn (+1)", ModernHudTheme.TextGray));

            // Fonte maior pro TÍTULO; corpo na fonte do layout. Tudo medido antes.
            float scBody = MasteryLayout.TooltipFont / 25f * _scale;
            float scTitle = (MasteryLayout.TooltipFont + 2f) / 25f * _scale;
            const float lineGap = 5f;

            float maxW = 0, totH = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                float sc0 = i == 0 ? scTitle : scBody;
                var s = font.MeasureString(lines[i].Text) * sc0;
                maxW = MathF.Max(maxW, s.X);
                totH += s.Y + lineGap;
            }

            // Ancorado no NÓ, nunca no cursor: no touch o dedo (e no desktop o cursor)
            // ficava NA FRENTE do texto. Nasce à DIREITA do nó; sem espaço, vai pra
            // esquerda; clamp na tela nos dois eixos.
            var nodeRect = node.Rect;
            int pad = (int)(12 * _scale);
            var box = new Rectangle(0, 0, (int)maxW + pad * 2, (int)totH + pad * 2 - (int)lineGap);
            box.X = nodeRect.Right + (int)(10 * _scale);
            box.Y = nodeRect.Y - (int)(6 * _scale);
            if (box.Right > UiScaler.VirtualSize.X)
                box.X = nodeRect.X - box.Width - (int)(10 * _scale);
            box.X = Math.Clamp(box.X, 0, Math.Max(0, UiScaler.VirtualSize.X - box.Width));
            box.Y = Math.Clamp(box.Y, 0, Math.Max(0, UiScaler.VirtualSize.Y - box.Height));

            // Fundo OPACO (o painel escuro atrás deixava o texto ilegível) + borda dupla.
            sb.Draw(pixel, new Rectangle(box.X + 3, box.Y + 3, box.Width, box.Height), Color.Black * 0.55f); // sombra
            sb.Draw(pixel, box, ModernHudTheme.BgDarkest);
            DrawBorder(sb, pixel, box, ModernHudTheme.BorderOuter);
            var inner = new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
            DrawBorder(sb, pixel, inner, ModernHudTheme.Accent);

            float y = box.Y + pad;
            for (int i = 0; i < lines.Count; i++)
            {
                float sc0 = i == 0 ? scTitle : scBody;
                var (t, c) = lines[i];
                var s = font.MeasureString(t) * sc0;
                var pos = new Vector2(box.X + pad, y);
                sb.DrawString(font, t, pos + Vector2.One, Color.Black, 0f, Vector2.Zero, sc0, SpriteEffects.None, 0f);
                sb.DrawString(font, t, pos, c, 0f, Vector2.Zero, sc0, SpriteEffects.None, 0f);
                y += s.Y + lineGap;
            }
        }

        private static Rectangle ShrinkToSquare(Rectangle r, float f)
        {
            int side = (int)(Math.Min(r.Width, r.Height) * f);
            return new Rectangle(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2, side, side);
        }

        private static void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle r, Color c)
        {
            sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
            sb.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
        }
    }
}
