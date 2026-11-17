#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Explosion (AT_SKILL_SUMMON_EXPLOSION = 223): MODEL_SUMMONER_SUMMON_SAHAMUTT (Skill/summon_sahamutt.bmd), SourceMain5.2:
    /// spawn   SummonSystem.cpp:133 caster +/- (rand_fps_check(2) ? rand%300+150 : rand%250+150) on X and Y,
    ///         HeadTargetAngle = target tile corner, SubType = summon level, SOUND_SUMMON_SAHAMUTT
    /// init    ZzzEffect.cpp:792 LifeTime 80, Scale 0.35 / 0.5 / 0.7, Alpha 0, Velocity 0.5
    /// move    ZzzEffect.cpp:7902 fade in on action 0 to 0.3 facing the target, then action 1: frames 4..12 charge (Distance / 13,
    ///         from 10: / 45), frame clamp 12, Alpha -0.3F from frame 11; LifeTime &lt; 20: action 0, Alpha -0.05F;
    ///         Z = terrain; frame &gt;= 11: CreateBomb3(Position, SubType) + rand_fps_check(2) SOUND_SUMMON_EXPLOSION;
    ///         bones 13, 23, 39, 49, 3, 4, 5, 61 (+ old - new position): 3 x FIRE_CURSEDLICH sub 2 (Scale 5 / 4 / 3, Alpha * 0.3)
    /// fire    ZzzEffectParticle.cpp:293 (sub 2: LifeTime rand%12+8, Scale (rand%30+20)*0.01*Scale, Rotation rand%360,
    ///         Gravity (rand%15+15)*0.01), 4344 (Scale -= (rand%20+10)*0.004F, Z += Gravity*25F), 9094 default sprite
    /// render  ZzzObject.cpp:1716 RenderBody(RENDER_TEXTURE, Alpha) with BodyLight terrain + (1,1,1).
    /// Not ported: the second RenderBody(RENDER_BRIGHT | RENDER_CHROME7) pass (needs a CHROME7 overlay mode with the stale
    /// sahaffur.tga binding), SetAction animation blending.
    /// </summary>
    internal sealed class SahamuttEffect : SummonBookEffectBase
    {
        private const string ModelPath = "Skill/summon_sahamutt.bmd";
        private const string FireTexturePath = "Effect/firehik02.jpg"; // BITMAP_FIRE_CURSEDLICH
        private const string SummonSound = "Sound/SE_Ch_summoner_skill05_explosion01.wav";
        private const string ExplosionSound = "Sound/SE_Ch_summoner_skill05_explosion03.wav";
        private const int MaxFires = 640;
        private static readonly int[] FireBones = { 13, 23, 39, 49, 3, 4, 5, 61 };
        private static readonly Vector3[] Palette =
        {
            new(1f, 0.6f, 0.4f), new(1f, 0.5f, 0f), new(1f, 0.5f, 0f), new(1f, 0.5f, 0.8f),
        };

        private struct Fire
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Fire[] _fires = new Fire[MaxFires];
        private readonly SourceBomb3Pool _bombs = new(96, 256);
        private SourceModelRenderer? _model;
        private Texture2D? _fireTexture, _explosionTexture, _magic1Texture, _sparkTexture;
        private int _fireCount;

        private bool _live;
        private Vector3 _position;
        private Vector3 _renderOrigin;
        private float _yaw;
        private float _alpha;
        private float _scale;
        private float _lifeTime;
        private float _frame;
        private int _action;
        private float _distance;
        private float _boomGlow;
        private static readonly Vector3 BodyGlow = new(1f, 0.5f, 0.2f);
        private static readonly Vector3 BoomGlow = new(1f, 0.55f, 0.25f);

        public SahamuttEffect(WalkerObject caster, Vector3 targetTileCorner)
            : base(caster, targetTileCorner, 4096)
        {
        }

        protected override Vector3[] CastingPalette => Palette;

        protected override async Task LoadCreatureContent()
        {
            _fireTexture = await LoadTexture(FireTexturePath);
            _explosionTexture = await LoadTexture(SourceBomb3Pool.ExplosionTexturePath);
            _magic1Texture = await LoadTexture(SourceBomb3Pool.Magic1TexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            _model = await LoadModel(ModelPath, -1);
            if (_model != null)
            {
                _model.UseNormalBlend();
                _model.AnimationSpeed = 0f;
            }
        }

        protected override void OnSummon()
        {
            Vector3 position = CasterPosition;
            position.X += FPSCounter.Instance.RandFPSCheck(2) ? MuGame.Random.Next(300) + 150 : -(MuGame.Random.Next(250) + 150);
            position.Y += FPSCounter.Instance.RandFPSCheck(2) ? MuGame.Random.Next(300) + 150 : -(MuGame.Random.Next(250) + 150);

            _live = true;
            _position = position;
            _renderOrigin = position;
            _yaw = CastYaw;
            _lifeTime = 80f;
            _scale = SummonLevel == 2 ? 0.7f : SummonLevel == 1 ? 0.5f : 0.35f;
            _alpha = 0f;
            _action = 0;
            _frame = 0f;
            SoundController.Instance.PlayBuffer(SummonSound);
        }

        private void SetAction(int action)
        {
            if (_action == action)
                return;
            _action = action;
            _frame = 0f;
        }

        protected override void TickCreature(float factor)
        {
            MoveFires(factor);
            _bombs.Move(factor, World?.Terrain);
            UpdateLights(factor);

            if (!_live)
                return;

            Vector3 target = TargetCorner;
            Vector3 old = _position;

            if (_lifeTime < 20f)
            {
                SetAction(0);
                _alpha -= 0.05f * factor;
            }
            else if (_action == 0 && _alpha < 0.3f)
            {
                _alpha += 0.05f * factor;
                Face(target);
            }
            else
            {
                SetAction(1);
                if (_frame >= 12f)
                    _frame = 12f;

                if (_frame >= 11f)
                    _alpha = _alpha > 0f ? _alpha - 0.3f * factor : 0f;
                else if (_frame < 3f && _alpha < 0.7f)
                    _alpha += 0.05f * factor;

                if (_frame > 4f && _frame < 12f)
                {
                    float step = _frame < 10f ? _distance / -13f : _distance / -45f;
                    _position += SourceJointMath.Rotate(new Vector3(0f, step, 0f), new Vector3(0f, 0f, _yaw)) * factor;
                }
                if (_frame <= 4f)
                    Face(target);
            }

            _position.Z = TerrainHeight(_position);
            Vector3 delta = old - _position;

            if (_frame >= 11f)
            {
                _bombs.Spawn(_position, SummonLevel, factor);
                if (FPSCounter.Instance.RandFPSCheck(2))
                    SoundController.Instance.PlayBuffer(ExplosionSound);
            }

            EmitFires(delta);

            // Epilogue: PlayAnimation(Velocity 0.5), LifeTime -= F.
            _frame += 0.5f * factor;
            _lifeTime -= factor;
            if (_lifeTime <= 0f)
                _live = false;
        }

        private void UpdateLights(float factor)
        {
            float time = (float)(FPSCounter.Instance.WorldTime * 0.001);
            // Slot 0: fiery body glow, follows the model alpha.
            if (_live && _alpha > 0f)
                Lights.Set(0, _position + new Vector3(0f, 0f, 80f), BodyGlow, 240f,
                    MathHelper.Clamp(_alpha / 0.7f, 0f, 1f) * EffectLights.Flicker(time, 0.9f, 0.15f));
            else
                Lights.Hide(0);

            // Slot 1: explosion while the bombs are spawned (frame >= 11), fading after.
            bool exploding = _live && _frame >= 11f;
            _boomGlow = exploding ? MathF.Min(1f, _boomGlow + 0.5f * factor) : MathF.Max(0f, _boomGlow - 0.08f * factor);
            if (_boomGlow > 0f)
                Lights.Set(1, _position + new Vector3(0f, 0f, 60f), BoomGlow, 380f,
                    1.8f * _boomGlow * EffectLights.Flicker(time, 3.7f, 0.25f));
            else
                Lights.Hide(1);
        }

        private void Face(Vector3 target)
        {
            _yaw = SourceJointMath.CreateAngle2D(_position, target);
            float dx = target.X - _position.X;
            float dy = target.Y - _position.Y;
            _distance = MathF.Sqrt(dx * dx + dy * dy);
        }

        private void EmitFires(Vector3 delta)
        {
            if (_model == null)
                return;

            // BMD::Animation with the BodyOrigin / BodyScale of the last render; only the 8 bone chains are evaluated.
            Matrix world = Matrix.CreateScale(_scale)
                * Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(new Vector3(0f, 0f, MathHelper.ToRadians(_yaw))))
                * Matrix.CreateTranslation(_renderOrigin);
            var light = new Vector3(_alpha * 0.3f);
            for (int i = 0; i < FireBones.Length; i++)
            {
                if (!_model.TryComputeBoneAt(_action, _frame, FireBones[i], out Matrix bone))
                    continue;
                Vector3 position = Vector3.Transform(bone.Translation, world) + delta;
                for (int s = 5; s >= 3; s--)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnFire(position, light, s);
            }
        }

        private void SpawnFire(Vector3 position, Vector3 light, float scale)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (f.Live)
                    continue;
                f.Live = true;
                f.Position = position;
                f.Light = light;
                f.LifeTime = MuGame.Random.Next(12) + 8;
                f.Scale = (MuGame.Random.Next(30) + 20) * 0.01f * scale;
                f.Rotation = MuGame.Random.Next(360);
                f.Gravity = (MuGame.Random.Next(15) + 15) * 0.01f;
                _fireCount++;
                return;
            }
        }

        private void MoveFires(float factor)
        {
            if (_fireCount == 0)
                return;
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                {
                    f.Live = false;
                    _fireCount--;
                    continue;
                }
                f.Scale -= (MuGame.Random.Next(20) + 10) * 0.004f * factor;
                f.Position.Z += f.Gravity * 25f * factor;
            }
        }

        protected override bool HasCreatureContent() => _live || _fireCount > 0 || _boomGlow > 0f || _bombs.HasLive;

        protected override void DrawCreature()
        {
            if (_live && _model != null && _alpha >= 0.01f)
            {
                _renderOrigin = _position;
                _model.CurrentAction = _action;
                _model.SetAnimationTime(_frame);
                _model.Update(LastGameTime);
                Vector3 light = (World?.Terrain?.EvaluateTerrainLight(_position.X, _position.Y) ?? Vector3.Zero) + Vector3.One;
                _model.DrawInstance(_position, new Vector3(0f, 0f, MathHelper.ToRadians(_yaw)), _scale, light, 1f,
                    MathF.Round(MathF.Min(_alpha, 1f) * 50f) / 50f);
            }

            _bombs.Draw(Batch, _explosionTexture, _magic1Texture, _sparkTexture);

            if (_fireCount > 0 && Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Fire f = ref _fires[i];
                    if (f.Live)
                        Batch.AddSprite(f.Position, f.Scale, f.Light, f.Rotation);
                }
                Batch.End();
            }
        }
    }
}
