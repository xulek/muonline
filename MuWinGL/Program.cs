using Client.Main;

#if DEBUG
Constants.DataPath = @"C:\Games\MU_Red_1_20_61_Full\Data";
#endif
if (System.Environment.GetEnvironmentVariable("MU_DATA_PATH") is { Length: > 0 } dataPath)
    Constants.DataPath = dataPath;

using var game = new MuGame();
game.Run();
