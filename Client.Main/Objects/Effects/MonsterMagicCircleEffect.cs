#nullable enable
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of SourceMain5.2 MODEL_CIRCLE + MODEL_CIRCLE_LIGHT (AttackEffect
    /// MONSTER_BALROG boss branch): the magic circle and its light layer spawn
    /// flat on the ground under the caster and fade out (~45/40 frames).
    /// </summary>
    public sealed class MonsterMagicCircleEffect : EffectObject
    {
        private const float CircleLifetimeSeconds = 1.8f;
        private const float CircleLightLifetimeSeconds = 1.6f;

        private CirclePiece? _circle;
        private CirclePiece? _circleLight;
        private float _life = CircleLifetimeSeconds;
        private float _groundZ;
        private readonly EffectLights _lights;

        public MonsterMagicCircleEffect(Vector3 center)
        {
            Position = center;
            _groundZ = center.Z;
            _lights = new EffectLights(this, 1);
            IsTransparent = true;
            AffectedByTransparency = true;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-350f, -350f, -80f),
                new Vector3(350f, 350f, 200f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var circleModel = await BMDLoader.Instance.Prepare("Skill/Circle.bmd");
            if (circleModel == null)
                return;

            float groundZ = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(Position.X, Position.Y)
                : Position.Z;
            _groundZ = groundZ;

            _circle = new CirclePiece(circleModel, CircleLifetimeSeconds)
            {
                Position = new Vector3(Position.X, Position.Y, groundZ)
            };
            _circleLight = new CirclePiece(circleModel, CircleLightLifetimeSeconds)
            {
                Position = new Vector3(Position.X, Position.Y, groundZ)
            };
            Children.Add(_circle);
            Children.Add(_circleLight);
            await _circle.Load();
            await _circleLight.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _life -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_life <= 0f)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            // Warm glow under the circle, faded with the circle's own alpha curve.
            float glow = 0.7f * EffectLights.Envelope(CircleLifetimeSeconds - _life, CircleLifetimeSeconds, 0.2f, CircleLifetimeSeconds - 0.2f);
            _lights.Set(0, new Vector3(Position.X, Position.Y, _groundZ + 40f), new Vector3(1f, 0.55f, 0.3f), 300f, glow);
        }

        public override void Dispose()
        {
            _lights.Dispose();
            base.Dispose();
        }

        private sealed class CirclePiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;
            private readonly float _totalLife;
            private float _life;

            public CirclePiece(Client.Data.BMD.BMD model, float lifetimeSeconds)
            {
                _model = model;
                _totalLife = lifetimeSeconds;
                _life = lifetimeSeconds;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 4f;
                IsTransparent = true;
                BlendState = Microsoft.Xna.Framework.Graphics.BlendState.Additive;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                _life -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                Alpha = MathHelper.Clamp(_life / _totalLife, 0f, 1f);
            }
        }
    }
}
