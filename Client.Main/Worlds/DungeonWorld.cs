using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Dungeon;
using Client.Main.Objects.Worlds.Events;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(1, "Dungeon")]
    public class DungeonWorld : WalkableWorldControl
    {
        private AmbientFlockSystem _batFlock;
        private AmbientFlockSystem _ratFlock;

        public DungeonWorld() : base(worldIndex: 2)
        {
            BackgroundMusicPath = "Music/Dungeon.mp3";
            AmbientSoundPath = "Sound/aDungeon.wav"; // Dungeon atmosphere
            Name = "Dungeon";
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(232, 126);
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

            // GOBoid fauna: bats (MODEL_BAT01, Object2/Bat01) and rats (MODEL_RAT01)
            _batFlock = new AmbientFlockSystem(this, "Object2/Bat01.bmd", 0.8f, BoidFlightStyle.FlyingLow, maxBoids: 6);
            _ratFlock = new AmbientFlockSystem(this, "Object2/Rat01.bmd", 0.55f, BoidFlightStyle.GroundScurry, maxBoids: 4);
            Objects.Add(_batFlock);
            Objects.Add(_ratFlock);

            base.AfterLoad();
        }

        public override void Dispose()
        {
            if (_batFlock != null) { Objects.Remove(_batFlock); _batFlock.Dispose(); _batFlock = null; }
            if (_ratFlock != null) { Objects.Remove(_ratFlock); _ratFlock.Dispose(); _ratFlock = null; }
            base.Dispose();
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();
            MapTileObjects[1] = typeof(SpiderWeb1Object);
            MapTileObjects[17] = typeof(SpiderWeb17Object);
            MapTileObjects[52] = typeof(RestPlaceObject);
            MapTileObjects[60] = typeof(RestPlaceObject);
        }
    }
}
