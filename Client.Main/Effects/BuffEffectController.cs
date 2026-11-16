#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Core.Client;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Effects
{
    /// <summary>
    /// Applies visual buff effects (auras, attached visuals) to player/monster objects
    /// by subscribing to BuffManager state changes.
    /// </summary>
    public class BuffEffectController
    {
        private readonly BuffManager _buffManager;
        private readonly ILogger<BuffEffectController> _logger;
        private bool _disposed;

        /// <summary>Attached buff visual effects per entity ((playerId, effectId) → visuals).</summary>
        private readonly Dictionary<(ushort PlayerId, BuffEffectId EffectId), List<WorldObject>> _buffVisuals = new();

        public BuffEffectController(BuffManager buffManager, ILoggerFactory loggerFactory)
        {
            _buffManager = buffManager;
            _logger = loggerFactory.CreateLogger<BuffEffectController>();

            _buffManager.BuffStateChanged += OnBuffStateChanged;
        }

        private void OnBuffStateChanged(object? sender, BuffStateChangedEventArgs e)
        {
            var world = MuGame.Instance?.ActiveScene as Scenes.GameScene;
            if (world == null) return;

            MuGame.ScheduleOnMainThread(() =>
            {
                if (!_disposed && ReferenceEquals(MuGame.Instance?.ActiveScene, world))
                    ApplyBuffVisual(world, e.PlayerId, e.EffectId, e.IsActive);
            });
        }

        private void ApplyBuffVisual(Scenes.GameScene scene, ushort playerId, BuffEffectId effectId, bool isActive)
        {
            ushort maskedId = (ushort)(playerId & 0x7FFF);

            switch (effectId)
            {
                case BuffEffectId.SwellLife:
                    ApplySwellLife(scene, maskedId, isActive);
                    break;

                case BuffEffectId.ManaShield:
                    // SourceMain5.2 eBuff_WizDefense: soul barrier bubble orbiting the owner.
                    ApplyAttachedVisual(scene, maskedId, effectId, isActive,
                        walker => new Objects.Effects.SpearSkillOrbitEffect(walker, 50f, Vector3.One), worldRoot: true);
                    break;

                case BuffEffectId.Stun:
                    // SourceMain5.2 eDeBuff_Stun: spinning rings above the head.
                    ApplyAttachedVisual(scene, maskedId, effectId, isActive,
                        walker => new StunRingsEffect(walker));
                    break;

                case BuffEffectId.LightningShockDamage:
                    // InsertBuffPhysicalEffect creates a one-shot impact when status 85 arrives.
                    if (isActive)
                        ApplyAttachedVisual(scene, maskedId, effectId, true,
                            walker => new Objects.Effects.LightningShockHitEffect(walker), worldRoot: true);
                    else
                        _buffVisuals.Remove((maskedId, effectId)); // The one-shot cracks finish independently.
                    break;

                case BuffEffectId.NeilDot:
                case BuffEffectId.SahamuttDot:
                case BuffEffectId.Sleep:
                case BuffEffectId.Blind:
                case BuffEffectId.AttackDown:
                case BuffEffectId.DefenseDown:
                case BuffEffectId.Thorns:
                case BuffEffectId.Berserker:
                case BuffEffectId.IgnoreDefense:
                case BuffEffectId.IncreaseHealth:
                case BuffEffectId.IncreaseBlock:
                    ApplySummonerDebuff(scene, maskedId, effectId, isActive);
                    break;

                case BuffEffectId.GreaterDamage:
                case BuffEffectId.GreaterDefense:
                case BuffEffectId.Invisible:
                    RefreshElfBuffVisuals(scene, maskedId);
                    break;

                case BuffEffectId.Poison:
                    ApplyBodyTint(scene, maskedId, effectId, isActive);
                    break;

                case BuffEffectId.Ice:
                    ApplyBodyTint(scene, maskedId, effectId, isActive);
                    break;

                default:
                    _logger?.LogTrace("No visual effect mapped for buff {EffectId}", effectId);
                    break;
            }
        }

        /// <summary>
        /// SourceMain5.2 eBuff_Life: BITMAP_LIGHT sub 1 emitter following the owner (world-level effect).
        /// </summary>
        private void ApplySwellLife(Scenes.GameScene scene, ushort playerId, bool isActive)
        {
            var key = (playerId, BuffEffectId.SwellLife);
            if (!isActive)
            {
                if (_buffVisuals.Remove(key, out var visuals))
                {
                    foreach (var visual in visuals)
                        (visual as SwellLifeBuffEffect)?.Stop();
                }
                return;
            }

            if (scene.World is not Controls.WalkableWorldControl walkableWorld) return;
            if (!walkableWorld.WalkerObjectsById.TryGetValue(playerId, out var walker)) return;

            if (_buffVisuals.TryGetValue(key, out var existing))
            {
                if (existing.Count > 0 && existing[0].Status != Models.GameControlStatus.Disposed)
                    return; // already attached
                _buffVisuals.Remove(key);
            }

            var effect = new SwellLifeBuffEffect(walker);
            walkableWorld.Objects.Add(effect);
            _ = effect.Load();
            _buffVisuals[key] = [effect];
        }

        /// <summary>
        /// SourceMain5.2 Summoner debuffs (CreateDamageOfTimeEffect, MODEL_ALICE_BUFFSKILL_EFFECT sub 3 / 4, BITMAP_SHINY + 6 /
        /// BITMAP_PIN_LIGHT sub 1 / 2): the emitter is deleted with the status, its live particles finish on their own.
        /// </summary>
        private void ApplySummonerDebuff(Scenes.GameScene scene, ushort playerId, BuffEffectId effectId, bool isActive)
        {
            var key = (playerId, effectId);
            if (!isActive)
            {
                if (_buffVisuals.Remove(key, out var visuals))
                {
                    foreach (var visual in visuals)
                    {
                        (visual as SummonDotEffect)?.Stop();
                        (visual as AliceDebuffEffect)?.Stop();
                        (visual as ThornsBuffEffect)?.Stop();
                        (visual as BerserkerBuffEffect)?.Stop();
                        (visual as RageBuffAuraEffect)?.Stop();
                    }
                }
                return;
            }

            if (scene.World is not Controls.WalkableWorldControl walkableWorld) return;
            if (_buffVisuals.TryGetValue(key, out var existing))
            {
                if (existing.Count > 0 && existing[0].Status != Models.GameControlStatus.Disposed && existing[0].World == walkableWorld)
                    return; // SearchEffect: already attached
                _buffVisuals.Remove(key);
            }

            if (!walkableWorld.WalkerObjectsById.TryGetValue(playerId, out var walker))
            {
                if (scene.Hero == null || (scene.Hero.NetworkId & 0x7FFF) != playerId)
                    return;
                walker = scene.Hero;
            }

            WorldObject effect = effectId switch
            {
                BuffEffectId.NeilDot => new SummonDotEffect(walker, neil: true),
                BuffEffectId.SahamuttDot => new SummonDotEffect(walker, neil: false),
                BuffEffectId.Thorns => new ThornsBuffEffect(walker),
                BuffEffectId.Berserker => new BerserkerBuffEffect(walker),
                BuffEffectId.IgnoreDefense => new RageBuffAuraEffect(walker, RageBuffAuraEffect.Kind.Attack),
                BuffEffectId.IncreaseHealth => new RageBuffAuraEffect(walker, RageBuffAuraEffect.Kind.Health),
                BuffEffectId.IncreaseBlock => new RageBuffAuraEffect(walker, RageBuffAuraEffect.Kind.Defense),
                BuffEffectId.Sleep => new AliceDebuffEffect(walker, AliceDebuffEffect.Kind.Sleep),
                BuffEffectId.Blind => new AliceDebuffEffect(walker, AliceDebuffEffect.Kind.Blind),
                BuffEffectId.AttackDown => new AliceDebuffEffect(walker, AliceDebuffEffect.Kind.AttackDown),
                _ => new AliceDebuffEffect(walker, AliceDebuffEffect.Kind.DefenseDown),
            };
            walkableWorld.Objects.Add(effect);
            _ = effect.Load();
            _buffVisuals[key] = [effect];
        }

        /// <summary>
        /// Attaches or removes a child visual effect on the entity.
        /// </summary>
        private readonly Dictionary<ushort, HashSet<BuffEffectId>> _tintBuffs = new();

        /// <summary>
        /// SourceMain5.2 RenderPartObject (ZzzObject.cpp:1097): eDeBuff_Poison and eDeBuff_Freeze tint the whole body
        /// via b-&gt;BodyLight - poison (0.3, 1, 0.5), freeze (0.3, 0.5, 1), both (0.3, 1, 0.8).
        /// </summary>
        private void ApplyBodyTint(Scenes.GameScene scene, ushort playerId, BuffEffectId effectId, bool isActive)
        {
            if (!_tintBuffs.TryGetValue(playerId, out var active))
                _tintBuffs[playerId] = active = new HashSet<BuffEffectId>();
            if (isActive)
                active.Add(effectId);
            else
                active.Remove(effectId);

            if (scene.World is not Controls.WalkableWorldControl walkableWorld ||
                !walkableWorld.WalkerObjectsById.TryGetValue(playerId, out var walker))
                return;

            bool poison = active.Contains(BuffEffectId.Poison);
            bool freeze = active.Contains(BuffEffectId.Ice);
            Vector3 tint = poison && freeze ? new Vector3(0.3f, 1f, 0.8f)
                : poison ? new Vector3(0.3f, 1f, 0.5f)
                : freeze ? new Vector3(0.3f, 0.5f, 1f)
                : Vector3.One;
            ApplyTintRecursive(walker, tint);
        }

        private static void ApplyTintRecursive(WorldObject obj, Vector3 tint)
        {
            if (obj is ModelObject model)
                model.StatusBodyLight = tint;
            for (int i = 0; i < obj.Children.Count; i++)
                ApplyTintRecursive(obj.Children[i], tint);
        }

        // Cloaking suppresses the attack sparks and the shared attack/defense orbit;
        // the server statuses persist, so the visuals return when cloaking ends.
        private void RefreshElfBuffVisuals(Scenes.GameScene scene, ushort playerId)
        {
            bool visible = !_buffManager.HasBuff(playerId, BuffEffectId.Invisible);
            bool attack = _buffManager.HasBuff(playerId, BuffEffectId.GreaterDamage);
            bool defense = _buffManager.HasBuff(playerId, BuffEffectId.GreaterDefense);
            ApplyAttachedVisual(scene, playerId, BuffEffectId.GreaterDamage, visible && attack,
                walker => new Objects.Effects.AttackBuffSparkEffect(walker), worldRoot: true);
            ApplyAttachedVisual(scene, playerId, BuffEffectId.GreaterDefense, visible && (attack || defense),
                walker => new Objects.Effects.SpearSkillOrbitEffect(walker, 20f, new Vector3(0.4f, 0.8f, 0.2f)), worldRoot: true);
        }

        private void ApplyAttachedVisual(
            Scenes.GameScene scene, ushort playerId, BuffEffectId effectId, bool isActive, Func<Objects.WalkerObject, WorldObject> factory,
            bool worldRoot = false)
        {
            if (scene.World is not Controls.WalkableWorldControl walkableWorld) return;

            var key = (playerId, effectId);
            if (isActive)
            {
                if (_buffVisuals.TryGetValue(key, out var existing))
                {
                    if (existing.Count > 0 && existing[0].Status != Models.GameControlStatus.Disposed && existing[0].World == walkableWorld)
                        return;
                    _buffVisuals.Remove(key);
                }

                if (!walkableWorld.WalkerObjectsById.TryGetValue(playerId, out var walker))
                {
                    if (scene.Hero == null || (scene.Hero.NetworkId & 0x7FFF) != playerId)
                        return;
                    walker = scene.Hero;
                }

                var visual = factory(walker);
                if (worldRoot)
                    walkableWorld.Objects.Add(visual);
                else
                    walker.Children.Add(visual);
                _ = visual.Load();
                _buffVisuals[key] = [visual];
                _logger?.LogDebug("Buff visual {Visual} attached to player {PlayerId}",
                    visual.GetType().Name, playerId);
            }
            else
            {
                if (!_buffVisuals.Remove(key, out var visuals))
                    return;

                foreach (var visual in visuals)
                {
                    if (visual.Parent != null)
                        visual.Parent.Children.Remove(visual);
                    else
                        visual.World?.RemoveObject(visual);
                    visual.Dispose();
                }
                _logger?.LogDebug("Buff visuals removed from player {PlayerId}", playerId);
            }
        }

        /// <summary>
        /// Release switch of AT_SKILL_WIZARDDEFENSE (ZzzCharacter.cpp:4835-4855): unless the target is cloaked,
        /// SOUND_SOULBARRIER and the five joints re-created with scale 20. The joints still live only while
        /// the server status is set.
        /// </summary>
        public void ReleaseSoulBarrier(ushort playerId)
        {
            if (_disposed)
                return;
            playerId = (ushort)(playerId & 0x7FFF);
            if (_buffManager.HasBuff(playerId, BuffEffectId.Invisible))
                return;

            Controllers.SoundController.Instance.PlayBuffer("Sound/eSoulBarrier.wav");
            if (_buffVisuals.TryGetValue((playerId, BuffEffectId.ManaShield), out var visuals) && visuals.Count > 0 &&
                visuals[0] is Objects.Effects.SpearSkillOrbitEffect orbit)
                orbit.Restart(20f);
        }

        /// <summary>Restores the local shield if its status arrived before the actor or world was ready.</summary>
        public void EnsureLocalManaShield(Scenes.GameScene scene, ushort playerId)
        {
            if (_disposed || playerId == 0)
                return;
            playerId = (ushort)(playerId & 0x7FFF);
            if (_buffManager.HasBuff(playerId, BuffEffectId.ManaShield))
                ApplyAttachedVisual(scene, playerId, BuffEffectId.ManaShield, true,
                    walker => new Objects.Effects.SpearSkillOrbitEffect(walker, 50f, Vector3.One), worldRoot: true);
        }

        /// <summary>
        /// Applies a colored aura glow effect to the entity.
        /// Creates/removes a child effect object on the walker.
        /// </summary>
        private void ApplyAura(Scenes.GameScene scene, ushort playerId, bool isActive, Color color, float auraRadius)
        {
            // Aura effects are handled as child effect objects attached to the entity.
            // For now, the BuffSlotControl UI shows the buff icon.
            // Full aura rendering requires a dedicated effect object.
            //
            // Future: Create BuffAuraEffect child object that renders a billboarded
            // radial gradient sprite around the entity.
            _logger?.LogTrace("Aura buff {Active} for player {PlayerId} (color: {Color})",
                isActive ? "applied" : "removed", playerId, color);
        }

        /// <summary>
        /// Clears all visual effects (e.g., on map change).
        /// </summary>
        public void ClearAll()
        {
            foreach (var visuals in _buffVisuals.Values)
            {
                foreach (var visual in visuals)
                {
                    if (visual.Parent != null)
                        visual.Parent.Children.Remove(visual);
                    else
                        visual.World?.RemoveObject(visual);
                    visual.Dispose();
                }
            }
            _buffVisuals.Clear();

            _logger?.LogDebug("All buff visual effects cleared");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _buffManager.BuffStateChanged -= OnBuffStateChanged;
            ClearAll();
        }
    }
}
