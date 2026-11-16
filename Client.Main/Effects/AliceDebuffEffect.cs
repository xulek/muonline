#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Effects
{
    /// <summary>
    /// Summoner curse visuals on the target while the status lasts, SourceMain5.2 (WSclient.cpp:15127-15157 on the buff packet,
    /// ZzzCharacter.cpp:10673-10731 keeps them alive, WSclient.cpp:15342-15361 deletes the emitter):
    /// Blind (eDeBuff_Blind 73)       MODEL_ALICE_BUFFSKILL_EFFECT sub 3, Scale 1.5, (1, 1, 1): ZzzEffect.cpp:9257 random non-dummy
    ///                                bone, 2 x CreateParticleFpsChecked(BITMAP_LIGHT+2 sub 6) (the second on another random bone)
    /// Sleep (eDeBuff_Sleep 72)       sub 4, (0.7, 0.1, 0.9): random non-dummy bone, rand_fps_check(2): Z - 20, BITMAP_TWINTAIL_WATER sub 2
    /// Weakness (eDeBuff_AttackDown 76) / Enervation (eDeBuff_DefenseDown 77): BITMAP_SHINY+6 (0.5) and BITMAP_PIN_LIGHT (1.0)
    ///                                sub 1 / 2, (1.4, 0.2, 0.2) / (0.25, 1, 0.7): ZzzEffect.cpp:8303 / 8377 random non-dummy bone
    ///                                with offset (0, 0, 100), rand_fps_check(2): Z - 20, particle sub 0 (SourceShinyPinPool)
    /// light+2 sub 6  ZzzEffectParticle.cpp:898 (LifeTime 21, Scale + (rand%4+8)*0.01, Gravity 0.1, TurningForce (0, (rand%2+60)*0.1, 0)
    ///                at yaw rand%360, Z += 2F, Rotation WorldTime%360), 4080 (as sub 5, Light = Alpha, Rotation += 2F), 9182 minus
    /// water sub 2    ZzzEffectParticle.cpp:1146 (LifeTime 60 + rand%10, Scale (rand%32+50)*0.02, XY += (rand%40-20)F, Z += rand%40*F,
    ///                Gravity (rand%10+20)*0.1), 5286 (Scale -= 0.026F, Z += Gravity*F, Light *= (1/1.02)^F), 9035 additive
    /// </summary>
    internal sealed class AliceDebuffEffect : SourceSkillEffectBase
    {
        public enum Kind { Sleep, Blind, AttackDown, DefenseDown }

        private const string CraTexturePath = "Effect/cra_04.jpg";  // BITMAP_LIGHT + 2
        private const string WaterTexturePath = "Effect/water.jpg"; // BITMAP_TWINTAIL_WATER
        private const int MaxParticles = 72;

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TurningForce;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
            public float Alpha;
        }

        private readonly Kind _kind;
        private readonly Vector3 _light;
        private readonly Particle[]? _particles;
        private readonly SourceShinyPinPool? _sparkles;
        private Texture2D? _texture, _shinyTexture, _lightTexture, _pinTexture;
        private Vector3 _angle;
        private int _particleCount;
        private bool _stopped;

        public AliceDebuffEffect(WalkerObject target, Kind kind)
            : base(target, 0f, 0f, MaxParticles)
        {
            _kind = kind;
            _light = kind switch
            {
                Kind.Sleep => new Vector3(0.7f, 0.1f, 0.9f),
                Kind.Blind => Vector3.One,
                Kind.AttackDown => new Vector3(1.4f, 0.2f, 0.2f),
                _ => new Vector3(0.25f, 1f, 0.7f),
            };
            if (kind is Kind.Sleep or Kind.Blind)
                _particles = new Particle[MaxParticles];
            else
                _sparkles = new SourceShinyPinPool(48);
        }

        /// <summary>Status ended: DeleteEffect removes the emitter, live particles finish.</summary>
        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            switch (_kind)
            {
                case Kind.Sleep:
                    _texture = await LoadTexture(WaterTexturePath);
                    break;
                case Kind.Blind:
                    _texture = await LoadTexture(CraTexturePath);
                    break;
                default:
                    _shinyTexture = await LoadTexture(SourceShinyPinPool.ShinyTexturePath);
                    _lightTexture = await LoadTexture(SourceShinyPinPool.LightTexturePath);
                    _pinTexture = await LoadTexture(SourceShinyPinPool.PinTexturePath);
                    break;
            }
        }

        protected override void OnStart()
        {
            // o->Angle of the emitter: the owner's angle when the status arrived.
            _angle = new Vector3(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));
        }

        protected override void OnTick(float factor)
        {
            Position = CasterPosition;
            int bones = SourceOwnerBones.Count(Caster);
            if (!_stopped && bones > 0 && !Caster.Hidden)
                Emit(bones, factor);

            switch (_kind)
            {
                case Kind.Sleep: MoveWater(factor); break;
                case Kind.Blind: MoveBlind(factor); break;
                default: _sparkles!.Move(factor); break;
            }
        }

        private void Emit(int bones, float factor)
        {
            var fps = FPSCounter.Instance;
            switch (_kind)
            {
                case Kind.Blind:
                {
                    int bone = MuGame.Random.Next(bones);
                    if (SourceOwnerBones.IsDummy(Caster, bone))
                        return;
                    if (SourceOwnerBones.TryGet(Caster, bone, Vector3.Zero, out Vector3 position) && fps.RandFPSCheck(1))
                        SpawnBlind(position, factor);
                    if (SourceOwnerBones.TryGet(Caster, MuGame.Random.Next(bones), Vector3.Zero, out position) && fps.RandFPSCheck(1))
                        SpawnBlind(position, factor);
                    break;
                }
                case Kind.Sleep:
                {
                    int bone = MuGame.Random.Next(bones);
                    if (SourceOwnerBones.IsDummy(Caster, bone) || !SourceOwnerBones.TryGet(Caster, bone, Vector3.Zero, out Vector3 position))
                        return;
                    if (fps.RandFPSCheck(2) && fps.RandFPSCheck(1))
                    {
                        position.Z -= 20f;
                        SpawnWater(position, factor);
                    }
                    break;
                }
                default:
                {
                    // BITMAP_SHINY + 6 and BITMAP_PIN_LIGHT are separate emitters with their own random bone.
                    var relative = new Vector3(0f, 0f, 100f);
                    int bone = MuGame.Random.Next(bones);
                    if (!SourceOwnerBones.IsDummy(Caster, bone) && SourceOwnerBones.TryGet(Caster, bone, relative, out Vector3 position)
                        && fps.RandFPSCheck(2) && fps.RandFPSCheck(1))
                    {
                        position.Z -= 20f;
                        _sparkles!.SpawnShiny(position, _light, 0.5f, factor);
                    }
                    bone = MuGame.Random.Next(bones);
                    if (!SourceOwnerBones.IsDummy(Caster, bone) && SourceOwnerBones.TryGet(Caster, bone, relative, out position)
                        && fps.RandFPSCheck(2) && fps.RandFPSCheck(1))
                    {
                        position.Z -= 20f;
                        _sparkles!.SpawnPin(position, _light, 1f, factor);
                    }
                    break;
                }
            }
        }

        private bool TryNew(out int index)
        {
            Particle[] particles = _particles!;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].Live)
                    continue;
                particles[i] = default;
                particles[i].Live = true;
                _particleCount++;
                index = i;
                return true;
            }
            index = -1;
            return false;
        }

        private void SpawnBlind(Vector3 position, float factor)
        {
            if (!TryNew(out int i))
                return;
            ref Particle p = ref _particles![i];
            p.Position = position;
            p.LifeTime = 21f;
            p.Scale = 1.5f + (MuGame.Random.Next(4) + 8) * 0.01f;
            p.Gravity = 0.1f; // (rand() % 1 + 1) * 0.1
            p.TurningForce = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(2) + 60) * 0.1f, 0f),
                new Vector3(_angle.X, _angle.Y, MuGame.Random.Next(360)));
            p.TurningForce.Z += 2f * factor;
            p.Rotation = (int)FPSCounter.Instance.WorldTime % 360;
            p.Alpha = 1f;
            p.Light = _light;
        }

        private void MoveBlind(float factor)
        {
            if (_particleCount == 0)
                return;
            Particle[] particles = _particles!;
            float drag = MathF.Pow(0.6f, factor);
            for (int i = 0; i < particles.Length; i++)
            {
                ref Particle p = ref particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime > 0f)
                {
                    p.TurningForce.Z -= p.Gravity * factor;
                    p.Position += p.TurningForce * factor;
                    p.Scale += factor * 0.04f;
                    if (p.LifeTime < 10f)
                        p.Alpha -= factor * 0.1f;
                    if (p.LifeTime < 20f)
                    {
                        p.TurningForce.X *= drag;
                        p.TurningForce.Y *= drag;
                    }
                    p.Light = new Vector3(p.Alpha);
                    p.Rotation += 2f * factor;
                }
                if (p.LifeTime <= 0f || p.Alpha < 0f)
                {
                    p.Live = false;
                    _particleCount--;
                }
            }
        }

        private void SpawnWater(Vector3 position, float factor)
        {
            if (!TryNew(out int i))
                return;
            ref Particle p = ref _particles![i];
            p.LifeTime = 60 + MuGame.Random.Next(10);
            p.Scale = (MuGame.Random.Next(32) + 50) * 0.02f;
            p.Position = position + new Vector3(
                (MuGame.Random.Next(40) - 20) * factor,
                (MuGame.Random.Next(40) - 20) * factor,
                MuGame.Random.Next(40) * factor);
            p.Gravity = (MuGame.Random.Next(10) + 20) * 0.1f;
            p.Light = _light;
        }

        private void MoveWater(float factor)
        {
            if (_particleCount == 0)
                return;
            Particle[] particles = _particles!;
            float fade = MathF.Pow(1f / 1.02f, factor);
            for (int i = 0; i < particles.Length; i++)
            {
                ref Particle p = ref particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                p.Scale -= factor * 0.026f;
                // A sprite with a non-positive scale is not drawn; release the slot.
                if (p.LifeTime <= 0f || p.Scale <= 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }
                p.Position.Z += p.Gravity * factor;
                p.Light *= fade;
            }
        }

        protected override bool HasActiveContent() => !_stopped || _particleCount > 0 || (_sparkles?.HasLive ?? false);

        protected override void OnDraw()
        {
            if (_sparkles != null)
            {
                _sparkles.Draw(Batch, _shinyTexture, _lightTexture, _pinTexture);
                return;
            }

            if (_particleCount == 0)
                return;
            BlendState blend = _kind == Kind.Blind ? Blendings.Negative : Blendings.OneOneAdditive;
            if (!Batch.Begin(_texture, blend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            Particle[] particles = _particles!;
            for (int i = 0; i < particles.Length; i++)
            {
                ref Particle p = ref particles[i];
                if (p.Live)
                    Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
            Batch.End();
        }
    }
}
