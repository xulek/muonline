#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Fire Ball (AT_SKILL_FIREBALL = 4, MODEL_FIRE sub 1) and Meteorite (AT_SKILL_METEO = 2, MODEL_FIRE sub 0), SourceMain5.2:
    /// meteor  ZzzCharacter.cpp:4871   CreateEffect(MODEL_FIRE, to->Position, to->Angle, o->Light) sub 0, SOUND_METEORITE01;
    ///         init ZzzEffect.cpp:2645 (X += 130 + rand % 32, Z += 400, Direction (0,0,-50), Angle (0,20,0)),
    ///         move 12250/12320 (ground hit: 6 x MODEL_STONE1/2, BITMAP_EXPLOTION, dies), BlendMeshLight (rand%4+4)*0.1
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4895   CreateEffect(MODEL_FIRE, o->Position, (0, 0, angle to target), o->Light, 1, to), SOUND_METEORITE01
    /// fire    ZzzEffect.cpp:2736 (init default), 12250/12440 (move: BlendMeshLight 0, BITMAP_FIRE sub 5, CheckTargetRange),
    ///         epilogue MoveParticle(o, true); impact CheckTargetRange 278: 2 x MODEL_STONE1/2 sub 0
    /// stone   ZzzEffect.cpp:2882 (init), 11028 (move, BITMAP_FIRE 1..3); particles BITMAP_FIRE 398/4570/9101
    /// </summary>
    internal sealed class FireBallEffect : SourceSkillEffectBase
    {
        private const string FireModelPath = "Skill/Fire01.bmd";     // MODEL_FIRE
        private const string FireTexturePath = "Effect/Fire01.jpg";  // BITMAP_FIRE (4x1)
        private const string SoundPath = "Sound/eMeteorite.wav";     // SOUND_METEORITE01
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg"; // BITMAP_EXPLOTION (4x4)
        private const string ExplosionSound = "Sound/eExplosion.wav";          // SOUND_EXPLOTION01
        private static readonly string[] StonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };

        private readonly int _skillId;
        private readonly bool _meteor;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;

        private struct Stone
        {
            public bool Live;
            public int Model;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private struct Fire
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private readonly WalkerObject? _target;
        private readonly Stone[] _stones = new Stone[8];
        private readonly Fire[] _fires = new Fire[96];
        private readonly SourceModelRenderer?[] _stoneRenderers = new SourceModelRenderer?[2];
        private SourceModelRenderer? _fireModel;
        private Texture2D? _fireTexture;
        private Texture2D? _explosionTexture;
        private bool _explosionLive;
        private Vector3 _explosionPosition;
        private float _explosionLifeTime;
        private float _ballBlendMeshLight;

        private bool _ballLive;
        private Vector3 _ballPosition;
        private Vector3 _ballAngle;
        private Vector3 _ballStep;
        private float _ballLifeTime;
        private float _ballFrame;
        private float _ballScale;
        private float _stoneFrame;

        public FireBallEffect(WalkerObject caster, WalkerObject? target, bool meteor = false)
            : base(caster, 5f, ReleaseDelaySeconds, 256)
        {
            _target = target;
            _meteor = meteor;
            _skillId = meteor ? 2 : 4;
        }

        protected override async Task LoadEffectContent()
        {
            _fireTexture = await LoadTexture(FireTexturePath);
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _fireModel = await LoadModel(FireModelPath, 1);
            if (_fireModel != null)
            {
                _fireModel.AnimationSpeed = 0f;
                _fireModel.UseNormalBlend();
            }

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
            Vector3 origin = CasterPosition;
            float yaw = IsAlive(_target)
                ? SourceJointMath.CreateAngle2D(origin, _target!.WorldPosition.Translation)
                : CasterYawDegrees;

            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            _ballLive = true;
            if (_meteor)
            {
                if (!IsAlive(_target))
                {
                    _ballLive = false;
                    return;
                }

                // MODEL_FIRE sub 0 at to->Position; Angle (0, 20, 0) replaces the target angle.
                _ballAngle = new Vector3(0f, 20f, 0f);
                _ballStep = SourceJointMath.Rotate(new Vector3(0f, 0f, -50f), _ballAngle);
                _ballPosition = _target!.WorldPosition.Translation
                    + new Vector3((130f + MuGame.Random.Next(32)) * factor, 0f, 400f * factor);
                _ballLifeTime = 40f;
                _ballScale = (MuGame.Random.Next(8) + 10) * 0.1f;
            }
            else
            {
                _ballAngle = new Vector3(0f, 0f, yaw);
                _ballStep = SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), _ballAngle);
                _ballPosition = origin + new Vector3(0f, 0f, 120f * factor);
                _ballLifeTime = 60f;
                _ballScale = (MuGame.Random.Next(4) + 8) * 0.1f;
            }
            PlayUnmappedSound(_skillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_ballLive)
            {
                // MoveEffect prologue: Luminosity fades over the last 5 ticks.
                float luminosity = (MuGame.Random.Next(4) + 7) * 0.1f;
                if (_ballLifeTime < 5f)
                    luminosity = MathF.Max(0f, luminosity - (5f - _ballLifeTime) * 0.2f);
                if (_meteor)
                {
                    float height = TerrainHeight(_ballPosition);
                    if (_ballPosition.Z < height)
                    {
                        _ballPosition.Z = height;
                        for (int j = 0; j < 6; j++)
                            if (FpsCheck(1, factor))
                                SpawnStone(_ballPosition, _ballAngle);
                        if (FpsCheck(1, factor))
                        {
                            _explosionLive = true;
                            _explosionLifeTime = 20f;
                            _explosionPosition = _ballPosition + new Vector3(0f, 0f, 80f);
                            Client.Main.Controllers.SoundController.Instance.PlayBuffer(ExplosionSound);
                        }
                        _ballLive = false;
                        goto particles;
                    }
                    _ballBlendMeshLight = (MuGame.Random.Next(4) + 4) * 0.1f;
                }

                if (FpsCheck(1, factor))
                    SpawnFire(5, _ballPosition, _ballAngle, new Vector3(luminosity, luminosity * 0.1f, 0f));

                if (!_meteor && IsAlive(_target) && _target!.Visible)
                {
                    Vector3 t = _target.WorldPosition.Translation;
                    float dx = _ballPosition.X - t.X;
                    float dy = _ballPosition.Y - t.Y;
                    if (MathF.Sqrt(dx * dx + dy * dy) <= 100f)
                    {
                        _ballLifeTime = 1f;
                        for (int j = 0; j < 2; j++)
                            if (FpsCheck(1, factor))
                                SpawnStone(_ballPosition, _ballAngle);
                    }
                }

                _ballFrame += ModelVelocity * factor;
                _ballPosition += _ballStep * factor;
                _ballLifeTime -= factor;
                if (_ballLifeTime <= 0f)
                    _ballLive = false;
            }

        particles:
            if (_explosionLive)
            {
                _explosionLifeTime -= factor;
                if (_explosionLifeTime <= 0f)
                    _explosionLive = false;
            }

            bool anyStone = false;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                anyStone = true;
                s.Position += s.Direction * factor; // sub 0: Direction was rotated at init (ZzzEffect.cpp:11012)
                s.Direction *= 0.9f; // per frame
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
                    SpawnFire(1 + MuGame.Random.Next(3), s.Position, s.Angle, Vector3.One);

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (anyStone)
                _stoneFrame += ModelVelocity * factor;

            MoveFires(factor);
        }

        private void SpawnStone(Vector3 position, Vector3 angle)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.Position = position;
                s.Angle = angle;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Angle.Z = MuGame.Random.Next(360);
                s.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f), s.Angle);
                s.Gravity = MuGame.Random.Next(16) + 8;
                return;
            }
        }

        private void SpawnFire(int subType, Vector3 position, Vector3 angle, Vector3 light)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.SubType = subType;
                p.Position = position;
                p.Angle = angle;
                p.Light = light;
                p.LifeTime = 24f;
                p.Rotation = MuGame.Random.Next(360);
                switch (subType)
                {
                    case 1:
                        p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                        p.Scale = (MuGame.Random.Next(4) + 10) * 0.01f;
                        break;
                    case 5:
                        p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                        p.Scale = (MuGame.Random.Next(64) + 128) * 0.01f;
                        break;
                    default:
                        p.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                        p.Scale = 1f;
                        break;
                }
                return;
            }
        }

        private void MoveFires(float factor)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire p = ref _fires[i];
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
                if (p.SubType == 5)
                {
                    p.Scale -= 0.04f * factor;
                    p.Rotation += 5f * factor;
                }
                else
                {
                    p.Scale += p.Gravity * factor;
                    p.Velocity *= 0.98f; // per frame
                }
                p.Position.Z += p.Gravity * 10f * factor;
            }
        }

        private static bool IsAlive(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        protected override bool HasActiveContent()
        {
            if (_ballLive || _explosionLive)
                return true;
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (_ballLive && _fireModel != null)
            {
                _fireModel.SetAnimationTime(_ballFrame);
                _fireModel.Update(LastGameTime);
                Vector3 light = terrain?.EvaluateTerrainLight(_ballPosition.X, _ballPosition.Y) ?? Vector3.One;
                // BlendMeshLight 0: mesh 1 contributes nothing (additive with black).
                Vector3 angle = new(0f, MathHelper.ToRadians(_ballAngle.Y), MathHelper.ToRadians(_ballAngle.Z));
                _fireModel.DrawInstance(_ballPosition, angle, _ballScale, light, _meteor ? MathF.Max(_ballBlendMeshLight, 0.003f) : 0.003f);
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
                    renderer.DrawInstance(s.Position, new Vector3(MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z)),
                        s.Scale, light, 1f);
                }
            }

            if (_explosionLive && _explosionTexture != null && Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                int frame = (int)((20f - _explosionLifeTime) / 2f);
                Batch.AddSpriteRegion(_explosionPosition, _explosionTexture.Width, _explosionTexture.Height, Vector3.One, 0f,
                    frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                Batch.End();
            }

            if (_fireTexture != null && Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Fire p = ref _fires[i];
                    if (!p.Live)
                        continue;

                    int frame = (int)((23f - p.LifeTime) / 6f);
                    // sub 5: Rotation; sub 1..3 (default branch): o->Angle[0].
                    float rotation = p.SubType == 5 ? p.Rotation : p.Angle.X;
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, rotation,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _fireModel?.Dispose();
            _stoneRenderers[0]?.Dispose();
            _stoneRenderers[1]?.Dispose();
        }
    }
}
