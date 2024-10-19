﻿using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI
{
    public class LabelControl : UIControl
    {
        private string _text;
        private object[] _textArgs = [];
        private string _renderedText;
        private float _fontSize = 10f;
        private float _scaleFactor;

        public string Text { get => _text; set { if (_text != value) { _text = value; OnChangeText(); } } }
        public object[] TextArgs { get => _textArgs; set { if (_textArgs != value) { _textArgs = value; OnChangeText(); } } }
        public float FontSize { get => _fontSize; set { if (_fontSize != value) { _fontSize = value; OnChangeText(); } } }
        public Color TextColor { get; set; } = Color.White;

        public LabelControl()
        {
            AutoSize = false;
        }

        public override void Draw(GameTime gameTime)
        {
            MuGame.Instance.SpriteBatch.Begin();
            MuGame.Instance.SpriteBatch.DrawString(
                MuGame.Instance.Font,
                _renderedText,
                ScreenLocation.Location.ToVector2(),
                TextColor,
                0f,
                Vector2.Zero,
                _scaleFactor,
                SpriteEffects.None,
                0f
            );
            MuGame.Instance.SpriteBatch.End();

            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

            base.Draw(gameTime);
        }

        private void OnChangeText()
        {
            if (string.IsNullOrEmpty(Text))
            {
                Width = 0;
                Height = 0;
                return;
            }

            _renderedText = SafeFormat(Text, TextArgs);

            float baseFontSize = 10f;
            var textSize = MuGame.Instance.Font.MeasureString(_renderedText);
            _scaleFactor = FontSize / baseFontSize;

            Width = (int)(textSize.X * _scaleFactor);
            Height = (int)(textSize.Y * _scaleFactor);
        }


        private string SafeFormat(string format, params object[] args)
        {
            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
