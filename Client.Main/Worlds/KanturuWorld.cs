using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Events;
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
        private ObjectAmbientSoundSystem _ambientSoundSystem;

        public KanturuWorld() : base(worldIndex: 38) // KANTURU (RUINS)
        {
            AmbientSoundPath = "Sound/w37/kan_ruin_global.wav";
        }

        public override Task Load()
        {
            _ambientSoundSystem = new ObjectAmbientSoundSystem(
                this,
                new Dictionary<int, string>
                {
                    [46] = "Sound/w37/kan_ruin_wheel.wav",
                    [77] = "Sound/w37/kan_ruin_waterfall.wav",
                    [92] = "Sound/w37/kan_ruin_elec.wav",
                    [98] = "Sound/w37/kan_ruin_plant.wav"
                },
                maxDistance: 900f,
                intervalMs: 2000.0);
            Objects.Add(_ambientSoundSystem);

            return base.Load();
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
