using System.Collections.Generic;

namespace Client.Main.Core.Utilities
{
    /// <summary>
    /// Default background music and looping ambient sound per map number.
    /// Used by <see cref="Controls.WorldControl"/> when a world class does not set its own paths.
    /// Paths are relative to the client data folder.
    /// </summary>
    /// <remarks>
    /// Seasons 1-6 maps follow SourceMain5.2: music from <c>g_lpszMp3</c> / per-map <c>PlayMp3</c> calls
    /// (ZzzScene.cpp and the GM*.cpp map modules), ambient loops from the per-map <c>PlayBuffer</c> switch
    /// in ZzzScene.cpp. Later maps are matched by track name in the Season 20 data. State-dependent music
    /// (Crywolf phases, Kanturu tower, Illusion Temple lobby/match, castle siege) uses the idle track here;
    /// the event modules switch tracks at runtime.
    /// </remarks>
    public static class WorldAudioDatabase
    {
        private static readonly Dictionary<ushort, string> Music = new()
        {
            { 0, "Music/main_theme.mp3" },           // Lorencia (outside the pub)
            { 1, "Music/Dungeon.mp3" },              // Dungeon
            { 2, "Music/Devias.mp3" },               // Devias
            { 3, "Music/Noria.mp3" },                // Noria
            { 4, "Music/lost_tower_a.mp3" },         // Lost Tower
            { 7, "Music/atlans.mp3" },               // Atlans
            { 8, "Music/tarkan.mp3" },               // Tarkan
            { 10, "Music/icarus.mp3" },              // Icarus
            { 24, "Music/kalima.mp3" },              // Kalima 1-6, 7
            { 25, "Music/kalima.mp3" },
            { 26, "Music/kalima.mp3" },
            { 27, "Music/kalima.mp3" },
            { 28, "Music/kalima.mp3" },
            { 29, "Music/kalima.mp3" },
            { 36, "Music/kalima.mp3" },
            { 30, "Music/castle.mp3" },              // Valley of Loren (castle, peace time)
            { 31, "Music/huntingground.mp3" },       // Land of Trials
            { 33, "Music/Aida.mp3" },                // Aida
            { 34, "Music/crywolf1st.mp3" },          // Crywolf (peace time)
            { 37, "Music/kanturu_1st.mp3" },         // Kanturu Ruins
            { 38, "Music/kanturu_2nd.mp3" },         // Kanturu Relics
            { 39, "Music/KanturuTower.mp3" },        // Refine Tower
            { 41, "Music/BalgasBarrack.mp3" },       // Balgass Barracks
            { 42, "Music/BalgasRefuge.mp3" },        // Balgass Refuge
            { 45, "Music/cursedtemplewait.mp3" },    // Illusion Temple 1-6 (lobby track)
            { 46, "Music/cursedtemplewait.mp3" },
            { 47, "Music/cursedtemplewait.mp3" },
            { 48, "Music/cursedtemplewait.mp3" },
            { 49, "Music/cursedtemplewait.mp3" },
            { 50, "Music/cursedtemplewait.mp3" },
            { 51, "Music/elbeland.mp3" },            // Elbeland
            { 56, "Music/SwampOfCalmness.mp3" },     // Swamp of Peace
            { 57, "Music/Raklion.mp3" },             // Raklion
            { 58, "Music/Raklion_Hatchery.mp3" },    // Raklion boss
            { 62, "Music/Santa_Village.mp3" },       // Santa Village
            { 63, "Music/PK_Field.mp3" },            // Vulcanus (PK field)
            { 64, "Music/DuelArena.mp3" },           // Duel Arena
            { 65, "Music/iDoppelganger.mp3" },       // Doppelganger 1-4
            { 66, "Music/iDoppelganger.mp3" },
            { 67, "Music/iDoppelganger.mp3" },
            { 68, "Music/iDoppelganger.mp3" },
            { 69, "Music/ImperialGuardianFort.mp3" }, // Imperial Guardian 1-4
            { 70, "Music/ImperialGuardianFort.mp3" },
            { 71, "Music/ImperialGuardianFort.mp3" },
            { 72, "Music/ImperialGuardianFort.mp3" },
            { 80, "Music/Karutan_A.mp3" },           // Karutan 1
            { 81, "Music/Karutan_B.mp3" },           // Karutan 2
            { 82, "Music/iDoppelganger.mp3" },       // Doppelganger Renewal
            { 91, "Music/Acheron.mp3" },             // Acheron
            { 98, "Music/cursedtemplewait.mp3" },    // Illusion Temple League
            { 110, "Music/Nars01.mp3" },             // Nars
            { 113, "Music/NixiesLake_2.mp3" },       // Nixies Lake
            { 122, "Music/SwampOfDarkness.mp3" },    // Swamp of Darkness
            { 123, "Music/KuberaMine.mp3" },         // Kubera Mine
            { 133, "Music/ArenilTemple.mp3" },       // Temple of Arnil
            { 134, "Music/AsherAida.mp3" },          // Ashen Aida
            { 135, "Music/OldKethotum.mp3" },        // Old Kethotum
            { 136, "Music/BlazeKethotum.mp3" },      // Blaze Kethotum
            { 138, "Music/IgnisVolcano.mp3" },       // Ignis Volcano
            { 140, "Music/BloodTarkan.mp3" },        // Bloody Tarkan
            { 142, "Music/iDoppelganger.mp3" },      // Doppelganger Ice Zone (new)
        };

        private static readonly Dictionary<ushort, string> Ambient = new()
        {
            { 0, "Sound/aWind.wav" },                // Lorencia
            { 1, "Sound/aDungeon.wav" },             // Dungeon
            { 2, "Sound/aWind.wav" },                // Devias
            { 3, "Sound/aWind.wav" },                // Noria
            { 4, "Sound/aTower.wav" },               // Lost Tower
            { 7, "Sound/aWater.wav" },               // Atlans
            { 8, "Sound/desert.wav" },               // Tarkan
            { 10, "Sound/aHeaven.wav" },             // Icarus
            { 24, "Sound/aKalima.wav" },             // Kalima 1-6, 7
            { 25, "Sound/aKalima.wav" },
            { 26, "Sound/aKalima.wav" },
            { 27, "Sound/aKalima.wav" },
            { 28, "Sound/aKalima.wav" },
            { 29, "Sound/aKalima.wav" },
            { 36, "Sound/aKalima.wav" },
            { 33, "Sound/w34/aida_ambi.wav" },       // Aida
            { 58, "Sound/aWind.wav" },               // Raklion boss
            { 79, "Sound/aWind.wav" },               // Loren Market
            { 80, "Sound/Karutan/Karutan_desert_env.wav" }, // Karutan 1
            { 81, "Sound/Karutan/Karutan_desert_env.wav" }, // Karutan 2
        };

        /// <summary>Default background music for a map, or <c>null</c> if the map has none.</summary>
        public static string GetMusic(ushort mapId) => Music.TryGetValue(mapId, out var path) ? path : null;

        /// <summary>Default looping ambient sound for a map, or <c>null</c> if the map has none.</summary>
        public static string GetAmbient(ushort mapId) => Ambient.TryGetValue(mapId, out var path) ? path : null;
    }
}
