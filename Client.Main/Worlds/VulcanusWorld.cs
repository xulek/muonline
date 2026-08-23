using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Events;
using Client.Main.Objects.Worlds.Vulcanus;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(63, "Vulcanus")]
    public class VulcanusWorld : WalkableWorldControl
    {
        private FireSnuffEmberSystem _emberSystem;

        public VulcanusWorld() : base(worldIndex: 64) // VULCANUS
        {

        }

        public override async Task Load()
        {
            // CGM_PK_Field::CreateFireSpark — same ember weather as Doppelganger2
            _emberSystem = new FireSnuffEmberSystem(this, maxEmbers: 40, scaleBias: 0.4f);
            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(170, 185);
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

            Objects.Add(_emberSystem);

            base.AfterLoad();
        }

        protected override void CreateMapTileObjects()
        {
            var vulcanusDefault = typeof(VulcanusObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = vulcanusDefault;
        }

        public override void Dispose()
        {
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
