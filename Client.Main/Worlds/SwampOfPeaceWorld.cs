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
    [WorldInfo(56, "Swamp of Peace")]
    public class SwampOfPeaceWorld : WalkableWorldControl
    {
        private ScrollingSmokeOverlay _smokeOverlay;

        public SwampOfPeaceWorld() : base(worldIndex: 57) // SWAMP OF PEACE (CALMNESS)
        {

        }

        public override async Task Load()
        {
            // GMSwampOfQuiet::RenderBaseSmoke — unconditional two-layer haze, tint 0.4/0.4/0.45
            _smokeOverlay = ScrollingSmokeOverlay.CreateStandard(new Color(0.4f, 0.4f, 0.45f));
            await _smokeOverlay.Load();

            await base.Load();
        }

        public override void DrawAfter(GameTime time)
        {
            base.DrawAfter(time);
            _smokeOverlay?.DrawOverlay(time);
        }

        public override void Dispose()
        {
            _smokeOverlay?.Dispose();
            _smokeOverlay = null;
            base.Dispose();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(140, 108);
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
    }
}
