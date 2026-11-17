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
    /// Twisting Slash (AT_SKILL_WHEEL = 41, masters AT_SKILL_TORNADO_SWORDA/B_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4883      PLAYER_ATTACK_SKILL_WHEEL, AttackTime = 1
    /// gate    ZzzCharacter.cpp:2876  AttackStage default: AnimationFrame >= 5 -> AttackTime = 15 (else 14 ticks)
    /// release ZzzCharacter.cpp:4252  MODEL_SKILL_WHEEL1 at the caster, SOUND_SKILL_SWORD4
    /// wheel1  ZzzEffect.cpp:1922 (init), 9767 (move: one MODEL_SKILL_WHEEL2 per tick), not rendered
    /// wheel2  ZzzEffect.cpp:1926 (init), 9770 (move), 17993 RenderWheelWeapon (caster weapon model)
    /// spark   ZzzEffectJoint.cpp:902 (BITMAP_JOINT_SPARK sub 0); particles ZzzEffectParticle.cpp:2027/6589
    ///         (BITMAP_SPARK sub 0), 1261/5361 (BITMAP_SMOKE sub 3); sprite BITMAP_LIGHT
    /// </summary>
    internal sealed class TwistingSlashEffect : SourceSkillEffectBase
    {
        private const string SmokeTexturePath = "Effect/smoke01.jpg";      // BITMAP_SMOKE
        private const string LightTexturePath = "Effect/flare01.jpg";      // BITMAP_LIGHT
        private const string SoundPath = "Sound/sKnightSkill4.wav";        // SOUND_SKILL_SWORD4

        private const int SkillId = 41;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const int MaxWheels = 16;
        private const int MaxSparkJoints = 384;
        private const int MaxSparks = 256;
        private const int MaxSmokes = 128;
        private const int GroupSpear = 3;

        private static readonly Vector3 LightSpriteLight = new(1f, 0.8f, 0.6f);

        private struct Wheel
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;      // degrees
            public float Alpha;
            public float Direction2;   // RenderWheelWeapon spin accumulator
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Wheel[] _wheels = new Wheel[MaxWheels];
        private readonly SourceSparkPool _sparkPool = new(MaxSparkJoints, MaxSparks);
        private readonly Particle[] _smokes = new Particle[MaxSmokes];

        private Texture2D? _sparkJointTexture;
        private Texture2D? _sparkTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _lightTexture;
        private SourceModelRenderer? _weapon;

        private Vector3 _emitterPosition;
        private Vector3 _emitterAngle;
        private float _emitterLifeTime;
        private float _wheelRadius = 150f;

        public TwistingSlashEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 2048)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _sparkJointTexture = await LoadTexture(SourceSparkPool.JointTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);

            // o->Weapon = c->Weapon[0].Type - MODEL_SWORD; RenderWheelWeapon draws that item model.
            if (Caster is PlayerObject player && player.Weapon1?.Model != null)
            {
                WeaponObject weapon = player.Weapon1;
                var renderer = new SourceModelRenderer(weapon.Model)
                {
                    ItemLevel = weapon.ItemLevel,
                    MaterialItemGroup = weapon.MaterialItemGroup,
                    MaterialItemIndex = weapon.MaterialItemIndex,
                };
                if (await renderer.LoadInto(World))
                    _weapon = renderer;

                if (weapon.MaterialItemGroup == GroupSpear)
                    _wheelRadius = 180f;
            }
        }

        protected override void OnStart()
        {
            _emitterPosition = CasterPosition;
            _emitterAngle = new Vector3(0f, 0f, CasterYawDegrees);
            _emitterLifeTime = 5f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            // MODEL_SKILL_WHEEL1: CreateEffectFpsChecked(MODEL_SKILL_WHEEL2, ..., SubType = 4 - LifeTime).
            if (_emitterLifeTime > 0f)
            {
                if (FpsCheck(1, factor))
                    SpawnWheel((int)(4f - _emitterLifeTime));
                _emitterLifeTime -= factor;
            }

            Vector3 owner = CasterPosition;
            for (int i = 0; i < MaxWheels; i++)
            {
                ref Wheel w = ref _wheels[i];
                if (!w.Live)
                    continue;

                w.Position = owner + SourceJointMath.Rotate(new Vector3(0f, -_wheelRadius, 0f), w.Angle);
                w.Angle.Z -= 18f * factor;

                if (FpsCheck(1, factor) && FpsCheck(1, factor))
                    SpawnSmoke(w.Position, w.Angle);

                for (int j = 0; j < 4; j++)
                {
                    if (!FpsCheck(1, factor))
                        continue;

                    var angle = new Vector3(
                        MuGame.Random.Next(60) + 60 - 90 + w.Angle.X,
                        w.Angle.Y,
                        MuGame.Random.Next(30) + 90 + w.Angle.Z);
                    var pos = new Vector3(
                        w.Position.X + MuGame.Random.Next(20) - 10,
                        w.Position.Y + MuGame.Random.Next(20) - 10,
                        w.Position.Z);
                    if (FpsCheck(1, factor))
                        _sparkPool.SpawnJoint(pos, angle);
                    if (FpsCheck(4, factor) && FpsCheck(1, factor))
                        _sparkPool.SpawnSpark(pos, angle);
                }

                w.LifeTime -= factor;
                if (w.LifeTime <= 0f)
                    w.Live = false;
            }

            _sparkPool.Move(factor, World?.Terrain);
            MoveSmokes(factor);

            // Dynamic lights: warm glow on the lead wheel plus a soft fill around the caster.
            ref Wheel lead = ref _wheels[0];
            float wheelGlow = lead.Live ? EffectLights.Envelope(25f - lead.LifeTime, 25f, 3f, 8f) : 0f;
            Lights.Set(0, lead.Position + new Vector3(0f, 0f, 40f), LightSpriteLight, 230f, 1.2f * wheelGlow);
            Lights.Set(1, owner + new Vector3(0f, 0f, 60f), LightSpriteLight, 360f, 0.55f * wheelGlow);
        }

        private void SpawnWheel(int subType)
        {
            for (int i = 0; i < MaxWheels; i++)
            {
                ref Wheel w = ref _wheels[i];
                if (w.Live)
                    continue;

                w.Live = true;
                w.Position = _emitterPosition;
                w.Angle = _emitterAngle;
                w.Direction2 = 0f;
                w.LifeTime = 25f;
                w.Alpha = subType switch { 1 => 0.6f, 2 => 0.5f, 3 => 0.4f, 4 => 0.3f, _ => 1f };
                return;
            }
        }

        // ------------------------------------------------------------------ BITMAP_SMOKE sub 3

        private void SpawnSmoke(Vector3 position, Vector3 angle)
        {
            for (int i = 0; i < MaxSmokes; i++)
            {
                ref Particle p = ref _smokes[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.LifeTime = 10f;
                p.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                p.Angle = new Vector3(MuGame.Random.Next(90) - 45, angle.Y, MuGame.Random.Next(360));
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                p.Light = Vector3.One;
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
            for (int i = 0; i < MaxSmokes; i++)
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

                float luminosity = p.LifeTime / 8f;
                p.Light = new Vector3(luminosity * 0.8f, luminosity * 0.8f, luminosity);
                p.Velocity *= 0.4f; // per frame, not scaled by the factor
                p.Scale += factor * 0.1f;
            }
        }

        // ------------------------------------------------------------------ lifecycle / draw

        protected override bool HasActiveContent()
        {
            if (_emitterLifeTime > 0f)
                return true;
            for (int i = 0; i < MaxWheels; i++)
                if (_wheels[i].Live) return true;
            if (_sparkPool.HasLive)
                return true;
            for (int i = 0; i < MaxSmokes; i++)
                if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            float factor = SourceJointMath.FrameFactor;

            _sparkPool.DrawJoints(Batch, _sparkJointTexture);

            // RenderWheelWeapon: Direction[2] -= 30 per render, Angle[1] = 90, Position[2] += 100 (both factor-scaled).
            if (_weapon != null)
            {
                for (int i = 0; i < MaxWheels; i++)
                {
                    ref Wheel w = ref _wheels[i];
                    if (!w.Live)
                        continue;

                    w.Direction2 -= 30f * factor;
                    var angle = new Vector3(
                        MathHelper.ToRadians(w.Angle.X),
                        MathHelper.ToRadians(90f),
                        MathHelper.ToRadians(w.Angle.Z + w.Direction2));
                    Vector3 position = w.Position + new Vector3(0f, 0f, 100f * factor);
                    Vector3 light = (World?.Terrain?.EvaluateTerrainLight(position.X, position.Y) ?? Vector3.Zero) + Vector3.One;
                    _weapon.DrawInstance(position, angle, 1f, light, 1f, w.Alpha);
                }
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < MaxWheels; i++)
                {
                    ref Wheel w = ref _wheels[i];
                    if (w.Live)
                        Batch.AddSprite(w.Position + new Vector3(0f, 0f, 20f), 2f, LightSpriteLight);
                }
                Batch.End();
            }

            DrawParticles(_smokeTexture, _smokes);
            _sparkPool.DrawSparks(Batch, _sparkTexture);
        }

        private void DrawParticles(Texture2D? texture, Particle[] particles)
        {
            if (!Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < particles.Length; i++)
            {
                ref Particle p = ref particles[i];
                if (p.Live)
                    Batch.AddSprite(p.Position, p.Scale, p.Light);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            _weapon?.Dispose();
        }
    }
}
