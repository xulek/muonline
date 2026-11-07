using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    /// <summary>
    /// Lugard NPC - Quest/merchant NPC.
    /// </summary>
    [NpcInfo(540, "Lugard")]
    public class Lugard : NPCObject
    {
        private double _nextBreathMs;

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"NPC/Lugard.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible || CurrentAction != (int)MonsterActionType.Stop1 ||
                World is not WalkableWorldControl world || world.Walker == null)
                return;

            double now = gameTime.TotalGameTime.TotalMilliseconds;
            if (now < _nextBreathMs)
                return;

            _nextBreathMs = now + 8000;
            if (MuGame.Random.Next(2) == 0)
                SoundController.Instance.PlayBufferWithAttenuation("Sound/Doppelganger/Lugard.wav", Position, world.Walker.Position);
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
