using System;
using System.Collections.Generic;
using Client.Data.ATT;
using Client.Main.Controls;
using Client.Main.Controls.UI.Game.Hud;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Utilities;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Objects.Pets;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Scenes
{
    internal sealed class GameSceneSkillController
    {
        private const ushort TeleportSkillId = 6;
        private const ushort SoulBarrierSkillId = 16;
        private const ushort InvisibilitySkillId = 70;
        private const ushort CancelInvisibilitySkillId = 71;
        private const ushort AbolishMagicSkillId = 72;
        private const ushort TwisterSkillId = 8;
        private const ushort HellFireSkillId = 10;
        private const ushort InfernoSkillId = 14;
        private const ushort EvilSpiritSkillId = 9;
        private const ushort NovaSkillId = 40;
        private const ushort NovaStartSkillId = 58;
        private const ushort DarkRavenCommandFirstSkillId = 120;
        private const ushort DarkRavenCommandLastSkillId = 123;

        private readonly GameScene _scene;
        private readonly ModernBottomHud _hud;
        private readonly ILogger _logger;
        private readonly Func<PlayerObject, bool> _isDuelAttackTarget;

        private Core.Client.SkillEntryState _pendingSkill;
        private ushort _pendingSkillTargetId;
        private Vector2 _pendingSkillTargetLocation;
        private bool _pendingSkillHasLocation;
        private uint _pendingSkillRange;
        private bool _pendingSkillIsArea;
        private bool _pendingSkillTargetIsPlayer;
        private readonly Dictionary<ushort, double> _nextSkillAllowedMs = new();
        private byte _nextAreaSkillAnimationCounter;
        private bool _novaCharging;

        public GameSceneSkillController(
            GameScene scene,
            ModernBottomHud hud,
            ILogger logger,
            Func<PlayerObject, bool> isDuelAttackTarget)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _hud = hud ?? throw new ArgumentNullException(nameof(hud));
            _logger = logger;
            _isDuelAttackTarget = isDuelAttackTarget ?? (_ => false);
        }

        public void Update()
        {
            UpdatePendingSkill();
            UpdateNovaState();
        }

        public void ClearPending()
        {
            ClearPendingSkill();
            if (_scene.World is WalkableWorldControl world && _scene.Hero != null)
                ForceReleaseNovaCharge(world, _scene.Hero);
            else
                _novaCharging = false;
        }

        public void NotifyLocalSkillAnimation(ushort skillId)
        {
            if (skillId == NovaStartSkillId)
            {
                _novaCharging = true;
                return;
            }

            if (skillId == NovaSkillId)
            {
                _novaCharging = false;
            }
        }

        public void HandleRightClickSkillUsage()
        {
            var mouse = MuGame.Instance.Mouse;
            var prevMouse = MuGame.Instance.PrevMouseState;
            bool rightPressed = mouse.RightButton == ButtonState.Pressed;
            bool rightJustPressed = rightPressed && prevMouse.RightButton == ButtonState.Released;
            bool rightJustReleased = !rightPressed && prevMouse.RightButton == ButtonState.Pressed;
            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released;

            var skill = _hud.SelectedSkill;
            var hero = _scene.Hero;
            var walkableForSkills = _scene.World as WalkableWorldControl;

            if (_novaCharging && (rightJustReleased || leftJustPressed))
            {
                TryReleaseNovaCharge(hero, walkableForSkills);
                return;
            }

            if (_scene.IsMouseInputConsumedThisFrame)
                return;

            if (IsMouseOverUi())
                return;

            // Allow continuous skill usage while holding right mouse button
            // The cooldown system (TryConsumeSkillDelay) will rate-limit the casting
            if (!rightPressed)
                return;

            if (skill == null)
                return;

            if (hero == null || hero.IsDead || walkableForSkills == null)
                return;

            if (IsDarkRavenCommandSkill(skill.SkillId))
            {
                TryUseDarkRavenCommand(skill.SkillId, hero, rightJustPressed);
                return;
            }

            if (skill.SkillId == NovaSkillId)
            {
                TryStartNovaCharge(skill, hero, walkableForSkills, rightJustPressed);
                return;
            }

            // Check if player is in SafeZone
            var terrainFlags = walkableForSkills.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                _logger?.LogDebug("Cannot use skill in SafeZone");
                _scene.SetMouseInputConsumed();
                return;
            }

            ClearPendingSkill();
            uint allowedRange = SkillDatabase.GetSkillRange(skill.SkillId);

            if (skill.SkillId == TeleportSkillId)
            {
                var mouseTile = new Vector2(walkableForSkills.MouseTileX, walkableForSkills.MouseTileY);
                if (IsInSkillRange(mouseTile, allowedRange))
                {
                    UseAreaSkill(skill, 0, mouseTile);
                }
                else
                {
                    _logger?.LogDebug("Teleport target out of range. Target=({X},{Y}) Range={Range}",
                        mouseTile.X, mouseTile.Y, allowedRange);
                }

                _scene.SetMouseInputConsumed();
                return;
            }

            // Invisibility targets a selected player, or the caster when nothing is selected.
            if (skill.SkillId == InvisibilitySkillId)
            {
                if (_scene.MouseHoverObject is PlayerObject invisibleTarget && invisibleTarget != hero &&
                    !invisibleTarget.IsDead && invisibleTarget.World == _scene.World)
                    UseSkillOnPlayerTarget(skill, invisibleTarget);
                else if (GetHoveredSkillTarget() == null)
                    UseSelfSkill(skill, hero);
                _scene.SetMouseInputConsumed();
                return;
            }

            // The original cancellation targets the selected character, or self if none is selected.
            if (skill.SkillId is CancelInvisibilitySkillId or AbolishMagicSkillId)
            {
                if (_scene.MouseHoverObject is PlayerObject cancelTarget && cancelTarget != hero &&
                    !cancelTarget.IsDead && cancelTarget.World == _scene.World)
                    UseSkillOnPlayerTarget(skill, cancelTarget);
                else if (GetHoveredSkillTarget() is MonsterObject cancelMonster)
                    UseSkillOnTarget(skill, cancelMonster);
                else
                    UseSelfSkill(skill, hero);
                _scene.SetMouseInputConsumed();
                return;
            }

            // Elf support skills: a selected player gets the buff (walking into range first), anything
            // else casts on the hero (AttackElf, ZzzInterface.cpp:4856-5005; SelectCharacter prefers
            // players for these skills, ZzzInterface.cpp:8086-8101).
            if (IsElfSupportSkill(skill.SkillId))
            {
                if (_scene.MouseHoverObject is PlayerObject supportTarget && supportTarget != hero &&
                    !supportTarget.IsDead && supportTarget.World == _scene.World)
                {
                    if (IsInSkillRange(supportTarget.Location, allowedRange))
                        UseSkillOnPlayerTarget(skill, supportTarget);
                    else
                        QueueSkillCast(skill, supportTarget, allowedRange, isAreaSkill: false);
                }
                else
                {
                    UseSelfSkill(skill, hero);
                }
                _scene.SetMouseInputConsumed();
                return;
            }

            // Self skills (e.g. Swell Life) are sent with the hero's own key wherever the cursor is,
            // as SourceMain5.2 does with SendRequestMagic(Skill, HeroKey) (ZzzInterface.cpp:5650).
            if (SkillDatabase.IsSelfSkill(skill.SkillId))
            {
                UseSelfSkill(skill, hero);
                _scene.SetMouseInputConsumed();
                return;
            }

            if (skill.SkillId == SoulBarrierSkillId)
            {
                if (_scene.MouseHoverObject is PlayerObject partyTarget && partyTarget != hero &&
                    !partyTarget.IsDead && partyTarget.World == _scene.World)
                {
                    if (IsPartyMember(partyTarget))
                    {
                        if (IsInSkillRange(partyTarget.Location, allowedRange))
                            UseSkillOnPlayerTarget(skill, partyTarget);
                        else
                            QueueSkillCast(skill, partyTarget, allowedRange, isAreaSkill: false);
                    }
                }
                else if (GetHoveredSkillTarget() == null)
                {
                    UseSelfSkill(skill, hero);
                }
                _scene.SetMouseInputConsumed();
                return;
            }

            var hoveredTarget = GetHoveredSkillTarget();
            if ((skill.SkillId == 76 || RequiresAreaTarget(skill.SkillId)) && hoveredTarget == null)
            {
                _scene.SetMouseInputConsumed();
                return;
            }
            if (IsAreaSkill(skill.SkillId))
            {
                if (skill.SkillId == HellFireSkillId || skill.SkillId == InfernoSkillId || skill.SkillId == EvilSpiritSkillId)
                {
                    UseAreaSkill(skill);
                    _scene.SetMouseInputConsumed();
                    return;
                }

                var skillTarget = hoveredTarget;
                var mouseTile = new Vector2(walkableForSkills.MouseTileX, walkableForSkills.MouseTileY);
                if (skillTarget == null)
                {
                    if (IsInSkillRange(mouseTile, allowedRange))
                    {
                        UseAreaSkill(skill, 0, mouseTile);
                    }
                    else
                    {
                        QueueAreaSkillCast(skill, mouseTile, allowedRange);
                    }
                }
                else if (IsInSkillRange(skillTarget.Location, allowedRange))
                {
                    UseAreaSkill(skill, skillTarget.NetworkId);
                }
                else
                {
                    QueueSkillCast(skill, skillTarget, allowedRange, isAreaSkill: true);
                }
            }
            else
            {
                if (hoveredTarget is MonsterObject targetMonster)
                {
                    if (IsInSkillRange(targetMonster.Location, allowedRange))
                        UseSkillOnTarget(skill, targetMonster);
                    else
                        QueueSkillCast(skill, targetMonster, allowedRange, isAreaSkill: false);
                }
                else if (hoveredTarget is PlayerObject targetPlayer)
                {
                    if (IsInSkillRange(targetPlayer.Location, allowedRange))
                        UseSkillOnPlayerTarget(skill, targetPlayer);
                    else
                        QueueSkillCast(skill, targetPlayer, allowedRange, isAreaSkill: false);
                }
            }

            _scene.SetMouseInputConsumed();
        }

        private void TryUseDarkRavenCommand(ushort skillId, PlayerObject hero, bool rightJustPressed)
        {
            if (!rightJustPressed)
                return;

            if (hero.EquippedHelper?.Kind != FlyingHelperKind.DarkRaven)
            {
                _logger?.LogDebug("Dark Raven command ignored because Dark Raven is not equipped.");
                _scene.SetMouseInputConsumed();
                return;
            }

            PetCommandMode commandMode = (PetCommandMode)(skillId - DarkRavenCommandFirstSkillId);
            ushort targetId = 0xFFFF;
            if (commandMode == PetCommandMode.AttackTarget)
            {
                WalkerObject target = GetHoveredSkillTarget();
                if (target == null)
                {
                    _logger?.LogDebug("Dark Raven target command ignored because no valid target is hovered.");
                    _scene.SetMouseInputConsumed();
                    return;
                }

                targetId = target.NetworkId;
            }

            _ = MuGame.Network.GetCharacterService().SendDarkRavenCommandAsync(commandMode, targetId);
            _scene.SetMouseInputConsumed();
        }

        private void UpdateNovaState()
        {
            if (!_novaCharging)
                return;

            var hero = _scene.Hero;
            if (hero == null || hero.IsDead)
            {
                if (_scene.World is WalkableWorldControl deadWorld && hero != null)
                    NovaChargeEffect.StopForCaster(deadWorld, hero.NetworkId);

                _novaCharging = false;
                return;
            }

            var selectedSkill = _hud.SelectedSkill;
            if (selectedSkill?.SkillId == NovaSkillId)
                return;

            // If player switched skill while charging, stop local charging state and visuals.
            if (_scene.World is WalkableWorldControl world)
                ForceReleaseNovaCharge(world, hero);
            else
                _novaCharging = false;
        }

        private void TryStartNovaCharge(Core.Client.SkillEntryState skill, PlayerObject hero, WalkableWorldControl world, bool rightJustPressed)
        {
            if (_novaCharging || !rightJustPressed)
                return;

            var terrainFlags = world.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                _logger?.LogDebug("Cannot use Nova in SafeZone");
                _scene.SetMouseInputConsumed();
                return;
            }

            if (hero.IsAttackOrSkillAnimationPlaying())
                return;

            if (!TryConsumeSkillDelay(NovaSkillId))
                return;

            if (!HasResourcesForNovaStart())
                return;

            _novaCharging = true;
            ClearPendingSkill();

            var startAction = hero.GetSkillAction(NovaStartSkillId, isInSafeZone: false);
            hero.PlayAction((ushort)startAction);
            hero.TriggerVehicleSkillAnimation();

            ushort targetId = hero.NetworkId != 0 ? hero.NetworkId : (ushort)_scene.Hero.NetworkId;
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaStartSkillId, targetId);

            // Start local charging visuals immediately; server packets will refine stage.
            NovaChargeEffect.GetOrCreate(world, hero);

            _logger?.LogInformation("Started Nova charge (skill {SkillId} -> start {StartSkillId})", skill.SkillId, NovaStartSkillId);
            _scene.SetMouseInputConsumed();
        }

        private void TryReleaseNovaCharge(PlayerObject hero, WalkableWorldControl world)
        {
            if (!_novaCharging)
                return;

            _novaCharging = false;

            if (hero == null || hero.IsDead || world == null)
                return;

            var releaseAction = hero.GetSkillAction(NovaSkillId, isInSafeZone: false);
            hero.PlayAction((ushort)releaseAction);
            hero.TriggerVehicleSkillAnimation();

            ushort targetId = ResolveNovaReleaseTargetId(hero);
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaSkillId, targetId);

            SpawnNovaExplosion(world, hero);

            _logger?.LogInformation("Released Nova charge with target {TargetId}", targetId);
            _scene.SetMouseInputConsumed();
        }

        private void ForceReleaseNovaCharge(WalkableWorldControl world, PlayerObject hero)
        {
            if (!_novaCharging || hero == null || hero.IsDead || world == null)
            {
                _novaCharging = false;
                return;
            }

            _novaCharging = false;

            // Leave the held PLAYER_SKILL_HELL_BEGIN loop like a normal release does.
            hero.PlayAction((ushort)hero.GetSkillAction(NovaSkillId, isInSafeZone: false));
            hero.TriggerVehicleSkillAnimation();

            ushort targetId = hero.NetworkId != 0 ? hero.NetworkId : ResolveNovaReleaseTargetId(hero);
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaSkillId, targetId);
            SpawnNovaExplosion(world, hero);
        }

        /// <summary>
        /// Consumes the current Nova charge stage and spawns the release explosion
        /// (matches remote-player path via NovaSkillEffect factory).
        /// </summary>
        private void SpawnNovaExplosion(WalkableWorldControl world, PlayerObject hero)
        {
            byte stage = NovaChargeEffect.ConsumeStageAndStop(world, hero.NetworkId);
            var explosion = new NovaReleaseEffect(hero, stage);
            world.Objects.Add(explosion);
            _ = explosion.Load();
        }

        private ushort ResolveNovaReleaseTargetId(PlayerObject hero)
        {
            var hoveredTarget = GetHoveredSkillTarget();
            if (hoveredTarget is MonsterObject monster && !monster.IsDead)
            {
                hero.FaceTowardsObject(monster, immediate: true);
                return monster.NetworkId;
            }

            if (hoveredTarget is PlayerObject player && !player.IsDead && _isDuelAttackTarget(player))
            {
                hero.FaceTowardsObject(player, immediate: true);
                return player.NetworkId;
            }

            return hero.NetworkId != 0 ? hero.NetworkId : _characterStateSafeIdFallback();

            ushort _characterStateSafeIdFallback()
            {
                var state = MuGame.Network?.GetCharacterState();
                return state?.Id ?? (ushort)0;
            }
        }

        private bool HasResourcesForNovaStart()
        {
            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState == null)
                return true;

            ushort manaCost = SkillDatabase.GetSkillManaCost(NovaSkillId);
            ushort agCost = SkillDatabase.GetSkillAGCost(NovaStartSkillId);
            if (agCost == 0)
                agCost = SkillDatabase.GetSkillAGCost(NovaSkillId);

            if (characterState.CurrentMana < manaCost)
            {
                _logger?.LogDebug("Not enough mana for Nova charge start. Required: {Required}, Current: {Current}",
                    manaCost, characterState.CurrentMana);
                return false;
            }

            if (characterState.CurrentAbility < agCost)
            {
                _logger?.LogDebug("Not enough AG for Nova charge start. Required: {Required}, Current: {Current}",
                    agCost, characterState.CurrentAbility);
                return false;
            }

            return true;
        }

        private WalkerObject GetHoveredSkillTarget()
        {
            if (_scene.World != null)
            {
                MonsterObject targetedMonster = WorldHoverSystem.FindBestLiveMonster(
                    _scene.World.VisibleObjects,
                    MuGame.Instance.MouseRay,
                    _scene.World);
                if (targetedMonster != null)
                    return targetedMonster;
            }

            if (_scene.MouseHoverObject is MonsterObject monster)
            {
                if (!monster.IsDead && !monster.IsSummoned && monster.World == _scene.World)
                    return monster;
                return null;
            }

            if (_scene.MouseHoverObject is PlayerObject player)
            {
                if (player != _scene.Hero &&
                    !player.IsDead &&
                    player.World == _scene.World &&
                    _isDuelAttackTarget(player))
                {
                    return player;
                }
            }

            return null;
        }

        private bool IsMouseOverUi()
        {
            return _scene.MouseHoverControl != null && _scene.MouseHoverControl != _scene.World;
        }

        private static bool IsAreaSkill(ushort skillId)
        {
            return SkillDatabase.IsAreaSkill(skillId);
        }

        private static bool IsDarkRavenCommandSkill(ushort skillId)
        {
            return skillId >= DarkRavenCommandFirstSkillId &&
                   skillId <= DarkRavenCommandLastSkillId;
        }

        private bool IsInSkillRange(Vector2 targetLocation, uint allowedRange)
        {
            var hero = _scene.Hero;
            if (hero == null)
                return false;

            return allowedRange == 0 || Vector2.Distance(hero.Location, targetLocation) <= allowedRange;
        }

        private void QueueSkillCast(Core.Client.SkillEntryState skill, WalkerObject target, uint allowedRange, bool isAreaSkill)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || hero == null)
                return;

            _pendingSkill = skill;
            _pendingSkillTargetId = target.NetworkId;
            _pendingSkillRange = allowedRange;
            _pendingSkillIsArea = isAreaSkill;
            _pendingSkillTargetIsPlayer = target is PlayerObject;

            MoveHeroTowardsTarget(target.Location, force: true);
        }

        private void UpdatePendingSkill()
        {
            var hero = _scene.Hero;
            if (_pendingSkill == null || hero == null || hero.IsDead)
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkill.SkillId == HellFireSkillId || _pendingSkill.SkillId == InfernoSkillId || _pendingSkill.SkillId == EvilSpiritSkillId)
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkill.SkillId == TeleportSkillId)
            {
                // Teleport is an instant skill; it shouldn't path towards the target.
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillTargetId == 0 && !_pendingSkillHasLocation)
            {
                ClearPendingSkill();
                return;
            }

            if (_hud.SelectedSkill == null || _hud.SelectedSkill.SkillId != _pendingSkill.SkillId)
            {
                ClearPendingSkill();
                return;
            }

            if (_scene.World is not WalkableWorldControl walkableWorld)
            {
                ClearPendingSkill();
                return;
            }

            var terrainFlags = walkableWorld.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillHasLocation)
            {
                if (IsInSkillRange(_pendingSkillTargetLocation, _pendingSkillRange))
                {
                    bool sent = UseAreaSkill(_pendingSkill, 0, _pendingSkillTargetLocation);
                    if (sent)
                        ClearPendingSkill();
                }
                else
                {
                    MoveHeroTowardsTarget(_pendingSkillTargetLocation, force: false);
                }
                return;
            }

            if (!walkableWorld.WalkerObjectsById.TryGetValue(_pendingSkillTargetId, out var walker))
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillTargetIsPlayer)
            {
                if (walker is not PlayerObject targetPlayer || targetPlayer.IsDead || !CanUseSkillOnPlayer(_pendingSkill.SkillId, targetPlayer))
                {
                    ClearPendingSkill();
                    return;
                }

                if (IsInSkillRange(targetPlayer.Location, _pendingSkillRange))
                {
                    bool sent = _pendingSkillIsArea
                        ? UseAreaSkill(_pendingSkill, targetPlayer.NetworkId)
                        : UseSkillOnPlayerTarget(_pendingSkill, targetPlayer);
                    if (sent)
                        ClearPendingSkill();
                }
                else
                {
                    MoveHeroTowardsTarget(targetPlayer.Location, force: false);
                }
                return;
            }

            if (walker is not MonsterObject targetMonster || targetMonster.IsDead || targetMonster.IsSummoned || targetMonster.World != _scene.World)
            {
                ClearPendingSkill();
                return;
            }

            if (IsInSkillRange(targetMonster.Location, _pendingSkillRange))
            {
                bool sent = _pendingSkillIsArea
                    ? UseAreaSkill(_pendingSkill, targetMonster.NetworkId)
                    : UseSkillOnTarget(_pendingSkill, targetMonster);
                if (sent)
                    ClearPendingSkill();
            }
            else
            {
                MoveHeroTowardsTarget(targetMonster.Location, force: false);
            }
        }

        private void ClearPendingSkill()
        {
            _pendingSkill = null;
            _pendingSkillTargetId = 0;
            _pendingSkillTargetLocation = Vector2.Zero;
            _pendingSkillHasLocation = false;
            _pendingSkillRange = 0;
            _pendingSkillIsArea = false;
            _pendingSkillTargetIsPlayer = false;
        }

        private void MoveHeroTowardsTarget(Vector2 targetLocation, bool force)
        {
            var hero = _scene.Hero;
            if (hero == null)
                return;

            if (!force && (hero.IsMoving || hero.MovementIntent))
                return;

            bool usePathfinding = !hero.IsAttackOrSkillAnimationPlaying();
            hero.MoveTo(targetLocation, sendToServer: true, usePathfinding: usePathfinding);
        }

        private bool UseSkillOnTarget(Core.Client.SkillEntryState skill, MonsterObject target)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || target.IsSummoned || hero == null)
                return false;

            if (hero.IsDead)
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            hero.FaceTowardsObject(target, immediate: true);

            _logger?.LogInformation("Using targeted skill {SkillId} (Level {Level}) on target {TargetId}",
                skill.SkillId, skill.SkillLevel, target.NetworkId);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(
                skill.SkillId,
                target.NetworkId);
            StartLocalCastAnimation(skill.SkillId, hero);
            SpawnLocalSkillVisual(skill.SkillId, hero, target.NetworkId, target.WorldPosition.Translation);

            return true;
        }

        /// <summary>
        /// Casts a skill activated by the Classic touch hotbar using its declared usage type.
        /// Target skills use the nearest monster, area skills use that monster's position,
        /// and self skills target the player's own character.
        /// </summary>
        internal bool CastSkillFromHotbar(Core.Client.SkillEntryState skill, MonsterObject target)
        {
            var hero = _scene.Hero;
            if (skill == null || hero == null || hero.IsDead)
                return false;

            if ((skill.SkillId == 76 || RequiresAreaTarget(skill.SkillId)) && target == null)
                return false;

            if (SkillDatabase.IsSelfSkill(skill.SkillId) || skill.SkillId == SoulBarrierSkillId ||
                IsElfSupportSkill(skill.SkillId))
                return UseSelfSkill(skill, hero);

            if (SkillDatabase.IsAreaSkill(skill.SkillId))
            {
                Vector2 targetLocation = target?.Location ?? hero.Location;
                ushort targetId = target?.NetworkId ?? (ushort)0;
                return UseAreaSkill(skill, targetId, targetLocation);
            }

            return target != null && UseSkillOnTarget(skill, target);
        }

        private bool UseSelfSkill(Core.Client.SkillEntryState skill, PlayerObject hero)
        {
            if (skill == null || hero == null || hero.IsDead)
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            ushort targetId = hero.NetworkId;
            if (targetId == 0)
                targetId = MuGame.Network?.GetCharacterState()?.Id ?? (ushort)0;

            _logger?.LogInformation("Using self skill {SkillId} (Level {Level}) on player {TargetId}",
                skill.SkillId, skill.SkillLevel, targetId);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(skill.SkillId, targetId);
            StartLocalCastAnimation(skill.SkillId, hero);
            SpawnLocalSkillVisual(skill.SkillId, hero, targetId, hero.WorldPosition.Translation);
            return true;
        }

        private bool UseSkillOnPlayerTarget(Core.Client.SkillEntryState skill, PlayerObject target)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || hero == null)
                return false;

            if (hero.IsDead || target.IsDead)
                return false;

            if (!CanUseSkillOnPlayer(skill.SkillId, target))
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            hero.FaceTowardsObject(target, immediate: true);

            _logger?.LogInformation("Using targeted skill {SkillId} (Level {Level}) on player {TargetId}",
                skill.SkillId, skill.SkillLevel, target.NetworkId);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(
                skill.SkillId,
                target.NetworkId);
            StartLocalCastAnimation(skill.SkillId, hero);
            SpawnLocalSkillVisual(skill.SkillId, hero, target.NetworkId, target.WorldPosition.Translation);

            return true;
        }

        private static bool IsPartyMember(PlayerObject target) =>
            MuGame.Network?.GetPartyManager()?.IsMember(target.NetworkId) == true;

        // AT_SKILL_HEALING / DEFENSE / ATTACK / RECOVER.
        private static bool IsElfSupportSkill(ushort skillId) => skillId is 26 or 27 or 28 or 234;

        private bool CanUseSkillOnPlayer(ushort skillId, PlayerObject target) =>
            skillId is InvisibilitySkillId or CancelInvisibilitySkillId or AbolishMagicSkillId ||
            IsElfSupportSkill(skillId) ||
            (skillId == SoulBarrierSkillId ? IsPartyMember(target) : _isDuelAttackTarget(target));

        private bool UseAreaSkill(Core.Client.SkillEntryState skill, ushort extraTargetId = 0, Vector2? targetLocationOverride = null)
        {
            var hero = _scene.Hero;
            if (skill == null || hero == null)
                return false;

            if (RequiresAreaTarget(skill.SkillId) && extraTargetId == 0)
                return false;

            if (hero.IsDead)
                return false;

            Vector2 targetTile = hero.Location;
            if (skill.SkillId != HellFireSkillId && skill.SkillId != InfernoSkillId && skill.SkillId != EvilSpiritSkillId)
            {
                if (targetLocationOverride.HasValue)
                {
                    targetTile = targetLocationOverride.Value;
                }
                else if (_scene.World is WalkableWorldControl world)
                {
                    if (extraTargetId != 0 && world.TryGetWalkerById(extraTargetId, out var target))
                        targetTile = target.Location;
                    else
                        targetTile = new Vector2(world.MouseTileX, world.MouseTileY);
                }
            }

            byte targetX = (byte)Math.Clamp((int)targetTile.X, 0, Constants.TERRAIN_SIZE - 1);
            byte targetY = (byte)Math.Clamp((int)targetTile.Y, 0, Constants.TERRAIN_SIZE - 1);
            byte requestTargetX = targetX;
            byte requestTargetY = targetY;

            if (skill.SkillId == TeleportSkillId)
            {
                if (_scene.World is WorldControl worldForTeleport &&
                    !worldForTeleport.IsWalkable(new Vector2(targetX, targetY)))
                {
                    _logger?.LogDebug("Teleport target ({X},{Y}) is not walkable.", targetX, targetY);
                    return false;
                }
            }

            if (!TryBeginSkillCast(skill, hero))
                return false;

            if (extraTargetId != 0 && _scene.World is WalkableWorldControl faceWorld &&
                faceWorld.TryGetWalkerById(extraTargetId, out var faceTarget))
                hero.FaceTowardsObject(faceTarget, immediate: true);
            else
                hero.FaceTowards(new Vector2(targetX, targetY), immediate: true);

            var characterState = MuGame.Network?.GetCharacterState();

            if (UsesCasterTileForAreaRequest(skill.SkillId))
            {
                requestTargetX = (byte)Math.Clamp((int)hero.Location.X, 0, Constants.TERRAIN_SIZE - 1);
                requestTargetY = (byte)Math.Clamp((int)hero.Location.Y, 0, Constants.TERRAIN_SIZE - 1);
            }

            if (skill.SkillId == TeleportSkillId)
            {
                _logger?.LogInformation("Using teleport skill {SkillId} (Level {Level}) to position ({X},{Y})",
                    skill.SkillId, skill.SkillLevel, targetX, targetY);

                characterState?.BeginTeleport();

                hero.StopMovement();
                // AT_SKILL_TELEPORT start flash; the arrival flash is spawned on MapChanged.
                TeleportSparkEffect.Spawn(_scene.World, hero.WorldPosition.Translation, hero);
                hero.Hidden = true; // Hide hero until server responds

                _ = MuGame.Network.GetCharacterService().SendEnterGateRequestAsync(0, targetX, targetY);
                return true;
            }

            byte animationCounter = NextAreaSkillAnimationCounter();
            if (characterState != null)
            {
                characterState.LastAreaSkillId = skill.SkillId;
                characterState.LastAreaSkillTargetX = requestTargetX;
                characterState.LastAreaSkillTargetY = requestTargetY;
                characterState.LastAreaSkillTargetId = extraTargetId;
                characterState.LastAreaSkillAnimationCounter = animationCounter;
                characterState.LastAreaSkillSentAtMs = GetNowMs();
            }

            if (extraTargetId != 0)
            {
                _logger?.LogInformation("Using skill {SkillId} (Level {Level}) at position ({X},{Y}) with target {TargetId}",
                    skill.SkillId, skill.SkillLevel, requestTargetX, requestTargetY, extraTargetId);
            }
            else
            {
                _logger?.LogInformation("Using area skill {SkillId} (Level {Level}) at position ({X},{Y})",
                    skill.SkillId, skill.SkillLevel, requestTargetX, requestTargetY);
            }

            float angleZ = MathHelper.WrapAngle(hero.Angle.Z);
            if (angleZ < 0f)
            {
                angleZ += MathHelper.TwoPi;
            }
            byte rotation = (byte)(angleZ / MathHelper.TwoPi * 256f);

            _ = MuGame.Network.GetCharacterService().SendAreaSkillRequestAsync(
                skill.SkillId,
                requestTargetX,
                requestTargetY,
                rotation,
                extraTargetId: extraTargetId,
                animationCounter: animationCounter);
            StartLocalCastAnimation(skill.SkillId, hero);
            if (_scene.World is WalkableWorldControl areaWorld)
            {
                float worldX = (requestTargetX + 0.5f) * Constants.TERRAIN_SCALE;
                float worldY = (requestTargetY + 0.5f) * Constants.TERRAIN_SCALE;
                var areaPoint = new Vector3(worldX, worldY, areaWorld.Terrain.RequestTerrainHeight(worldX, worldY));
                SpawnLocalSkillVisual(skill.SkillId, hero, IsDrainLife(skill.SkillId) ? extraTargetId : (ushort)0, areaPoint);
            }

            return true;
        }

        /// <summary>
        /// SourceMain5.2 plays the hero's cast action when the request is sent (UseSkillWizard: SendRequestMagic +
        /// SetPlayerMagic, ZzzInterface.cpp:2385); ReceiveMagic only restarts AttackTime for the hero
        /// (WSclient.cpp:3930), so the release gate runs on an animation that is already playing.
        /// </summary>
        private static void StartLocalCastAnimation(ushort skillId, PlayerObject hero)
        {
            if (skillId is NovaSkillId or NovaStartSkillId or TeleportSkillId)
                return;

            int animationId = ArrowProjectileSpawner.IsArrowSkill(skillId)
                ? (int)hero.GetArrowSkillAnimation(skillId)
                : hero.GetPacketSkillAnimation(skillId);
            animationId = hero.ApplySlashSwordCount(skillId, animationId);
            if (animationId > 0)
                hero.BeginLocalCast(skillId, animationId);
        }

        /// <summary>
        /// MoveCharacter releases the hero's skill from its own Skill / AttackTime state set at request time, so the
        /// visual starts with the local cast; the server echo then skips it (PlayerObject.ConsumeLocalSkillVisual).
        /// </summary>
        private void SpawnLocalSkillVisual(ushort skillId, PlayerObject hero, ushort targetId, Vector3 targetPosition)
        {
            if (skillId is NovaSkillId or NovaStartSkillId or TeleportSkillId ||
                _scene.World is not WalkableWorldControl world)
                return;

            var context = new Objects.Effects.Skills.SkillEffectContext
            {
                Caster = hero,
                TargetId = targetId,
                SkillId = skillId,
                TargetPosition = targetPosition,
                World = world
            };
            if (!Objects.Effects.Skills.SkillVisualEffectRegistry.SpawnInWorld(skillId, context, world, out WorldObject? effect) ||
                effect == null)
                return;

            hero.NoteLocalSkillVisual(skillId);
            ReleaseImmediately(effect);
        }

        private static void ReleaseImmediately(WorldObject effect)
        {
            if (effect is Objects.Effects.Joints.SourceSkillEffectBase sourceEffect)
                sourceEffect.ReleaseImmediately();

            var children = effect.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Objects.Effects.Joints.SourceSkillEffectBase child)
                    child.ReleaseImmediately();
            }
        }

        // These skills aim at the selected tile but send the caster's tile in the request.
        private static bool UsesCasterTileForAreaRequest(ushort skillId) =>
            skillId is TwisterSkillId or 236 or 237 or 76 or 230 or 456 or 24 or 52; // 24 / 52: caster tile + angle + target key; 237: ZzzInterface.cpp:5516

        private static bool IsDrainLife(ushort skillId) => skillId is 214 or 458;

        // ZzzInterface.cpp:6373: Drain Life and Chain Lightning send the selected character's key with the area request.
        private static bool RequiresAreaTarget(ushort skillId) => IsDrainLife(skillId) || skillId == 215;

        private void QueueAreaSkillCast(Core.Client.SkillEntryState skill, Vector2 targetLocation, uint allowedRange)
        {
            var hero = _scene.Hero;
            if (skill == null || hero == null)
                return;

            _pendingSkill = skill;
            _pendingSkillTargetId = 0;
            _pendingSkillTargetLocation = targetLocation;
            _pendingSkillHasLocation = true;
            _pendingSkillRange = allowedRange;
            _pendingSkillIsArea = true;
            _pendingSkillTargetIsPlayer = false;

            MoveHeroTowardsTarget(targetLocation, force: true);
        }

        private bool TryBeginSkillCast(Core.Client.SkillEntryState skill, PlayerObject hero)
        {
            if (hero.IsAttackOrSkillAnimationPlaying())
                return false;

            if (!TryConsumeSkillDelay(skill.SkillId))
                return false;

            // Check player resources and stat requirements (mirrors SourceMain CSkillManager checks)
            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState != null)
            {
                ushort manaCost = SkillDatabase.GetSkillManaCost(skill.SkillId);
                ushort agCost = SkillDatabase.GetSkillAGCost(skill.SkillId);

                if (characterState.CurrentMana < manaCost)
                {
                    _logger?.LogDebug("Not enough mana to use skill {SkillId}. Required: {Required}, Current: {Current}",
                        skill.SkillId, manaCost, characterState.CurrentMana);
                    return false;
                }

                if (characterState.CurrentAbility < agCost)
                {
                    _logger?.LogDebug("Not enough AG to use skill {SkillId}. Required: {Required}, Current: {Current}",
                        skill.SkillId, agCost, characterState.CurrentAbility);
                    return false;
                }

                // Stat requirement check (SourceMain: DemendConditionCheckSkill)
                var demand = Core.Client.SkillManager.GetUnmetDemand(characterState, skill.SkillId, out int required);
                if (demand != Core.Client.SkillManager.SkillDemand.None)
                {
                    _logger?.LogDebug("Skill {SkillId} demand not met: {Demand} {Required}", skill.SkillId, demand, required);
                    return false;
                }
            }

            bool isInSafeZone = false;
            if (_scene.World is WalkableWorldControl walkableWorld)
            {
                var flags = walkableWorld.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
                isInSafeZone = flags.HasFlag(TWFlags.SafeZone);
            }

            var action = hero.GetSkillAction(skill.SkillId, isInSafeZone);
            hero.PlayAction((ushort)action);
            hero.TriggerVehicleSkillAnimation();
            return true;
        }

        private bool TryConsumeSkillDelay(ushort skillId)
        {
            double now = GetNowMs();
            if (!Core.Client.SkillCooldownTracker.TryConsume(skillId, now))
                return false;

            // Mirror to local tracking for backward compat
            int delayMs = SkillDatabase.GetSkillCooldown(skillId);
            if (delayMs <= 0)
                return true;

            _nextSkillAllowedMs[skillId] = now + delayMs;
            return true;
        }

        private static double GetNowMs()
        {
            var gameTime = MuGame.Instance?.GameTime;
            if (gameTime != null)
                return gameTime.TotalGameTime.TotalMilliseconds;

            return Environment.TickCount64;
        }

        private byte NextAreaSkillAnimationCounter()
        {
            // Mirrors original client behavior: a small rolling serial number is used
            // to tie AreaSkillHit packets to the AreaSkill animation.
            _nextAreaSkillAnimationCounter++;
            if (_nextAreaSkillAnimationCounter > 50)
                _nextAreaSkillAnimationCounter = 1;

            return _nextAreaSkillAnimationCounter;
        }
    }
}
