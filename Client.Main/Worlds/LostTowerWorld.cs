using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Events;
using Client.Main.Objects.Worlds.LostTower;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(4, "Lost Tower")]
    public class LostTowerWorld : WalkableWorldControl
    {
        // Gameplay colour grade / atmosphere (World Glow, height fog).
        private protected override void ConfigureWorldGlow(Client.Main.Graphics.SceneBloom glow)
        {
            glow.Tint = new Microsoft.Xna.Framework.Vector3(0.850f, 0.850f, 1.000f);
            glow.Saturation = 0.95f;
            glow.Contrast = 1.08f;
            glow.Lift = new Microsoft.Xna.Framework.Vector3(0.000f, 0.000f, 0.020f);
            glow.Vignette = 0.3f;
        }

        public override Client.Main.Graphics.HeightFogPreset HeightFog { get; } =
            new(new Microsoft.Xna.Framework.Vector3(0.140f, 0.110f, 0.200f), 0.42f, Falloff: 1f / 110f);

        private AmbientFlockSystem _batFlock;
        public LostTowerWorld() : base(worldIndex: 5)
        {
            BackgroundMusicPath = "Music/lost_tower_a.mp3";
            AmbientSoundPath = "Sound/aTower.wav"; // Tower atmosphere
            Name = "Lost Tower";
            // SourceMain5.2 lights the map with the terrain light map only (no extra ambient): darker, like the original.
            Terrain.AmbientLight = 0f;
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(208, 81);
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

            // GOBoid fauna: Lost Tower hosts bats (MODEL_BAT01)
            _batFlock = new AmbientFlockSystem(this, "Object2/Bat01.bmd", 0.8f, BoidFlightStyle.FlyingLow, maxBoids: 5);
            Objects.Add(_batFlock);

            base.AfterLoad();
        }

        public override void Dispose()
        {
            if (_batFlock != null) { Objects.Remove(_batFlock); _batFlock.Dispose(); _batFlock = null; }
            base.Dispose();
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();

            // SourceMain5.2 uses these Lost Tower object types for animated
            // blend meshes, moving UVs, hidden meshes and object effects.
            MapTileObjects[3] = typeof(LostTowerObject);  // source type 3 -> Object04.bmd
            MapTileObjects[4] = typeof(LostTowerObject);  // source type 4 -> Object05.bmd
            MapTileObjects[18] = typeof(LightBeamObject); // source type 18 -> Object19.bmd
            MapTileObjects[19] = typeof(LostTowerObject); // source type 19 -> Object20.bmd
            MapTileObjects[20] = typeof(LostTowerObject); // source type 20 -> Object21.bmd
            MapTileObjects[23] = typeof(LostTowerObject); // source type 23 -> Object24.bmd
            MapTileObjects[24] = typeof(LostTowerObject); // source type 24 -> Object25.bmd
            MapTileObjects[25] = typeof(LostTowerObject); // source type 25 -> Object26.bmd
            MapTileObjects[38] = typeof(LostTowerObject); // source type 38 -> Object39.bmd
            MapTileObjects[39] = typeof(LostTowerObject); // source type 39 -> Object40.bmd
            MapTileObjects[40] = typeof(LostTowerObject); // source type 40 -> Object41.bmd
        }
    }
}

