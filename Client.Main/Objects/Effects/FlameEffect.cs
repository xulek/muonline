#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Flame (AT_SKILL_FLAME = 5), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4899   CreateEffect(BITMAP_FLAME, target xy + terrain height, o->Angle, o->Light, 5, o), SOUND_FLAME
    /// flame   ZzzEffect.cpp:1127 (init LifeTime 20), 8713 (move: BITMAP_FLAME particle), 19382 (Flame01 decal 2x2)
    /// area    ZzzCharacter.cpp:4327   0x1E path: BITMAP_FLAME sub 0 at the tile centre (LifeTime 40, 6 particles per tick,
    ///         rand_fps_check(8) MODEL_STONE sub 0, ZzzEffect.cpp:8631)
    /// particle ZzzEffectParticle.cpp:590 (sub 0 init), prologue MovePosition only, 9244 (RenderSprite)
    /// </summary>
    internal sealed class FlameEffect : SourceSkillEffectBase
    {
        private const string FlameTexturePath = "Effect/Flame01.jpg"; // BITMAP_FLAME
        private const string SoundPath = "Sound/sFlame.wav";          // SOUND_FLAME

        private const int SkillId = 5;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float EffectScale = 0.9f; // CreateEffect default Scale

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public float RiseZ;
            public float Scale;
            public float LifeTime;
        }

        private readonly Vector3 _targetPosition;
        private readonly bool _area;
        private readonly SourceStonePool _stones = new(16, 48);
        private readonly Particle[] _particles = new Particle[256];
        private Texture2D? _flameTexture;

        private bool _flameLive;
        private Vector3 _flamePosition;
        private float _flameAngle;
        private float _flameLifeTime;

        // Dynamic light (slot 0: ground fire glow).
        private static readonly Vector3 FireLightColor = new(1f, 0.5f, 0.18f);
        private float _lightAge;

        public FlameEffect(WalkerObject caster, Vector3 targetPosition, bool area)
            : base(caster, 5f, ReleaseDelaySeconds, 256)
        {
            _targetPosition = targetPosition;
            _area = area;
        }

        protected override async Task LoadEffectContent()
        {
            _flameTexture = await LoadTexture(FlameTexturePath);
            if (_area)
                await _stones.LoadAsync(World);
        }

        protected override void OnStart()
        {
            _flameLive = true;
            _flamePosition = new Vector3(_targetPosition.X, _targetPosition.Y, TerrainHeight(_targetPosition));
            _flameAngle = CasterYawDegrees;
            _flameLifeTime = _area ? 40f : 20f;
            if (Caster is MonsterObject monster)
                monster.PlayAttackSound(SoundPath, _flamePosition);
            else
                PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_flameLive)
            {
                if (_area)
                {
                    for (int j = 0; j < 6; j++)
                        if (FpsCheck(1, factor))
                            SpawnParticle(_flamePosition + new Vector3(MuGame.Random.Next(50) - 25, MuGame.Random.Next(50) - 25, 0f), 1f);
                    if (FpsCheck(8, factor) && FpsCheck(1, factor))
                        _stones.Spawn(_flamePosition, new Vector3(0f, 0f, _flameAngle), Vector3.One, 0);
                }
                else if (FpsCheck(1, factor))
                {
                    SpawnParticle(_flamePosition + new Vector3(MuGame.Random.Next(32) - 16, MuGame.Random.Next(32) - 16, 0f), EffectScale);
                }

                _flameLifeTime -= factor;
                if (_flameLifeTime <= 0f)
                    _flameLive = false;
            }

            UpdateLight(factor);
            _stones.Move(factor, World?.Terrain);

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                // MovePosition with (0, 0, Vz) rotated by a yaw-only angle; the sub 0 move case is empty.
                p.Position.Z += p.RiseZ * factor;
            }
        }

        private void UpdateLight(float factor)
        {
            _lightAge += factor;
            // Flame life plus most of the rising-particle tail, in 25 Hz ticks.
            float duration = (_area ? 40f : 20f) + 16f;
            float k = EffectLights.Envelope(_lightAge, duration, 3f, 16f);
            if (k <= 0f)
            {
                Lights.Hide(0);
                return;
            }

            float intensity = (_area ? 1.3f : 1.1f) * k * EffectLights.Flicker(_lightAge * 0.04f, 0.9f, 0.2f);
            Lights.Set(0, _flamePosition + new Vector3(0f, 0f, 60f), FireLightColor, _area ? 320f : 260f, intensity);
        }

        private void SpawnParticle(Vector3 position, float scale)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.LifeTime = 20f;
                p.RiseZ = (MuGame.Random.Next(128) + 128) * 0.15f;
                p.Scale = scale * (MuGame.Random.Next(64) + 64) * 0.01f;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_flameLive || _stones.HasLive)
                return true;
            for (int i = 0; i < _particles.Length; i++) if (_particles[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            _stones.DrawModels(LastGameTime, World?.Terrain);
            _stones.DrawFires(Batch);

            if (!Batch.Begin(_flameTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            // RenderEffectShadows: Flame01 decal 2 x 2 tiles, Luminosity (rand % 4 + 8) * 0.1 per frame.
            if (_flameLive)
            {
                float luminosity = (MuGame.Random.Next(4) + 8) * 0.1f;
                Batch.AddTerrainDecal(World?.Terrain, _flamePosition.X, _flamePosition.Y, 2f, 2f, new Vector3(luminosity), -_flameAngle);
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    Batch.AddSprite(p.Position, p.Scale, Vector3.One);
            }
            Batch.End();
        }
    
        protected override void DisposeEffect()
        {
            _stones.Dispose();
        }
    }
}
