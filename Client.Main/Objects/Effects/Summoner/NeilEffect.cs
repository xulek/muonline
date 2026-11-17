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
    /// Requiem (AT_SKILL_SUMMON_REQUIEM = 224): MODEL_SUMMONER_SUMMON_NEIL (Skill/summon_neil.bmd), SourceMain5.2:
    /// spawn   SummonSystem.cpp:150 caster + Rotate((0,-100,0), Angle), HeadTargetAngle = target tile corner, SOUND_SUMMON_NEIL
    /// init    ZzzEffect.cpp:806 LifeTime 80, Scale 1, Alpha 0, Velocity 0.35, Z += 10F; knives 817 (LifeTime 50, Alpha 1),
    ///         grounds 824 (LifeTime 50, Alpha 0)
    /// move    ZzzEffect.cpp:7992 Alpha -0.05 below 20 else +0.04 to 0.7 per frame; frame &gt; 8: FpsChecked NIFE1 (+2 from level 1,
    ///         +3 from level 2) at the target; frame &gt; 10: FpsChecked GROUND1 at Rotate((0,-60,0)) and GROUND1..3 at the target,
    ///         SOUND_SUMMON_REQUIEM; knives 8029 (Alpha -0.05 below 20 else +0.05 to 1), grounds 8037 (-0.05 / +0.3)
    /// render  ZzzObject.cpp:1721 Neil: meshes 0, 2 additive x Alpha below 0.7 else RENDER_TEXTURE (Alpha), BodyLight terrain + 1;
    ///         mesh 1 additive (1, 0, 0) x Alpha; BITMAP_LIGHT sprites (1, 0, 0) at bones 51..59; frames 0..10 of action 0:
    ///         10 x CreateObjectBlur(bones 51 -> 59, (1, 0, 0), type 2) per frame over the last Velocity*F frames;
    ///         1773 knives: meshes 0, 2 RENDER_TEXTURE, mesh 1 additive (1, 0, 0) x Alpha; 1780 grounds: mesh 0 additive x Alpha,
    ///         mesh 1 additive x Alpha with U = -WorldTime*0.001
    /// Ended actions (PlayAnimation false) hold the last key.
    /// </summary>
    internal sealed class NeilEffect : SummonBookEffectBase
    {
        private const string NeilPath = "Skill/summon_neil.bmd";
        private const string LightTexturePath = "Effect/flare01.jpg";        // BITMAP_LIGHT
        private const string BlurTexturePath = "Effect/motion_blur_r.jpg";   // BITMAP_BLUR + 2
        private const string SummonSound = "Sound/SE_Ch_summoner_skill06_requiem01.wav";
        private const string RequiemSound = "Sound/SE_Ch_summoner_skill06_requiem02.wav";
        private const float NeilVelocity = 0.35f;
        private const float PartVelocity = 0.3f;
        private static readonly Vector3 Red = new(1f, 0f, 0f);
        private static readonly Vector3 RedGlow = new(1f, 0.15f, 0.1f);
        private static readonly string[] KnifePaths = { "Skill/nelleff_nife01.bmd", "Skill/nelleff_nife02.bmd", "Skill/nelleff_nife03.bmd" };
        private static readonly string[] GroundPaths = { "Skill/nell_nifegrund01.bmd", "Skill/nell_nifegrund02.bmd", "Skill/nell_nifegrund03.bmd" };
        private static readonly Vector3[] Palette =
        {
            new(0.7f, 0.7f, 1f), new(0f, 0.7f, 1f), new(0f, 0f, 1f), new(0.8f, 0.5f, 1f),
        };

        private struct Part
        {
            public bool Live;
            public bool Ground;
            public int Model;
            public Vector3 Position;
            public float LifeTime;
            public float Alpha;
            public float Frame;
        }

        private readonly Part[] _parts = new Part[8];
        private readonly SourceObjectBlur _blur = new();
        private readonly SourceModelRenderer?[] _knifeBody = new SourceModelRenderer?[3];
        private readonly SourceModelRenderer?[] _knifeGlow = new SourceModelRenderer?[3];
        private readonly SourceModelRenderer?[] _ground = new SourceModelRenderer?[3];
        private readonly SourceModelRenderer?[] _groundScroll = new SourceModelRenderer?[3];
        private SourceModelRenderer? _neilBright, _neilBody, _neilGlow;
        private Texture2D? _lightTexture, _blurTexture;
        private int _neilKeys = 13;
        private readonly int[] _knifeKeys = { 1, 1, 1 };

        private bool _live;
        private Vector3 _position;
        private float _yaw;
        private float _alpha;
        private float _lifeTime;
        private float _frame;
        private int _skill;
        private int _spriteCount;
        private float _partGlow;
        private readonly Vector3[] _sprites = new Vector3[9];

        public NeilEffect(WalkerObject caster, Vector3 targetTileCorner)
            : base(caster, targetTileCorner, 2048)
        {
        }

        protected override Vector3[] CastingPalette => Palette;

        private async Task<SourceModelRenderer?> Load(string path, int blendMesh, int visibleMask, bool normal)
        {
            var r = new SourceModelRenderer(path, blendMesh) { VisibleMeshMask = visibleMask, AnimationSpeed = 0f };
            if (normal)
                r.UseNormalBlend();
            return await r.LoadInto(World) ? r : null;
        }

        protected override async Task LoadCreatureContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
            _blurTexture = await LoadTexture(BlurTexturePath);
            _neilBright = await Load(NeilPath, -2, 0b101, false);
            _neilBody = await Load(NeilPath, -1, 0b101, true);
            _neilGlow = await Load(NeilPath, -2, 0b010, false);
            if (_neilBody?.Model?.Actions is { Length: > 0 } actions)
                _neilKeys = actions[0].NumAnimationKeys;
            for (int i = 0; i < 3; i++)
            {
                _knifeBody[i] = await Load(KnifePaths[i], -1, 0b101, true);
                _knifeGlow[i] = await Load(KnifePaths[i], -2, 0b010, false);
                if (_knifeBody[i]?.Model?.Actions is { Length: > 0 } knifeActions)
                    _knifeKeys[i] = knifeActions[0].NumAnimationKeys;
                _ground[i] = await Load(GroundPaths[i], -2, 0b001, false);
                _groundScroll[i] = await Load(GroundPaths[i], -2, 0b010, false);
            }
        }

        protected override void OnSummon()
        {
            _live = true;
            _yaw = CastYaw;
            _position = CasterPosition + SourceJointMath.Rotate(new Vector3(0f, -100f, 0f), CastAngle);
            _position.Z += 10f * SourceJointMath.FrameFactor;
            _lifeTime = 80f;
            _alpha = 0f;
            _frame = 0f;
            _skill = 0;
            SoundController.Instance.PlayBuffer(SummonSound);
        }

        protected override void TickCreature(float factor)
        {
            _blur.Move();
            for (int i = 0; i < _parts.Length; i++)
            {
                ref Part p = ref _parts[i];
                if (!p.Live)
                    continue;
                if (p.LifeTime < 20f) p.Alpha -= 0.05f;
                else if (p.Alpha < 1f) p.Alpha += p.Ground ? 0.3f : 0.05f;
                p.Frame += PartVelocity * factor;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }
            UpdateLights(factor);

            if (!_live)
                return;

            if (_lifeTime < 20f) _alpha -= 0.05f;
            else if (_alpha < 0.7f) _alpha += 0.04f;

            Vector3 target = TargetCorner;
            if (_frame > 8f && _skill == 0)
            {
                _skill = 1;
                for (int i = 0; i <= SummonLevel; i++)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        AddPart(false, i, target, 1f);
            }
            if (_frame > 10f && _skill == 1)
            {
                _skill = 2;
                Vector3 front = _position + SourceJointMath.Rotate(new Vector3(0f, -60f, 0f), new Vector3(0f, 0f, _yaw));
                if (FPSCounter.Instance.RandFPSCheck(1))
                    AddPart(true, 0, front, 0f);
                for (int i = 0; i <= SummonLevel; i++)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        AddPart(true, i, target, 0f);
                SoundController.Instance.PlayBuffer(RequiemSound);
            }

            _frame += NeilVelocity * factor;
            _lifeTime -= factor;
            if (_lifeTime <= 0f)
                _live = false;
        }

        private void UpdateLights(float factor)
        {
            // Slot 0: red glow over the knives / ground blades at the target, eased toward their strongest alpha.
            float target = 0f;
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i].Live && _parts[i].Alpha > target)
                    target = _parts[i].Alpha;
            target = MathF.Min(target, 1f);
            _partGlow = target > _partGlow ? MathF.Min(target, _partGlow + 0.25f * factor) : MathF.Max(target, _partGlow - 0.1f * factor);
            if (_partGlow > 0f)
                Lights.Set(0, TargetCorner + new Vector3(0f, 0f, 60f), RedGlow, 320f, 1.3f * _partGlow);
            else
                Lights.Hide(0);

            // Slot 1: Neil itself.
            if (_live && _alpha > 0f)
                Lights.Set(1, _position + new Vector3(0f, 0f, 100f), RedGlow, 200f, 0.8f * MathHelper.Clamp(_alpha / 0.7f, 0f, 1f));
            else
                Lights.Hide(1);
        }

        private void AddPart(bool ground, int model, Vector3 position, float alpha)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].Live)
                    continue;
                _parts[i] = new Part { Live = true, Ground = ground, Model = model, Position = position, LifeTime = 50f, Alpha = alpha };
                return;
            }
        }

        protected override bool HasCreatureContent()
        {
            if (_live || _blur.IsLive || _partGlow > 0f)
                return true;
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i].Live)
                    return true;
            return false;
        }

        private static float HeldFrame(float frame, int keys) => MathF.Min(frame, MathF.Max(0f, keys - 1));

        private static float Q(float value) => MathF.Round(MathF.Max(value, 0f) * 50f) / 50f;

        protected override void DrawCreature()
        {
            var terrain = World?.Terrain;
            Vector3 angle = new(0f, 0f, MathHelper.ToRadians(_yaw));
            _spriteCount = 0;

            if (_live && _alpha >= 0.01f && _neilBody != null)
            {
                Vector3 light = (terrain?.EvaluateTerrainLight(_position.X, _position.Y) ?? Vector3.Zero) + Vector3.One;
                float frame = HeldFrame(_frame, _neilKeys);
                if (_alpha < 0.7f)
                    DrawAnimated(_neilBright, frame, _position, angle, 1f, light, Q(_alpha), 1f);
                else
                    DrawAnimated(_neilBody, frame, _position, angle, 1f, light, 1f, Q(_alpha));
                DrawAnimated(_neilGlow, frame, _position, angle, 1f, Red, Q(_alpha), 1f);

                Matrix world = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(angle)) * Matrix.CreateTranslation(_position);
                for (int b = 51; b <= 59; b++)
                    if (_neilBody.TryComputeBoneAt(0, frame, b, out Matrix bone))
                        _sprites[_spriteCount++] = Vector3.Transform(bone.Translation, world);

                // CreateObjectBlur over the last Velocity*F frames, 10 samples, while action 0 is on frames 0..10.
                if (_frame <= 10f)
                {
                    float speed = NeilVelocity * SourceJointMath.FrameFactor;
                    float sample = _frame - speed;
                    for (int i = 0; i < 10; i++)
                    {
                        float f = MathF.Max(sample, 0f);
                        if (_neilBody.TryComputeBoneAt(0, f, 51, out Matrix start) && _neilBody.TryComputeBoneAt(0, f, 59, out Matrix end))
                            _blur.Add(Vector3.Transform(start.Translation, world), Vector3.Transform(end.Translation, world));
                        sample += speed / 10f;
                    }
                }
            }

            for (int i = 0; i < _parts.Length; i++)
            {
                ref Part p = ref _parts[i];
                if (!p.Live || p.Alpha < 0.01f)
                    continue;
                Vector3 light = (terrain?.EvaluateTerrainLight(p.Position.X, p.Position.Y) ?? Vector3.Zero) + Vector3.One;
                if (p.Ground)
                {
                    // One animation key: no per-frame bake.
                    _ground[p.Model]?.DrawInstance(p.Position, angle, 1f, light, Q(p.Alpha));
                    SourceModelRenderer? scroll = _groundScroll[p.Model];
                    if (scroll != null)
                    {
                        scroll.ScrollU = -(float)(FPSCounter.Instance.WorldTime * 0.001 % 1.0);
                        scroll.DrawInstance(p.Position, angle, 1f, light, Q(p.Alpha));
                    }
                }
                else
                {
                    float frame = HeldFrame(p.Frame, _knifeKeys[p.Model]);
                    DrawAnimated(_knifeBody[p.Model], frame, p.Position, angle, 1f, light, 1f, Q(p.Alpha));
                    DrawAnimated(_knifeGlow[p.Model], frame, p.Position, angle, 1f, Red, Q(p.Alpha), 1f);
                }
            }

            if (_blurTexture != null && Batch.Begin(_blurTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.PointClamp))
            {
                _blur.Append(Batch, Red);
                Batch.End();
            }

            if (_spriteCount > 0 && Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spriteCount; i++)
                    Batch.AddSprite(_sprites[i], 1f, Red, 0f);
                Batch.End();
            }
        }

        private void DrawAnimated(SourceModelRenderer? r, float frame, Vector3 position, Vector3 angle, float scale, Vector3 light,
            float blendMeshLight, float alpha)
        {
            if (r == null)
                return;
            r.SetAnimationTime(frame);
            r.Update(LastGameTime);
            r.DrawInstance(position, angle, scale, light, blendMeshLight, alpha);
        }
    }
}
