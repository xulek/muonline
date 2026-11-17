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
    /// Infinity Arrow (AT_SKILL_INFINITY_ARROW = 77), SourceMain5.2:
    /// packet  WSclient.cpp:4544       CreateEffect(MODEL_INFINITY_ARROW, so), SOUND_INFINITYARROW (packet handler)
    /// effect  ZzzEffect.cpp:422 (sub 0 spawns carrier sub 1 + ARROW1-4), 6870 / 6887 / 6926 (move), 18514 (render)
    /// render  ZzzObject.cpp:1616 (carrier: no mesh, bones 1..9 except 5 emit BITMAP_FLARE+1 sub 16 every frame),
    ///         1628 (ARROW1-4: RENDER_TEXTURE | RENDER_BRIGHT with BodyLight = Light)
    /// flare   ZzzEffectJoint.cpp:2021/2185 (init), 5933 (move: spiral up, then homing on the carrier)
    /// Divergence: the bone-29 position uses VectorAddScaled(owner position, FPS_ANIMATION_FACTOR) in the original,
    /// which only lands on the owner at 25 FPS; the 25 FPS result is used.
    /// </summary>
    internal sealed class InfinityArrowEffect : SourceSkillEffectBase
    {
        private static readonly string[] ArrowPaths = { "Skill/arrowsre02.bmd", "Skill/arrowsre03.bmd", "Skill/arrowsre04.bmd", "Skill/arrowsre05.bmd" };
        private const string CarrierPath = "Skill/arrowsre01.bmd";       // MODEL_INFINITY_ARROW
        private const string FlareTexturePath = "Effect/Flare02.jpg";    // BITMAP_FLARE + 1 (GL_REPEAT)
        private const float ModelVelocity = 0.3f;
        private const int ChestBone = 29;

        private struct Flare
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly SourceModelRenderer?[] _arrows = new SourceModelRenderer?[4];
        private readonly Flare[] _flares = new Flare[384];
        private SourceModelRenderer? _carrier;
        private Texture2D? _flareTexture;
        private float _yaw;
        private float _frame;
        private float _arrowsLifeTime;   // ARROW1-3
        private float _arrow4LifeTime;   // ARROW4
        private float _carrierLifeTime;
        private float _arrowLight = 1f;
        private Vector3 _arrow4Light = new(1f, 0.5f, 0.3f);
        private float _arrow4Scale = 1f;
        private static readonly Vector3 GlowColor = new(1f, 0.5f, 0.3f);
        private float _age;

        public InfinityArrowEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 3072)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _flareTexture = await LoadTexture(FlareTexturePath);
            for (int i = 0; i < ArrowPaths.Length; i++)
            {
                var renderer = await LoadModel(ArrowPaths[i], -2);
                if (renderer != null)
                    renderer.AnimationSpeed = 0f;
                _arrows[i] = renderer;
            }
            _carrier = await LoadModel(CarrierPath, -2);
            if (_carrier != null)
                _carrier.AnimationSpeed = 0f;
        }

        protected override void OnStart()
        {
            _yaw = CasterYawDegrees;
            _arrowsLifeTime = 40f;
            _arrow4LifeTime = 15f;
            _carrierLifeTime = 40f;
        }

        private Vector3 ChestPosition => GetCasterBonePosition(ChestBone);

        protected override void OnTick(float factor)
        {
            _frame += ModelVelocity * factor;

            if (_arrowsLifeTime > 0f)
            {
                _arrowLight *= MathF.Pow(0.95f, factor);
                _arrowsLifeTime -= factor;
            }
            if (_arrow4LifeTime > 0f)
            {
                _arrow4Scale *= MathF.Pow(1.2f, factor);
                _arrow4Light *= MathF.Pow(0.9f, factor);
                _arrow4LifeTime -= factor;
            }
            if (_carrierLifeTime > 0f)
            {
                EmitCarrierFlares(factor);
                _carrierLifeTime -= factor;
            }

            MoveFlares(factor);

            // Chest glow while the arrows / flares gather (carrier 40 frames, flares fade by ~55).
            _age += factor;
            float glow = 1.1f * EffectLights.Envelope(_age, 55f, 3f, 25f) * EffectLights.Flicker(_age * 0.04f, 4.2f, 0.1f);
            if (glow > 0f)
                Lights.Set(0, ChestPosition, GlowColor, 220f, glow);
            else
                Lights.Hide(0);
        }

        private void EmitCarrierFlares(float factor)
        {
            if (_carrier == null)
                return;

            Vector3 carrierPosition = ChestPosition;
            _carrier.Position = carrierPosition;
            _carrier.Angle = new Vector3(0f, 0f, Caster.Angle.Z);
            _carrier.SetAnimationTime(_frame);
            _carrier.Update(LastGameTime);
            Matrix[]? bones = _carrier.GetBoneTransforms();
            if (bones == null)
                return;

            Matrix world = _carrier.WorldPosition;
            for (int idx = 1; idx <= 9 && idx < bones.Length; idx++)
            {
                if (idx == 5 || !FpsCheck(1, factor))
                    continue;
                SpawnFlare(Vector3.Transform(bones[idx].Translation, world));
            }
        }

        private void SpawnFlare(Vector3 position)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;

                f.Live = true;
                f.Position = position;
                f.Angle = new Vector3(0f, 0f, CasterYawDegrees);
                f.Light = new Vector3(1f, 0.5f, 0.3f);
                f.Velocity = 7f;
                f.LifeTime = 35f;
                int maxTails = SourceJointMath.ScaledMaxTails(8);
                if (f.Trail == null || f.Trail.MaxTails != maxTails)
                    f.Trail = new SourceTrail(maxTails);
                else
                    f.Trail.Clear();
                f.Trail.Push(position, f.Angle, 20f);
                return;
            }
        }

        private void MoveFlares(float factor)
        {
            Vector3 target = ChestPosition;
            float fade = MathF.Pow(1f / 1.2f, factor);
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;

                SourceJointMath.AngleBasis(f.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                f.Position += -forward * (f.Velocity * factor);

                if (f.LifeTime >= 25f)
                {
                    f.Angle.Z -= 10f * factor;
                    f.Position.Z += 2.5f * factor;
                }
                else
                {
                    f.Velocity = MathF.Min(f.Velocity + factor * 5f, 30f);
                    float oldAngle = f.Angle.Z;
                    float distance = SourceJointMath.MoveHumming(ref f.Position, ref f.Angle, target, f.Velocity, factor);
                    if (distance <= 70f && MathF.Abs(oldAngle - f.Angle.Z) > 20f && f.Velocity >= 20f)
                        f.Velocity -= factor * 10f;
                    if (f.LifeTime < 10f)
                        f.Light = new Vector3(f.Light.X * fade);
                }

                f.Trail.Push(f.Position, right, up, 20f);
                f.LifeTime -= factor;
                if (f.LifeTime < 0f)
                    f.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_arrowsLifeTime > 0f || _arrow4LifeTime > 0f || _carrierLifeTime > 0f)
                return true;
            for (int i = 0; i < _flares.Length; i++) if (_flares[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var angle = new Vector3(0f, 0f, MathHelper.ToRadians(_yaw));
            Vector3 owner = CasterPosition;

            if (_arrowsLifeTime > 0f)
            {
                for (int i = 0; i < 3; i++)
                {
                    SourceModelRenderer? r = _arrows[i];
                    if (r == null)
                        continue;
                    r.SetAnimationTime(_frame);
                    r.Update(LastGameTime);
                    r.DrawInstance(owner, angle, 1f, new Vector3(_arrowLight), 1f);
                }
            }

            if (_arrow4LifeTime > 0f && _arrows[3] != null)
            {
                SourceModelRenderer r = _arrows[3]!;
                r.SetAnimationTime(_frame);
                r.Update(LastGameTime);
                r.DrawInstance(ChestPosition, new Vector3(0f, 0f, Caster.Angle.Z), _arrow4Scale, _arrow4Light, 1f);
            }

            if (Batch.Begin(_flareTexture, Blendings.InverseSourceColorAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (f.Live)
                        Batch.AddTrail(f.Trail, SourceQuadBatch.TrailFaces.Two, f.Light, SourceQuadBatch.TrailShade.Flat, f.Trail.MaxTails - 1);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            for (int i = 0; i < _arrows.Length; i++)
                _arrows[i]?.Dispose();
            _carrier?.Dispose();
        }
    }
}
