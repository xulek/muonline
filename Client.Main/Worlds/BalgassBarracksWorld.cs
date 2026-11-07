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
    [WorldInfo(41, "Balgass Barracks")]
    public class BalgassBarracksWorld : WalkableWorldControl
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

        public BalgassBarracksWorld() : base(worldIndex: 42) // BARRACKS (BALGASS BARRACKS)
        {

        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(33, 80);
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
