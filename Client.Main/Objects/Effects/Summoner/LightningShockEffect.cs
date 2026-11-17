#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    // MODEL_LIGHTNING_SHOCK sub 0/1: ZzzEffect.cpp:939, 9302; children in
    // ZzzEffect.cpp:1181/3722/4932 and ZzzEffectParticle.cpp:767/5018.
    internal sealed class LightningShockEffect : SourceSkillEffectBase
    {
        private enum Kind : byte { Magic, Mega1, Mega2, Mega3, Smoke58, Smoke54 }
        private struct Particle
        {
            public Kind Kind;
            public Vector3 Position, Angle, Velocity, Light, OriginalLight;
            public float Life, Scale, Rotation, Alpha, Gravity;
        }
        private struct Crack { public float Angle, Scale; }
        private const int MaxParticles = 3000;
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly int[] _active = new int[MaxParticles];
        private readonly int[] _free = new int[MaxParticles];
        private readonly Vector3[] _flarePositions = new Vector3[11];
        private readonly Vector3[] _marks = new Vector3[5];
        private readonly Crack[] _cracks = new Crack[3];
        private readonly Texture2D?[] _mega = new Texture2D?[3];
        private readonly Vector3 _initialPosition;
        private readonly Vector3 _initialAngle;
        private int _count, _freeCount = MaxParticles;
        private Texture2D? _magic, _shiny, _pin, _light, _smoke, _damage;
        private SourceModelRenderer? _crackRenderer;
        private Vector3 _orb, _spriteOrb, _impact, _crackPosition;
        private float _yaw, _orbLife = 20f, _gravity = 1f, _velocity;
        private float _impactLife, _crackLife, _crackAlpha = 1f;
        private float _markLife, _markScale, _markAlpha = 1f, _markLight = 1f;
        private float _flashLife, _flashScale = .1f, _flashLight = 1f;
        private float _rotation, _pinRotation1, _pinRotation2;
        private bool _orbAlive = true, _spritesVisible;
        private static readonly Vector3 OrbGlow = new(1f, 0.4f, 0.35f);
        private static readonly Vector3 ImpactGlow = new(1f, 0.6f, 0.3f);
        private Vector3 _orbLightPosition;
        private float _orbGlow, _impactAge = -1f;

        public LightningShockEffect(WalkerObject caster) : base(caster, 0f, 0f, 4096)
        {
            _initialPosition = caster.WorldPosition.Translation;
            _initialAngle = caster.Angle;
            for (int i = 0; i < MaxParticles; i++) _free[i] = MaxParticles - 1 - i;
        }

        protected override async Task LoadEffectContent()
        {
            _crackRenderer = await LoadModel("Effect/knight_plancrack_a.bmd");
            if (_crackRenderer != null) _crackRenderer.AnimationSpeed = 0f;
            _magic = await LoadTexture("Effect/Magic_Ground1.jpg");
            _shiny = await LoadTexture("Effect/Shiny02.jpg");
            _pin = await LoadTexture("Effect/pin_lights.jpg");
            _light = await LoadTexture("Effect/flare01.jpg");
            _smoke = await LoadTexture("Effect/smoke01.jpg");
            _damage = await LoadTexture("Effect/damage01mono.jpg");
            _mega[0] = await LoadTexture("Effect/lighting_mega01.jpg");
            _mega[1] = await LoadTexture("Effect/lighting_mega02.jpg");
            _mega[2] = await LoadTexture("Effect/lighting_mega03.jpg");
        }

        protected override void OnStart()
        {
            _yaw = MathHelper.ToDegrees(_initialAngle.Z);
            _orb = _initialPosition + Vector3.UnitZ * (280f * FPSCounter.Instance.FPS_ANIMATION_FACTOR);
            PlayUnmappedSound(230, "Sound/lightning_shock.wav");
        }

        protected override void OnTick(float factor)
        {
            _spritesVisible = false;
            bool hadImpact = _markLife > 0f;
            if (_orbAlive) TickOrb(factor);
            else if (_impactLife > 0f) { EmitImpact(factor); _impactLife -= factor; }
            if (hadImpact && _crackLife > 0f) { _crackAlpha *= MathF.Pow(.9f, factor); _crackLife -= factor; }
            if (hadImpact && _flashLife > 0f)
            {
                _flashScale += .5f * factor;
                if (_flashScale > 3.5f) _flashLight *= MathF.Pow(.5f, factor);
                _flashLife -= factor;
            }
            if (hadImpact && _markLife > 0f)
            {
                _markAlpha = _markAlpha > 0f ? _markAlpha - .05f * factor : 0f;
                _markScale += _markAlpha * factor;
                if (_markScale > 5f) _markLight *= MathF.Pow(.5f, factor);
                _markLife -= factor;
            }
            MoveParticles(factor);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            float time = (float)(FPSCounter.Instance.WorldTime * 0.001);
            // Slot 0: falling orb.
            if (_orbAlive)
            {
                _orbLightPosition = _orb;
                _orbGlow = MathF.Min(1f, _orbGlow + 0.34f * factor);
            }
            else
            {
                _orbGlow = MathF.Max(0f, _orbGlow - 0.34f * factor);
            }
            if (_orbGlow > 0f)
                Lights.Set(0, _orbLightPosition, OrbGlow, 230f, 1.2f * _orbGlow * EffectLights.Flicker(time, 0.3f, 0.3f));
            else
                Lights.Hide(0);

            // Slot 1: ground impact flash.
            if (_impactAge >= 0f)
            {
                _impactAge += factor;
                float flash = EffectLights.Flash(_impactAge, 16f, 1.4f);
                if (flash > 0f)
                    Lights.Set(1, _impact + new Vector3(0f, 0f, 60f), ImpactGlow, 260f + 180f * flash,
                        2.2f * flash * EffectLights.Flicker(time, 5.1f, 0.35f));
                else
                    Lights.Hide(1);
            }
        }

        private void TickOrb(float factor)
        {
            Vector3 direction = Vector3.Zero;
            if (Caster.CurrentFrame > 6f || _orbLife < 15f)
            {
                direction = new Vector3(0f, -20f, -75f - _velocity);
                _gravity += .1f * factor;
                _velocity += _gravity * factor;
            }
            _spriteOrb = _orb;
            _spritesVisible = true;
            _rotation = (float)FPSCounter.Instance.WorldTime * .0006f * 360f;
            _pinRotation1 = MuGame.Random.Next(360);
            _pinRotation2 = MuGame.Random.Next(360);
            if (FPSCounter.Instance.RandFPSCheck(1)) AddParticle(new Particle { Kind = Kind.Magic,
                Position = _orb, Light = new Vector3(1f, .3f, .3f), Scale = .8f, Life = 10f });
            for (int i = 0; i < _flarePositions.Length; i++)
            {
                float scale = (MuGame.Random.Next(80) + 32) * .01f;
                Vector3 pos = _orb + new Vector3(MuGame.Random.Next(70) - 35, MuGame.Random.Next(70) - 35, MuGame.Random.Next(70) - 35);
                _flarePositions[i] = pos;
                if (MuGame.Random.Next(3) == 0) SpawnMega(pos, new Vector3(1f, .7f, .4f), scale);
            }
            var transforms = Caster.GetBoneTransforms();
            for (int i = 0; i < 2; i++)
            {
                float scale = (MuGame.Random.Next(60) + 22) * .01f;
                int bone = MuGame.Random.Next(41);
                Vector3 offset = new(MuGame.Random.Next(30) - 15, MuGame.Random.Next(30) - 15, MuGame.Random.Next(30) - 15);
                if (transforms != null && bone < transforms.Length)
                {
                    Vector3 pos = Vector3.Transform(Vector3.Transform(offset, transforms[bone]), Caster.WorldPosition);
                    SpawnMega(pos, new Vector3(1f, .5f, .4f), scale);
                }
            }
            if (_orb.Z < TerrainHeight(_orb))
            {
                _orbAlive = false;
                if (FPSCounter.Instance.RandFPSCheck(1)) StartImpact(factor);
            }
            else
            {
                _orb += SourceJointMath.Rotate(direction, new Vector3(
                    MathHelper.ToDegrees(_initialAngle.X), MathHelper.ToDegrees(_initialAngle.Y), _yaw)) * factor;
                _orbLife -= factor;
                if (_orbLife <= 0f) _orbAlive = false;
            }
        }

        private void StartImpact(float factor)
        {
            _impact = _orb;
            _impactAge = 0f;
            _impactLife = 12f;
            _flashLife = 10f;
            _markLife = _crackLife = 20f;
            _crackPosition = new Vector3(_orb.X, _orb.Y, TerrainHeight(_orb) + 10f + 10f * factor);
            for (int i = 0; i < _marks.Length; i++)
                _marks[i] = _orb + SourceJointMath.Rotate(new Vector3(0f, 150f, 0f), new Vector3(0f, 0f, _yaw + i * 72f));
            for (int i = 0; i < _cracks.Length; i++)
            {
                float scale = 1f + MuGame.Random.Next(4) * .1f;
                _cracks[i].Angle = MuGame.Random.Next(360);
                _cracks[i].Scale = scale + MuGame.Random.Next(10) * .05f;
            }
        }

        private void EmitImpact(float factor)
        {
            for (int i = 0; i < 11; i++)
            {
                float scale = (MuGame.Random.Next(80) + 32) * .01f;
                Vector3 pos = _impact + new Vector3(MuGame.Random.Next(70) - 35, MuGame.Random.Next(70) - 35, MuGame.Random.Next(70) - 35);
                SpawnMega(pos, new Vector3(1f, .7f, .4f), scale);
            }
            for (int i = 0; i < 6; i++)
            {
                float scale = (MuGame.Random.Next(60) + 22) * .01f;
                Vector3 pos = _impact + SourceJointMath.Rotate(new Vector3(0f, MuGame.Random.Next(400), 0f), new Vector3(0f, 0f, _yaw + MuGame.Random.Next(360)));
                pos.Z = TerrainHeight(pos) + 20f;
                SpawnMega(pos, Vector3.UnitX, scale);
            }
            Vector3 smokePos = new(_impact.X, _impact.Y, TerrainHeight(_impact) + 10f);
            for (int i = 0; i < 2; i++) SpawnSmoke58(smokePos, factor);
            if (FPSCounter.Instance.RandFPSCheck(2) && FPSCounter.Instance.RandFPSCheck(1))
                AddParticle(new Particle { Kind = Kind.Smoke54, Position = smokePos, Scale = 2.8f + MuGame.Random.Next(50) * .01f * factor,
                    Life = 22f, Rotation = MuGame.Random.Next(360), Gravity = (MuGame.Random.Next(30) + 50) * .05f, Light = Vector3.UnitX });
            // MODEL_STONE1/2 sub13 has zero scale (default CreateEffect Scale) and no child spawns.
        }

        private void SpawnMega(Vector3 position, Vector3 light, float scale)
        {
            var kind = (Kind)((int)Kind.Mega1 + MuGame.Random.Next(3));
            if (FPSCounter.Instance.RandFPSCheck(1))
                AddParticle(new Particle { Kind = kind, Position = position, OriginalLight = light, Scale = scale,
                    Life = 5f, Alpha = 1f, Rotation = MuGame.Random.Next(360) });
        }

        private void SpawnSmoke58(Vector3 position, float factor)
        {
            if (!FPSCounter.Instance.RandFPSCheck(1)) return;
            float scale = (MuGame.Random.Next(32) + 80) * .01f;
            position += new Vector3((MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) + 32) * factor);
            AddParticle(new Particle { Kind = Kind.Smoke58, Position = position,
                Angle = new Vector3(MuGame.Random.Next(90) - 45, MathHelper.ToDegrees(_initialAngle.Y), MuGame.Random.Next(360)),
                Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f), Scale = scale, Life = 50f, Light = Vector3.UnitX });
        }

        private void AddParticle(Particle particle)
        {
            if (_freeCount == 0) return;
            int index = _free[--_freeCount];
            _particles[index] = particle;
            _active[_count++] = index;
        }

        private void MoveParticles(float factor)
        {
            for (int i = 0; i < _count;)
            {
                int index = _active[i];
                ref Particle p = ref _particles[index];
                p.Life -= factor;
                if (p.Life <= 0f) { _active[i] = _active[--_count]; _free[_freeCount++] = index; continue; }
                switch (p.Kind)
                {
                    case Kind.Magic: p.Scale -= .05f * factor; p.Light -= Vector3.One * (.01f * factor); break;
                    case Kind.Mega1: case Kind.Mega2: case Kind.Mega3:
                        p.Alpha -= .15f * factor; p.Light = p.OriginalLight * p.Alpha;
                        if (p.Alpha < .1f) p.Life = 0f;
                        break;
                    case Kind.Smoke58:
                        p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                        p.Light = Vector3.UnitX * (p.Life / 50f); p.Velocity *= .4f; p.Scale += .05f * factor; break;
                    case Kind.Smoke54:
                        p.Light *= MathF.Pow(1f / 1.02f, factor);
                        if (p.Light.X <= .05f) p.Life = 0f;
                        p.Gravity -= .05f * factor; p.Position.Z += (p.Scale + p.Gravity) * 1.5f * factor;
                        p.Scale += p.Scale * factor / 100f; break;
                }
                i++;
            }
        }

        protected override bool HasActiveContent() => _orbAlive || _spritesVisible || _orbGlow > 0f || _impactLife > 0f || _crackLife > 0f || _flashLife > 0f || _markLife > 0f || _count > 0;

        protected override void OnDraw()
        {
            if (_crackRenderer != null && _crackLife > 0f)
            {
                _crackRenderer.SetMeshScrollV(1, -((int)FPSCounter.Instance.WorldTime % 2000) * .0001f);
                Vector3 light = new Vector3(1f, .4f, .2f) * _crackAlpha;
                _crackRenderer.Light = light; _crackRenderer.Update(LastGameTime);
                for (int i = 0; i < _cracks.Length; i++)
                    _crackRenderer.DrawInstance(_crackPosition, new Vector3(_initialAngle.X, _initialAngle.Y, MathHelper.ToRadians(_cracks[i].Angle)), _cracks[i].Scale, light, 1f);
            }
            if (Batch.Begin(_damage, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_flashLife > 0f)
                {
                    Batch.AddTerrainDecal(World?.Terrain, _impact.X, _impact.Y, _flashScale, _flashScale, new Vector3(1f, .8f, .5f) * _flashLight, -_yaw);
                    Batch.AddTerrainDecal(World?.Terrain, _impact.X, _impact.Y, _flashScale, _flashScale, Vector3.UnitX * _flashLight, -_yaw);
                }
                Batch.End();
            }
            if (Batch.Begin(_magic, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_markLife > 0f) for (int i = 0; i < _marks.Length; i++) for (int j = 0; j < 2; j++)
                    Batch.AddTerrainDecal(World?.Terrain, _marks[i].X, _marks[i].Y, _markScale, _markScale, new Vector3(1f, .2f, .05f) * _markLight, -_yaw);
                if (_spritesVisible)
                {
                    Batch.AddSprite(_spriteOrb, .8f, new Vector3(1f, .2f, .2f), _rotation);
                    Batch.AddSprite(_spriteOrb, .4f, new Vector3(1f, .2f, .2f), -_rotation);
                }
                AddParticles(Kind.Magic); Batch.End();
            }
            if (Batch.Begin(_shiny, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_spritesVisible) { Batch.AddSprite(_spriteOrb, 3.2f, new Vector3(1f, .4f, .4f), _rotation); Batch.AddSprite(_spriteOrb, 2.4f, new Vector3(1f, .4f, .4f), -_rotation); }
                Batch.End();
            }
            if (Batch.Begin(_pin, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_spritesVisible) { Batch.AddSprite(_spriteOrb, 1.6f, new Vector3(1f, .4f, .4f), _pinRotation1); Batch.AddSprite(_spriteOrb, 1.6f, new Vector3(1f, .4f, .4f), _pinRotation2); }
                Batch.End();
            }
            if (Batch.Begin(_light, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_spritesVisible) for (int i = 0; i < _flarePositions.Length; i++) Batch.AddSprite(_flarePositions[i], 2.2f, new Vector3(1f, .2f, .1f));
                Batch.End();
            }
            if (Batch.Begin(_smoke, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            { AddParticles(Kind.Smoke58); AddParticles(Kind.Smoke54); Batch.End(); }
            for (int i = 0; i < _mega.Length; i++)
                if (Batch.Begin(_mega[i], Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
                { AddParticles((Kind)((int)Kind.Mega1 + i)); Batch.End(); }
        }

        private void AddParticles(Kind kind)
        {
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[_active[i]];
                if (p.Kind == kind && (p.Life > 0f || kind == Kind.Smoke54)) Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
        }
        protected override void DisposeEffect() => _crackRenderer?.Dispose();
    }
}
