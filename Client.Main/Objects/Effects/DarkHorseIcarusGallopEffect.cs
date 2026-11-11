#nullable enable
using System;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Vehicle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Icarus-specific Dark Horse gallop effect.
    /// Mirrors SourceMain5.2 GOBoid.cpp (MODEL_DARK_HORSE, PLAYER_RUN_RIDE_HORSE,
    /// WD_10HEAVEN branch): while galloping on Icarus each hoof strike spawns a ground
    /// shock-wave ring plus 5..9 BITMAP_SMOKE puffs, instead of the normal smoke dust.
    /// The ring uses the original shockwave_ground01.bmd (flat ground ring). Growth and
    /// fade mirror the BITMAP_SHOCK_WAVE SubType 1 effect (ZzzEffect.cpp): life ~20 frames,
    /// scale grows a random 0..0.4 tile/frame, light fades linearly. Size is matched to the
    /// original sprite: the sprite's o->Scale is in tiles and the ring spans ~0.807 of the
    /// texture, so the model scale = tiles * 0.807 * TERRAIN_SCALE / nativeSize (RingScalePerTile).
    /// Note: the runtime DarkHorse.bmd has a different bone layout than SourceMain's
    /// hardcoded indices (19/25/32/38), so the four hooves are resolved by bone NAME
    /// ("Bip01 L/R Foot", "Bip01 L/R Toe0") instead of by index.
    /// </summary>
    public sealed class DarkHorseIcarusGallopEffect : EffectObject
    {
        private const string RingModelPath = "Effect/shockwave_ground01.bmd";
        private const float RingLifeFrames = 20f;
        // shockwave_ground01.bmd renders ~194.8 units wide at scale 1 (probed from runtime data).
        // Original BITMAP_SHOCK_WAVE SubType 1 uses RenderTerrainAlphaBitmap where o->Scale is in
        // TILES (1 tile = TERRAIN_SCALE units), and the ShockWave texture ring spans ~0.807 of the
        // texture width. Model scale per original tile = 0.807 * TERRAIN_SCALE / native ≈ 0.414.
        private const float RingScalePerTile = 0.807f * Constants.TERRAIN_SCALE / 195f;
        // User feedback: ring too large. Keep it modest — ~55% of the tile-mapped size.
        private const float RingSizeFactor = 0.55f;
        // Hard cap so the ring never balloons past ~1.6 scale (≈310 world units).
        private const float RingMaxScale = 1.6f;

        private readonly VehicleObject _vehicle;
        private readonly DarkHorseGallopSmoke _smoke;
        private readonly int[] _footBones = { -1, -1, -1, -1 };
        private bool _bonesResolved;
        private int _lastRunWindow = -1;

        /// <summary>True while the Dark Horse is galloping on Icarus; the vehicle toggles this.</summary>
        public bool Emitting { get; set; }

        public DarkHorseIcarusGallopEffect(VehicleObject vehicle)
        {
            _vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));

            _smoke = new DarkHorseGallopSmoke();
            Children.Add(_smoke);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || _vehicle == null || _vehicle.Model == null)
                return;

            if (!Emitting)
            {
                _lastRunWindow = -1;
                return;
            }

            ResolveFootBones();
            UpdateGallopRings();
        }

        private void ResolveFootBones()
        {
            if (_bonesResolved || _vehicle.Model?.Bones == null || _vehicle.Model.Bones.Length == 0)
                return;

            var bones = _vehicle.Model.Bones;
            string[] names = { "Bip01 L Foot", "Bip01 R Foot", "Bip01 L Toe0", "Bip01 R Toe0" };
            for (int i = 0; i < _footBones.Length; i++)
                _footBones[i] = FindBoneIndex(bones, names[i]);
            _bonesResolved = true;
        }

        private static int FindBoneIndex(BMDTextureBone[] bones, string name)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (string.Equals(bones[i].Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            // Fallback for variant skeletons: first "Foot" bone, then any "Toe0".
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].Name?.Contains("Foot", StringComparison.OrdinalIgnoreCase) == true)
                    return i;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].Name?.Contains("Toe0", StringComparison.OrdinalIgnoreCase) == true)
                    return i;
            return -1;
        }

        private void UpdateGallopRings()
        {
            var model = _vehicle.Model;
            if (model.Actions == null || model.Actions.Length == 0)
                return;

            int runAction = _vehicle.RunActionIndex;
            if (runAction < 0 || runAction >= model.Actions.Length)
                return;

            // One hoof strike per quarter of the gallop cycle (original: 4 shock waves
            // per run cycle at animation frames 1.0..1.8).
            int totalFrames = Math.Max(model.Actions[runAction]?.NumAnimationKeys ?? 1, 1);
            int quarter = Math.Max(1, totalFrames / 4);
            int frame = _vehicle.CurrentFrame;
            int window = Math.Min(3, frame / quarter);

            if (window != _lastRunWindow)
            {
                _lastRunWindow = window;
                SpawnAtFoot(window);
            }
        }

        private void SpawnAtFoot(int footIndex)
        {
            if (footIndex < 0 || footIndex >= _footBones.Length)
                return;

            int boneIndex = _footBones[footIndex];
            if (boneIndex < 0)
                return;

            Matrix[] bones = _vehicle.GetBoneTransforms();
            if (bones == null || boneIndex >= bones.Length)
                return;

            // Bone matrices are object-local; combine with the vehicle world transform.
            Vector3 objectSpace = Vector3.Transform(Vector3.Zero, bones[boneIndex]);
            Vector3 world = Vector3.Transform(objectSpace, _vehicle.WorldPosition);

            if (World?.Terrain != null)
                world.Z = World.Terrain.RequestTerrainHeight(world.X, world.Y) + 4f;

            SpawnRing(world);
            _smoke.EmitBurst(world);
        }

        private void SpawnRing(Vector3 worldPosition)
        {
            if (World == null)
                return;

            var ring = new ShockwaveRingModel
            {
                Position = worldPosition,
                Angle = Vector3.Zero,
                // Original start: (rand()%10+10)/10 tiles = 1.0..1.9 tiles, scaled down (RingSizeFactor).
                Scale = (10 + MuGame.Random.Next(10)) / 10f * RingScalePerTile * RingSizeFactor
            };
            World.Objects.Add(ring);
            _ = ring.Load();
        }

        /// <summary>
        /// Ground shock-wave ring (original BITMAP_SHOCK_WAVE SubType 1):
        /// life ~20 frames, scale grows a random 0..0.4 tile/frame with small X/Y jitter.
        /// Fades out by reducing Alpha (TotalAlpha → effect.Alpha) so the ring visibly
        /// disappears instead of popping out at full brightness.
        /// </summary>
        private sealed class ShockwaveRingModel : ModelObject
        {
            private float _lifeFrames = RingLifeFrames;
            private float _scaleGrowth;

            public ShockwaveRingModel()
            {
                ContinuousAnimation = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                BlendState = BlendState.Additive;
                BlendMeshState = BlendState.Additive;
                BlendMesh = 0;
                DepthState = DepthStencilState.DepthRead;
                LightEnabled = true;
                Alpha = 0f;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(RingModelPath);
                await base.Load();

                // Original: o->Scale += (rand() % 5) / 10.f per frame → 0..0.4 TILES/frame.
                // Converted to model scale, scaled down via RingSizeFactor.
                _scaleGrowth = MuGame.Random.Next(5) * (0.1f * RingScalePerTile * RingSizeFactor);
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                Scale = Math.Min(Scale + _scaleGrowth * factor, RingMaxScale);
                Position = new Vector3(
                    Position.X + (float)(MuGame.Random.Next(8) - 4) * factor,
                    Position.Y + (float)(MuGame.Random.Next(8) - 4) * factor,
                    Position.Z);

                _lifeFrames -= factor;
                // Fade out by reducing alpha (linear, like the original Luminosity = LifeTime / 20).
                float t = MathHelper.Clamp(_lifeFrames / RingLifeFrames, 0f, 1f);
                Alpha = t * 0.8f;

                if (_lifeFrames <= 0f)
                    RemoveSelf();
            }

            private void RemoveSelf()
            {
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);
                Dispose();
            }
        }

        /// <summary>
        /// Smoke puffs around each shock wave (original BITMAP_SMOKE = Effect/smoke01.jpg,
        /// 5..9 per burst, subtype 0: life ~16 frames, rises and grows).
        /// Emission is burst-driven by the parent effect, not continuous.
        /// </summary>
        private sealed class DarkHorseGallopSmoke : SourceParticleSystem
        {
            private const string SmokeTexturePath = "Effect/smoke01.jpg";
            private const int MaxParticles = 48;

            private Texture2D _texture = null!;
            private Vector2 _textureCenter;

            protected override Texture2D? ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public DarkHorseGallopSmoke()
                : base(MaxParticles)
            {
                BlendState = BlendState.Additive;
                MaxDistance = 2000f;
                ReferenceDistance = 800f;
                ScaleGrowth = 0.8f;
            }

            public override async Task LoadContent()
            {
                await TextureLoader.Instance.Prepare(SmokeTexturePath);
                _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            /// <summary>Emits 5..9 smoke puffs around the given world position (original rand()%5+5).</summary>
            public void EmitBurst(Vector3 position)
            {
                if (_texture == null)
                    return;

                int count = 5 + MuGame.Random.Next(5);
                for (int i = 0; i < count; i++)
                {
                    CreateParticle(
                        type: 0,
                        position: new Vector3(
                            position.X + (float)(MuGame.Random.Next(50) - 25),
                            position.Y + (float)(MuGame.Random.Next(50) - 25),
                            position.Z + (float)(MuGame.Random.Next(16) - 8) - 10f),
                        angle: Vector3.Zero,
                        light: new Vector3(1f, 1f, 1f));
                }
            }

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                // Original BITMAP_SMOKE subtype 0: life 16 frames, scale 0.48..0.79.
                float lifetime = RandomRange(0.55f, 0.75f);
                particle.LifeTime = lifetime;
                particle.MaxLifeTime = lifetime;
                particle.Scale = RandomRange(0.5f, 0.8f);
                particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
                particle.Velocity = new Vector3(
                    RandomRange(-6f, 6f),
                    RandomRange(-6f, 6f),
                    RandomRange(10f, 22f));
                particle.Gravity = -2f;
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                particle.Position += particle.Velocity * dt;
                particle.Velocity.Z += particle.Gravity * dt;
                particle.Rotation += dt * 0.5f;
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
            {
                // Original: Luminosity = LifeTime / 8 → linear fade.
                float alpha = MathHelper.Clamp(lifeRatio, 0f, 1f) * 0.6f;
                return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, alpha);
            }
        }
    }
}
