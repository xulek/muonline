using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Windows.Forms;
using Client.Data.BMD;
using Client.Main.Content;  // Contains BMDLoader and TextureLoader
using Vector3 = Microsoft.Xna.Framework.Vector3;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using MessageBox = System.Windows.Forms.MessageBox;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace BMDViewer
{
    // Definition of game states.
    public enum GameState
    {
        FolderSelection,
        ModelViewing
    }

    // Minimal Constants class – DataPath set to application directory.
    public static class Constants
    {
        public static string DataPath = AppDomain.CurrentDomain.BaseDirectory;
    }

    // Minimal implementation of BaseReader<T>
    public abstract class BaseReader<T>
    {
        public T Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            return Read(data);
        }
        public T Load(byte[] data)
        {
            return Read(data);
        }
        protected abstract T Read(byte[] data);
    }

    // Dummy implementations for decryption.
    public static class FileCryptor
    {
        public static byte[] Decrypt(byte[] data) => data;
    }
    public static class LEACrypto
    {
        public static byte[] Decrypt(byte[] data) => data;
    }

    // Minimal implementation of MathUtils.
    public static class MathUtils
    {
        public static System.Numerics.Quaternion AngleQuaternion(System.Numerics.Vector3 angles)
        {
            return System.Numerics.Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z);
        }
    }

    // Extension methods for BinaryReader.
    public static class BinaryReaderExtensions
    {
        public static T[] ReadStructArray<T>(this BinaryReader br, int count) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            T[] array = new T[count];
            byte[] bytes = br.ReadBytes(count * size);
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                for (int i = 0; i < count; i++)
                    array[i] = Marshal.PtrToStructure<T>(ptr + i * size);
            }
            finally { handle.Free(); }
            return array;
        }
        public static string ReadFixedString(this BinaryReader br, int length)
        {
            byte[] bytes = br.ReadBytes(length);
            return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }
    }

    // Helper class for vector conversion.
    public static class VectorConversion
    {
        public static Microsoft.Xna.Framework.Vector3 ToXna(this System.Numerics.Vector3 v)
        {
            return new Microsoft.Xna.Framework.Vector3(v.X, v.Y, v.Z);
        }
    }

    // Simple BMD loader – uses the Read method from BMDReader.
    public class SimpleBMDLoader : BMDReader
    {
        public BMD LoadBMD(byte[] data)
        {
            return Read(data);
        }
    }

    // Main game class.
    public class BMDViewerGame : Game
    {
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private BasicEffect basicEffect;
        private SpriteFont font; // For drawing the menu.
        private KeyboardState previousKeyboardState;


        // Game state.
        private GameState currentState = GameState.FolderSelection;

        // Folder and list of BMD files.
        private string folderPath = "";
        private List<string> availableBMDFiles = new List<string>();
        private int selectedIndex = -1;
        private int scrollIndex = 0; // Index of the first displayed item

        // Loaded model and buffers.
        private BMD loadedModel;
        private DynamicVertexBuffer[] meshVertexBuffers;
        private DynamicIndexBuffer[] meshIndexBuffers;
        private Texture2D[] meshTextures;

        private Matrix world = Matrix.Identity;
        private Matrix view;
        private Matrix projection;
        private float rotation = 0f;
        private bool modelLoaded = false;

        // Flag to prevent opening the dialog multiple times.
        private bool _inputHandled = false;

        public BMDViewerGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                // Set a higher resolution.
                PreferredBackBufferWidth = 1920,
                PreferredBackBufferHeight = 1080
            };
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            System.Windows.Forms.Application.EnableVisualStyles();
            graphics.ApplyChanges();
        }

        protected override void Initialize()
        {
            // Set up the camera.
            view = Matrix.CreateLookAt(new Vector3(0, 300, 700), Vector3.Zero, Vector3.Up);
            projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f,
                1500f);

            // Initialize BasicEffect – enable texturing and lighting.
            basicEffect = new BasicEffect(GraphicsDevice);

            // Enable lighting, but set full ambient light.
            basicEffect.LightingEnabled = false;
            basicEffect.AmbientLightColor = new Vector3(1f, 1f, 1f); // Full ambient light

            // Disable all directional lights to prevent shadows or reflections.
            basicEffect.DirectionalLight0.Enabled = false;
            basicEffect.DirectionalLight1.Enabled = false;
            basicEffect.DirectionalLight2.Enabled = false;

            // Disable reflections.
            basicEffect.SpecularColor = Vector3.Zero;
            basicEffect.SpecularPower = 0f;

            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            // Load SpriteFont (make sure DefaultFont.spritefont is in Content).
            font = Content.Load<SpriteFont>("Arial");

            // Set GraphicsDevice in loaders.
            BMDLoader.Instance.SetGraphicsDevice(GraphicsDevice);
            TextureLoader.Instance.SetGraphicsDevice(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            KeyboardState currentKeyboardState = Keyboard.GetState();

            if (currentKeyboardState.IsKeyDown(Keys.Escape))
                Exit();

            if (currentState == GameState.FolderSelection)
            {
                // Pressing F opens the folder selection.
                if (currentKeyboardState.IsKeyDown(Keys.F) && !_inputHandled)
                {
                    _inputHandled = true;
                    OpenAndLoadFolder();
                }
                else if (!currentKeyboardState.IsKeyDown(Keys.F))
                {
                    _inputHandled = false;
                }

                // Navigation of the file list (detecting key transitions)
                if (availableBMDFiles.Count > 0)
                {
                    if (currentKeyboardState.IsKeyDown(Keys.Down) && !previousKeyboardState.IsKeyDown(Keys.Down))
                    {
                        selectedIndex = (selectedIndex + 1) % availableBMDFiles.Count;
                    }
                    else if (currentKeyboardState.IsKeyDown(Keys.Up) && !previousKeyboardState.IsKeyDown(Keys.Up))
                    {
                        selectedIndex = (selectedIndex - 1 + availableBMDFiles.Count) % availableBMDFiles.Count;
                    }

                    // Determine the maximum number of displayed items.
                    int maxVisible = (GraphicsDevice.Viewport.Height - 110) / 30;
                    if (selectedIndex < scrollIndex)
                        scrollIndex = selectedIndex;
                    else if (selectedIndex >= scrollIndex + maxVisible)
                        scrollIndex = selectedIndex - maxVisible + 1;

                    if (currentKeyboardState.IsKeyDown(Keys.Enter) && !previousKeyboardState.IsKeyDown(Keys.Enter))
                    {
                        LoadBMDModel(availableBMDFiles[selectedIndex]);
                        currentState = GameState.ModelViewing;
                    }
                }
            }
            else if (currentState == GameState.ModelViewing)
            {
                // Model rotation.
                rotation += (float)gameTime.ElapsedGameTime.TotalSeconds;
                world = Matrix.CreateRotationX(MathHelper.ToRadians(-90)) * Matrix.CreateRotationY(rotation);

                // Navigation between models using Left and Right keys.
                // Perform action only on transition from unpressed state.
                if (availableBMDFiles.Count > 0)
                {
                    if (currentKeyboardState.IsKeyDown(Keys.Right) && !previousKeyboardState.IsKeyDown(Keys.Right))
                    {
                        selectedIndex = (selectedIndex + 1) % availableBMDFiles.Count;
                        LoadBMDModel(availableBMDFiles[selectedIndex]);
                    }
                    else if (currentKeyboardState.IsKeyDown(Keys.Left) && !previousKeyboardState.IsKeyDown(Keys.Left))
                    {
                        selectedIndex = (selectedIndex - 1 + availableBMDFiles.Count) % availableBMDFiles.Count;
                        LoadBMDModel(availableBMDFiles[selectedIndex]);
                    }
                }

                // Pressing B returns to the menu.
                if (currentKeyboardState.IsKeyDown(Keys.B) && !previousKeyboardState.IsKeyDown(Keys.B))
                {
                    currentState = GameState.FolderSelection;
                    modelLoaded = false;
                }
            }

            previousKeyboardState = currentKeyboardState;
            base.Update(gameTime);
        }

        // Opens FolderBrowserDialog and scans the folder for BMD files.
        private void OpenAndLoadFolder()
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder containing BMD files";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    folderPath = fbd.SelectedPath;
                    availableBMDFiles = Directory.GetFiles(folderPath, "*.bmd").ToList();
                    if (availableBMDFiles.Count > 0)
                    {
                        selectedIndex = 0;
                        scrollIndex = 0;
                    }
                    else
                    {
                        MessageBox.Show("No BMD files found in this folder.");
                    }
                }
            }
        }

        // Loads BMD model from disk and prepares buffers and textures.
        private void LoadBMDModel(string filePath)
        {
            try
            {
                var task = BMDLoader.Instance.Prepare(filePath);
                task.Wait();
                loadedModel = task.Result;

                if (loadedModel != null && loadedModel.Meshes.Length > 0)
                {
                    int meshCount = loadedModel.Meshes.Length;
                    meshVertexBuffers = new DynamicVertexBuffer[meshCount];
                    meshIndexBuffers = new DynamicIndexBuffer[meshCount];
                    meshTextures = new Texture2D[meshCount];

                    // Calculate bone matrices. If the model has actions, use the first frame (bind pose)
                    Matrix[] boneMatrix = new Matrix[loadedModel.Bones.Length];
                    if (loadedModel.Actions.Length > 0)
                    {
                        for (int i = 0; i < loadedModel.Bones.Length; i++)
                        {
                            var bone = loadedModel.Bones[i];
                            if (bone == BMDTextureBone.Dummy)
                            {
                                boneMatrix[i] = Matrix.Identity;
                                continue;
                            }
                            var bm = bone.Matrixes[0];
                            var pos = bm.Position[0].ToXna();
                            var q = bm.Quaternion[0];
                            var rot = Matrix.CreateFromQuaternion(new Microsoft.Xna.Framework.Quaternion(q.X, q.Y, q.Z, q.W));
                            Matrix localMatrix = rot;
                            localMatrix.Translation = pos;
                            if (bone.Parent != -1)
                                boneMatrix[i] = localMatrix * boneMatrix[bone.Parent];
                            else
                                boneMatrix[i] = localMatrix;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < boneMatrix.Length; i++)
                            boneMatrix[i] = Matrix.Identity;
                    }

                    // Prepare buffers and textures for each mesh.
                    for (int m = 0; m < meshCount; m++)
                    {
                        BMDLoader.Instance.GetModelBuffers(loadedModel, m, Color.White, boneMatrix, out var vb, out var ib);
                        meshVertexBuffers[m] = vb;
                        meshIndexBuffers[m] = ib;

                        string texPath = BMDLoader.Instance.GetTexturePath(loadedModel, loadedModel.Meshes[m].TexturePath);
                        if (!string.IsNullOrEmpty(texPath))
                        {
                            meshTextures[m] = TextureLoader.Instance.GetTexture2D(texPath);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Texture path not found: " + loadedModel.Meshes[m].TexturePath);
                        }
                    }
                    modelLoaded = true;
                }
                else
                {
                    MessageBox.Show("Model contains no meshes.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading BMD model: " + ex.Message);
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            // Setting culling
            GraphicsDevice.RasterizerState = new RasterizerState() { CullMode = CullMode.CullClockwiseFace };
            GraphicsDevice.Clear(Color.CornflowerBlue);

            if (currentState == GameState.FolderSelection)
            {
                // Draw the menu with the list of BMD files.
                spriteBatch.Begin();
                spriteBatch.DrawString(font, "Folder: " + folderPath, new Vector2(20, 20), Color.Yellow);
                spriteBatch.DrawString(font, "Press F to load folder", new Vector2(20, 50), Color.Yellow);
                if (availableBMDFiles.Count > 0)
                {
                    spriteBatch.DrawString(font, "Select model (Up/Down, Enter):", new Vector2(20, 80), Color.White);
                    int maxVisible = (GraphicsDevice.Viewport.Height - 110) / 30;
                    for (int i = 0; i < maxVisible && (i + scrollIndex) < availableBMDFiles.Count; i++)
                    {
                        string fileName = Path.GetFileName(availableBMDFiles[i + scrollIndex]);
                        Vector2 pos = new Vector2(40, 110 + i * 30);
                        Color col = ((i + scrollIndex) == selectedIndex) ? Color.Red : Color.White;
                        spriteBatch.DrawString(font, fileName, pos, col);
                    }
                }
                spriteBatch.End();
            }
            else if (currentState == GameState.ModelViewing)
            {
                if (modelLoaded && loadedModel != null && meshVertexBuffers != null && meshIndexBuffers != null)
                {
                    basicEffect.World = world;
                    basicEffect.View = view;
                    basicEffect.Projection = projection;

                    foreach (var pass in basicEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        for (int m = 0; m < loadedModel.Meshes.Length; m++)
                        {
                            if (meshVertexBuffers[m] == null || meshIndexBuffers[m] == null)
                                continue;

                            if (meshTextures[m] != null)
                            {
                                basicEffect.TextureEnabled = true;
                                basicEffect.Texture = meshTextures[m];
                            }
                            else
                            {
                                basicEffect.TextureEnabled = false;
                            }
                            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                            GraphicsDevice.BlendState = BlendState.Opaque;
                            basicEffect.Alpha = 1.0f;

                            GraphicsDevice.SetVertexBuffer(meshVertexBuffers[m]);
                            GraphicsDevice.Indices = meshIndexBuffers[m];
                            int primitiveCount = meshIndexBuffers[m].IndexCount / 3;
                            GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                        }
                    }
                }

                // Drawing information about the currently selected object
                spriteBatch.Begin();
                string currentFileName = availableBMDFiles.Count > 0 ? Path.GetFileName(availableBMDFiles[selectedIndex]) : "None";
                spriteBatch.DrawString(font, "Selected object: " + currentFileName, new Vector2(20, 20), Color.Yellow);
                spriteBatch.DrawString(font, "Use Left/Right arrows to navigate models. Press B to go back.", new Vector2(20, 50), Color.White);
                spriteBatch.End();
            }

            base.Draw(gameTime);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            using (var game = new BMDViewerGame())
            {
                game.Run();
            }
        }
    }
}