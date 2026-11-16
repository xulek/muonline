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
    /// Combo (AT_SKILL_COMBO = 59), SourceMain5.2:
    /// packet  WSclient.cpp:4487       CreateEffect(MODEL_COMBO) on the caster, SOUND_COMBO (packet handler)
    /// combo   ZzzEffect.cpp:3261 (init, 60 x BITMAP_LIGHT sub 0), 12848 (move), Skill/combo.bmd, BlendMesh -2
    /// light   ZzzEffectJoint.cpp:2374 (init), 6403 (move), RenderJoints default
    /// </summary>
    internal sealed class ComboEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Skill/combo.bmd";      // MODEL_COMBO
        private const string LightTexturePath = "Effect/flare01.jpg"; // BITMAP_LIGHT
        private const int RayCount = 60;
        private const float ModelVelocity = 0.3f;

        private struct Ray
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Light;
            public float Scale;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly Ray[] _rays = new Ray[RayCount];
        private SourceModelRenderer? _model;
        private Texture2D? _lightTexture;

        private bool _comboLive;
        private Vector3 _comboPosition;
        private float _comboLifeTime;
        private float _comboScale;
        private float _comboGravity;
        private float _comboBlendMeshLight;
        private float _comboFrame;

        public ComboEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 4096)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
            _model = await LoadModel(ModelPath, -2);
            if (_model != null)
                _model.AnimationSpeed = 0f;
        }

        protected override void OnStart()
        {
            _comboLive = true;
            _comboLifeTime = 20f;
            _comboGravity = 0.1f;
            _comboBlendMeshLight = 1f;
            _comboScale = 0.9f;
            _comboPosition = CasterPosition + new Vector3(0f, 0f, 50f * SourceJointMath.FrameFactor);

            int maxTails = SourceJointMath.ScaledMaxTails(30);
            for (int j = 0; j < RayCount; j++)
            {
                ref Ray r = ref _rays[j];
                float scale = MuGame.Random.Next(40) + 70;
                var angle = new Vector3(-(float)MuGame.Random.Next(40) + 30f, 0f, MuGame.Random.Next(360));
                SourceJointMath.AngleBasis(angle, out r.Right, out r.Forward, out r.Up);
                r.Live = true;
                r.Position = _comboPosition;
                r.Scale = scale;
                r.Velocity = MuGame.Random.Next(10) + 10f;
                r.Light = new Vector3(0.1f, 0.5f, 1f);
                r.LifeTime = 20f;
                r.Trail = new SourceTrail(maxTails);
                r.Trail.Push(r.Position, r.Right, r.Up, r.Scale);
            }
        }

        protected override void OnTick(float factor)
        {
            if (_comboLive)
            {
                if (_comboLifeTime > 4f)
                {
                    _comboScale += _comboGravity * factor;
                    _comboGravity += 0.1f * factor;
                }
                _comboBlendMeshLight *= MathF.Pow(1f / 1.4f, factor);
                _comboFrame += ModelVelocity * factor;

                _comboLifeTime -= factor;
                if (_comboLifeTime <= 0f)
                    _comboLive = false;
            }

            float lightDecay = MathF.Pow(1f / 1.2f, factor);
            for (int j = 0; j < RayCount; j++)
            {
                ref Ray r = ref _rays[j];
                if (!r.Live)
                    continue;

                if (r.LifeTime > 16f)
                {
                    Vector3 step = -r.Forward * (r.Velocity * factor);
                    for (int i = 0; i < 10; i++)
                    {
                        r.Trail.Push(r.Position, r.Right, r.Up, r.Scale);
                        r.Position += step;
                    }
                }
                r.Light *= lightDecay;

                r.LifeTime -= factor;
                if (r.LifeTime < 0f)
                    r.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_comboLive)
                return true;
            for (int j = 0; j < RayCount; j++)
                if (_rays[j].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int j = 0; j < RayCount; j++)
                {
                    ref Ray r = ref _rays[j];
                    if (r.Live)
                        Batch.AddTrail(r.Trail, SourceQuadBatch.TrailFaces.Both, r.Light,
                            SourceQuadBatch.TrailShade.Flat, r.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_comboLive && _model != null)
            {
                _model.SetAnimationTime(_comboFrame);
                _model.Update(LastGameTime);
                _model.DrawInstance(_comboPosition, Vector3.Zero, _comboScale,
                    World?.Terrain?.EvaluateTerrainLight(_comboPosition.X, _comboPosition.Y) ?? Vector3.One,
                    MathF.Max(_comboBlendMeshLight, 0.003f));
            }
        }

        protected override void DisposeEffect()
        {
            _model?.Dispose();
        }
    }
}
