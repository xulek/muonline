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
    /// Twister (AT_SKILL_STORM = 8), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4339   CreateEffect(MODEL_STORM, o->Position, o->Angle, Light, 0, o), SOUND_STORM
    /// storm   ZzzEffect.cpp:2077 (init: LifeTime 59, BlendMesh 0, Direction (0,-10,0), on terrain), 10454 (move),
    ///         epilogue MoveParticle(o, true), Skill/Storm01.bmd
    /// children BITMAP_SMOKE sub 3, BITMAP_JOINT_THUNDER sub 0 (10) homing on the storm, MODEL_STONE1/2 sub 2
    /// </summary>
    internal sealed class TwisterEffect : SourceSkillEffectBase
    {
        private const string StormModelPath = "Skill/Storm01.bmd";             // MODEL_STORM
        private const string SmokeTexturePath = "Effect/smoke01.jpg";          // BITMAP_SMOKE
        private const string ThunderTexturePath = "Effect/JointThunder01.jpg"; // BITMAP_JOINT_THUNDER
        private static readonly string[] StonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };
        private const string SoundPath = "Sound/sTornado.wav";                 // SOUND_STORM

        private const int SkillId = 8;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;
        private const float ThunderVelocity = 50f;

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private struct Thunder
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public float LifeTime;
            public SourceTrail Trail;
        }

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

        private readonly Smoke[] _smokes = new Smoke[48];
        private readonly Thunder[] _thunders = new Thunder[16];
        private readonly Stone[] _stones = new Stone[32];
        private readonly SourceModelRenderer?[] _stoneRenderers = new SourceModelRenderer?[2];
        private SourceModelRenderer? _stormModel;
        private Texture2D? _smokeTexture;
        private Texture2D? _thunderTexture;

        private bool _stormLive;
        private Vector3 _stormPosition;
        private Vector3 _stormAngle;
        private Vector3 _stormStep;
        private float _stormLifeTime;
        private float _stormFrame;
        private float _stoneFrame;

        public TwisterEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 1024)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _thunderTexture = await LoadTexture(ThunderTexturePath);
            _stormModel = await LoadModel(StormModelPath, 0);
            if (_stormModel != null)
            {
                _stormModel.AnimationSpeed = 0f;
                _stormModel.UseNormalBlend();
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
            _stormLive = true;
            _stormAngle = new Vector3(0f, 0f, CasterYawDegrees);
            _stormPosition = CasterPosition;
            _stormPosition.Z = TerrainHeight(_stormPosition);
            _stormStep = SourceJointMath.Rotate(new Vector3(0f, -10f, 0f), _stormAngle);
            _stormLifeTime = 59f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_stormLive)
            {
                if (FpsCheck(1, factor))
                    SpawnSmoke(_stormPosition);

                var thunderAngle = new Vector3(90f, 0f, _stormAngle.Z);
                if (FpsCheck(2, factor) && FpsCheck(1, factor))
                    SpawnThunder(_stormPosition + new Vector3(-200f, 0f, 700f), thunderAngle);
                if (FpsCheck(2, factor) && FpsCheck(1, factor))
                    SpawnThunder(_stormPosition + new Vector3(200f, 0f, 700f), thunderAngle);

                _stormPosition.Z = TerrainHeight(_stormPosition);
                if (FpsCheck(4, factor) && FpsCheck(1, factor))
                    SpawnStone(_stormPosition);

                _stormFrame += ModelVelocity * factor;
                _stormPosition += _stormStep * factor;
                _stormLifeTime -= factor;
                if (_stormLifeTime <= 0f)
                    _stormLive = false;
            }

            MoveSmokes(factor);
            MoveThunders(factor);
            MoveStones(factor);
        }

        // ------------------------------------------------------------------ BITMAP_SMOKE sub 3

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.LifeTime = 10f;
                p.Light = Vector3.One;
                p.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                p.Angle = new Vector3(MuGame.Random.Next(90) - 45, 0f, MuGame.Random.Next(360));
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                float luminosity = p.LifeTime / 8f;
                p.Light = new Vector3(luminosity * 0.8f, luminosity * 0.8f, luminosity);
                p.Velocity *= 0.4f; // per frame
                p.Scale += factor * 0.1f;
            }
        }

        // ------------------------------------------------------------------ BITMAP_JOINT_THUNDER sub 0 (10)

        private void SpawnThunder(Vector3 position, Vector3 angle)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live)
                    continue;

                t.Live = true;
                t.Position = position;
                t.Angle = angle;
                t.LifeTime = 2f;
                int maxTails = SourceJointMath.ScaledMaxTails(50);
                if (t.Trail == null || t.Trail.MaxTails != maxTails)
                    t.Trail = new SourceTrail(maxTails);
                else
                    t.Trail.Clear();
                t.Trail.Push(position, angle, 10f);
                return;
            }
        }

        private void MoveThunders(float factor)
        {
            Vector3 target = _stormPosition + new Vector3(0f, 0f, 80f);
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (!t.Live)
                    continue;

                SourceJointMath.AngleBasis(t.Angle, out Vector3 endRight, out Vector3 endForward, out Vector3 endUp);
                t.Position += -endForward * (ThunderVelocity * factor);

                int steps = t.Trail.MaxTails;
                for (int j = 0; j < steps; j++)
                {
                    float distance = SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, target, 50f, factor);
                    var angle = new Vector3(
                        t.Angle.X + (MuGame.Random.Next(1024) - 512) / 10f * factor,
                        t.Angle.Y,
                        t.Angle.Z + (MuGame.Random.Next(1024) - 512) / 10f * factor);
                    SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                    t.Trail.Push(t.Position, right, up, 10f);

                    if (distance < ThunderVelocity * 1.5f)
                        break;

                    t.Position += -forward * (ThunderVelocity * factor * factor);
                }

                t.Trail.Push(t.Position, endRight, endUp, 10f);
                t.LifeTime -= factor;
                if (t.LifeTime < 0f)
                    t.Live = false;
            }
        }

        // ------------------------------------------------------------------ MODEL_STONE1/2 sub 2

        private void SpawnStone(Vector3 position)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                if (!AnyStone())
                    _stoneFrame = 0f;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.Position = position;
                s.Angle = _stormAngle;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Angle.Z = MuGame.Random.Next(360);
                s.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f), s.Angle);
                s.Gravity = MuGame.Random.Next(16) + 8;
                return;
            }
        }

        private bool AnyStone()
        {
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            return false;
        }

        private void MoveStones(float factor)
        {
            bool any = false;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                any = true;
                // sub 2 is not 0 / 10 / 12: the pre-rotated Direction is rotated by Angle again (ZzzEffect.cpp:11028).
                s.Position += SourceJointMath.Rotate(s.Direction, s.Angle) * factor;
                s.Direction *= 0.9f; // per frame
                s.Position.Z += s.Gravity * factor;
                s.Angle.Z += 20f * factor;
                s.Gravity += 0.5f * factor;

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (any)
                _stoneFrame += ModelVelocity * factor;
        }

        // ------------------------------------------------------------------ lifecycle / draw

        protected override bool HasActiveContent()
        {
            if (_stormLive || AnyStone())
                return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            for (int i = 0; i < _thunders.Length; i++) if (_thunders[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
                float pow2 = MathF.Pow(2f, SourceJointMath.FrameFactor);
                for (int i = 0; i < _thunders.Length; i++)
                {
                    ref Thunder t = ref _thunders[i];
                    if (t.Live)
                        Batch.AddTrail(t.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat,
                            (t.Trail.MaxTails - 1) / pow2, scroll, false, 2f * scroll);
                }
                Batch.End();
            }

            if (_stormLive && _stormModel != null)
            {
                float lt = _stormLifeTime + SourceJointMath.FrameFactor;
                _stormModel.ScrollU = MathF.Round(-lt * 0.1f * 50f) / 50f;
                _stormModel.SetAnimationTime(_stormFrame);
                _stormModel.Update(LastGameTime);
                Vector3 light = terrain?.EvaluateTerrainLight(_stormPosition.X, _stormPosition.Y) ?? Vector3.One;
                _stormModel.DrawInstance(_stormPosition, new Vector3(0f, 0f, MathHelper.ToRadians(_stormAngle.Z)), 0.9f, light,
                    MathF.Max(MathF.Round(lt * 0.1f * 50f) / 50f, 0.003f));
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
                    renderer.DrawInstance(s.Position, new Vector3(0f, 0f, MathHelper.ToRadians(s.Angle.Z)), s.Scale, light, 1f);
                }
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _stormModel?.Dispose();
            _stoneRenderers[0]?.Dispose();
            _stoneRenderers[1]?.Dispose();
        }
    }
}
