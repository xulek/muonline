using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Top-level container that spawns magic circle + flares
    /// and destroys itself after _lifetime_ seconds.
    /// </summary>
    public class LevelUpEffect : EffectObject
    {
        private const float _normalLifetime = 20f / 25f;
        private const float _masterLifetime = 40f / 25f;

        private readonly bool _masterLevel;
        private readonly Vector3 _angle;
        private LevelUpFlare _masterAnchor;
        private LevelUpModelEffect _masterChangeUpEffect;
        private LevelUpModelEffect _masterChangeUpCylinder;
        private bool _masterFinishSpawned;
        private float _lifetime;

        public LevelUpEffect(
            Vector3 position,
            bool masterLevel = false,
            Vector3 angle = default)
        {
            Position = position;
            _masterLevel = masterLevel;
            _angle = angle;
            _lifetime = masterLevel ? _masterLifetime : _normalLifetime;
        }

        public override async Task Load()
        {
            await base.Load();

            if (!_masterLevel)
            {
                var circle = new LevelUpMagicCircle(Position);
                World.Objects.Add(circle);
                await circle.Load();
            }

            Vector3 flareStartPos = Position;
            int flareCount = _masterLevel ? 20 : 15;
            for (int i = 0; i < flareCount; i++)
            {
                int flareVariant = _masterLevel ? (i == 0 ? 45 : 46) : 0;
                var flare = new LevelUpFlare(flareStartPos, flareVariant, i, _angle);
                World.Objects.Add(flare);
                await flare.Load();
                if (i == 0)
                    _masterAnchor = flare;
            }

            if (_masterLevel)
            {
                _masterChangeUpEffect = await AddModelIfAvailable(
                    new[] { "Effect/Change_Up_Eff.bmd", "Effect/change_up_eff.bmd" },
                    Position + new Vector3(0f, 0f, 22f),
                    0.4f,
                    0.7f,
                    new Vector3(0.1f, 0.4f, 0.6f),
                    grows: false);
                _masterChangeUpCylinder = await AddModelIfAvailable(
                    new[] { "Effect/clinderlight.bmd", "Effect/cylinderlight.bmd" },
                    Position,
                    0.1f,
                    0f,
                    new Vector3(0.4f, 0.5f, 1f),
                    grows: true);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_masterLevel && !_masterFinishSpawned && _masterAnchor?.IsAtFinalFrame == true)
            {
                _masterFinishSpawned = true;
                _masterChangeUpEffect?.Activate();
                _masterChangeUpCylinder?.Activate();
            }

            _lifetime -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_lifetime <= 0f)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private async Task<LevelUpModelEffect> AddModelIfAvailable(
            string[] candidates,
            Vector3 position,
            float scale,
            float blendMeshLight,
            Vector3 light,
            bool grows)
        {
            string modelPath = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(candidates[i]))
                {
                    modelPath = candidates[i];
                    break;
                }
            }

            if (modelPath == null)
                return null;

            var model = new LevelUpModelEffect(
                modelPath,
                position,
                scale,
                blendMeshLight,
                light,
                grows);
            World.Objects.Add(model);
            await model.Load();
            return model;
        }

        private sealed class LevelUpModelEffect : ModelObject
        {
            private const float SourceFrameRate = 25f;

            private readonly string _modelPath;
            private readonly bool _grows;
            private float _lifeFrames = 10f;
            private bool _active;

            public LevelUpModelEffect(
                string modelPath,
                Vector3 position,
                float scale,
                float blendMeshLight,
                Vector3 light,
                bool grows)
            {
                _modelPath = modelPath;
                _grows = grows;
                Position = position;
                Scale = scale;
                Light = light;
                BlendMesh = -2;
                BlendMeshLight = blendMeshLight;
                BlendState = BlendState.NonPremultiplied;
                BlendMeshState = BlendState.NonPremultiplied;
                DepthState = DepthStencilState.DepthRead;
                IsTransparent = true;
                ContinuousAnimation = true;
                AnimationSpeed = 4f;
                Hidden = true;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }

            public void Activate()
            {
                _active = true;
                Hidden = false;
                _lifeFrames = 10f;
            }

            public override void Update(GameTime gameTime)
            {
                if (!_active)
                    return;

                base.Update(gameTime);
                float frameDelta = MathHelper.Clamp(
                    (float)gameTime.ElapsedGameTime.TotalSeconds * SourceFrameRate,
                    0f,
                    5f);
                if (_grows)
                {
                    Scale += 0.08f * frameDelta;
                    BlendMeshLight = _lifeFrames * 0.015f;
                }

                _lifeFrames -= frameDelta;
                Alpha = MathHelper.Clamp(_lifeFrames / 10f, 0f, 1f); // fade out instead of vanishing
                if (_lifeFrames <= 0f)
                {
                    World?.RemoveObject(this);
                    Dispose();
                }
            }
        }
    }
}
