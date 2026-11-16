#nullable enable
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Killing Blow (AT_SKILL_THRUST = 260), SourceMain5.2:
    /// packet   WSclient.cpp:4690       PLAYER_SKILL_THRUST (MonkSystem.cpp:393), effects at the packet (hero: at cast,
    ///          ZzzInterface.cpp:2727), no release gate
    /// create   MonkSystem.cpp:1071     Angle[2] = CreateAngle2D(o, target); MODEL_WOLF_HEAD_EFFECT sub 0 on the owner,
    ///          BITMAP_SBUMB effect (Scale 2.1), SOUND_RAGESKILL_THRUST (packet sound map)
    /// wolf     ZzzEffect.cpp:5944 (bone 27; sub 0: LifeTime 10 / PlaySpeed; sub 1: Scale 1.2..1.6, Direction -50,
    ///          LifeTime 2..3; sub 2: Scale 0.8..1.0, Direction -40, LifeTime 3..5, yaw +-5 * F), 16902 (LifeTime >= 3:
    ///          jittered owner angle, Scale 1.5, 3 x BITMAP_PIN_LIGHT joint + sub 1 + sub 2, all FpsChecked),
    ///          17899 (MoveParticle), ZzzObject.cpp:2522 (RENDER_TEXTURE | RENDER_BRIGHT, BodyLight 204)
    /// pin      ZzzEffectJoint.cpp:2654 (Scale 5, Velocity 15..74, LifeTime 1..3, MaxTails 5), 6803 (Velocity += 0.1F,
    ///          Light *= 0.9^F)
    /// sbumb    ZzzEffect.cpp:5971 (LifeTime 20), 16952 (LifeTime &lt; 17: particle at the target, FpsChecked;
    ///          SOUND_RAGESKILL_THRUST_ATTACK at 15 / 9 / 6); ZzzEffectParticle.cpp:3839, 8881, 9269 (4-frame strip)
    /// Not ported: sub 1 / 2 "Owner->Velocity +=" (side effect on the character object).
    /// </summary>
    internal sealed class KillingBlowEffect : SourceSkillEffectBase
    {
        private const string WolfHeadModelPath = "Effect/wolf_head_effect.bmd";        // MODEL_WOLF_HEAD_EFFECT
        private const string PinLightTexturePath = "Effect/pin_lights.jpg";             // BITMAP_PIN_LIGHT
        private const string SbumbTexturePath = "Effect/sbumb.jpg";                     // BITMAP_SBUMB
        private const string AttackSoundPath = "Sound/Ragefighter/Rage_Thrust_Att.wav"; // SOUND_RAGESKILL_THRUST_ATTACK
        private const int FistBone = 27;

        private struct Pin
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
        }

        private struct Copy
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 AngleRadians;
            public Vector3 Step;
            public float Scale;
            public float LifeTime;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 Position;
            public float Scale;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Pin[] _pins = new Pin[24];
        private readonly SourceTrail[] _pinTrails = new SourceTrail[24];
        private readonly Copy[] _copies = new Copy[12];
        private readonly Burst[] _bursts = new Burst[8];
        private WalkerObject? _target;
        private SourceModelRenderer? _wolfRenderer;
        private Texture2D? _pinTexture;
        private Texture2D? _sbumbTexture;

        private Vector3 _angle;           // owner angle, degrees
        private Vector3 _headPosition;
        private float _headScale = 1f;
        private float _headLife;
        private float _sbumbLife = 20f;
        private int _lastSoundMark = -1;
        private int _liveCount;

        public KillingBlowEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
        }

        protected override async Task LoadEffectContent()
        {
            _wolfRenderer = await LoadModel(WolfHeadModelPath);
            if (_wolfRenderer != null)
                _wolfRenderer.AnimationSpeed = 0f;
            _pinTexture = await LoadTexture(PinLightTexturePath);
            _sbumbTexture = await LoadTexture(SbumbTexturePath);
            await SoundController.Instance.PreloadSoundAsync(AttackSoundPath);
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            float yaw = _target != null
                ? SourceJointMath.CreateAngle2D(CasterPosition, _target.WorldPosition.Translation)
                : CasterYawDegrees;
            _angle = new Vector3(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), yaw);

            float playSpeed = 0.4f;
            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillThrust < actions.Length && actions[(int)PlayerAction.PlayerSkillThrust]?.PlaySpeed > 0f)
            {
                playSpeed = actions[(int)PlayerAction.PlayerSkillThrust].PlaySpeed;
            }

            _headLife = 10f / playSpeed;
            _headPosition = GetCasterBonePosition(FistBone);

            int tails = SourceJointMath.ScaledMaxTails(5);
            for (int i = 0; i < _pinTrails.Length; i++)
                _pinTrails[i] = new SourceTrail(tails);
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        protected override void OnTick(float factor)
        {
            TickHead(factor);
            TickCopies(factor);
            TickPins(factor);
            TickSbumb(factor);
            TickBursts(factor);
        }

        private void TickHead(float factor)
        {
            if (_headLife <= 0f)
                return;

            if (_headLife >= 3f)
            {
                int nRandom = MuGame.Random.Next(6);
                float value = 7f + MuGame.Random.Next(3);
                float amount = value * (nRandom % 3 * 2 + 1);
                Vector3 jittered = _angle;
                jittered.Z += nRandom / 3 != 0 ? amount : -amount;
                int temp = MuGame.Random.Next(3);
                if (temp == 1)
                    jittered.X += amount;
                else if (temp == 2)
                    jittered.X -= amount;

                Vector3 bone = GetCasterBonePosition(FistBone);
                _headPosition = bone;
                _headScale = 1.5f;

                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = bone;
                    p.Y += MuGame.Random.Next(20) - 10;
                    p.Z += MuGame.Random.Next(28) + 6;
                    if (FpsCheck(1, factor))
                        SpawnPin(p, jittered);
                }

                if (FpsCheck(1, factor))
                    SpawnCopy(bone, jittered, -50f, 1.2f + MuGame.Random.Next(5) * 0.1f, 2 + MuGame.Random.Next(2));
                if (FpsCheck(1, factor))
                {
                    Vector3 angle = jittered;
                    angle.Z += (MuGame.Random.Next(10) - 5f) * factor;
                    SpawnCopy(bone, angle, -40f, 0.8f + MuGame.Random.Next(3) * 0.1f, 3 + MuGame.Random.Next(3));
                }
            }

            _headLife -= factor;
        }

        private void SpawnPin(Vector3 position, Vector3 angleDeg)
        {
            for (int i = 0; i < _pins.Length; i++)
            {
                ref Pin pin = ref _pins[i];
                if (pin.Live)
                    continue;

                SourceJointMath.AngleBasis(angleDeg, out pin.Right, out Vector3 forward, out pin.Up);
                pin.Live = true;
                pin.Position = position;
                pin.Forward = -forward; // local (0, -1, 0)
                pin.Light = Vector3.One;
                pin.Velocity = MuGame.Random.Next(60) + 15;
                pin.LifeTime = MuGame.Random.Next(3) + 1;
                SourceTrail trail = _pinTrails[i];
                trail.Reset(SourceJointMath.ScaledMaxTails(5));
                trail.Push(position, pin.Right, pin.Up, 5f);
                _liveCount++;
                return;
            }
        }

        private void SpawnCopy(Vector3 position, Vector3 angleDeg, float direction, float scale, float lifeTime)
        {
            for (int i = 0; i < _copies.Length; i++)
            {
                ref Copy copy = ref _copies[i];
                if (copy.Live)
                    continue;

                copy.Live = true;
                copy.Position = position;
                copy.AngleRadians = ToRadians(angleDeg);
                copy.Step = SourceJointMath.Rotate(new Vector3(0f, direction, 0f), angleDeg);
                copy.Scale = scale;
                copy.LifeTime = lifeTime;
                _liveCount++;
                return;
            }
        }

        private void TickCopies(float factor)
        {
            for (int i = 0; i < _copies.Length; i++)
            {
                ref Copy copy = ref _copies[i];
                if (!copy.Live)
                    continue;

                copy.Position += copy.Step * factor;
                copy.LifeTime -= factor;
                if (copy.LifeTime <= 0f)
                {
                    copy.Live = false;
                    _liveCount--;
                }
            }
        }

        private void TickPins(float factor)
        {
            float decay = System.MathF.Pow(0.9f, factor);
            for (int i = 0; i < _pins.Length; i++)
            {
                ref Pin pin = ref _pins[i];
                if (!pin.Live)
                    continue;

                pin.Position += pin.Forward * (pin.Velocity * factor);
                pin.Velocity += factor * 0.1f;
                pin.Light *= decay;
                _pinTrails[i].Push(pin.Position, pin.Right, pin.Up, 5f);
                pin.LifeTime -= factor;
                if (pin.LifeTime < 0f)
                {
                    pin.Live = false;
                    _liveCount--;
                }
            }
        }

        private void TickSbumb(float factor)
        {
            if (_sbumbLife <= 0f)
                return;

            bool skipSound = false;
            if (_sbumbLife < 17f)
            {
                if (!TargetAlive)
                {
                    skipSound = true;
                }
                else
                {
                    Vector3 target = _target!.WorldPosition.Translation;
                    var position = new Vector3(
                        target.X + MuGame.Random.Next(80) - 40,
                        target.Y + MuGame.Random.Next(80) - 40,
                        target.Z + MuGame.Random.Next(120) + 30);
                    float scale = 1.5f + MuGame.Random.Next(10) * 0.02f;
                    if (FpsCheck(1, factor))
                        SpawnBurst(position, scale * 2.1f);
                }
            }

            if (!skipSound)
            {
                int mark = (int)_sbumbLife;
                if (mark != _lastSoundMark && (mark == 15 || mark == 9 || mark == 6))
                    SoundController.Instance.PlayBuffer(AttackSoundPath);
                _lastSoundMark = mark;
            }

            _sbumbLife -= factor;
        }

        private void SpawnBurst(Vector3 position, float scale)
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst burst = ref _bursts[i];
                if (burst.Live)
                    continue;

                burst.Live = true;
                burst.Position = position;
                burst.Scale = scale;
                burst.LifeTime = 4f;
                _liveCount++;
                return;
            }
        }

        private void TickBursts(float factor)
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst burst = ref _bursts[i];
                if (!burst.Live)
                    continue;

                burst.LifeTime -= factor;
                if (burst.LifeTime <= 0f)
                {
                    burst.Live = false;
                    _liveCount--;
                }
            }
        }

        protected override bool HasActiveContent() => _headLife > 0f || _sbumbLife > 0f || _liveCount > 0;

        protected override void OnDraw()
        {
            if (Batch.Begin(_pinTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _pins.Length; i++)
                {
                    if (!_pins[i].Live)
                        continue;
                    SourceTrail trail = _pinTrails[i];
                    Batch.AddTrail(trail, SourceQuadBatch.TrailFaces.Both, _pins[i].Light, SourceQuadBatch.TrailShade.Flat, trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_wolfRenderer != null)
            {
                // BodyLight = RequestTerrainLight + o->Light (the caster's light, (1,1,1)); one sample for the
                // whole cluster so every instance shares the same vertex bake.
                Vector3 light = (World?.Terrain?.EvaluateTerrainLight(_headPosition.X, _headPosition.Y) ?? Vector3.Zero) + Vector3.One;
                if (_headLife > 0f)
                    _wolfRenderer.DrawInstance(_headPosition, ToRadians(_angle), _headScale, light, 1f);
                for (int i = 0; i < _copies.Length; i++)
                {
                    ref Copy copy = ref _copies[i];
                    if (copy.Live)
                        _wolfRenderer.DrawInstance(copy.Position, copy.AngleRadians, copy.Scale, light, 1f);
                }
            }

            if (_sbumbTexture != null && Batch.Begin(_sbumbTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                float width = _sbumbTexture.Width * 0.25f;
                float height = _sbumbTexture.Height;
                for (int i = 0; i < _bursts.Length; i++)
                {
                    ref Burst burst = ref _bursts[i];
                    if (!burst.Live)
                        continue;
                    int frame = (int)(4f - burst.LifeTime);
                    Batch.AddSpriteRegion(burst.Position, width * burst.Scale, height * burst.Scale, Vector3.One, 0f,
                        frame % 4 * 0.25f + 0.005f, 0f, 0.25f - 0.01f, 1f);
                }
                Batch.End();
            }
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _wolfRenderer?.Dispose();
            _wolfRenderer = null;
        }
    }
}
