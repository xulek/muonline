#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    // MoveParticles, ZzzEffectParticle.cpp:3878-3889: one shared wind update per game frame.
    internal static class SourceParticleWind
    {
        private static Vector2 _velocity;
        public static Vector2 Current { get; private set; }

        public static void Update(float factor)
        {
            _velocity.X = MathHelper.Clamp(_velocity.X + (MuGame.Random.Next(2001) - 1000) * 0.0006f, -0.6f, 0.6f) * factor;
            _velocity.Y = MathHelper.Clamp(_velocity.Y + (MuGame.Random.Next(2001) - 1000) * 0.0006f, -0.6f, 0.6f) * factor;
            Vector2 wind = Current;
            wind.X = MathHelper.Clamp(wind.X + _velocity.X, -1.7f, 1.7f) * factor;
            wind.Y = MathHelper.Clamp(wind.Y + _velocity.Y, -1.7f, 1.7f) * factor;
            Current = wind;
        }
    }
}
