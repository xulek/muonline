using System;
using System.Collections.Generic;
using Client.Data.ATT;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Events
{
    public sealed class ObjectAmbientSoundSystem : EffectObject
    {
        private readonly WalkableWorldControl _world;
        private readonly IReadOnlyDictionary<int, string> _soundsByType;
        private readonly float _maxDistance;
        private readonly double _intervalMs;
        private readonly IReadOnlySet<int> _safeZoneSuppressedTypes;
        private readonly Dictionary<int, double> _lastPlayedMs = new();
        private readonly List<MapTileObject> _sources = new();
        private readonly Dictionary<int, MapTileObject> _nearestSources = new();
        private bool _sourcesCollected;

        public ObjectAmbientSoundSystem(
            WalkableWorldControl world,
            IReadOnlyDictionary<int, string> soundsByType,
            float maxDistance = 900f,
            double intervalMs = 2500.0,
            IReadOnlySet<int> safeZoneSuppressedTypes = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _soundsByType = soundsByType ?? throw new ArgumentNullException(nameof(soundsByType));
            _maxDistance = maxDistance;
            _intervalMs = intervalMs;
            _safeZoneSuppressedTypes = safeZoneSuppressedTypes;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_world.Status != GameControlStatus.Ready || _world.Walker == null || _world.Terrain == null)
                return;

            if (!_sourcesCollected)
                CollectSources();

            if (_sources.Count == 0)
                return;

            bool heroInSafeZone = _safeZoneSuppressedTypes != null && IsHeroInSafeZone();
            Vector3 heroPosition = _world.Walker.Position;
            double nowMs = gameTime.TotalGameTime.TotalMilliseconds;
            _nearestSources.Clear();

            foreach (var source in _sources)
            {
                if (source.Status != GameControlStatus.Ready)
                    continue;

                if (!_soundsByType.ContainsKey(source.Type))
                    continue;

                if (heroInSafeZone && _safeZoneSuppressedTypes.Contains(source.Type))
                    continue;

                if (Vector3.DistanceSquared(source.Position, heroPosition) > _maxDistance * _maxDistance)
                    continue;

                if (_lastPlayedMs.TryGetValue(source.Type, out double lastMs) && nowMs - lastMs < _intervalMs)
                    continue;

                KeepNearestSource(_nearestSources, source, heroPosition);
            }

            foreach (var (type, source) in _nearestSources)
            {
                _lastPlayedMs[type] = nowMs;
                SoundController.Instance.PlayBufferWithAttenuation(
                    _soundsByType[type], source.Position, heroPosition, _maxDistance);
            }
        }

        private static void KeepNearestSource(Dictionary<int, MapTileObject> nearest, MapTileObject source, Vector3 listener)
        {
            if (!nearest.TryGetValue(source.Type, out var previous) ||
                Vector3.DistanceSquared(source.Position, listener) < Vector3.DistanceSquared(previous.Position, listener))
                nearest[source.Type] = source;
        }

        private void CollectSources()
        {
            _sourcesCollected = true;

            foreach (var obj in _world.Objects)
            {
                if (obj is MapTileObject tile && _soundsByType.ContainsKey(tile.Type))
                    _sources.Add(tile);
            }
        }

        private bool IsHeroInSafeZone()
        {
            int tileX = (int)(_world.Walker.Position.X / Constants.TERRAIN_SCALE);
            int tileY = (int)(_world.Walker.Position.Y / Constants.TERRAIN_SCALE);
            if (tileX < 0 || tileX >= Constants.TERRAIN_SIZE || tileY < 0 || tileY >= Constants.TERRAIN_SIZE)
                return false;

            var flag = _world.Terrain.RequestTerrainFlag(tileX, tileY);
            return flag.HasFlag(TWFlags.SafeZone);
        }
    }
}
