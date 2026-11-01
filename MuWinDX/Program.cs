using System.Windows.Forms;
using Client.Main;

#if DEBUG
Constants.DataPath = @"C:\Games\MU_Red_1_20_61_Full\Data";
#endif
if (System.Environment.GetEnvironmentVariable("MU_DATA_PATH") is { Length: > 0 } dataPath)
    Constants.DataPath = dataPath;

Application.SetHighDpiMode(HighDpiMode.SystemAware);
PerformanceRuntimeTuning.Apply();

using var game = new MuGame();
game.Run();
