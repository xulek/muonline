#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Kalima
{
    /// <summary>
    /// Implements GMHellas.cpp map-object rendering for Kalima (InHellas):
    /// an animated water film (BITMAP_WATER flipbook, alpha 0.3) over object types
    /// 0..66 except {2,4,12,14,15,18,20,21,27,29,30,31,32,41,43,52,54,55}, plus the
    /// Type 34 RENDER_CHROME|BRIGHT second pass on mesh 1.
    /// </summary>
    public class KalimaObject : MapTileObject
    {
        private const string ChromeTexturePath = "Effect/Chrome01.jpg";

        // Source exclusion list from GMHellas.cpp RenderHellasObjectMesh
        private static readonly bool[] ShellExcludedTypes = BuildExcludedTypes();

        private static readonly Task<Texture2D[]>?[] WaterFlipbookLoader = new Task<Texture2D[]>?[1];
        private Texture2D[]? _waterTextures;
        private Texture2D? _chromeTexture;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        public KalimaObject()
        {
            BlendMeshState = BlendState.Additive;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (HasWaterShell(Type))
                _waterTextures = await GetWaterFlipbookAsync();

            if (Type == 34)
                _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture(ChromeTexturePath);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
                return;

            base.Draw(gameTime);

            if (_waterTextures != null && HasWaterShell(Type))
                DrawWaterShellPass(gameTime);

            if (Type == 34 && _chromeTexture != null)
                DrawChromeMeshPass();
        }

        private void DrawWaterShellPass(GameTime gameTime)
        {
            if (Model?.Meshes == null || _waterTextures == null)
                return;

            // BITMAP_WATER flipbook advances once per 1000/REFERENCE_FPS ms (~20ms)
            int frame = (int)((float)gameTime.TotalGameTime.TotalMilliseconds / 20f) & 31;
            var waterTexture = _waterTextures[frame];

            var prevBlend = GraphicsDevice.BlendState;
            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
                {
                    Texture2D originalTexture = GetMeshTexture(meshIndex);
                    try
                    {
                        SetMeshTextureOverride(meshIndex, waterTexture);
                        DrawMesh(meshIndex);
                    }
                    finally
                    {
                        if (originalTexture != null)
                            SetMeshTextureOverride(meshIndex, originalTexture);
                        else
                            ClearMeshTextureOverride(meshIndex);
                    }
                }
            }
            finally
            {
                GraphicsDevice.BlendState = prevBlend;
            }
        }

        private void DrawChromeMeshPass()
        {
            if (Model?.Meshes == null || Model.Meshes.Length <= 1 || _chromeTexture == null)
                return;

            var prevBlend = GraphicsDevice.BlendState;
            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                const int meshIndex = 1;
                Texture2D originalTexture = GetMeshTexture(meshIndex);
                try
                {
                    SetMeshTextureOverride(meshIndex, _chromeTexture);
                    DrawMesh(meshIndex);
                }
                finally
                {
                    if (originalTexture != null)
                        SetMeshTextureOverride(meshIndex, originalTexture);
                    else
                        ClearMeshTextureOverride(meshIndex);
                }
            }
            finally
            {
                GraphicsDevice.BlendState = prevBlend;
            }
        }

        internal static bool HasWaterShell(int type) =>
            type is >= 0 and <= 66 && !ShellExcludedTypes[type];

        private static bool[] BuildExcludedTypes()
        {
            var excluded = new bool[256];
            foreach (int type in new[] { 2, 4, 12, 14, 15, 18, 20, 21, 27, 29, 30, 31, 32, 41, 43, 52, 54, 55 })
                excluded[type] = true;
            return excluded;
        }

        private static Task<Texture2D[]> GetWaterFlipbookAsync()
        {
            if (WaterFlipbookLoader[0] != null)
                return WaterFlipbookLoader[0]!;

            var task = LoadWaterFlipbookAsync();
            WaterFlipbookLoader[0] = task;
            return task;
        }

        private static async Task<Texture2D[]> LoadWaterFlipbookAsync()
        {
            var textures = new Texture2D[32];
            for (int i = 0; i < textures.Length; i++)
            {
                string path = $"Object8/wt{i:00}.jpg";
                textures[i] = await TextureLoader.Instance.PrepareAndGetTexture(path)
                              ?? GraphicsManager.Instance.Pixel;
            }
            return textures;
        }
    }
}
