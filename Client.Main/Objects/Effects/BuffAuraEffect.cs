using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// A simple glowing aura effect that follows a player.
    /// </summary>
    public class BuffAuraEffect : SpriteObject
    {
        private readonly PlayerObject _owner;
        private readonly Color _auraColor;
        private float _rotation;
        private float _pulse;

        public override string TexturePath => "Effect/Magic_Ground2.ozj";

        public BuffAuraEffect(PlayerObject owner, Color color)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _auraColor = color;
            
            IsTransparent = true;
            BlendState = BlendState.Additive;
            LightEnabled = false;
            Scale = 1.5f;
            Alpha = 0.6f;
        }

        public override void Update(GameTime gameTime)
        {
            if (_owner == null || _owner.Status == GameControlStatus.Disposed)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            Position = _owner.WorldPosition.Translation + new Vector3(0, 0, 5f); // Slightly above ground
            
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _rotation += dt * 0.5f;
            _pulse = MathF.Sin((float)gameTime.TotalGameTime.TotalSeconds * 2f) * 0.1f + 0.9f;
            
            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || SpriteTexture == null) return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var effect = GraphicsManager.Instance.AlphaTestEffect3D;

            var oldBlend = gd.BlendState;
            var oldDepth = gd.DepthStencilState;

            try
            {
                gd.BlendState = BlendState.Additive;
                gd.DepthStencilState = DepthStencilState.DepthRead;

                effect.World = Matrix.CreateScale(Scale * _pulse * 50f)
                                  * Matrix.CreateRotationX(-MathHelper.PiOver2)
                                  * Matrix.CreateRotationZ(_rotation)
                                  * Matrix.CreateTranslation(Position);
                effect.View = Camera.Instance.View;
                effect.Projection = Camera.Instance.Projection;
                effect.Texture = SpriteTexture;
                effect.VertexColorEnabled = false;
                effect.DiffuseColor = _auraColor.ToVector3();
                effect.Alpha = Alpha * _pulse;

                var verts = new VertexPositionTexture[4];
                verts[0] = new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 0));
                verts[1] = new VertexPositionTexture(new Vector3(1, 1, 0), new Vector2(1, 0));
                verts[2] = new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1));
                verts[3] = new VertexPositionTexture(new Vector3(1, -1, 0), new Vector2(1, 1));

                short[] idx = { 0, 1, 2, 2, 1, 3 };

                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        verts, 0, 4,
                        idx, 0, 2);
                }
            }
            catch { }
            finally
            {
                gd.BlendState = oldBlend;
                gd.DepthStencilState = oldDepth;
            }
        }
    }
}
