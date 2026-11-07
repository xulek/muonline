using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(479, "Gatekeeper Titus")]
    public class Titus : NPCObject
    {
        private double _nextIdleSoundMs;

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("NPC/titus.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible || CurrentAction != (int)MonsterActionType.Stop1 ||
                World is not WalkableWorldControl world || world.Walker == null)
                return;

            double now = gameTime.TotalGameTime.TotalMilliseconds;
            if (now < _nextIdleSoundMs)
                return;

            _nextIdleSoundMs = now + 8000;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/w64/GatekeeperTitus.wav", Position, world.Walker.Position);
        }

        protected override void HandleClick()
        {
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendTalkToNpcRequestAsync(NetworkId);
            }
        }
    }
}
