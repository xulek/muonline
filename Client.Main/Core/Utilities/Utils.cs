using System;
using System.IO;
using Client.Main.Objects;
using Client.Main.Objects.Effects;

namespace Client.Main.Core.Utilities
{
    public static class Utils
    {
        public static string GetActualPath(string path)
        {
            path = path.Replace('\\', '/');
            if (File.Exists(path) || Directory.Exists(path))
                return path;
            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(directory))
                return path;
            directory = GetActualPath(directory);
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            return path;
        }
        public static SpriteObject GetEffectByCode(EffectType e)
        {
            switch (e)
            {
                case EffectType.Light:
                    return new LightEffect();
                case EffectType.TargetPosition1:
                    return new TargetPosition1();
                default:
                    throw new Exception("Effect code now exists");
            }
        }

    }
}
