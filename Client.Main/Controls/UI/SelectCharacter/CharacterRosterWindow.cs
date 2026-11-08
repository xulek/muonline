using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>
    /// Character roster of the Modern selection screen: five slot cards (class emblem, name, class, level), empty
    /// slots that open character creation, and a delete button. Click selects, double-click or Enter enters the game,
    /// Up/Down move the selection.
    /// </summary>
    public sealed class CharacterRosterWindow : OrnateWindowControl
    {
        public sealed record Slot(string Name, CharacterClassNumber Class, ushort Level);

        public const int MaxSlots = 5;

        private const int WindowWidth = 380;
        private const int CardX = 24, CardWidth = WindowWidth - 48, CardHeight = 50, CardStep = 92, CardTop = 74;
        private const int FooterTop = CardTop + MaxSlots * CardStep + 2;
        private const int WindowHeight = FooterTop + 70;
        private const int HitDelete = FirstHitId;
        private const int HitSlot = FirstHitId + 10;
        private const int DoubleClickMs = 450;

        private IReadOnlyList<Slot> _slots = Array.Empty<Slot>();
        private int _lastClickSlot = -1;
        private long _lastClickTicks;

        public CharacterRosterWindow() : base(new Point(WindowWidth, WindowHeight))
        {
            Title = "CHARACTERS";
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 34 };
        }

        public int SelectedIndex { get; set; } = -1;

        /// <summary>While busy (loading, entering, a dialog is open) the roster is dimmed and ignores input.</summary>
        public bool Busy { get; set; }

        public event EventHandler<int> SlotSelected;
        public event EventHandler CreateRequested;
        public event EventHandler DeleteRequested;
        public event EventHandler EnterRequested;

        protected override bool InputEnabled => !Busy;

        protected override IEnumerable<string> PreloadPaths => new[]
        {
            "Emblems/class_wizard", "Emblems/class_knight", "Emblems/class_elf", "Emblems/class_gladiator",
            "Emblems/class_lord", "Emblems/class_summoner", "Emblems/class_fighter", "Emblems/create"
        };

        public void SetSlots(IReadOnlyList<Slot> slots)
        {
            _slots = slots ?? Array.Empty<Slot>();
            if (SelectedIndex >= _slots.Count)
                SelectedIndex = _slots.Count - 1;
        }

        private bool HasSelection => SelectedIndex >= 0 && SelectedIndex < _slots.Count;

        // --- Layout -------------------------------------------------------------------------------------

        private Rectangle CardBounds(int index) => At(CardX, CardTop + index * CardStep, CardWidth, CardHeight);
        private Rectangle DeleteBounds => At(WindowWidth - CardX - 150, FooterTop + 6, 150, 38);

        // --- Input --------------------------------------------------------------------------------------

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || !Visible || Busy || _slots.Count == 0)
                return;

            KeyboardState keys = MuGame.Instance.Keyboard;
            KeyboardState previous = MuGame.Instance.PrevKeyboard;
            bool Pressed(Keys key) => keys.IsKeyDown(key) && previous.IsKeyUp(key);

            if (Pressed(Keys.Up) || Pressed(Keys.Down))
            {
                int next = Math.Clamp((HasSelection ? SelectedIndex : 0) + (Pressed(Keys.Up) ? -1 : 1), 0, _slots.Count - 1);
                if (next != SelectedIndex)
                    Select(next);
            }
            else if (Pressed(Keys.Enter) && HasSelection)
            {
                EnterRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override int HitTest(Point position)
        {
            if (DeleteBounds.Contains(position))
                return HasSelection ? HitDelete : HitNone;

            for (int i = 0; i < MaxSlots; i++)
                if (CardBounds(i).Contains(position))
                    return HitSlot + i;

            return HitNone;
        }

        protected override void OnActivate(int hit)
        {
            if (hit == HitDelete)
            {
                PlayClickSound();
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            int slot = hit - HitSlot;
            if (slot >= _slots.Count)
            {
                PlayClickSound();
                CreateRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            long now = Environment.TickCount64;
            bool doubleClick = slot == _lastClickSlot && now - _lastClickTicks <= DoubleClickMs;
            _lastClickSlot = slot;
            _lastClickTicks = now;

            if (doubleClick && slot == SelectedIndex)
            {
                EnterRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (slot != SelectedIndex)
                Select(slot);
        }

        private void Select(int index)
        {
            PlayClickSound();
            SelectedIndex = index;
            SlotSelected?.Invoke(this, index);
        }

        // --- Drawing ------------------------------------------------------------------------------------

        protected override void DrawContent(SpriteBatch sb, float alpha)
        {
            float a = (Busy ? 0.6f : 1f) * alpha;

            // Unselected cards first so the glow of the selected one is drawn on top.
            for (int i = 0; i < MaxSlots; i++)
                if (i != SelectedIndex)
                    DrawSlot(sb, i, a);
            if (HasSelection)
                DrawSlot(sb, SelectedIndex, a);

            Rectangle footer = At(CardX + 4, FooterTop + 6, CardWidth - 160, 38);
            OrnateDraw.TextLeft(sb, $"{_slots.Count} / {MaxSlots} characters", footer, 12.5f, OrnateStyle.TextMuted, a);

            var deleteState = new WidgetState(Clicks.IsHover(HitDelete), Clicks.IsPressed(HitDelete), Disabled: !HasSelection || Busy);
            OrnateWidgets.Button(sb, DeleteBounds, "Delete", deleteState, a);
        }

        private void DrawSlot(SpriteBatch sb, int index, float a)
        {
            Rectangle card = CardBounds(index);
            bool hover = !Busy && Clicks.IsHover(HitSlot + index);

            if (index >= _slots.Count)
            {
                Rectangle emptySocket = OrnateWidgets.Card(sb, card, new WidgetState(Hover: hover), a * (hover ? 0.95f : 0.7f));
                OrnateWidgets.SocketEmblem(sb, "create", emptySocket, a * (hover ? 1f : 0.55f));
                OrnateDraw.TextLeft(sb, "Create Character", TextArea(card, emptySocket), 15f,
                    hover ? OrnateStyle.Gold : OrnateStyle.TextMuted, a);
                return;
            }

            Slot slot = _slots[index];
            bool selected = index == SelectedIndex;
            Rectangle socket = OrnateWidgets.Card(sb, card, new WidgetState(Hover: hover, Selected: selected), a);
            OrnateWidgets.SocketEmblem(sb, CharacterClasses.EmblemFor(slot.Class), socket, a);

            Rectangle text = TextArea(card, socket);
            Rectangle nameLine = new(text.X, card.Y + 6, text.Width, card.Height / 2 - 4);
            Rectangle classLine = new(text.X, card.Center.Y + 1, text.Width, card.Height / 2 - 6);
            OrnateDraw.TextLeft(sb, slot.Name, nameLine, 17f, selected ? OrnateStyle.Gold : OrnateStyle.TextLight, a, bold: true);
            OrnateDraw.TextLeft(sb, CharacterClassDatabase.GetClassName(slot.Class), classLine, 12.5f,
                selected ? OrnateStyle.GoldDim : OrnateStyle.TextMuted, a);
            OrnateDraw.TextRight(sb, $"Lv. {slot.Level}", new Rectangle(text.X, card.Y, text.Width - 6, card.Height), 14f,
                selected ? Color.White : OrnateStyle.GoldDim, a, bold: selected);
        }

        private static Rectangle TextArea(Rectangle card, Rectangle socket) =>
            new(socket.Right + 14, card.Y, card.Right - socket.Right - 26, card.Height);
    }
}
