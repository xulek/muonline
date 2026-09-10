using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(67, "Doppelganger Underwater")]
    public class DoppelgangerUnderwaterWorld : WalkableWorldControl
    {
        public DoppelgangerUnderwaterWorld() : base(worldIndex: 68) // DOPPELGANGER UNDERWATER (SEA)
        {

        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(110, 58);
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

            // SourceMain: Atlans/DoppelGanger3 share the animated-water path
            // (WaterMove default %20000 * 0.00005 -> 0.05 UV/s) with wind-driven wobble.
            Terrain.WaterSpeed = 0.05f;
            Terrain.DistortionAmplitude = 0.25f;
            Terrain.DistortionFrequency = 1.0f;
            
            base.AfterLoad();
        }
    }
}

