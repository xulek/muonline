using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Effects.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    public class WaterSplashObject : ModelObject
    {
        private LegacyWaterfallParticleSystem _particleSystem;

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(alwaysUpdate: true);
        protected override bool RequiresPerFrameAnimation => true;

        private WorldObject[] _sceneSplash;

        public override async Task Load()
        {
            // Types 54-56 are invisible effect markers in Object94.
            HiddenMesh = -2;

            if (Constants.ENABLE_MENU_SCENE_EFFECTS)
            {
                _sceneSplash = CreateSceneSplash();
                foreach (var effect in _sceneSplash)
                {
                    effect.World = World;
                    World.Objects.Add(effect);
                    await effect.Load();
                }
                await base.Load();
                return;
            }

            _particleSystem = new LegacyWaterfallParticleSystem(Type)
            {
                Position = Position,
                Angle = Angle,
                SourceScale = Scale,
                World = World
            };

            World.Objects.Add(_particleSystem);
            await _particleSystem.Load();
            await base.Load();
        }

        /// <summary>
        /// Splash in place of the original sprite columns: drops on ballistic arcs and a soft spray cloud.
        /// 55 marks where falling water lands, 54 the falling curtain, 56 small trickles.
        /// </summary>
        private WorldObject[] CreateSceneSplash()
        {
            float s = Math.Clamp(Scale, 0.5f, 2f);
            Vector3 at = Position;
            return Type switch
            {
                55 => new WorldObject[]
                {
                    new WaterDroplets(at, at, new DropletSpec(
                        Rate: 55f * s, Jitter: 18f * s, OutSpeed: new Vector2(30f, 120f) * s, UpSpeed: new Vector2(110f, 260f) * s,
                        Gravity: 700f, Life: new Vector2(0.5f, 1.0f), Size: new Vector2(0.35f, 0.7f), FallDepth: 40f)),
                    new SplashMist(at, rate: 7f, radius: 30f * s, alpha: 0.38f, size: 1.3f * s, rise: 25f),
                },
                54 => new WorldObject[]
                {
                    new WaterDroplets(at, at, new DropletSpec(
                        Rate: 20f * s, Jitter: 25f * s, OutSpeed: new Vector2(20f, 70f), UpSpeed: new Vector2(-40f, 50f),
                        Gravity: 600f, Life: new Vector2(0.5f, 0.9f), Size: new Vector2(0.3f, 0.55f), FallDepth: 250f)),
                    new SplashMist(at, rate: 5f, radius: 40f * s, alpha: 0.3f, size: 1.5f * s, rise: -20f),
                },
                _ => new WorldObject[]
                {
                    new WaterDroplets(at, at, new DropletSpec(
                        Rate: 14f * s, Jitter: 10f, OutSpeed: new Vector2(15f, 50f), UpSpeed: new Vector2(40f, 120f),
                        Gravity: 650f, Life: new Vector2(0.4f, 0.8f), Size: new Vector2(0.28f, 0.5f), FallDepth: 30f)),
                    new SplashMist(at, rate: 3f, radius: 15f, alpha: 0.25f, size: 0.9f, rise: 15f),
                },
            };
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_particleSystem == null)
                return;

            _particleSystem.Position = Position;
            _particleSystem.Angle = Angle;
            _particleSystem.SourceScale = Scale;
        }

        public override void Dispose()
        {
            if (_sceneSplash != null)
            {
                foreach (var effect in _sceneSplash)
                {
                    World?.Objects.Remove(effect);
                    effect.Dispose();
                }
                _sceneSplash = null;
            }
            base.Dispose();
        }

        private sealed class LegacyWaterfallParticleSystem : SourceParticleSystem
        {
            private const short Waterfall5Marker = 54;
            private const short Waterfall3Marker = 55;
            private const short Waterfall2Marker = 56;
            private const int MaxParticles = 256;
            private const float FramesPerSecond = 60f;

            private readonly short _markerType;
            private Texture2D _texture;
            private Vector2 _textureCenter;

            public float SourceScale { get; set; }

            protected override Texture2D ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public LegacyWaterfallParticleSystem(short markerType)
                : base(MaxParticles)
            {
                _markerType = markerType;
                MaxDistance = 5500f;
                ReferenceDistance = 800f;
                MinDistanceScale = 1f;
                ScaleGrowth = 0f;
            }

            public override async Task LoadContent()
            {
                string texturePath = _markerType switch
                {
                    Waterfall5Marker => "Effect/waterFall5.OZJ",
                    Waterfall3Marker => "Effect/waterFall3.OZJ",
                    _ => "Effect/waterFall2.OZJ"
                };

                _texture = await TextureLoader.Instance.PrepareAndGetTexture(texturePath);
                if (_texture != null)
                    _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            protected override void OnBeforeParticlesUpdated(float dt)
            {
                if (_markerType == Waterfall2Marker && !FPSCounter.Instance.RandFPSCheck(8))
                    return;

                if (!FPSCounter.Instance.RandFPSCheck(1))
                    return;

                int subType = _markerType switch
                {
                    Waterfall5Marker => 9,
                    Waterfall3Marker => 14,
                    _ => 4
                };

                CreateParticle(
                    type: _markerType,
                    position: Position,
                    angle: Angle,
                    light: Vector3.One,
                    subType: subType,
                    scale: SourceScale);
            }

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                float animationFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                switch (_markerType)
                {
                    case Waterfall5Marker:
                        particle.LifeTime = 30f / FramesPerSecond;
                        particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                        particle.Scale = 0.6f + SourceScale;
                        particle.Velocity.Z = -(MuGame.Random.Next(5) + 7);
                        particle.Light = new Vector3(0.2f);
                        break;

                    case Waterfall3Marker:
                        particle.LifeTime = 30f / FramesPerSecond;
                        particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                        particle.Velocity.Z = MuGame.Random.Next(5) + 5;
                        particle.Scale = (MuGame.Random.Next(10) + 10) * 0.05f * SourceScale;
                        break;

                    default:
                        particle.LifeTime = 30f / FramesPerSecond;
                        particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                        particle.Scale = (MuGame.Random.Next(6) + 6) * 0.1f;
                        particle.Velocity.Z = -(MuGame.Random.Next(3) + 3);
                        particle.Light = new Vector3(0.25f);
                        particle.Position.X += (MuGame.Random.Next(20) - 10) * animationFactor;
                        particle.Position.Y += (MuGame.Random.Next(20) - 10) * animationFactor;
                        particle.Position.Z += (MuGame.Random.Next(40) - 20) * animationFactor;
                        break;
                }

                particle.MaxLifeTime = particle.LifeTime;
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                float animationFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                if (particle.EnableMove)
                {
                    Matrix rotation = Matrix.CreateFromYawPitchRoll(
                        particle.Angle.Y,
                        particle.Angle.X,
                        particle.Angle.Z);
                    particle.Position += Vector3.TransformNormal(particle.Velocity, rotation) * animationFactor;
                }

                switch (_markerType)
                {
                    case Waterfall5Marker:
                        particle.Scale -= 0.005f * animationFactor;
                        particle.Velocity.Z += 0.1f * animationFactor;

                        float lifeFrames = particle.LifeTime * FramesPerSecond;
                        if (lifeFrames < 8f)
                            particle.Light *= MathF.Pow(1f / 1.2f, animationFactor);
                        else if (lifeFrames > 20f)
                            particle.Light *= MathF.Pow(1.1f, animationFactor);
                        break;

                    case Waterfall3Marker:
                        particle.Scale += 0.05f * animationFactor;
                        particle.Velocity.Z -= 0.6f * animationFactor;
                        particle.Light *= MathF.Pow(1f / 1.1f, animationFactor);
                        break;

                    default:
                        particle.Scale += 0.03f * animationFactor;
                        particle.Velocity.X = (MuGame.Random.Next(20) - 10) * 0.1f;
                        particle.Velocity.Y = (MuGame.Random.Next(20) - 10) * 0.1f;
                        particle.Velocity.Z += 0.1f * animationFactor;

                        if (particle.LifeTime * FramesPerSecond < 10f)
                            particle.Light *= MathF.Pow(1f / 1.1f, animationFactor);

                        particle.Rotation -= MathHelper.ToRadians(1.1f * animationFactor);
                        break;
                }
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
                new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha);

            protected override float GetParticleScale(
                in SourceParticle particle,
                float lifeRatio,
                float distanceScale,
                float perspectiveScale) => particle.Scale * perspectiveScale;
        }
    }
}
