using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Tarkan;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(33, "Aida")]
    public class AidaWorld : WalkableWorldControl
    {
        // Gameplay colour grade / atmosphere (World Glow, height fog).
        private protected override void ConfigureWorldGlow(Client.Main.Graphics.SceneBloom glow)
        {
            glow.Tint = new Microsoft.Xna.Framework.Vector3(0.900f, 1.000f, 0.850f);
            glow.Saturation = 1.06f;
            glow.Contrast = 1.04f;
            glow.Lift = new Microsoft.Xna.Framework.Vector3(0.000f, 0.012f, 0.000f);
            glow.Vignette = 0.22f;
        }

        public override Client.Main.Graphics.HeightFogPreset HeightFog { get; } =
            new(new Microsoft.Xna.Framework.Vector3(0.350f, 0.450f, 0.300f), 0.38f);

        private TarkanBoidManager _bugManager;

        public AidaWorld() : base(worldIndex: 34) // AIDA
        {
            Name = "Aida";
            BackgroundMusicPath = "Music/Aida.mp3";
        }

        // Aida shares Tarkan's energy-tail crawler fauna (GOBoid.cpp MODEL_BUG01+1, Bug02.bmd)

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(85, 10);
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

            _bugManager = new TarkanBoidManager(this);

            base.AfterLoad();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            _bugManager?.Update(time);
        }

        public override void Dispose()
        {
            _bugManager?.Clear();
            _bugManager = null;
            base.Dispose();
        }
    }
}
