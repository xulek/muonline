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
    /// Book summon damage-over-time visuals on the target (eDeBuff_SahamuttDOT = 75, eDeBuff_NeilDOT = 74), SourceMain5.2:
    /// spawn    WSclient.cpp:15110 / ZzzCharacter.cpp:10733 -> SummonSystem.cpp:360 CreateDamageOfTimeEffect
    ///          (MODEL_SUMMONER_EQUIP_HEAD_SAHAMUTT / _NEIL sub 1, LifeTime 100000, ZzzEffect.cpp:720 / 737), not rendered (18252)
    /// Sahamutt ZzzEffect.cpp:7749 rand_fps_check(2): CreateEffectFpsChecked(BITMAP_FIRE_CURSEDLICH sub 0, Skill = rand % NumBones)
    ///          fire effect ZzzEffect.cpp:656 (LifeTime 10), 7551 (follows the bone, one FIRE_CURSEDLICH sub 3 particle per frame,
    ///          Light Alpha * 0.3); particle ZzzEffectParticle.cpp:324 (LifeTime rand%12+16, Scale (rand%30+20)*0.02,
    ///          Rotation rand%360, Gravity (rand%15+15)*0.02), 4370 (Scale -= 0.03F, Z += Gravity*10F, follows the target), 9094
    /// Neil     ZzzEffect.cpp:7808 one BITMAP_LIGHT+2 sub 5 particle per frame; ZzzEffectParticle.cpp:869 (BITMAP_ADV_SMOKE,
    ///          random bone, LifeTime 21, Scale (rand%4+8)*0.01*F, Gravity 0.4, TurningForce (0,(rand%2+60)*0.1,0) at yaw rand%360,
    ///          Z += 4F, Rotation WorldTime%360), 4048 (move, Light (a, 0, 0.6a)), 9182 (default sprite)
    /// end      RemoveDamageOfTimeEffect deletes only the emitter; live fires and particles finish.
    /// </summary>
    internal sealed class SummonDotEffect : SourceSkillEffectBase
    {
        private const string FireTexturePath = "Effect/firehik02.jpg"; // BITMAP_FIRE_CURSEDLICH
        private const string SmokeTexturePath = "Effect/fi01.jpg";     // BITMAP_ADV_SMOKE
        private const int MaxEmitters = 24;
        private const int MaxFireParticles = 384;
        private const int MaxNeilParticles = 48;

        private struct Emitter
        {
            public bool Live;
            public int Bone;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TurningForce;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
            public float Alpha;
        }

        private readonly bool _neil;
        private readonly Emitter[]? _emitters;
        private readonly Particle[] _particles;
        private Texture2D? _texture;
        private Vector3 _targetPosition;
        private int _emitterCount;
        private int _particleCount;
        private bool _stopped;

        public SummonDotEffect(WalkerObject target, bool neil)
            : base(target, 0f, 0f, neil ? MaxNeilParticles : MaxFireParticles)
        {
            _neil = neil;
            _particles = new Particle[neil ? MaxNeilParticles : MaxFireParticles];
            if (!neil)
                _emitters = new Emitter[MaxEmitters];
        }

        /// <summary>Debuff ended: DeleteEffect removes the emitter only.</summary>
        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            _texture = await LoadTexture(_neil ? SmokeTexturePath : FireTexturePath);
        }

        protected override void OnStart()
        {
            _targetPosition = CasterPosition;
        }

        protected override void OnTick(float factor)
        {
            Vector3 target = CasterPosition;
            Position = target;
            Matrix[]? bones = Caster.GetBoneTransforms();
            bool hasBones = bones != null && bones.Length > 0;
            var fps = FPSCounter.Instance;

            // MoveEffects (emitters) runs before MoveParticles.
            if (_neil)
            {
                if (!_stopped && hasBones && fps.RandFPSCheck(1))
                    SpawnNeil(BonePosition(bones!, MuGame.Random.Next(bones!.Length)), factor);
                MoveNeil(factor, target);
            }
            else
            {
                if (!_stopped && hasBones && fps.RandFPSCheck(2) && fps.RandFPSCheck(1))
                    SpawnEmitter(MuGame.Random.Next(bones!.Length));
                MoveEmitters(factor, bones);
                MoveFires(factor, target);
            }

            _targetPosition = target;
        }

        // TransformPosition(BoneTransform[bone], 0, p, false) * BodyScale + Owner->Position.
        private Vector3 BonePosition(Matrix[] bones, int bone)
        {
            SourceOwnerBones.TryGet(Caster, bone < bones.Length ? bone : 0, Vector3.Zero, out Vector3 position);
            return position;
        }

        private void SpawnEmitter(int bone)
        {
            Emitter[] emitters = _emitters!;
            for (int i = 0; i < emitters.Length; i++)
            {
                ref Emitter e = ref emitters[i];
                if (e.Live)
                    continue;
                e.Live = true;
                e.Bone = bone;
                e.LifeTime = 10f;
                _emitterCount++;
                return;
            }
        }

        private void MoveEmitters(float factor, Matrix[]? bones)
        {
            if (_emitterCount == 0)
                return;
            Emitter[] emitters = _emitters!;
            bool hasBones = bones != null && bones.Length > 0;
            for (int i = 0; i < emitters.Length; i++)
            {
                ref Emitter e = ref emitters[i];
                if (!e.Live)
                    continue;
                if (hasBones && FPSCounter.Instance.RandFPSCheck(1))
                    SpawnFire(BonePosition(bones!, e.Bone));
                e.LifeTime -= factor;
                if (e.LifeTime <= 0f)
                {
                    e.Live = false;
                    _emitterCount--;
                }
            }
        }

        private void SpawnFire(Vector3 position)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;
                p.Live = true;
                p.Position = position;
                p.LifeTime = MuGame.Random.Next(12) + 16;
                p.Scale = (MuGame.Random.Next(30) + 20) * 0.02f;
                p.Rotation = MuGame.Random.Next(360);
                p.Gravity = (MuGame.Random.Next(15) + 15) * 0.02f;
                _particleCount++;
                return;
            }
        }

        private void MoveFires(float factor, Vector3 target)
        {
            if (_particleCount == 0)
                return;
            Vector3 follow = target - _targetPosition;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                p.Scale -= 0.03f * factor;
                if (p.LifeTime <= 0f || p.Scale < 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }
                p.Position.Z += p.Gravity * 10f * factor;
                p.Position += follow;
            }
        }

        private void SpawnNeil(Vector3 position, float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;
                p.Live = true;
                p.Position = position;
                p.LifeTime = 21f;
                p.Scale = (MuGame.Random.Next(4) + 8) * 0.01f * factor;
                p.Gravity = 0.4f; // (rand() % 1 + 4) * 0.1
                // Angle = target angle with Angle[2] = rand() % 360.
                var angle = new Vector3(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MuGame.Random.Next(360));
                p.TurningForce = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(2) + 60) * 0.1f, 0f), angle);
                p.TurningForce.Z += 4f * factor;
                p.Rotation = (int)FPSCounter.Instance.WorldTime % 360;
                p.Alpha = 1f;
                _particleCount++;
                return;
            }
        }

        private void MoveNeil(float factor, Vector3 target)
        {
            if (_particleCount == 0)
                return;
            Vector3 follow = target - _targetPosition;
            float drag = MathF.Pow(0.6f, factor);
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }
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
                if (p.Alpha < 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }
                p.Position += follow;
                p.Rotation += factor;
            }
        }

        protected override bool HasActiveContent() => !_stopped || _emitterCount > 0 || _particleCount > 0;

        protected override void OnDraw()
        {
            if (_particleCount == 0 || Caster.Hidden)
                return;
            if (!Batch.Begin(_texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            var fireLight = new Vector3(0.3f);
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                Batch.AddSprite(p.Position, p.Scale, _neil ? new Vector3(p.Alpha, 0f, p.Alpha * 0.6f) : fireLight, p.Rotation);
            }
            Batch.End();
        }
    }
}
