using System.Collections.Generic;
using Client.Main;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Manages visual effects for active buffs on players.
    /// </summary>
    public class BuffEffectManager
    {
        public static BuffEffectManager Instance { get; private set; }

        private sealed class BuffVisualSet
        {
            public ElfBuffMistEmitter Left { get; set; }
            public ElfBuffMistEmitter Right { get; set; }
            public List<ElfBuffOrbitingLight> Orbits { get; set; } = new();
            public WorldObject Aura { get; set; }
        }

        private readonly Dictionary<(ushort PlayerId, byte EffectId), BuffVisualSet> _visuals = new();
        private readonly HashSet<(ushort PlayerId, byte EffectId)> _activeBuffs = new();
        
        public BuffEffectManager() => Instance = this;

        public void Update(GameTime gameTime)
        {
            // Optional: Add logic to periodically check for missing visuals or update positions
            // Most visuals (Aura, Emitters) update themselves if added to the world.
        }

        public void HandleBuff(byte effectId, ushort playerId, bool isActive)
        {
            ushort maskedId = (ushort)(playerId & 0x7FFF);
            var key = (maskedId, effectId);

            MuGame.ScheduleOnMainThread(() =>
            {
                if (isActive)
                {
                    _activeBuffs.Add(key);
                    Attach(maskedId, effectId);
                }
                else
                {
                    _activeBuffs.Remove(key);
                    Detach(maskedId, effectId);
                }
            });
        }

        private void Attach(ushort playerId, byte effectId)
        {
            var key = (playerId, effectId);
            if (!_activeBuffs.Contains(key))
                return;

            if (_visuals.TryGetValue(key, out var existing))
            {
                if (IsAlive(existing.Left) || IsAlive(existing.Right) || AreAlive(existing.Orbits) || IsAlive(existing.Aura))
                    return;

                Detach(playerId, effectId);
            }

            if (MuGame.Instance?.ActiveScene is not GameScene gameScene)
                return;

            if (gameScene.World is not WalkableWorldControl world || world.Status != GameControlStatus.Ready)
                return;

            PlayerObject target = world.FindPlayerById(playerId);
            if (target == null && gameScene.Hero != null && gameScene.Hero.NetworkId == playerId)
            {
                target = gameScene.Hero;
            }

            if (target == null)
            {
                return;
            }

            if (target.Status != GameControlStatus.Ready)
            {
                return;
            }

            var visualSet = new BuffVisualSet();

            if (effectId == 3) // Elf Soldier Buff
            {
                var left = CreateEmitter(target, PlayerObject.LeftHandBoneIndex, new Vector3(-6f, 0f, 16f));
                var right = CreateEmitter(target, PlayerObject.RightHandBoneIndex, new Vector3(6f, 0f, 16f));
                List<ElfBuffOrbitingLight> orbits = CreateOrbits(target);

                // Initialize positions
                if (target.TryGetBoneWorldMatrix(PlayerObject.LeftHandBoneIndex, out var leftMat))
                    left.Position = leftMat.Translation + new Vector3(-6f, 0f, 16f);
                else
                    left.Position = target.WorldPosition.Translation + new Vector3(-6f, 0f, 16f);

                if (target.TryGetBoneWorldMatrix(PlayerObject.RightHandBoneIndex, out var rightMat))
                    right.Position = rightMat.Translation + new Vector3(6f, 0f, 16f);
                else
                    right.Position = target.WorldPosition.Translation + new Vector3(6f, 0f, 16f);

                for (int i = 0; i < orbits.Count; i++)
                    orbits[i].Position = target.WorldPosition.Translation;

                world.Objects.Add(left);
                world.Objects.Add(right);
                for (int i = 0; i < orbits.Count; i++)
                    world.Objects.Add(orbits[i]);

                _ = left.Load();
                _ = right.Load();
                for (int i = 0; i < orbits.Count; i++)
                    _ = orbits[i].Load();

                visualSet.Left = left;
                visualSet.Right = right;
                visualSet.Orbits = orbits;
            }
            else
            {
                // Generic Auras for other buffs
                Color? auraColor = null;
                switch (effectId)
                {
                    case 1: // Greater Damage
                        auraColor = new Color(255, 100, 100); // Red
                        break;
                    case 2: // Greater Defense
                        auraColor = new Color(100, 150, 255); // Blue
                        break;
                    case 4: // Soul Barrier
                        auraColor = new Color(150, 200, 255); // Light Blue
                        break;
                    case 8: // Greater Fortitude (Swell Life)
                        auraColor = new Color(255, 150, 200); // Pink/Red
                        break;
                    case 5: // Critical Damage Increase
                        auraColor = new Color(255, 255, 100); // Yellow
                        break;
                    case 32: // Spell of Quickness / Wizardry Enhance
                        auraColor = new Color(200, 255, 255); // Cyan/White
                        break;
                }

                if (auraColor.HasValue)
                {
                    var aura = new BuffAuraEffect(target, auraColor.Value);
                    aura.Position = target.WorldPosition.Translation;
                    world.Objects.Add(aura);
                    _ = aura.Load();
                    visualSet.Aura = aura;
                }
            }

            _visuals[key] = visualSet;
        }

        public void EnsureBuffsForPlayer(ushort playerId)
        {
            foreach (var buff in _activeBuffs)
            {
                if (buff.PlayerId == playerId)
                    Attach(playerId, buff.EffectId);
            }
        }

        private void Detach(ushort playerId, byte effectId)
        {
            var key = (playerId, effectId);
            if (!_visuals.TryGetValue(key, out var visuals))
                return;

            _visuals.Remove(key);

            RemoveObject(visuals.Left);
            RemoveObject(visuals.Right);
            if (visuals.Orbits != null)
            {
                for (int i = 0; i < visuals.Orbits.Count; i++)
                    RemoveObject(visuals.Orbits[i]);
            }
            RemoveObject(visuals.Aura);
        }

        private ElfBuffMistEmitter CreateEmitter(PlayerObject target, int boneIndex, Vector3 offset)
            => new ElfBuffMistEmitter(target, boneIndex, offset);

        private List<ElfBuffOrbitingLight> CreateOrbits(PlayerObject target)
        {
            float scale = MathHelper.Clamp(target?.TotalScale ?? 1f, 0.6f, 1.4f);

            // Create two layers of orbiting lights for richer visual effect
            // Orbits encompass the entire player model
            // Inner layer: 3 orbs at mid height
            // Outer layer: 3 orbs at varied heights
            const int innerCount = 3;
            const int outerCount = 3;
            int totalCount = innerCount + outerCount;

            // Tuned a bit tighter so orbs sit closer to the player model
            float innerRadius = 65f * scale;
            float outerRadius = 95f * scale;
            float innerHeight = 70f * scale;
            float outerHeight = 95f * scale;

            var list = new List<ElfBuffOrbitingLight>(totalCount);

            // Inner layer orbs - mid-body height
            for (int i = 0; i < innerCount; i++)
            {
                float radiusJitter = MathHelper.Lerp(-8f, 12f, (float)MuGame.Random.NextDouble());
                float heightJitter = MathHelper.Lerp(-20f, 25f, (float)MuGame.Random.NextDouble());
                list.Add(new ElfBuffOrbitingLight(
                    target,
                    innerRadius + radiusJitter,
                    innerHeight + heightJitter,
                    i,
                    innerCount));
            }

            // Outer layer orbs - upper body / head height
            for (int i = 0; i < outerCount; i++)
            {
                float radiusJitter = MathHelper.Lerp(-12f, 18f, (float)MuGame.Random.NextDouble());
                float heightJitter = MathHelper.Lerp(-25f, 30f, (float)MuGame.Random.NextDouble());
                list.Add(new ElfBuffOrbitingLight(
                    target,
                    outerRadius + radiusJitter,
                    outerHeight + heightJitter,
                    i,
                    outerCount));
            }

            return list;
        }

        private static void RemoveObject(WorldObject obj)
        {
            if (obj == null)
                return;

            if (obj.Parent != null)
            {
                obj.Parent.Children.Remove(obj);
            }
            else if (obj.World != null)
            {
                obj.World.Objects.Remove(obj);
            }

            obj.Dispose();
        }

        private static bool IsAlive(WorldObject obj) =>
            obj != null && obj.Status != GameControlStatus.Disposed;

        private static bool AreAlive(List<ElfBuffOrbitingLight> orbits)
        {
            if (orbits == null || orbits.Count == 0)
                return false;

            for (int i = 0; i < orbits.Count; i++)
            {
                if (!IsAlive(orbits[i]))
                    return false;
            }

            return true;
        }
    }
}
