#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>A short-lived attack model, optionally drawn as a fan from one shared pose.</summary>
    internal sealed class MonsterAttackModelEffect : EffectObject
    {
        private readonly SourceModelRenderer _renderer;
        private readonly Vector3 _tint;
        private readonly float _lifetime;
        private readonly float _startScale;
        private readonly float _endScale;
        private readonly float _speed;
        private readonly int _rays;
        private readonly float _yawStep;
        private float _age;
        private bool _loaded;
        private bool _disposed;

        private MonsterAttackModelEffect(string model, Vector3 position, Vector3 angle, Vector3 tint,
            float lifetime, float scale, float? endScale, float speed, int rays, float yawStep, bool animated)
        {
            _renderer = new SourceModelRenderer(model, continuousAnimation: animated)
            {
                HoldLastFrame = animated,
                UseSceneLighting = false,
                Light = tint,
            };
            Position = position;
            Angle = angle;
            _tint = tint;
            _lifetime = lifetime;
            _startScale = scale;
            _endScale = endScale ?? scale;
            _speed = speed;
            _rays = rays;
            _yawStep = yawStep;
            IsTransparent = true;
            AffectedByTransparency = true;
            float reach = 450f + speed * lifetime;
            BoundingBoxLocal = new BoundingBox(new Vector3(-reach, -reach, -150f), new Vector3(reach, reach, 600f));
        }

        internal static void Spawn(WorldControl world, string model, Vector3 position, Vector3 angle, Vector3 tint,
            float lifetime, float scale, float? endScale = null, float speed = 0f,
            int rays = 1, float yawStep = 0f, bool animated = false)
        {
            var effect = new MonsterAttackModelEffect(
                model, position, angle, tint, lifetime, scale, endScale, speed, rays, yawStep, animated);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _loaded = await _renderer.LoadInto(World);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (!_loaded || _age >= _lifetime)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            if (_renderer.ContinuousAnimation)
                _renderer.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || !_loaded || _disposed || Status != GameControlStatus.Ready)
                return;

            float progress = _age / _lifetime;
            float scale = MathHelper.Lerp(_startScale, _endScale, progress);
            float fade = MathF.Min(1f, (1f - progress) * 2f);
            for (int i = 0; i < _rays; i++)
            {
                Vector3 angle = Angle;
                angle.Z += (i - (_rays - 1) * 0.5f) * _yawStep;
                float distance = _speed * _age;
                var offset = new Vector3(MathF.Sin(angle.Z) * distance, -MathF.Cos(angle.Z) * distance, 0f);
                _renderer.DrawInstance(Position + offset, angle, scale, _tint, fade);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _renderer.Dispose();
            base.Dispose();
        }
    }
}
