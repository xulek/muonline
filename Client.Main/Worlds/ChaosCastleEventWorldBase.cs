using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Objects.Worlds.ChaosCastle;
using Client.Main.Objects.Worlds.Events;

namespace Client.Main.Worlds
{
    /// <summary>
    /// Base for all Chaos Castle levels (WD_18CHAOS_CASTLE..): registers the
    /// CSChaosCastle object behaviours and the tumbling event rain
    /// (CreateChaosCastleRain: 80 drops, +20 fall-speed bonus, ~30-40u streaks,
    /// no splashes over NoGround tiles).
    /// </summary>
    public abstract class ChaosCastleEventWorldBase : S6EventWorldBase
    {
        private EventRainSystem _rainSystem;

        protected ChaosCastleEventWorldBase(short worldIndex, string name)
            : base(worldIndex, name)
        {
        }

        public override Task Load()
        {
            _rainSystem = new EventRainSystem(
                this,
                maxDrops: 80,
                speedBonus: 20f,
                streakLength: 34f,
                spawnSplashes: true,
                lightningFlicker: false);
            Objects.Add(_rainSystem);

            return base.Load();
        }

        protected override void CreateMapTileObjects()
        {
            var chaosCastleDefault = typeof(ChaosCastleObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = chaosCastleDefault;
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
