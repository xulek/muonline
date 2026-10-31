using Microsoft.Xna.Framework;

namespace Client.Main.Core.Utilities
{
    /// <summary>
    /// Item upgrade appearance rules ported from SourceMain5.2 (ZzzObject.cpp):
    /// <c>RenderPartObjectEffect</c> level remapping and excellent exclusions, and the
    /// <c>PartObjectColor</c>, <c>PartObjectColor2</c> and <c>PartObjectColor3</c> palettes.
    /// Items are identified by their item group (0 = swords ... 15 = scrolls) and index.
    /// </summary>
    public static class ItemVisualRules
    {
        private const int GroupSword = 0, GroupMace = 2, GroupSpear = 3, GroupBow = 4, GroupStaff = 5,
            GroupShield = 6, GroupWing = 12, GroupHelper = 13, GroupPotion = 14;

        private static bool IsArmorGroup(int group) => group >= 7 && group <= 11;

        /// <summary>
        /// Level used for the upgrade effect. Wings never glow, jewels always look +8, etc.
        /// Unknown items (group &lt; 0) keep their level.
        /// </summary>
        public static int GetRenderLevel(int group, int index, int level)
        {
            switch (group)
            {
                case GroupBow when index == 7 || index == 15: // bolts, arrows
                    return level >= 1 ? level * 2 + 1 : 0;
                case GroupWing:
                    if (index <= 6 || (index >= 36 && index <= 43) || index == 49 || index == 50 ||
                        index == 11 || (index >= 60 && index <= 129) || (index >= 139 && index <= 143 && index != 141))
                        return 0;
                    if (index == 15 || index == 30 || index == 31 || index == 136 || index == 137 || index == 138 || index == 141)
                        return 8; // Jewel of Chaos, compiled / packed jewels
                    if (index == 7 || (index >= 12 && index <= 14) || (index >= 16 && index <= 19) || (index >= 44 && index <= 47))
                        return 9; // skill orbs and crystals
                    return level;
                case GroupHelper:
                    return index switch
                    {
                        3 or 10 => 8,                       // Horn of Dinorant, Transformation Ring
                        39 or 40 or 41 or 42 => 13,         // event transformation rings
                        30 or 50 or 52 or 53 => 0,          // Cape of Lord, covenant, condor items
                        _ => level,
                    };
                case GroupPotion:
                    if (index <= 6) return level > 0 ? 7 : 0; // HP/MP potions
                    return index switch
                    {
                        13 or 14 or 16 or 22 or 25 or 26 or 31 => 8, // jewels, Tear of Elf, Soul Shard
                        12 => 8,
                        20 => 9,
                        41 or 42 or 43 or 44 or 64 => 0,             // harmony / refine stones
                        17 or 18 or 19 => level <= 6 ? level / 2 : 13, // Devil Square items
                        _ => level,
                    };
                default:
                    return level;
            }
        }

        /// <summary>
        /// Items that RenderPartObjectEffect gives extra chrome passes below +3:
        /// 1 = CHROME2 (Bright 1.5 -> 2.25 x light) + CHROME4 (Jewel of Harmony, Moonstone Pendant,
        /// Illusion Sorcerer Covenant, Helper 43/44/45/93/94/116);
        /// 2 = additive texture (0.25 x light) + CHROME4 (Potion + 64).
        /// </summary>
        public static int GetLowLevelChromeMode(int group, int index)
        {
            if (group == GroupPotion && index == 42) return 1;
            if (group == GroupPotion && index == 64) return 2;
            if (group == GroupHelper && index is 38 or 43 or 44 or 45 or 50 or 93 or 94 or 116) return 1;
            return 0;
        }

        /// <summary>Excellent items pulse, except wings and capes (RenderPartObjectEffect).</summary>
        public static bool AllowsExcellentGlow(int group, int index)
        {
            if (group == GroupHelper && index == 30) return false; // Cape of Lord
            if (group != GroupWing) return true;
            return !(index <= 6 || (index >= 36 && index <= 43) || (index >= 130 && index <= 135) || index == 49 || index == 50);
        }

        /// <summary>PartObjectColor: tint of the RENDER_CHROME and RENDER_METAL passes.</summary>
        public static Vector3 GetChromeColor(int group, int index)
        {
            return Palette(GetChromeColorIndex(group, index));
        }

        private static int GetChromeColorIndex(int group, int index)
        {
            switch (group)
            {
                case GroupSword:
                    return index switch
                    {
                        14 => 2, 20 => 10, 21 => 5, 22 => 18, 23 => 23, 24 => 24, 25 => 27, 28 => 8, 31 => 10,
                        32 => 16, 33 => 42, 34 => 18, 35 => 45,
                        _ => 0,
                    };
                case GroupMace:
                    return index switch
                    {
                        8 => 9, 9 => 10, 10 => 12, 12 => 16, 14 => 22, 15 => 28, 17 => 40, 18 => 5,
                        _ => 0,
                    };
                case GroupSpear:
                    return index switch { 9 => 1, 10 => 9, 11 => 20, _ => 0 };
                case GroupBow:
                    return index switch
                    {
                        5 or 13 => 5, 17 or 19 => 9, 18 => 10, 20 => 16, 21 => 20, 22 => 26, 23 => 35, 24 => 36,
                        _ => 0,
                    };
                case GroupStaff:
                    return index switch
                    {
                        5 => 2, 9 => 5, 11 => 17, 12 => 19, 13 => 25, 14 => 24, 15 => 15, 16 => 1, 17 => 3, 18 => 30,
                        19 => 21, 20 => 5, 22 => 1, 30 => 1, 31 => 19, 33 => 43, 34 => 5,
                        _ => 0,
                    };
                case GroupShield:
                    return index switch { 16 => 6, 19 => 29, 20 => 36, 21 => 30, _ => 0 };
                case GroupWing:
                    return index == 14 ? 2 : 0;
                case GroupPotion:
                    return index == 25 || index == 26 ? 2 : 0;
            }

            if (!IsArmorGroup(group))
                return 0;

            return index switch
            {
                1 => 1, 9 => 2, 12 => 2, 3 => 3, 13 => 4, 4 => 5, 14 => 5, 6 => 6, 15 => 7, 16 => 10, 17 => 9,
                18 => 5, 19 => 9, 20 => 9, 21 => 16, 22 => 17, 23 => 11, 24 => 16, 25 => 11, 26 => 12, 27 => 10,
                28 => 15, 29 => 18, 30 => 19, 31 => 20, 32 => 21, 33 => 22, 34 => 24, 35 => 25, 36 => 26,
                37 => 27, 38 => 28, 39 => 29, 40 => 30, 41 => 31, 42 => 32, 43 => 33, 44 => 34, 45 => 36,
                46 => 42, 47 => 37, 48 => 1, 49 => 35, 50 => 39, 51 => 40, 52 => 36, 53 => 41, 59 => 16,
                60 => 42, 61 => 18, 73 => 45,
                _ => 0,
            };
        }

