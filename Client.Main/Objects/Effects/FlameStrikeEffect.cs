#nullable enable
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    // AT_SKILL_FLAME_STRIKE 236: WSclient.cpp:5219, ZzzCharacter.cpp:2832/4610;
    // MODEL_EFFECT_FLAME_STRIKE: ZzzEffect.cpp:110/4958/15171/18615.
    internal sealed class FlameStrikeEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Effect/FlameStrike.bmd";
        private const string BlurTexturePath = "Effect/motion_blur_r.jpg";
        private const string LavaTexturePath = "Effect/Lava.jpg";
        private const string SoundPath = "Sound/flame_strike.wav";
        private const float EffectScale = 0.9f;
        private readonly SourceObjectBlur _blur1 = new();
        private readonly SourceObjectBlur _blur2 = new();
        private readonly SourceObjectBlur _blur3 = new();
        private SourceModelRenderer? _renderer;
        private Texture2D? _blurTexture;
        private Texture2D? _lavaTexture;
        private Vector3 _origin;
        private Vector3 _ownerLight;
        private float _life = 35f;
        private float _alpha;
        private float _velocity;
        private double _frame;
        private bool _detached;
        private bool _dead;

        public FlameStrikeEffect(WalkerObject caster)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _origin = caster.WorldPosition.Translation;
            _ownerLight = caster.Light;
            int action = (int)PlayerAction.PlayerSkillFlamestrike;
            var actions = caster.Model?.Actions;
            _velocity = actions != null && action < actions.Length && actions[action] != null
                ? actions[action].PlaySpeed : 0.69f;
        }

        protected override async Task LoadEffectContent()
        {
            _blurTexture = await LoadTexture(BlurTexturePath);
            _lavaTexture = await LoadTexture(LavaTexturePath);
            _renderer = await LoadModel(ModelPath, blendMesh: -2);
            if (_renderer != null)
            {
                _renderer.HoldLastFrame = true;
                _renderer.AnimationSpeed = 0f;
            }
        }

        protected override void OnStart() => PlayUnmappedSound(236, SoundPath);

        protected override void OnTick(float factor)
        {
            _blur1.Move();
            _blur2.Move();
            _blur3.Move();
            if (_life < 20f && _alpha < 0.1f)
            {
                _dead = true;
                return;
            }
            if (_life < 20f)
                _alpha -= 0.1f;
            else if (_alpha < 1f)
                _alpha += 0.1f;

            double ownerFrame = Caster.AnimationFramePosition;
            if (ownerFrame > 13f)
                _detached = true;
            else if (!_detached && Caster.CurrentAction == (int)PlayerAction.PlayerSkillFlamestrike)
            {
                _frame = ownerFrame;
                CreateBlurSamples(ownerFrame, factor);
            }
            _frame += _velocity * factor;
            _life -= factor;
            if (_life <= 0f)
                _dead = true;
        }

        private void CreateBlurSamples(double ownerFrame, float factor)
        {
            if (_renderer?.Model == null || Caster.Model?.Actions == null)
                return;
            int action = (int)PlayerAction.PlayerSkillFlamestrike;
            if (action >= Caster.Model.Actions.Length || Caster.Model.Actions[action] == null)
                return;

            float ownerSpeed = Caster.Model.Actions[action].PlaySpeed * factor;
            float effectSpeed = _velocity * factor;
            double ownerSample = ownerFrame - ownerSpeed;
            double effectSample = _frame - effectSpeed;
            Matrix yaw = Matrix.CreateRotationZ(Caster.Angle.Z);
            for (int i = 0; i < 10; i++)
            {
                if (ownerSample >= 5f && ownerSample <= 13f &&
                    Caster.TryComputeSourceBlurBoneAt(action, ownerSample, PlayerObject.LeftHandBoneIndex, out Matrix parent) &&
                    _renderer.TryComputeSourceBlurBoneAt(0, effectSample, 9, out Matrix bone9) &&
                    _renderer.TryComputeSourceBlurBoneAt(0, effectSample, 6, out Matrix bone6) &&
                    _renderer.TryComputeSourceBlurBoneAt(0, effectSample, 8, out Matrix bone8) &&
                    _renderer.TryComputeSourceBlurBoneAt(0, effectSample, 5, out Matrix bone5))
                {
                    Matrix parentWorld = parent * yaw;
                    Vector3 p9 = _origin + (bone9 * parentWorld).Translation * EffectScale;
                    Vector3 p6 = _origin + (bone6 * parentWorld).Translation * EffectScale;
                    Vector3 p8 = _origin + (bone8 * parentWorld).Translation * EffectScale;
                    Vector3 p5 = _origin + (bone5 * parentWorld).Translation * EffectScale;
                    _blur1.Add(p9, p6);
                    _blur2.Add(p9, p6);
                    _blur3.Add(p8, p5);
                }
                ownerSample += ownerSpeed / 10f;
                effectSample += effectSpeed / 10f;
            }
        }

        protected override bool HasActiveContent() => !_dead;

        protected override void OnDraw()
        {
            if (_renderer != null && _alpha > 0f)
            {
                Matrix[] bones = Caster.GetBoneTransforms();
                if (bones != null && PlayerObject.LeftHandBoneIndex < bones.Length)
                {
                    float v = ((int)(FPSCounter.Instance.WorldTime * 0.05) % 16 / 4) * 0.25f;
                    _renderer.SetMeshScrollV(1, v);
                    _renderer.SetAnimationTime(_frame);
                    _renderer.Update(LastGameTime);
                    Vector3 light = _ownerLight + (World?.Terrain?.EvaluateTerrainLight(_origin.X, _origin.Y) ?? Vector3.Zero);
                    Vector3 anchor = (bones[PlayerObject.LeftHandBoneIndex] * Caster.WorldPosition).Translation;
                    _renderer.DrawInstance(anchor, Vector3.Zero, EffectScale, light, _alpha, _alpha);
                }
            }

            if (Batch.Begin(_blurTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.PointClamp))
            {
                _blur1.Append(Batch);
                _blur2.Append(Batch);
                Batch.End();
            }
            if (Batch.Begin(_lavaTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                _blur3.Append(Batch);
                Batch.End();
            }
        }

        protected override void DisposeEffect() => _renderer?.Dispose();
    }
}
