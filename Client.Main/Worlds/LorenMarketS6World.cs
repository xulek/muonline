using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    [WorldInfo(79, "Loren Market")]
    public class LorenMarketS6World : S6EventWorldBase
    {
        private const string RainSound = "Sound/aRain.wav";

        public LorenMarketS6World() : base(worldIndex: 80, name: "Loren Market")
        {
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (Walker != null)
                SoundController.Instance.PlayBufferWithAttenuation(RainSound, Walker.Position, Walker.Position, loop: true);
        }

        public override void Dispose()
        {
            SoundController.Instance.StopManagedLoop(RainSound);
            base.Dispose();
        }
    }
}