        private static Vector3 Palette(int color) => color switch
        {
            1 => new(1.0f, 0.2f, 0.0f),
            2 => new(0.0f, 0.5f, 1.0f),
            3 => new(0.0f, 0.5f, 1.0f),
            4 => new(0.0f, 0.8f, 0.4f),
            5 => new(1.0f, 1.0f, 1.0f),
            6 => new(0.6f, 0.8f, 0.4f),
            7 => new(0.9f, 0.8f, 1.0f),
            8 => new(0.8f, 0.8f, 1.0f),
            9 => new(0.5f, 0.5f, 0.8f),
            10 => new(0.75f, 0.65f, 0.5f),
            11 => new(0.35f, 0.35f, 0.6f),
            12 => new(0.47f, 0.67f, 0.6f),
            13 => new(0.0f, 0.3f, 0.6f),
            14 => new(0.65f, 0.65f, 0.55f),
            15 => new(0.2f, 0.3f, 0.6f),
            16 => new(0.8f, 0.46f, 0.25f),
            17 => new(0.65f, 0.45f, 0.3f),
            18 => new(0.5f, 0.4f, 0.3f),
            19 => new(0.37f, 0.37f, 1.0f),
            20 => new(0.3f, 0.7f, 0.3f),
            21 => new(0.5f, 0.4f, 1.0f),
            22 => new(0.45f, 0.45f, 0.23f),
            23 => new(0.3f, 0.3f, 0.45f),
            24 => new(0.6f, 0.5f, 0.2f),
            25 => new(0.6f, 0.6f, 0.6f),
            26 => new(0.3f, 0.7f, 0.3f),
            27 => new(0.5f, 0.6f, 0.7f),
            28 => new(0.45f, 0.45f, 0.23f),
            29 => new(0.2f, 0.7f, 0.3f),
            30 => new(0.7f, 0.3f, 0.3f),
            31 => new(0.7f, 0.5f, 0.3f),
            32 => new(0.5f, 0.2f, 0.7f),
            33 => new(0.8f, 0.4f, 0.6f),
            34 => new(0.6f, 0.4f, 0.8f),
            35 => new(0.7f, 0.4f, 0.4f),
            36 => new(0.5f, 0.5f, 0.7f),
            37 => new(0.7f, 0.5f, 0.7f),
            38 => new(0.2f, 0.4f, 0.7f),
            39 => new(0.3f, 0.6f, 0.4f),
            40 => new(0.7f, 0.2f, 0.2f),
            41 => new(0.7f, 0.2f, 0.7f),
            42 => new(0.8f, 0.4f, 0.0f),
            43 => new(0.8f, 0.6f, 0.2f),
            44 => new(0.8f, 0.7f, 0.4f),
            45 => new(0.5f, 0.8f, 0.9f),
            _ => new(1.0f, 0.5f, 0.0f),
        };

        /// <summary>
        /// PartObjectColor2: tint of the RENDER_CHROME2/CHROME4 pass (+11 and higher).
        /// XYZ multiplies the current body light; W = 1 means XYZ is an absolute colour.
        /// </summary>
        public static Vector4 GetChrome2Tint(int group, int index)
        {
            int color = 0;
            if ((group == GroupBow && (index == 5 || index == 13)) || (group == GroupSword && index == 14) ||
                (group == GroupStaff && index == 5))
            {
                color = 2;
            }
            else if (IsArmorGroup(group))
            {
                color = index switch
                {
                    4 or 14 or 15 or 17 or 39 or 40 or 41 or 42 => 1,
                    18 or 43 => 2,
                    21 or 44 => 3,
                    _ => 0,
                };
            }

            return color switch
            {
                1 => new Vector4(1.0f, 0.5f, 0.0f, 0f),
                2 => new Vector4(0.0f, 0.5f, 1.0f, 0f),
                3 => new Vector4(1.0f, 1.0f, 1.0f, 1f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 0f),
            };
        }

        /// <summary>PartObjectColor3: colour of the ancient (RENDER_CHROME3) pass.</summary>
        public static Vector3 GetAncientColor(int group, int index)
        {
            if (IsArmorGroup(group) && (index == 3 || index == 9 || index == 17))
                return new Vector3(1.0f, 0.7f, 0.2f);
            return new Vector3(0.1f, 0.6f, 1.0f);
        }
    }
}
