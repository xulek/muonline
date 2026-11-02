using Client.Main.Configuration;
using Client.Data.ATT;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects;
using Client.Main.Objects.Worlds.Devias;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Worlds
{
    [WorldInfo(2, "Devias")]
    public class DeviasWorld : WalkableWorldControl
    {
        private DeviasSnowAmbientEffect _snowEffect;
        private bool _insideBuilding;
        private bool _churchMusic;
        public DeviasWorld() : base(worldIndex: 3)
        {
            BackgroundMusicPath = "Music/Devias.mp3";
            AmbientSoundPath = "Sound/aWind.wav"; // Wind ambient for open spaces
            Name = "Devias";
        }

        public override async Task Load()
        {
            var snowSettings = MuGame.AppSettings?.Environment?.DeviasSnow;
            if (snowSettings?.Enabled != false)
            {
                _snowEffect = new DeviasSnowAmbientEffect(this, snowSettings ?? new DeviasSnowEffectSettings());
                Objects.Add(_snowEffect);
            }

            await base.Load();
        }

        public override void AfterLoad()
        {
            Vector2 defaultSpawn = new Vector2(219, 24);

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

            Terrain.ConfigureGrass();
            Terrain.ConfigureDeviasSnow(MuGame.AppSettings?.Environment?.DeviasGroundSnow ?? new DeviasGroundSnowSettings());

            base.AfterLoad();
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();

            MapTileObjects[0] = typeof(TreeObject);
            MapTileObjects[75] = typeof(TreeObject);

            MapTileObjects[19] = typeof(AuroraObject);
            MapTileObjects[20] = typeof(SteelDoorObject);

            MapTileObjects[30] = typeof(DeviasObject);
            MapTileObjects[66] = typeof(DeviasObject);
            MapTileObjects[54] = typeof(DeviasObject);
            MapTileObjects[56] = typeof(DeviasObject);
            MapTileObjects[92] = typeof(DeviasObject);
            MapTileObjects[93] = typeof(DeviasObject);
            MapTileObjects[100] = typeof(DeviasObject);

            MapTileObjects[65] = typeof(SteelDoorObject);
            MapTileObjects[67] = typeof(MapTileObject);
            MapTileObjects[68] = typeof(MapTileObject);

            MapTileObjects[86] = typeof(SteelDoorObject); // Sliding doors

            for (var i = 76; i < 79; i++)
                MapTileObjects[i] = typeof(HouseWallObject); // Wall

            for (var i = 81; i < 83; i++)
                MapTileObjects[i] = typeof(HouseWallObject); // Roof

            for (var i = 83; i < 86; i++)
                MapTileObjects[i] = typeof(FlagObject);

            for (var i = 88; i < 89; i++)
                MapTileObjects[i] = typeof(SteelDoorObject);

            MapTileObjects[91] = typeof(RestPlaceObject);

            MapTileObjects[98] = typeof(HouseWallObject); // Roof
            MapTileObjects[99] = typeof(HouseWallObject); // Roof
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (Status == Models.GameControlStatus.Ready)
            {
                if (Visible)
                {
                    bool insideBuilding = HeroTile == 3 || HeroTile >= 10;
                    if (_insideBuilding != insideBuilding)
                    {
                        _insideBuilding = insideBuilding;
                        if (insideBuilding)
                            SoundController.Instance.StopAmbientSound();
                        else
                            SoundController.Instance.PlayAmbientSound(AmbientSoundPath);
                    }

                    if (Walker != null)
                    {
                        int x = (int)Walker.Location.X;
                        int y = (int)Walker.Location.Y;
                        if (Terrain.RequestTerrainFlag(x, y).HasFlag(TWFlags.SafeZone))
                        {
                            bool churchMusic = x >= 205 && x <= 214 && y >= 13 && y <= 31;
                            if (_churchMusic != churchMusic)
                            {
                                _churchMusic = churchMusic;
                                SoundController.Instance.PlayBackgroundMusic(churchMusic ? "Music/Church.mp3" : BackgroundMusicPath);
                            }
                        }
                    }
                }
                Terrain.UpdateDeviasSnow(time, this);
            }
        }

        public override void Dispose()
        {
            if (_snowEffect != null)
            {
                Objects.Remove(_snowEffect);
                _snowEffect.Dispose();
                _snowEffect = null;
            }

            base.Dispose();
        }
    }
}
