using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Kalima;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    /// <summary>
    /// Base for all Kalima levels (WD_24HELLAS..HELLAS_7): registers the GMHellas
    /// water-shell object behaviours and the ambient Bahamut boid manager.
    /// </summary>
    public abstract class KalimaEventWorldBase : S6EventWorldBase
    {
        private KalimaAmbientManager _ambientManager;
        private static double _lastAmbientSoundMs;
        private static double _lastStoneSoundMs;

        protected KalimaEventWorldBase(short worldIndex, string name)
            : base(worldIndex, name)
        {
        }

        public override Task Load()
        {
            _ambientManager = new KalimaAmbientManager(this);
            return base.Load();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            _ambientManager?.Update(time);

            // GMHellas.cpp MoveHellasObjectSetting uses a shared four-second timer.
            double nowMs = time.TotalGameTime.TotalMilliseconds;
            if (Status == Models.GameControlStatus.Ready && Visible && nowMs - _lastAmbientSoundMs > 4000.0)
            {
                _lastAmbientSoundMs = nowMs;
                SoundController.Instance.PlayBuffer(MuGame.Random.Next(2) == 0
                    ? "Sound/aKalima01.wav"
                    : "Sound/aKalima02.wav");
            }

            if (Status == Models.GameControlStatus.Ready && Visible && nowMs - _lastStoneSoundMs > 20000.0)
            {
                _lastStoneSoundMs = nowMs;
                SoundController.Instance.PlayBuffer("Sound/aKalimaStone.wav");
            }
        }

        // SourceMain: WD_24HELLAS.. clear color (30/256, 40/256, 40/256)
        public override void Draw(GameTime time)
        {
            GraphicsDevice.Clear(new Color(30, 40, 40));
            base.Draw(time);
        }

        protected override void CreateMapTileObjects()
        {
            var kalimaDefault = typeof(KalimaObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = kalimaDefault;
        }

        public override void Dispose()
        {
            _ambientManager?.Clear();
            _ambientManager = null;
            base.Dispose();
        }
    }
}
