#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>One shared BMD renderer and light for the whole group of falling ice.</summary>
    public sealed class MonsterBlizzardStrikeEffect : EffectObject
    {
        private const float ImpactSeconds = 0.36f;
        private const float FallAcceleration = 1250f;
        private static readonly Vector3 IceTint = new(0.48f, 0.72f, 1f);

        private readonly record struct Shard(Vector3 Ground, float ReleaseTime, float FallTime,
            float Height, float Speed, float Scale, float Yaw);

        private readonly Shard[] _shards;
        private readonly SourceModelRenderer _renderer;
        private readonly EffectLights _lights;
        private readonly bool _wide;
        private bool _loaded;
        private float _age;
        private float _endTime;
        private float _centerGround;
        private bool _disposed;

        public MonsterBlizzardStrikeEffect(Vector3 position) : this(position, wide: false) { }

        internal MonsterBlizzardStrikeEffect(Vector3 position, bool wide)
        {
            Position = position;
            _wide = wide;
            _shards = new Shard[wide ? 12 : 20];
            _renderer = new SourceModelRenderer(wide ? "Effect/ice_stone01.bmd" : "Skill/Blizzard.bmd",
                continuousAnimation: false)
            {
                UseSceneLighting = false,
                Light = IceTint,
            };
            _lights = new EffectLights(this, 1);
            IsTransparent = true;
            AffectedByTransparency = true;

            float radius = wide ? 650f : 260f;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-radius, -radius, -120f), new Vector3(radius, radius, 1050f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _loaded = await _renderer.LoadInto(World);
            if (!_loaded)
                return;

            var terrain = World?.Terrain;
            _centerGround = terrain?.RequestTerrainHeight(Position.X, Position.Y) ?? Position.Z;
            float radius = _wide ? 540f : 155f;
            for (int i = 0; i < _shards.Length; i++)
            {
                float yaw = (float)MuGame.Random.NextDouble() * MathHelper.TwoPi;
                float r = radius * MathF.Sqrt((float)MuGame.Random.NextDouble());
                Vector3 ground = Position + new Vector3(MathF.Cos(yaw) * r, MathF.Sin(yaw) * r, 0f);
                ground.Z = (terrain?.RequestTerrainHeight(ground.X, ground.Y) ?? Position.Z) + 8f;
                float height = _wide ? MuGame.Random.Next(360, 850) : 500f;
                float speed = MuGame.Random.Next(490, 840);
                float delay = (float)MuGame.Random.NextDouble() * 0.36f;
                float fallTime = 2f * height / (speed + MathF.Sqrt(speed * speed + 2f * FallAcceleration * height));
                float scale = _wide ? MuGame.Random.Next(4, 8) : 0.5f;
                _shards[i] = new Shard(ground, delay, fallTime, height, speed, scale, yaw);
                _endTime = MathF.Max(_endTime, delay + fallTime + ImpactSeconds);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (!_loaded || _age >= _endTime)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            float intensity = EffectLights.Envelope(_age, _endTime, 0.18f, _endTime - ImpactSeconds);
            _lights.Set(0, new Vector3(Position.X, Position.Y, _centerGround + 60f),
                IceTint, _wide ? 480f : 340f, intensity * 0.85f);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || !_loaded || _disposed || Status != GameControlStatus.Ready)
                return;

            foreach (ref readonly Shard shard in _shards.AsSpan())
            {
                float time = _age - shard.ReleaseTime;
                if (time < 0f || time >= shard.FallTime + ImpactSeconds)
                    continue;

                bool landed = time >= shard.FallTime;
                float height = landed ? 0f : shard.Height - time * (shard.Speed + 0.5f * FallAcceleration * time);
                float opacity = landed ? 1f - (time - shard.FallTime) / ImpactSeconds : MathF.Min(1f, time * 10f);
                var angle = new Vector3(landed ? 0f : time * 3f, 0f, shard.Yaw);
                _renderer.DrawInstance(shard.Ground + Vector3.UnitZ * height, angle, shard.Scale, IceTint, opacity);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _lights.Dispose();
            _renderer.Dispose();
            base.Dispose();
        }
    }
}
