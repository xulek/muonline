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
    [WorldInfo(65, "Doppelganger Ice Zone")]
    public class DoppelgangerIceWorld : WalkableWorldControl
    {
        private RaklionWeatherSystem _weatherSystem;

        public DoppelgangerIceWorld() : base(worldIndex: 66) // DOPPELGANGER ICEZONE (SNOW)
        {

        }

        // SourceMain: IsIceCity() covers WD_65DOPPLEGANGER1 — same Raklion snow + RenderBaseSmoke
        public override Task Load()
        {
            _weatherSystem = new RaklionWeatherSystem(this, flakeCount: 80, flakeTextureBase: "World66");
            Objects.Add(_weatherSystem);

            return base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(196, 28);
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
