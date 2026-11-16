#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Chain Drive (AT_SKILL_GIANTSWING = 262), SourceMain5.2:
    /// packet   WSclient.cpp:4765 / 13032  PLAYER_SKILL_GIANTSWING, effect at the packet (hero: at cast, ZzzInterface.cpp:2743)
    /// create   MonkSystem.cpp:1097     BITMAP_SWORDEFF effect on the owner, SOUND_RAGESKILL_GIANTSWING (packet sound map)
    /// effect   ZzzEffect.cpp:5990 (LifeTime 200), 17052 (ribbon joint bone 5 every tick; owner frame &gt; 6: SHOCKWAVE01,
    ///          WOLF_HEAD_EFFECT2 sub 3, DAMAGE1 on the target, LifeTime 45; frame &gt; 11: SHOCKWAVE02, WOLF_HEAD_EFFECT2 sub 5,
    ///          2 x SHOCKWAVE_SPIN01, SHINY+4 on the target, LifeTime 20; SOUND_RAGESKILL_GIANTSWING_ATTACK at both)
    /// ribbon   ZzzEffectJoint.cpp:2673 (FACE_TWO, Scale 100, LifeTime 10, MaxTails 50, Light (0.6,0.6,1)), 6822 (bone 5)
    /// waves    ZzzEffect.cpp:5995 / 6071 / 6098 init, 17161 / 17172 / 17201 move, ZzzObject.cpp:2504 / 2511 / 2517 render
    /// wolf     ZzzEffect.cpp:6196, 17439, ZzzObject.cpp:2470 (BodyLight (0.4,0.4,0.6) * 0.3)
    /// target   ZzzEffect.cpp:6089 / 17181 (DAMAGE1), 6145 / 17322 (SHINY+4); ZzzEffectParticle.cpp:3845, 8886, 9274, 2706, 7379
    /// The one-shot spawns are FpsChecked individually in the original; the 25 Hz behaviour (all created) is used when the
    /// target exists. Not ported: "Owner->Velocity +=" of the flying wolf heads (side effect on the character object).
    /// </summary>
    internal sealed class ChainDriveEffect : SourceSkillEffectBase
    {
        private const string ShockWave1Path = "Effect/shockwave01.bmd";       // MODEL_SHOCKWAVE01
        private const string ShockWave2Path = "Effect/shockwave02.bmd";       // MODEL_SHOCKWAVE02
        private const string SpinPath = "Effect/shockwave_spin01.bmd";        // MODEL_SHOCKWAVE_SPIN01
        private const string WolfHead2Path = "Effect/wolf_head_effect2.bmd";  // MODEL_WOLF_HEAD_EFFECT2
        private const string RibbonTexturePath = "Effect/!SwordEff.jpg";      // BITMAP_SWORDEFF
        private const string DamageTexturePath = "Effect/Damage1.jpg";        // BITMAP_DAMAGE1
        private const string RingTexturePath = "Effect/ring.jpg";             // BITMAP_SHINY + 4
        private const string AttackSoundPath = "Sound/Ragefighter/Rage_Giantswing_Att.wav"; // SOUND_RAGESKILL_GIANTSWING_ATTACK
        private static readonly Vector3 RibbonLight = new(0.6f, 0.6f, 1f);
        private static readonly Vector3 WolfBodyLight = new Vector3(0.4f, 0.4f, 0.6f) * 0.3f;

        private const int WaveShock1 = 0;
        private const int WaveShock2 = 1;
        private const int WaveSpin0 = 2;
        private const int WaveSpin1 = 3;

        private struct Ribbon
        {
            public bool Live;
            public Vector3 Right;
            public Vector3 Up;
            public float LifeTime;
            public int MaxTails;
        }

        private struct Wave
        {
            public bool Live;
            public int Kind;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public Vector3 StartPosition;
            public float Scale;
            public float LifeTime;
        }

        private struct Wolf
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Step;
            public float Scale;
            public float LifeTime;
        }

        private struct TargetFx
        {
            public bool Live;
            public bool Ring;
            public Vector3 Position;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Ribbon[] _ribbons = new Ribbon[16];
        private readonly SourceTrail[] _ribbonTrails = new SourceTrail[16];
        private readonly Wave[] _waves = new Wave[72];
        private readonly Wolf[] _wolves = new Wolf[64];
        private readonly TargetFx[] _targetFx = new TargetFx[4];
        private readonly Particle[] _damages = new Particle[16];
        private readonly Particle[] _rings = new Particle[24];
        private WalkerObject? _target;
        private SourceModelRenderer? _shock1Renderer;
        private SourceModelRenderer? _shock2Renderer;
        private SourceModelRenderer? _spinRenderer;
        private SourceModelRenderer? _wolfRenderer;
        private Texture2D? _ribbonTexture;
        private Texture2D? _damageTexture;
        private Texture2D? _ringTexture;

        private float _life = 200f;
        private float _playSpeed = 0.4f;
        private int _actionKeys = int.MaxValue;
        private int _liveCount;

        public ChainDriveEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
        }

        protected override async Task LoadEffectContent()
        {
            _shock1Renderer = await LoadStatic(ShockWave1Path);
            _shock2Renderer = await LoadStatic(ShockWave2Path);
            _spinRenderer = await LoadStatic(SpinPath);
            _wolfRenderer = await LoadStatic(WolfHead2Path);
            _ribbonTexture = await LoadTexture(RibbonTexturePath);
            _damageTexture = await LoadTexture(DamageTexturePath);
            _ringTexture = await LoadTexture(RingTexturePath);
            await SoundController.Instance.PreloadSoundAsync(AttackSoundPath);
        }

        private async Task<SourceModelRenderer?> LoadStatic(string path)
        {
            SourceModelRenderer? renderer = await LoadModel(path);
            if (renderer != null)
                renderer.AnimationSpeed = 0f;
            return renderer;
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillGiantswing < actions.Length && actions[(int)PlayerAction.PlayerSkillGiantswing] is { } action)
            {
                if (action.PlaySpeed > 0f)
                    _playSpeed = action.PlaySpeed;
                _actionKeys = action.NumAnimationKeys;
            }

            // A ribbon lives 11 ticks, far below MaxTails 50 / F: only that many tails are ever stored.
            float factor = MathF.Max(SourceJointMath.FrameFactor, 0.001f);
            int tails = Math.Min(SourceJointMath.ScaledMaxTails(50), (int)(11f / factor) + 4);
            for (int i = 0; i < _ribbonTrails.Length; i++)
                _ribbonTrails[i] = new SourceTrail(tails);
        }

        private Vector3 OwnerAngleDegrees => new(
            MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        // TransformByObjectBone(result, owner, bone, relative).
        private Vector3 OwnerBone(int bone, Vector3 relative)
        {
            if (Caster is PlayerObject player && player.TryGetBoneWorldMatrix(bone, out Matrix matrix))
                return Vector3.Transform(relative, matrix);
            return CasterPosition + relative;
        }

        protected override void OnTick(float factor)
        {
            TickEffect(factor);
            TickWaves(factor);
            TickWolves(factor);
            TickTargetFx(factor);
            TickRibbons(factor);
            TickParticles(_damages, factor, 1.2f, 0.8f);
            TickParticles(_rings, factor, 1.1f, 0.9f);
        }

        private void TickEffect(float factor)
        {
            if (_life <= 0f)
                return;

            float frame = CasterFrame;
            if (frame > _actionKeys)
            {
                _life = 0f;
                return;
            }

            Vector3 ownerAngle = OwnerAngleDegrees;
            if (FpsCheck(1, factor))
                SpawnRibbon(GetCasterBonePosition(5), ownerAngle);

            bool interrupted = false;
            if (frame > 6f && _life > 50f)
            {
                bool once = TargetAlive;
                var angle = new Vector3(60f, 0f, ownerAngle.Z - 180f);
                if (once || FpsCheck(1, factor))
                    SpawnWave(WaveShock1, OwnerBone(0, new Vector3(-180f, 100f, 0f)), angle, new Vector3(0.4f, 0.5f, 1f), 1f);
                if (once || FpsCheck(1, factor))
                    SpawnWolf(3, GetCasterBonePosition(5), ownerAngle, 1f, 4f / _playSpeed);

                if (!once)
                {
                    interrupted = true;
                }
                else
                {
                    SpawnTargetFx(false, _target!.WorldPosition.Translation, 20f);
                    SoundController.Instance.PlayBuffer(AttackSoundPath);
                    _life = 45f;
                }
            }

            if (!interrupted && frame > 11f && _life > 20f)
            {
                bool once = TargetAlive;
                var angle = new Vector3(90f, 0f, ownerAngle.Z - 180f);
                if (once || FpsCheck(1, factor))
                    SpawnWave(WaveShock2, OwnerBone(4, new Vector3(-80f, 120f, 0f)), angle, new Vector3(0.5f, 0.5f, 1f), 0.5f);
                if (once || FpsCheck(1, factor))
                    SpawnWolf(5, GetCasterBonePosition(5), ownerAngle, 1.5f, 4f / _playSpeed);

                Vector3 spinPosition = OwnerBone(4, new Vector3(-80f, 20f, 0f));
                if (once || FpsCheck(1, factor))
                    SpawnWave(WaveSpin0, spinPosition, angle, Vector3.One, 0.5f);
                if (once || FpsCheck(1, factor))
                    SpawnWave(WaveSpin1, spinPosition, angle, Vector3.One, 0.9f);

                if (once)
                {
                    SpawnTargetFx(true, _target!.WorldPosition.Translation, 16f);
                    SoundController.Instance.PlayBuffer(AttackSoundPath);
                    _life = 20f;
                }
            }

            _life -= factor;
        }

        private void SpawnRibbon(Vector3 position, Vector3 angleDeg)
        {
            for (int i = 0; i < _ribbons.Length; i++)
            {
                ref Ribbon ribbon = ref _ribbons[i];
                if (ribbon.Live)
                    continue;

                ribbon.Live = true;
                ribbon.LifeTime = 10f;
                ribbon.MaxTails = SourceJointMath.ScaledMaxTails(50);
                SourceJointMath.AngleBasis(angleDeg, out ribbon.Right, out _, out ribbon.Up);
                SourceTrail trail = _ribbonTrails[i];
                trail.Reset(ribbon.MaxTails);
                trail.Push(position, ribbon.Right, ribbon.Up, 100f);
                _liveCount++;
                return;
            }
        }

        private void TickRibbons(float factor)
        {
            Vector3 position = GetCasterBonePosition(5);
            for (int i = 0; i < _ribbons.Length; i++)
            {
                ref Ribbon ribbon = ref _ribbons[i];
                if (!ribbon.Live)
                    continue;

                _ribbonTrails[i].Push(position, ribbon.Right, ribbon.Up, 100f);
                ribbon.LifeTime -= factor;
                if (ribbon.LifeTime < 0f)
                {
                    ribbon.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnWave(int kind, Vector3 position, Vector3 angleDeg, Vector3 light, float scale)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                ref Wave wave = ref _waves[i];
                if (wave.Live)
                    continue;

                wave.Live = true;
                wave.Kind = kind;
                wave.Position = position;
                wave.Angle = angleDeg;
                wave.Light = light;
                wave.Scale = scale;
                wave.StartPosition = Vector3.Zero;
                if (kind == WaveShock1 || kind == WaveShock2)
                {
                    wave.LifeTime = (kind == WaveShock1 ? 5f : 14f) / (_playSpeed * 2f);
                    Vector3 owner = CasterPosition;
                    var direction = new Vector3(position.X - owner.X, position.Y - owner.Y, TerrainHeight(position) - TerrainHeight(owner));
                    if (direction.LengthSquared() > 0f)
                        direction.Normalize();
                    wave.StartPosition = direction * (kind == WaveShock1 ? 30f : 25f);
                }
                else
                {
                    wave.LifeTime = 8f / _playSpeed;
                }
                _liveCount++;
                return;
            }
        }

        private void TickWaves(float factor)
        {
            float spinGrow = MathF.Pow(1.01f, factor);
            for (int i = 0; i < _waves.Length; i++)
            {
                ref Wave wave = ref _waves[i];
                if (!wave.Live)
                    continue;

                switch (wave.Kind)
                {
                    case WaveShock1:
                        wave.Scale += 0.2f * factor;
                        wave.StartPosition *= 0.99f;   // per frame
                        wave.Position -= wave.StartPosition;
                        wave.Light *= 0.65f;           // per frame
                        wave.Position.Z = 220f + wave.Scale * 80f;
                        break;
                    case WaveShock2:
                        wave.Light *= 0.8f;            // per frame
                        wave.Scale += 0.1f * factor;
                        wave.StartPosition *= 0.95f;   // per frame
                        wave.Position -= wave.StartPosition;
                        break;
                    default:
                        wave.Scale *= spinGrow;
                        wave.Angle.Y += (wave.Kind == WaveSpin1 ? 10f : -10f) * factor;
                        wave.Light *= 0.9f;            // per frame
                        break;
                }

                wave.LifeTime -= factor;
                if (wave.LifeTime <= 0f)
                {
                    wave.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnWolf(int subType, Vector3 position, Vector3 angleDeg, float scale, float lifeTime)
        {
            for (int i = 0; i < _wolves.Length; i++)
            {
                ref Wolf wolf = ref _wolves[i];
                if (wolf.Live)
                    continue;

                wolf.Live = true;
                wolf.SubType = subType;
                wolf.Position = position;
                wolf.Angle = angleDeg;
                wolf.Scale = scale;
                wolf.LifeTime = lifeTime;
                wolf.Step = subType == 4 ? SourceJointMath.Rotate(new Vector3(0f, -40f, 0f), angleDeg)
                    : subType == 6 ? SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), angleDeg)
                    : Vector3.Zero;
                _liveCount++;
                return;
            }
        }

        private void TickWolves(float factor)
        {
            Vector3 ownerAngle = OwnerAngleDegrees;
            for (int i = 0; i < _wolves.Length; i++)
            {
                ref Wolf wolf = ref _wolves[i];
                if (!wolf.Live)
                    continue;

                if (wolf.SubType == 3)
                {
                    wolf.Angle = ownerAngle;
                    if (wolf.LifeTime >= 3f)
                    {
                        int nRandom = MuGame.Random.Next(6);
                        float value = 2f + MuGame.Random.Next(3);
                        float amount = value * (nRandom % 3 * 2 + 1);
                        Vector3 jittered = ownerAngle;
                        jittered.Z += nRandom / 3 != 0 ? amount : -amount;
                        int temp = MuGame.Random.Next(3);
                        if (temp == 1)
                            jittered.X += amount;
                        else if (temp == 2)
                            jittered.X -= amount;

                        wolf.Position = GetCasterBonePosition(4);
                        Vector3 fist = GetCasterBonePosition(27);
                        if (FpsCheck(1, factor))
                            SpawnWolf(4, fist, jittered, 0.7f + MuGame.Random.Next(3) * 0.1f, 5 + MuGame.Random.Next(2));
                        if (FpsCheck(1, factor))
                            SpawnWolf(6, fist, jittered, 1.2f + MuGame.Random.Next(5) * 0.1f, 2 + MuGame.Random.Next(2));
                    }
                }
                else if (wolf.SubType == 5)
                {
                    wolf.Position = GetCasterBonePosition(27);
                    wolf.Angle = ownerAngle;
                }
                else
                {
                    wolf.Position += wolf.Step * factor;
                }

                wolf.LifeTime -= factor;
                if (wolf.LifeTime <= 0f)
                {
                    wolf.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnTargetFx(bool ring, Vector3 position, float lifeTime)
        {
            for (int i = 0; i < _targetFx.Length; i++)
            {
                ref TargetFx fx = ref _targetFx[i];
                if (fx.Live)
                    continue;

                fx.Live = true;
                fx.Ring = ring;
                fx.Position = position;
                fx.LifeTime = lifeTime;
                _liveCount++;
                return;
            }
        }

        private void TickTargetFx(float factor)
        {
            for (int i = 0; i < _targetFx.Length; i++)
            {
                ref TargetFx fx = ref _targetFx[i];
                if (!fx.Live)
                    continue;

                if (fx.Ring)
                {
                    var position = new Vector3(
                        fx.Position.X + MuGame.Random.Next(100) - 40,
                        fx.Position.Y + MuGame.Random.Next(100) - 40,
                        fx.Position.Z + MuGame.Random.Next(100) + 50);
                    if (FpsCheck(1, factor))
                        SpawnParticle(_rings, position, Vector3.One, 1.9f, 8f);
                }
                else if (fx.LifeTime < 17f && (int)fx.LifeTime % 4 == 0 && TargetAlive)
                {
                    Vector3 target = _target!.WorldPosition.Translation;
                    var position = new Vector3(
                        target.X + MuGame.Random.Next(90) - 40,
                        target.Y + MuGame.Random.Next(90) - 40,
                        target.Z + MuGame.Random.Next(100) + 50);
                    float scale = 1.5f + MuGame.Random.Next(5) * 0.01f;
                    if (FpsCheck(1, factor))
                        SpawnParticle(_damages, position, new Vector3(0.6f, 0.94f, 1f), scale * 1.4f, 5f);
                }

                fx.LifeTime -= factor;
                if (fx.LifeTime <= 0f)
                {
                    fx.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnParticle(Particle[] pool, Vector3 position, Vector3 light, float scale, float lifeTime)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                ref Particle p = ref pool[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.Light = light;
                p.Scale = scale;
                p.LifeTime = lifeTime;
                _liveCount++;
                return;
            }
        }

        private void TickParticles(Particle[] pool, float factor, float grow, float lightDecay)
        {
            float growth = MathF.Pow(grow, factor);
            for (int i = 0; i < pool.Length; i++)
            {
                ref Particle p = ref pool[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    _liveCount--;
                    continue;
                }

                p.Scale *= growth;
                p.Light *= lightDecay; // per frame
            }
        }

        protected override bool HasActiveContent() => _life > 0f || _liveCount > 0;

        protected override void OnDraw()
        {
            if (Batch.Begin(_ribbonTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                for (int i = 0; i < _ribbons.Length; i++)
                {
                    if (_ribbons[i].Live)
                        Batch.AddTrail(_ribbonTrails[i], SourceQuadBatch.TrailFaces.Two, RibbonLight, SourceQuadBatch.TrailShade.Flat, _ribbons[i].MaxTails - 1);
                }
                Batch.End();
            }

            for (int i = 0; i < _waves.Length; i++)
            {
                ref Wave wave = ref _waves[i];
                if (!wave.Live)
                    continue;

                Vector3 angle = ToRadians(wave.Angle);
                switch (wave.Kind)
                {
                    case WaveShock1:
                        _shock1Renderer?.DrawInstance(wave.Position, angle, wave.Scale, wave.Light, 1f);
                        _shock1Renderer?.DrawInstance(wave.Position, angle, wave.Scale, wave.Light, 1f);
                        break;
                    case WaveShock2:
                        _shock2Renderer?.DrawInstance(wave.Position, angle, wave.Scale, wave.Light, 1f);
                        _shock2Renderer?.DrawInstance(wave.Position, angle, wave.Scale, wave.Light, 1f);
                        break;
                    default:
                        _spinRenderer?.DrawInstance(wave.Position, angle, wave.Scale, wave.Light, 1f);
                        break;
                }
            }

            if (_wolfRenderer != null)
            {
                for (int i = 0; i < _wolves.Length; i++)
                {
                    ref Wolf wolf = ref _wolves[i];
                    if (wolf.Live)
                        _wolfRenderer.DrawInstance(wolf.Position, ToRadians(wolf.Angle), wolf.Scale, WolfBodyLight, 1f);
                }
            }

            if (Batch.Begin(_damageTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _damages.Length; i++)
                {
                    ref Particle p = ref _damages[i];
                    if (!p.Live)
                        continue;
                    p.Light *= 2f; // RenderParticles: VectorScale(o->Light, 2.0f, o->Light) on the stored light
                    Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }

            if (Batch.Begin(_ringTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _rings.Length; i++)
                {
                    ref Particle p = ref _rings[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _shock1Renderer?.Dispose();
            _shock2Renderer?.Dispose();
            _spinRenderer?.Dispose();
            _wolfRenderer?.Dispose();
        }
    }
}
