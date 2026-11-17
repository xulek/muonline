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
    /// <summary>
    /// Fire Breath (AT_SKILL_RIDER = 49), SourceMain5.2:
    /// packet  WSclient.cpp:4475       PLAYER_SKILL_RIDER (_FLY in Tarkan / Heaven / Maya), AttackTime = 1
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 -> AttackTime = 15 (else 14 ticks)
    /// release ZzzCharacter.cpp:4236   CreateEffect(BITMAP_SHOTGUN), SOUND_SKILL_SWORD3
    /// shotgun ZzzEffect.cpp:3105 (init, 40 x BITMAP_JOINT_SPARK sub 1), 12645 (move, BITMAP_FIRE+2 sub 10, CreateBomb2),
    ///         epilogue MoveParticle(o, true); not rendered
    /// fire    ZzzEffectParticle.cpp:447 (init), 4721 (move default), 9112 (render 4x1 sheet)
    /// bomb    ZzzEffect.cpp:6470 CreateBomb2: 20 x BITMAP_SPARK sub 2, BITMAP_EXPLOTION+1 (2511, 4309, 8997)
    /// </summary>
    internal sealed class FireBreathEffect : SourceSkillEffectBase
    {
        private const string FireTexturePath = "Effect/Fire03.jpg";      // BITMAP_FIRE + 2 (4x1)
        private const string ExplosionTexturePath = "Effect/DinoE.jpg";  // BITMAP_EXPLOTION + 1 (4x1)
        private const string SoundPath = "Sound/sKnightSkill3.wav";      // SOUND_SKILL_SWORD3
        private const string ExplosionSound = "Sound/eExplosion.wav";    // SOUND_EXPLOTION01

        private const int SkillId = 49;
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Particle[] _fires = new Particle[48];
        private readonly Particle[] _explosions = new Particle[8];
        private readonly SourceSparkPool _sparkPool = new(48, 192);

        private Texture2D? _fireTexture;
        private Texture2D? _explosionTexture;
        private Texture2D? _sparkJointTexture;
        private Texture2D? _sparkTexture;

        private bool _shotLive;
        private Vector3 _shotPosition;
        private Vector3 _shotAngle;
        private float _shotLifeTime;

        // Dynamic lights (slot 0: fire breath head, slot 1: CreateBomb2 explosion).
        private static readonly Vector3 FireLightColor = new(1f, 0.5f, 0.18f);
        private float _shotAge;
        private bool _bombLight;
        private float _bombAge;
        private Vector3 _bombPosition;

        public FireBreathEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 512)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _fireTexture = await LoadTexture(FireTexturePath);
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _sparkJointTexture = await LoadTexture(SourceSparkPool.JointTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            _shotAngle = new Vector3(0f, 0f, CasterYawDegrees);
            _shotLive = true;
            _shotLifeTime = 10f;
            _shotPosition = origin + SourceJointMath.Rotate(new Vector3(0f, -20f, 50f), _shotAngle) * SourceJointMath.FrameFactor;

            SpawnSparkFan(origin + SourceJointMath.Rotate(new Vector3(-20f, -20f, 60f), _shotAngle));
            SpawnSparkFan(origin + SourceJointMath.Rotate(new Vector3(30f, -20f, 60f), _shotAngle));

            PlayUnmappedSound(SkillId, SoundPath);
        }

        private void SpawnSparkFan(Vector3 position)
        {
            Vector3 angle = _shotAngle;
            for (int i = 0; i < 20; i++)
            {
                angle.X = _shotAngle.X + MuGame.Random.Next(20) + 5;
                angle.Y += i * 18; // cumulative in the original
                _sparkPool.SpawnJoint(position, angle, 1);
            }
        }

        protected override void OnTick(float factor)
        {
            if (_shotLive)
                MoveShot(factor);

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, _shotAngle) * factor;
                p.Gravity += 0.004f * factor;
                p.Scale += p.Gravity * factor;
                p.Velocity *= 0.98f; // per frame
                p.Position.Z += p.Gravity * 10f * factor;
            }

            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }

            _sparkPool.Move(factor, World?.Terrain);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            float dt = factor * 0.04f;
            if (_shotLive)
            {
                _shotAge += dt;
                float k = MathF.Min(1f, _shotAge / 0.08f) * MathHelper.Clamp(_shotLifeTime / 3f, 0f, 1f);
                Lights.Set(0, _shotPosition, FireLightColor, 220f, 1.1f * k * EffectLights.Flicker(_shotAge, 0.3f, 0.15f));
            }
            else
            {
                Lights.Hide(0);
            }

            if (_bombLight)
            {
                _bombAge += dt;
                float k = EffectLights.Flash(_bombAge, 0.5f, 1.5f);
                if (k > 0f)
                    Lights.Set(1, _bombPosition, FireLightColor, 260f + 120f * k, 1.9f * k);
                else
                {
                    _bombLight = false;
                    Lights.Hide(1);
                }
            }
        }

        private void MoveShot(float factor)
        {
            Vector3 angle = _shotAngle + new Vector3(0f, 0f, 90f);
            float scale = ((15f - _shotLifeTime) / 20f) * (MuGame.Random.Next(3) + 2);

            if (FpsCheck(1, factor))
                SpawnFire(_shotPosition + SourceJointMath.Rotate(new Vector3(0f, 20f, 0f), angle), scale);
            if (FpsCheck(1, factor))
                SpawnFire(_shotPosition + SourceJointMath.Rotate(new Vector3(0f, -20f, 0f), angle), scale);

            // Every frame of the (int)LifeTime == 1 window, as in the original.
            if ((int)_shotLifeTime == 1)
                CreateBomb2(_shotPosition);

            // Epilogue MoveParticle(o, true): Direction (0, -30, 0) rotated by o->Angle.
            _shotPosition += SourceJointMath.Rotate(new Vector3(0f, -30f, 0f), _shotAngle) * factor;

            _shotLifeTime -= factor;
            if (_shotLifeTime <= 0f)
                _shotLive = false;
        }

        private void CreateBomb2(Vector3 position)
        {
            position.Z += 30f;
            if (!_bombLight && _bombAge <= 0f)
            {
                _bombLight = true;
                _bombPosition = position;
            }
            for (int j = 0; j < 20; j++)
                _sparkPool.SpawnSpark(position, new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, MuGame.Random.Next(30)), 2);

            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.Scale = 4f;
                p.LifeTime = 12f;
                SoundController.Instance.PlayBuffer(ExplosionSound);
                return;
            }
        }

        private void SpawnFire(Vector3 position, float scale)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.Scale = scale;
                p.LifeTime = 24f;
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_shotLive || _sparkPool.HasLive)
                return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            for (int i = 0; i < _explosions.Length; i++) if (_explosions[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            _sparkPool.DrawJoints(Batch, _sparkJointTexture);

            // Both sheets: RenderSprite(Width * 0.25, Height, Light, o->Angle[0] (= 0), Frame % 4 * 0.25, 0, 0.25, 1).
            if (_fireTexture != null && Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Particle p = ref _fires[i];
                    if (!p.Live)
                        continue;

                    int frame = (int)((23f - p.LifeTime) / 6f);
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, Vector3.One, 0f,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            if (_explosionTexture != null && Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _explosions.Length; i++)
                {
                    ref Particle p = ref _explosions[i];
                    if (!p.Live)
                        continue;

                    int frame = (int)((12f - p.LifeTime) / 3f);
                    Batch.AddSpriteRegion(p.Position, _explosionTexture.Width * p.Scale * 0.25f, _explosionTexture.Height * p.Scale, Vector3.One, 0f,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            _sparkPool.DrawSparks(Batch, _sparkTexture);
        }
    }
}
