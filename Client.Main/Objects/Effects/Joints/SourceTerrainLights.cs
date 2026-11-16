#nullable enable
using Client.Main.Controls;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// SourceMain5.2 AddTerrainLight(x, y, Light, Range, PrimaryTerrainLight) (ZzzLodTerrain.cpp:892) for effects that
    /// call it every tick: one reusable <see cref="DynamicLight"/> per slot, radius = Range tiles. The original adds
    /// the light to the terrain light buffer with a linear falloff over Range tiles; here the dynamic light system
    /// applies its own falloff. A slot is registered on its first use and unregistered by <see cref="Clear"/>.
    /// </summary>
    internal sealed class SourceTerrainLights
    {
        private readonly WorldObject _owner;
        private readonly DynamicLight?[] _lights;
        private readonly bool[] _registered;
        private TerrainControl? _terrain;

        public SourceTerrainLights(WorldObject owner, int slots)
        {
            _owner = owner;
            _lights = new DynamicLight?[slots];
            _registered = new bool[slots];
        }

        /// <summary>AddTerrainLight for this tick; <paramref name="range"/> is in tiles.</summary>
        public void Set(int slot, TerrainControl? terrain, Vector3 position, Vector3 light, int range)
        {
            if (terrain == null || (uint)slot >= (uint)_lights.Length)
                return;

            DynamicLight dynamicLight = _lights[slot] ??= new DynamicLight { Owner = _owner };
            dynamicLight.Position = position;
            dynamicLight.Color = light;
            dynamicLight.Radius = range * Constants.TERRAIN_SCALE;
            dynamicLight.Intensity = 1f;

            if (!_registered[slot] || !ReferenceEquals(_terrain, terrain))
            {
                if (_registered[slot])
                    _terrain?.RemoveDynamicLight(dynamicLight);
                _terrain = terrain;
                terrain.AddDynamicLight(dynamicLight);
                _registered[slot] = true;
            }
        }

        /// <summary>The effect stopped calling AddTerrainLight for this slot.</summary>
        public void Clear(int slot)
        {
            if ((uint)slot >= (uint)_lights.Length || !_registered[slot])
                return;

            _terrain?.RemoveDynamicLight(_lights[slot]!);
            _registered[slot] = false;
        }

        public void Dispose()
        {
            for (int i = 0; i < _lights.Length; i++)
                Clear(i);
        }
    }
}
