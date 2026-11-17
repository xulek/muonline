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
    /// Decay (AT_SKILL_BLAST_POISON = 38), SourceMain5.2:
    /// packet  WSclient.cpp:4842       SetPlayerMagic
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4283   2 x MODEL_FIRE sub 6 at the tile centre, Light (0.8, 0.5, 0.1), SOUND_DEATH_POISON1
    /// fire    ZzzEffect.cpp:2683 (init: HiddenMesh -2, joint BITMAP_SMOKE sub 0), 12250/12305 (move: sprites, impact),
    ///         12440 (BITMAP_FIRE sub 5 x2, green); impact: MODEL_SKILL_INFERNO sub 2, 15 x BITMAP_SMOKE sub 11,
    ///         6 x MODEL_STONE, BITMAP_EXPLOTION, SOUND_DEATH_POISON2
    /// </summary>
    internal sealed class DecayEffect : SourceSkillEffectBase
    {
        private const string LightTexturePath = "Effect/flare01.jpg";        // BITMAP_LIGHT
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";        // BITMAP_SHINY + 1
        private const string TrailTexturePath = "Effect/Flare.jpg";          // BITMAP_SMOKE joint TexType BITMAP_FLARE
        private const string SmokeTexturePath = "Effect/smoke01.jpg";        // BITMAP_SMOKE
        private const string FireTexturePath = "Effect/Fire01.jpg";          // BITMAP_FIRE (4x1)
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg";// BITMAP_EXPLOTION (4x4)
        private const string InfernoModelPath = "Skill/Inferno01.bmd";      // MODEL_SKILL_INFERNO
        private const string Sound1 = "Sound/eBlastPoison_1.wav";
        private const string Sound2 = "Sound/eBlastPoison_2.wav";
        private const string ExplosionSound = "Sound/eExplosion.wav";

        private const int SkillId = 38;
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private static readonly Vector3 FireLight = new(0.8f, 0.5f, 0.1f);
        private static readonly Vector3 HeadAngle = new(0f, 20f, 0f);
        private static readonly Vector3 ImpactLight = new(0.1f, 0.5f, 0.1f);
        private static readonly Vector3 PoisonGlowColor = new(0.4f, 0.9f, 0.2f);
        private const float ImpactLightSeconds = 0.6f;

        private struct Ball
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Step;
            public float LifeTime;
            public float TrailLifeTime;
            public SourceTrail Trail;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public Vector3 BaseLight;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 Position;
            public float Scale;
            public float LifeTime;
        }

        private readonly Vector3 _target;
        private readonly Ball[] _balls = new Ball[2];
        private readonly Burst[] _infernos = new Burst[2];
        private readonly Burst[] _explosions = new Burst[2];
        private readonly Particle[] _smokes = new Particle[48];
        private readonly Particle[] _fires = new Particle[96];
        private readonly SourceStonePool _stones = new(12, 48);
        private SourceModelRenderer? _infernoModel;
        private Texture2D? _lightTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _trailTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _fireTexture;
        private Texture2D? _explosionTexture;
        private Vector3 _trailRight;
        private Vector3 _trailUp;
        private float _infernoFrame;
        private readonly Vector3[] _impactLightPositions = new Vector3[2];
        private readonly float[] _impactLightAges = { -1f, -1f };
        private float _lightTime;

        public DecayEffect(WalkerObject caster, Vector3 target)
            : base(caster, 5f, ReleaseDelaySeconds, 512)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _trailTexture = await LoadTexture(TrailTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _fireTexture = await LoadTexture(FireTexturePath);
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _infernoModel = await LoadModel(InfernoModelPath, -2);
            if (_infernoModel != null)
            {
                _infernoModel.AnimationSpeed = 0f;
                _infernoModel.HiddenMesh = 0; // HiddenMesh = SkillIndex (0)
            }
            await _stones.LoadAsync(World);
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            var ground = new Vector3(_target.X, _target.Y, TerrainHeight(_target));
            SourceJointMath.AngleBasis(HeadAngle, out _trailRight, out _, out _trailUp);
            int maxTails = SourceJointMath.ScaledMaxTails(20);

            for (int i = 0; i < _balls.Length; i++)
            {
                ref Ball b = ref _balls[i];
                b.Live = true;
                b.LifeTime = 40f;
                b.TrailLifeTime = 20f;
                b.Position = ground + new Vector3(
                    (MuGame.Random.Next(100) + 200) * factor,
                    (MuGame.Random.Next(100) - 50) * factor,
                    (MuGame.Random.Next(300) + 500) * factor);
                b.Step = SourceJointMath.Rotate(new Vector3(0f, 0f, -50f - MuGame.Random.Next(50)), HeadAngle);
                b.Trail = new SourceTrail(maxTails);
                b.Trail.Push(b.Position, _trailRight, _trailUp, 100f);
            }

            PlayUnmappedSound(SkillId, Sound1);
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _balls.Length; i++)
            {
                ref Ball b = ref _balls[i];

                // BITMAP_SMOKE joint sub 0: follows the fire while it lives, LifeTime 20.
                if (b.TrailLifeTime > 0f)
                {
                    if (b.Live)
                        b.Trail.Push(b.Position, _trailRight, _trailUp, 100f);
                    b.TrailLifeTime -= factor;
                }

                if (!b.Live)
                    continue;

                float height = TerrainHeight(b.Position);
                if (b.Position.Z < height)
                {
                    b.Position.Z = height;
                    _impactLightPositions[i] = b.Position;
                    _impactLightAges[i] = 0f;
                    Impact(i, b.Position, factor);
                    b.Live = false;
                    continue;
                }

                float luminosity = (MuGame.Random.Next(4) + 7) * 0.1f;
                if (b.LifeTime < 5f)
                    luminosity = MathF.Max(0f, luminosity - (5f - b.LifeTime) * 0.2f);
                for (int j = 0; j < 2; j++)
                    if (FpsCheck(1, factor))
                        SpawnFire(new Vector3(b.Position.X + MuGame.Random.Next(50) - 25, b.Position.Y, b.Position.Z),
                            new Vector3(luminosity * 0.1f, luminosity, 0f));

                b.Position += b.Step * factor;
                b.LifeTime -= factor;
                if (b.LifeTime <= 0f)
                    b.Live = false;
            }

            for (int i = 0; i < 2; i++)
            {
                ref Burst inf = ref _infernos[i];
                if (inf.Live)
                {
                    inf.Scale += 0.04f * factor;
                    inf.LifeTime -= factor;
                    if (inf.LifeTime <= 0f)
                        inf.Live = false;
                }

                ref Burst e = ref _explosions[i];
                if (e.Live)
                {
                    e.LifeTime -= factor;
                    if (e.LifeTime <= 0f)
                        e.Live = false;
                }
            }
            _infernoFrame += 0.5f * factor;

            MoveSmokes(factor);
            MoveFires(factor);
            _stones.Move(factor, World?.Terrain);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            // One slot per ball: green glow while falling, then a poison burst flash where it lands.
            _lightTime += factor * 0.04f;
            float fadeIn = MathHelper.Clamp(_lightTime / 0.15f, 0f, 1f);
            for (int i = 0; i < _balls.Length; i++)
            {
                ref Ball b = ref _balls[i];
                if (b.Live)
                {
                    Lights.Set(i, b.Position, PoisonGlowColor, 220f, fadeIn * MathHelper.Clamp(b.LifeTime / 5f, 0f, 1f));
                    continue;
                }

                float age = _impactLightAges[i];
                if (age < 0f)
                {
                    Lights.Hide(i);
                    continue;
                }

                age += factor * 0.04f;
                _impactLightAges[i] = age;
                float flash = EffectLights.Flash(age, ImpactLightSeconds);
                Lights.Set(i, _impactLightPositions[i] + new Vector3(0f, 0f, 60f), PoisonGlowColor, 260f + 160f * flash, 2f * flash);
            }
        }

        private void Impact(int index, Vector3 position, float factor)
        {
            if (FpsCheck(1, factor))
            {
                _infernos[index] = new Burst { Live = true, Position = position, Scale = 0.3f, LifeTime = 12f };
                _infernoFrame = 0f;
            }

            for (int i = 0; i < 15; i++)
            {
                if (!FpsCheck(1, factor))
                    continue;
                var pos = new Vector3(position.X + MuGame.Random.Next(160) - 80, position.Y + MuGame.Random.Next(160) - 100, position.Z + 50f);
                SpawnSmoke(pos, (MuGame.Random.Next(32) + 80) * 0.025f);
            }

            for (int j = 0; j < 6; j++)
                if (FpsCheck(1, factor))
                    _stones.Spawn(position, HeadAngle, ImpactLight, 0);

            SoundController.Instance.PlayBuffer(Sound2);

            if (FpsCheck(1, factor))
            {
                _explosions[index] = new Burst { Live = true, Position = position + new Vector3(0f, 0f, 80f), LifeTime = 20f };
                SoundController.Instance.PlayBuffer(ExplosionSound);
            }
        }

        private void SpawnSmoke(Vector3 position, float scale)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position + new Vector3(
                    (MuGame.Random.Next(64) - 32) * factor,
                    (MuGame.Random.Next(64) - 32) * factor,
                    (MuGame.Random.Next(64) + 32) * factor);
                p.LifeTime = 50f;
                p.Scale = scale;
                p.Angle = new Vector3(MuGame.Random.Next(90) - 45, HeadAngle.Y, MuGame.Random.Next(360));
                p.Rotation = MuGame.Random.Next(360);
                p.BaseLight = ImpactLight; // TurningForce
                p.Light = ImpactLight;
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Light = p.BaseLight * (p.LifeTime / 50f);
                p.Velocity *= 0.4f; // per frame
                p.Scale += factor * 0.05f;
            }
        }

        private void SpawnFire(Vector3 position, Vector3 light)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.Angle = HeadAngle;
                p.Light = light;
                p.LifeTime = 24f;
                p.Rotation = MuGame.Random.Next(360);
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                p.Scale = (MuGame.Random.Next(64) + 128) * 0.01f;
                return;
            }
        }

        private void MoveFires(float factor)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                // BITMAP_FIRE sub 5
                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Gravity += 0.004f * factor;
                p.Scale -= 0.04f * factor;
                p.Rotation += 5f * factor;
                p.Position.Z += p.Gravity * 10f * factor;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < 2; i++)
                if (_balls[i].Live || _balls[i].TrailLifeTime > 0f || _infernos[i].Live || _explosions[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            return _stones.HasLive;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            // RenderJoints BITMAP_SMOKE sub 0: tail 0 skipped, light * min(LifeTime, 20) * 0.1.
            if (Batch.Begin(_trailTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    ref Ball b = ref _balls[i];
                    if (b.TrailLifeTime <= 0f)
                        continue;
                    float alpha = MathF.Min(b.TrailLifeTime, 20f) * 0.1f;
                    Batch.AddTrail(b.Trail, SourceQuadBatch.TrailFaces.Both, new Vector3(alpha), SourceQuadBatch.TrailShade.Flat, b.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_infernoModel != null)
            {
                bool updated = false;
                for (int i = 0; i < 2; i++)
                {
                    ref Burst inf = ref _infernos[i];
                    if (!inf.Live)
                        continue;
                    if (!updated)
                    {
                        _infernoModel.SetAnimationTime(_infernoFrame);
                        _infernoModel.Update(LastGameTime);
                        updated = true;
                    }
                    Vector3 light = (terrain?.EvaluateTerrainLight(inf.Position.X, inf.Position.Y) ?? Vector3.One) * new Vector3(0f, 0.5f, 0f);
                    _infernoModel.DrawInstance(inf.Position, Vector3.Zero, inf.Scale, light, 0.1f);
                }
            }

            _stones.DrawModels(LastGameTime, terrain);

            // Sprites of the falling fire (CreateSprite every tick at the fire position).
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _balls.Length; i++)
                    if (_balls[i].Live)
                        Batch.AddSprite(_balls[i].Position, 3f, new Vector3(0.5f, 0.5f, 1f));
                Batch.End();
            }
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    if (!_balls[i].Live)
                        continue;
                    Batch.AddSprite(_balls[i].Position, 3f, Vector3.One, MuGame.Random.Next(360));
                    Batch.AddSprite(_balls[i].Position, 4f, FireLight, MuGame.Random.Next(360));
                }
                Batch.End();
            }

            if (_explosionTexture != null && Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < 2; i++)
                {
                    ref Burst e = ref _explosions[i];
                    if (!e.Live)
                        continue;
                    int frame = (int)((20f - e.LifeTime) / 2f);
                    Batch.AddSpriteRegion(e.Position, _explosionTexture.Width, _explosionTexture.Height, ImpactLight, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                }
                Batch.End();
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Particle p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }

            if (_fireTexture != null && Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Particle p = ref _fires[i];
                    if (!p.Live)
                        continue;
                    int frame = (int)((23f - p.LifeTime) / 6f);
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, p.Rotation,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            _stones.DrawFires(Batch);
        }

        protected override void DisposeEffect()
        {
            _infernoModel?.Dispose();
            _stones.Dispose();
        }
    }
}
