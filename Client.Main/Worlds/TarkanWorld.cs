using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Tarkan;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(8, "Tarkan")]
    public class TarkanWorld : WalkableWorldControl
    {
        public TarkanWorld() : base(worldIndex: 9) // TARKAN
        {
            Name = "Tarkan";
            BackgroundMusicPath = "Music/tarkan.mp3";
            AmbientSoundPath = "Sound/aDesert.wav";
        }

        private TarkanSandstormOverlay _sandstormOverlay;
        private TarkanBoidManager _boidManager;

        public override async Task Load()
        {
            _sandstormOverlay = new TarkanSandstormOverlay();
            await _sandstormOverlay.Load();

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(200, 58);
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

            _boidManager = new TarkanBoidManager(this);

            // SourceMain WaterMove: WD_8TARKAN = (WorldTime % 40000) * 0.000025f -> 0.025 UV/s
            Terrain.WaterSpeed = 0.025f;

            base.AfterLoad();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            _boidManager?.Update(time);
        }

        public override void DrawAfter(GameTime time)
        {
            base.DrawAfter(time);
            _sandstormOverlay?.DrawOverlay(time);
        }

        public override void Dispose()
        {
            _sandstormOverlay?.Dispose();
            _sandstormOverlay = null;
            _boidManager?.Clear();
            _boidManager = null;
            base.Dispose();
        }

        protected override void CreateMapTileObjects()
        {
            var tarkanDefault = typeof(TarkanObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = tarkanDefault;
        }
    }
}

