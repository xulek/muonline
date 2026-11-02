using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Everyday gameplay packets that had no handler: master level up, fruits, item effects, consumables with
    /// effect, visual effects, fireworks/fanfare, vault state, pet info, summon health, poison damage and the
    /// server-stored key configuration. Mirrors the corresponding WSclient.cpp Receive* functions.
    /// </summary>
    public class GameplayHandler : IGamePacketHandler
    {
        private readonly ILogger<GameplayHandler> _logger;
        private readonly CharacterState _characterState;
        private readonly TargetProtocolVersion _targetVersion;

        public GameplayHandler(ILoggerFactory loggerFactory, CharacterState characterState, TargetProtocolVersion targetVersion)
        {
            _logger = loggerFactory.CreateLogger<GameplayHandler>();
            _characterState = characterState;
            _targetVersion = targetVersion;
        }

        // ─────────────────────────── Master level (F3/51) ───────────────────────────

        /// <summary>MasterCharacterLevelUpdate (0xF3/0x51), normal (20 bytes) or extended (28 bytes) layout.</summary>
        [PacketHandler(0xF3, 0x51)]
        public Task HandleMasterCharacterLevelUpdateAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 20)
            {
                _logger.LogWarning("MasterCharacterLevelUpdate too short: {Length}", span.Length);
                return Task.CompletedTask;
            }

            ushort masterLevel = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            ushort gained = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
            ushort points = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            uint maxHp, maxMana, maxSd, maxAbility;
            if (span.Length >= 28)
            {
                maxHp = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
                maxMana = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
                maxSd = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
                maxAbility = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);
            }
            else
            {
                maxHp = BinaryPrimitives.ReadUInt16LittleEndian(span[12..]);
                maxMana = BinaryPrimitives.ReadUInt16LittleEndian(span[14..]);
                maxSd = BinaryPrimitives.ReadUInt16LittleEndian(span[16..]);
                maxAbility = BinaryPrimitives.ReadUInt16LittleEndian(span[18..]);
            }

            ushort oldLevel = _characterState.MasterLevel;
            _characterState.UpdateMasterLevelAndExperience(masterLevel, _characterState.MasterExperience, _characterState.MasterExperienceForNextLevel, points);
            _characterState.UpdateMaximumHealthShield(maxHp, maxSd);
            _characterState.UpdateMaximumManaAbility(maxMana, maxAbility);
            _logger.LogInformation("Master level up: {Level} (+{Gained} points, {Points} available).", masterLevel, gained, points);

            if (masterLevel > oldLevel)
            {
                MuGame.ScheduleOnMainThread(async () =>
                {
                    if (MuGame.Instance?.ActiveScene is not GameScene scene || scene.World == null || scene.Hero == null) return;
                    var effect = new LevelUpEffect(scene.Hero.Position, masterLevel: true, angle: scene.Hero.Angle);
                    scene.World.Objects.Add(effect);
                    await effect.Load();
                    scene.ChatLog?.AddMessage("System", $"You have reached Master Level {masterLevel}! You received {gained} master point(s), {points} available.", MessageType.System);
                });
            }

            return Task.CompletedTask;
        }

        // ─────────────────────────── Fruits (2C) ───────────────────────────

        /// <summary>FruitConsumptionResponse (0x2C): result of eating a fruit (adds/removes stat points).</summary>
        [PacketHandler(0x2C, PacketRouter.NoSubCode)]
        public Task HandleFruitConsumptionResponseAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 7) return Task.CompletedTask;

            byte result = span[3];
            ushort points = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            string stat = span[6] switch { 0 => "Energy", 1 => "Vitality", 2 => "Agility", 3 => "Strength", 4 => "Command", _ => "stat" };

            // Successful consumptions change the stat value itself.
            if (result is 0 or 3 or 6)
            {
                int delta = result == 0 ? points : -points;
                ushort str = _characterState.Strength, agi = _characterState.Agility, vit = _characterState.Vitality, ene = _characterState.Energy, cmd = _characterState.Leadership;
                switch (span[6])
                {
                    case 0: ene = (ushort)Math.Max(0, ene + delta); break;
                    case 1: vit = (ushort)Math.Max(0, vit + delta); break;
                    case 2: agi = (ushort)Math.Max(0, agi + delta); break;
                    case 3: str = (ushort)Math.Max(0, str + delta); break;
                    case 4: cmd = (ushort)Math.Max(0, cmd + delta); break;
                }
                _characterState.UpdateStats(str, agi, vit, ene, cmd);
            }

            string text = result switch
            {
                0 => $"{points} point(s) of {stat} have been added.",
                3 or 6 => $"{points} point(s) of {stat} have been removed.",
                1 => "The fruit had no effect.",
                4 => "The fruit had no effect.",
                2 or 5 => "You can't use this fruit now.",
                16 => "Unequip all items before using a fruit.",
                33 => $"You can't add more {stat} with fruits.",
                37 => $"You can't remove more {stat} with fruits.",
                38 => $"{stat} can't be reduced below the base value of your class.",
                _ => $"Fruit result {result}.",
            };
            ShowSystemMessage(text, result is 0 or 3 or 6 ? MessageType.System : MessageType.Error);
            return Task.CompletedTask;
        }

        // ─────────────────────────── Item effects (2D) and consumables (29) ───────────────────────────

        /// <summary>EffectItemConsumption (0x2D): buff of an item (seals, scrolls, elixirs, event items) with remaining time.</summary>
        [PacketHandler(0x2D, PacketRouter.NoSubCode)]
        public Task HandleEffectItemConsumptionAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 17) return Task.CompletedTask;

            byte type = span[6];
            byte action = span[8];
            uint seconds = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
            byte effect = span[16];
            string name = GetItemEffectName(type);

            if (action == 0) // Add
                _characterState.ActivateBuff(effect, _characterState.Id, seconds > 0 ? TimeSpan.FromSeconds(seconds) : null, name);
            else // Remove / Replace
                _characterState.DeactivateBuff(effect, _characterState.Id);

            _logger.LogDebug("Item effect {Effect} ({Name}) {Action}, {Seconds}s.", effect, name, action == 0 ? "added" : "removed", seconds);
            return Task.CompletedTask;
        }

        /// <summary>Pseudo effect ids for ConsumeItemWithEffect, outside the S6 magic effect range.</summary>
        private const byte AleEffectId = 240, RemedyOfLoveEffectId = 241, PotionOfSoulEffectId = 242;

        /// <summary>ConsumeItemWithEffect (0x29): Ale, Remedy of Love or Potion of Soul with its effect time.</summary>
        [PacketHandler(0x29, PacketRouter.NoSubCode)]
        public Task HandleConsumeItemWithEffectAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 6) return Task.CompletedTask;

            ushort seconds = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            (byte Id, string Name) effect = span[3] switch
            {
                0 => (AleEffectId, "Ale"),
                1 => (RemedyOfLoveEffectId, "Remedy of Love"),
                77 => (PotionOfSoulEffectId, "Potion of Soul"),
                _ => (0, string.Empty),
            };
            if (effect.Id == 0) return Task.CompletedTask;

            _characterState.ActivateBuff(effect.Id, _characterState.Id, TimeSpan.FromSeconds(seconds), effect.Name);
            ShowSystemMessage($"{effect.Name} takes effect for {seconds} seconds.", MessageType.System);
            return Task.CompletedTask;
        }

        private static string GetItemEffectName(byte type) => type switch
        {
            1 => "Attack Speed",
            2 => "Damage",
            3 => "Defense",
            4 => "Maximum Health",
            5 => "Maximum Mana",
            6 => "Experience Rate",
            7 => "Drop Rate",
            8 => "Sustenance",
            9 => "Strength",
            10 => "Agility",
            11 => "Vitality",
            12 => "Energy",
            13 => "Command",
            14 => "Physical Damage",
            15 => "Wizardry Damage",
            16 => "Mobility",
            _ => "Item effect",
        };

        // ─────────────────────────── Visual effects (48, F3/40) ───────────────────────────

        /// <summary>ShowEffect (0x48): shield potion, level up or shield lost effect on a player (ReceiveDisplayEffectViewport).</summary>
        [PacketHandler(0x48, PacketRouter.NoSubCode)]
        public Task HandleShowEffectAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 6) return Task.CompletedTask;

            ushort playerId = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(span[3..]) & 0x7FFF);
            byte effect = span[5];
            MuGame.ScheduleOnMainThread(async () =>
            {
                if (MuGame.Instance?.ActiveScene is not GameScene scene || scene.World is not WorldControl world) return;
                if (!world.TryGetWalkerById(playerId, out var walker) || walker == null) return;
                switch (effect)
                {
                    case 16: // LevelUp
                    {
                        SoundController.Instance.PlayBuffer("Sound/pLevelUp.wav");
                        bool master = walker is Objects.Player.PlayerObject player && IsMasterClass(player.CharacterClass);
                        var levelUp = new LevelUpEffect(walker.Position, masterLevel: master, angle: walker.Angle);
                        world.Objects.Add(levelUp);
                        await levelUp.Load();
                        break;
                    }
                    case 3: // ShieldPotion
                        SoundController.Instance.PlayBuffer("Sound/pDrink.wav");
                        break;
                }
            });
            return Task.CompletedTask;
        }

        /// <summary>
        /// 0xF3/0x40: ServerCommand family – fireworks (0), fanfare (2), swirl (58), christmas fireworks (59) and
        /// dialog commands (ReceiveServerCommand). Dialog texts come from GlobalText which this client does not load.
        /// </summary>
        [PacketHandler(0xF3, 0x40)]
        public Task HandleServerCommandAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 7) return Task.CompletedTask;

            byte type = span[4];
            byte p1 = span[5], p2 = span[6];
            switch (type)
            {
                case 0:  // fireworks at (x, y)
                case 59: // christmas fireworks
                    _logger.LogInformation("Fireworks ({Type}) at ({X}, {Y}).", type, p1, p2);
                    SoundController.Instance.PlayBuffer("Sound/eFirecracker1.wav");
                    SpawnCelebrationAt(p1, p2);
                    break;
                case 2: // fanfare (SOUND_MEDAL)
                    SoundController.Instance.PlayBuffer("Sound/eMedal.wav");
                    break;
                case 58: // swirl on an object
                {
                    ushort target = (ushort)(((p1 << 8) | p2) & 0x7FFF);
                    MuGame.ScheduleOnMainThread(async () =>
                    {
                        if (MuGame.Instance?.ActiveScene is not GameScene scene || scene.World is not WorldControl world) return;
                        if (!world.TryGetWalkerById(target, out var walker) || walker == null) return;
                        var swirl = new LevelUpEffect(walker.Position, masterLevel: false, angle: walker.Angle);
                        world.Objects.Add(swirl);
                        await swirl.Load();
                    });
                    break;
                }
                default:
                    _logger.LogInformation("Server command {Type} ({P1}, {P2}) has no client action.", type, p1, p2);
                    break;
            }

            return Task.CompletedTask;
        }

        private static void SpawnCelebrationAt(byte x, byte y)
        {
            MuGame.ScheduleOnMainThread(async () =>
            {
                if (MuGame.Instance?.ActiveScene is not GameScene scene || scene.World is not WorldControl world || world.Terrain == null) return;
                float wx = (x + 0.5f) * Constants.TERRAIN_SCALE, wy = (y + 0.5f) * Constants.TERRAIN_SCALE;
                var position = new Vector3(wx, wy, world.Terrain.RequestTerrainHeight(wx, wy));
                var effect = new LevelUpEffect(position, masterLevel: false, angle: Vector3.Zero);
                world.Objects.Add(effect);
                await effect.Load();
            });
        }

        private static bool IsMasterClass(MUnique.OpenMU.Network.Packets.CharacterClassNumber characterClass) => characterClass is
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.GrandMaster or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.BladeMaster or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.HighElf or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.LordEmperor or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.DuelMaster or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.DimensionMaster or
            MUnique.OpenMU.Network.Packets.CharacterClassNumber.FistMaster;

        // ─────────────────────────── Vault (82, 83) ───────────────────────────

        /// <summary>VaultClosed (0x82): the server closed the vault (e.g. after moving away).</summary>
        [PacketHandler(0x82, PacketRouter.NoSubCode)]
        public Task HandleVaultClosedAsync(Memory<byte> packet)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (VaultControl.Instance.Visible)
                    VaultControl.Instance.CloseWindow();
            });
            return Task.CompletedTask;
        }

        /// <summary>VaultProtectionInformation (0x83): vault lock state (ReceiveStorageStatus).</summary>
        [PacketHandler(0x83, PacketRouter.NoSubCode)]
        public Task HandleVaultProtectionInformationAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 4) return Task.CompletedTask;
            byte state = span[3];
            MuGame.ScheduleOnMainThread(() =>
            {
                _characterState.SetVaultProtectionState(state);
                string? text = state switch
                {
                    1 => "The vault is locked. Unlock it with your PIN to move items or Zen.",
                    10 => "Wrong vault PIN.",
                    11 => "Unlock the vault before changing the PIN.",
                    12 => "The vault has been unlocked.",
                    13 => "Wrong password: the vault PIN was not removed.",
                    _ => null,
                };
                if (text != null)
                    ShowSystemMessage(text, state is 10 or 11 or 13 ? MessageType.Error : MessageType.System);
            });
            return Task.CompletedTask;
        }

        // ─────────────────────────── Pets (A9), summons (F3/20) ───────────────────────────

        /// <summary>PetInfoResponse (0xA9): level, experience and health of a Dark Horse / Dark Raven item.</summary>
        [PacketHandler(0xA9, PacketRouter.NoSubCode)]
        public Task HandlePetInfoResponseAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 13) return Task.CompletedTask;
            _characterState.SetPetInfo(span[4], span[5], span[6], BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), span[12]);
            return Task.CompletedTask;
        }

        /// <summary>SummonHealthUpdate (0xF3/0x20): health of the own summoned monster (ReceiveSummonLife).</summary>
        [PacketHandler(0xF3, 0x20)]
        public Task HandleSummonHealthUpdateAsync(Memory<byte> packet)
        {
            if (packet.Length >= 5)
                _characterState.SummonHealthPercent = packet.Span[4] == 0 ? null : packet.Span[4];
            return Task.CompletedTask;
        }

        /// <summary>PoisonDamage (0xF3/0x07, old clients): removes health without a damage number.</summary>
        [PacketHandler(0xF3, 0x07)]
        public Task HandlePoisonDamageAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 8) return Task.CompletedTask;
            ushort damage = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort shield = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            uint health = _characterState.CurrentHealth > damage ? _characterState.CurrentHealth - damage : 0;
            _characterState.UpdateCurrentHealthShield(health, shield);
            return Task.CompletedTask;
        }

        // ─────────────────────────── Key configuration (F3/30) ───────────────────────────

        /// <summary>ApplyKeyConfiguration (0xF3/0x30): skill and potion hotkeys stored on the server (ReceiveOption).</summary>
        [PacketHandler(0xF3, 0x30)]
        public Task HandleApplyKeyConfigurationAsync(Memory<byte> packet)
        {
            _logger.LogDebug("ApplyKeyConfiguration raw ({Length}): {Hex}", packet.Length, Convert.ToHexString(packet.Span));
            var config = KeyConfiguration.Parse(packet.Span[4..]);
            if (config == null)
            {
                _logger.LogWarning("ApplyKeyConfiguration too short: {Length}", packet.Length);
                return Task.CompletedTask;
            }

            MuGame.ScheduleOnMainThread(() => _characterState.SetServerKeyConfiguration(config));
            return Task.CompletedTask;
        }

        private static void ShowSystemMessage(string text, MessageType type)
        {
            MuGame.ScheduleOnMainThread(() =>
                (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, type));
        }
    }
}
