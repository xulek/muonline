using Client.Main.Controls;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds
{
    /// <summary>
    /// One terrain <see cref="DynamicLight"/> for a map object's fire/glow. It is registered once,
    /// keeps a static position (only re-assigned when it actually changes) and throttles colour
    /// updates to the legacy 25 Hz step. The light is removed together with its owner
    /// (WorldObject.Dispose calls RemoveDynamicLightsByOwner).
    /// </summary>
    internal sealed class MapObjectGlowLight
    {
        private const double LegacyStepSeconds = 1.0 / 25.0;

        private readonly DynamicLight _light;
        private double _accumulator = LegacyStepSeconds;
        private bool _registered;

        public MapObjectGlowLight(WorldObject owner, float radius, float intensity = 1f)
        {
            _light = new DynamicLight
            {
                Owner = owner,
                Radius = radius,
                Intensity = intensity,
                Color = Vector3.Zero,
            };
        }

        public bool IsPositioned { get; private set; }

        public void Register()
        {
            if (_registered)
                return;

            var terrain = _light.Owner?.World?.Terrain;
            if (terrain == null)
                return;

            terrain.AddDynamicLight(_light);
            _registered = true;
        }

        public void SetPosition(Vector3 position)
        {
            if (!IsPositioned || _light.Position != position)
                _light.Position = position;
            IsPositioned = true;
        }

        /// <summary>Returns true once per elapsed legacy 25 Hz step.</summary>
        public bool Step(GameTime gameTime)
        {
            if (!_registered)
                Register();

            _accumulator += gameTime.ElapsedGameTime.TotalSeconds;
            if (_accumulator < LegacyStepSeconds)
                return false;

            _accumulator = 0d;
            return true;
        }

        public void SetColor(Vector3 color) => _light.Color = color;
    }
}
