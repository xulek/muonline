using Client.Main.Controls;
using Client.Main.Objects.Worlds.Icarus;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Icarus;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(10, "Icarus")]
    public class IcarusWorld : WalkableWorldControl
    {
        private static readonly Color CLEAR_COLOR = new Color(3f / 256f, 25f / 256f, 44f / 256f, 1f);
        private IcarusCloudLayer _cloudLayer;
        private IcarusStormSystem _stormSystem;
        private IcarusAmbientManager _ambientManager;

        public IcarusWorld() : base(worldIndex: 11)
        {
            EnableShadows = false;
            // Intentionally load only specified terrain tile textures for Icarus.
            Terrain.TextureMappingFiles = new Dictionary<int, string>
            {
                { 10, "TileRock04.OZJ" }
            };
            ExtraHeight = 0f;
            BackgroundMusicPath = "Music/icarus.mp3";
            AmbientSoundPath = "Sound/aHeaven.wav"; // Heaven atmosphere for Icarus
        }

        public override async Task Load()
        {
            // One fixed, batched cloud shell replaces the previous 2000-4000 particle
            // implementation. It stays visible independently from lightning flashes.
            _cloudLayer = new IcarusCloudLayer();
            Objects.Add(_cloudLayer);

            _stormSystem = new IcarusStormSystem();
            Objects.Add(_stormSystem);
            _ambientManager = new IcarusAmbientManager(this);

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(14, 12);
            Walker.Reset();
            bool shouldUseDefaultSpawn = false;
            if (MuGame.Network == null ||
                MuGame.Network.CurrentState == Core.Client.ClientConnectionState.Initial ||
                MuGame.Network.CurrentState == Core.Client.ClientConnectionState.Disconnected)
            {
                shouldUseDefaultSpawn = true;
            }
            else if (Walker.Location == Vector2.Zero)
            {
                shouldUseDefaultSpawn = true;
            }
            if (shouldUseDefaultSpawn)
            {
                Walker.Location = defaultSpawn;
            }
            Walker.MoveTargetPosition = Walker.TargetPosition;
            Walker.Position = Walker.TargetPosition;
            base.AfterLoad();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            _ambientManager?.Update(time);
        }

        public override void Dispose()
        {
            _ambientManager?.Clear();
            _ambientManager = null;
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();

            // Keep Object01-Object06 on the normal map-object path. They remain useful
            // as stable anchors for local lightning, but no longer carry the base sky.
            MapTileObjects[10] = typeof(WallObject);
        }

        public override void Draw(GameTime time)
        {
            GraphicsDevice.Clear(CLEAR_COLOR);
            base.Draw(time);
        }
    }
}

