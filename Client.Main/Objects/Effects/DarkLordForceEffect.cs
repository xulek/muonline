#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dark Lord Force / Force Wave (AT_SKILL_FORCE 60 / AT_SKILL_FORCE_WAVE 66), SourceMain5.2:
    /// packet   WSclient.cpp:3967       PLAYER_ATTACK_STRIKE (ride / Fenrir variants), SOUND_SKILL_SWORD1 (packet map)
    /// gate     ZzzCharacter.cpp:2696   AnimationFrame &gt;= 3 in the strike actions
    /// release  ZzzCharacter.cpp:4918   2 x MODEL_WAVES sub 1, MODEL_PIERCING2 sub 0, SOUND_ATTACK_SPEAR
    /// waves    ZzzEffect.cpp:3292 (Z += 50F; sub 1: LifeTime 15, RENDER_NODEPTH, Z += 80F, 2 x BITMAP_PIERCING joints,
    ///          XY jitter * F; sub 2: Scale PKKey * 0.05, Z -= 50F), 12859 (Scale += Gravity, BlendMeshLight / 1.5^F)
    /// pierce   ZzzEffect.cpp:3350 (Scale 2, LifeTime 10, Direction (0,-60,0), Z += 130F), 12941 (Direction += 12F,
    ///          LifeTime &gt; 5: BlendMeshLight / 1.6^F + MODEL_WAVES sub 2 FpsChecked), epilogue MoveParticle(o, true)
    /// joint    ZzzEffectJoint.cpp:2405 (BITMAP_PIERCING sub 0: LifeTime 10, MaxTails 30, Velocity 20, no auto tails),
    ///          6442 (LifeTime &gt; 9: 30 x CreateTail + step; Velocity -= 2F; Light / 1.4^F)
    /// Models: BlendMesh -2 (all meshes additive with BodyLight = terrain light + o-&gt;Light), Velocity 0.3.
    /// </summary>
    internal sealed class DarkLordForceEffect : SourceSkillEffectBase
    {
        private const string WavesModelPath = "Skill/m_waves.bmd";       // MODEL_WAVES
        private const string PiercingModelPath = "Skill/m_Piercing.bmd"; // MODEL_PIERCING2
        private const string PiercingTexturePath = "Effect/Piercing.jpg"; // BITMAP_PIERCING, GL_REPEAT
        private const string HitSoundPath = "Sound/sDarkSpear.wav";      // SOUND_ATTACK_SPEAR
        private const float ModelVelocity = 0.3f;
        private const int CastWaveCount = 2;
        private const int BeamsPerWave = 2;
        private const int BeamCount = CastWaveCount * BeamsPerWave;
        private const int BeamMaxTails = 30;

        private struct Wave
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public float Scale;
            public float Gravity;
            public float BlendMeshLight;
            public float Frame;
            public float LifeTime;
        }

        private struct Beam
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly Wave[] _castWaves = new Wave[CastWaveCount];
        private readonly Wave[] _trailWaves = new Wave[32];
        private readonly Beam[] _beams = new Beam[BeamCount];

        private SourceModelRenderer? _wavesRenderer;
        private SourceModelRenderer? _wavesNoDepthRenderer;
        private SourceModelRenderer? _piercingRenderer;
        private Texture2D? _piercingTexture;

        private Vector3 _piercingPosition;
        private Vector3 _piercingAngle;
        private float _piercingDirectionY;
        private float _piercingLight;
        private float _piercingFrame;
        private float _piercingLife;

        public DarkLordForceEffect(WalkerObject caster)
            : base(caster, triggerFrame: 3f, triggerTimeoutSeconds: 14f * 0.04f,
                maxQuads: BeamCount * 200 * 2 + 8)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _wavesRenderer = await LoadModel(WavesModelPath);
            _wavesNoDepthRenderer = await LoadModel(WavesModelPath);
            if (_wavesNoDepthRenderer != null)
                _wavesNoDepthRenderer.DepthState = DepthStencilState.None;
            _piercingRenderer = await LoadModel(PiercingModelPath);
            foreach (var r in new[] { _wavesRenderer, _wavesNoDepthRenderer, _piercingRenderer })
                if (r != null)
                    r.AnimationSpeed = 0f;
            _piercingTexture = await LoadTexture(PiercingTexturePath);
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            Vector3 origin = CasterPosition;
            Vector3 casterAngle = new(0f, 0f, CasterYawDegrees);

            // 2 x MODEL_WAVES sub 1
            for (int i = 0; i < CastWaveCount; i++)
            {
                Vector3 position = origin;
                position.Z += 50f * factor + 80f * factor;
                var angle = new Vector3(90f, 0f, casterAngle.Z);

                for (int j = 0; j < BeamsPerWave; j++)
                    SpawnBeam(i * BeamsPerWave + j, position, angle, MuGame.Random.Next(40) + 70f);

                position.X += (MuGame.Random.Next(100) - 50f) * factor;
                position.Y += (MuGame.Random.Next(100) - 50f) * factor;
                _castWaves[i] = new Wave
                {
                    Live = true,
                    SubType = 1,
                    Position = position,
                    Angle = angle,
                    Scale = 0.1f + MuGame.Random.Next(50) / 100f,
                    Gravity = 0.01f,
                    BlendMeshLight = 1f,
                    LifeTime = 15f,
                };
            }

            // MODEL_PIERCING2 sub 0
            _piercingPosition = origin + Vector3.UnitZ * (130f * factor);
            _piercingAngle = casterAngle;
            _piercingDirectionY = -60f;
            _piercingLight = 1f;
            _piercingLife = 10f;

            SoundController.Instance.PlayBuffer(HitSoundPath); // SOUND_SKILL_SWORD1 was played at the packet
        }

        private void SpawnBeam(int index, Vector3 position, Vector3 waveAngle, float scale)
        {
            ref Beam b = ref _beams[index];
            int tails = SourceJointMath.ScaledMaxTails(BeamMaxTails);
            if (b.Trail == null || b.Trail.MaxTails != tails)
                b.Trail = new SourceTrail(tails);
            else
                b.Trail.Clear();
            b.Live = true;
            b.Position = position;
            b.Angle = new Vector3(-90f, 0f, waveAngle.Z);
            b.Scale = scale;
            b.Velocity = 20f;
            b.LifeTime = 10f;
            b.Light = FpsCheck(2, SourceJointMath.FrameFactor) ? new Vector3(1f, 0.8f, 0.6f) : Vector3.One;
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _castWaves.Length; i++)
                MoveWave(ref _castWaves[i], factor);
            for (int i = 0; i < _trailWaves.Length; i++)
                MoveWave(ref _trailWaves[i], factor);

            for (int i = 0; i < _beams.Length; i++)
            {
                ref Beam b = ref _beams[i];
                if (!b.Live)
                    continue;
                if (b.LifeTime > 9f)
                {
                    Vector3 step = SourceJointMath.Rotate(new Vector3(0f, 0f, -b.Velocity * factor), b.Angle) * factor;
                    for (int j = 0; j < BeamMaxTails; j++)
                    {
                        b.Trail.Push(b.Position, b.Angle, b.Scale);
                        b.Position += step;
                    }
                }
                b.Velocity -= factor * 2f;
                b.Light *= MathF.Pow(1f / 1.4f, factor);
                b.LifeTime -= factor;
                if (b.LifeTime <= 0f)
                    b.Live = false;
            }

            if (_piercingLife > 0f)
            {
                _piercingDirectionY = MathF.Min(0f, _piercingDirectionY + 12f * factor);
                if (_piercingLife > 5f)
                {
                    _piercingLight *= MathF.Pow(1f / 1.6f, factor);
                    if (FpsCheck(1, factor))
                        SpawnTrailWave(_piercingPosition, (int)_piercingLife);
                }

                // epilogue: PlayAnimation(Velocity), MoveParticle(o, true)
                _piercingFrame += ModelVelocity * factor;
                _piercingPosition += SourceJointMath.Rotate(new Vector3(0f, _piercingDirectionY, 0f), _piercingAngle) * factor;
                _piercingLife -= factor;
            }
        }

        private void SpawnTrailWave(Vector3 position, int pkKey)
        {
            for (int i = 0; i < _trailWaves.Length; i++)
            {
                ref Wave w = ref _trailWaves[i];
                if (w.Live)
                    continue;
                // MODEL_WAVES sub 2: Z += 50F then -= 50F.
                w = new Wave
                {
                    Live = true,
                    SubType = 2,
                    Position = position,
                    Angle = new Vector3(90f, 0f, _piercingAngle.Z),
                    Scale = pkKey * 0.05f,
                    Gravity = 0.01f,
                    BlendMeshLight = 1f,
                    LifeTime = 15f,
                };
                return;
            }
        }

        private static void MoveWave(ref Wave w, float factor)
        {
            if (!w.Live)
                return;

            w.Scale += w.Gravity * factor;
            if (w.SubType == 1)
            {
                w.Gravity += 0.07f * factor;
                if (w.Scale > 2f) w.Scale = 2f;
            }
            else
            {
                w.Gravity += 0.01f * factor;
                if (w.Scale > 1.5f) w.Scale = 1.5f;
            }
            w.BlendMeshLight *= MathF.Pow(1f / 1.5f, factor);
            w.Frame += ModelVelocity * factor;
            w.LifeTime -= factor;
            if (w.LifeTime <= 0f)
                w.Live = false;
        }

        protected override bool HasActiveContent()
        {
            if (_piercingLife > 0f)
                return true;
            for (int i = 0; i < _beams.Length; i++) if (_beams[i].Live) return true;
            for (int i = 0; i < _castWaves.Length; i++) if (_castWaves[i].Live) return true;
            for (int i = 0; i < _trailWaves.Length; i++) if (_trailWaves[i].Live) return true;
            return false;
        }

        private Vector3 BodyLight(Vector3 position)
        {
            // BodyLight: RequestTerrainLight + o->Light (the caster's light, (1,1,1)).
            Vector3 terrain = World?.Terrain?.EvaluateTerrainLight(position.X, position.Y) ?? Vector3.Zero;
            return terrain + Vector3.One;
        }

        private static float Quantize(float bml) => MathF.Round(bml * 50f) / 50f;

        protected override void OnDraw()
        {
            if (Batch.Begin(_piercingTexture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                for (int i = 0; i < _beams.Length; i++)
                {
                    ref Beam b = ref _beams[i];
                    if (b.Live)
                        Batch.AddTrail(b.Trail, SourceQuadBatch.TrailFaces.Both, b.Light, SourceQuadBatch.TrailShade.Flat, b.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_piercingRenderer != null && _piercingLife > 0f)
            {
                _piercingRenderer.SetAnimationTime(_piercingFrame);
                _piercingRenderer.Update(LastGameTime);
                _piercingRenderer.DrawInstance(_piercingPosition, new Vector3(0f, 0f, MathHelper.ToRadians(_piercingAngle.Z)), 2f,
                    BodyLight(_piercingPosition), Quantize(_piercingLight));
            }

            if (_wavesRenderer != null)
                for (int i = 0; i < _trailWaves.Length; i++)
                    DrawWave(_wavesRenderer, in _trailWaves[i]);

            if (_wavesNoDepthRenderer != null)
                for (int i = 0; i < _castWaves.Length; i++)
                    DrawWave(_wavesNoDepthRenderer, in _castWaves[i]);
        }

        private void DrawWave(SourceModelRenderer renderer, in Wave w)
        {
            if (!w.Live)
                return;
            renderer.SetAnimationTime(w.Frame);
            renderer.Update(LastGameTime);
            renderer.DrawInstance(w.Position,
                new Vector3(MathHelper.ToRadians(w.Angle.X), MathHelper.ToRadians(w.Angle.Y), MathHelper.ToRadians(w.Angle.Z)),
                w.Scale, BodyLight(w.Position), Quantize(w.BlendMeshLight));
        }

        protected override void DisposeEffect()
        {
            _wavesRenderer?.Dispose();
            _wavesNoDepthRenderer?.Dispose();
            _piercingRenderer?.Dispose();
            _wavesRenderer = null;
            _wavesNoDepthRenderer = null;
            _piercingRenderer = null;
        }
    }
}
