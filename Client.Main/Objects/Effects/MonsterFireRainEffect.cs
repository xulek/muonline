#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>Ground fire burst with one batch of animated flames and a fading light.</summary>
    public sealed class MonsterFireRainEffect : EffectObject
    {
        internal const string FireTexturePath = "Effect/Fire01.jpg";
        private readonly record struct Fire(Vector3 Position, float Lifetime, int Phase);

        private readonly Fire[] _fires;
        private readonly SourceQuadBatch _batch;
        private readonly EffectLights _lights;
        private Texture2D? _texture;
        private float _age;
        private float _lifetime;
        private float _centerGround;
        private bool _disposed;
        private static readonly Vector3 FireTint = new(1f, 0.55f, 0.32f);

        public MonsterFireRainEffect(Vector3 center, int fireCount = 8)
        {
            Position = center;
            _fires = new Fire[fireCount];
            _batch = new SourceQuadBatch(fireCount);
            _lights = new EffectLights(this, 1);
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-700f, -700f, -100f), new Vector3(700f, 700f, 300f));
        }

        internal static void SpawnForAttack(MonsterObject caster, Vector3? center = null, int fireCount = 8)
        {
            if (caster.World is not WalkableWorldControl world)
                return;

            Vector3 origin = center ?? caster.Position;
            var rain = new MonsterFireRainEffect(origin, fireCount);
            world.Objects.Add(rain);
            _ = rain.Load();
            caster.PlayAttackSound("Sound/eMeteorite.wav", origin);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _batch.Initialize(GraphicsDevice);
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(FireTexturePath);
            if (_texture == null)
                return;

            var terrain = World?.Terrain;
            _centerGround = terrain?.RequestTerrainHeight(Position.X, Position.Y) ?? Position.Z;
            for (int i = 0; i < _fires.Length; i++)
            {
                float x = Position.X + (i == 0 ? 0f : MuGame.Random.Next(1024) - 512f);
                float y = Position.Y + (i == 0 ? 0f : MuGame.Random.Next(1024) - 512f);
                float z = terrain?.RequestTerrainHeight(x, y) ?? Position.Z;
                float lifetime = 1.2f + (float)MuGame.Random.NextDouble() * 0.6f;
                _fires[i] = new Fire(new Vector3(x, y, z + 75f), lifetime, MuGame.Random.Next(4));
                _lifetime = MathF.Max(_lifetime, lifetime);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_texture == null || _age >= _lifetime)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            float glow = EffectLights.Envelope(_age, _lifetime, 0.15f, _lifetime - 0.5f);
            glow *= EffectLights.Flicker(_age, 0.9f, 0.15f);
            _lights.Set(0, new Vector3(Position.X, Position.Y, _centerGround + 70f), FireTint, 520f, glow);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || _disposed || !_batch.Begin(_texture, Blendings.OneOneAdditive,
                    DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            int frame = (int)(_age * 12f);
            foreach (ref readonly Fire fire in _fires.AsSpan())
            {
                float fade = MathHelper.Clamp((fire.Lifetime - _age) / 0.5f, 0f, 1f);
                if (fade <= 0f)
                    continue;
                const float frameWidth = 0.25f;
                float u = ((frame + fire.Phase) & 3) * frameWidth;
                _batch.AddSpriteRegion(fire.Position, 115f, 200f, Vector3.One * fade, 0f, u, 0f, frameWidth, 1f);
            }
            _batch.End();
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _lights.Dispose();
            _batch.Dispose();
            base.Dispose();
        }
    }
}
