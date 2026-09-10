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
    [WorldInfo(81, "Karutan 2")]
    public class Karutan2World : WalkableWorldControl
    {
        private ScrollingSmokeOverlay _sandOverlay;

        public Karutan2World() : base(worldIndex: 82) // KARUTAN 2
        {

        }

        public override async Task Load()
        {
            // SourceMain RenderOutSides WD_80/81KARUTAN: single sand02.jpg layer,
            // tint 0.3/0.3/0.25, fastest scroll (WorldTime%100000 * 0.004f), tile 3x2
            _sandOverlay = new ScrollingSmokeOverlay(
                new ScrollingSmokeOverlay.Layer
                {
                    TexturePath = "Object9/sand02.jpg",
                    Tint = new Color(0.3f, 0.3f, 0.25f),
                    USpeed = 0.004f,
                    TileU = 3f,
                    TileV = 2f,
                    Blend = ScrollingSmokeOverlay.LayerBlend.Additive
                });
            await _sandOverlay.Load();

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(40, 88);
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

        public override void DrawAfter(GameTime time)
        {
            base.DrawAfter(time);
            _sandOverlay?.DrawOverlay(time);
        }

        public override void Dispose()
        {
            _sandOverlay?.Dispose();
            _sandOverlay = null;
            base.Dispose();
        }
    }
}

