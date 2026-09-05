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
        public TimeSpan? Duration { get; init; }
        public bool IsDebuff { get; init; }
    }

    public static class BuffDefinitionRegistry
    {
        private static readonly Dictionary<BuffEffectId, BuffDefinition> Definitions = new()
        {
            [BuffEffectId.GreaterDamage] = Create(BuffEffectId.GreaterDamage, "Greater Damage", "Attack increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.GreaterDefense] = Create(BuffEffectId.GreaterDefense, "Greater Defense", "Defense increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.ManaShield] = Create(BuffEffectId.ManaShield, "Mana Shield", "Damage absorbed by mana", TimeSpan.FromMinutes(3)),
            [BuffEffectId.ElfSoldier] = Create(BuffEffectId.ElfSoldier, "Elf Guardian", "Attack and defense increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.SwellLife] = Create(BuffEffectId.SwellLife, "Swell Life", "Maximum HP increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.CriticalDamage] = Create(BuffEffectId.CriticalDamage, "Critical Damage", "Critical damage increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.HealOverTime] = Create(BuffEffectId.HealOverTime, "Heal", "Recovering health", TimeSpan.FromSeconds(30)),
            [BuffEffectId.Poison] = Create(BuffEffectId.Poison, "Poison", "Taking poison damage", TimeSpan.FromSeconds(20), isDebuff: true),
            [BuffEffectId.Ice] = Create(BuffEffectId.Ice, "Ice", "Movement speed reduced", TimeSpan.FromSeconds(10), isDebuff: true),
            [BuffEffectId.Slow] = Create(BuffEffectId.Slow, "Slow", "Movement speed reduced", TimeSpan.FromSeconds(10), isDebuff: true),
            [BuffEffectId.Weaken] = Create(BuffEffectId.Weaken, "Weaken", "Combat power reduced", TimeSpan.FromSeconds(20), isDebuff: true),
            [BuffEffectId.Invisible] = Create(BuffEffectId.Invisible, "Invisible", "Hidden from enemies", TimeSpan.FromMinutes(1)),
            [BuffEffectId.Invincible] = Create(BuffEffectId.Invincible, "Invincible", "Damage ignored", TimeSpan.FromSeconds(30)),
            [BuffEffectId.Berserk] = Create(BuffEffectId.Berserk, "Berserk", "Attack increased, defense reduced", TimeSpan.FromMinutes(3)),
            [BuffEffectId.Reflection] = Create(BuffEffectId.Reflection, "Reflection", "Reflects received damage", TimeSpan.FromMinutes(3)),
            [BuffEffectId.Transform] = Create(BuffEffectId.Transform, "Transform", "Character transformed", null),
            [BuffEffectId.ElfAttack] = Create(BuffEffectId.ElfAttack, "Elf Attack Buff", "Attack increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.ElfDefense] = Create(BuffEffectId.ElfDefense, "Elf Defense Buff", "Defense increased", TimeSpan.FromMinutes(3)),
            [BuffEffectId.ElfHeal] = Create(BuffEffectId.ElfHeal, "Elf Heal", "Recovering health", TimeSpan.FromSeconds(30)),
            [BuffEffectId.Stun] = Create(BuffEffectId.Stun, "Stun", "Unable to move", TimeSpan.FromSeconds(5), isDebuff: true),
        };

        public static BuffDefinition Get(BuffEffectId effectId)
        {
            if (Definitions.TryGetValue(effectId, out var definition))
                return definition;

            return new BuffDefinition
            {
                EffectId = effectId,
                Name = $"Unknown Buff ({(byte)effectId})",
            };
        }

        public static TimeSpan? GetDuration(BuffEffectId effectId) => Get(effectId).Duration;

        public static string GetName(BuffEffectId effectId) => Get(effectId).Name;

        public static string? GetValueText(BuffEffectId effectId) => Get(effectId).ValueText;

        private static BuffDefinition Create(
            BuffEffectId effectId,
            string name,
            string? valueText,
            TimeSpan? duration,
            bool isDebuff = false) =>
            new()
            {
                EffectId = effectId,
                Name = name,
                ValueText = valueText,
                Duration = duration,
                IsDebuff = isDebuff,
            };
    }
}
