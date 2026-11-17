using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Raklion;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(57, "Raklion")]
    public class RaklionWorld : WalkableWorldControl
    {
        // Gameplay colour grade / atmosphere (World Glow, height fog).
        private protected override void ConfigureWorldGlow(Client.Main.Graphics.SceneBloom glow)
        {
            glow.Tint = new Microsoft.Xna.Framework.Vector3(0.850f, 0.920f, 1.000f);
            glow.Saturation = 0.98f;
            glow.Contrast = 1.05f;
            glow.Lift = new Microsoft.Xna.Framework.Vector3(0.010f, 0.015f, 0.030f);
            glow.Vignette = 0.22f;
        }

        public override Client.Main.Graphics.HeightFogPreset HeightFog { get; } =
            new(new Microsoft.Xna.Framework.Vector3(0.700f, 0.780f, 0.880f), 0.4f);

        private RaklionWeatherSystem _weatherSystem;

        public RaklionWorld() : base(worldIndex: 58) // RAKLION
        {

        }

        public override async Task Load()
        {
            _weatherSystem = new RaklionWeatherSystem(this, flakeCount: 80);
            Objects.Add(_weatherSystem);

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(222, 210);
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

        public override void Dispose()
        {
            if (_weatherSystem != null)
            {
                Objects.Remove(_weatherSystem);
                _weatherSystem.Dispose();
                _weatherSystem = null;
            }

            base.Dispose();
        }
    }
}
