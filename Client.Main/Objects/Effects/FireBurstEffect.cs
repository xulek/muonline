#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Fire Burst (AT_SKILL_LONGPIER_ATTACK = 61, masters AT_SKILL_FIRE_BUST_UP..+4), SourceMain5.2:
    /// packet   WSclient.cpp:3985       PLAYER_ATTACK_STRIKE (ride / Fenrir), sound map eFirebust
    /// gate     ZzzCharacter.cpp:2705   AnimationFrame &gt;= 3 in the strike actions
    /// release  ZzzCharacter.cpp:4930   bone 0 (80,0,20): 3 x MODEL_PIER_PART sub 0 (yaw +90 / 0 / -90, offsets
    ///          (0,0,H), (0,0,H/2), (0,0,H/2) from the target, H = target BoundingBoxMax.z),
    ///          MODEL_DARKLORD_SKILL sub 0 + 1 (Light (1,0.6,0.3))
    /// pier     ZzzEffect.cpp:3483 (sub 0: LifeTime 20, Gravity 2, Velocity 10, HiddenMesh 1, Scale 1.2,
    ///          Direction (0,-26,0); sub 1: LifeTime = owner, HiddenMesh 0, Scale 0.5, Alpha (20 - LifeTime) / 5,
    ///          BITMAP_FIRE+1 sub 0), 13162 (sub 0: i &lt; Gravity steps of MoveHumming + Direction + sub 1 FpsChecked,
    ///          SOUND_ATTACK_FIRE_BUST_EXP every tick; sub 1: LifeTime &lt; 5 Alpha / 1.3^F), no PlayAnimation / MoveParticle
    /// flash    ZzzEffect.cpp:3551 (LifeTime 10, Scale 0.2, Velocity 0.1, Angle (45, +-45, 0)), 13236 (break),
    ///          ZzzObject.cpp:954 (BodyLight = Light)
    /// fire     ZzzEffectParticle.cpp (BITMAP_FIRE+1 sub 0: defaults, LifeTime 2, static, Fire02.jpg additive)
    /// </summary>
    internal sealed class FireBurstEffect : SourceSkillEffectBase
    {
        private const string PierPartModelPath = "Skill/PierPart.bmd";       // MODEL_PIER_PART
        private const string DarkLordSkillModelPath = "Skill/DarkLordSkill.bmd"; // MODEL_DARKLORD_SKILL
        private const string FireTexturePath = "Effect/Fire02.jpg";           // BITMAP_FIRE + 1
        private const string ExplosionSoundPath = "Sound/eFirebustBoom.wav";  // SOUND_ATTACK_FIRE_BUST_EXP
        private static readonly Vector3 FlashLight = new(1f, 0.6f, 0.3f);

        private struct Head
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Offset;
            public float Velocity;
            public float Gravity;
            public float LifeTime;
        }

        private struct Segment
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public float Alpha;
            public float LifeTime;
        }

        private struct Fire
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
        }

        private struct Flash
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public float Frame;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Head[] _heads = new Head[3];
        private readonly Segment[] _segments = new Segment[256];
        private readonly Fire[] _fires = new Fire[256];
        private readonly Flash[] _flashes = new Flash[2];
        private WalkerObject? _target;
        private SourceModelRenderer? _headRenderer;
        private SourceModelRenderer? _segmentRenderer;
        private SourceModelRenderer? _flashRenderer;
        private Texture2D? _fireTexture;

        public FireBurstEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: 3f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 256)
        {
            _targetId = targetId;
        }

        protected override async Task LoadEffectContent()
        {
            _headRenderer = await LoadModel(PierPartModelPath, -1, 1);
            _segmentRenderer = await LoadModel(PierPartModelPath, -1, 0);
            _flashRenderer = await LoadModel(DarkLordSkillModelPath, -1);
            foreach (var r in new[] { _headRenderer, _segmentRenderer, _flashRenderer })
            {
                if (r == null)
                    continue;
                r.AnimationSpeed = 0f;
                r.UseNormalBlend();
            }
            _fireTexture = await LoadTexture(FireTexturePath);
            await SoundController.Instance.PreloadSoundAsync(ExplosionSoundPath);
        }

        protected override void OnStart()
        {
            if (World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            Vector3 position = CasterPosition + Vector3.UnitZ * 100f;
            if (Caster is PlayerObject player && player.TryGetBoneWorldMatrix(0, out Matrix bone))
                position = Vector3.Transform(new Vector3(80f, 0f, 20f), bone);

            float yaw = CasterYawDegrees;
            float height = _target?.BoundingBoxLocal.Max.Z ?? 0f;
            SpawnHead(0, position, yaw + 90f, new Vector3(0f, 0f, height));
            SpawnHead(1, position, yaw, new Vector3(0f, 0f, height - height / 2f));
            SpawnHead(2, position, yaw - 90f, new Vector3(0f, 0f, height - height / 2f));

            // Angle (45, 45 - 90 * SubType, 0); the rand_fps_check(2) branch picks the same values.
            for (int i = 0; i < 2; i++)
                _flashes[i] = new Flash { Live = true, Position = position, Angle = new Vector3(45f, 45f - 90f * i, 0f), LifeTime = 10f };
        }

        private void SpawnHead(int index, Vector3 position, float yaw, Vector3 offset)
        {
            _heads[index] = new Head
            {
                Live = true,
                Position = position,
                Angle = new Vector3(0f, 0f, yaw),
                Offset = offset,
                Velocity = 10f,
                Gravity = 2f,
                LifeTime = 20f,
            };
        }

        protected override void OnTick(float factor)
        {
            if (_target != null && _target.Status == Models.GameControlStatus.Disposed)
                _target = null;

            for (int h = 0; h < _heads.Length; h++)
            {
                ref Head head = ref _heads[h];
                if (!head.Live)
                    continue;

                if (_target != null)
                {
                    Vector3 p = _target.WorldPosition.Translation + head.Offset;
                    for (int i = 1; i < head.Gravity; ++i)
                    {
                        if (FpsCheck(2, factor))
                            head.Angle.X += head.Angle.X < -90f ? 20f * factor : -20f * factor;
                        SourceJointMath.MoveHumming(ref head.Position, ref head.Angle, p, head.Velocity, factor);
                        head.Velocity += 0.4f * factor;
                        if (head.LifeTime < 10f)
                            head.Velocity += 0.1f * factor;

                        head.Position += SourceJointMath.Rotate(new Vector3(0f, -26f, 0f), head.Angle) * factor;

                        if (FpsCheck(1, factor))
                            SpawnSegment(head.Position, head.Angle, head.LifeTime);
                    }
                    head.Gravity += 0.1f * factor;
                    SoundController.Instance.PlayBufferSingleChannel(ExplosionSoundPath);
                }

                head.LifeTime -= factor;
                if (head.LifeTime <= 0f)
                    head.Live = false;
            }

            float fade = MathF.Pow(1f / 1.3f, factor);
            for (int i = 0; i < _segments.Length; i++)
            {
                ref Segment s = ref _segments[i];
                if (!s.Live)
                    continue;
                if (s.LifeTime < 5f)
                    s.Alpha *= fade;
                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                    f.Live = false;
            }

            for (int i = 0; i < _flashes.Length; i++)
            {
                ref Flash f = ref _flashes[i];
                if (!f.Live)
                    continue;
                f.Frame += 0.1f * factor;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                    f.Live = false;
            }
        }

        private void SpawnSegment(Vector3 position, Vector3 angle, float ownerLifeTime)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                ref Segment s = ref _segments[i];
                if (s.Live)
                    continue;
                s = new Segment { Live = true, Position = position, Angle = angle, LifeTime = ownerLifeTime, Alpha = (20f - ownerLifeTime) / 5f };
                break;
            }

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (f.Live)
                    continue;
                f = new Fire { Live = true, Position = position, LifeTime = 2f };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < _heads.Length; i++) if (_heads[i].Live) return true;
            for (int i = 0; i < _flashes.Length; i++) if (_flashes[i].Live) return true;
            for (int i = 0; i < _segments.Length; i++) if (_segments[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            return false;
        }

        private Vector3 BodyLight(Vector3 position)
        {
            // RequestTerrainLight + o->Light (1,1,1).
            Vector3 terrain = World?.Terrain?.EvaluateTerrainLight(position.X, position.Y) ?? Vector3.Zero;
            return terrain + Vector3.One;
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void OnDraw()
        {
            if (_headRenderer != null)
            {
                for (int i = 0; i < _heads.Length; i++)
                {
                    ref Head h = ref _heads[i];
                    if (h.Live)
                        _headRenderer.DrawInstance(h.Position, ToRadians(h.Angle), 1.2f, BodyLight(h.Position), 1f);
                }
            }

            if (_segmentRenderer != null)
            {
                for (int i = 0; i < _segments.Length; i++)
                {
                    ref Segment s = ref _segments[i];
                    if (s.Live)
                        _segmentRenderer.DrawInstance(s.Position, ToRadians(s.Angle), 0.5f, BodyLight(s.Position), 1f,
                            MathF.Round(MathHelper.Clamp(s.Alpha, 0.003f, 1f) * 50f) / 50f);
                }
            }

            if (_flashRenderer != null)
            {
                for (int i = 0; i < _flashes.Length; i++)
                {
                    ref Flash f = ref _flashes[i];
                    if (!f.Live)
                        continue;
                    _flashRenderer.SetAnimationTime(f.Frame);
                    _flashRenderer.Update(LastGameTime);
                    _flashRenderer.DrawInstance(f.Position, ToRadians(f.Angle), 0.2f, FlashLight, 1f);
                }
            }

            if (Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Fire f = ref _fires[i];
                    if (f.Live)
                        Batch.AddSprite(f.Position, 1f, Vector3.One);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _headRenderer?.Dispose();
            _segmentRenderer?.Dispose();
            _flashRenderer?.Dispose();
        }
    }
}
