using System;
using System.Collections.Generic;

namespace Client.Main.Content
{
    /// <summary>
    /// Per-model monster action PlaySpeed overrides, ported 1:1 from SourceMain5.2
    /// OpenMonsterModel() in ZzzOpenData.cpp (speed defaults at 2356-2373,
    /// per-type cases throughout the function body up to ~3110).
    ///
    /// Original base defaults for every monster model:
    ///   Stop1=0.25 Stop2=0.2 Walk=0.34 Attack1=0.33 Attack2=0.33 Shock=0.5 Die=0.55, Die.Loop=true
    /// followed by global multipliers on actions 0..5:
    ///   type 3 (Dark Knight) x1.2; type 5 (Giant) and 25 (Stone Golem) x0.7;
    ///   type 37 (Hydra) and 42 (Tantallos) x0.4.
    ///
    /// Keys are monster model file numbers: "Monster/Monster{key}.bmd".
    /// Values are indexed by MonsterActionType (0=Stop1 .. 11=Attack5);
    /// -1 means keep the computed default.
    /// Values are stored in original units; apply the same x2 factor as SetActionSpeed.
    /// </summary>
    public static class MonsterActionSpeedDatabase
    {
        public const float KeepDefault = -1f;

        /// <summary>Original default PlaySpeeds per action index (Stop1..Attack5).</summary>
        public static readonly float[] Defaults =
        {
            0.25f, 0.2f, 0.34f, 0.33f, 0.33f, 0.5f, 0.55f, -1f, -1f, -1f, -1f, -1f
        };

