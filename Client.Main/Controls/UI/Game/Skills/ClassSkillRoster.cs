using System;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Roster de skills por classe (S6, ids = SkillNumber do OpenMU) para a tela de skills
    /// exibir também as AINDA NÃO aprendidas (estilo mobile: ícone cinza + cadeado). O
    /// servidor só envia as aprendidas; esta tabela client-side define composição e ordem
    /// da grade. Curada à mão — ajustar aqui se o servidor usar outro conjunto.
    /// </summary>
    internal static class ClassSkillRoster
    {
        private static readonly ushort[] DarkKnight =
        {
            // Skills de ataque próprias
            19,  // Falling Slash
            20,  // Lunge
            21,  // Uppercut
            22,  // Cyclone
            23,  // Slash
            41,  // Twisting Slash
            42,  // Rageful Blow
            43,  // Death Stab
            44,  // Crescent Moon Slash
            47,  // Impale
            48,  // Swell Life
            49,  // Fire Breath
            232, // Strike of Destruction
            // Buffs/utilitárias do DK (lista Normais do OpenMU — precisam aparecer aqui)
            18,  // Defense
            67,  // Stun
            68,  // Cancel Stun
            69,  // Swell Mana
            70,  // Invisibility
            71,  // Cancel Invisibility
            210, // Spell of Protection
            211, // Spell of Restriction
            212, // Spell of Pursuit
            213, // Shield Burn
        };

        private static readonly ushort[] DarkWizard =
        {
            17,  // Energy Ball
            4,   // Fire Ball
            11,  // Power Wave
            3,   // Lightning
            2,   // Meteorite
            1,   // Poison
            6,   // Teleport
            5,   // Flame
            7,   // Ice
            8,   // Twister
            9,   // Evil Spirit
            10,  // Hellfire
            12,  // Aqua Beam
            38,  // Decay
            39,  // Ice Storm
            13,  // Cometfall
            14,  // Inferno
            15,  // Teleport Ally
            16,  // Soul Barrier
            40,  // Nova
            233, // Expansion of Wizardry
        };

        private static readonly ushort[] FairyElf =
        {
            26,  // Heal
            27,  // Greater Defense
            28,  // Greater Damage
            24,  // Triple Shot
            30,  // Summon Goblin
            31,  // Summon Stone Golem
            32,  // Summon Assassin
            33,  // Summon Elite Yeti
            34,  // Summon Dark Knight
            35,  // Summon Bali
            36,  // Summon Soldier
            51,  // Ice Arrow
            52,  // Penetration
            46,  // Starfall
            77,  // Infinity Arrow
            234, // Recovery
            235, // Multi Shot
        };

        private static readonly ushort[] MagicGladiator =
        {
            55, // Fire Slash
            56, // Power Slash
            57, // Spiral Slash
            2,  // Meteorite
            3,  // Lightning
            4,  // Fire Ball
            5,  // Flame
            7,  // Ice
            8,  // Twister
            9,  // Evil Spirit
            10, // Hellfire
            12, // Aqua Beam
            13, // Cometfall
            14, // Inferno
        };

        private static readonly ushort[] DarkLord =
        {
            60,  // Force
            66,  // Force Wave
            61,  // Fire Burst
            62,  // Earthshake
            64,  // Increase Critical Damage
            65,  // Electric Spike
            78,  // Fire Scream
            238, // Chaotic Diseier
        };

        private static readonly ushort[] Summoner =
        {
            214, // Drain Life
            217, // Thorns (Alice)
            218, // Berserker
            219, // Sleep
            220, // Blind
            221, // Weakness
            222, // Enervation
            223, // Explosion (Summon)
            224, // Requiem
            225, // Pollution
            230, // Lightning Shock
        };

        private static readonly ushort[] RageFighter =
        {
            260, // Killing Blow
            261, // Beast Uppercut
            262, // Chain Drive
            263, // Dark Side
            264, // Dragon Roar
            265, // Dragon Slasher
            266, // Ignore Defense
            267, // Increase Health
            268, // Increase Block
            269, // Charge
            270, // Phoenix Shot
        };

        public static ushort[] For(CharacterClassNumber cls) => cls switch
        {
            CharacterClassNumber.DarkKnight or CharacterClassNumber.BladeKnight or CharacterClassNumber.BladeMaster => DarkKnight,
            CharacterClassNumber.DarkWizard or CharacterClassNumber.SoulMaster or CharacterClassNumber.GrandMaster => DarkWizard,
            CharacterClassNumber.FairyElf or CharacterClassNumber.MuseElf or CharacterClassNumber.HighElf => FairyElf,
            CharacterClassNumber.MagicGladiator or CharacterClassNumber.DuelMaster => MagicGladiator,
            CharacterClassNumber.DarkLord or CharacterClassNumber.LordEmperor => DarkLord,
            CharacterClassNumber.Summoner or CharacterClassNumber.BloodySummoner or CharacterClassNumber.DimensionMaster => Summoner,
            CharacterClassNumber.RageFighter or CharacterClassNumber.FistMaster => RageFighter,
            _ => Array.Empty<ushort>()
        };

        /// <summary>Nome de exibição da classe base (como o mobile mostra no requisito).</summary>
        public static string DisplayName(CharacterClassNumber cls) => cls switch
        {
            CharacterClassNumber.DarkKnight or CharacterClassNumber.BladeKnight or CharacterClassNumber.BladeMaster => "Dark Knight",
            CharacterClassNumber.DarkWizard or CharacterClassNumber.SoulMaster or CharacterClassNumber.GrandMaster => "Dark Wizard",
            CharacterClassNumber.FairyElf or CharacterClassNumber.MuseElf or CharacterClassNumber.HighElf => "Fairy Elf",
            CharacterClassNumber.MagicGladiator or CharacterClassNumber.DuelMaster => "Magic Gladiator",
            CharacterClassNumber.DarkLord or CharacterClassNumber.LordEmperor => "Dark Lord",
            CharacterClassNumber.Summoner or CharacterClassNumber.BloodySummoner or CharacterClassNumber.DimensionMaster => "Summoner",
            CharacterClassNumber.RageFighter or CharacterClassNumber.FistMaster => "Rage Fighter",
            _ => cls.ToString()
        };
    }
}
