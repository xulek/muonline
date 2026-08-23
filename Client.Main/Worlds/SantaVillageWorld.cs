using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.SantaVillage;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(62, "Santa Village")]
    public class SantaVillageWorld : WalkableWorldControl
    {
        private SantaSnowSystem _snowSystem;

        public SantaVillageWorld() : base(worldIndex: 63) // SANTATOWN (SANTA VILLAGE)
        {
        }

        public override async Task Load()
        {
            _snowSystem = new SantaSnowSystem(this, flakeCount: 60);
            Objects.Add(_snowSystem);

            await base.Load();
        }

        protected override void CreateMapTileObjects()
        {
            var santaVillageDefault = typeof(SantaVillageObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = santaVillageDefault;
        }


        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(220, 30);
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
            if (_snowSystem != null)
            {
                Objects.Remove(_snowSystem);
                _snowSystem.Dispose();
                _snowSystem = null;
            }

            base.Dispose();
        }
    }
}
