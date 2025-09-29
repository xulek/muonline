using System;

namespace Client.Main
{
        public static class Constants
        {
                public static string IPAddress = "127.0.0.1";
                public static int Port = 44405;

                // Terrain constants
                public const int TERRAIN_SIZE = 256;
                public const int TERRAIN_SIZE_MASK = 255;
                public const float TERRAIN_SCALE = 100f;


                // Game settings
                public static string DataPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "C:\\Users\\Windows-Desktop\\Unity\\Mu Online\\Assets\\StreamingAssets\\Data");
                public static string DataPath2 = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "C:\\Users\\Windows-Desktop\\Unity\\Mu Online\\Assets\\Resources\\");
    }
}
