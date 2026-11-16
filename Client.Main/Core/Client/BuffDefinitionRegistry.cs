#nullable enable
using System;
using System.Collections.Generic;

namespace Client.Main.Core.Client
{
    public sealed class BuffDefinition
    {
        public BuffEffectId EffectId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? ValueText { get; init; }

        /// <summary>Base duration of the server effect (OpenMU MagicEffectDefinition.Duration constant).</summary>
        public TimeSpan? Duration { get; init; }

        /// <summary>
        /// Energy of the caster per extra second (OpenMU durationPerEnergy: TotalEnergy * 1 / divisor); 0 when the
        /// duration does not grow with energy.
        /// </summary>
        public float EnergyPerSecond { get; init; }

        /// <summary>Skills of the hero that apply this effect (used to add the hero's own energy bonus).</summary>
        public ushort[] CasterSkillIds { get; init; } = Array.Empty<ushort>();

        public bool IsDebuff { get; init; }
    }

    /// <summary>
    /// Buff names follow SourceMain5.2 eBuffState (_enum.h:3690), durations OpenMU's magic effect initializers
    /// (Persistence/Initialization/Skills/*EffectInitializer.cs, SkillsInitializerBase.ApplyElementalModifier).
    /// The MagicEffectStatus packet carries no time: a duration here is only shown, the server ends the effect.
    /// </summary>
    public static class BuffDefinitionRegistry
    {
        private static readonly Dictionary<BuffEffectId, BuffDefinition> Definitions = new();

        static BuffDefinitionRegistry()
        {
            Add(1, "Greater Damage", "Attack increased", 60);                              // GreaterDamage
            Add(2, "Greater Defense", "Defense increased", 60);                            // GreaterDefense
            Add(3, "Elf Soldier", "Attack and defense increased", null);                  // eBuff_HelpNpc
            Add(4, "Soul Barrier", "Damage absorbed by mana", 60, 40f, 16);               // SoulBarrier
            Add(5, "Critical Damage", "Critical damage increased", 60, 10f, 64);          // CriticalDamageIncrease
            Add(6, "Infinity Arrow", "Arrows are not consumed", 600, 0f, 77);             // InfiniteArrow
            Add(7, "AG Recovery", "AG recovery increased", null);                         // eBuff_AddAG
            Add(8, "Swell Life", "Maximum HP increased", 60, 5f, 48);                     // GreaterFortitude
            Add(9, "Mana Increase", "Maximum mana increased", null);                      // eBuff_AddMana
            Add(10, "Potion of Bless", "Damage to castle objects increased", 120);        // PotionOfBless
            Add(11, "Potion of Soul", "Attack speed and AG recovery increased", 60);      // PotionOfSoul
            Add(12, "Removal Magic", null, null);                                          // eBuff_RemovalMagic
            Add(18, "Invisibility", "Hidden from monsters", null);                        // Transparency
            Add(19, "Skill Addition", null, null);                                         // eBuff_AddSkill
            Add(28, "Game Master", null, null);                                            // GameMaster
            Add(55, "Poison", "Taking poison damage", 10, isDebuff: true);                // Poisoned: 20 s for Poison, 10 s else
            Add(56, "Ice", "Movement speed reduced", 10, isDebuff: true);                 // Iced
            Add(57, "Freeze", "Unable to move", 5, isDebuff: true);                       // Freeze (Ice Arrow)
            Add(58, "Defense Reduction", "Defense decreased", 10, isDebuff: true);        // DefenseReduction
            Add(59, "Attack Reduction", "Attack decreased", null, isDebuff: true);        // eDeBuff_Attack
            Add(60, "Magic Reduction", "Wizardry decreased", null, isDebuff: true);       // eDeBuff_MagicPower
            Add(61, "Stun", "Unable to move", null, isDebuff: true);                      // Stunned
            Add(65, "Restraint", "Unable to move", null, isDebuff: true);                 // SpellOfRestriction
            Add(71, "Damage Reflection", "Reflects received damage", null);               // Reflection
            Add(72, "Sleep", "Asleep", null, isDebuff: true);                              // Sleep
            Add(73, "Blind", "Attack success rate decreased", null, isDebuff: true);      // Blind
            Add(74, "Requiem", "Bleeding", null, isDebuff: true);                          // eDeBuff_NeilDOT
            Add(75, "Explosion", "Burning", null, isDebuff: true);                         // eDeBuff_SahamuttDOT
            Add(76, "Weakness", "Attack decreased", 10, isDebuff: true);                   // Weakness
            Add(77, "Innovation", "Defense decreased", null, isDebuff: true);              // eDeBuff_DefenseDown
            Add(81, "Berserker", "Wizardry increased, defense reduced", 30, 20f, 218);    // Berserker
            Add(82, "Swell of Magic Power", "Wizardry increased", 1200);                   // WizEnhance
            Add(85, "Lightning Shock", "Lightning Shock", null, isDebuff: true);           // eDeBuff_LightningShockDamage
            Add(86, "Blow of Destruction", "Unable to move", null, isDebuff: true);        // eDeBuff_BlowOfDestruction
            Add(129, "Ignore Defense", "Ignores part of the enemy defense", 60, 10f);      // IgnoreDefense
            Add(130, "Increase Health", "Maximum health increased", 60, 10f);              // IncreaseHealth
            Add(131, "Increase Block", "Defense rate increased", 60, 10f);                 // IncreaseBlock
        }

        public static BuffDefinition Get(BuffEffectId effectId)
        {
            if (Definitions.TryGetValue(effectId, out var definition))
                return definition;

            return new BuffDefinition
            {
                EffectId = effectId,
                Name = $"Effect {(byte)effectId}",
            };
        }

        public static TimeSpan? GetDuration(BuffEffectId effectId) => Get(effectId).Duration;

        public static string GetName(BuffEffectId effectId) => Get(effectId).Name;

        public static string? GetValueText(BuffEffectId effectId) => Get(effectId).ValueText;

        /// <summary>
        /// Duration of an effect: the base, plus the hero's energy bonus when the hero itself cast one of the
        /// effect's skills just before (the energy of another caster is unknown, so only the base is used).
        /// </summary>
        public static TimeSpan? EstimateDuration(BuffEffectId effectId, ushort? heroCastSkillId, int heroEnergy)
        {
            var definition = Get(effectId);
            if (!definition.Duration.HasValue)
                return null;

            double seconds = definition.Duration.Value.TotalSeconds;
            if (definition.EnergyPerSecond > 0f && heroCastSkillId.HasValue &&
                Array.IndexOf(definition.CasterSkillIds, heroCastSkillId.Value) >= 0)
            {
                seconds += heroEnergy / definition.EnergyPerSecond;
            }

            return TimeSpan.FromSeconds(seconds);
        }

        private static void Add(
            byte effectId,
            string name,
            string? valueText,
            int? seconds,
            float energyPerSecond = 0f,
            ushort casterSkillId = 0,
            bool isDebuff = false)
        {
            var id = (BuffEffectId)effectId;
            Definitions[id] = new BuffDefinition
            {
                EffectId = id,
                Name = name,
                ValueText = valueText,
                Duration = seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : null,
                EnergyPerSecond = energyPerSecond,
                CasterSkillIds = casterSkillId != 0 ? new[] { casterSkillId } : Array.Empty<ushort>(),
                IsDebuff = isDebuff,
            };
        }

        private static void Add(byte effectId, string name, string? valueText, int? seconds, bool isDebuff) =>
            Add(effectId, name, valueText, seconds, 0f, 0, isDebuff);
    }
}
