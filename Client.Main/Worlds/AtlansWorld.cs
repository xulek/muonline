using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Atlans;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(7, "Atlans")]
    public class AtlansWorld : WalkableWorldControl
    {
        private BoidManager _boidManager;
        private Objects.Worlds.Noria.NoriaLeafAmbientEffect _leafEffect;

        public AtlansWorld() : base(worldIndex: 8)
        {
            BackgroundMusicPath = "Music/atlans.mp3";
            Name = "Atlans";
        }
        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(20, 20);

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

            // SourceMain: Atlans/DoppelGanger3 share the animated-water path
            // (WaterMove default %20000 * 0.00005 -> 0.05 UV/s) with wind-driven wobble.
            Terrain.WaterSpeed = 0.05f;
            Terrain.DistortionAmplitude = 0.25f;
            Terrain.DistortionFrequency = 1.0f;

            // Initialize fish boid system for underwater areas
            _boidManager = new BoidManager(this);
            _leafEffect = new Objects.Worlds.Noria.NoriaLeafAmbientEffect(this, new Client.Main.Configuration.NoriaLeafEffectSettings());
            if (_leafEffect != null)
                Objects.Add(_leafEffect);

            base.AfterLoad();
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();

            var waterPlantIndices = new[] { 5, 6, 24, 25, 26, 27, 31, 33 };
            foreach (var index in waterPlantIndices)
            {
                MapTileObjects[index] = typeof(WaterPlantObject);
            }

            var gateIndices = new[] { 32, 34 };
            foreach (var index in gateIndices)
            {
                MapTileObjects[index] = typeof(GateObject);
            }

            MapTileObjects[22] = typeof(BubblesObject);
            MapTileObjects[23] = typeof(WaterPortalObject);
            MapTileObjects[38] = typeof(LightBeamObject);
            MapTileObjects[39] = typeof(RestPlaceObject);
            MapTileObjects[40] = typeof(PortalObject);
        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            // Update fish boid system
            _boidManager?.Update(time);
        }

        public override void Dispose()
        {
            // Clean up fish before disposing world
            _boidManager?.Clear();
            _boidManager = null;

            base.Dispose();
        }
    }
}






