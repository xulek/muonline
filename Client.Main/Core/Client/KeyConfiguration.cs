#nullable enable
using System;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Server-stored key configuration (0xF3/0x30, original PRECEIVE_OPTION / SaveOptions in ZzzOpenData.cpp):
    /// 10 skill hotkeys, game option flags, the Q/W/E/R potion hotkeys and the chat box layout.
    /// </summary>
    public sealed class KeyConfiguration
    {
        public const int SkillHotkeyCount = 10;
        public const int Length = 30;
        public const ushort EmptySkill = 0xFFFF;

        public const byte AutoAttackOn = 0x01;
        public const byte WhisperSoundOn = 0x02;
        public const byte SlideHelpOff = 0x04;

        /// <summary>Skill numbers of the hotkeys 1..0, <see cref="EmptySkill"/> when unassigned.</summary>
        public ushort[] SkillHotkeys { get; } = new ushort[SkillHotkeyCount];

        public byte GameOptions { get; set; } = AutoAttackOn;

        /// <summary>Q, W, E, R: item number within the potion group (14), 0xFF when unassigned.</summary>
        public byte[] PotionHotkeys { get; } = { 0xFF, 0xFF, 0xFF, 0xFF };

        /// <summary>Q, W, E, R: item level of the assigned potion.</summary>
        public byte[] PotionLevels { get; } = new byte[4];

        public byte ChatLogBox { get; set; }

        public KeyConfiguration()
        {
            Array.Fill(SkillHotkeys, EmptySkill);
        }

        /// <summary>Parses the configuration bytes (packet data after the sub code).</summary>
        public static KeyConfiguration? Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < 26) return null;
            var config = new KeyConfiguration();
            for (int i = 0; i < SkillHotkeyCount; i++)
                config.SkillHotkeys[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]); // MAKEWORD(HotKey[i*2+1], HotKey[i*2])
            config.GameOptions = data[20];
            config.PotionHotkeys[0] = data[21];
            config.PotionHotkeys[1] = data[22];
            config.PotionHotkeys[2] = data[23];
            config.ChatLogBox = data[24];
            config.PotionHotkeys[3] = data[25];
            // Levels as four bytes Q, W, E, R, the layout SaveOptions writes.
            for (int i = 0; i < 4 && 26 + i < data.Length; i++)
                config.PotionLevels[i] = data[26 + i];
            return config;
        }

        public byte[] ToBytes()
        {
            var data = new byte[Length];
            for (int i = 0; i < SkillHotkeyCount; i++)
            {
                data[i * 2] = (byte)(SkillHotkeys[i] >> 8);
                data[i * 2 + 1] = (byte)(SkillHotkeys[i] & 0xFF);
            }
            data[20] = GameOptions;
            data[21] = PotionHotkeys[0];
            data[22] = PotionHotkeys[1];
            data[23] = PotionHotkeys[2];
            data[24] = ChatLogBox;
            data[25] = PotionHotkeys[3];
            for (int i = 0; i < 4; i++)
                data[26 + i] = PotionLevels[i];
            return data;
        }

        public bool HasAnyAssignment()
        {
            foreach (var skill in SkillHotkeys) if (skill != EmptySkill && skill != 0) return true;
            foreach (var potion in PotionHotkeys) if (potion != 0xFF) return true;
            return false;
        }
    }
}
