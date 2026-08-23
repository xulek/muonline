using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Events;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(30, "Valley of Loren")]
    public class ValleyOfLorenWorld : WalkableWorldControl
    {
        /// <summary>SourceMain draws siege haze/embers only while IsBattleCastleStart(); the server toggles this.</summary>
        public static bool CastleSiegeActive = true;

        private ScrollingSmokeOverlay _smokeOverlay;
        private FireSnuffEmberSystem _emberSystem;

        public ValleyOfLorenWorld() : base(worldIndex: 31) // VALLEY OF LOREN
        {

        }

        public override async Task Load()
        {
            if (CastleSiegeActive)
            {
                // GMBattleCastle::RenderBaseSmoke — tint 0.3/0.3/0.25 during the siege
                _smokeOverlay = ScrollingSmokeOverlay.CreateStandard(new Color(0.3f, 0.3f, 0.25f));
                await _smokeOverlay.Load();

                _emberSystem = new FireSnuffEmberSystem(this, maxEmbers: 40, scaleBias: 0.5f);
            }

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(94, 230);
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

            if (_emberSystem != null)
                Objects.Add(_emberSystem);

            // SourceMain: TerrainGrassEnable=false and terrain light direction (0.5,-1,1) for BattleCastle
            Terrain.ConfigureGrass(0f, 255);

            base.AfterLoad();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            // SourceMain InBattleCastle2: linear black fog while inside the castle zone
            // x in [16100,19000], y in [18900,21700], GL_FOG start/end 2000/2700
            if (!CastleSiegeActive)
                return;

            var cameraPos = Camera.Instance.Position;
            bool inSiegeZone =
                cameraPos.X >= 16100f && cameraPos.X <= 19000f &&
                cameraPos.Y >= 18900f && cameraPos.Y <= 21700f;

            Graphics.WorldFog.Enabled = inSiegeZone;
        }

        public override void DrawAfter(GameTime time)
        {
            base.DrawAfter(time);
            _smokeOverlay?.DrawOverlay(time);
        }

        public override void Dispose()
        {
            Graphics.WorldFog.Disable();

            _smokeOverlay?.Dispose();
            _smokeOverlay = null;

            if (_emberSystem != null)
            {
                Objects.Remove(_emberSystem);
                _emberSystem.Dispose();
                _emberSystem = null;
            }

            base.Dispose();
        }

    }
}

