using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Light over the water pools of the character scene: one mesh covering every water tile of the area, drawn
    /// with SceneWater.fx (Pool) - moving caustics and sparkles, faded out towards the shore.
    /// </summary>
    internal sealed class PoolSurface : WorldObject
    {
        private const byte WaterTexture = 5;   // TileWater01
        private const float Lift = 2f;

        private readonly Rectangle _tiles;
        private VertexPositionColorTexture[] _vertices;
        private float _time;

        /// <param name="tiles">Terrain tiles to search for water (x, y, width, height).</param>
        public PoolSurface(Rectangle tiles)
        {
            _tiles = tiles;
            IsTransparent = true;
            AffectedByTransparency = false;
        }

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(forceVisible: true, alwaysUpdate: true);

        // Right after the terrain, before other transparent objects that may stand in the water.
        public override float Depth => float.MaxValue;

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_vertices == null && World?.Terrain != null && World.Status == Models.GameControlStatus.Ready)
                Build(World.Terrain);
        }

        private void Build(TerrainControl terrain)
        {
            bool IsWater(int x, int y) =>
                x >= 0 && y >= 0 && x < Constants.TERRAIN_SIZE && y < Constants.TERRAIN_SIZE &&
                terrain.GetBaseTextureIndexAt(x, y) == WaterTexture &&
                // The terrain skips NoGround tiles (the void between the islands); no water floating there.
                !terrain.RequestTerrainFlag(x, y).HasFlag(Client.Data.ATT.TWFlags.NoGround);

            // Corner opacity: share of the four tiles around the corner that are water.
            float Corner(int x, int y)
            {
                int n = (IsWater(x - 1, y - 1) ? 1 : 0) + (IsWater(x, y - 1) ? 1 : 0) + (IsWater(x - 1, y) ? 1 : 0) + (IsWater(x, y) ? 1 : 0);
                float a = n / 4f;
                return a * a;
            }

            var list = new List<VertexPositionColorTexture>();
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int y = _tiles.Top; y < _tiles.Bottom; y++)
            {
                for (int x = _tiles.Left; x < _tiles.Right; x++)
                {
                    if (!IsWater(x, y))
                        continue;

                    VertexPositionColorTexture V(int cx, int cy)
                    {
                        float wx = cx * Constants.TERRAIN_SCALE, wy = cy * Constants.TERRAIN_SCALE;
                        var p = new Vector3(wx, wy, terrain.RequestTerrainHeight(wx, wy) + Lift);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                        return new VertexPositionColorTexture(p, new Color(1f, 1f, 1f, Corner(cx, cy)), Vector2.Zero);
                    }

                    var a = V(x, y); var b = V(x + 1, y); var c = V(x + 1, y + 1); var d = V(x, y + 1);
                    list.Add(a); list.Add(b); list.Add(c);
                    list.Add(a); list.Add(c); list.Add(d);
                }
            }

            _vertices = list.ToArray();
            if (_vertices.Length > 0)
            {
                Position = (min + max) * 0.5f;
                BoundingBoxLocal = new BoundingBox(min - Position - new Vector3(10f), max - Position + new Vector3(10f));
            }
        }

        public override void Draw(GameTime gameTime)
        {
            _time = (float)(gameTime.TotalGameTime.TotalSeconds % 3600.0);
            Effect effect = GraphicsManager.Instance.SceneWaterEffect;
            if (!Visible || effect == null || _vertices == null || _vertices.Length == 0)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;

            effect.CurrentTechnique = effect.Techniques["Pool"];
            effect.Parameters["WorldViewProjection"]?.SetValue(Camera.Instance.ViewProjection);
            effect.Parameters["Time"]?.SetValue(_time);
            effect.Parameters["Intensity"]?.SetValue(0.75f);
            effect.Parameters["Tint"]?.SetValue(new Vector3(0.45f, 0.9f, 1f));
            effect.Parameters["FoamColor"]?.SetValue(new Vector3(0.9f, 1f, 1f));

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                for (int start = 0; start < _vertices.Length; start += 30000)
                {
                    int count = Math.Min(30000, _vertices.Length - start);
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, start, count / 3);
                }
            }

            gd.BlendState = previousBlend;
            gd.DepthStencilState = previousDepth;
            gd.RasterizerState = previousRasterizer;
        }
    }
}
