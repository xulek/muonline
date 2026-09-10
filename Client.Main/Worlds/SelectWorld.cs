using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Worlds.SelectWrold;
using Client.Main.Scenes.SelectCharacter;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    public class SelectWorld : WorldControl
    {
        private readonly Vector3 _characterDisplayPosition = new(14000, 12295, 250);
        private readonly Vector3 _characterDisplayAngle = new(0, 0, MathHelper.ToRadians(90));
        private ILogger<SelectWorld> _logger;
        private CharacterSelectionController _controller;
        private readonly SemaphoreSlim _faceLoadGate = new(1, 1);
        private CreateRoleFaceObject _faceModel;
        private int _faceRequestVersion;
        private bool _showingCreateRoleFace;

        public Vector3 CharacterDisplayPosition => _characterDisplayPosition;
        public Vector3 CharacterDisplayAngle => _characterDisplayAngle;

        public SelectWorld() : base(worldIndex: 94)
        {
            EnableShadows = false;
            Terrain.PreferIndexBatching = true;
            _logger = MuGame.AppLoggerFactory?.CreateLogger<SelectWorld>() ?? throw new System.InvalidOperationException("LoggerFactory not initialized in MuGame");
        }

        protected override bool DeferCameraActivation => true;

        protected override void ConfigureCameraState(ref CameraState cameraState)
        {
            cameraState = cameraState.With(
                viewFar: 5500f,
                fieldOfView: 29f * Constants.FOV_SCALE,
                target: new Vector3(14229.295898f, 12340.358398f, 380f));
        }

        public void SetController(CharacterSelectionController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// Shows the selected class model in front of the selection-world camera.
        /// Requests are serialized so rapid class changes cannot rebuild the same object
        /// concurrently or publish an older class after a newer selection.
        /// </summary>
        public async Task ShowCreateRoleFace(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                Interlocked.Increment(ref _faceRequestVersion);
                _showingCreateRoleFace = true;
                if (_faceModel != null)
                    _faceModel.Hidden = true;
                return;
            }

            int requestVersion = Interlocked.Increment(ref _faceRequestVersion);
            await _faceLoadGate.WaitAsync();
            try
            {
                if (requestVersion != Volatile.Read(ref _faceRequestVersion))
                    return;

                _showingCreateRoleFace = true;
                Vector3 anchor = GetCreateRoleFaceAnchor();

                if (_faceModel == null)
                {
                    var face = new CreateRoleFaceObject
                    {
                        World = this,
                        Hidden = true
                    };

                    try
                    {
                        await face.SetClass(modelName, anchor);
                        if (requestVersion != Volatile.Read(ref _faceRequestVersion) ||
                            face.Status != GameControlStatus.Ready)
                        {
                            face.Dispose();
                            return;
                        }

                        Objects.Add(face);
                        _faceModel = face;
                    }
                    catch (Exception ex)
                    {
                        face.Dispose();
                        _logger.LogWarning(ex, "Unable to load character creation preview {Model}.", modelName);
                        return;
                    }
                }
                else
                {
                    _faceModel.Hidden = true;
                    await _faceModel.SetClass(modelName, anchor);
                }

                if (requestVersion != Volatile.Read(ref _faceRequestVersion) ||
                    _faceModel == null || _faceModel.Status != GameControlStatus.Ready)
                    return;

                _faceModel.Hidden = false;
                ActivateObjectForRendering(_faceModel, forceFullVisibilityRebuild: true);
            }
            finally
            {
                _faceLoadGate.Release();
            }
        }

        public void HideCreateRoleFace()
        {
            Interlocked.Increment(ref _faceRequestVersion);
            _showingCreateRoleFace = false;
            if (_faceModel != null)
                _faceModel.Hidden = true;
        }

        public void BeginCreateRoleFace()
        {
            _showingCreateRoleFace = true;
        }

        private Vector3 GetCreateRoleFaceAnchor()
        {
            var camera = Camera.Instance;
            Vector3 forward = camera.Target - camera.Position;
            if (forward.LengthSquared() < 0.001f)
                return _characterDisplayPosition;

            forward.Normalize();
            Vector3 left = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
            return camera.Position + forward * 900f + left * 60f;
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();
            MapTileObjects[14] = null;
            MapTileObjects[71] = typeof(BlendedObjects);
            MapTileObjects[11] = typeof(BlendedObjects);
            MapTileObjects[36] = typeof(LightObject);
            MapTileObjects[25] = typeof(BlendedObjects);
            MapTileObjects[33] = typeof(BlendedObjects);
            MapTileObjects[30] = typeof(BlendedObjects);
            MapTileObjects[31] = typeof(FlowersObject2);
            MapTileObjects[34] = typeof(FlowersObject);
            MapTileObjects[26] = typeof(WaterFallObject);
            MapTileObjects[24] = typeof(WaterFallObject);
            MapTileObjects[54] = typeof(WaterSplashObject);
            MapTileObjects[55] = typeof(WaterSplashObject);
            MapTileObjects[56] = typeof(WaterSplashObject);
        }

        public override void AfterLoad()
        {
            base.AfterLoad();

            // water animation parameters
            Terrain.WaterSpeed = 0.05f;
            Terrain.DistortionAmplitude = 0.2f;
            Terrain.DistortionFrequency = 1.0f;

        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (!Visible) return;

            // Keep the selected cinematic actor published even if a body-part model swap
            // or first-frame recovery temporarily invalidated its visibility.
            if (Status == GameControlStatus.Ready && _controller != null && !_showingCreateRoleFace)
            {
                _controller.EnsureActiveCharacterVisible(this);

                foreach (var (player, label) in _controller.Labels)
                {
                    if (player.Status != GameControlStatus.Ready || player.Hidden)
                    {
                        label.Visible = false;
                        continue;
                    }

                    var head = new Vector3(
                        player.WorldPosition.Translation.X,
                        player.WorldPosition.Translation.Y,
                        player.BoundingBoxWorld.Min.Z - 20);

                    var sp = GraphicsDevice.Viewport.Project(
                                 head,
                                 Camera.Instance.Projection,
                                 Camera.Instance.View,
                                 Matrix.Identity);

                    if (sp.Z is < 0 or > 1)
                    {
                        label.Visible = false;
                        continue;
                    }

                    var font = GraphicsManager.Instance.Font;
                    float k = label.FontSize / Constants.BASE_FONT_SIZE;
                    Vector2 s = font.MeasureString(label.Text) * k;

                    var virtualPos = UiScaler.ToVirtual(new Point((int)sp.X, (int)sp.Y));

                    label.X = (int)(virtualPos.X - s.X / 2f);
                    label.Y = (int)(virtualPos.Y - s.Y - 4);
                    label.ControlSize = new Point((int)s.X, (int)s.Y);
                    label.Visible = true;
                }
            }

            // Debug key handling
            if (MuGame.Instance.PrevKeyboard.IsKeyDown(Keys.Delete) && MuGame.Instance.Keyboard.IsKeyUp(Keys.Delete))
            {
                if (Objects.Count > 0)
                {
                    var obj = Objects[0];
                    _logger?.LogDebug($"Removing obj: {obj.Type} -> {obj.ObjectName}");
                    Objects.RemoveAt(0);
                }
            }
            else if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Add))
            {
                Camera.Instance.ViewFar += 10;
            }
            else if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Subtract))
            {
                Camera.Instance.ViewFar -= 10;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            var gd = GraphicsManager.Instance.GraphicsDevice;
            gd.BlendState = BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            base.Draw(gameTime);
        }

    }
}
