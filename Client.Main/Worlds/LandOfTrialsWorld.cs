using Client.Main.Controls;
using Client.Main.Controllers;
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
        private double _ambientElapsedSeconds;

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

        public override void Update(GameTime time)
        {
            base.Update(time);
            _ambientElapsedSeconds += time.ElapsedGameTime.TotalSeconds;
            if (_ambientElapsedSeconds >= 300.0)
            {
                _ambientElapsedSeconds = 0;
                SoundController.Instance.PlayBuffer("Sound/w31/aW31.wav");
            }
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
