using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.DevilSquare;
using Client.Main.Objects.Worlds.Events;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(9, "Devil Square")]
    public class DevilSquare1World : S6EventWorldBase
    {
        private EventRainSystem _rainSystem;

        public DevilSquare1World() : base(worldIndex: 10, name: "Devil Square") // WD_9DEVILSQUARE
        {
            AmbientSoundPath = "Sound/aRain.wav";
        }

        public override async Task Load()
        {
            _rainSystem = new EventRainSystem(
                this,
                maxDrops: 200,
                speedBonus: 0f,
                streakLength: 20f,
                spawnSplashes: true,
                lightningFlicker: true);
            Objects.Add(_rainSystem);

            await base.Load();
        }

        protected override void CreateMapTileObjects()
        {
            var devilSquareDefault = typeof(DevilSquareObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = devilSquareDefault;
        }

        public override void Dispose()
        {
            if (_rainSystem != null)
            {
                Objects.Remove(_rainSystem);
                _rainSystem.Dispose();
                _rainSystem = null;
            }

            base.Dispose();
        }
    }
}
