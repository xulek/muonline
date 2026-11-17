#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Death Stab (AT_SKILL_DEATHSTAB = 43, masters AT_SKILL_BLOW_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4067       PLAYER_ATTACK_DEATHSTAB, AttackTime = 1
    /// stage   ZzzCharacter.cpp:2439   charge joints, m_vPosSword, MODEL_SPEAR x2, hurt = 35, release at AttackTime 12
    /// charge  ZzzEffectJoint.cpp:1528 (MODEL_SPEARSKILL sub 2 init), 4235 (move), 6957 (colour)
    /// spear   ZzzEffect.cpp:642 (init), 7387 (move: BITMAP_FLARE sub 12); not rendered (item model)
    /// flare   ZzzEffectJoint.cpp:1836 (init), 5520 (move), 6883 (re-entry), 7038/7198 (U, colour)
    /// hurt    ZzzCharacter.cpp:3915   BITMAP_JOINT_THUNDER sub 7 bone to parent while m_byHurtByDeathstab &gt; 0
    /// thunder ZzzEffectJoint.cpp:1117 (init), 4688 (move), 7026 (U)
    /// </summary>
    internal sealed class DeathStabEffect : SourceSkillEffectBase
    {
        private const string ChargeTexturePath = "Effect/NSkill.jpg";          // BITMAP_FLARE_FORCE
        private const string FlareTexturePath = "Effect/Flare.jpg";            // BITMAP_FLARE
        private const string ThunderTexturePath = "Effect/JointThunder01.jpg"; // BITMAP_JOINT_THUNDER (GL_REPEAT)
        private const string SoundPath = "Sound/sKnightSkill2.wav";            // SOUND_SKILL_SWORD2

        private const int SkillId = 43;
        private const int WeaponBone = 33;      // c->Weapon[0].LinkBone (GetHandOfWeapon = 0)
        private const float LimitAttackTime = 15f;
        private const byte HurtFrames = 35;

        private const int MaxCharges = 64;
        private const int MaxSpears = 32;
        private const int MaxFlares = 128;
        private const int MaxThunders = 512;

        private static readonly Vector3 ChargeLight = new(1f, 0.3f, 0.3f);
        private static readonly Vector3 FlareLight = new(0.1f, 0.1f, 1f);
        private static readonly Vector3 ThunderLight = new(0.5f, 0.5f, 1f);

        // m_byHurtByDeathstab lives on the target: the latest Death Stab on a target owns it.
        private static readonly Dictionary<WalkerObject, DeathStabEffect> HurtOwners = new();

        private struct Charge
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TargetPosition;
            public Vector3 Right;
            public Vector3 Up;
            public float LifeTime;
            public float Scale;
            public SourceTrail Trail;
        }

        private struct Spear
        {
            public bool Live;
            public Vector3 Position;
            public float Yaw;
            public float LifeTime;
        }

        private struct Flare
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TargetPosition;
            public Vector3 Right;
            public Vector3 Up;
            public float Yaw;
            public float Direction0;
            public float Direction1;
            public float Direction2;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Thunder
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 TargetPosition;
            public Vector3 Angle;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly WalkerObject? _target;
        private readonly Charge[] _charges = new Charge[MaxCharges];
        private readonly Spear[] _spears = new Spear[MaxSpears];
        private readonly Flare[] _flares = new Flare[MaxFlares];
        private readonly Thunder[] _thunders = new Thunder[MaxThunders];

        private Texture2D? _chargeTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _thunderTexture;

        private float _attackTime = 1f;
        private int _lastAttackEffectTime = -1;
        private bool _stageActive;
        private Vector3 _posSword;
        private int _hurt;
        private bool _stageLights;
        private float _lightAge;
        private Vector3 _hurtLightPos;
        private static readonly Vector3 ThrustGlow = new(0.35f, 0.4f, 1f);

        public DeathStabEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 0f, 0f, 2048)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _chargeTexture = await LoadTexture(ChargeTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
            _thunderTexture = await LoadTexture(ThunderTexturePath);
        }

        protected override void OnStart()
        {
            // AttackStage breaks out for a missing weapon link bone; only players carry bone 33.
            _stageActive = Caster is PlayerObject;
            _stageLights = _stageActive;
            _posSword = SwordPoint();
        }

        protected override void OnTick(float factor)
        {
            // Frame order of the original: MoveCharacters (AttackStage, hurt) -> MoveEffects -> MoveJoints.
            if (_stageActive)
                TickAttackStage(factor);

            TickHurt(factor);

            for (int i = 0; i < MaxSpears; i++)
            {
                ref Spear spear = ref _spears[i];
                if (!spear.Live)
                    continue;

                if (FpsCheck(1, factor))
                    SpawnFlare(spear.Position, spear.Yaw);

                spear.LifeTime -= factor;
                if (spear.LifeTime <= 0f)
                    spear.Live = false;
            }

            MoveCharges(factor);
            MoveFlares(factor);
            MoveThunders(factor);

            _lightAge += factor;
            UpdateLights();
        }

        private void UpdateLights()
        {
            // Red charge gathering at the sword point, then a blue flash where the spears burst out.
            float charge = _stageLights ? EffectLights.Envelope(_lightAge - 1f, 12f, 3f, 4f) : 0f;
            Lights.Set(0, _posSword, ChargeLight, 200f, charge);
            float thrust = _lightAge - 6f;
            float flash = _stageLights ? 1.7f * MathF.Min(1f, thrust / 1.5f) * EffectLights.Flash(thrust, 22f, 1.4f) : 0f;
            Lights.Set(1, _posSword, ThrustGlow, 340f, flash);

            // Flickering thunder on the stabbed target.
            float hurt = 0f;
            if (OwnsHurt && IsAlive(_target))
            {
                hurt = MathF.Min(1f, _hurt / 8f) * MathF.Min(1f, (HurtFrames - _hurt + 1) / 2f)
                    * EffectLights.Flicker(_lightAge * 0.04f, 7.3f, 0.45f);
                _hurtLightPos = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 100f);
            }
            Lights.Set(2, _hurtLightPos, ThunderLight, 230f, 1.1f * hurt);
        }

        // ------------------------------------------------------------------ AttackStage

        private void TickAttackStage(float factor)
        {
            float yaw = CasterYawDegrees;
            float rad = MathHelper.ToRadians(yaw);
            float sin = MathF.Sin(rad);
            float cos = MathF.Cos(rad);

            if ((int)_attackTime == 8 && _lastAttackEffectTime != 8)
            {
                PlayUnmappedSound(SkillId, SoundPath);
                _lastAttackEffectTime = 8;
            }

            if (_attackTime >= 2f && _attackTime <= 8f && FpsCheck(1, factor))
            {
                for (int j = 0; j < 3; j++)
                {
                    Vector3 pos = NearRandom(CasterPosition + new Vector3(0f, 0f, 120f), 300);
                    pos.X += -1400f * sin;
                    pos.Y += 1400f * cos;
                    SpawnCharge(pos, yaw);
                }
            }

            if (_attackTime <= 8f && FpsCheck(1, factor))
                _posSword = SwordPoint();

            if (_attackTime >= 6f && _attackTime <= 12f)
            {
                if (FpsCheck(2, factor))
                {
                    Vector3 pos = GetCasterBonePosition(WeaponBone);
                    float distance = 100f + (_attackTime - 8f) * 10f;
                    pos.X += distance * sin;
                    pos.Y += -distance * cos;
                    SpawnSpear(pos, yaw);
                    SpawnSpear(pos, yaw);
                }

                if (_attackTime >= 10f && IsAlive(_target))
                {
                    HurtOwners[_target!] = this;
                    _hurt = HurtFrames;
                }
            }

            if (_attackTime >= 12f)
                _attackTime = LimitAttackTime;

            _attackTime += factor;
            if ((int)_attackTime >= LimitAttackTime)
                _stageActive = false;
        }

        private Vector3 SwordPoint()
        {
            float rad = MathHelper.ToRadians(CasterYawDegrees);
            Vector3 pos = GetCasterBonePosition(WeaponBone);
            pos.X += 300f * MathF.Sin(rad);
            pos.Y += -300f * MathF.Cos(rad);
            return pos;
        }

        // ------------------------------------------------------------------ hurt (target side)

        private bool OwnsHurt => _hurt > 0 && _target != null
            && HurtOwners.TryGetValue(_target, out var owner) && ReferenceEquals(owner, this);

        private void TickHurt(float factor)
        {
            if (!OwnsHurt)
                return;

            if (!IsAlive(_target))
            {
                ReleaseHurt();
                return;
            }

            if (FpsCheck(2, factor) && _target is ModelObject model)
                SpawnBoneThunders(model);

            // BYTE -= float: the result is truncated toward zero.
            _hurt = Math.Max(0, (int)(_hurt - factor));
            if (_hurt == 0)
                ReleaseHurt();
        }

        private void SpawnBoneThunders(ModelObject model)
        {
            BMD? bmd = model.Model;
            Matrix[]? bones = model.GetBoneTransforms();
            if (bmd?.Bones == null || bones == null)
                return;

            Matrix world = model.WorldPosition;
            int numBones = Math.Min(bmd.Bones.Length, bones.Length);
            Vector3 angle = new(
                MathHelper.ToDegrees(model.Angle.X),
                MathHelper.ToDegrees(model.Angle.Y),
                MathHelper.ToDegrees(model.Angle.Z));

            for (int i = 0; i < numBones; i++)
            {
                BMDTextureBone bone = bmd.Bones[i];
                if (bone == BMDTextureBone.Dummy)
                    continue;

                int parent = bone.Parent;
                if (parent <= -1 || parent >= numBones)
                    continue;

                Vector3 pos1 = NearRandom(Vector3.Transform(bones[i].Translation, world), 20);
                Vector3 pos2 = NearRandom(Vector3.Transform(bones[parent].Translation, world), 20);
                if (!SpawnThunder(pos1, pos2, angle))
                    return;
            }
        }

        private void ReleaseHurt()
        {
            _hurt = 0;
            if (_target != null && HurtOwners.TryGetValue(_target, out var owner) && ReferenceEquals(owner, this))
                HurtOwners.Remove(_target);
        }

        // ------------------------------------------------------------------ MODEL_SPEARSKILL sub 2

        private void SpawnCharge(Vector3 position, float yaw)
        {
            for (int i = 0; i < MaxCharges; i++)
            {
                ref Charge c = ref _charges[i];
                if (c.Live)
                    continue;

                c.Live = true;
                c.Position = position;
                c.TargetPosition = position;
                c.LifeTime = 20f;
                c.Scale = 40f;
                c.Trail = PrepareTrail(c.Trail, SourceJointMath.ScaledMaxTails(5));
                Basis(yaw, out c.Right, out c.Up);
                c.Trail.Push(position, c.Right, c.Up, c.Scale);
                return;
            }
        }

        private void MoveCharges(float factor)
        {
            for (int i = 0; i < MaxCharges; i++)
            {
                ref Charge c = ref _charges[i];
                if (!c.Live)
                    continue;

                if (IsAlive(Caster))
                {
                    c.Scale = c.LifeTime * 3f;
                    float rate1 = MathHelper.Clamp((c.LifeTime - 10f) / 10f, 0f, 1f);
                    float rate2 = 1f - rate1;
                    Vector3 magic = SourceJointMath.MagicScrew(i * 17721, 1.4f) + c.TargetPosition;
                    c.Position = rate2 * _posSword + rate1 * magic;
                }

                c.Trail.Push(c.Position, c.Right, c.Up, c.Scale);

                c.LifeTime -= factor;
                if (c.LifeTime < 0f)
                    c.Live = false;
            }
        }

        // ------------------------------------------------------------------ MODEL_SPEAR sub 1

        private void SpawnSpear(Vector3 position, float yaw)
        {
            for (int i = 0; i < MaxSpears; i++)
            {
                ref Spear s = ref _spears[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Position = position;
                s.Yaw = yaw;
                s.LifeTime = 10f;
                return;
            }
        }

        // ------------------------------------------------------------------ BITMAP_FLARE sub 12

        private void SpawnFlare(Vector3 position, float yaw)
        {
            for (int i = 0; i < MaxFlares; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;

                float rad = MathHelper.ToRadians(yaw);
                Basis(yaw, out Vector3 right, out Vector3 up);

                f.Live = true;
                f.Position = position;
                // The original never assigns TargetPosition for this joint (stale pool data);
                // the spear position is the only defined origin.
                f.TargetPosition = position;
                f.Right = right;
                f.Up = up;
                f.Yaw = yaw;
                f.LifeTime = 70f;
                f.Direction0 = MuGame.Random.Next(360);
                f.Direction1 = -4f * MathF.Sin(-rad);
                f.Direction2 = -4f * MathF.Cos(-rad);
                f.Trail = PrepareTrail(f.Trail, SourceJointMath.ScaledMaxTails(50));
                f.Trail.Push(position, right, up, 100f);
                return;
            }
        }

        private void MoveFlares(float factor)
        {
            for (int i = 0; i < MaxFlares; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;

                float rad = MathHelper.ToRadians(90f - f.Yaw);
                float sx = MathF.Sin(rad);
                float cx = MathF.Cos(rad);

                // MoveJoint re-enters itself while (int)LifeTime % 12 != 0.
                for (int guard = 0; guard < 256; guard++)
                {
                    float count = (f.Direction0 + f.LifeTime) * 0.1f;
                    float vx = -MathF.Cos(count) * (40f * 0.65f);
                    float vz = MathF.Sin(count) * (40f * 0.65f) - (90f - f.LifeTime) * 0.3f;

                    f.TargetPosition.X += f.Direction1 * factor;
                    f.TargetPosition.Y += f.Direction2 * factor;
                    f.Position = new Vector3(
                        vx * sx + f.TargetPosition.X,
                        vx * cx + f.TargetPosition.Y,
                        vz + f.TargetPosition.Z);

                    f.Trail.Push(f.Position, f.Right, f.Up, 100f);

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

        // ------------------------------------------------------------------ BITMAP_JOINT_THUNDER sub 7

        private bool SpawnThunder(Vector3 from, Vector3 to, Vector3 angle)
        {
            for (int i = 0; i < MaxThunders; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live)
                    continue;

                t.Live = true;
                t.Position = from;
                t.StartPosition = from;
                t.TargetPosition = to;
                t.Angle = angle;
                t.LifeTime = 1f;
                t.Trail = PrepareTrail(t.Trail, SourceJointMath.ScaledMaxTails(3));
                t.Trail.Push(from, angle, 20f);
                SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, t.TargetPosition, 360f, SourceJointMath.FrameFactor);
                return true;
            }

            return false;
        }

        private void MoveThunders(float factor)
        {
            const float velocity = 5f;
            for (int i = 0; i < MaxThunders; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (!t.Live)
                    continue;

                // Prologue matrix (angle before this frame's humming) is the one the epilogue tail uses.
                SourceJointMath.AngleBasis(t.Angle, out Vector3 endRight, out _, out Vector3 endUp);

                t.Position = t.StartPosition;
                int steps = t.Trail.MaxTails;
                for (int j = 0; j < steps; j++)
                {
                    SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, t.TargetPosition, MuGame.Random.Next(100) + 50, factor);

                    var angle = new Vector3(
                        t.Angle.X + (MuGame.Random.Next(100) + 20) * factor,
                        t.Angle.Y,
                        t.Angle.Z + (MuGame.Random.Next(100) + 20) * factor);
                    SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                    t.Trail.Push(t.Position, right, up, 20f);

                    t.Position += -forward * (velocity * factor * factor);
                }

                t.Position = t.TargetPosition;
                t.Trail.Push(t.Position, endRight, endUp, 20f);

                t.LifeTime -= factor;
                if (t.LifeTime < 0f)
                    t.Live = false;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static SourceTrail PrepareTrail(SourceTrail? trail, int maxTails)
        {
            if (trail == null || trail.MaxTails != maxTails)
                return new SourceTrail(maxTails);

            trail.Clear();
            return trail;
        }

        private static void Basis(float yaw, out Vector3 right, out Vector3 up)
            => SourceJointMath.AngleBasis(new Vector3(0f, 0f, yaw), out right, out _, out up);

        // GetNearRandomPos (ZzzEffect.h)
        private static Vector3 NearRandom(Vector3 pos, int range)
        {
            int span = range * 2 + 1;
            return new Vector3(
                pos.X + MuGame.Random.Next(span) - range,
                pos.Y + MuGame.Random.Next(span) - range,
                pos.Z + MuGame.Random.Next(span) - range);
        }

        private static bool IsAlive(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        protected override bool HasActiveContent()
        {
            if (_stageActive || OwnsHurt)
                return true;
            for (int i = 0; i < MaxCharges; i++)
                if (_charges[i].Live) return true;
            for (int i = 0; i < MaxSpears; i++)
                if (_spears[i].Live) return true;
            for (int i = 0; i < MaxFlares; i++)
                if (_flares[i].Live) return true;
            for (int i = 0; i < MaxThunders; i++)
                if (_thunders[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            float scroll = (float)((long)FPSCounter.Instance.WorldTime % 1000) * 0.001f;

            if (Batch.Begin(_chargeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < MaxCharges; i++)
                {
                    ref Charge c = ref _charges[i];
                    if (!c.Live)
                        continue;

                    float alpha = MathF.Min(c.LifeTime, 20f) * 0.05f;
                    Batch.AddTrail(c.Trail, SourceQuadBatch.TrailFaces.Both, ChargeLight * alpha,
                        SourceQuadBatch.TrailShade.Flat, c.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            // SubType 11..13 takes the BITMAP_FLARE_FORCE U and colour branches for any joint type.
            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < MaxFlares; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (!f.Live)
                        continue;

                    Batch.AddTrail(f.Trail, SourceQuadBatch.TrailFaces.Both, FlareLight,
                        SourceQuadBatch.TrailShade.FlareForce, (f.Trail.MaxTails - 1) / 2, scroll);
                }
                Batch.End();
            }

            if (Batch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                // bTileMapping + thunder: U = (NumTails - j) / 16 * 4^F - 2 * Scroll * 2^F,
                // face TWO adds Scroll * 2^F * 2 back.
                float factor = SourceJointMath.FrameFactor;
                float pow2 = MathF.Pow(2f, factor);
                float denominator = 16f / (pow2 * pow2);
                float uScroll = 2f * scroll * pow2;

                for (int i = 0; i < MaxThunders; i++)
                {
                    ref Thunder t = ref _thunders[i];
                    if (!t.Live)
                        continue;

                    Batch.AddTrail(t.Trail, SourceQuadBatch.TrailFaces.Both, ThunderLight,
                        SourceQuadBatch.TrailShade.Flat, denominator, uScroll, false, uScroll);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            ReleaseHurt();
        }
    }
}
