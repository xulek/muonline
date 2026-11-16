#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Fireworks of a dropped Box of Luck / firecracker item, SourceMain5.2:
    /// packet  WSclient.cpp:7914 ReceiveServerCommand cmd 0 (sub 0) / cmd 59 (sub 1, Christmas): BITMAP_FIRECRACKER0001
    /// root    ZzzEffect.cpp:3011 (LifeTime 31), 12526 (BITMAP_JOINT_SPIRIT sub 25 at LifeTime 31/24/17/9/1, +-100)
    /// rocket  ZzzEffectJoint.cpp:878 (Velocity 9, LifeTime 26, MaxTails 30, BITMAP_SHINY, Angle -90), 4125 (rise, grow,
    ///         BITMAP_LIGHT 0.5 + BITMAP_DS_SHOCK 0.15 sprites, BITMAP_FIRECRACKER0002 at LifeTime 10, fade below 10)
    /// burst   ZzzEffect.cpp:3017 (EXPLOTION_MONO 0.6, 60 x SPARK+1 sub 27, 30 x sub 28, DS_SHOCK sprite, 60 x SHINY sub 6,
    ///         sub 1: MODEL_HALLOWEEN_CANDY_STAR sub 1 + 2 x MODEL_XMAS_EVENT_BOX..SOCKS, SOUND_XMAS_FIRECRACKER),
    ///         12537 (rand_fps_check(5): BITMAP_FIRECRACKER0003 +-150, scale 0.5..0.9)
    /// star    ZzzEffect.cpp:3055 (LifeTime 15, random roll), 12550 (firecracker0001..0007 frames, then fall and fade)
    /// particles ZzzEffectParticle.cpp:2476/4230/8993 (EXPLOTION_MONO), 2332/6641 (SPARK+1 27/28), 2517/7108 (SHINY 6)
    /// models  ZzzEffect.cpp:3868/7421 (candy star), 3908/7451 (xmas box, candy, tree, socks)
    /// </summary>
    internal sealed class FireworksEffect : SourceSkillEffectBase
    {
        private const int MaxRockets = 16;
        private const int MaxBursts = 16;
        private const int MaxStars = 96;
        private const int MaxParticles = 2048;
        private const int MaxModels = 16;
        private const int StarFrames = 7;

        private static readonly Vector3 RocketAngle = new(-90f, 0f, 0f);
        private static readonly string[] XmasModelPaths =
        {
            "Skill/xmasebox.bmd", "Skill/xmasecandy.bmd", "Skill/xmasetree.bmd", "Skill/xmaseyangbal.bmd"
        };

        private enum ParticleKind : byte { Explosion, Spark27, Spark28, Shiny6 }

        private struct Rocket
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
        }

        private struct Star
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
            public int Frame;
        }

        private struct Particle
        {
            public bool Live;
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float StartScale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private struct ModelPiece
        {
            public bool Live;
            public int Model; // 0..3 xmas, 4 candy star
            public Vector3 Position;
            public Vector3 Direction;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float LifeTime;
            public float GroundOffset;
            public int SpinAxis;
        }

        private struct Flash
        {
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
        }

        private readonly Vector3 _origin;
        private readonly int _subType;
        private readonly Rocket[] _rockets = new Rocket[MaxRockets];
        private readonly Burst[] _bursts = new Burst[MaxBursts];
        private readonly Star[] _stars = new Star[MaxStars];
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly ModelPiece[] _models = new ModelPiece[MaxModels];
        private readonly Flash[] _flashes = new Flash[MaxBursts];
        private int _flashCount;
        private float _lifeTime = 31f;

        private Texture2D? _shinyTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _shockTexture;
        private Texture2D? _sparkTexture;
        private Texture2D? _explosionTexture;
        private readonly Texture2D?[] _starTextures = new Texture2D?[StarFrames];
        private readonly SourceModelRenderer?[] _modelRenderers = new SourceModelRenderer?[5];

        private FireworksEffect(WalkerObject anchor, Vector3 origin, int subType)
            : base(anchor, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 6144)
        {
            _origin = origin;
            _subType = subType;
            Position = origin;
        }

        /// <summary>ReceiveServerCommand cmd 0 (sub 0) / cmd 59 (sub 1) at tile (x, y).</summary>
        public static void Spawn(Client.Main.Controls.WalkableWorldControl world, byte x, byte y, int subType)
        {
            if (world.Walker == null || world.Terrain == null)
                return;

            float wx = (x + 0.5f) * Constants.TERRAIN_SCALE;
            float wy = (y + 0.5f) * Constants.TERRAIN_SCALE;
            var origin = new Vector3(wx, wy, world.Terrain.RequestTerrainHeight(wx, wy));
            var effect = new FireworksEffect(world.Walker, origin, subType);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        protected override async Task LoadEffectContent()
        {
            _shinyTexture = await LoadTexture("Effect/Shiny01.jpg");          // BITMAP_SHINY
            _lightTexture = await LoadTexture("Effect/flare01.jpg");          // BITMAP_LIGHT
            _shockTexture = await LoadTexture("Effect/Shockwave.jpg");        // BITMAP_DS_SHOCK
            _sparkTexture = await LoadTexture("Effect/Spark03.jpg");          // BITMAP_SPARK + 1
            _explosionTexture = await LoadTexture("Effect/explotion01mono.jpg"); // BITMAP_EXPLOTION_MONO
            for (int i = 0; i < StarFrames; i++)
                _starTextures[i] = await LoadTexture($"Effect/firecracker000{i + 1}.jpg");

            if (_subType == 1)
            {
                for (int i = 0; i < XmasModelPaths.Length; i++)
                {
                    _modelRenderers[i] = await LoadModel(XmasModelPaths[i], blendMesh: -1);
                    _modelRenderers[i]?.UseNormalBlend();
                }

                _modelRenderers[4] = await LoadModel("Skill/hstar.bmd", blendMesh: -1);
                _modelRenderers[4]?.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            TickRoot(factor);
            TickRockets(factor);
            TickBursts(factor);
            TickStars(factor);
            TickParticles(factor);
            TickModels(factor);
        }

        private void TickRoot(float factor)
        {
            if (_lifeTime <= 0f)
                return;

            // MoveEffect BITMAP_FIRECRACKER0001: a rocket at LifeTime 31, 24, 17, 9 and 1 (CreateJointFpsChecked).
            int life = (int)_lifeTime;
            if ((life == 1 || life == 9 || life == 17 || life == 24 || life == 31) && FpsCheck(1, factor))
            {
                var position = new Vector3(
                    _origin.X + (MuGame.Random.Next(200) - 100),
                    _origin.Y + (MuGame.Random.Next(200) - 100),
                    _origin.Z);
                SpawnRocket(position);
            }

            _lifeTime -= factor;
        }

        private void SpawnRocket(Vector3 position)
        {
            for (int i = 0; i < _rockets.Length; i++)
            {
                ref Rocket r = ref _rockets[i];
                if (r.Live)
                    continue;

                r.Live = true;
                r.Position = position;
                r.Light = new Vector3(0.9f, 0.8f, 1f);
                r.Scale = 1f;
                r.LifeTime = 26f;
                int maxTails = SourceJointMath.ScaledMaxTails(30);
                r.Trail ??= new SourceTrail(200);
                r.Trail.Reset(maxTails);
                r.Trail.Push(position, RocketAngle, r.Scale);
                return;
            }
        }

        private void TickRockets(float factor)
        {
            for (int i = 0; i < _rockets.Length; i++)
            {
                ref Rocket r = ref _rockets[i];
                if (!r.Live)
                    continue;

                // MoveJoint prologue: Velocity 9 along AngleMatrix(-90, 0, 0) = +Z.
                r.Position += SourceJointMath.Rotate(new Vector3(0f, -9f, 0f), RocketAngle) * factor;

                if (r.LifeTime < 10f)
                {
                    r.Light *= MathF.Pow(1f / 1.45f, factor);
                    if (r.Light.X < 0.2f)
                    {
                        r.Live = false;
                        continue;
                    }
                }
                else
                {
                    r.Position.X += (MuGame.Random.Next(16) - 8f) * factor;
                    r.Position.Y += (MuGame.Random.Next(16) - 8f) * factor;
                    r.Position.Z += 9f * factor;
                    r.Scale += 3f * factor;
                }

                if ((int)r.LifeTime == 10 && FpsCheck(1, factor))
                    SpawnBurst(r.Position, r.Light);

                r.Trail.Push(r.Position, RocketAngle, r.Scale);
                r.LifeTime -= factor;
                if (r.LifeTime <= 0f)
                    r.Live = false;
            }
        }

        private void SpawnBurst(Vector3 position, Vector3 light)
        {
            int slot = Array.FindIndex(_bursts, b => !b.Live);
            if (slot < 0)
                return;

            _bursts[slot] = new Burst { Live = true, Position = position, LifeTime = 30f };

            // CreateParticle(BITMAP_EXPLOTION_MONO, ..., o->Light, 0, 0.6f): plays SOUND_EXPLOTION01.
            AddParticle(ParticleKind.Explosion, position, Vector3.Zero, RocketAngle, light, 0.6f, 20f, 0f, 0f);
            SoundController.Instance.PlayBuffer("Sound/eExplosion.wav");

            for (int i = 0; i < 60; i++)
            {
                // SPARK+1 sub 27: random direction, length 12.
                var speed = new Vector3(MuGame.Random.Next(16) - 8f, MuGame.Random.Next(16) - 8f, MuGame.Random.Next(16) - 8f);
                if (speed.LengthSquared() > 0f)
                    speed.Normalize();
                speed *= 12f;
                AddParticle(ParticleKind.Spark27, position, speed, RocketAngle, Vector3.One,
                    MuGame.Random.Next(20) / 20f + 1f, MuGame.Random.Next(10) + 10f, 0f, 0f);
            }

            for (int i = 0; i < 30; i++)
            {
                // SPARK+1 sub 28: Angle[2] random, Velocity = rotate((rand40-20)*0.25, ...), Gravity 20.
                var angle = new Vector3(RocketAngle.X, RocketAngle.Y, MuGame.Random.Next(360));
                var p = new Vector3((MuGame.Random.Next(40) - 20) * 0.1f, (MuGame.Random.Next(40) - 20) * 0.1f, 0f) * 2.5f;
                AddParticle(ParticleKind.Spark28, position, SourceJointMath.Rotate(p, angle), angle, Vector3.One,
                    MuGame.Random.Next(20) / 20f + 1f, MuGame.Random.Next(10) + 20f, 20f, 0f);
            }

            // CreateSprite(BITMAP_DS_SHOCK, +-50, rand() % 10 * 0.1 + 1.5): one frame.
            if (_flashCount < _flashes.Length)
            {
                _flashes[_flashCount++] = new Flash
                {
                    Position = new Vector3(position.X + (MuGame.Random.Next(100) - 50), position.Y + (MuGame.Random.Next(100) - 50), position.Z),
                    Light = light,
                    Scale = MuGame.Random.Next(10) * 0.1f + 1.5f
                };
            }

            Vector3 shinyLight = Vector3.One;
            for (int i = 0; i < 60; i++)
            {
                shinyLight = new Vector3(
                    0.3f + MuGame.Random.Next(700) * 0.001f,
                    0.3f + MuGame.Random.Next(700) * 0.001f,
                    0.3f + MuGame.Random.Next(700) * 0.001f);
                // SHINY sub 6: random angle, Velocity = rotate((8, 8, 8)), Gravity 20, flickers below LifeTime 60.
                var angle = new Vector3(MuGame.Random.Next(360), MuGame.Random.Next(360), MuGame.Random.Next(360));
                float scale = MuGame.Random.Next(5) / 10f + 0.5f;
                AddParticle(ParticleKind.Shiny6, position, SourceJointMath.Rotate(new Vector3(8f, 8f, 8f), angle), angle,
                    shinyLight, scale, MuGame.Random.Next(10) + 60f, 20f, MuGame.Random.Next(20) - 10f);
            }

            if (_subType == 1)
            {
                SpawnModel(4, position, shinyLight);
                SpawnModel(MuGame.Random.Next(4), position, shinyLight);
                SpawnModel(MuGame.Random.Next(4), position, shinyLight);
            }

            SoundController.Instance.PlayBuffer("Sound/xmas/Christmas_Fireworks01.wav"); // SOUND_XMAS_FIRECRACKER
        }

        private void AddParticle(ParticleKind kind, Vector3 position, Vector3 velocity, Vector3 angle, Vector3 light,
            float scale, float lifeTime, float gravity, float rotation)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;

                p = new Particle
                {
                    Live = true,
                    Kind = kind,
                    Position = position,
                    Velocity = velocity,
                    Angle = angle,
                    Light = light,
                    Scale = scale,
                    StartScale = scale,
                    Gravity = gravity,
                    Rotation = rotation,
                    LifeTime = lifeTime
                };
                return;
            }
        }

        private void SpawnModel(int model, Vector3 position, Vector3 light)
        {
            if (_modelRenderers[model] == null)
                return;

            int slot = Array.FindIndex(_models, m => !m.Live);
            if (slot < 0)
                return;

            var angle = new Vector3(MuGame.Random.Next(360), MuGame.Random.Next(360), MuGame.Random.Next(360));
            var p = new Vector3((MuGame.Random.Next(60) - 30) * 0.1f, (MuGame.Random.Next(60) - 30) * 0.1f, -1f) * 1.5f;
            bool star = model == 4;
            float scale = star
                ? 2f + (MuGame.Random.Next(10) - 5) * 0.02f
                : 0.7f + (MuGame.Random.Next(10) - 5) * 0.02f + (model == 0 ? 0.3f : 0f); // MODEL_XMAS_EVENT_BOX +0.3 (25 Hz)
            _models[slot] = new ModelPiece
            {
                Live = true,
                Model = model,
                Position = position,
                Direction = SourceJointMath.Rotate(p, angle),
                Angle = angle,
                Light = light,
                Scale = scale,
                Gravity = star ? MuGame.Random.Next(10) + 10f : MuGame.Random.Next(5) + 15f,
                LifeTime = star ? MuGame.Random.Next(10) + 40f : MuGame.Random.Next(10) + 50f,
                GroundOffset = star ? 0f : 3f,
                SpinAxis = MuGame.Random.Next(3)
            };
        }

        private void TickBursts(float factor)
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst b = ref _bursts[i];
                if (!b.Live)
                    continue;

                if (FpsCheck(5, factor) && FpsCheck(1, factor))
                {
                    var light = new Vector3(
                        0.3f + MuGame.Random.Next(700) * 0.001f,
                        0.3f + MuGame.Random.Next(700) * 0.001f,
                        0.3f + MuGame.Random.Next(700) * 0.001f);
                    var position = new Vector3(
                        b.Position.X + (MuGame.Random.Next(300) - 150),
                        b.Position.Y + (MuGame.Random.Next(300) - 150),
                        b.Position.Z);
                    SpawnStar(position, light, 0.5f + MuGame.Random.Next(5) * 0.1f);
                }

                b.LifeTime -= factor;
                if (b.LifeTime <= 0f)
                    b.Live = false;
            }
        }

        private void SpawnStar(Vector3 position, Vector3 light, float scale)
        {
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i].Live)
                    continue;

                _stars[i] = new Star
                {
                    Live = true,
                    Position = position,
                    Light = light,
                    Scale = scale,
                    Rotation = MuGame.Random.Next(360),
                    LifeTime = 15f
                };
                return;
            }
        }

        private void TickStars(float factor)
        {
            for (int i = 0; i < _stars.Length; i++)
            {
                ref Star s = ref _stars[i];
                if (!s.Live)
                    continue;

                if (s.LifeTime > 15f - 8f)
                {
                    s.Frame = Math.Min((int)((15f - s.LifeTime) / 8f * 7f), 6);
                }
                else
                {
                    s.Frame = 6;
                    s.Position.Z -= factor;
                    s.Light *= MathF.Pow(1f / 1.05f, factor);
                    s.Scale *= MathF.Pow(1.02f, factor);
                }

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
        }

        private void TickParticles(float factor)
        {
            var terrain = World?.Terrain;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                // MovePosition(Position, Angle, Velocity): the velocity is rotated by the particle angle every frame.
                if (p.Velocity != Vector3.Zero)
                    p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;

                switch (p.Kind)
                {
                    case ParticleKind.Spark27:
                        p.Light *= MathF.Pow(1f / 1.02f, factor);
                        p.Scale *= MathF.Pow(1f / 1.03f, factor);
                        break;
                    case ParticleKind.Spark28:
                    {
                        p.Light *= MathF.Pow(1f / 1.02f, factor);
                        p.Scale *= MathF.Pow(1f / 1.01f, factor);
                        if (p.Light.X <= 0.03f)
                        {
                            p.Live = false;
                            break;
                        }

                        p.Position.Z += p.Gravity * 0.5f * factor;
                        p.Gravity -= 1.5f * factor;
                        float height = terrain?.RequestTerrainHeight(p.Position.X, p.Position.Y) ?? p.Position.Z;
                        if (p.Position.Z < height + 3f)
                        {
                            p.Position.Z = height + 3f;
                            p.Gravity = -p.Gravity * 0.3f;
                            p.LifeTime -= 2f * factor;
                        }

                        break;
                    }
                    case ParticleKind.Shiny6:
                        p.Light *= MathF.Pow(1f / 1.01f, factor);
                        if (p.Light.X < 0.2f)
                        {
                            p.Live = false;
                            break;
                        }

                        p.StartScale *= MathF.Pow(1f / 1.01f, factor);
                        p.Scale = p.LifeTime < 60f && FpsCheck(2, factor) ? 0f : p.StartScale;
                        if (p.LifeTime < 60f)
                        {
                            float damp = MathF.Pow(1f / 1.04f, factor);
                            p.Velocity *= damp;
                            p.Position.Z -= p.Gravity * 0.1f * factor;
                            p.Gravity += 0.5f * factor;
                        }

                        break;
                }
            }
        }

        private void TickModels(float factor)
        {
            var terrain = World?.Terrain;
            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelPiece m = ref _models[i];
                if (!m.Live)
                    continue;

                if (m.SpinAxis == 0) m.Angle.X += 20f * factor;
                else if (m.SpinAxis == 1) m.Angle.Y += 20f * factor;
                else m.Angle.Z += 20f * factor;

                m.Position.Z += m.Gravity * 0.5f * factor;
                m.Gravity -= 1.5f * factor;
                float height = (terrain?.RequestTerrainHeight(m.Position.X, m.Position.Y) ?? m.Position.Z) + m.GroundOffset;
                if (m.Position.Z < height)
                {
                    m.Position.Z = height;
                    m.Gravity = -m.Gravity * 0.3f;
                    m.LifeTime -= 2f * factor;
                }

                m.Position += m.Direction * factor;
                m.LifeTime -= factor;
                if (m.LifeTime <= 0f)
                    m.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f || _flashCount > 0)
                return true;
            foreach (var r in _rockets) if (r.Live) return true;
            foreach (var b in _bursts) if (b.Live) return true;
            foreach (var s in _stars) if (s.Live) return true;
            foreach (var m in _models) if (m.Live) return true;
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            // Joints: RENDER_TYPE_ALPHA_BLEND, both faces, BITMAP_SHINY.
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _rockets.Length; i++)
                {
                    ref Rocket r = ref _rockets[i];
                    if (r.Live)
                        Batch.AddTrail(r.Trail, SourceQuadBatch.TrailFaces.Both, r.Light, SourceQuadBatch.TrailShade.Flat, r.Trail.MaxTails - 1);
                }

                // SHINY sub 6 particles share the texture.
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && p.Kind == ParticleKind.Shiny6)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }

                Batch.End();
            }

            DrawModels();

            // Sprites (one frame each): BITMAP_LIGHT 0.5 and BITMAP_DS_SHOCK 0.15 on every rocket, DS_SHOCK flash of a burst.
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _rockets.Length; i++)
                    if (_rockets[i].Live)
                        Batch.AddSprite(_rockets[i].Position, 0.5f, _rockets[i].Light);
                Batch.End();
            }

            if (Batch.Begin(_shockTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _rockets.Length; i++)
                    if (_rockets[i].Live)
                        Batch.AddSprite(_rockets[i].Position, 0.15f, _rockets[i].Light);
                for (int i = 0; i < _flashCount; i++)
                    Batch.AddSprite(_flashes[i].Position, _flashes[i].Scale, _flashes[i].Light);
                Batch.End();
            }
            _flashCount = 0;

            for (int frame = 0; frame < StarFrames; frame++)
            {
                if (!Batch.Begin(_starTextures[frame], Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.PointClamp))
                    continue;
                for (int i = 0; i < _stars.Length; i++)
                {
                    ref Star s = ref _stars[i];
                    if (s.Live && s.Frame == frame)
                        Batch.AddSprite(s.Position, s.Scale, s.Light, s.Rotation);
                }
                Batch.End();
            }

            if (Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && p.Kind is ParticleKind.Spark27 or ParticleKind.Spark28)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }

            if (_explosionTexture != null &&
                Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (!p.Live || p.Kind != ParticleKind.Explosion)
                        continue;
                    int frame = (int)((20f - p.LifeTime) / 2f);
                    Batch.AddSpriteRegion(p.Position, _explosionTexture.Width * p.Scale, _explosionTexture.Height * p.Scale, p.Light, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                }
                Batch.End();
            }
        }

        private void DrawModels()
        {
            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelPiece m = ref _models[i];
                if (!m.Live)
                    continue;
                SourceModelRenderer? renderer = _modelRenderers[m.Model];
                if (renderer == null)
                    continue;
                renderer.SetAnimationTime(0);
                renderer.Update(LastGameTime);
                renderer.DrawInstance(m.Position,
                    new Vector3(MathHelper.ToRadians(m.Angle.X), MathHelper.ToRadians(m.Angle.Y), MathHelper.ToRadians(m.Angle.Z)),
                    m.Scale, m.Light, 1f);
            }
        }
    }
}
