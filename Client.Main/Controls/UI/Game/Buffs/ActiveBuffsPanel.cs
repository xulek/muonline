#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controls.UI.Game.Map;
using Client.Main.Core.Client;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Buffs
{
    /// <summary>
    /// Transparent container that lays out buff icon slots in a grid.
    /// </summary>
    public sealed class ActiveBuffsPanel : UIControl
    {
        private readonly CharacterState _characterState;
        private readonly CurrentLocationControl _locationControl;
        private readonly List<BuffSlotControl> _buffSlots = new();

        private const int MaxVisibleBuffs = 32;
        private const int BuffsPerRow = 8;

        private int _slotWidth = BuffSlotControl.DefaultSlotWidth;
        private int _slotHeight = BuffSlotControl.DefaultSlotHeight;
        private int _spacing = 3;
        private int _visibleBuffCount;

        private Point _lastVirtualSize = Point.Zero;

        public ActiveBuffsPanel(CharacterState characterState, CurrentLocationControl locationControl)
        {
            _characterState = characterState;
            _locationControl = locationControl;

            AutoViewSize = false;
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            for (int i = 0; i < MaxVisibleBuffs; i++)
            {
                var slot = new BuffSlotControl
                {
                    Visible = false,
                    X = 0,
                    Y = 0
                };

                _buffSlots.Add(slot);
                Controls.Add(slot);
            }

            _characterState.ActiveBuffsChanged += OnActiveBuffsChanged;

            RefreshViewportScale(force: true);
            UpdateBuffDisplay();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            RefreshViewportScale();
            UpdateAnchorPosition();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || _visibleBuffCount <= 0)
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
            {
                return;
            }

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                for (int i = 0; i < Controls.Count; i++)
                {
                    Controls[i].Draw(gameTime);
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override void Dispose()
        {
            _characterState.ActiveBuffsChanged -= OnActiveBuffsChanged;
            base.Dispose();
        }

        private void OnActiveBuffsChanged()
        {
            MuGame.ScheduleOnMainThread(UpdateBuffDisplay);
        }

        private void RefreshViewportScale(bool force = false)
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (!force && virtualSize == _lastVirtualSize)
            {
                return;
            }

            _lastVirtualSize = virtualSize;

            float scaleX = virtualSize.X / 1024f;
            float scaleY = virtualSize.Y / 768f;
            float scale = Math.Clamp(MathF.Min(scaleX, scaleY), 0.82f, 1.25f);

            _slotWidth = ScaleValue(BuffIconAtlas.IconWidth + 4, scale);
            _slotHeight = ScaleValue(BuffIconAtlas.IconHeight + 4, scale);
            _spacing = ScaleValue(3, scale);

            for (int i = 0; i < _buffSlots.Count; i++)
            {
                _buffSlots[i].SetSlotSize(_slotWidth, _slotHeight);
            }

            UpdateBuffDisplay();
            UpdateAnchorPosition();
        }

        private void UpdateBuffDisplay()
        {
            var sortedBuffs = _characterState.GetActiveBuffs()
                .Where(b => BuffIconAtlas.ShouldRender(b.EffectId))
                .ToList();

            var activeBuffs = OrderLikeReferenceClient(sortedBuffs)
                .Take(MaxVisibleBuffs)
                .ToList();

            _visibleBuffCount = activeBuffs.Count;

            for (int i = 0; i < _buffSlots.Count; i++)
            {
                if (i >= _visibleBuffCount)
                {
                    _buffSlots[i].Buff = null;
                    _buffSlots[i].Visible = false;
                    continue;
                }

                int row = i / BuffsPerRow;
                int col = i % BuffsPerRow;

                _buffSlots[i].X = col * (_slotWidth + _spacing);
                _buffSlots[i].Y = row * (_slotHeight + _spacing);
                _buffSlots[i].Buff = activeBuffs[i];
                _buffSlots[i].Visible = true;
            }

            if (_visibleBuffCount <= 0)
            {
                ControlSize = new Point(1, 1);
                ViewSize = ControlSize;
                return;
            }

            int rows = (_visibleBuffCount + BuffsPerRow - 1) / BuffsPerRow;
            int cols = Math.Min(_visibleBuffCount, BuffsPerRow);

            int width = cols * _slotWidth + (cols - 1) * _spacing;
            int height = rows * _slotHeight + (rows - 1) * _spacing;

            ControlSize = new Point(Math.Max(1, width), Math.Max(1, height));
            ViewSize = ControlSize;

            UpdateAnchorPosition();
        }

        private void UpdateAnchorPosition()
        {
            if (_locationControl == null)
            {
                return;
            }

            Point virtualSize = UiScaler.VirtualSize;
            Point anchor = _locationControl.GetBuffAnchor(_spacing + 2);

            int rightLimit = Math.Max(0, virtualSize.X - ViewSize.X - 8);
            int bottomLimit = Math.Max(0, virtualSize.Y - ViewSize.Y - 8);

            X = Math.Clamp(anchor.X, 0, rightLimit);
            Y = Math.Clamp(anchor.Y, 0, bottomLimit);
        }

        private static int ScaleValue(int value, float scale)
        {
            return Math.Max(1, (int)MathF.Round(value * scale));
        }

        private static IEnumerable<ActiveBuffState> OrderLikeReferenceClient(IReadOnlyList<ActiveBuffState> buffs)
        {
            var ordered = new LinkedList<ActiveBuffState>();

            foreach (var buff in buffs)
            {
                if (BuffIconAtlas.IsDebuff(buff.EffectId))
                {
                    ordered.AddLast(buff);
                }
                else
                {
                    ordered.AddFirst(buff);
                }
            }

            return ordered;
        }
    }
}
