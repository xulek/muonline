#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Kanturu
{
    /// <summary>
    /// Implements GM_Kanturu_1st/2nd map-object animations: gear wheels (46),
    /// furnace lights (70), waterfall scrolls (77/10), bi-directional ducts (42),
    /// stream meshes (96) and the reactor-core glow sprite on Type 4 bone 1.
    /// </summary>
    public class KanturuObject : MapTileObject
    {
        private const float LegacyFramesPerSecond = 50f;

        private readonly Controls.DynamicLight? _furnaceLight;
        private KanturuCoreGlowEmitter? _coreGlow;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        public KanturuObject()
        {
            BlendMeshState = BlendState.Additive;

            if (Type == 70)
            {
                _furnaceLight = new Controls.DynamicLight
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

            if (_furnaceLight != null && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_furnaceLight);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (Type == 4 && _coreGlow == null)
            {
                _coreGlow = new KanturuCoreGlowEmitter(this);
                Children.Add(_coreGlow);
            }
        }

        public override void Update(GameTime gameTime)
        {
            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;

            switch (Type)
            {
                case 46:
                    // Velocity=0.01f; BlendMeshLight=sinf(WT*0.0015)*0.8+1
                    AnimationSpeed = 0.25f;
                    BlendMeshLight = MathF.Sin(totalMs * 0.0015f) * 0.8f + 1f;
                    break;

                case 70:
                    {
                        // Furnace pulse + range-4 warm terrain light
                        float luminosity = MathF.Sin(totalMs * 0.002f) * 0.45f + 0.55f;
                        if (_furnaceLight != null)
                        {
                            _furnaceLight.Position = WorldPosition.Translation;
                            _furnaceLight.Color = new Vector3(
                                MathF.Min(1f, luminosity * 1.4f),
                                luminosity * 0.7f,
                                luminosity * 0.4f);
                        }

                        AnimationSpeed = 1f;
                        Light = new Vector3(luminosity * 1.4f, luminosity * 0.7f, luminosity * 0.4f);
                    }
                    break;

                case 77:
                case 72:
                    // Waterfall scroll: V = -(int)WT % 10000 * 0.0002
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 10:
                    {
                        // Map38: BlendMeshLight=sinf(WT*0.0015)+1 clamped [0.1,0.9]
                        float pulse = MathF.Sin(totalMs * 0.0015f) + 1f;
                        BlendMeshLight = MathHelper.Clamp(pulse, 0.1f, 0.9f);
                    }
                    break;

                case 42:
                    // Bi-directional scroll: U = V = -(int)WT % 10000 * 0.0002
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(
                        -((int)totalMs % 10000) * 0.0002f,
                        -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 96:
                    // Stream mesh: -(int)WT % 20000 * 0.00005
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(-((int)totalMs % 20000) * 0.00005f, 0f);
                    break;
            }

            base.Update(gameTime);

            if (_furnaceLight != null && World?.Terrain != null)
                _furnaceLight.Position = WorldPosition.Translation;
        }

        public override void Dispose()
        {
            if (_furnaceLight != null && World?.Terrain != null)
                World.Terrain.RemoveDynamicLight(_furnaceLight);

            base.Dispose();
        }
    }

    /// <summary>
    /// GM_Kanturu_2nd Type 4 reactor core: pulsing LIGHT sprite on bone 1
    /// (fLumi=(sinf(WT*0.002)+2)*0.5).
    /// </summary>
    internal sealed class KanturuCoreGlowEmitter : Objects.Effects.Particles.SourceParticleSystem
    {
        private const float LegacyFramesPerSecond = 50f;
        private const string GlowTexturePath = "Effect/light.jpg";
        private readonly KanturuObject _owner;
        private Texture2D? _texture;
        private float _emitAccumulator;

        public KanturuCoreGlowEmitter(KanturuObject owner)
            : base(capacity: 12)
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
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(GlowTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_texture == null || Status != GameControlStatus.Ready || !Visible)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _emitAccumulator += dt * LegacyFramesPerSecond;

            while (_emitAccumulator >= 1f)
            {
                _emitAccumulator -= 1f;

                float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
                float luminosity = (MathF.Sin(totalMs * 0.002f) + 2f) * 0.5f;

                Vector3 position = _owner.WorldPosition.Translation;
                var bones = _owner.GetBoneTransforms();
                if (bones != null && bones.Length > 1)
                    position = Vector3.Transform(bones[1].Translation, _owner.WorldPosition);

                CreateParticle(
                    0,
                    position,
                    Vector3.Zero,
                    new Vector3(luminosity * 0.3f, luminosity * 0.5f, luminosity),
                    subType: 0,
                    scale: luminosity / 3.2f);
            }
        }

        protected override void OnParticleCreated(ref Objects.Effects.Particles.SourceParticle particle)
        {
            particle.LifeTime = 0.35f;
            particle.MaxLifeTime = 0.35f;
            particle.Gravity = 0f;
            particle.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref Objects.Effects.Particles.SourceParticle particle, float dt)
        {
            particle.Scale += 0.4f * dt;
        }
    }
}
