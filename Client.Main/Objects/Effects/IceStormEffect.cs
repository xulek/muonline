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
    /// Ice Storm (AT_SKILL_BLAST_FREEZE = 39, masters AT_SKILL_ICE_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4848       SetPlayerMagic
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4303   10 x MODEL_BLIZZARD sub 0 (z + rand % 50 * i, PKKey i + 1), SOUND_SUDDEN_ICE1
    /// blizzard ZzzEffect.cpp:3200 (init), 12745 (move: smoke 0, energy 1 / fire+2 7, sprites, impact -> smoke 11,
    ///          MODEL_ICE_SMALL, MODEL_BLIZZARD sub 1 (BlendMeshLight / 1.1^F, SOUND_SUDDEN_ICE2 for PKKey 1)), Skill/blizzard.bmd
    /// </summary>
    internal sealed class IceStormEffect : SourceSkillEffectBase
    {
        private const string BlizzardModelPath = "Skill/blizzard.bmd";  // MODEL_BLIZZARD
        private const string ShardModelPath = "Skill/Ice02.bmd";        // MODEL_ICE_SMALL
        private const string SmokeTexturePath = "Effect/smoke01.jpg";   // BITMAP_SMOKE
        private const string EnergyTexturePath = "Effect/Thunder01.jpg";// BITMAP_ENERGY
        private const string FireTexturePath = "Effect/Fire03.jpg";     // BITMAP_FIRE + 2 (4x1)
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";   // BITMAP_SHINY + 1
        private const string LightTexturePath = "Effect/flare01.jpg";   // BITMAP_LIGHT
        private const string Sound1 = "Sound/eSuddenIce_1.wav";
        private const string Sound2 = "Sound/eSuddenIce_2.wav";

        private const int SkillId = 39;
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private enum Kind { Smoke0, Smoke11, Energy1, Fire7 }

        private struct Flake
        {
            public bool Live;
            public int PKKey;
            public Vector3 Position;
            public Vector3 Start;
            public float Gravity;
            public float Light;
            public float LifeTime;
        }

        private struct Crystal
        {
            public bool Live;
            public bool PlaySound;
            public Vector3 Position;
            public float Light;
            public float BlendMeshLight;
            public float LifeTime;
        }

        private struct Shard
        {
            public bool Live;
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
            public Kind Kind;
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

        private readonly Vector3 _target;
        private readonly Flake[] _flakes = new Flake[10];
        private readonly Crystal[] _crystals = new Crystal[10];
        private readonly Shard[] _shards = new Shard[10];
        private readonly Particle[] _particles = new Particle[512];
        private SourceModelRenderer? _blizzardModel;
        private SourceModelRenderer? _shardModel;
        private Texture2D? _smokeTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _fireTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _lightTexture;
        private float _angleZ;
        private Vector3 _lightCenter;

        private static readonly Vector3 IceGlowColor = new(0.4f, 0.7f, 1f);

        public IceStormEffect(WalkerObject caster, Vector3 target)
            : base(caster, 5f, ReleaseDelaySeconds, 1024)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _energyTexture = await LoadTexture(EnergyTexturePath);
            _fireTexture = await LoadTexture(FireTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _blizzardModel = await LoadModel(BlizzardModelPath, -2);
            if (_blizzardModel != null)
                _blizzardModel.AnimationSpeed = 0f;
            _shardModel = await LoadModel(ShardModelPath, -1);
            if (_shardModel != null)
            {
                _shardModel.AnimationSpeed = 0f;
                _shardModel.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            _angleZ = CasterYawDegrees;
            var ground = new Vector3(_target.X, _target.Y, TerrainHeight(_target));
            _lightCenter = ground;

            for (int i = 0; i < _flakes.Length; i++)
            {
                ref Flake f = ref _flakes[i];
                f.Live = true;
                f.PKKey = i + 1;
                f.LifeTime = MuGame.Random.Next(15) + 15;
                f.Gravity = -20f - (MuGame.Random.Next(30) + 10) * factor;
                f.Light = 0f;
                f.Position = new Vector3(
                    ground.X + MuGame.Random.Next(300) - 150 + 100f * factor,
                    ground.Y + MuGame.Random.Next(300) - 150,
                    ground.Z + MuGame.Random.Next(50) * i + 600f);
                f.Start = f.Position;
            }

            PlayUnmappedSound(SkillId, Sound1);
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _flakes.Length; i++)
            {
                ref Flake f = ref _flakes[i];
                if (!f.Live)
                    continue;

                f.Position.X = f.Start.X + MathF.Sin(MuGame.Random.Next(1000) * 0.01f) * 10f;
                f.Position.Y = f.Start.Y + MathF.Sin(MuGame.Random.Next(1000) * 0.01f) * 10f;
                f.Position.Z += f.Gravity * factor;
                f.Gravity -= MuGame.Random.Next(5) * factor;
                f.Start.X -= 10f * factor;

                if (FpsCheck(1, factor))
                    Spawn(Kind.Smoke0, f.Position, Vector3.One, 1.5f);
                if (FpsCheck(2, factor))
                {
                    if (FpsCheck(1, factor)) Spawn(Kind.Energy1, f.Position, Vector3.One, 0.5f);
                }
                else if (FpsCheck(1, factor))
                {
                    Spawn(Kind.Fire7, f.Position, Vector3.One, 0.5f);
                }

                f.Light += 0.1f * factor;

                float height = TerrainHeight(f.Position);
                if (f.Position.Z < height)
                {
                    Vector3 pos = f.Position + new Vector3(0f, 0f, 50f);
                    if (FpsCheck(1, factor))
                        Spawn(Kind.Smoke11, pos, new Vector3(0.24f, 0.28f, 0.8f), (MuGame.Random.Next(32) + 80) * 0.025f);
                    if (FpsCheck(5, factor) && FpsCheck(1, factor))
                        SpawnShard(pos);
                    if (FpsCheck(1, factor))
                        _crystals[i] = new Crystal { Live = true, Position = pos, Light = f.Light, BlendMeshLight = 1f, LifeTime = 20f, PlaySound = f.PKKey == 1 };
                    f.Live = false;
                    continue;
                }

                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                    f.Live = false;
            }

            float crystalDecay = MathF.Pow(1f / 1.1f, factor);
            for (int i = 0; i < _crystals.Length; i++)
            {
                ref Crystal c = ref _crystals[i];
                if (!c.Live)
                    continue;

                c.BlendMeshLight *= crystalDecay;
                if ((int)c.LifeTime == 18 && c.PlaySound)
                {
                    SoundController.Instance.PlayBuffer(Sound2);
                    c.PlaySound = false;
                }
                c.LifeTime -= factor;
                if (c.LifeTime <= 0f)
                    c.Live = false;
            }

            MoveShards(factor);
            MoveParticles(factor);
            UpdateLights();
        }

        private void UpdateLights()
        {
            // Falling blizzard: cold glow following the live flakes (kept low enough to reach the ground).
            int flakes = 0;
            Vector3 sum = Vector3.Zero;
            float flakeLight = 0f;
            for (int i = 0; i < _flakes.Length; i++)
            {
                if (!_flakes[i].Live)
                    continue;
                flakes++;
                sum += _flakes[i].Position;
                flakeLight += _flakes[i].Light;
            }
            if (flakes > 0)
            {
                Vector3 center = sum / flakes;
                center.Z = MathF.Min(center.Z, _lightCenter.Z + 250f);
                float glow = MathHelper.Clamp(flakeLight / flakes, 0f, 1f) * (flakes / (float)_flakes.Length);
                Lights.Set(0, center, IceGlowColor, 320f, 1.1f * glow);
            }
            else
            {
                Lights.Hide(0);
            }

            // Impact: bright flash at the target while the crystals burst, decaying with them.
            float impact = 0f;
            for (int i = 0; i < _crystals.Length; i++)
            {
                ref Crystal c = ref _crystals[i];
                if (c.Live)
                    impact = MathF.Max(impact, c.BlendMeshLight * MathHelper.Clamp(c.LifeTime / 20f, 0f, 1f));
            }
            Lights.Set(1, _lightCenter + new Vector3(0f, 0f, 60f), IceGlowColor, 300f + 120f * impact, 2f * impact);
        }

        private void SpawnShard(Vector3 position)
        {
            for (int i = 0; i < _shards.Length; i++)
            {
                ref Shard s = ref _shards[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Position = position;
                s.Angle = new Vector3(0f, 0f, MuGame.Random.Next(360));
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f), s.Angle);
                s.Gravity = MuGame.Random.Next(16) + 8;
                return;
            }
        }

        private void MoveShards(float factor)
        {
            for (int i = 0; i < _shards.Length; i++)
            {
                ref Shard s = ref _shards[i];
                if (!s.Live)
                    continue;

                s.Position += s.Direction * factor;
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
                    Spawn(Kind.Smoke0, s.Position, Vector3.One, 1f);

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
        }

        private void Spawn(Kind kind, Vector3 position, Vector3 light, float scale)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;

                const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
                p = default;
                p.Live = true;
                p.Kind = kind;
                p.Position = position;
                p.Light = light;
                p.BaseLight = light;
                p.Angle = new Vector3(0f, 0f, _angleZ);
                switch (kind)
                {
                    case Kind.Smoke0:
                        p.LifeTime = 20f;
                        p.Scale = (MuGame.Random.Next(64) + 64) * 0.01f;
                        p.Rotation = MuGame.Random.Next(360);
                        p.Gravity = (MuGame.Random.Next(32) + 60) * 0.1f;
                        break;
                    case Kind.Smoke11:
                        p.LifeTime = 50f;
                        p.Scale = scale;
                        p.Position += new Vector3((MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) + 32) * factor);
                        p.Angle = new Vector3(MuGame.Random.Next(90) - 45, 0f, MuGame.Random.Next(360));
                        p.Rotation = MuGame.Random.Next(360);
                        p.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                        break;
                    case Kind.Energy1:
                        p.LifeTime = 10f;
                        p.Scale = scale * (MuGame.Random.Next(8) + 6) * 0.1f;
                        p.Rotation = MuGame.Random.Next(360);
                        p.Gravity = 20f;
                        p.Light = new Vector3(0.5f);
                        break;
                    case Kind.Fire7:
                        p.LifeTime = 24f;
                        p.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                        p.Scale = (MuGame.Random.Next(64) + 128) * 0.008f + scale;
                        p.Rotation = MuGame.Random.Next(360);
                        break;
                }
                return;
            }
        }

        private void MoveParticles(float factor)
        {
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

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                switch (p.Kind)
                {
                    case Kind.Smoke0:
                        p.Light = new Vector3(p.LifeTime / 8f);
                        p.Gravity += 0.2f * factor;
                        p.Position.Z += p.Gravity * factor;
                        p.Scale += factor * 0.05f;
                        break;
                    case Kind.Smoke11:
                        p.Light = p.BaseLight * (p.LifeTime / 50f);
                        p.Velocity *= 0.4f; // per frame
                        p.Scale += factor * 0.05f;
                        break;
                    case Kind.Energy1:
                        p.Rotation += p.Gravity * factor;
                        p.Light = new Vector3(p.LifeTime / 15f);
                        p.Scale += factor * 0.1f;
                        break;
                    case Kind.Fire7:
                        p.Gravity += 0.004f * factor;
                        p.Scale -= 0.04f * factor;
                        p.Position.Z += p.Gravity * 10f * factor;
                        break;
                }
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < 10; i++)
                if (_flakes[i].Live || _crystals[i].Live || _shards[i].Live) return true;
            for (int i = 0; i < _particles.Length; i++) if (_particles[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;
            var angle = new Vector3(0f, 0f, MathHelper.ToRadians(_angleZ));

            if (_blizzardModel != null)
            {
                for (int i = 0; i < _flakes.Length; i++)
                {
                    ref Flake f = ref _flakes[i];
                    if (f.Live)
                        _blizzardModel.DrawInstance(f.Position, angle, 0.5f, new Vector3(f.Light), 1f);
                }
                for (int i = 0; i < _crystals.Length; i++)
                {
                    ref Crystal c = ref _crystals[i];
                    if (c.Live)
                        _blizzardModel.DrawInstance(c.Position, angle, 0.9f, new Vector3(c.Light),
                            MathF.Max(MathF.Round(c.BlendMeshLight * 50f) / 50f, 0.003f));
                }
            }

            if (_shardModel != null)
            {
                for (int i = 0; i < _shards.Length; i++)
                {
                    ref Shard s = ref _shards[i];
                    if (!s.Live)
                        continue;
                    Vector3 light = terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.One;
                    _shardModel.DrawInstance(s.Position,
                        new Vector3(MathHelper.ToRadians(s.Angle.X), 0f, MathHelper.ToRadians(s.Angle.Z)), s.Scale, light, 1f);
                }
            }

            // One-frame sprites of the falling blizzards.
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flakes.Length; i++)
                    if (_flakes[i].Live)
                        Batch.AddSprite(_flakes[i].Position, (MuGame.Random.Next(4) + 4) * 0.2f, new Vector3(_flakes[i].Light), MuGame.Random.Next(360));
                Batch.End();
            }
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flakes.Length; i++)
                    if (_flakes[i].Live)
                        Batch.AddSprite(_flakes[i].Position, 1f, new Vector3(_flakes[i].Light), MuGame.Random.Next(360));
                Batch.End();
            }

            DrawParticles(_smokeTexture, Kind.Smoke0, Kind.Smoke11);
            DrawParticles(_energyTexture, Kind.Energy1, Kind.Energy1);

            if (_fireTexture != null && Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (!p.Live || p.Kind != Kind.Fire7)
                        continue;
                    int frame = (int)((23f - p.LifeTime) / 6f);
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, p.Rotation,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }
        }

        private void DrawParticles(Texture2D? texture, Kind a, Kind b)
        {
            if (!Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live && (p.Kind == a || p.Kind == b))
                    Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            _blizzardModel?.Dispose();
            _shardModel?.Dispose();
        }
    }
}
