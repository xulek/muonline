using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.ImperialGuardian;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(70, "Imperial Guardian 3")]
    public class ImperialGuardian3World : WalkableWorldControl
    {
        private GuardianWeatherSystem _weatherSystem;

        public ImperialGuardian3World() : base(worldIndex: 71) // IMPERIAL GUARDIAN (GAION)
        {

        }

        public override Task Load()
        {
            _weatherSystem = new GuardianWeatherSystem(this);
            Objects.Add(_weatherSystem);

            return base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(86, 66);
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

