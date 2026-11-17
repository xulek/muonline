#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dark Lord Summon party (AT_SKILL_PARTY_TELEPORT = 63), SourceMain5.2:
    /// packet   WSclient.cpp:4436       PLAYER_ATTACK_TELEPORT (ride / Fenrir variants)
    /// gate     ZzzCharacter.cpp:2739   (int)AttackTime &gt; 5 in the teleport actions
    /// release  ZzzCharacter.cpp:4224   MODEL_CIRCLE sub 2 (owner), MODEL_CIRCLE_LIGHT sub 3, SOUND_PART_TELEPORT
    /// circle   ZzzEffect.cpp:2230 (LifeTime 250, BlendMesh 0), 10793 (BlendMeshLight (250 - LifeTime) / LifeTime * 0.1),
    ///          ZzzObject.cpp:1514 (BodyLight (0.5,0.5,1), texture BITMAP_MAGIC_EMBLEM)
    /// light    ZzzEffect.cpp:2258 (LifeTime 250, BlendMesh 0), 10860 (BlendMeshLight min 0.5, TexCoordU -LifeTime * 0.01,
    ///          LifeTime &gt; 30 rand_fps_check(2): BITMAP_FLARE_BLUE particle sub 0 within 200, LifeTime &gt; 40:
    ///          BITMAP_FLARE_BLUE joint sub 19 at +600), ZzzObject.cpp:1508 (BodyLight (0.1,0.1,10))
    /// particle ZzzEffectParticle.cpp:174 / 3982 / 9163 (LifeTime 30 + rand%10, Velocity z rand%100/50, Scale 0.2,
    ///          Velocity z += 0.4F up to 8F, LifeTime &lt; 5: Light *= 0.98 + (0.02 - F))
    /// joint    ZzzEffectJoint.cpp:1630 / 1807 (MaxTails 20, Scale 40, LifeTime 25 + rand%50, Direction z -(rand%20+35)),
    ///          5475 (LifeTime &lt;= 25: Direction z -= 5F, Z += Direction z * F)
    /// Not ported: AddTerrainLight.
    /// </summary>
    internal sealed class PartySummonEffect : SourceSkillEffectBase
    {
        private const string CircleModelPath = "Skill/Circle01.bmd";       // MODEL_CIRCLE
        private const string CircleLightModelPath = "Skill/Circle02.bmd";  // MODEL_CIRCLE_LIGHT
        private const string EmblemTexturePath = "Effect/Magic_b.jpg";     // BITMAP_MAGIC_EMBLEM
        private const string FlareTexturePath = "Effect/flareBlue.jpg";    // BITMAP_FLARE_BLUE
        private const string SoundPath = "Sound/eSummon.wav";              // SOUND_PART_TELEPORT
        private const float ModelVelocity = 0.3f;
        private const int JointMaxTails = 20;
        private static readonly Vector3 CircleBodyLight = new(0.5f, 0.5f, 1f);
        private static readonly Vector3 CircleLightBodyLight = new(0.1f, 0.1f, 10f);

        private struct Flare
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Light;
            public float LifeTime;
        }

        private struct Joint
        {
            public bool Live;
            public Vector3 Position;
            public float DirectionZ;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly Flare[] _flares = new Flare[64];
        private readonly Joint[] _joints = new Joint[64];
        private SourceModelRenderer? _circle;
        private SourceModelRenderer? _circleLight;
        private Texture2D? _flareTexture;
        private Vector3 _origin;
        private Vector3 _angle;
        private float _circleLife;
        private float _lightLife;
        private float _frame;
        private static readonly Vector3 SummonColor = new(0.4f, 0.5f, 1f);

        public PartySummonEffect(WalkerObject caster)
            : base(caster, triggerFrame: float.MaxValue, triggerTimeoutSeconds: 5f * 0.04f, maxQuads: 64 + 64 * 200 * 2)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _circle = await LoadModel(CircleModelPath, blendMesh: 0);
            _circleLight = await LoadModel(CircleLightModelPath, blendMesh: 0);
            foreach (var r in new[] { _circle, _circleLight })
            {
                if (r == null)
                    continue;
                r.AnimationSpeed = 0f;
                r.UseNormalBlend();
            }
            _circle?.OverrideAllTextures(await LoadTexture(EmblemTexturePath));
            _flareTexture = await LoadTexture(FlareTexturePath);
        }

        protected override void OnStart()
        {
            _origin = CasterPosition;
            _angle = new Vector3(0f, 0f, CasterYawDegrees);
            _circleLife = 250f;
            _lightLife = 250f;
            SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_lightLife > 0f)
            {
                if (_lightLife > 30f && FpsCheck(2, factor))
                {
                    Vector3 position = _origin + SourceJointMath.Rotate(new Vector3(0f, MuGame.Random.Next(200), 0f),
                        new Vector3(0f, 0f, MuGame.Random.Next(360)));
                    if (FpsCheck(1, factor))
                        SpawnFlare(position);
                    if (_lightLife > 40f && FpsCheck(1, factor))
                        SpawnJoint(position + Vector3.UnitZ * 600f);
                }
                _lightLife -= factor;
            }

            if (_circleLife > 0f)
                _circleLife -= factor;
            _frame += ModelVelocity * factor;

            MoveFlares(factor);
            MoveJoints(factor);

            // slot 0: the summoning circle on the ground (250 frames); slot 1: the flare column above it.
            float age = 250f - _circleLife;
            float pulse = EffectLights.Flicker(age * 0.04f, 0.4f, 0.08f);
            Lights.Set(0, _origin + Vector3.UnitZ * 60f, SummonColor, 420f, 1f * EffectLights.Envelope(age, 250f, 10f, 40f) * pulse);
            float column = 250f - _lightLife;
            Lights.Set(1, _origin + Vector3.UnitZ * 320f, SummonColor, 300f, 0.6f * EffectLights.Envelope(column, 220f, 15f, 30f) * pulse);
        }

        private void SpawnFlare(Vector3 position)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;
                f = new Flare
                {
                    Live = true,
                    Position = position,
                    Velocity = new Vector3(0f, 0f, MuGame.Random.Next(100) / 50f),
                    Light = Vector3.One,
                    LifeTime = 30 + MuGame.Random.Next(10),
                };
                return;
            }
        }

        private void SpawnJoint(Vector3 position)
        {
            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint j = ref _joints[i];
                if (j.Live)
                    continue;
                int tails = SourceJointMath.ScaledMaxTails(JointMaxTails);
                if (j.Trail == null || j.Trail.MaxTails != tails)
                    j.Trail = new SourceTrail(tails);
                else
                    j.Trail.Clear();
                j.Live = true;
                j.Position = position;
                j.DirectionZ = -(MuGame.Random.Next(20) + 35);
                j.LifeTime = 25 + MuGame.Random.Next(50);
                return;
            }
        }

        private void MoveFlares(float factor)
        {
            float fade = 0.98f + (0.02f * 1 - factor); // verbatim
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f) { f.Live = false; continue; }
                f.Position += f.Velocity * factor; // MovePosition: the yaw does not change a Z velocity
                f.Velocity.Z = MathF.Min(8f * factor, f.Velocity.Z + 0.4f * factor);
                if (f.LifeTime < 5f)
                    f.Light = Vector3.Max(Vector3.Zero, f.Light * fade);
            }
        }

        private void MoveJoints(float factor)
        {
            var angle = new Vector3(0f, 0f, 45f);
            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint j = ref _joints[i];
                if (!j.Live)
                    continue;
                if (j.LifeTime <= 25f)
                {
                    j.DirectionZ -= 5f * factor;
                    j.Position.Z += j.DirectionZ * factor;
                }
                j.Trail.Push(j.Position, angle, 40f);
                j.LifeTime -= factor;
                if (j.LifeTime < 0f)
                    j.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_circleLife > 0f || _lightLife > 0f)
                return true;
            for (int i = 0; i < _flares.Length; i++) if (_flares[i].Live) return true;
            for (int i = 0; i < _joints.Length; i++) if (_joints[i].Live) return true;
            return false;
        }

        private static float Quantize(float v) => MathF.Round(v * 50f) / 50f;

        protected override void OnDraw()
        {
            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _joints.Length; i++)
                {
                    ref Joint j = ref _joints[i];
                    if (j.Live)
                        Batch.AddTrail(j.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat, j.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            var angle = new Vector3(0f, 0f, MathHelper.ToRadians(_angle.Z));
            if (_circle != null && _circleLife > 0f)
            {
                float bml = _circleLife > 240f ? (250f - _circleLife) * 0.1f : _circleLife * 0.1f;
                _circle.SetAnimationTime(_frame);
                _circle.Update(LastGameTime);
                _circle.DrawInstance(_origin, angle, 1f, CircleBodyLight, Quantize(bml));
            }

            if (_circleLight != null && _lightLife > 0f)
            {
                float bml = _lightLife >= 240f ? (250f - _lightLife) * 0.1f : _lightLife * 0.1f;
                _circleLight.ScrollU = -_lightLife * 0.01f;
                _circleLight.SetAnimationTime(_frame);
                _circleLight.Update(LastGameTime);
                _circleLight.DrawInstance(_origin, angle, 1f, CircleLightBodyLight, Quantize(MathF.Min(0.5f, bml)));
            }

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (f.Live)
                        Batch.AddSprite(f.Position, 0.2f, f.Light);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _circle?.Dispose();
            _circleLight?.Dispose();
            _circle = null;
            _circleLight = null;
        }
    }
}
