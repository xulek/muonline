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
    /// Ice (AT_SKILL_SLOW = 7), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4819   MODEL_ICE at the target, 5 x MODEL_ICE_SMALL, SOUND_ICE
    /// ice     ZzzEffect.cpp:2284 (init sub 0), 12155 (move: stop at frame 5, smoke, Alpha fade), Skill/Ice01.bmd
    /// small   ZzzEffect.cpp:2816/2882 (stone init), 11007/11028 (stone move, BITMAP_SMOKE), Skill/Ice02.bmd
    /// smoke   BITMAP_SMOKE sub 0 (ZzzEffectParticle.cpp:1158/5304)
    /// </summary>
    internal sealed class IceEffect : SourceSkillEffectBase
    {
        private const string IceModelPath = "Skill/Ice01.bmd";       // MODEL_ICE
        private const string SmallModelPath = "Skill/Ice02.bmd";     // MODEL_ICE_SMALL
        private const string SmokeTexturePath = "Effect/smoke01.jpg"; // BITMAP_SMOKE
        private const string SoundPath = "Sound/sIce.wav";           // SOUND_ICE

        private const int SkillId = 7;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;

        private struct Shard
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public float Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private readonly WalkerObject? _target;
        private readonly Shard[] _shards = new Shard[5];
        private readonly Smoke[] _smokes = new Smoke[48];
        private SourceModelRenderer? _iceModel;
        private SourceModelRenderer? _smallModel;
        private Texture2D? _smokeTexture;

        private bool _iceLive;
        private Vector3 _icePosition;
        private Vector3 _iceAngle;
        private float _iceLifeTime;
        private float _iceAlpha;
        private float _iceFrame;
        private float _iceVelocity;
        private float _smallFrame;

        public IceEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 5f, ReleaseDelaySeconds, 128)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _iceModel = await LoadModel(IceModelPath, 0);
            _smallModel = await LoadModel(SmallModelPath, -1);
            if (_iceModel != null)
            {
                _iceModel.AnimationSpeed = 0f;
                _iceModel.UseNormalBlend();
            }
            if (_smallModel != null)
            {
                _smallModel.AnimationSpeed = 0f;
                _smallModel.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            if (!(_target != null && _target.Status != GameControlStatus.Disposed && _target.World != null))
                return;

            Vector3 position = _target.WorldPosition.Translation;
            var angle = new Vector3(0f, 0f, CasterYawDegrees);

            _iceLive = true;
            _icePosition = position;
            _iceAngle = angle;
            _iceLifeTime = 50f;
            _iceAlpha = 1f;
            _iceVelocity = 1f;

            for (int i = 0; i < _shards.Length; i++)
            {
                ref Shard s = ref _shards[i];
                s.Live = true;
                s.Position = position;
                s.Angle = angle;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Angle.Z = MuGame.Random.Next(360);
                s.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f), s.Angle);
                s.Gravity = MuGame.Random.Next(16) + 8;
            }

            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_iceLive)
            {
                if (_iceFrame >= 5f)
                {
                    _iceVelocity = 0f;
                    if (FpsCheck(2, factor) && FpsCheck(1, factor))
                    {
                        SpawnSmoke(new Vector3(
                            _icePosition.X + MuGame.Random.Next(64) - 32,
                            _icePosition.Y + MuGame.Random.Next(64) - 32,
                            _icePosition.Z + MuGame.Random.Next(128) + 32));
                    }
                    _iceAlpha -= 0.05f * factor;
                    if (_iceAlpha < 0f)
                        _iceLive = false;
                }

                _iceFrame += _iceVelocity * factor;
                _iceLifeTime -= factor;
                if (_iceLifeTime <= 0f)
                    _iceLive = false;
            }

            bool anyShard = false;
            for (int i = 0; i < _shards.Length; i++)
            {
                ref Shard s = ref _shards[i];
                if (!s.Live)
                    continue;

                anyShard = true;
                s.Position += s.Direction * factor; // sub 0: Direction was rotated at init (ZzzEffect.cpp:11012)
                s.Direction *= 0.9f; // per frame
                s.Position.Z += s.Gravity * factor;
                s.Gravity -= 3f * factor;

                float height = TerrainHeight(s.Position);
                if (s.Position.Z < height)
                {
                    s.Position.Z = height;
                    s.Gravity = -s.Gravity * 0.5f;
                    s.LifeTime -= 4f * factor;
                    s.Angle.X -= s.Scale * 128f * factor;
                }
                else
                {
                    s.Angle.X -= s.Scale * 32f * factor;
                }

                // MODEL_ICE_SMALL sub 0: rand_fps_check(10) -> CreateParticleFpsChecked(BITMAP_SMOKE).
                if (FpsCheck(10, factor) && FpsCheck(1, factor))
                    SpawnSmoke(s.Position);

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (anyShard)
                _smallFrame += ModelVelocity * factor;

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Light = p.LifeTime / 8f;
                p.Gravity += 0.2f * factor;
                p.Position.Z += p.Gravity * factor;
                p.Scale += factor * 0.05f;
            }
        }

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.Light = 1f;
                p.LifeTime = 20f;
                p.Scale = (MuGame.Random.Next(64) + 64) * 0.01f;
                p.Rotation = MuGame.Random.Next(360);
                p.Gravity = (MuGame.Random.Next(32) + 60) * 0.1f;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_iceLive)
                return true;
            for (int i = 0; i < _shards.Length; i++) if (_shards[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (_iceLive && _iceModel != null)
            {
                _iceModel.SetAnimationTime(_iceFrame);
                _iceModel.Update(LastGameTime);
                Vector3 light = terrain?.EvaluateTerrainLight(_icePosition.X, _icePosition.Y) ?? Vector3.One;
                _iceModel.DrawInstance(_icePosition, new Vector3(0f, 0f, MathHelper.ToRadians(_iceAngle.Z)), 0.8f, light, 1f, MathF.Max(_iceAlpha, 0.003f));
            }

            if (_smallModel != null)
            {
                bool updated = false;
                for (int i = 0; i < _shards.Length; i++)
                {
                    ref Shard s = ref _shards[i];
                    if (!s.Live)
                        continue;

                    if (!updated)
                    {
                        _smallModel.SetAnimationTime(_smallFrame);
                        _smallModel.Update(LastGameTime);
                        updated = true;
                    }

                    Vector3 light = terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.One;
                    _smallModel.DrawInstance(s.Position,
                        new Vector3(MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z)),
                        s.Scale, light, 1f);
                }
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, new Vector3(p.Light), p.Rotation);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _iceModel?.Dispose();
            _smallModel?.Dispose();
        }
    }
}
