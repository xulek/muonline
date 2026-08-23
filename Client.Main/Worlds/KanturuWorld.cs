using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Kanturu;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(37, "Kanturu")]
    public class KanturuWorld : WalkableWorldControl
    {
        public KanturuWorld() : base(worldIndex: 38) // KANTURU (RUINS)
        {

        }

        protected override void CreateMapTileObjects()
        {
            var kanturuDefault = typeof(KanturuObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = kanturuDefault;
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(20, 217);
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
