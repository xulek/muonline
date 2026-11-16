#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Registry for skill visual effects. Auto-discovers effect classes decorated with
    /// <see cref="SkillVisualEffectAttribute"/> at startup.
    /// </summary>
    public static class SkillVisualEffectRegistry
    {
        private static readonly Dictionary<ushort, ISkillVisualEffect> _effects = new();
        private static readonly Dictionary<ushort, ushort> _masterSkillReplaces = new();
        private static readonly ILogger? _logger;
        private static bool _initialized;

        static SkillVisualEffectRegistry()
        {
            _logger = MuGame.AppLoggerFactory?.CreateLogger("SkillVisualEffectRegistry");
        }

        /// <summary>
        /// Initializes the registry by discovering all effect classes.
        /// Call this once during game startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            DiscoverEffects();
            BuildMasterSkillFallbacks();
        }

        /// <summary>
        /// Tries to spawn a visual effect for the given skill.
        /// </summary>
        /// <param name="skillId">The skill ID.</param>
        /// <param name="context">The effect context with caster, target, and world.</param>
        /// <param name="effect">The spawned effect, or null if no effect registered.</param>
        /// <returns>True if an effect was spawned, false otherwise.</returns>
        public static bool TrySpawn(ushort skillId, SkillEffectContext context, out WorldObject? effect)
        {
            if (!_initialized)
                Initialize();

            if (TryResolveFactory(skillId, out var factory))
            {
                try
                {
                    effect = factory.CreateEffect(context);
                    if (effect != null)
                    {
                        _logger?.LogDebug("Spawned visual effect for skill {SkillId}: {EffectType}",
                            skillId, effect.GetType().Name);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating visual effect for skill {SkillId}", skillId);
                }
            }

            effect = null;
            return false;
        }

        /// <summary>
        /// Spawns the skill visual into the world and starts loading it.
        /// </summary>
        public static bool SpawnInWorld(ushort skillId, SkillEffectContext context, Client.Main.Controls.WalkableWorldControl world) =>
            SpawnInWorld(skillId, context, world, out _);

        /// <summary>
        /// Spawns the skill visual into the world and starts loading it.
        /// </summary>
        public static bool SpawnInWorld(ushort skillId, SkillEffectContext context, Client.Main.Controls.WalkableWorldControl world, out WorldObject? effect)
        {
            if (!TrySpawn(skillId, context, out effect) || effect == null)
                return false;

            WorldObject spawned = effect;
            if (spawned.World == null)
                world.Objects.Add(spawned);

            if (spawned.Status == Client.Main.Models.GameControlStatus.NonInitialized)
            {
                MuGame.TaskScheduler?.QueueTask(
                    async () => await spawned.Load(),
                    Client.Main.Controllers.TaskScheduler.Priority.High,
                    $"SkillEffect.Load.{spawned.GetType().Name}");
            }

            return true;
        }

        /// <summary>
        /// Checks if a visual effect is registered for the given skill.
        /// </summary>
        public static bool HasEffect(ushort skillId)
        {
            if (!_initialized)
                Initialize();

            return TryResolveFactory(skillId, out _);
        }

        /// <summary>
        /// Gets all registered skill IDs with visual effects.
        /// </summary>
        public static IEnumerable<ushort> GetRegisteredSkillIds()
        {
            if (!_initialized)
                Initialize();

            return _effects.Keys;
        }

        /// <summary>
        /// Master-level skills reuse the visuals of the normal skill they replace (the original
        /// client maps them back to the base AT_SKILL before AttackStage), so a strengthener or
        /// mastery id without its own factory falls back along the replacement chain.
        /// </summary>
        private static bool TryResolveFactory(ushort skillId, out ISkillVisualEffect factory)
        {
            ushort current = skillId;
            for (int depth = 0; depth < 4; depth++)
            {
                if (_effects.TryGetValue(current, out factory!))
                    return true;

                if (!_masterSkillReplaces.TryGetValue(current, out ushort replaced) || replaced == current)
                    break;

                current = replaced;
            }

            factory = null!;
            return false;
        }

        private static void BuildMasterSkillFallbacks()
        {
            foreach (var entry in ServerMasterSkills.All)
            {
                if (entry.Replaced != 0)
                    _masterSkillReplaces[entry.Number] = entry.Replaced;
            }
        }

        private static void DiscoverEffects()
        {
            var assembly = Assembly.GetExecutingAssembly();
            int registered = 0;

            var effectTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(ISkillVisualEffect).IsAssignableFrom(t))
                .Where(t => t.GetCustomAttributes<SkillVisualEffectAttribute>().Any());

            foreach (var type in effectTypes)
            {
                try
                {
                    var instance = (ISkillVisualEffect)Activator.CreateInstance(type)!;
                    var attributes = type.GetCustomAttributes<SkillVisualEffectAttribute>();

                    foreach (var attr in attributes)
                    {
                        if (_effects.TryAdd(attr.SkillId, instance))
                        {
                            registered++;
                            _logger?.LogTrace("Registered skill effect: {SkillId} => {Type}",
                                attr.SkillId, type.Name);
                        }
                        else
                        {
                            _logger?.LogWarning(
                                "Duplicate skill effect for ID {SkillId}: {Type} (already registered)",
                                attr.SkillId, type.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to instantiate skill effect: {Type}", type.Name);
                }
            }

            _logger?.LogInformation("Registered {Count} skill visual effects.", registered);
        }
    }
}
