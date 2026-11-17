using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Tarkan;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(8, "Tarkan")]
    public class TarkanWorld : WalkableWorldControl
    {
        // Gameplay colour grade / atmosphere (World Glow, height fog).
        private protected override void ConfigureWorldGlow(Client.Main.Graphics.SceneBloom glow)
        {
            glow.Tint = new Microsoft.Xna.Framework.Vector3(1.000f, 0.850f, 0.650f);
            glow.Saturation = 1.02f;
            glow.Contrast = 1.06f;
            glow.Lift = new Microsoft.Xna.Framework.Vector3(0.025f, 0.015f, 0.000f);
            glow.Vignette = 0.24f;
        }

        public override Client.Main.Graphics.HeightFogPreset HeightFog { get; } =
            new(new Microsoft.Xna.Framework.Vector3(0.600f, 0.450f, 0.300f), 0.45f);

        public TarkanWorld() : base(worldIndex: 9) // TARKAN
        {
            Name = "Tarkan";
            BackgroundMusicPath = "Music/tarkan.mp3";
            AmbientSoundPath = "Sound/desert.wav";
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

