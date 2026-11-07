using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Worlds.Elbeland;
using Client.Main.Objects.Worlds.Events;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(51, "Elbeland")]
    public class ElvelandWorld : WalkableWorldControl
    {
        private ElbelandTornadoSystem _tornadoSystem;
        private ElbelandEagleSystem _eagleSystem;
        private ObjectAmbientSoundSystem _ambientSoundSystem;

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

            _ambientSoundSystem = new ObjectAmbientSoundSystem(
                this,
                new Dictionary<int, string>
                {
                    [2] = "Sound/w52/SE_Obj_watersmall01.wav",
                    [53] = "Sound/w52/SE_Amb_ravine01.wav",
                    [56] = "Sound/w52/SE_Amb_enteratlance01.wav",
                    [59] = "Sound/w52/SE_Obj_waterfallsmall01.wav",
                    [85] = "Sound/w52/SE_Obj_enterdevias01.wav",
                    [89] = "Sound/w52/SE_Obj_waterway01.wav",
                    [110] = "Sound/w52/SE_Obj_villageprotection01.wav"
                },
                maxDistance: 900f,
                intervalMs: 2500.0,
                safeZoneSuppressedTypes: new HashSet<int> { 2, 53, 59, 89 });
            Objects.Add(_ambientSoundSystem);

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
            if (_ambientSoundSystem != null) { Objects.Remove(_ambientSoundSystem); _ambientSoundSystem.Dispose(); _ambientSoundSystem = null; }
            base.Dispose();
        }
    }
}

