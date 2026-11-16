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
    /// Cometfall (AT_SKILL_BLAST = 13, masters AT_SKILL_BLAST_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:5243       SetPlayerMagic (ReceiveMagicContinue default)
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4409   2 x MODEL_SKILL_BLAST at the tile centre (terrain height)
    /// blast   ZzzEffect.cpp:1394 (init: offset, Direction (0,0,-50-rand%50), Angle (0,20,0), BITMAP_JOINT_ENERGY sub 5),
    ///         9565 (move: ground hit -> 6 stones, BITMAP_SHINY+4, BITMAP_EXPLOTION), epilogue MoveParticle(o, true)
    /// trail   ZzzEffectJoint.cpp:240 (JOINT_ENERGY sub 5: MaxTails 10, follows EyeLeft), JointLaser01.jpg
    /// </summary>
    internal sealed class CometfallEffect : SourceSkillEffectBase
    {
        private const string BlastModelPath = "Skill/Blast01.bmd";              // MODEL_SKILL_BLAST
        private const string TrailTexturePath = "Effect/JointLaser01.jpg";      // BITMAP_JOINT_ENERGY
        private const string RingTexturePath = "Effect/ring.jpg";               // BITMAP_SHINY + 4
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg";   // BITMAP_EXPLOTION (4x4)
        private const string ExplosionSound = "Sound/eExplosion.wav";           // SOUND_EXPLOTION01

        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;

        private struct Blast
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Step;
            public float Scale;
            public float LifeTime;
            public float Frame;
            public SourceTrail Trail;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
        }

        private static readonly Vector3 BlastAngle = new(0f, 20f, 0f);

        private readonly Vector3 _target;
        private readonly Blast[] _blasts = new Blast[2];
        private readonly Burst[] _rings = new Burst[2];
        private readonly Burst[] _explosions = new Burst[2];
        private readonly SourceStonePool _stones = new(12, 48);
        private SourceModelRenderer? _blastModel;
        private Texture2D? _trailTexture;
        private Texture2D? _ringTexture;
        private Texture2D? _explosionTexture;
        private Vector3 _trailRight;
        private Vector3 _trailUp;

        public CometfallEffect(WalkerObject caster, Vector3 target)
            : base(caster, 5f, ReleaseDelaySeconds, 256)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _trailTexture = await LoadTexture(TrailTexturePath);
            _ringTexture = await LoadTexture(RingTexturePath);
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _blastModel = await LoadModel(BlastModelPath, 0);
            if (_blastModel != null)
            {
                _blastModel.AnimationSpeed = 0f;
                _blastModel.UseNormalBlend();
            }
            await _stones.LoadAsync(World);
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            var ground = new Vector3(_target.X, _target.Y, TerrainHeight(_target));
            SourceJointMath.AngleBasis(BlastAngle, out _trailRight, out _, out _trailUp);
            int maxTails = SourceJointMath.ScaledMaxTails(10);

            for (int i = 0; i < _blasts.Length; i++)
            {
                ref Blast b = ref _blasts[i];
                b.Live = true;
                b.LifeTime = 30f;
                b.Scale = (MuGame.Random.Next(8) + 10) * 0.1f;
                b.Position = ground + new Vector3(
                    (MuGame.Random.Next(100) + 200) * factor,
                    (MuGame.Random.Next(100) - 50) * factor,
                    (MuGame.Random.Next(500) + 300) * factor);
                b.Step = SourceJointMath.Rotate(new Vector3(0f, 0f, -50f - MuGame.Random.Next(50)), BlastAngle);
                b.Trail = new SourceTrail(maxTails);
                b.Trail.Push(b.Position, _trailRight, _trailUp, 100f);
            }
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _blasts.Length; i++)
            {
                ref Blast b = ref _blasts[i];
                if (!b.Live)
                    continue;

                float height = TerrainHeight(b.Position);
                if (b.Position.Z < height)
                {
                    b.Position.Z = height;
                    for (int j = 0; j < 6; j++)
                        if (FpsCheck(1, factor) && FpsCheck(1, factor))
                            _stones.Spawn(b.Position, BlastAngle, Vector3.One, 0);

                    Vector3 burst = b.Position + new Vector3(0f, 0f, 80f);
                    if (FpsCheck(1, factor))
                        _rings[i] = new Burst { Live = true, Position = burst, LifeTime = 20f };
                    if (FpsCheck(1, factor))
                    {
                        _explosions[i] = new Burst { Live = true, Position = burst, LifeTime = 20f };
                        SoundController.Instance.PlayBuffer(ExplosionSound);
                    }
                    b.Live = false; // the JOINT_ENERGY trail dies with its target
                    continue;
                }

                b.Frame += ModelVelocity * factor;
                b.Position += b.Step * factor;
                b.Trail.Push(b.Position, _trailRight, _trailUp, 100f);

                b.LifeTime -= factor;
                if (b.LifeTime <= 0f)
                    b.Live = false;
            }

            for (int i = 0; i < 2; i++)
            {
                Age(ref _rings[i], factor);
                Age(ref _explosions[i], factor);
            }

            _stones.Move(factor, World?.Terrain);
        }

        private static void Age(ref Burst burst, float factor)
        {
            if (!burst.Live)
                return;
            burst.LifeTime -= factor;
            if (burst.LifeTime <= 0f)
                burst.Live = false;
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < 2; i++)
                if (_blasts[i].Live || _rings[i].Live || _explosions[i].Live) return true;
            return _stones.HasLive;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_trailTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _blasts.Length; i++)
                {
                    ref Blast b = ref _blasts[i];
                    if (b.Live)
                        Batch.AddTrail(b.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat, b.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_blastModel != null)
            {
                for (int i = 0; i < _blasts.Length; i++)
                {
                    ref Blast b = ref _blasts[i];
                    if (!b.Live)
                        continue;

                    _blastModel.SetAnimationTime(b.Frame);
                    _blastModel.Update(LastGameTime);
                    Vector3 light = terrain?.EvaluateTerrainLight(b.Position.X, b.Position.Y) ?? Vector3.One;
                    _blastModel.DrawInstance(b.Position, new Vector3(0f, MathHelper.ToRadians(BlastAngle.Y), 0f), b.Scale, light, 1f);
                }
            }

            _stones.DrawModels(LastGameTime, terrain);

            if (Batch.Begin(_ringTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < 2; i++)
                {
                    ref Burst r = ref _rings[i];
                    if (!r.Live)
                        continue;
                    // BITMAP_SHINY+4 sub 0: Scale = sin(LifeTime * 10 deg) * 3 + 2, Light = LifeTime / 5.
                    float scale = MathF.Sin(r.LifeTime * 10f * MathF.PI / 180f) * 3f + 2f;
                    Batch.AddSprite(r.Position, scale, new Vector3(r.LifeTime / 5f));
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
                    Batch.AddSpriteRegion(e.Position, _explosionTexture.Width, _explosionTexture.Height, Vector3.One, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                }
                Batch.End();
            }

            _stones.DrawFires(Batch);
        }

        protected override void DisposeEffect()
        {
            _blastModel?.Dispose();
            _stones.Dispose();
        }
    }
}
