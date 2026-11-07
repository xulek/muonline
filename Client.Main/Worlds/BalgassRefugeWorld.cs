using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Events;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(42, "Balgass Refuge")]
    public class BalgassRefugeWorld : WalkableWorldControl
    {
        private static readonly IReadOnlyDictionary<int, string> AmbientSoundsByType =
            new Dictionary<int, string>
            {
                [74] = "Sound/w42/cage01.wav",
                [75] = "Sound/w42/cage02.wav",
                [79] = "Sound/w42/volcano.wav",
                [92] = "Sound/w42/firepillar.wav"
            };

        private ObjectAmbientSoundSystem _ambientSoundSystem;

        public BalgassRefugeWorld() : base(worldIndex: 43) // REFUGE (BALGASS RESTING PLACE)
        {

        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(98, 185);
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

            // SourceMain WaterMove: WD_42CHANGEUP3RD_2ND = (WorldTime % 50000) * 0.00002f -> 0.02 UV/s (slowest)
            Terrain.WaterSpeed = 0.02f;

            _ambientSoundSystem = new ObjectAmbientSoundSystem(this, AmbientSoundsByType, maxDistance: 900f);
            Objects.Add(_ambientSoundSystem);

            base.AfterLoad();
        }

        public override void Dispose()
        {
            if (_ambientSoundSystem != null)
            {
                Objects.Remove(_ambientSoundSystem);
                _ambientSoundSystem.Dispose();
                _ambientSoundSystem = null;
            }

            base.Dispose();
        }
    }
}
