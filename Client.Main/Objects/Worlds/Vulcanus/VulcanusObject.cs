#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Vulcanus
{
    /// <summary>
    /// Implements GM_PK_Field.cpp map-object visuals for Vulcanus:
    /// Type 15 lava stream UV scroll, Type 16 molten pulse, Types 0-6 hidden
    /// geyser/vent shells with smoke emission, and Types 67/68 furnace bodies
    /// rendered twice with red chrome (RENDER_BRIGHT|CHROME).
    /// </summary>
    public class VulcanusObject : MapTileObject
    {
        private const string ChromeTexturePath = "Effect/Chrome01.jpg";

        private Texture2D? _chromeTexture;
        private VulcanusVentSmokeEmitter? _ventEmitter;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        protected override bool ForceTwoSidedMeshes => Type is >= 0 and <= 6 or 15 or 16;

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (Type == 67 || Type == 68)
                _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture(ChromeTexturePath);

            if (Type is >= 0 and <= 6 && _ventEmitter == null)
            {
                _ventEmitter = new VulcanusVentSmokeEmitter(this);
                Children.Add(_ventEmitter);
            }
        }

        public override void Update(GameTime gameTime)
        {
            ApplySourceAnimation(Type, gameTime);
            base.Update(gameTime);
        }

        private void ApplySourceAnimation(int sourceType, GameTime gameTime)
        {
            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;

            switch (sourceType)
            {
                case 15:
                    // RenderBody stream: U = -(int)WorldTime % 10000 * 0.0001f
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(-((int)totalMs % 10000) * 0.0001f, 0f);
                    break;

                case 16:
                    {
                        // fLumi = (sinf(WorldTime * 0.002f) + 1.f) * 0.5f bright body pulse
                        float luminosity = (MathF.Sin(totalMs * 0.002f) + 1f) * 0.5f;
                        Light = new Vector3(luminosity, luminosity * 0.55f, luminosity * 0.3f);
                    }
                    break;

                case >= 0 and <= 6:
                    HiddenMesh = -2;
                    break;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
                return;

            base.Draw(gameTime);

            if ((Type == 67 || Type == 68) && _chromeTexture != null)
                DrawRedChromePass();
        }

        private void DrawRedChromePass()
        {
            if (Model?.Meshes == null || _chromeTexture == null)
                return;

            var prevBlend = GraphicsDevice.BlendState;
            try
            {
                // RENDER_CHROME|RENDER_BRIGHT with red tint -> additive chrome pass
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
                {
                    Texture2D originalTexture = GetMeshTexture(meshIndex);
                    try
                    {
                        SetMeshTextureOverride(meshIndex, _chromeTexture);
                        Light = new Vector3(1f, 0.2f, 0.1f);
                        DrawMesh(meshIndex);
                    }
                    finally
                    {
                        if (originalTexture != null)
                            SetMeshTextureOverride(meshIndex, originalTexture);
                        else
                            ClearMeshTextureOverride(meshIndex);
                    }
                }
            }
            finally
            {
                GraphicsDevice.BlendState = prevBlend;
            }
        }
    }

    /// <summary>
    /// Timed vent smoke/geyser puffs for the hidden Types 0-6 emitter set.
    /// </summary>
    internal sealed class VulcanusVentSmokeEmitter : Objects.Effects.Particles.SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private readonly VulcanusObject _owner;
        private Texture2D? _texture;
        private float _burstCooldown;

        public VulcanusVentSmokeEmitter(VulcanusObject owner)
            : base(capacity: 24)
        {
            _owner = owner;
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
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_texture == null || Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _burstCooldown -= dt;
            if (_burstCooldown > 0f || ActiveCount >= Particles.Length - 4)
                return;

            // nRanDelay = (int)Position[0] % 3 + 1 seconds between bursts
            _burstCooldown = MathF.Max(1f, MathF.Abs(_owner.Position.X) % 3f) + 1f;

            Vector3 position = _owner.WorldPosition.Translation;
            for (int i = 0; i < 6; i++)
                CreateParticle(6, position, Vector3.Zero, new Vector3(1f, 0.4f, 0.15f), 6, 0.8f);
        }

        protected override void OnParticleCreated(ref Objects.Effects.Particles.SourceParticle particle)
        {
            particle.LifeTime = 1.8f;
            particle.MaxLifeTime = 1.8f;
            particle.Velocity = new Vector3(
                MuGame.Random.Next(41) - 20f,
                MuGame.Random.Next(41) - 20f,
                90f + MuGame.Random.Next(60));
            particle.Gravity = 0f;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
        }

        protected override void UpdateLiveParticle(ref Objects.Effects.Particles.SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Scale += 0.7f * dt;
        }
    }
}

