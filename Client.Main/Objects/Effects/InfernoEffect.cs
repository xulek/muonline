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
    /// Inferno (AT_SKILL_INFERNO = 14), SourceMain5.2:
    /// packet  WSclient.cpp:4869       PLAYER_SKILL_INFERNO
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4424   CreateInferno(o->Position) + MODEL_SKILL_INFERNO sub 0
    /// inferno ZzzEffect.cpp:6556      8 x CreateBomb(r 220, Exp, 0) (6396: 20 x BITMAP_SPARK sub 2, BITMAP_EXPLOTION),
    ///                                 MODEL_STONE1/2 sub 1 + sub 0 per bomb
    /// model   ZzzEffect.cpp:1417      Skill/Inferno01.bmd, BlendMesh -2, Scale 0.9, Velocity 0.5, Light 0.8, LifeTime 15
    /// </summary>
    internal sealed class InfernoEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Skill/Inferno01.bmd";               // MODEL_SKILL_INFERNO
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg"; // BITMAP_EXPLOTION (4x4)
        private const string FireTexturePath = "Effect/Fire01.jpg";           // BITMAP_FIRE (4x1)
        private const string ExplosionSound = "Sound/eExplosion.wav";         // SOUND_EXPLOTION01
        private static readonly string[] StonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };

        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float StoneVelocity = 0.3f;

        private struct Stone
        {
            public bool Live;
            public int Model;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Stone[] _stones = new Stone[16];
        private readonly Particle[] _explosions = new Particle[8];
        private readonly Particle[] _fires = new Particle[64];
        private readonly SourceSparkPool _sparkPool = new(1, 160);
        private readonly SourceModelRenderer?[] _stoneRenderers = new SourceModelRenderer?[2];
        private SourceModelRenderer? _model;
        private Texture2D? _explosionTexture;
        private Texture2D? _fireTexture;
        private Texture2D? _sparkTexture;

        private Vector3 _position;
        private float _modelAngle;
        private readonly Vector3? _monsterReleasePosition;
        private readonly float _monsterReleaseYaw;
        private float _modelLifeTime;
        private float _modelFrame;
        private float _stoneFrame;

        // Dynamic lights (slot 0: ring of bombs flash, slot 1: lingering ember glow).
        private static readonly Vector3 FireLightColor = new(1f, 0.45f, 0.15f);
        private static readonly Vector3 EmberLightColor = new(1f, 0.35f, 0.1f);
        private float _lightAge;

        public InfernoEffect(WalkerObject caster, float releaseDelaySeconds = ReleaseDelaySeconds)
            : base(caster, 5f, releaseDelaySeconds, 512)
        {
            if (caster is MonsterObject)
            {
                _monsterReleasePosition = CasterPosition;
                _monsterReleaseYaw = CasterYawDegrees;
            }
        }

        internal static void SpawnMonsterAttack(MonsterObject caster)
        {
            if (caster.World is not { } world)
                return;

            var effect = new InfernoEffect(caster, releaseDelaySeconds: 0f);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        protected override async Task LoadEffectContent()
        {
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _fireTexture = await LoadTexture(FireTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            _model = await LoadModel(ModelPath, -2);
            if (_model != null)
                _model.AnimationSpeed = 0f;
            for (int i = 0; i < 2; i++)
            {
                var stone = await LoadModel(StonePaths[i], -1);
                if (stone == null)
                    continue;
                stone.AnimationSpeed = 0f;
                stone.UseNormalBlend();
                _stoneRenderers[i] = stone;
            }
        }

        protected override void OnStart()
        {
            _position = _monsterReleasePosition ?? CasterPosition;
            _modelAngle = _monsterReleasePosition.HasValue ? _monsterReleaseYaw : CasterYawDegrees;
            _modelLifeTime = 15f;

            if (Caster is MonsterObject monster)
                monster.PlayAttackSound(ExplosionSound, _position);

            for (int j = 0; j < 8; j++)
            {
                var angle = new Vector3(0f, 0f, j * 45f);
                Vector3 p = _position + SourceJointMath.Rotate(new Vector3(0f, -220f, 0f), angle);

                // CreateBomb(p, true, 0)
                Vector3 bomb = p + new Vector3(0f, 0f, 80f);
                for (int k = 0; k < 20; k++)
                    _sparkPool.SpawnSpark(bomb, new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, MuGame.Random.Next(30)), 2);
                SpawnExplosion(bomb);

                SpawnStone(p, angle, 1);
                SpawnStone(p, angle, 0);
            }
        }

        private void SpawnExplosion(Vector3 position)
        {
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.LifeTime = 20f;
                p.Scale = 1f;
                if (Caster is not MonsterObject)
                    SoundController.Instance.PlayBuffer(ExplosionSound);
                return;
            }
        }

        private void SpawnStone(Vector3 position, Vector3 angle, int subType)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.SubType = subType;
                s.Position = position;
                s.Angle = angle;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Angle.Z = MuGame.Random.Next(360);
                var local = subType == 1 ? Vector3.Zero : new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f);
                s.Direction = SourceJointMath.Rotate(local, s.Angle);
                s.Gravity = MuGame.Random.Next(16) + 8;
                return;
            }
        }

        protected override void OnTick(float factor)
        {
            if (_modelLifeTime > 0f)
            {
                _modelFrame += 0.5f * factor;
                _modelLifeTime -= factor;
            }

            bool anyStone = false;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                anyStone = true;
                if (s.SubType == 0)
                {
                    s.Position += s.Direction * factor; // sub 0: Direction pre-rotated (ZzzEffect.cpp:11012)
                    s.Direction *= 0.9f;                // per frame
                    s.Position.Z += s.Gravity * factor;
                    s.Gravity -= 3f * factor;
                    float height = TerrainHeight(s.Position);
                    if (s.Position.Z < height)
                    {
                        s.Position.Z = height;
                        s.Gravity = -s.Gravity * 0.5f;
                        s.LifeTime -= 4f * factor;
                        s.Angle.X -= s.Scale * 128f * factor;
                    }
                    else
                    {
                        s.Angle.X -= s.Scale * 32f * factor;
                    }
                    if (FpsCheck(10, factor) && FpsCheck(1, factor))
                        SpawnFire(s.Position, s.Angle, 1 + MuGame.Random.Next(3));
                }
                else
                {
                    // sub 1: zero Direction, rises (Gravity += 0.5).
                    s.Position.Z += s.Gravity * factor;
                    s.Gravity += 0.5f * factor;
                }

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (anyStone)
                _stoneFrame += StoneVelocity * factor;

            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }

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
                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Gravity += 0.004f * factor;
                p.Scale += p.Gravity * factor;
                p.Velocity *= 0.98f; // per frame
                p.Position.Z += p.Gravity * 10f * factor;
            }

            _sparkPool.Move(factor, World?.Terrain);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            _lightAge += factor * 0.04f;
            Vector3 center = _position + new Vector3(0f, 0f, 80f);

            // Explosions: short attack (no pop), then a 0.7 s decay.
            float flash = MathF.Min(1f, _lightAge / 0.05f) * EffectLights.Flash(_lightAge, 0.7f, 1.4f);
            if (flash > 0f)
                Lights.Set(0, center, FireLightColor, 300f + 200f * flash, 2.3f * flash);
            else
                Lights.Hide(0);

            float ember = EffectLights.Envelope(_lightAge, 1.4f, 0.2f, 0.9f);
            if (ember > 0f)
                Lights.Set(1, center, EmberLightColor, 360f, 0.7f * ember * EffectLights.Flicker(_lightAge, 0.4f, 0.2f));
            else
                Lights.Hide(1);
        }

        private void SpawnFire(Vector3 position, Vector3 angle, int subType)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.SubType = subType;
                p.Position = position;
                p.Angle = angle;
                p.LifeTime = 24f;
                if (subType == 1)
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                    p.Scale = (MuGame.Random.Next(4) + 10) * 0.01f;
                }
                else
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                    p.Scale = 1f;
                }
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_modelLifeTime > 0f || _sparkPool.HasLive)
                return true;
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            for (int i = 0; i < _explosions.Length; i++) if (_explosions[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (_modelLifeTime > 0f && _model != null)
            {
                _model.SetAnimationTime(_modelFrame);
                _model.Update(LastGameTime);
                Vector3 light = (terrain?.EvaluateTerrainLight(_position.X, _position.Y) ?? Vector3.One) * 0.8f;
                _model.DrawInstance(_position, new Vector3(0f, 0f, MathHelper.ToRadians(_modelAngle)), 0.9f, light, 1f);
            }

            for (int m = 0; m < 2; m++)
            {
                SourceModelRenderer? renderer = _stoneRenderers[m];
                if (renderer == null)
                    continue;

                bool updated = false;
                for (int i = 0; i < _stones.Length; i++)
                {
                    ref Stone s = ref _stones[i];
                    if (!s.Live || s.Model != m)
                        continue;

                    if (!updated)
                    {
                        renderer.SetAnimationTime(_stoneFrame);
                        renderer.Update(LastGameTime);
                        updated = true;
                    }

                    Vector3 light = terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.One;
                    renderer.DrawInstance(s.Position,
                        new Vector3(MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z)),
                        s.Scale, light, 1f);
                }
            }

            if (_explosionTexture != null && Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var light = new Vector3(0.7f);
                for (int i = 0; i < _explosions.Length; i++)
                {
                    ref Particle p = ref _explosions[i];
                    if (!p.Live)
                        continue;
                    int frame = (int)((20f - p.LifeTime) / 2f);
                    Batch.AddSpriteRegion(p.Position, _explosionTexture.Width * p.Scale, _explosionTexture.Height * p.Scale, light, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
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
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, Vector3.One, p.Angle.X,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            _sparkPool.DrawSparks(Batch, _sparkTexture);
        }

        protected override void DisposeEffect()
        {
            _model?.Dispose();
            _stoneRenderers[0]?.Dispose();
            _stoneRenderers[1]?.Dispose();
        }
    }
}
