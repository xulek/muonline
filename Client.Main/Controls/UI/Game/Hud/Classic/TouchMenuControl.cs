using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Menu button in the lower-right corner with a two-column fold-out containing the
    /// Forge, Guide, Allocate, Friend, Skill, Mail, and Settings icons. The Skill entry
    /// opens the skill panel; the remaining entries retain their existing behavior.
    /// </summary>
    public sealed class TouchMenuControl : UIControl
    {
        // Bronze icons in the upper-right corner. They are drawn without an outer frame:
        // notification and menu occupy the first row, with the bag in the second row.
        private const string NotifTexPath = "Interface/DH/mi_btn_notification.OZP";
        private const string GearTexPath = "Interface/DH/mi_btn_menu.OZP";
        private const string SqBagTexPath = "Interface/DH/mi_btn_bag.OZP";

        private sealed record MenuEntry(string Label, string TexPath, Action OnTap);

        // Layout in virtual UI space, anchored to the upper-right corner:
        // row 1: notification and menu; row 2: bag below the menu.
        private const int GearSize = 48;          // Size of every fixed and fold-out button.
        private const int BtnGap = 20;            // Gap between buttons to avoid misclicks.
        private const int MarginRight = 16;       // Right-edge margin.
        private const int MarginTop = 14;         // Top margin.
        // X centers: menu in the right column, notification to its left.
        private static int MenuBtnCX => UiScaler.VirtualSize.X - MarginRight - GearSize / 2;
        private static int NotifBtnCX => MenuBtnCX - GearSize - BtnGap;
        private const int Row1CY = MarginTop + GearSize / 2;                           // ~44
        private const int Row2CY = Row1CY + GearSize + BtnGap;                         // ~117
        // Menu and notification occupy row 1; bag occupies row 2 below the menu.
        private static int SideBtnX => MenuBtnCX; // Menu/bag column; the fold-out anchors here.
        private const int MenuBtnCY = Row1CY;     // Menu button center Y.
        private const int BagBtnCY = Row2CY;      // Bag button center Y.
        // Fold-out icons use the same size and center step as the fixed buttons so the grid
        // stays aligned and does not introduce gaps.
        private const int ItemSize = GearSize;               // All icons use the same 48px box.
        private const int ItemGapX = BtnGap;                // Horizontal gap.
        private const int RowStep = GearSize + BtnGap;      // Vertical row step.
        private const int LabelH = 22;

        // Position (column, row) of every fold-out entry; row 0 is the top.
        // Fixed buttons occupy notification (col0,row0), menu (col1,row0), and bag (col1,row1).
        // The fold-out starts at col0,row1 and fills the rows below in two columns.
        //   row0: notif  | menu        (fixos)
        //   row1: Forge  | bag         (bag é fixo; Forge preenche a col0)
        //   row2: Guide  | Allocate
        //   row3: Friend | Skill
        //   row4: Settings | Mail
        //   row5: Exit   | —
        // The order below matches the _entries array:
        // [0]Forge [1]Guide [2]Allocate [3]Friend [4]Skill [5]Mail [6]Potions [7]Settings [8]Exit
        //   row0: notif  | menu        (fixos)
        //   row1: Forge  | bag         (bag fixo)
        //   row2: Guide  | Allocate
        //   row3: Friend | Skill
        //   row4: Potions| Mail        (the potion tab replaces the gear slot)
        //   row5: Settings | Exit      (Settings moved down one row)
        private static readonly (int Col, int Row)[] GridMap =
        {
            (0, 1), // Forge, next to the bag.
            (0, 2), // Guide
            (1, 2), // Allocate
            (0, 3), // Friend
            (1, 3), // Skill
            (0, 4), // Mail.
            (1, 4), // Potions.
            (0, 5), // Settings
            (1, 5), // Exit
        };

        /// <summary>The Skill entry toggles the Skill Imprint screen. The bell has no action.</summary>
        public SkillImprintControl ImprintPanel { get; set; }

        /// <summary>The Potions entry toggles the five-slot Potion Imprint screen.</summary>
        public PotionImprintControl PotionPanel { get; set; }

        /// <summary>
        /// Touch HUD attack/skill cluster. It crossfades while the fold-out or skill panel
        /// is open so both layers do not compete for input.
        /// </summary>
        public TouchActionButtonsControl HotbarToHide { get; set; }

        private Texture2D _texGear;
        private Texture2D _texNotif;
        private Texture2D _texSqBag;
        private Rectangle _bagRect;
        private Rectangle _notifRect;
        private readonly MenuEntry[] _entries;
        private readonly Texture2D[] _entryTex;
        private readonly Rectangle[] _entryRects;
        private UiThemeId _loadedTheme = (UiThemeId)(-1);
        private Rectangle _gearRect;
        private bool _open;
        private float _gearSpin; // Small rotation while opening, matching the mobile UI.
        private float _foldAnim; // 0..1 fold-out fade/slide animation.

        public TouchMenuControl()
        {
            Interactive = true;
            AutoViewSize = false;
            _entries = new[]
            {
                new MenuEntry("Forge", "Interface/DH/mi_menu_forge.OZP", null),
                new MenuEntry("Guide", "Interface/DH/mi_menu_guide.OZP", null),
                new MenuEntry("Allocate", "Interface/DH/mi_menu_allocate.OZP", OpenMastery),
                new MenuEntry("Friend", "Interface/DH/mi_menu_friend.OZP", null),
                // The Skill book opens the Skill Imprint screen.
                new MenuEntry("Skill", "Interface/DH/mi_menu_skill.OZP", OpenImprint),
                new MenuEntry("Mail", "Interface/DH/mi_menu_mail.OZP", null),
                // The potion tab occupies the former gear slot; Settings moves down one row.
                new MenuEntry("Potions", "Interface/DH/mi_menu_potion_tab.OZP", OpenPotionImprint),
                new MenuEntry("Settings", "Interface/DH/mi_menu_settings.OZP", OpenExitMenu),
                new MenuEntry("Exit", "Interface/DH/mi_menu_exit.OZP", ConfirmAndQuit),
            };
            _entryTex = new Texture2D[_entries.Length];
            _entryRects = new Rectangle[_entries.Length];

            // Keep the bounds restricted to the area actually used. A full-screen interactive
            // control captures the pointer and breaks world point-to-click. This must run
            // after _entries and _entryRects are initialized because LayoutRects iterates both.
            UpdateBounds();
        }

        public void Close()
        {
            _open = false;
            UpdateBounds();
        }

        // "Skill" toggles the Skill Imprint screen.
        private void OpenImprint()
        {
            _open = false;
            UpdateBounds();
            ImprintPanel?.Toggle();
            if (ImprintPanel != null && ImprintPanel.Visible)
                ImprintPanel.BringToFront();
        }

        // "Potions" toggles the Potion Imprint screen.
        private void OpenPotionImprint()
        {
            _open = false;
            UpdateBounds();
            PotionPanel?.Toggle();
            if (PotionPanel != null && PotionPanel.Visible)
                PotionPanel.BringToFront();
        }

        // The Allocate icon opens the Mastery skill tree.
        private void OpenMastery()
        {
            _open = false;
            UpdateBounds();
            var scene = Scene as GameScene;
            if (scene == null)
                return;

            if (UiThemeManager.CurrentId == UiThemeId.Classic)
            {
                var tree = scene.ClassicMasteryTree;
                if (tree == null)
                    return;

                tree.Toggle();
                if (tree.Visible)
                {
                    tree.BringToFront();
                    Scene.FocusControl = tree;
                }
            }
            else
            {
                var tree = scene.MasteryTree;
                if (tree == null)
                    return;

                tree.Visible = !tree.Visible;
                if (tree.Visible)
                {
                    tree.BringToFront();
                    Scene.FocusControl = tree;
                }
            }
        }

        // The Settings icon opens the pause/logout menu, matching the Escape key.
        private void OpenExitMenu()
        {
            _open = false;
            UpdateBounds();
            var pauseMenu = (Scene as GameScene)?.PauseMenu;
            if (pauseMenu == null)
                return;
            pauseMenu.Visible = true;
            pauseMenu.BringToFront();
            if (Scene != null)
                Scene.FocusControl = pauseMenu;
        }

        // Exit opens the standard confirmation dialog and closes the game when confirmed.
        private void ConfirmAndQuit()
        {
            _open = false;
            UpdateBounds();
            RequestDialog.Show("Do you want to exit the game?", () =>
            {
#if !IOS
                MuGame.ScheduleOnMainThread(() => MuGame.Instance.Exit());
#endif
            });
        }

        public override async Task Load()
        {
            await base.Load();
            if (_loadedTheme == UiThemeManager.CurrentId)
                return;
            async Task<Texture2D> L(string p) { try { return await UiThemeManager.LoadThemeTextureAsync(p); } catch { return null; } }
            _texGear = await L(GearTexPath);
            _texNotif = await L(NotifTexPath);
            _texSqBag = await L(SqBagTexPath);
            for (int i = 0; i < _entries.Length; i++)
                _entryTex[i] = await L(_entries[i].TexPath);
            _loadedTheme = UiThemeManager.CurrentId;
        }

        private void UpdateBounds()
        {
            LayoutRects();
            var bounds = Rectangle.Union(_gearRect, _bagRect);
            bounds = Rectangle.Union(bounds, _notifRect);
            if (_open)
            {
                for (int i = 0; i < _entryRects.Length; i++)
                {
                    var r = _entryRects[i];
                    r.Height += LabelH; // Include the label below the icon.
                    bounds = Rectangle.Union(bounds, r);
                }
            }
            // Keep a small safety margin so the top icon is not clipped by the control bounds.
            bounds = Rectangle.Union(bounds, new Rectangle(bounds.X - 12, bounds.Y - 12, bounds.Width + 24, bounds.Height + 24));
            X = bounds.X;
            Y = bounds.Y;
            ControlSize = new Point(bounds.Width, bounds.Height);
            ViewSize = ControlSize;
        }

        private void LayoutRects()
        {
            // Upper-right corner: notification and menu on row 1, bag on row 2.
            _notifRect = new Rectangle(NotifBtnCX - GearSize / 2, Row1CY - GearSize / 2, GearSize, GearSize);
            _gearRect = new Rectangle(MenuBtnCX - GearSize / 2, MenuBtnCY - GearSize / 2, GearSize, GearSize);
            _bagRect = new Rectangle(SideBtnX - GearSize / 2, BagBtnCY - GearSize / 2, GearSize, GearSize);

            // Grade 2 colunas alinhada aos botões fixos. Os CENTROS vêm de GearSize (mesma
            // coluna/linha dos fixos); o box desenhado é ItemSize (10% maior), centrado nesse
            // centro — assim os ícones ficam maiores SEM desalinhar da grade.
            int col1CX = SideBtnX;                              // centro X da coluna direita
            int col0CX = col1CX - GearSize - ItemGapX;          // centro X da coluna esquerda
            int topCY = MenuBtnCY;                              // centro Y da linha 0
            for (int i = 0; i < _entries.Length; i++)
            {
                var (col, row) = GridMap[i];
                int cx = (col == 0) ? col0CX : col1CX;
                int cy = topCY + row * RowStep;
                _entryRects[i] = new Rectangle(cx - ItemSize / 2, cy - ItemSize / 2, ItemSize, ItemSize);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible)
                return;

            UpdateBounds();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float k = MathHelper.Clamp(dt * 12f, 0f, 1f);

            // Transições suaves: fold-out (fade+slide) e crossfade da hotbar.
            float foldTarget = _open ? 1f : 0f;
            _foldAnim = MathHelper.Lerp(_foldAnim, foldTarget, k);
            if (MathF.Abs(_foldAnim - foldTarget) < 0.01f) _foldAnim = foldTarget;

            if (HotbarToHide != null)
            {
                bool hideBar = _open || ImprintPanel?.Visible == true || PotionPanel?.Visible == true;
                float aTarget = hideBar ? 0f : 1f;
                HotbarToHide.MasterAlpha = MathHelper.Lerp(HotbarToHide.MasterAlpha, aTarget, k);
                if (MathF.Abs(HotbarToHide.MasterAlpha - aTarget) < 0.01f)
                    HotbarToHide.MasterAlpha = aTarget;
            }

            float target = _open ? MathHelper.ToRadians(45f) : 0f;
            _gearSpin = MathHelper.Lerp(_gearSpin, target, k);

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            bool justPressed = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
            if (!justPressed)
                return;
            var p = new Point(mouse.Position.X, mouse.Position.Y);

            if (_notifRect.Contains(p))
            {
                // Notification no longer opens a panel; it only closes the fold-out.
                Scene?.SetMouseInputConsumed();
                _open = false;
                UpdateBounds();
                return;
            }

            if (_gearRect.Contains(p))
            {
                Scene?.SetMouseInputConsumed();
                _open = !_open;
                UpdateBounds();
                return;
            }

            if (_bagRect.Contains(p))
            {
                // The bag opens the same inventory used by the I key.
                Scene?.SetMouseInputConsumed();
                _open = false;
                UpdateBounds();
                var inv = Inventory.InventoryControl.Instance;
                if (inv != null)
                {
                    if (inv.Visible)
                    {
                        inv.Hide();
                    }
                    else
                    {
                        // Defer Show until the end of the frame. Show() changes z-order and
                        // focus; doing that during pointer dispatch can consume the same tap.
                        MuGame.ScheduleOnMainThread(() =>
                        {
                            inv.Show();
                            Controllers.SoundController.Instance.PlayBuffer("Sound/iCreateWindow.wav");
                        });
                    }
                }
                return;
            }

            if (!_open)
                return;

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entryRects[i].Contains(p))
                {
                    Scene?.SetMouseInputConsumed();
                    _entries[i].OnTap?.Invoke();
                    return;
                }
            }

            // A tap outside closes the fold-out without consuming the world click.
            _open = false;
            UpdateBounds();
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;
            var sb = GraphicsManager.Instance?.Sprite;
            var pixel = GraphicsManager.Instance?.Pixel;
            var font = GraphicsManager.Instance?.Font;
            if (sb == null || pixel == null)
                return;

            LayoutRects();

            // Escopo LinearClamp (mesmo padrão do arco/painel): o chrome oval reescalonado
            // no batch PointClamp da cena sai serrilhado.
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, Controllers.UiScaler.SpriteTransform);

            // Notification (sino): ícone bronze cru, sem aro.
            if (_texNotif != null && !_texNotif.IsDisposed)
                sb.Draw(_texNotif, _notifRect, Color.White);
            else
                DrawDisc(sb, pixel, new Vector2(_notifRect.X + GearSize / 2f, _notifRect.Y + GearSize / 2f), GearSize / 2f - 2, new Color(30, 26, 20));

            // Menu (grade 2x2): ícone bronze cru; gira um tico ao abrir o fold-out.
            if (_texGear != null && !_texGear.IsDisposed)
            {
                var c = new Vector2(_gearRect.X + _gearRect.Width / 2f, _gearRect.Y + _gearRect.Height / 2f);
                var origin = new Vector2(_texGear.Width / 2f, _texGear.Height / 2f);
                float scale = _gearRect.Width / (float)_texGear.Width;
                sb.Draw(_texGear, c, null, Color.White, _gearSpin, origin, scale, SpriteEffects.None, 0f);
            }
            else
            {
                DrawDisc(sb, pixel, new Vector2(_gearRect.X + GearSize / 2f, _gearRect.Y + GearSize / 2f), GearSize / 2f - 2, new Color(30, 26, 20));
            }

            // Bag (bolsa): ícone bronze cru; abre o inventário.
            if (_texSqBag != null && !_texSqBag.IsDisposed)
                sb.Draw(_texSqBag, _bagRect, Color.White);
            else
                DrawDisc(sb, pixel, new Vector2(_bagRect.X + GearSize / 2f, _bagRect.Y + GearSize / 2f), GearSize / 2f - 2, new Color(30, 26, 20));

            // Fold-out with a smooth fade-and-slide animation.
            if (_foldAnim > 0.02f)
            {
                float fa = _foldAnim;
                int rise = (int)((1f - fa) * 14f);
                for (int i = 0; i < _entries.Length; i++)
                {
                    var rect = _entryRects[i];
                    rect.Y += rise;
                    var tex = _entryTex[i];
                    // The potion tab has less transparent padding, so shrink it slightly to
                    // match the apparent size of the other icons.
                    if (_entries[i].Label == "Potions")
                    {
                        int shrink = (int)(rect.Width * 0.14f);
                        rect = new Rectangle(rect.X + shrink / 2, rect.Y + shrink / 2, rect.Width - shrink, rect.Height - shrink);
                    }
                    if (tex != null && !tex.IsDisposed)
                    {
                        sb.Draw(tex, rect, Color.White * fa);
                    }
                    else
                    {
                        DrawDisc(sb, pixel, new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f), rect.Width / 2f - 1, new Color(24, 22, 18) * (0.95f * fa));
                    }

                    // Labels are intentionally omitted because the mobile icons are explicit.
                }
            }

            // Restaura o batch padrão da cena (PointClamp).
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Controllers.UiScaler.SpriteTransform);

            base.Draw(gameTime);
        }

        private static void DrawDisc(SpriteBatch sb, Texture2D pixel, Vector2 c, float r, Color color)
        {
            int ir = (int)r;
            for (int y = -ir; y <= ir; y++)
            {
                float t = 1f - (y * y) / (r * r);
                if (t <= 0f) continue;
                float hw = r * MathF.Sqrt(t);
                sb.Draw(pixel, new Rectangle((int)(c.X - hw), (int)(c.Y + y), (int)(hw * 2), 1), color);
            }
        }

        private static void DrawOutlined(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, float scale, Color color, int thickness)
        {
            // Contorno acompanha o alpha do texto (senão o outline "fantasma" fica na frente no fade).
            var outline = Color.Black * (color.A / 255f);
            for (int dx = -thickness; dx <= thickness; dx++)
                for (int dy = -thickness; dy <= thickness; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    sb.DrawString(font, text, pos + new Vector2(dx, dy), outline, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
