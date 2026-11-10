using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Waterfall sheet of the character scene, drawn with SceneWater.fx (Waterfall): two scrolling layers of the
    /// original texture with wobble, foam at the lip and a fade into mist at the bottom. The geometry is static, so it
    /// is baked once into world space; the scrolling happens in the shader. Spray and mist come from
    /// <see cref="WaterfallMist"/>, placed along the bottom edge.
    /// </summary>
    public class WaterFallObject : ModelObject
    {
        private VertexPositionColorTexture[] _vertices;
        private Texture2D _texture;
        private WaterfallMist _mist;
        private static readonly int[] QuadCorners = { 0, 1, 2, 0, 2, 3 };

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(alwaysUpdate: true);

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object94/Object{idx}.bmd");
            await base.Load();
            // After base.Load, which derives transparency from the meshes: as an opaque map object the waterfall
            // went to the static instanced queue, which never calls Draw (the water stood still).
            IsTransparent = true;

            string texturePath = Model?.Meshes?.Length > 0 ? BMDLoader.Instance.GetTexturePath(Model, Model.Meshes[0].TexturePath) : null;
            if (!string.IsNullOrEmpty(texturePath))
                _texture = await TextureLoader.Instance.PrepareAndGetTexture(texturePath);
        }

        // Drawn after the opaque world, sorted with the other transparent objects.
        public override float Depth => Position.Y + Position.Z + 400f;

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_vertices == null && Constants.ENABLE_MENU_SCENE_EFFECTS && Status == Models.GameControlStatus.Ready)
                BuildGeometry();
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            Effect effect = GraphicsManager.Instance.SceneWaterEffect;
            if (!Constants.ENABLE_MENU_SCENE_EFFECTS || effect == null || _texture == null || _texture.IsDisposed)
            {
                base.Draw(gameTime);
                return;
            }

            if (_vertices == null)
                return;

            var gd = GraphicsDevice;
            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;

            effect.CurrentTechnique = effect.Techniques["Waterfall"];
            effect.Parameters["WorldViewProjection"]?.SetValue(Camera.Instance.ViewProjection);
            // Draw time: the world updates static map objects only now and then, a clock kept in Update stalls.
            effect.Parameters["Time"]?.SetValue((float)(gameTime.TotalGameTime.TotalSeconds % 3600.0));
            effect.Parameters["Intensity"]?.SetValue(0.75f);
            effect.Parameters["Tint"]?.SetValue(new Vector3(0.8f, 0.92f, 1.08f));
            effect.Parameters["FoamColor"]?.SetValue(new Vector3(0.55f, 0.75f, 0.85f));
            effect.Parameters["ScrollA"]?.SetValue(0.55f);
            effect.Parameters["ScrollB"]?.SetValue(0.85f);
            effect.Parameters["WaterTexture"]?.SetValue(_texture);

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertices.Length / 3);
            }

            gd.BlendState = previousBlend;
            gd.DepthStencilState = previousDepth;
            gd.RasterizerState = previousRasterizer;
        }

        private bool UsesShader =>
            Constants.ENABLE_MENU_SCENE_EFFECTS && _vertices != null && _texture != null && !_texture.IsDisposed &&
            GraphicsManager.Instance.SceneWaterEffect != null;

        // ModelObject draws its blend meshes (the _R water texture) again in DrawAfter: that still copy lay over the
        // scrolling one. With the shader the whole sheet is drawn in Draw only.
        public override void DrawAfter(GameTime gameTime)
        {
            if (!UsesShader)
                base.DrawAfter(gameTime);
        }

        /// <summary>World-space triangles; colour G = fall progress from the lip (0) to the bottom (1).</summary>
        private bool BuildGeometry()
        {
            var bones = BoneTransform;
            if (Model?.Meshes == null || bones == null)
                return false;

            Matrix world = WorldPosition;
            var positions = new List<Vector3>();
            var texCoords = new List<Vector2>();
            foreach (var mesh in Model.Meshes)
            {
                if (mesh?.Triangles == null || mesh.Vertices == null || mesh.TexCoords == null)
                    continue;

                foreach (var triangle in mesh.Triangles)
                {
                    int corners = triangle.Polygon == 4 ? 4 : 3;
                    for (int k = 0; k < (corners == 4 ? 6 : 3); k++)
                    {
                        int c = corners == 4 ? QuadCorners[k] : k;
                        var vertex = mesh.Vertices[triangle.VertexIndex[c]];
                        Matrix bone = vertex.Node >= 0 && vertex.Node < bones.Length ? bones[vertex.Node] : Matrix.Identity;
                        positions.Add(Vector3.Transform(Vector3.Transform(vertex.Position, bone), world));
                        var uv = mesh.TexCoords[triangle.TexCoordIndex[c]];
                        texCoords.Add(new Vector2(uv.U, uv.V));
                    }
                }
            }

            if (positions.Count == 0)
                return false;

            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var p in positions)
            {
                top = MathF.Max(top, p.Z);
                bottom = MathF.Min(bottom, p.Z);
            }

            float height = MathF.Max(1f, top - bottom);
            _vertices = new VertexPositionColorTexture[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                float progress = (top - positions[i].Z) / height;
                float edge = MathHelper.Clamp(progress / 0.04f, 0f, 1f);
                _vertices[i] = new VertexPositionColorTexture(positions[i], new Color(0f, progress, 0f, edge), texCoords[i]);
            }

            SpawnMist(positions, bottom, height);
            return true;
        }

        private void SpawnMist(List<Vector3> positions, float bottom, float height)
        {
            if (_mist != null || World == null)
                return;

            // The water falls into the abyss and fades out on the way (shader); the mist hangs where it fades,
            // the spray jumps off the lip.
            float top = bottom + height;
            if (!Band(positions, top - height * 0.62f, height * 0.08f, out Vector3 veilMin, out Vector3 veilMax) ||
                !Band(positions, top - height * 0.015f, height * 0.03f, out Vector3 lipMin, out Vector3 lipMax))
                return;

            _mist = new WaterfallMist(veilMin, veilMax, lipMin, lipMax, height) { World = World };
            World.Objects.Add(_mist);
            _ = _mist.Load();
        }

        private static bool Band(List<Vector3> positions, float z, float halfHeight, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                if (MathF.Abs(p.Z - z) > halfHeight)
                    continue;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (min.X == float.MaxValue)
                return false;
            min.Z = max.Z = z;
            return true;
        }

        public override void Dispose()
        {
            if (_mist != null)
            {
                World?.Objects.Remove(_mist);
                _mist.Dispose();
                _mist = null;
            }

            base.Dispose();
        }
    }
}
