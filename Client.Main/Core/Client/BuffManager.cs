#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Buff identifiers matching SourceMain MagicEffect packet IDs.
    /// </summary>
    public enum BuffEffectId : byte
    {
        GreaterDamage = 0,
        GreaterDefense = 1,
        ManaShield = 2,
        ElfSoldier = 3,
        SwellLife = 4,
        CriticalDamage = 5,
        HealOverTime = 6,

        Poison = 8,
        Ice = 9,
        Slow = 10,
        Weaken = 11,

        Invisible = 12,
        Invincible = 13,
        Berserk = 14,
        Reflection = 15,
        Transform = 16,

        ElfAttack = 20,
        ElfDefense = 21,
        ElfHeal = 22,

        /// <summary>
        /// SourceMain5.2 eDeBuff_Stun (_enum.h:3756, eBuffState value 61):
        /// spinning rings debuff.
        /// </summary>
        Stun = 61,
    }

    public class BuffStateChangedEventArgs : EventArgs
    {
        public ushort PlayerId { get; init; }
        public BuffEffectId EffectId { get; init; }
        public bool IsActive { get; init; }
    }

    public sealed class BuffRuntimeState
    {
        public BuffEffectId EffectId { get; init; }
        public ushort PlayerId { get; init; }
        public bool IsActive { get; init; }
        public DateTime ActivatedAt { get; init; }
        public TimeSpan? Duration { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? ValueText { get; init; }

        public TimeSpan? GetRemainingTime(DateTime now)
        {
            if (!ExpiresAt.HasValue)
                return null;

            var remaining = ExpiresAt.Value - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Central buff manager - handles active buff state and fires visual-effect events.
    /// </summary>
    public class BuffManager
    {
        private readonly ILogger<BuffManager> _logger;
        private readonly Dictionary<(ushort PlayerId, BuffEffectId EffectId), ActiveBuffState> _states = new();
        private readonly List<(ushort PlayerId, BuffEffectId EffectId)> _expiredScratch = new(8);
        private long _nextExpirationUtcTicks = long.MaxValue;

        public event EventHandler<BuffStateChangedEventArgs>? BuffStateChanged;

        public BuffManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<BuffManager>();
        }

        public void ProcessMagicEffectStatus(ushort playerId, byte effectId, bool isActive)
        {
            var typedEffectId = (BuffEffectId)effectId;
            var key = (playerId, typedEffectId);

            if (isActive)
            {
                bool wasActive = _states.TryGetValue(key, out var state) && state.IsActive;
                var definition = BuffDefinitionRegistry.Get(typedEffectId);
                var activatedAt = DateTime.UtcNow;

                DateTime? expiresAt = definition.Duration.HasValue
                    ? activatedAt + definition.Duration.Value
                    : null;
                _states[key] = new ActiveBuffState
                {
                    IsActive = true,
                    ActivatedAt = activatedAt,
                    RawEffectId = effectId,
                    Definition = definition,
                    ExpiresAt = expiresAt,
                };

                if (expiresAt.HasValue && expiresAt.Value.Ticks < _nextExpirationUtcTicks)
                    _nextExpirationUtcTicks = expiresAt.Value.Ticks;

                _logger.LogDebug("Buff activated: Player={PlayerId}, Effect={EffectId}", playerId & 0x7FFF, effectId);

                if (!wasActive)
                    RaiseBuffChanged(playerId, typedEffectId, isActive: true);

                return;
            }

            if (_states.TryGetValue(key, out var existing) && existing.IsActive)
            {
                existing.IsActive = false;
                if (existing.ExpiresAt.HasValue && existing.ExpiresAt.Value.Ticks <= _nextExpirationUtcTicks)
                    _nextExpirationUtcTicks = 0L;
                _logger.LogDebug("Buff deactivated: Player={PlayerId}, Effect={EffectId}", playerId & 0x7FFF, effectId);
                RaiseBuffChanged(playerId, typedEffectId, isActive: false);
            }
        }

        public void Update()
        {
            long nextExpiration = _nextExpirationUtcTicks;
            if (nextExpiration == long.MaxValue)
                return;

            long nowTicks = DateTime.UtcNow.Ticks;
            if (nextExpiration > nowTicks)
                return;

            _expiredScratch.Clear();
            long followingExpiration = long.MaxValue;

            foreach (var kv in _states)
            {
                var state = kv.Value;
                if (!state.IsActive || !state.ExpiresAt.HasValue)
                    continue;

                long expiresAtTicks = state.ExpiresAt.Value.Ticks;
                if (expiresAtTicks <= nowTicks)
                {
                    _expiredScratch.Add(kv.Key);
                }
                else if (expiresAtTicks < followingExpiration)
                {
                    followingExpiration = expiresAtTicks;
                }
            }

            _nextExpirationUtcTicks = followingExpiration;
            for (int i = 0; i < _expiredScratch.Count; i++)
            {
                var key = _expiredScratch[i];
                if (!_states.TryGetValue(key, out var state) || !state.IsActive)
                    continue;

                state.IsActive = false;
                RaiseBuffChanged(key.PlayerId, key.EffectId, isActive: false);
            }
        }

        public bool HasBuff(ushort playerId, BuffEffectId effectId) =>
            _states.TryGetValue((playerId, effectId), out var state) && state.IsActive;

        public BuffRuntimeState? GetBuffState(ushort playerId, BuffEffectId effectId)
        {
            if (!_states.TryGetValue((playerId, effectId), out var state) || !state.IsActive)
                return null;

            return state.ToRuntimeState(playerId, effectId);
        }

        public TimeSpan? GetRemainingTime(ushort playerId, BuffEffectId effectId) =>
            GetBuffState(playerId, effectId)?.GetRemainingTime(DateTime.UtcNow);

        public IEnumerable<BuffEffectId> GetActiveBuffs(ushort playerId)
        {
            foreach (var kv in _states)
            {
                if (kv.Key.PlayerId == playerId && kv.Value.IsActive)
                    yield return kv.Key.EffectId;
            }
        }

        public void ClearPlayerBuffs(ushort playerId)
        {
            List<BuffEffectId>? changed = null;

            foreach (var kv in _states)
            {
                if (kv.Key.PlayerId != playerId || !kv.Value.IsActive)
                    continue;

                kv.Value.IsActive = false;
                changed ??= new List<BuffEffectId>();
                changed.Add(kv.Key.EffectId);
            }

            if (changed == null)
                return;

            _nextExpirationUtcTicks = 0L;
            foreach (var effectId in changed)
                RaiseBuffChanged(playerId, effectId, isActive: false);
        }

        public static string GetBuffName(BuffEffectId effectId) => BuffDefinitionRegistry.GetName(effectId);

        private void RaiseBuffChanged(ushort playerId, BuffEffectId effectId, bool isActive)
        {
            BuffStateChanged?.Invoke(this, new BuffStateChangedEventArgs
            {
                PlayerId = playerId,
                EffectId = effectId,
                IsActive = isActive,
            });
        }

        private sealed class ActiveBuffState
        {
            public bool IsActive;
            public DateTime ActivatedAt;
            public byte RawEffectId;
            public BuffDefinition Definition = BuffDefinitionRegistry.Get(0);
            public DateTime? ExpiresAt;

            public BuffRuntimeState ToRuntimeState(ushort playerId, BuffEffectId effectId) =>
                new()
                {
                    EffectId = effectId,
                    PlayerId = playerId,
                    IsActive = IsActive,
                    ActivatedAt = ActivatedAt,
                    Duration = Definition.Duration,
                    ExpiresAt = ExpiresAt,
                    Name = Definition.Name,
                    ValueText = Definition.ValueText,
                };
        }
    }
}
