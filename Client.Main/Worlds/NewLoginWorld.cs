using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Scene;
using Client.Main.Objects.Player;
using Client.Main.Objects.Worlds.Login;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Worlds
{
    public class NewLoginWorld : CinematicWorld
    {
        private static readonly ILogger _logger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { }).CreateLogger<NewLoginWorld>();

        public NewLoginWorld() : base(worldIndex: 95)
        {
            EnableShadows = false;
            Terrain.PreferIndexBatching = true;
            // Night sea: the moon path, sails and the magic light glow; blacks lifted towards blue.
            Glow.Threshold = 0.6f;
            Glow.Knee = 0.3f;
            Glow.Intensity = 0.85f;
            Glow.Tint = new Vector3(0.95f, 0.9f, 1.05f);
            Glow.Saturation = 1.1f;
            Glow.Contrast = 1.06f;
            Glow.Lift = new Vector3(0.006f, 0.008f, 0.025f);
            Glow.Vignette = 0.5f;
        }

        private bool _sceneEffectsAdded;

        /// <summary>
        /// Effects beyond the map data: the moon path on the sea (shader) with glitter, the magic light on the island
        /// with its aura and rising sparks, sea mist on the horizon, lightning in the clouds, glinting and shooting
        /// stars (hull spray comes from the splash markers).
        /// </summary>
        private void AddSceneEffects()
        {
            if (_sceneEffectsAdded || !Constants.ENABLE_MENU_SCENE_EFFECTS)
                return;
            _sceneEffectsAdded = true;

            var pillar = new Vector3(16445f, 17360f, 330f);
            var effects = new WorldObject[]
            {
                new SeaSheen(new Vector3(22300f, 9800f, 191f), new Vector3(17000f, 16700f, 191f), nearWidth: 520f, farWidth: 1800f) { Intensity = 0.9f },
                new SeaGlitter(new Vector3(22200f, 9900f, 190f), new Vector3(17300f, 16300f, 190f), nearWidth: 450f, farWidth: 1500f, rate: 240f),
                new PillarAura(pillar, height: 2100f, radius: 120f, color: new Vector3(1f, 0.45f, 0.95f)),
                new CloudLightning(new Vector3(15500f, 11500f, 1600f), new Vector3(21500f, 16500f, 3300f), size: 80f),
                new RisingSparks(pillar, radius: 280f, rate: 22f, riseSpeed: new Vector2(120f, 260f), size: 2.4f,
                    new Vector3(1f, 0.4f, 0.9f), new Vector3(0.7f, 0.5f, 1f), new Vector3(1f, 0.75f, 0.95f)),
                new DriftingMist(new Vector3(13500f, 12500f, 230f), new Vector3(20500f, 17500f, 480f), count: 18,
                    wind: new Vector3(140f, -60f, 0f), alpha: 0.75f, size: new Vector2(13f, 22f), color: new Vector3(0.7f, 0.72f, 0.95f)),
                new SkyGlints(new Vector3(15500f, 11500f, 2900f), new Vector3(21500f, 16500f, 4700f), size: 1.6f),
                new ShootingStars(new Vector3(15500f, 11500f, 3800f), new Vector3(21500f, 16500f, 4700f), across: new Vector3(1f, 1f, 0f)) { Size = 1.8f },
            };

            foreach (var effect in effects)
            {
                effect.World = this;
                Objects.Add(effect);
                _ = effect.Load();
            }
        }

        protected override bool DeferCameraActivation => true;

        protected override void ConfigureCameraState(ref CameraState cameraState)
        {
            // Preserve the exact framing used before deferred camera activation was introduced.
            // The legacy LoginScene lifecycle invoked NewLoginWorld.AfterLoad twice, so its
            // historical +650 Z adjustment was effectively applied twice (+1300 total).
            cameraState = cameraState.With(
                viewFar: 50000f,
                target: cameraState.Target + new Vector3(0f, 0f, 1300f));
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();
            MapTileObjects[5] = typeof(ShipObject);
            MapTileObjects[12] = typeof(ShipObject);
            MapTileObjects[13] = typeof(ShipObject);

            MapTileObjects[54] = typeof(WaterSplashObject);
            MapTileObjects[1] = typeof(ShipWaterPathObject);

            MapTileObjects[18] = typeof(BlendedObjects);
            MapTileObjects[7] = typeof(BlendedObjects);
            MapTileObjects[8] = typeof(BlendedObjects);
            MapTileObjects[10] = typeof(BlendedObjects);
            MapTileObjects[11] = typeof(BlendedObjects);
            MapTileObjects[19] = typeof(BlendedObjects);
            MapTileObjects[20] = typeof(BlendedObjects);
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
            AddSceneEffects();

            // water animation parameters
            Terrain.WaterSpeed = 0.15f;             // Example: faster water movement
            Terrain.DistortionAmplitude = 0.2f;      // Example: stronger distortion
            Terrain.DistortionFrequency = 1.0f;      // Example: lower frequency for distortion
            Terrain.WaterFlowDirection = Vector2.UnitY;

        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            if (!Visible) return;

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
    }
}
