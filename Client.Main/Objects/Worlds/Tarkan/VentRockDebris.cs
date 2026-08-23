#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Flying rock debris from the Tarkan Type-83 vent eruption
    /// (source: CreateEffectFpsChecked(MODEL_STONE1 + rand()%2, ...)).
    /// Tumbles upward with gravity and expires after a short flight.
    /// </summary>
    public sealed class VentRockDebris : ModelObject
    {
        private const float ReferenceFps = 25f;
        private const float LifeSeconds = 0.9f;

        private Vector3 _velocity;
        private float _age;

        public bool IsExpired { get; private set; }

        /// <summary>1 or 2 — appended to the Skill/Stone model base name.</summary>
        public string ModelPath { get; set; } = "1";

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare($"Skill/Stone{int.Parse(ModelPath):00}.bmd");
            await base.Load();

            _velocity = new Vector3(
                MuGame.Random.Next(33) - 16f,
                MuGame.Random.Next(33) - 16f,
                120f + MuGame.Random.Next(60));
            Angle = new Vector3(
                MathHelper.ToRadians(MuGame.Random.Next(360)),
                MathHelper.ToRadians(MuGame.Random.Next(360)),
                MathHelper.ToRadians(MuGame.Random.Next(360)));
            Scale *= 0.6f + (float)MuGame.Random.NextDouble() * 0.4f;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float legacyFrames = dt * ReferenceFps;

            Position += _velocity * legacyFrames * 0.4f;
            _velocity.Z -= 260f * dt;

            Angle = new Vector3(
                Angle.X + MathHelper.ToRadians(9f) * legacyFrames,
                Angle.Y + MathHelper.ToRadians(7f) * legacyFrames,
                Angle.Z);

            _age += dt;
            if (_age >= LifeSeconds)
                IsExpired = true;
        }
    }
}
