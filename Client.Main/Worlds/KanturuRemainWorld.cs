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
    [WorldInfo(38, "Kanturu Remain")]
    public class KanturuRemainWorld : WalkableWorldControl
    {
        private ObjectAmbientSoundSystem _ambientSoundSystem;

        public KanturuRemainWorld() : base(worldIndex: 39) // KANTURU REMAIN (RELICS)
        {
            AmbientSoundPath = "Sound/w38/kan_relic_global.wav";

            // World39 folder is missing TileRock05-07.ozj - map to existing rock textures
            Terrain.TextureMappingFiles[10] = "TileRock04.OZJ";
            Terrain.TextureMappingFiles[11] = "TileRock04.OZJ"; // Fallback for missing TileRock05
            Terrain.TextureMappingFiles[12] = "TileRock03.OZJ"; // Fallback for missing TileRock06
            Terrain.TextureMappingFiles[13] = "TileRock02.OZJ"; // Fallback for missing TileRock07
        }

        public override Task Load()
        {
            _ambientSoundSystem = new ObjectAmbientSoundSystem(
                this,
                new Dictionary<int, string>
                {
                    [9] = "Sound/w38/kan_relic_gear.wav",
                    [31] = "Sound/w38/kan_relic_incubator.wav",
                    [35] = "Sound/w38/kan_relic_incubator.wav",
                    [36] = "Sound/w38/kan_relic_incubator.wav",
                    [37] = "Sound/w38/kan_relic_incubator.wav"
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
            Vector2 defaultSpawn = new Vector2(72, 105);
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
