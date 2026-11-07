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
    [WorldInfo(39, "Refine Tower")]
    public class RefineTowerWorld : WalkableWorldControl
    {
        private ObjectAmbientSoundSystem _ambientSoundSystem;

        public RefineTowerWorld() : base(worldIndex: 40) // REFINE TOWER
        {
            AmbientSoundPath = "Sound/w39/kan_boss_global.wav";
        }

        public override Task Load()
        {
            _ambientSoundSystem = new ObjectAmbientSoundSystem(
                this,
                new Dictionary<int, string>
                {
                    [25] = "Sound/w39/kan_boss_incubator.wav",
                    [40] = "Sound/w39/kan_boss_crystal.wav",
                    [41] = "Sound/w39/kan_boss_crystal.wav",
                    [42] = "Sound/w39/kan_boss_crystal.wav",
                    [71] = "Sound/w39/kan_boss_gear.wav",
                    [73] = "Sound/w39/kan_boss_field.wav"
                },
                maxDistance: 1000f,
                intervalMs: 2000.0);
            Objects.Add(_ambientSoundSystem);

            return base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(76, 151);
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
