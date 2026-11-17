#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Impale (AT_SKILL_SPEAR = 47), SourceMain5.2:
    /// packet  WSclient.cpp:4055       PLAYER_ATTACK_SKILL_SPEAR, AttackTime = 1
    /// stage   ZzzCharacter.cpp:2522   AttackTime 4 MODEL__SPEAR, 8 MODEL_SPEAR x2, 10 SOUND_RIDINGSPEAR, 13-14 MODEL_SPEARSKILL x3
    /// gather  ZzzEffect.cpp:646/7400  BITMAP_JOINT_HEALING sub 6 (ZzzEffectJoint.cpp:452, 3584) + BITMAP_SHINY+1 sprites
    /// cone    ZzzEffect.cpp:642/7387  BITMAP_FLARE sub 4 (ZzzEffectJoint.cpp:1866, 5520)
    /// spear   ZzzEffect.cpp:650, epilogue MoveParticle, 18027 RenderSkillSpear (RidingSpear01, RENDER_BRIGHT)
    /// </summary>
    internal sealed class ImpaleEffect : SourceSkillEffectBase
    {
        private const string HealingTexturePath = "Effect/JointEnergy01.jpg"; // BITMAP_JOINT_HEALING
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";         // BITMAP_SHINY + 1
        private const string FlareTexturePath = "Effect/Flare.jpg";           // BITMAP_FLARE
        private const string SpearModelPath = "Skill/RidingSpear01.bmd";      // MODEL_SPEARSKILL
        private const string SoundPath = "Sound/eRidingSpear.wav";            // SOUND_RIDINGSPEAR

        private const int SkillId = 47;
        private const int WeaponBone = 33;
        private const float LimitAttackTime = 15f;
        private const float ModelVelocity = 0.3f;

        private struct Emitter
        {
            public bool Live;
            public Vector3 Position;
            public float Yaw;
            public float LifeTime;
        }

        private struct Healing
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TargetPosition;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Flare
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TargetPosition;
            public Vector3 Right;
            public Vector3 Up;
            public float SinYaw;
            public float CosYaw;
            public float Direction0;
            public float Direction1;
            public float Direction2;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Spear
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Direction;
            public float Yaw;
            public float LifeTime;
        }

        private readonly Emitter[] _gatherers = new Emitter[4];   // MODEL__SPEAR
        private readonly Emitter[] _cones = new Emitter[8];       // MODEL_SPEAR sub 0
        private readonly Healing[] _healings = new Healing[48];
        private readonly Flare[] _flares = new Flare[64];
        private readonly Spear[] _spears = new Spear[32];

        private Texture2D? _healingTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _flareTexture;
        private SourceModelRenderer? _spearModel;

        private float _attackTime = 1f;
        private int _lastAttackEffectTime = -1;
        private bool _stageActive;
        private float _spearFrame;
        private bool _stageLights;
        private float _lightAge;
        private float _thrustAge = -1f;
        private Vector3 _thrustPos;
        private static readonly Vector3 GatherGlow = new(1f, 1f, 0.5f);
        private static readonly Vector3 ThrustGlow = new(0.85f, 0.9f, 1f);

        public ImpaleEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 1024)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _healingTexture = await LoadTexture(HealingTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
            _spearModel = await LoadModel(SpearModelPath, -2);
            if (_spearModel != null)
                _spearModel.AnimationSpeed = 0f;
        }

        protected override void OnStart()
        {
            // AttackStage breaks out for a missing weapon link bone; only players carry bone 33.
            _stageActive = Caster is PlayerObject;
            _stageLights = _stageActive;
        }

        protected override void OnTick(float factor)
        {
            if (_stageActive)
                TickAttackStage(factor);

            for (int i = 0; i < _gatherers.Length; i++)
            {
                ref Emitter e = ref _gatherers[i];
                if (!e.Live)
                    continue;

                for (int j = 0; j < 3; j++)
                {
                    if (!FpsCheck(1, factor))
                        continue;
                    var angle = new Vector3(MuGame.Random.Next(90), 0f, MuGame.Random.Next(360));
                    Vector3 start = e.Position - SourceJointMath.Rotate(new Vector3(0f, -100f, 0f), angle);
                    SpawnHealing(start, e.Position, angle);
                }

                e.LifeTime -= factor;
                if (e.LifeTime <= 0f)
                    e.Live = false;
            }

            for (int i = 0; i < _cones.Length; i++)
            {
                ref Emitter e = ref _cones[i];
                if (!e.Live)
                    continue;

                if (FpsCheck(1, factor))
                    SpawnFlare(e.Position, e.Yaw);

                e.LifeTime -= factor;
                if (e.LifeTime <= 0f)
                    e.Live = false;
            }

            bool anySpear = false;
            for (int i = 0; i < _spears.Length; i++)
            {
                ref Spear s = ref _spears[i];
                if (!s.Live)
                    continue;

                anySpear = true;
                s.Position += s.Direction * factor;
                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (anySpear)
                _spearFrame += ModelVelocity * factor;

            MoveHealings(factor);
            MoveFlares(factor);

            // Dynamic lights: yellow energy gathering on the weapon, then a flash travelling with the spear thrust.
            _lightAge += factor;
            float gather = _stageLights && _lastAttackEffectTime >= 4 ? 0.9f * EffectLights.Envelope(_lightAge - 3f, 8f, 2f, 3f) : 0f;
            Lights.Set(0, _gatherers[0].Position, GatherGlow, 180f, gather);

            ref Spear lead = ref _spears[0];
            if (lead.Live)
            {
                if (_thrustAge < 0f)
                    _thrustAge = 0f;
                _thrustPos = lead.Position;
            }
            if (_thrustAge >= 0f)
            {
                _thrustAge += factor;
                Lights.Set(1, _thrustPos, ThrustGlow, 320f,
                    1.6f * MathF.Min(1f, _thrustAge / 1.5f) * EffectLights.Flash(_thrustAge, 18f, 1.4f));
            }
        }

        // ------------------------------------------------------------------ AttackStage

        private void TickAttackStage(float factor)
        {
            float yaw = CasterYawDegrees;
            float rad = MathHelper.ToRadians(yaw);
            float sin = MathF.Sin(rad);
            float cos = MathF.Cos(rad);
            int attackTime = (int)_attackTime;

            if (attackTime == 10 && _lastAttackEffectTime != 10)
            {
                PlayUnmappedSound(SkillId, SoundPath);
                _lastAttackEffectTime = 10;
            }
            else if (attackTime == 4 && _lastAttackEffectTime != 4)
            {
                SpawnEmitter(_gatherers, GetCasterBonePosition(WeaponBone), yaw, 5f);
                _lastAttackEffectTime = 4;
            }
            else if (attackTime == 8 && _lastAttackEffectTime != 8)
            {
                Vector3 position = CasterPosition + new Vector3(50f * sin, -50f * cos, 110f);
                SpawnEmitter(_cones, position, yaw, 10f);
                SpawnEmitter(_cones, position, yaw, 10f);
                _lastAttackEffectTime = 8;
            }

            if (_attackTime >= 13f && _attackTime <= 14f && FpsCheck(1, factor))
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 position = CasterPosition + new Vector3(145f * sin, -145f * cos, 110f);
                    position.X += MuGame.Random.Next(60) - 30;
                    position.Y += MuGame.Random.Next(60) - 30;
                    position.Z += MuGame.Random.Next(60) - 30;
                    SpawnSpear(position, yaw, sin, cos);
                }
            }

            _attackTime += factor;
            if ((int)_attackTime >= LimitAttackTime)
                _stageActive = false;
        }

        private static void SpawnEmitter(Emitter[] pool, Vector3 position, float yaw, float lifeTime)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                ref Emitter e = ref pool[i];
                if (e.Live)
                    continue;

                e.Live = true;
                e.Position = position;
                e.Yaw = yaw;
                e.LifeTime = lifeTime;
                return;
            }
        }

        private void SpawnSpear(Vector3 position, float yaw, float sin, float cos)
        {
            for (int i = 0; i < _spears.Length; i++)
            {
                ref Spear s = ref _spears[i];
                if (s.Live)
                    continue;

                bool first = true;
                for (int k = 0; k < _spears.Length; k++)
                    if (_spears[k].Live) { first = false; break; }
                if (first)
                    _spearFrame = 0f;

                s.Live = true;
                s.Position = position;
                s.Yaw = yaw;
                s.LifeTime = 20f;
                s.Direction = new Vector3(5f * sin, -5f * cos, 0f);
                return;
            }
        }

        // ------------------------------------------------------------------ BITMAP_JOINT_HEALING sub 6

        private void SpawnHealing(Vector3 position, Vector3 target, Vector3 angle)
        {
            for (int i = 0; i < _healings.Length; i++)
            {
                ref Healing h = ref _healings[i];
                if (h.Live)
                    continue;

                SourceJointMath.AngleBasis(angle, out h.Right, out h.Forward, out h.Up);
                h.Live = true;
                h.Position = position;
                h.TargetPosition = target;
                h.Velocity = 0f;
                h.LifeTime = 12f;
                int maxTails = SourceJointMath.ScaledMaxTails(4);
                if (h.Trail == null || h.Trail.MaxTails != maxTails)
                    h.Trail = new SourceTrail(maxTails);
                else
                    h.Trail.Clear();
                h.Trail.Push(position, h.Right, h.Up, 5f);
                return;
            }
        }

        private void MoveHealings(float factor)
        {
            for (int i = 0; i < _healings.Length; i++)
            {
                ref Healing h = ref _healings[i];
                if (!h.Live)
                    continue;

                h.Position += -h.Forward * (h.Velocity * factor);
                h.Velocity += factor * 2f;
                h.Trail.Push(h.Position, h.Right, h.Up, 5f);

                h.LifeTime -= factor;
                if (h.LifeTime < 0f)
                    h.Live = false;
            }
        }

        // ------------------------------------------------------------------ BITMAP_FLARE sub 4

        private void SpawnFlare(Vector3 position, float yaw)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;

                float rad = MathHelper.ToRadians(yaw);
                float rad90 = MathHelper.ToRadians(90f - yaw);
                SourceJointMath.AngleBasis(new Vector3(0f, 0f, yaw), out f.Right, out _, out f.Up);
                f.Live = true;
                f.Position = position;
                // TargetPosition is never assigned for this joint in the original (stale pool data).
                f.TargetPosition = position;
                f.SinYaw = MathF.Sin(rad90);
                f.CosYaw = MathF.Cos(rad90);
                f.LifeTime = 110f;
                f.Direction0 = MuGame.Random.Next(360);
                f.Direction1 = -2f * MathF.Sin(-rad);
                f.Direction2 = -2f * MathF.Cos(-rad);
                int maxTails = SourceJointMath.ScaledMaxTails(200);
                if (f.Trail == null || f.Trail.MaxTails != maxTails)
                    f.Trail = new SourceTrail(maxTails);
                else
                    f.Trail.Clear();
                f.Trail.Push(position, f.Right, f.Up, 50f);
                return;
            }
        }

        private void MoveFlares(float factor)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;

                // MoveJoint re-enters itself while (int)LifeTime % 12 != 0.
                for (int guard = 0; guard < 256; guard++)
                {
                    float count = (f.Direction0 + f.LifeTime) * 0.1f;
                    float radius = MathF.Max(f.LifeTime + 40f, 10f) * 0.65f;
                    float vx = -MathF.Cos(count) * radius;
                    float vz = MathF.Sin(count) * radius;

                    f.TargetPosition.X += f.Direction1 * factor;
                    f.TargetPosition.Y += f.Direction2 * factor;
                    f.Position = new Vector3(vx * f.SinYaw + f.TargetPosition.X, vx * f.CosYaw + f.TargetPosition.Y, vz + f.TargetPosition.Z);
                    f.Trail.Push(f.Position, f.Right, f.Up, 50f);

                    f.LifeTime -= factor;
                    if (f.LifeTime < 0f)
                    {
                        f.Live = false;
                        break;
                    }

                    if ((int)f.LifeTime % 12 == 0)
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle / draw

        protected override bool HasActiveContent()
        {
            if (_stageActive)
                return true;
            for (int i = 0; i < _gatherers.Length; i++) if (_gatherers[i].Live) return true;
            for (int i = 0; i < _cones.Length; i++) if (_cones[i].Live) return true;
            for (int i = 0; i < _healings.Length; i++) if (_healings[i].Live) return true;
            for (int i = 0; i < _flares.Length; i++) if (_flares[i].Live) return true;
            for (int i = 0; i < _spears.Length; i++) if (_spears[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_healingTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var light = new Vector3(1f, 1f, 0.5f);
                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live)
                        Batch.AddTrail(h.Trail, SourceQuadBatch.TrailFaces.Both, light, SourceQuadBatch.TrailShade.Flat, h.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (f.Live)
                        Batch.AddTrail(f.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat, f.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_spearModel != null)
            {
                bool updated = false;
                for (int i = 0; i < _spears.Length; i++)
                {
                    ref Spear s = ref _spears[i];
                    if (!s.Live)
                        continue;

                    if (!updated)
                    {
                        _spearModel.SetAnimationTime(_spearFrame);
                        _spearModel.Update(LastGameTime);
                        updated = true;
                    }

                    // RenderSkillSpear: Light * LifeTime * 0.05 with RENDER_BRIGHT (additive), BlendMeshLight 1.
                    var light = new Vector3(0.3f * s.LifeTime * 0.05f);
                    _spearModel.DrawInstance(s.Position, new Vector3(0f, 0f, MathHelper.ToRadians(s.Yaw)), 1.5f, light, 1f);
                }
            }

            // CreateSprite(BITMAP_SHINY + 1, p, ...) from every healing joint with LifeTime <= 10 (one frame each).
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (!h.Live || h.LifeTime > 10f)
                        continue;

                    float alpha = (6f - MathF.Abs(h.LifeTime - 6f)) * 0.15f;
                    Batch.AddSprite(h.TargetPosition, (MuGame.Random.Next(8) + 8) * 0.05f, new Vector3(alpha), MuGame.Random.Next(360));
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _spearModel?.Dispose();
        }
    }
}
