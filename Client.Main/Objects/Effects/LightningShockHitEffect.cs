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
    // eDeBuff_LightningShockDamage: WSclient.cpp:15188; MODEL_LIGHTNING_SHOCK sub2:
    // ZzzEffect.cpp:939/4932/15152; MODEL_KNIGHT_PLANCRACK_A: ZzzObject.cpp:596.
    internal sealed class LightningShockHitEffect : SourceSkillEffectBase
    {
        private const int MaxParticles = 3000;

        private enum ParticleKind : byte { Light, Mega1, Mega2, Mega3, Smoke58, Smoke54 }

        private struct Particle
        {
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public Vector3 OriginalLight;
            public float Life;
            public float Scale;
            public float Rotation;
            public float Alpha;
            public float Gravity;
        }

        private struct Crack
        {
            public float Angle;
            public float Scale;
        }

        private readonly Vector3 _positionAtInsertion;
        private readonly Vector3 _angleAtInsertion;
        private readonly Crack[] _cracks = new Crack[3];
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly int[] _activeParticles = new int[MaxParticles];
        private readonly int[] _freeParticles = new int[MaxParticles];
        private readonly Texture2D?[] _megaTextures = new Texture2D?[3];
        private Texture2D? _lightTexture;
        private Texture2D? _smokeTexture;
        private int _activeCount;
        private int _freeCount = MaxParticles;
        private SourceModelRenderer? _renderer;
        private Vector3 _position;
        private float _life = 20f;
        private float _emitterLife = 15f;
        private float _alpha = 1f;

        public LightningShockHitEffect(WalkerObject target) : base(target, 0f, 0f, 1)
        {
            _positionAtInsertion = target.WorldPosition.Translation;
            _angleAtInsertion = target.Angle;
            for (int i = 0; i < _freeParticles.Length; i++)
                _freeParticles[i] = MaxParticles - 1 - i;
        }

        protected override async Task LoadEffectContent()
        {
            _renderer = await LoadModel("Effect/knight_plancrack_a.bmd");
            if (_renderer != null)
                _renderer.AnimationSpeed = 0f;
            _lightTexture = await LoadTexture("Effect/flare01.jpg");
            _smokeTexture = await LoadTexture("Effect/smoke01.jpg");
            _megaTextures[0] = await LoadTexture("Effect/lighting_mega01.jpg");
            _megaTextures[1] = await LoadTexture("Effect/lighting_mega02.jpg");
            _megaTextures[2] = await LoadTexture("Effect/lighting_mega03.jpg");
        }

        protected override void OnStart()
        {
            _position = _positionAtInsertion + Vector3.UnitZ * (10f * FPSCounter.Instance.FPS_ANIMATION_FACTOR);
            for (int i = 0; i < _cracks.Length; i++)
            {
                float inputScale = 0.5f + MuGame.Random.Next(4) * 0.1f;
                _cracks[i].Angle = MuGame.Random.Next(360);
                _cracks[i].Scale = inputScale + MuGame.Random.Next(10) * 0.05f;
            }
        }

        protected override void OnTick(float factor)
        {
            if (_life > 0f)
            {
                _alpha *= MathF.Pow(0.9f, factor);
                _life -= factor;
            }
            if (_emitterLife > 0f)
            {
                EmitParticles(factor);
                _emitterLife -= factor;
            }
            MoveParticles(factor);
        }

        private void EmitParticles(float factor)
        {
            var bones = Caster.Model?.Bones;
            Matrix[]? transforms = Caster.GetBoneTransforms();
            if (bones == null || transforms == null)
                return;

            int count = Math.Min(bones.Length, transforms.Length);
            Matrix world = Caster.WorldPosition;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = Vector3.Transform(transforms[i].Translation, world);
                if (FPSCounter.Instance.RandFPSCheck(60))
                {
                    float scale = 5f + (MuGame.Random.Next(20) - 10) * 0.1f;
                    if (FPSCounter.Instance.RandFPSCheck(1) && TryAllocate(out int index))
                    {
                        _particles[index] = new Particle { Kind = ParticleKind.Light, Position = position,
                            Scale = scale, Life = 50f, Light = new Vector3(0.8f, 0f, 0f) };
                    }
                }
                if (FPSCounter.Instance.RandFPSCheck(5))
                {
                    float scale = (MuGame.Random.Next(70) + 22) * 0.01f;
                    int texture = MuGame.Random.Next(3);
                    if (FPSCounter.Instance.RandFPSCheck(1) && TryAllocate(out int index))
                    {
                        _particles[index] = new Particle { Kind = (ParticleKind)((int)ParticleKind.Mega1 + texture),
                            Position = position, Scale = scale, Life = 5f, Alpha = 1f,
                            Rotation = MuGame.Random.Next(360), OriginalLight = new Vector3(1f, 0.8f, 0.4f) };
                    }
                }
            }

            if (FPSCounter.Instance.RandFPSCheck(1) && TryAllocate(out int smoke58))
            {
                float scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                Vector3 position = _positionAtInsertion + new Vector3(
                    (MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) - 32) * factor,
                    (MuGame.Random.Next(64) + 32) * factor);
                Vector3 angle = new(MuGame.Random.Next(90) - 45, MathHelper.ToDegrees(_angleAtInsertion.Y), MuGame.Random.Next(360));
                _particles[smoke58] = new Particle { Kind = ParticleKind.Smoke58, Position = position,
                    Angle = angle, Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f),
                    Scale = scale, Life = 50f, Light = Vector3.UnitX };
            }
            if (FPSCounter.Instance.RandFPSCheck(4) && FPSCounter.Instance.RandFPSCheck(1) && TryAllocate(out int smoke54))
            {
                _particles[smoke54] = new Particle { Kind = ParticleKind.Smoke54, Position = _positionAtInsertion,
                    Scale = 2.8f + MuGame.Random.Next(50) * 0.01f * factor, Life = 22f,
                    Rotation = MuGame.Random.Next(360), Gravity = (MuGame.Random.Next(30) + 50) * 0.05f,
                    Light = Vector3.UnitX };
            }
        }

        private bool TryAllocate(out int index)
        {
            if (_freeCount == 0)
            {
                index = -1;
                return false;
            }
            index = _freeParticles[--_freeCount];
            _activeParticles[_activeCount++] = index;
            return true;
        }

        private void MoveParticles(float factor)
        {
            for (int i = 0; i < _activeCount;)
            {
                int index = _activeParticles[i];
                ref Particle particle = ref _particles[index];
                particle.Life -= factor;
                if (particle.Life <= 0f)
                {
                    _activeParticles[i] = _activeParticles[--_activeCount];
                    _freeParticles[_freeCount++] = index;
                    continue;
                }

                switch (particle.Kind)
                {
                    case ParticleKind.Light:
                        particle.Light *= 0.9f; // per frame in the original
                        particle.Scale *= MathF.Pow(0.95f, factor);
                        break;
                    case ParticleKind.Mega1:
                    case ParticleKind.Mega2:
                    case ParticleKind.Mega3:
                        particle.Alpha -= 0.15f * factor;
                        particle.Light = particle.OriginalLight * particle.Alpha;
                        if (particle.Alpha < 0.1f)
                            particle.Life = 0f;
                        break;
                    case ParticleKind.Smoke58:
                        particle.Position += SourceJointMath.Rotate(particle.Velocity, particle.Angle) * factor;
                        particle.Light = Vector3.UnitX * (particle.Life / 50f);
                        particle.Velocity *= 0.4f; // per frame in the original
                        particle.Scale += 0.05f * factor;
                        break;
                    case ParticleKind.Smoke54:
                        particle.Light *= MathF.Pow(1f / 1.02f, factor);
                        if (particle.Light.X <= 0.05f)
                            particle.Life = 0f;
                        particle.Gravity -= 0.05f * factor;
                        particle.Position.Z += (particle.Scale + particle.Gravity) * 1.5f * factor;
                        particle.Scale += particle.Scale * factor / 100f;
                        break;
                }
                i++;
            }
        }

        protected override bool HasActiveContent() => _life > 0f || _emitterLife > 0f || _activeCount > 0;

        protected override void OnDraw()
        {
            if (_renderer != null && _life > 0f)
            {
                float scrollV = -((int)FPSCounter.Instance.WorldTime % 2000) * 0.0001f;
                _renderer.SetMeshScrollV(1, scrollV);
                Vector3 light = new Vector3(1f, 0.4f, 0.2f) * _alpha;
                _renderer.Light = light;
                _renderer.Update(LastGameTime);
                for (int i = 0; i < _cracks.Length; i++)
                {
                    Vector3 angle = new(_angleAtInsertion.X, _angleAtInsertion.Y, MathHelper.ToRadians(_cracks[i].Angle));
                    _renderer.DrawInstance(_position, angle, _cracks[i].Scale, light, 1f);
                }
            }
            DrawParticles(_lightTexture, ParticleKind.Light, Blendings.OneOneAdditive, SamplerState.LinearClamp);
            DrawParticles(_smokeTexture, ParticleKind.Smoke58, Blendings.OneOneAdditive, SamplerState.LinearClamp, includeSmoke54: true);
            for (int i = 0; i < _megaTextures.Length; i++)
                DrawParticles(_megaTextures[i], (ParticleKind)((int)ParticleKind.Mega1 + i), Blendings.OneOneAdditive, SamplerState.LinearWrap);
        }

        private void DrawParticles(Texture2D? texture, ParticleKind kind, BlendState blend, SamplerState sampler, bool includeSmoke54 = false)
        {
            if (!Batch.Begin(texture, blend, DepthStencilState.DepthRead, sampler))
                return;
            for (int i = 0; i < _activeCount; i++)
            {
                ref Particle particle = ref _particles[_activeParticles[i]];
                bool visible = particle.Life > 0f || particle.Kind == ParticleKind.Smoke54;
                if (visible && (particle.Kind == kind || (includeSmoke54 && particle.Kind == ParticleKind.Smoke54)))
                    Batch.AddSprite(particle.Position, particle.Scale, particle.Light, particle.Rotation);
            }
            Batch.End();
        }

        protected override void DisposeEffect() => _renderer?.Dispose();
    }
}
