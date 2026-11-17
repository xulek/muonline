#nullable enable
using System;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    // AT_SKILL_INVISIBLE 70: WSclient.cpp:4275, ZzzCharacter.cpp:2756/4094;
    // BITMAP_MAGIC+1 sub6/7: ZzzEffect.cpp:1224/8193/19154;
    // BITMAP_LIGHT sub8: ZzzEffectParticle.cpp:3192/3871/7974/8930.
    internal sealed class InvisibilityCastEffect : SourceSkillEffectBase
    {
        private const int MaxParticles = 3000;
        private const int MaxRings = 200;
        private const string LightTexturePath = "Effect/flare01.jpg";
        private const string GroundTexturePath = "Effect/Magic_Ground2.jpg";
        private const string SoundPath = "Sound/BattleCastle/sDTrans.wav";

        private struct Particle
        {
            public Vector3 Position;
            public float Scale;
            public float Life;
        }

        private struct Ring
        {
            public bool Active;
            public float Life;
            public float Angle;
        }

        private readonly WalkerObject _owner;
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly int[] _active = new int[MaxParticles];
        private readonly int[] _free = new int[MaxParticles];
        private readonly Ring[] _rings = new Ring[MaxRings];
        private int _activeCount;
        private int _freeCount = MaxParticles;
        private Texture2D? _lightTexture;
        private Texture2D? _groundTexture;
        private Vector3 _startPosition;
        private float _life = 60f;
        private float _originalAlpha;
        private float _lastOwnedAlpha;
        private bool _changedAlpha;
        private bool _alphaRecovered;
        private static readonly Vector3 CloakColor = new(1f, 0.5f, 0.25f);
        private float _age;

        public InvisibilityCastEffect(WalkerObject owner)
            : base(owner, 0f, 0f, 1024)
        {
            _owner = owner;
            for (int i = 0; i < _free.Length; i++)
                _free[i] = MaxParticles - 1 - i;
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
            _groundTexture = await LoadTexture(GroundTexturePath);
        }

        protected override void OnStart()
        {
            // The release deletes the elf support joints immediately, before the cloak status arrives.
            // The elf support orbit lives at the world root (BuffEffectController).
            var objects = World?.Objects;
            if (objects != null)
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    if (objects[i] is SpearSkillOrbitEffect { IsElfSupportOrbit: true } orbit && orbit.IsOwnedBy(_owner))
                        orbit.Stop();
                }
            }
            var children = _owner.Children;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] is SpearSkillOrbitEffect { IsElfSupportOrbit: true } orbit)
                    orbit.Stop();
            }
            _startPosition = _owner.WorldPosition.Translation;
            _originalAlpha = _owner.Alpha;
            _ = MuGame.Random.Next(50); // subtype 6 scale; it is not drawn
            if (World?.WorldIndex == 31)
                SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_life > 0f)
            {
                _ = MuGame.Random.Next(10); // BlendMeshLight; subtype 6 is not drawn
                SpawnParticles();
                _lastOwnedAlpha = _life / 80f;
                _owner.Alpha = _lastOwnedAlpha;
                _changedAlpha = true;
                _life -= factor;
            }
            else if (!_alphaRecovered)
            {
                float previous = _owner.Alpha;
                _lastOwnedAlpha = previous < _originalAlpha
                    ? MathF.Min(previous + 0.05f, _originalAlpha)
                    : MathF.Max(previous - 0.05f, _originalAlpha);
                _owner.Alpha = _lastOwnedAlpha;
                _alphaRecovered = _lastOwnedAlpha == _originalAlpha;
            }

            MoveParticles(factor);

            // Ember glow on the owner while the cloak particles stream off (60 frames + particle tail).
            _age += factor;
            Lights.Set(0, _owner.WorldPosition.Translation + Vector3.UnitZ * 100f, CloakColor, 240f,
                0.9f * EffectLights.Envelope(_age, 75f, 6f, 25f) * EffectLights.Flicker(_age * 0.04f, 6.1f, 0.12f));

            for (int i = 0; i < _rings.Length; i++)
            {
                if (!_rings[i].Active)
                    continue;
                _rings[i].Life -= factor;
                if (_rings[i].Life <= 0f)
                    _rings[i].Active = false;
            }
        }

        private void SpawnParticles()
        {
            var bones = _owner.Model?.Bones;
            Matrix[] transforms = _owner.GetBoneTransforms();
            if (bones == null || bones.Length == 0 || transforms == null)
                return;

            Matrix world = _owner.WorldPosition;
            Vector3 originOffset = _startPosition - world.Translation;
            for (int i = 0; i < _life; i++)
            {
                int bone = MuGame.Random.Next(bones.Length);
                if (bone >= transforms.Length || bones[bone] == BMDTextureBone.Dummy)
                    continue;
                Vector3 relative = new(MuGame.Random.Next(100) - 50,
                    MuGame.Random.Next(20) - 10, MuGame.Random.Next(20) - 10);
                Vector3 position = Vector3.Transform(relative, transforms[bone] * world) + originOffset;
                if (!FPSCounter.Instance.RandFPSCheck(1) || _freeCount == 0)
                    continue;
                int index = _free[--_freeCount];
                float life = MuGame.Random.Next(10) + 10f;
                _ = MuGame.Random.Next(10); // sub8 Gravity is initialized but unused
                _particles[index] = new Particle
                {
                    Position = position,
                    Life = life,
                    Scale = (MuGame.Random.Next(50) + 50f) / 100f
                };
                _active[_activeCount++] = index;
            }
        }

        private void MoveParticles(float factor)
        {
            Vector2 wind = SourceParticleWind.Current;
            for (int i = 0; i < _activeCount;)
            {
                int index = _active[i];
                ref Particle p = ref _particles[index];
                p.Life -= factor;
                p.Position.X += ((MuGame.Random.Next(2001) - 1000) * 0.0004f * factor + wind.X) * 0.5f;
                p.Position.Y += ((MuGame.Random.Next(2001) - 1000) * 0.0004f * factor + wind.Y) * 0.5f;
                p.Position.Z += 2.5f * factor;
                p.Scale -= 0.05f * factor;
                if (p.Life <= 0f || p.Scale <= 0.1f)
                {
                    _active[i] = _active[--_activeCount];
                    _free[_freeCount++] = index;
                }
                else
                    i++;
            }
        }

        private void SpawnRing()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Active)
                    continue;
                _rings[i] = new Ring { Active = true, Life = 40f, Angle = MuGame.Random.Next(360) };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_life > 0f || _activeCount > 0 || !_alphaRecovered)
                return true;
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i].Active)
                    return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_life > 0f && (int)_life % 25 == 0)
                SpawnRing();

            if (Batch.Begin(_groundTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Vector3 position = _owner.WorldPosition.Translation;
                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                for (int i = 0; i < _rings.Length; i++)
                {
                    ref Ring ring = ref _rings[i];
                    if (!ring.Active)
                        continue;
                    ring.Angle += 10f * factor;
                    float brightness = ring.Life < 5f ? 1f - (5f - ring.Life) * 0.2f
                        : ring.Life > 30f ? (40f - ring.Life) * 0.1f : 1f;
                    float size = ring.Life * 0.07f;
                    Batch.AddTerrainDecal(World?.Terrain, position.X, position.Y, size, size,
                        new Vector3(0.9f, 0.4f, 0.2f) * brightness, -ring.Angle);
                }
                Batch.End();
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _activeCount; i++)
                {
                    ref Particle p = ref _particles[_active[i]];
                    Batch.AddSprite(p.Position, p.Scale, new Vector3(p.Scale, p.Scale * 0.5f, p.Scale * 0.3f));
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            if (_changedAlpha && _owner.Status != GameControlStatus.Disposed && _owner.Alpha == _lastOwnedAlpha)
                _owner.Alpha = _originalAlpha;
        }
    }
}
