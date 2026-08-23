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
    [WorldInfo(72, "Imperial Guardian")]
    public class ImperialGuardian1World : WalkableWorldControl
    {
        private GuardianWeatherSystem _weatherSystem;

        public ImperialGuardian1World() : base(worldIndex: 73) // IMPERIAL GUARDIAN (GAION)
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
            Vector2 defaultSpawn = new Vector2(93, 67);
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

