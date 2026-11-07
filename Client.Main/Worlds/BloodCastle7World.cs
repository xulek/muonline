using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.BloodCastle;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(17, "Blood Castle 7")]
    public class BloodCastle7World : WalkableWorldControl
    {
        public BloodCastle7World() : base(worldIndex: 12) // All BC1-7 use World12
        {
            Name = "Blood Castle 7";
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();
            RegisterBloodCastleObjects();
        }

        private void RegisterBloodCastleObjects()
        {
            MapTileObjects[9] = typeof(BloodCastleObject);
            MapTileObjects[10] = typeof(BloodCastleObject);
            MapTileObjects[11] = typeof(BloodCastleObject);
            MapTileObjects[13] = typeof(BloodCastleObject);
            MapTileObjects[28] = typeof(BloodCastleObject);
            MapTileObjects[29] = typeof(BloodCastleObject);
            MapTileObjects[36] = typeof(BloodCastleObject);
            MapTileObjects[37] = typeof(BloodCastleObject);
        }

        public override async Task Load()
        {
            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(13, 9);

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

            BloodCastleObject.AttachAmbientEffect(this);
            base.AfterLoad();
        }
    }
}
