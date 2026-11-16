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
    /// Evil Spirit (AT_SKILL_EVIL = 9, masters AT_SKILL_EVIL_SPIRIT_UP(_M)..+4), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4443   4 x (BITMAP_JOINT_SPIRIT sub 0, scale 80 + 20) at caster + 100, angle i * 90, SOUND_EVIL
    /// spirit  ZzzEffectJoint.cpp:601 (init, RENDER_TYPE_ALPHA_BLEND_MINUS), 3697 (move: homing on the caster + 80)
    /// laser   ZzzEffect.cpp:1892 (MODEL_LASER sub 0: LifeTime 1, BlendMesh 0, BlendMeshLight = Light[0], RENDER_DARK)
    /// </summary>
    internal sealed class EvilSpiritEffect : SourceSkillEffectBase
    {
        private const string SpiritTexturePath = "Effect/JointSpirit01.jpg"; // BITMAP_JOINT_SPIRIT
        private const string LaserModelPath = "Skill/Laser01.bmd";          // MODEL_LASER
        private const string SoundPath = "Sound/sEvil.wav";                 // SOUND_EVIL

        private const int SkillId = 9;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float Velocity = 70f;

        private struct Spirit
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 TargetPosition;
            public float DirectionX;
            public float DirectionZ;
            public float Scale;
            public float Light;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Laser
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public float BlendMeshLight;
            public float LifeTime;
        }

        private readonly Spirit[] _spirits = new Spirit[8];
        private readonly Laser[] _lasers = new Laser[32];
        private Texture2D? _spiritTexture;
        private SourceModelRenderer? _laserModel;

        public EvilSpiritEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 512)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _spiritTexture = await LoadTexture(SpiritTexturePath);
            _laserModel = await LoadModel(LaserModelPath, -2);
            if (_laserModel != null)
            {
                _laserModel.AnimationSpeed = 0f;
                _laserModel.BlendState = Blendings.Negative;
                _laserModel.BlendMeshState = Blendings.Negative;
            }
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            Vector3 start = origin + new Vector3(0f, 0f, 100f);
            int maxTails = SourceJointMath.ScaledMaxTails(6);

            for (int i = 0; i < 4; i++)
            {
                var angle = new Vector3(0f, 0f, i * 90f);
                InitSpirit(ref _spirits[i * 2], start, origin, angle, 80f, maxTails);
                InitSpirit(ref _spirits[i * 2 + 1], start, origin, angle, 20f, maxTails);
            }

            PlayUnmappedSound(SkillId, SoundPath);
        }

        private static void InitSpirit(ref Spirit s, Vector3 start, Vector3 target, Vector3 angle, float scale, int maxTails)
        {
            s.Live = true;
            s.Position = start;
            s.TargetPosition = target;
            s.Angle = angle;
            s.Scale = scale;
            s.Light = 1f;
            s.LifeTime = 49f;
            s.Trail = new SourceTrail(maxTails);
            s.Trail.Push(start, angle, scale);
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _lasers.Length; i++)
            {
                ref Laser l = ref _lasers[i];
                if (!l.Live)
                    continue;
                l.LifeTime -= factor;
                if (l.LifeTime <= 0f)
                    l.Live = false;
            }

            Vector3 caster = CasterPosition;
            for (int i = 0; i < _spirits.Length; i++)
            {
                ref Spirit s = ref _spirits[i];
                if (!s.Live)
                    continue;

                // Prologue flight with the angle of the previous frame; that matrix feeds the epilogue tail.
                SourceJointMath.AngleBasis(s.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                s.Position += -forward * (Velocity * factor);

                if (s.Scale == 80f && FpsCheck(1, factor))
                    SpawnLaser(s.Position, s.Angle, s.Light);

                s.TargetPosition = caster + new Vector3(0f, 0f, 80f);
                SourceJointMath.MoveHumming(ref s.Position, ref s.Angle, s.TargetPosition, 10f, factor);

                s.DirectionX += (MuGame.Random.Next(32) - 16) * 0.2f;
                s.DirectionZ += (MuGame.Random.Next(32) - 16) * 0.8f;
                s.Angle.X += s.DirectionX * factor;
                s.Angle.Z += s.DirectionZ * factor;
                s.DirectionX *= 0.6f; // per frame in the original
                s.DirectionZ *= 0.8f;

                float height = TerrainHeight(s.Position);
                if (s.Position.Z < height + 100f)
                {
                    s.DirectionX = 0f;
                    s.Angle.X = -5f;
                }
                if (s.Position.Z > height + 400f)
                {
                    s.DirectionX = 0f;
                    s.Angle.X = 5f;
                }

                s.Light = s.LifeTime * 0.1f;
                s.Trail.Push(s.Position, right, up, s.Scale);

                s.LifeTime -= factor;
                if (s.LifeTime < 0f)
                    s.Live = false;
            }
        }

        private void SpawnLaser(Vector3 position, Vector3 angle, float light)
        {
            for (int i = 0; i < _lasers.Length; i++)
            {
                ref Laser l = ref _lasers[i];
                if (l.Live)
                    continue;

                l.Live = true;
                l.Position = position;
                l.Angle = angle;
                l.BlendMeshLight = light;
                l.LifeTime = 1f;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < _spirits.Length; i++) if (_spirits[i].Live) return true;
            for (int i = 0; i < _lasers.Length; i++) if (_lasers[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_spiritTexture, Blendings.Negative, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live)
                        Batch.AddTrail(s.Trail, SourceQuadBatch.TrailFaces.Both, new Vector3(s.Light),
                            SourceQuadBatch.TrailShade.Flat, s.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_laserModel != null)
            {
                for (int i = 0; i < _lasers.Length; i++)
                {
                    ref Laser l = ref _lasers[i];
                    if (!l.Live)
                        continue;

                    float blendMeshLight = MathF.Round(l.BlendMeshLight * 50f) / 50f;
                    _laserModel.DrawInstance(l.Position,
                        new Vector3(MathHelper.ToRadians(l.Angle.X), MathHelper.ToRadians(l.Angle.Y), MathHelper.ToRadians(l.Angle.Z)),
                        1.3f, Vector3.One, MathF.Max(blendMeshLight, 0.003f));
                }
            }
        }

        protected override void DisposeEffect()
        {
            _laserModel?.Dispose();
        }
    }
}
