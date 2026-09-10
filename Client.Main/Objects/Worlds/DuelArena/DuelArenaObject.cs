#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.DuelArena
{
    /// <summary>
    /// GMDuelArena.cpp scenery: Type 34 hidden brazier with a warm flickering terrain
    /// light (L=(rand()%3+5)*0.1 -> L*0.9/L*0.2/L*0.1), Type 35 smoke vents and
    /// Type 36 fire bowls with red LIGHT glow.
    /// </summary>
    public class DuelArenaObject : MapTileObject
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private const string FireTexturePath = "Effect/Fire01.jpg";

        private readonly Controls.DynamicLight? _brazierLight;
        private DuelArenaPuffEmitter? _puffEmitter;
        private Texture2D? _fireTexture;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        public DuelArenaObject()
        {
            if (Type == 34)
            {
                _brazierLight = new Controls.DynamicLight
                {
                    Owner = this,
                    Radius = Constants.TERRAIN_SCALE * 4f,
                    Intensity = 1f
                };
            }
        }

        public override async Task Load()
        {
            await base.Load();

            if (_brazierLight != null && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_brazierLight);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (Type == 35 || Type == 36)
            {
                _puffEmitter = new DuelArenaPuffEmitter(this, Type == 36);
                Children.Add(_puffEmitter);

                if (Type == 36)
                    _fireTexture = await TextureLoader.Instance.PrepareAndGetTexture(FireTexturePath);
                else
                    await TextureLoader.Instance.Prepare(SmokeTexturePath);
            }
        }

        public override void Update(GameTime gameTime)
        {
            switch (Type)
            {
                case 34:
                    {
                        float luminosity = (MuGame.Random.Next(3) + 5) * 0.1f;
                        Light = new Vector3(luminosity * 0.9f, luminosity * 0.2f, luminosity * 0.1f);
                        if (_brazierLight != null)
                        {
                            _brazierLight.Position = WorldPosition.Translation;
                            _brazierLight.Color = Light;
                        }

                        HiddenMesh = -2;
                    }
                    break;
            }

            base.Update(gameTime);

            if (_brazierLight != null && World?.Terrain != null)
                _brazierLight.Position = WorldPosition.Translation;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (Type != 36 || _fireTexture == null || !Visible ||
                Status != GameControlStatus.Ready || Camera.Instance == null)
            {
                return;
            }

            // LIGHT sprite scale 2.0*Scale with red tint (1.0, 0.2, 0)
            var spriteBatch = Controllers.GraphicsManager.Instance.Sprite;
            var device = Controllers.GraphicsManager.Instance.GraphicsDevice;
            if (spriteBatch == null || device == null)
                return;

            Vector3 projected = device.Viewport.Project(
                WorldPosition.Translation,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            float flicker = 0.85f + 0.15f * MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * 0.02f);
            float sizePx = 2f * Scale * 40f;

            void drawFire()
            {
                spriteBatch.Draw(
                    _fireTexture,
                    new Vector2(projected.X, projected.Y),
                    null,
                    new Color(1f, 0.2f, 0f, flicker),
                    0f,
                    new Vector2(_fireTexture.Width * 0.5f, _fireTexture.Height * 0.5f),
                    sizePx / _fireTexture.Width,
                    SpriteEffects.None,
                    projected.Z);
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    drawFire();
                }
            }
            else
            {
                drawFire();
            }
        }

        public override void Dispose()
        {
            if (_brazierLight != null && World?.Terrain != null)
                World.Terrain.RemoveDynamicLight(_brazierLight);

            base.Dispose();
        }
    }

    /// <summary>
    /// Type 35 smoke puffs (SMOKE type 14, rand_fps_check(3)) or Type 36 fire flakes.
    /// </summary>
    internal sealed class DuelArenaPuffEmitter : Objects.Effects.Particles.SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private readonly DuelArenaObject _owner;
        private readonly bool _fire;
        private Texture2D? _texture;
        private float _emitAccumulator;

        public DuelArenaPuffEmitter(DuelArenaObject owner, bool fire)
            : base(capacity: 24)
        {
            _owner = owner;
            _fire = fire;
            MaxDistance = 1800f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter =>
            _texture == null ? Vector2.Zero : new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(
                _fire ? "Effect/Fire01.jpg" : SmokeTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_texture == null || Status != GameControlStatus.Ready || !_owner.Visible)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // rand_fps_check(3)
            _emitAccumulator += dt * LegacyFramesPerSecond / 3f;
            while (_emitAccumulator >= 1f)
            {
                _emitAccumulator -= 1f;
                CreateParticle(
                    0,
                    _owner.WorldPosition.Translation,
                    Vector3.Zero,
                    _fire ? new Vector3(1f, 0.3f, 0.1f) : new Vector3(0.6f, 0.6f, 0.6f),
                    subType: 0,
                    scale: _fire ? 0.9f : 0.7f);
            }
        }

        protected override void OnParticleCreated(ref Objects.Effects.Particles.SourceParticle particle)
        {
            particle.LifeTime = _fire ? 0.7f : 1.6f;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Velocity = new Vector3(
                MuGame.Random.Next(21) - 10f,
                MuGame.Random.Next(21) - 10f,
                _fire ? 60f : 30f);
            particle.Gravity = 0f;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
        }

        protected override void UpdateLiveParticle(ref Objects.Effects.Particles.SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Scale += (_fire ? -0.25f : 0.5f) * dt;
        }

        private const float LegacyFramesPerSecond = 50f;
    }
}



