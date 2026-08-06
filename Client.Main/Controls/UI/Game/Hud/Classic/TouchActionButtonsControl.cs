using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

// Action buttons used by the Classic mobile HUD.
namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Touch action cluster in the lower-right corner, based on the MU Immortal layout:
    /// one large attack button and up to seven skill buttons on an arc. Artwork comes from
    /// MU Immortal (atlas_main: Attack, skill_frame, img_guang).
    /// Arc positions come from the main_mainskillui prefab anchored bottom-right.
    /// </summary>
    public sealed class TouchActionButtonsControl : UIControl
    {
        private const string AttackTexPath = "Interface/DH/mi_attack.OZP";
        private const string SkillFrameTexPath = "Interface/DH/mi_skill_frame.OZP";
        private const string SkillGlowTexPath = "Interface/DH/mi_skill_glow.OZP";
        // Skill Settings socket stack: silver ring from bg_allShow, ty_bg_skillBg disc,
        // and img_guang glow. Proportions use the 75px settings icon.
        private const string SocketRingTexPath = "Interface/SkillWin/socket_ring.OZP";
        private const string HoleBaseTexPath = "Interface/SkillWin/hole_base.OZP";
        private const string HoleGlowTexPath = "Interface/SkillWin/hole_glow.OZP";
        private const string AttackRingTexPath = "Interface/SkillWin/attack_ring.OZP";
        private const string AttackIconTexPath = "Interface/SkillWin/attack_icon.OZP";

        // Keep all seven skill slots available and use a separate circular target for reset.
        private const int ResetSize = 56;
        private static readonly Vector2 ResetCenter = new(-320f, 58f);

        // ════════════════════════════════════════════════════════════════════════
        // Exact positions from the main_mainskillui.u3d prefab. They are derived from each
        // button's RectTransform and should not be changed without updating the source data.
        // The root is 350x350 at the lower-right anchor; BtnCommon is 142x132 at Zone(71,-44).
        // The global scale fits the larger prefab into our virtual UI.
        // ════════════════════════════════════════════════════════════════════════
        private const float PrefabScale = 0.80f;

        // Prefab sizes (sizeDelta) multiplied by Scale.
        private static readonly int AttackSize = (int)(142f * PrefabScale);  // BtnCommon 142x132
        private static readonly int SkillSize = (int)(77f * PrefabScale);    // BtnSkill 77x78

        // Without the full-width footer plate, the arc can be lowered for the reference mock.
        private const int BottomHudLift = 0;

        private const int SkillCount = 7;
        // Exact prefab anchored positions relative to the lower-right corner.
        private static readonly Vector2[] SkillArcRaw =
        {
            new(-46.0f,   278.0f),  // BtnSkill1
            new(-140.0f,  244.8f),  // BtnSkill2
            new(-213.8f,  174.8f),  // BtnSkill3
            new(-260.0f,  81.4f),   // BtnSkill4
            new(-134.9f,  343.6f),  // BtnSkill5
            new(-236.2f,  272.0f),  // BtnSkill6
            new(-307.0f,  170.9f),  // BtnSkill7
        };
        private static readonly Vector2[] SkillArc = new Vector2[SkillCount];
        // BtnCommon center relative to the lower-right corner.
        private static readonly Vector2 AttackCenter = new(-104.0f * PrefabScale, 131.0f * PrefabScale);

        private static void BuildFan()
        {
            for (int i = 0; i < SkillCount; i++)
                SkillArc[i] = SkillArcRaw[i] * PrefabScale;
        }

        private Texture2D _texAttack;
        private Texture2D _texSkillFrame;
        private Texture2D _texSkillGlow;
        private Texture2D _texSocketRing;
        private Texture2D _texHoleBase;
        private Texture2D _texHoleGlow;
        private Texture2D _texAttackRing;
        private Texture2D _texAttackIcon;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);

        private Rectangle _attackRect;
        private Rectangle _resetRect;
        private readonly Rectangle[] _skillRects = new Rectangle[SkillArc.Length];
        private bool _attackPressed;
        private int _pressedSkill = -1;

        /// <summary>
        /// Alpha global do cluster (0..1), animado pelo TouchMenuControl pra fazer o
        /// crossfade suave quando o menu/painel abre. Abaixo de ~0.95 o input é ignorado
        /// (barra em fade não deve capturar toques).
        /// </summary>
        public float MasterAlpha { get; set; } = 1f;

        public TouchActionButtonsControl()
        {
            Interactive = true;
            AutoViewSize = false;
            // Cobre o canto inferior direito (área toda do cluster).
            int w = 360;
            int h = 380;
            ControlSize = new Point(w, h);
            ViewSize = new Point(w, h);
            X = UiScaler.VirtualSize.X - w;
            Y = UiScaler.VirtualSize.Y - h;
        }

        public override async Task Load()
        {
            await base.Load();
            if (_loadedTheme == UiThemeManager.CurrentId)
                return;
            try { _texAttack = await UiThemeManager.LoadThemeTextureAsync(AttackTexPath); } catch { _texAttack = null; }
            try { _texSkillFrame = await UiThemeManager.LoadThemeTextureAsync(SkillFrameTexPath); } catch { _texSkillFrame = null; }
            try { _texSkillGlow = await UiThemeManager.LoadThemeTextureAsync(SkillGlowTexPath); } catch { _texSkillGlow = null; }
            try { _texSocketRing = await UiThemeManager.LoadThemeTextureAsync(SocketRingTexPath); } catch { _texSocketRing = null; }
            try { _texHoleBase = await UiThemeManager.LoadThemeTextureAsync(HoleBaseTexPath); } catch { _texHoleBase = null; }
            try { _texHoleGlow = await UiThemeManager.LoadThemeTextureAsync(HoleGlowTexPath); } catch { _texHoleGlow = null; }
            try { _texAttackRing = await UiThemeManager.LoadThemeTextureAsync(AttackRingTexPath); } catch { _texAttackRing = null; }
            try { _texAttackIcon = await UiThemeManager.LoadThemeTextureAsync(AttackIconTexPath); } catch { _texAttackIcon = null; }
            _loadedTheme = UiThemeManager.CurrentId;
        }

        private void LayoutButtons()
        {
            // Canto inferior direito da tela em coords de UI virtual, levantado acima do HUD.
            float cornerX = UiScaler.VirtualSize.X;
            float cornerY = UiScaler.VirtualSize.Y - BottomHudLift;

            var ac = new Vector2(cornerX + AttackCenter.X, cornerY - AttackCenter.Y);
            _attackRect = CenteredRect(ac, AttackSize / 2f);

            var resetCenter = new Vector2(cornerX + ResetCenter.X, cornerY - ResetCenter.Y);
            _resetRect = CenteredRect(resetCenter, ResetSize / 2f);

            BuildFan();
            for (int i = 0; i < SkillArc.Length; i++)
            {
                var c = new Vector2(cornerX + SkillArc[i].X, cornerY - SkillArc[i].Y);
                _skillRects[i] = CenteredRect(c, SkillSize / 2f);
            }
        }

        private IReadOnlyList<SkillEntryState> Skills =>
            (Scene as GameScene)?.ModernHud?.HotbarSkills ?? System.Array.Empty<SkillEntryState>();

        // Quantas posições do arco exibir: 4 (Set 1) ou 7 (Set 2), conforme o set ativo
        // da tela Skill Imprint. As posições 0..3 formam a fileira interna coesa (Set 1);
        // 4..6 são a fileira externa que só aparece no Set 2.
        private int VisibleCount =>
            System.Math.Min(SkillArc.Length, (Scene as GameScene)?.ModernHud?.VisibleSkillCount ?? SkillCount);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible || Scene is not GameScene gs)
                return;

            LayoutButtons();

            // Em fade (menu/painel abertos) o cluster não captura toques.
            if (MasterAlpha < 0.95f)
            {
                _attackPressed = false;
                _pressedSkill = -1;
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            bool down = mouse.LeftButton == ButtonState.Pressed;
            bool justPressed = down && prev.LeftButton == ButtonState.Released;
            var p = new Point(mouse.Position.X, mouse.Position.Y);

            _attackPressed = down && _attackRect.Contains(p);
            _pressedSkill = -1;

            var skills = Skills;
            // Skills têm prioridade de toque sobre o ataque (ficam por cima no arco).
            int hitSkill = -1;
            int visible = VisibleCount;
            for (int i = 0; i < _skillRects.Length && i < visible; i++)
            {
                if (_skillRects[i].Contains(p)) { hitSkill = i; break; }
            }
            if (down && hitSkill >= 0) _pressedSkill = hitSkill;

            if (justPressed && hitSkill >= 0)
            {
                Scene.SetMouseInputConsumed();
                SkillEntryState skill = hitSkill < skills.Count ? skills[hitSkill] : null;
                if (skill != null)
                    CastSkill(gs, skill);
                else
                    gs.ModernHud?.BeginSkillAssignment(hitSkill);
            }
            else if (justPressed && _resetRect.Contains(p))
            {
                Scene.SetMouseInputConsumed();
                gs.ModernHud?.ResetActiveSkillSet();
            }
            else if (justPressed && _attackRect.Contains(p))
            {
                Scene.SetMouseInputConsumed();
                DoAttack(gs);
            }
            else if (down && (_attackRect.Contains(p) || _resetRect.Contains(p) || hitSkill >= 0))
            {
                Scene.SetMouseInputConsumed();
            }
        }

        private void DoAttack(GameScene gs)
        {
            var target = FindNearestMonster(gs);
            if (target != null)
                gs.Hero.Attack(target);
        }

        private void CastSkill(GameScene gs, SkillEntryState skill)
        {
            var target = FindNearestMonster(gs);
            gs.SkillController?.CastSkillFromHotbar(skill, target);
        }

        private static MonsterObject FindNearestMonster(GameScene gs)
        {
            var world = gs.World;
            var hero = gs.Hero;
            if (world?.Monsters == null || hero == null)
                return null;
            MonsterObject nearest = null;
            float best = float.MaxValue;
            var hloc = hero.Location;
            var monsters = world.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m == null || m.IsDead || m.World != world) continue;
                float d = Vector2.DistanceSquared(hloc, m.Location);
                if (d < best) { best = d; nearest = m; }
            }
            return nearest;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Scene is not GameScene gs) return;
            var sb = GraphicsManager.Instance?.Sprite;
            if (sb == null) return;

            LayoutButtons();
            var skills = Skills;

            var pixel = GraphicsManager.Instance?.Pixel;

            // Botões de skill: disco escuro translúcido (fundo do slot, como na referência)
            // + o aro mi_skill_frame por cima. Ícone só quando há skill no slot da hotbar.
            if (MasterAlpha <= 0.01f)
            {
                base.Draw(gameTime);
                return;
            }

            // Escopo LinearClamp próprio: o batch da cena usa PointClamp e o reescalonamento
            // dos sprites do soquete/aro sai serrilhado ("falhado" — feedback do usuário);
            // bilinear = o acabamento liso do mobile. Mesmo padrão do SkillPanelControl.
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, UiScaler.SpriteTransform);

            int visibleDraw = VisibleCount;
            for (int i = 0; i < _skillRects.Length && i < visibleDraw; i++)
            {
                var skill = (i < skills.Count) ? skills[i] : null;
                float a = ((_pressedSkill == i) ? 0.7f : 1f) * MasterAlpha;
                var rect = (_pressedSkill == i) ? Shrink(_skillRects[i], 3) : _skillRects[i];
                // Pilha de soquete da tela Skill Settings (efeito aprovado): aro prateado
                // embaixo, disco escuro (vazio) OU ícone (cheio) cobrindo a borda interna
                // do aro, glow quente por cima. Proporções do settings (ícone 75):
                // aro 88/75, base 75/75, glow 70/75.
                const float InnerScale = 0.855f; // base do slot VAZIO um pouco menor (borda fina)
                var inner = ScaledRect(rect, InnerScale);

                // ── EFEITO DE BORDA (ideia do usuário) ──────────────────────────────────
                // Slot COM skill: a skill preenche 100% do círculo (rect cheio) e o glow vem
                // por cima 15% MENOR (0.85). Assim a skill vai até a borda e o glow forma o
                // anel interno = a "borda que faz parte da skill".
                // Slot VAZIO: aro escuro (socket_ring) + base translúcida (inner).
                bool drewIcon = skill != null && DrawSkillIcon(sb, rect, skill, a);
                if (drewIcon)
                {
                    if (Tex(_texHoleGlow))
                        sb.Draw(_texHoleGlow, ScaledRect(rect, 0.90f), Color.White * a);
                }
                else
                {
                    if (Tex(_texSocketRing))
                        sb.Draw(_texSocketRing, ScaledRect(rect, 88f / 75f), Color.White * a);
                    if (Tex(_texHoleBase))
                        sb.Draw(_texHoleBase, inner, Color.White * a);
                }

                if (_texSocketRing == null && Tex(_texSkillFrame))
                    sb.Draw(_texSkillFrame, rect, Color.White * (skill != null ? a : a * 0.9f));
            }

            // Dedicated circular reset target for touch users. It clears the active set,
            // not learned skills or server-side character data.
            {
                float a = MasterAlpha;
                var rect = _resetRect;
                if (Tex(_texSocketRing))
                    sb.Draw(_texSocketRing, ScaledRect(rect, 88f / 75f), Color.White * a);
                if (Tex(_texHoleBase))
                    sb.Draw(_texHoleBase, ScaledRect(rect, 0.855f), Color.White * a);

                var font = GraphicsManager.Instance?.Font;
                if (font != null)
                {
                    const float textScale = 0.56f;
                    const string text = "R";
                    var size = font.MeasureString(text) * textScale;
                    var pos = new Vector2(
                        rect.X + (rect.Width - size.X) / 2f,
                        rect.Y + (rect.Height - size.Y) / 2f);
                    sb.DrawString(font, text, pos + Vector2.One, Color.Black * 0.8f * a,
                        0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                    sb.DrawString(font, text, pos, new Color(235, 200, 120) * a,
                        0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                }
            }

            // Botão de ataque (o maior): disco escuro + aro Attack.png do mobile (o mesmo
            // da tela Skill Settings) emoldurando o emblema.
            {
                float a = (_attackPressed ? 0.7f : 1f) * MasterAlpha;
                var rect = _attackPressed ? Shrink(_attackRect, 4) : _attackRect;
                // SEM DrawDisc embaixo: o scanline tem borda dura (escada) e vaza além do
                // aro — o interior escuro já vem ASSADO no Attack.png, como no settings.
                if (pixel != null && !Tex(_texAttackRing))
                    DrawDisc(sb, pixel, Mid(rect), rect.Width / 2f - 2f, new Color(22, 20, 18) * (0.80f * a));
                if (Tex(_texAttackRing))
                {
                    // Attack.png é 142x133 (não quadrado) e tem interior escuro assado:
                    // desenha ANTES do emblema (ordem do settings: ring → icon).
                    var mid = Mid(rect);
                    int rw = rect.Width;
                    int rh = (int)MathF.Round(rw * 133f / 142f);
                    sb.Draw(_texAttackRing, new Rectangle((int)(mid.X - rw / 2f), (int)(mid.Y - rh / 2f), rw, rh), Color.White * a);
                }
                // Emblema btn_AttackChange (84x94 no aro 142x133, o mesmo do settings):
                // o mi_attack do DH é escuro e some sobre o interior assado do Attack.png.
                if (Tex(_texAttackIcon))
                {
                    var mid2 = Mid(rect);
                    // Espada reduzida 15% (estava grande demais): fator 0.85 sobre a
                    // proporção original 84x94 do emblema.
                    const float IconScale = 0.85f;
                    int iw = (int)MathF.Round(rect.Width * 84f / 142f * IconScale);
                    int ih = (int)MathF.Round(rect.Height * 94f / 133f * IconScale);
                    sb.Draw(_texAttackIcon, new Rectangle((int)(mid2.X - iw / 2f), (int)(mid2.Y - ih / 2f), iw, ih), Color.White * a);
                }
                else if (_texAttack != null && !_texAttack.IsDisposed)
                    sb.Draw(_texAttack, rect, Color.White * a);
            }

            // Restaura o batch padrão da cena (PointClamp) pros próximos controles.
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, UiScaler.SpriteTransform);

            base.Draw(gameTime);
        }

        private bool DrawSkillIcon(SpriteBatch sb, Rectangle frameRect, SkillEntryState skill, float alpha)
        {
            // Ícone pré-cortado (detecta célula VAZIA no atlas e devolve null — sem isso
            // o scanline "desenha" texels transparentes e o slot fica vazado no chão).
            var cut = global::Client.Main.Controls.UI.Game.Skills.SkillIconRenderer.GetCircleIconTexture(skill.SkillId);
            if (cut != null)
            {
                sb.Draw(cut, frameRect, Color.White * alpha);
                return true;
            }
            return false;
        }

        private static Vector2 Mid(Rectangle r) => new(r.X + r.Width / 2f, r.Y + r.Height / 2f);

        // Disco cheio desenhado com o pixel branco (varredura por linha).
        private static void DrawDisc(SpriteBatch sb, Texture2D pixel, Vector2 c, float r, Color color)
        {
            int ir = (int)r;
            for (int y = -ir; y <= ir; y++)
            {
                int half = (int)MathF.Sqrt(MathF.Max(0f, r * r - y * y));
                sb.Draw(pixel, new Rectangle((int)(c.X - half), (int)(c.Y + y), half * 2, 1), color);
            }
        }

        private static bool Tex(Texture2D t) => t != null && !t.IsDisposed;

        // Retângulo concêntrico ao dado, escalado pelo fator (pilha de soquete).
        // Arredonda as BORDAS (não posição+largura separadas): assim o centro é
        // preservado EXATAMENTE para qualquer fator, e aro + base ficam 100%
        // concêntricos (antes, Round(pos) e Round(w) independentes deslocavam o
        // centro em sub-pixel → a skill parecia ~99% centralizada no aro).
        private static Rectangle ScaledRect(Rectangle r, float factor)
        {
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float halfW = r.Width * factor / 2f;
            float halfH = r.Height * factor / 2f;
            int left = (int)MathF.Round(cx - halfW);
            int top = (int)MathF.Round(cy - halfH);
            int right = (int)MathF.Round(cx + halfW);
            int bottom = (int)MathF.Round(cy + halfH);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static Rectangle CenteredRect(Vector2 c, float r)
            => new((int)(c.X - r), (int)(c.Y - r), (int)(r * 2f), (int)(r * 2f));
        private static Rectangle Shrink(Rectangle r, int by)
            => new(r.X + by, r.Y + by, r.Width - by * 2, r.Height - by * 2);
        private static Rectangle Expand(Rectangle r, int by)
            => new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);
    }
}
