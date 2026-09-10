using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.DuelArena;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(64, "Duel Arena")]
    public class DuelArenaWorld : WalkableWorldControl
    {
        public DuelArenaWorld() : base(worldIndex: 65) // DUELARENA
        {

        }

        protected override void CreateMapTileObjects()
        {
            var duelArenaDefault = typeof(DuelArenaObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = duelArenaDefault;
        }


        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(100, 120);
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
