using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Elbeland;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(51, "Elbeland")]
    public class ElvelandWorld : WalkableWorldControl
    {
        private ElbelandTornadoSystem _tornadoSystem;
        private ElbelandEagleSystem _eagleSystem;

        public ElvelandWorld() : base(worldIndex: 52)
        {
            BackgroundMusicPath = "Music/elbeland.mp3";
            Name = "Elbeland";
        }

        public override Task Load()
        {
            _tornadoSystem = new ElbelandTornadoSystem(this);
            Objects.Add(_tornadoSystem);

            // GMNewTown Type-62 eagles: MODEL_EAGLE (Object52/sos3bi01), MoveEagle
            // circular wander verbatim (PlaySpeed 0.5, Scale 0.5, Gravity=10*Scale).
            _eagleSystem = new ElbelandEagleSystem(this, maxEagles: 3);
            Objects.Add(_eagleSystem);

            return base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(61, 201);
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

        public override void Dispose()
        {
            if (_tornadoSystem != null) { Objects.Remove(_tornadoSystem); _tornadoSystem.Dispose(); _tornadoSystem = null; }
            if (_eagleSystem != null) { Objects.Remove(_eagleSystem); _eagleSystem.Dispose(); _eagleSystem = null; }
            base.Dispose();
        }
    }
}

