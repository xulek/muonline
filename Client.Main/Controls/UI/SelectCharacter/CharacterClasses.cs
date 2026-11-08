using System.Collections.Generic;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>Playable base classes as presented by the character screens (roster, creation).</summary>
    public static class CharacterClasses
    {
        /// <param name="PreviewModel">Creation preview bust in <c>Data/Logo</c>. NewFace01..07 follow the base class
        /// order Wizard, Knight, Elf, Gladiator, Lord, Summoner, Rage Fighter (MODEL_FACE + class in the original client).</param>
        public sealed record Info(CharacterClassNumber Class, string Name, string Description, string Emblem, string PreviewModel);

        /// <summary>Classes offered by character creation, in display order.</summary>
        public static IReadOnlyList<Info> Creatable { get; } = new[]
        {
            new Info(CharacterClassNumber.DarkWizard, "Dark Wizard",
                "Masters of destructive magic.\nHigh magic damage, low defense.", "class_wizard", "NewFace01"),
            new Info(CharacterClassNumber.DarkKnight, "Dark Knight",
                "Warriors of strength and honor.\nHigh health and physical damage.", "class_knight", "NewFace02"),
            new Info(CharacterClassNumber.FairyElf, "Fairy Elf",
                "Agile archers and healers.\nHigh agility, support abilities.", "class_elf", "NewFace03"),
            new Info(CharacterClassNumber.MagicGladiator, "Magic Gladiator",
                "Hybrid warriors with magic.\nBalanced melee and magic skills.", "class_gladiator", "NewFace04"),
            new Info(CharacterClassNumber.DarkLord, "Dark Lord",
                "Commanders with dark powers.\nSummons pets and commands armies.", "class_lord", "NewFace05"),
            new Info(CharacterClassNumber.Summoner, "Summoner",
                "Mystics with curse powers.\nCurses enemies and summons allies.", "class_summoner", "NewFace06"),
            new Info(CharacterClassNumber.RageFighter, "Rage Fighter",
                "Hand-to-hand combat masters.\nHighest HP, powerful combos.", "class_fighter", "NewFace07"),
        };

        public static string PreviewModelFor(CharacterClassNumber value)
        {
            foreach (Info info in Creatable)
                if (info.Class == value)
                    return info.PreviewModel;
            return null;
        }

        /// <summary>Emblem key (<c>Icons/</c>, <c>Emblems/</c>) of a class or any of its evolutions.</summary>
        public static string EmblemFor(CharacterClassNumber value) => value switch
        {
            CharacterClassNumber.DarkWizard or CharacterClassNumber.SoulMaster or CharacterClassNumber.GrandMaster => "class_wizard",
            CharacterClassNumber.DarkKnight or CharacterClassNumber.BladeKnight or CharacterClassNumber.BladeMaster => "class_knight",
            CharacterClassNumber.FairyElf or CharacterClassNumber.MuseElf or CharacterClassNumber.HighElf => "class_elf",
            CharacterClassNumber.MagicGladiator or CharacterClassNumber.DuelMaster => "class_gladiator",
            CharacterClassNumber.DarkLord or CharacterClassNumber.LordEmperor => "class_lord",
            CharacterClassNumber.Summoner or CharacterClassNumber.BloodySummoner or CharacterClassNumber.DimensionMaster => "class_summoner",
            CharacterClassNumber.RageFighter or CharacterClassNumber.FistMaster => "class_fighter",
            _ => "class_knight"
        };

        /// <summary>Name rules of character creation: 3-10 letters or digits.</summary>
        public const int MinNameLength = 3;
        public const int MaxNameLength = 10;

        /// <summary>Keeps only letters and digits, cut to <see cref="MaxNameLength"/>.</summary>
        public static string SanitizeName(string value)
        {
            var result = new System.Text.StringBuilder(MaxNameLength);
            foreach (char c in value ?? string.Empty)
            {
                if (result.Length >= MaxNameLength)
                    break;
                if (char.IsLetterOrDigit(c))
                    result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>Returns an error message, or null when the name can be submitted.</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Enter a character name.";
            if (name.Length < MinNameLength)
                return $"The name needs at least {MinNameLength} characters.";
            if (name.Length > MaxNameLength)
                return $"The name can have at most {MaxNameLength} characters.";
            return null;
        }
    }
}