        private static readonly Dictionary<int, float[]> Overrides = new Dictionary<int, float[]>
        {
            [3] = new[] { -1f, -1f, 0.7f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [7] = new[] { -1f, -1f, 0.6f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [9] = new[] { -1f, -1f, 0.7f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [10] = new[] { -1f, -1f, 1.2f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [11] = new[] { -1f, -1f, 0.28f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [13] = new[] { -1f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [14] = new[] { -1f, -1f, 0.28f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [15] = new[] { -1f, 0.35f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [18] = new[] { -1f, -1f, 0.5f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [20] = new[] { -1f, -1f, 0.6f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [21] = new[] { -1f, -1f, 0.4f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [22] = new[] { -1f, -1f, 0.5f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [29] = new[] { -1f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [32] = new[] { -1f, 0.8f, -1f, 0.5f, 0.7f, -1f, -1f, 0.8f, -1f, -1f, -1f, -1f },
            [33] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, -1f, 0.4f, 0.4f, 0.4f, 0.4f, -1f },
            [35] = new[] { -1f, -1f, -1f, 0.5f, 0.5f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [38] = new[] { -1f, -1f, -1f, 0.15f, 0.15f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [40] = new[] { -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [42] = new[] { -1f, -1f, 0.18f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [43] = new[] { -1f, -1f, -1f, 0.35f, 0.35f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [45] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [50] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [51] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [52] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [53] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [54] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [55] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [56] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [57] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [64] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.1f, -1f, -1f, -1f, -1f, -1f },
            [65] = new[] { -1f, -1f, -1f, 0.3f, 0.25f, 0.15f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [67] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.1f, -1f, -1f, -1f, -1f, -1f },
            [68] = new[] { -1f, -1f, -1f, 0.2f, 0.3f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [70] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.1f, -1f, -1f, -1f, -1f, -1f },
            [71] = new[] { -1f, -1f, 0.3f, 0.5f, 0.5f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [72] = new[] { -1f, -1f, 0.3f, 0.5f, 0.5f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [73] = new[] { -1f, -1f, 0.3f, 0.4f, 0.4f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [87] = new[] { 0.05f, 0.05f, 0.1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [90] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, -1f, 0.33f, 0.33f, 0.33f, -1f, -1f },
            [93] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, -1f, 0.33f, 0.33f, -1f, -1f, -1f },
            [95] = new[] { -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [100] = new[] { -1f, -1f, -1f, 0.37f, 0.37f, -1f, 0.15f, -1f, -1f, -1f, -1f, -1f },
            [101] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [102] = new[] { 0.15f, 0.15f, 0.3f, 0.23f, 0.23f, -1f, 0.15f, -1f, -1f, -1f, -1f, -1f },
            [103] = new[] { 0.15f, 0.15f, 0.3f, 0.23f, 0.23f, -1f, 0.15f, -1f, -1f, -1f, -1f, -1f },
            [105] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [106] = new[] { -1f, -1f, 0.15f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [107] = new[] { 0.25f, 0.25f, 0.23f, 0.28f, 0.28f, -1f, 0.19f, -1f, -1f, -1f, -1f, -1f },
            [108] = new[] { 0.25f, 0.25f, 0.27f, 0.3f, 0.3f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [109] = new[] { 0.25f, 0.25f, 0.27f, 0.25f, 0.25f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [110] = new[] { 0.25f, 0.25f, 0.27f, 0.3f, 0.3f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [111] = new[] { -1f, -1f, -1f, 0.23f, 0.23f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [112] = new[] { 0.25f, 0.25f, 0.27f, 0.27f, 0.27f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [113] = new[] { -1f, -1f, -1f, 0.26f, 0.26f, -1f, 0.21f, -1f, -1f, -1f, -1f, -1f },
            [114] = new[] { 0.25f, 0.25f, 0.25f, 0.3f, 0.3f, 0.5f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [115] = new[] { 0.25f, 0.25f, 0.34f, 0.33f, 0.33f, -1f, 0.23f, -1f, -1f, -1f, -1f, -1f },
            [116] = new[] { 0.25f, 0.25f, 0.34f, 0.23f, 0.23f, -1f, 0.23f, -1f, -1f, -1f, -1f, -1f },
            [117] = new[] { 0.25f, 0.25f, 0.34f, 0.25f, 0.25f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [119] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.12f, 0.12f, -1f, -1f, -1f, -1f },
            [120] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.12f, 0.12f, -1f, -1f, -1f, -1f },
            [122] = new[] { 0.22f, 0.22f, 0.25f, 0.25f, 0.25f, -1f, 0.12f, 0.22f, 0.25f, 0.25f, -1f, -1f },
            [123] = new[] { -1f, -1f, -1f, -1f, -1f, 0.3f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [128] = new[] { 0.4f, 0.4f, 0.4f, -1f, -1f, 0.4f, 0.5f, -1f, -1f, -1f, -1f, -1f },
            [129] = new[] { 0.25f, 0.28f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [130] = new[] { 0.2f, 0.2f, 0.25f, 0.3f, 0.3f, 0.25f, 0.23f, -1f, -1f, -1f, -1f, -1f },
            [131] = new[] { 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.25f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [132] = new[] { 0.45f, 0.45f, 0.45f, 0.45f, 0.45f, 0.45f, 0.45f, -1f, -1f, -1f, -1f, -1f },
            [133] = new[] { 0.2f, 0.2f, 0.25f, 0.35f, 0.35f, 0.25f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [134] = new[] { 0.25f, 0.25f, 0.2f, 0.35f, 0.35f, 0.25f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [135] = new[] { 0.25f, 0.25f, 0.25f, 0.33f, 0.33f, 0.25f, 0.22f, -1f, -1f, -1f, -1f, -1f },
            [136] = new[] { 0.25f, 0.25f, 0.25f, 0.3f, 0.3f, 0.25f, 0.28f, -1f, -1f, -1f, -1f, -1f },
            [137] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [138] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [139] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [140] = new[] { -1f, -1f, -1f, 0.4f, 0.4f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [141] = new[] { -1f, -1f, -1f, 0.45f, 0.45f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [142] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [143] = new[] { -1f, -1f, 0.25f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [144] = new[] { -1f, -1f, 0.25f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [145] = new[] { -1f, -1f, 0.25f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [146] = new[] { -1f, -1f, 0.6f, 0.4f, -1f, -1f, 0.35f, -1f, -1f, -1f, -1f, -1f },
            [148] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.13f, -1f, -1f, -1f, -1f, -1f },
            [149] = new[] { -1f, -1f, 0.46f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [150] = new[] { -1f, -1f, 0.25f, 0.21f, -1f, -1f, 0.23f, -1f, -1f, -1f, -1f, -1f },
            [151] = new[] { -1f, -1f, 0.2f, 0.25f, 0.25f, 0.35f, 0.18f, 0.25f, 0.25f, 0.25f, -1f, -1f },
            [155] = new[] { 0.3f, 0.3f, 0.6f, -1f, -1f, 0.5f, 0.5f, -1f, -1f, -1f, -1f, -1f },
            [156] = new[] { -1f, 0.3f, 0.29f, -1f, -1f, -1f, 0.4f, -1f, -1f, -1f, -1f, -1f },
            [158] = new[] { 0.28f, -1f, 0.3f, 0.17f, -1f, 0.25f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [159] = new[] { 0.25f, -1f, 0.3f, 0.28f, -1f, 0.2f, 0.18f, -1f, -1f, -1f, -1f, -1f },
            [160] = new[] { 0.2f, -1f, 0.4f, 0.17f, -1f, 0.25f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [161] = new[] { 0.28f, -1f, 0.5f, 0.17f, -1f, 0.25f, 0.4f, -1f, -1f, -1f, -1f, -1f },
            [162] = new[] { 0.28f, -1f, 0.5f, 0.17f, -1f, 0.25f, 0.4f, -1f, -1f, -1f, -1f, -1f },
            [163] = new[] { 0.28f, -1f, 0.3f, 0.25f, 0.25f, 0.25f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [164] = new[] { 0.28f, -1f, 0.3f, 0.25f, 0.25f, 0.25f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [165] = new[] { 0.3f, 0.3f, 0.4f, 0.38f, 0.4f, 0.5f, 0.2f, 0.4f, 0.38f, 0.38f, -1f, -1f },
            [166] = new[] { 0.3f, 0.3f, 0.5f, 0.86f, 0.86f, 0.5f, 0.4f, 0.4f, 0.76f, -1f, -1f, -1f },
            [167] = new[] { 0.6f, 0.6f, 0.55f, 0.75f, 0.5f, 0.5f, 0.3f, 0.8f, 0.5f, -1f, -1f, -1f },
            [168] = new[] { 0.6f, 0.6f, 0.5f, 0.71f, 0.4f, 0.5f, 0.3f, 0.8f, 0.4f, -1f, -1f, -1f },
            [169] = new[] { 0.3f, 0.3f, 0.4f, 0.33f, 0.38f, 0.5f, 0.3f, -1f, 0.4f, 0.45f, -1f, -1f },
            [170] = new[] { 0.3f, 0.3f, 0.35f, 0.4f, 0.45f, 0.5f, 0.4f, 0.45f, 0.45f, -1f, -1f, -1f },
            [171] = new[] { 0.3f, 0.3f, 0.35f, 0.4f, 0.35f, 0.5f, 0.35f, -1f, 0.4f, -1f, -1f, -1f },
            [172] = new[] { 0.3f, 0.3f, 0.4f, 0.45f, 0.5f, 0.5f, 0.3f, -1f, 0.45f, -1f, -1f, -1f },
            [173] = new[] { 0.25f, 0.2f, 0.5f, 0.66f, 0.33f, 0.5f, 0.55f, -1f, -1f, -1f, -1f, -1f },
            [174] = new[] { 0.25f, 0.2f, 0.4f, 0.33f, 0.36f, 0.5f, 0.3f, 0.4f, -1f, -1f, -1f, -1f },
            [175] = new[] { 0.3f, 0.3f, 0.65f, 0.86f, 0.86f, 0.5f, 0.3f, 0.86f, -1f, -1f, -1f, -1f },
            [176] = new[] { 0.6f, 0.6f, 0.8f, 0.96f, 0.96f, 1f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [177] = new[] { 0.25f, 0.2f, 0.2f, 0.4f, 0.38f, 0.5f, 0.4f, -1f, 0.38f, -1f, -1f, -1f },
            [178] = new[] { 0.3f, 0.3f, 0.37f, 0.33f, 0.33f, 0.5f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [179] = new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.45f, 0.4f, -1f, -1f, -1f, -1f },
            [181] = new[] { 0.25f, 0.2f, 0.55f, 0.66f, 0.66f, 0.5f, 0.55f, -1f, -1f, -1f, -1f, -1f },
            [182] = new[] { 0.3f, 0.3f, 0.35f, 0.33f, 0.33f, 0.5f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [190] = new[] { -1f, -1f, -1f, -1f, -1f, 0.25f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [191] = new[] { -1f, -1f, -1f, -1f, -1f, 0.25f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [192] = new[] { 0.5f, 0.4f, 0.68f, 0.5f, 0.5f, 1.0f, 1.1f, 0.99f, -1f, -1f, -1f, -1f },
            [193] = new[] { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, -1f, -1f, -1f },
            [195] = new[] { 0.15f, 0.15f, 0.3f, 0.23f, 0.23f, -1f, 0.15f, -1f, -1f, -1f, -1f, -1f },
            [196] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [197] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [198] = new[] { 0.25f, 0.25f, 0.23f, 0.28f, 0.28f, -1f, 0.19f, -1f, -1f, -1f, -1f, -1f },
            [199] = new[] { 0.25f, 0.25f, 0.27f, 0.27f, 0.27f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [200] = new[] { -1f, -1f, -1f, 0.26f, 0.26f, -1f, 0.21f, -1f, -1f, -1f, -1f, -1f },
            [201] = new[] { 0.25f, 0.25f, 0.25f, 0.3f, 0.3f, 0.5f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [202] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [203] = new[] { -1f, -1f, 0.25f, -1f, -1f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [204] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [207] = new[] { -1f, -1f, -1f, -1f, -1f, -1f, 0.13f, -1f, -1f, -1f, -1f, -1f },
            [208] = new[] { -1f, -1f, 0.46f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [209] = new[] { -1f, -1f, 0.25f, 0.21f, -1f, -1f, 0.23f, -1f, -1f, -1f, -1f, -1f },
            [210] = new[] { -1f, -1f, 0.95f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [211] = new[] { -1f, -1f, 1f, 0.4f, 0.4f, -1f, 0.2f, -1f, -1f, -1f, -1f, -1f },
            [212] = new[] { 0.7f, 0.7f, 0.6f, 0.8f, 0.8f, 0.25f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [213] = new[] { -1f, -1f, -1f, 0.4f, -1f, -1f, 0.3f, -1f, -1f, -1f, -1f, -1f },
            [214] = new[] { -1f, -1f, 0.9f, 0.37f, 0.37f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [215] = new[] { -1f, -1f, 0.9f, 0.37f, 0.37f, -1f, 0.25f, -1f, -1f, -1f, -1f, -1f },
            [216] = new[] { -1f, -1f, -1f, 0.8f, 0.8f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
            [217] = new[] { -1f, -1f, -1f, 0.75f, 0.75f, -1f, -1f, -1f, -1f, -1f, -1f, -1f },
        };

        /// <summary>
        /// Applies SourceMain5.2 OpenMonsterModel defaults and per-model overrides
        /// to a freshly loaded monster BMD. Uses the same x2 factor as SetActionSpeed.
        /// NOTE: file 180 (Forsaker) is intentionally absent — its case is inside
        /// `#ifdef LDS_ADD_EG_2_MONSTER_GUARDIANPRIEST`, never defined in SourceMain5.2.
        /// </summary>
        public static void Apply(Client.Data.BMD.BMD model, int monsterModelFileNumber)
        {
            if (model?.Actions == null)
                return;

            int type = monsterModelFileNumber - 1; // EMonsterModelType value in SourceMain5.2

            var speeds = (float[])Defaults.Clone();

            // Global multipliers applied to actions Stop1..Shock (indexes 0..5)
            float mult = 1f;
            if (type == 3) mult = 1.2f;              // MONSTER_MODEL_DARK_KNIGHT
            else if (type == 5 || type == 25) mult = 0.7f;   // GIANT / STONE_GOLEM
            else if (type == 37 || type == 42) mult = 0.4f;  // HYDRA / TANTALLOS
            if (mult != 1f)
            {
                for (int i = 0; i <= 5; i++)
                    speeds[i] *= mult;
            }

            if (Overrides.TryGetValue(monsterModelFileNumber, out var ov))
            {
                int count = Math.Min(ov.Length, speeds.Length);
                for (int i = 0; i < count; i++)
                {
                    if (ov[i] >= 0f)
                        speeds[i] = ov[i];
                }
            }

            int actions = Math.Min(speeds.Length, model.Actions.Length);
            for (int i = 0; i < actions; i++)
            {
                var action = model.Actions[i];
                if (action == null || speeds[i] < 0f)
                    continue;
                action.PlaySpeed = speeds[i] * 2f;
            }

            // NOTE: SourceMain5.2 also sets Actions[MONSTER01_DIE].Loop = true; the port's
            // BMDTextureAction has no Loop flag — death looping is handled by the
            // animation controller (HoldOnLastFrame / MonsterActionType.Die handling).
        }
    }
}
