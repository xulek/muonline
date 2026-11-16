#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Energy Ball (AT_SKILL_ENERGYBALL = 17), SourceMain5.2:
    /// packet  WSclient.cpp:3926       SetPlayerMagic (others), AttackTime = 1
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: PLAYER_SKILL_HAND1/2, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4951   CreateEffect(BITMAP_ENERGY, o->Position, (0, 0, angle to target), o->Light, 0, to), SOUND_MAGIC
    /// energy  ZzzEffect.cpp:852 (init), 8809 (move), CheckTargetRange 245 (impact), epilogue MoveParticle(o, true)
    /// particles BITMAP_ENERGY 0 (ZzzEffectParticle.cpp:734/4970), BITMAP_SPARK+1 0/1 (2132/6641), default RenderSprite
    /// </summary>
    internal sealed class EnergyBallEffect : SourceSkillEffectBase
    {
        private const string EnergyTexturePath = "Effect/Thunder01.jpg"; // BITMAP_ENERGY
        private const string SparkTexturePath = "Effect/Spark03.jpg";    // BITMAP_SPARK + 1
        private const string SoundPath = "Sound/sMagic.wav";             // SOUND_MAGIC

        private const int SkillId = 17;
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private struct Particle
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly WalkerObject? _target;
        private readonly Particle[] _energies = new Particle[16];
        private readonly Particle[] _sparks = new Particle[32];

        private Texture2D? _energyTexture;
        private Texture2D? _sparkTexture;

        private bool _ballLive;
        private Vector3 _ballPosition;
        private Vector3 _ballAngle;
        private Vector3 _ballStep;
        private float _ballLifeTime;

        public EnergyBallEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 5f, ReleaseDelaySeconds, 128)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _energyTexture = await LoadTexture(EnergyTexturePath);
            _sparkTexture = await LoadTexture(SparkTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            float yaw = IsAlive(_target)
                ? SourceJointMath.CreateAngle2D(origin, _target!.WorldPosition.Translation)
                : CasterYawDegrees;

            _ballLive = true;
            _ballAngle = new Vector3(0f, 0f, yaw);
            _ballStep = SourceJointMath.Rotate(new Vector3(0f, -60f, 0f), _ballAngle);
            _ballPosition = origin + new Vector3(0f, 0f, 100f * SourceJointMath.FrameFactor);
            _ballLifeTime = 20f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_ballLive)
            {
                float luminosity = _ballLifeTime * 0.2f;
                var light = new Vector3(luminosity);
                if (FpsCheck(1, factor))
                    SpawnEnergy(light);
                if (FpsCheck(1, factor))
                    SpawnSpark(0, light, 4f);

                // CheckTargetRange: within 100 units (XY) of the target -> LifeTime = 1 and the impact burst.
                if (IsAlive(_target) && _target!.Visible)
                {
                    Vector3 t = _target.WorldPosition.Translation;
                    float dx = _ballPosition.X - t.X;
                    float dy = _ballPosition.Y - t.Y;
                    if (MathF.Sqrt(dx * dx + dy * dy) <= 100f)
                    {
                        _ballLifeTime = 1f;
                        if (FpsCheck(1, factor))
                            SpawnSpark(1, Vector3.One, 6f);
                    }
                }

                _ballPosition += _ballStep * factor;
                _ballLifeTime -= factor;
                if (_ballLifeTime <= 0f)
                    _ballLive = false;
            }

            for (int i = 0; i < _energies.Length; i++)
            {
                ref Particle p = ref _energies[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }
                p.Rotation += 20f * factor;
            }

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Particle p = ref _sparks[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                // MovePosition rotates the (already rotated) velocity by the particle angle.
                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Scale -= factor * (p.SubType == 1 ? 2f : 0.5f);
                if (p.Scale < 0.2f)
                    p.Live = false;
            }
        }

        private void SpawnEnergy(Vector3 light)
        {
            for (int i = 0; i < _energies.Length; i++)
            {
                ref Particle p = ref _energies[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = _ballPosition;
                p.Light = light;
                p.Scale = (MuGame.Random.Next(8) + 6) * 0.1f;
                p.Rotation = MuGame.Random.Next(360);
                p.LifeTime = 2f;
                return;
            }
        }

        private void SpawnSpark(int subType, Vector3 light, float scale)
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Particle p = ref _sparks[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.SubType = subType;
                p.Position = _ballPosition;
                p.Light = light;
                p.Scale = scale;
                p.LifeTime = 2f;
                p.Angle = _ballAngle;
                if (subType == 1)
                {
                    p.Angle = new Vector3(MuGame.Random.Next(360), MuGame.Random.Next(360), MuGame.Random.Next(360));
                    p.Velocity = SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), p.Angle);
                }
                return;
            }
        }

        private static bool IsAlive(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        protected override bool HasActiveContent()
        {
            if (_ballLive)
                return true;
            for (int i = 0; i < _energies.Length; i++) if (_energies[i].Live) return true;
            for (int i = 0; i < _sparks.Length; i++) if (_sparks[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_energyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _energies.Length; i++)
                {
                    ref Particle p = ref _energies[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }

            if (Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _sparks.Length; i++)
                {
                    ref Particle p = ref _sparks[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }
        }
    }
}
