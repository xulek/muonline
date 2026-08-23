using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.LandOfTrials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(31, "Land of Trials")]
    public class LandOfTrialsWorld : WalkableWorldControl
    {
        private LandOfTrialsMistSystem _mistSystem;

        public LandOfTrialsWorld() : base(worldIndex: 32) // LAND OF TRIALS
        {

        }

        public override async Task Load()
        {
            _mistSystem = new LandOfTrialsMistSystem(this);
            Objects.Add(_mistSystem);

            await base.Load();
        }

        protected override void CreateMapTileObjects()
        {
            var landOfTrialsDefault = typeof(LandOfTrialsObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = landOfTrialsDefault;
        }

        public override void Dispose()
        {
            if (_mistSystem != null)
            {
                Objects.Remove(_mistSystem);
                _mistSystem.Dispose();
                _mistSystem = null;
            }

            base.Dispose();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = Walker.Location = new Vector2(60, 20);
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
    }
}
