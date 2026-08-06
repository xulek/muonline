using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

// Virtual joystick used by the Classic mobile HUD.
namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Virtual movement joystick in the lower-left corner, matching the MU mobile style.
    /// While dragged, it moves the character continuously in the stick direction.
    /// Behavior reference: Main_JoyStickUI.lua from MU Dragon Havoc
    /// (drag -> direction -> targetCell = cell + direction -> MoveTo; release -> stop).
    /// </summary>
    public sealed class VirtualJoystickControl : UIControl
    {
        // Base and knob radii, based on the MU Immortal proportions (174 and 78).
        private const float BaseRadius = 87f;
        private const float KnobRadius = 33.345f; // Reduced from the original 39px knob.
        // Dead zone: below this distance, movement is not emitted.
        private const float DeadZone = 0.28f;

        // Joystick artwork extracted from MU Immortal (atlas_main: stick_bg/stick_main).
        private const string StickBgPath = "Interface/DH/mi_stick_bg.OZP";
        private const string StickKnobPath = "Interface/DH/mi_stick_knob.OZP";
        private Texture2D _texBase;
        private Texture2D _texKnob;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);

        private bool _active;
        private Vector2 _center;        // Fixed base center in virtual UI coordinates.
        private Vector2 _knob;          // Current knob position in virtual UI coordinates.

        public VirtualJoystickControl()
        {
            Interactive = true;
            AutoViewSize = false;
            // Anchor the base to the lower-left corner of the virtual space.
            int marginX = 40;
            int marginBottom = 56;   // Raise the stick slightly above the bottom bar.
            int size = (int)(BaseRadius * 2f);
            ControlSize = new Point(size, size);
            ViewSize = new Point(size, size);
            X = marginX;
            Y = UiScaler.VirtualSize.Y - size - marginBottom;
        }

        public override async Task Load()
        {
            await base.Load();
            if (_loadedTheme == UiThemeManager.CurrentId)
                return;
            try { _texBase = await UiThemeManager.LoadThemeTextureAsync(StickBgPath); } catch { _texBase = null; }
            try { _texKnob = await UiThemeManager.LoadThemeTextureAsync(StickKnobPath); } catch { _texKnob = null; }
            _loadedTheme = UiThemeManager.CurrentId;
        }

        private Vector2 Center =>
            new(DisplayRectangle.X + DisplayRectangle.Width / 2f,
                DisplayRectangle.Y + DisplayRectangle.Height / 2f);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible || Scene == null)
                return;

            var hero = (Scene as GameScene)?.Hero;
            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            bool down = mouse.LeftButton == ButtonState.Pressed;
            bool prevDown = prev.LeftButton == ButtonState.Pressed;
            var p = new Vector2(mouse.Position.X, mouse.Position.Y);

            _center = Center;

            // Começa a arrastar se o toque iniciou dentro da base.
            if (!_active && down && !prevDown)
            {
                float startR = BaseRadius * 1.35f; // área de captura um pouco maior
                if (Vector2.Distance(p, _center) <= startR)
                {
                    _active = true;
                }
            }

            if (_active)
            {
                // Enquanto ativo, consome o ponteiro para o clique-no-mundo não disparar.
                Scene.SetMouseInputConsumed();

                if (!down)
                {
                    // Soltou: recentra o manípulo. NÃO chama StopMovement (deixa o char
                    // terminar o passo atual suavemente — evita o "pulo" ao soltar).
                    _active = false;
                    _knob = Vector2.Zero;
                    _lastDx = _lastDy = 0;   // zera a direção pra o próximo drag reemitir
                    return;
                }

                // Vetor do analógico (tela, y para baixo), clampeado ao raio da base.
                Vector2 v = p - _center;
                float dist = v.Length();
                float clamped = MathHelper.Clamp(dist, 0f, BaseRadius);
                Vector2 dir = dist > 0.0001f ? v / dist : Vector2.Zero;
                _knob = dir * clamped;

                float norm = clamped / BaseRadius; // 0..1
                if (norm < DeadZone || hero == null)
                {
                    // Zona morta: não move (mantém o manípulo desenhado).
                    return;
                }

                DriveMovement(hero, dir);
            }
        }

        // Quantos tiles à frente projetar o alvo na direção do analógico. Um alvo LONGE faz
        // o MoveTo montar um caminho de vários tiles, então o char anda CONTÍNUO (IsMoving
        // fica true entre tiles) — sem o flick de idle que reiniciava a animação, e permitindo
        // entrar em corrida. Antes era 1 tile por vez, esperando parar → travava a animação.
        private const int ProjectTiles = 8;
        private int _lastDx, _lastDy;      // última direção de tile emitida

        private void DriveMovement(HeroObject hero, Vector2 screenDir)
        {
            // Mapeia direção de TELA -> delta de TILE (iso do MU).
            //   tela-cima (0,-1) -> tile (-1,+1); direita (1,0) -> (+1,+1);
            //   baixo (0,+1) -> (+1,-1); esquerda (-1,0) -> (-1,-1).  (sy p/ baixo positivo)
            float sx = screenDir.X;
            float sy = screenDir.Y;
            Vector2 raw = new(sx + sy, sx - sy);
            if (raw.LengthSquared() < 0.0001f)
                return;

            int dx = Math.Sign(MathF.Round(raw.X * 1.2f));
            int dy = Math.Sign(MathF.Round(raw.Y * 1.2f));
            if (dx == 0 && dy == 0)
                return;

            // Reemite quando a DIREÇÃO muda OU quando a fila do caminho está ACABANDO (≤3
            // tiles restantes). Reemitir ANTES de esvaziar mantém isAboutToMove SEMPRE true —
            // sem isso, o path zerava por 1 frame, _runFrames resetava e a corrida caía pra
            // andar a cada ~6 passos (corre/anda/corre). Alimentar à frente = corrida contínua.
            bool dirChanged = dx != _lastDx || dy != _lastDy;
            // The current WalkerObject API does not expose the internal path queue.
            // Re-emit only when the current movement step has completed; direction
            // changes still re-target immediately.
            bool pathLow = !hero.IsMoving;
            if (!dirChanged && !pathLow)
                return;

            var loc = hero.Location;

            // COLISÃO: o world walkable vem da Scene (o mesmo que o clique-mouse usa em
            // WalkableWorldControl). hero.World nem sempre é o WalkableWorldControl, por
            // isso a versão anterior não bloqueava e o char atravessava tudo.
            var world = Scene?.World as WalkableWorldControl;

            // Projeta na direção do analógico parando no PRIMEIRO tile bloqueado, então o
            // alvo é o último tile caminhável — o char anda contínuo até a parede e para.
            int steps = 0;
            int lastX = (int)loc.X, lastY = (int)loc.Y;
            for (int i = 1; i <= ProjectTiles; i++)
            {
                int nx = (int)loc.X + dx * i;
                int ny = (int)loc.Y + dy * i;
                if (nx < 0 || ny < 0 || nx >= Constants.TERRAIN_SIZE || ny >= Constants.TERRAIN_SIZE)
                    break;
                if (world != null && !world.IsWalkable(new Vector2(nx, ny)))
                    break;
                lastX = nx; lastY = ny; steps++;
            }
            if (steps == 0)
                return; // encostado na parede nessa direção: não emite movimento

            var target = new Vector2(lastX, lastY);
            if (target == loc)
                return;

            _lastDx = dx;
            _lastDy = dy;
            // usePathfinding:true = MESMO A* do clique-mouse. Garante colisão até em
            // diagonais que passam por cantos bloqueados (BuildDirectPath cortava reto e
            // atravessava). O alvo multi-tile mantém movimento contínuo / corrida.
            hero.MoveTo(target, sendToServer: true, usePathfinding: true);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;

            var pixel = GraphicsManager.Instance?.Pixel;
            var sb = GraphicsManager.Instance?.Sprite;
            if (pixel == null || sb == null) return;

            _center = Center;
            Vector2 knobPos = _center + (_active ? _knob : Vector2.Zero);

            float alpha = _active ? 1.0f : 0.7f;

            // Arte real do Dragon Havoc, se carregada; senão desenho procedural.
            if (_texBase != null && !_texBase.IsDisposed && _texKnob != null && !_texKnob.IsDisposed)
            {
                var baseRect = CenteredRect(_center, BaseRadius);
                var knobRect = CenteredRect(knobPos, KnobRadius);
                sb.Draw(_texBase, baseRect, Color.White * alpha);
                sb.Draw(_texKnob, knobRect, Color.White * alpha);
            }
            else
            {
                // Fallback procedural (sprite ausente).
                DrawRing(sb, pixel, _center, BaseRadius, 3f, new Color(230, 230, 235) * (alpha * 0.9f));
                DrawDisc(sb, pixel, _center, BaseRadius - 3f, new Color(20, 24, 30) * (alpha * 0.5f));
                DrawTick(sb, pixel, _center, BaseRadius);
                DrawDisc(sb, pixel, knobPos, KnobRadius, new Color(60, 70, 85) * alpha);
                DrawRing(sb, pixel, knobPos, KnobRadius, 3f, new Color(235, 200, 120) * alpha);
                DrawDisc(sb, pixel, knobPos, KnobRadius * 0.5f, new Color(235, 200, 120) * (alpha * 0.6f));
            }

            base.Draw(gameTime);
        }

        private static Rectangle CenteredRect(Vector2 center, float radius)
            => new((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));

        // ── Helpers de desenho (círculos com o pixel branco) ─────────────
        private static void DrawDisc(SpriteBatch sb, Texture2D pixel, Vector2 c, float r, Color color)
        {
            int ir = (int)r;
            for (int y = -ir; y <= ir; y++)
            {
                int half = (int)MathF.Sqrt(r * r - y * y);
                sb.Draw(pixel, new Rectangle((int)(c.X - half), (int)(c.Y + y), half * 2, 1), color);
            }
        }

        private static void DrawRing(SpriteBatch sb, Texture2D pixel, Vector2 c, float r, float thickness, Color color)
        {
            float rIn = r - thickness;
            int ir = (int)r;
            for (int y = -ir; y <= ir; y++)
            {
                float yy = y;
                float outer = r * r - yy * yy;
                if (outer < 0) continue;
                int xo = (int)MathF.Sqrt(outer);
                float innerSq = rIn * rIn - yy * yy;
                if (innerSq <= 0)
                {
                    sb.Draw(pixel, new Rectangle((int)(c.X - xo), (int)(c.Y + y), xo * 2, 1), color);
                }
                else
                {
                    int xi = (int)MathF.Sqrt(innerSq);
                    sb.Draw(pixel, new Rectangle((int)(c.X - xo), (int)(c.Y + y), xo - xi, 1), color);
                    sb.Draw(pixel, new Rectangle((int)(c.X + xi), (int)(c.Y + y), xo - xi, 1), color);
                }
            }
        }

        private static void DrawTick(SpriteBatch sb, Texture2D pixel, Vector2 c, float r)
        {
            var col = new Color(200, 200, 210) * 0.25f;
            int len = 10;
            // topo, base, esq, dir
            sb.Draw(pixel, new Rectangle((int)c.X - 1, (int)(c.Y - r + 4), 2, len), col);
            sb.Draw(pixel, new Rectangle((int)c.X - 1, (int)(c.Y + r - 4 - len), 2, len), col);
            sb.Draw(pixel, new Rectangle((int)(c.X - r + 4), (int)c.Y - 1, len, 2), col);
            sb.Draw(pixel, new Rectangle((int)(c.X + r - 4 - len), (int)c.Y - 1, len, 2), col);
        }
    }
}
