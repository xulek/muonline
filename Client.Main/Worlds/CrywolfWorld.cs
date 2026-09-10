using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Crywolf;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(34, "Crywolf")]
    internal class CrywolfWorld : WalkableWorldControl
    {
        private CrywolfWeatherSystem _weatherSystem;
        private Client.Main.Objects.Worlds.Events.AmbientFlockSystem _scorpionFlock;

        public CrywolfWorld() : base(worldIndex: 35) // CRYWOLF
        {

        }

        public override async Task Load()
        {
            _weatherSystem = new CrywolfWeatherSystem(this);
            Objects.Add(_weatherSystem);

            // GOBoid fauna: scorpions outside safe zones (MODEL_SCOLPION, Object35/scorpion)
            _scorpionFlock = new Client.Main.Objects.Worlds.Events.AmbientFlockSystem(
                this, "Object35/scorpion.bmd", scale: 0.95f, Client.Main.Objects.Worlds.Events.BoidFlightStyle.GroundScurry, maxBoids: 4);
            Objects.Add(_scorpionFlock);

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(121, 25);
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

            if (_scorpionFlock != null)
            {
                Objects.Remove(_scorpionFlock);
                _scorpionFlock.Dispose();
                _scorpionFlock = null;
            }

            base.Dispose();
        }
    }
}


