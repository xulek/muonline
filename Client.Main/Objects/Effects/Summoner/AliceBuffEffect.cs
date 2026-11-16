#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Thorns (AT_SKILL_ALICE_THORNS = 217), Berserker (218), Sleep (219), Blind (220), SourceMain5.2 (hand + gate: base class):
    /// packet  WSclient.cpp:4562-4616 AttackTime = 1, PLAYER_SKILL_SLEEP (Sleep: mount variants), sound
    /// release ZzzCharacter.cpp:5022 (Sleep / Blind / Thorns: BITMAP_MAGIC+1 sub 11 / 12 at the caster, rings sub 0 / 1 / 2 on the
    ///         target), 4573 (Berserker: BITMAP_MAGIC+1 sub 11 + rings sub 0 on the caster with (1, 0.1, 0.2), SOUND_SKILL_BERSERKER)
    /// circle  ZzzEffect.cpp:1224 (LifeTime 20), 19154 (Magic_Ground2 decal (20 - LifeTime) * 0.15, Light * luminosity, last 5
    ///         ticks fade, rot -Angle[2]; sub 12 minus)
    /// rings   MODEL_ALICE_BUFFSKILL_EFFECT / 2 (Effect/elshildring / elshildring2): ZzzEffect.cpp:898 (LifeTime 34 / 35, Scale
    ///         0.1 / 0.15, Alpha 0, BlendMeshLight 0, Angle[2] 0), 9133 (owner + 100F Z, Alpha / BlendMeshLight +0.05F above 20
    ///         else -0.05F, dead below 0, Angle[2] +-8F, Scale += 0.035F; sprites 2 x BITMAP_LIGHT 5.0, BITMAP_SHINY+5 2.0 / 1.0
    ///         at +-rot; 3 x CreateJointFpsChecked(BITMAP_JOINT_HEALING sub 15 / 16) from a 200 dome),
    ///         ZzzObject.cpp:608 RenderBody(RENDER_TEXTURE | RENDER_BRIGHT (sub 1: RENDER_DARK), Alpha, BlendMesh 0, BlendMeshLight)
    /// joint   ZzzEffectJoint.cpp:527 (LifeTime 10, MaxTails 2, Velocity 0; 16 minus), 3550 (Velocity += 4F, MoveHumming 10 to
    ///         the ring, Light *= (1/1.08)^F, BITMAP_SHINY+1 sprite (rand%4+4)*0.2 at the ring every frame).
    /// </summary>
    internal sealed class AliceBuffEffect : AliceSkillEffectBase
    {
        public enum Kind { Sleep, Blind, Thorns, Berserker }

        private const string RingModelPath = "Effect/elshildring.bmd";
        private const string Ring2ModelPath = "Effect/elshildring2.bmd";
        private const string MagicTexturePath = "Effect/Magic_Ground2.jpg";   // BITMAP_MAGIC + 1
        private const string LightTexturePath = "Effect/flare01.jpg";         // BITMAP_LIGHT
        private const string HealingTexturePath = "Effect/JointEnergy01.jpg"; // BITMAP_JOINT_HEALING
        private const string Shiny1TexturePath = "Effect/Shiny02.jpg";        // BITMAP_SHINY + 1
        private const string BerserkerSound = "Sound/Berserker.wav";

        private struct Ring
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
            public float Alpha;
            public float BlendMeshLight;
            public float Yaw;
            public float Scale;
        }

        private struct Healing
        {
            public AliceBuffEffect? Owner;
            public int Ring;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        // Shared across casts so the trails are allocated once: 2 rings x 3 joints per tick x LifeTime 10.
        private static readonly Healing[] SharedJoints = new Healing[256];

        private readonly WalkerObject? _target;
        private readonly int _sub;
        private readonly Vector3 _circleLight, _ringLight, _flareLight, _shinyLight, _jointLight;
        private readonly Ring[] _rings = new Ring[2];
        private readonly SourceModelRenderer?[] _ringModels = new SourceModelRenderer?[2];
        private Texture2D? _magicTexture, _lightTexture, _healingTexture, _shiny1Texture;
        private Vector3 _circleCenter;
        private float _circleYaw;
        private float _circleLife;
        private int _jointCount;

        public AliceBuffEffect(WalkerObject caster, WalkerObject? target, Kind kind)
            : base(caster, kind switch { Kind.Blind => Skill.Blind, Kind.Thorns => Skill.Thorns, Kind.Berserker => Skill.Berserker, _ => Skill.Sleep }, 512)
        {
            _target = kind == Kind.Berserker ? caster : target;
            _sub = kind switch { Kind.Blind => 1, Kind.Thorns => 2, _ => 0 };
            switch (kind)
            {
                case Kind.Sleep:
                    _circleLight = new Vector3(0.7f, 0.3f, 0.8f);
                    _ringLight = new Vector3(0.8f, 0.3f, 0.9f);
                    break;
                case Kind.Blind:
                    _circleLight = _ringLight = Vector3.One;
                    break;
                case Kind.Thorns:
                    _circleLight = _ringLight = new Vector3(0.8f, 0.5f, 0.2f);
                    break;
                default:
                    _circleLight = _ringLight = new Vector3(1f, 0.1f, 0.2f);
                    break;
            }
            _flareLight = _sub switch { 0 => new Vector3(0.8f, 0.1f, 0.9f), 1 => Vector3.One, _ => new Vector3(0.8f, 0.5f, 0.2f) };
            _shinyLight = _sub switch { 0 => new Vector3(0.7f, 0.6f, 0.9f), 1 => Vector3.One, _ => new Vector3(0.8f, 0.5f, 0.2f) };
            _jointLight = _sub switch { 0 => new Vector3(0.7f, 0.5f, 0.7f), 1 => Vector3.One, _ => new Vector3(0.8f, 0.5f, 0.2f) };
        }

        protected override async Task LoadReleaseContent()
        {
            _magicTexture = await LoadTexture(MagicTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _healingTexture = await LoadTexture(HealingTexturePath);
            _shiny1Texture = await LoadTexture(Shiny1TexturePath);
            _ringModels[0] = await LoadModel(RingModelPath, 0);
            _ringModels[1] = await LoadModel(Ring2ModelPath, 0);
            for (int i = 0; i < 2; i++)
            {
                SourceModelRenderer? r = _ringModels[i];
                if (r == null)
                    continue;
                r.AnimationSpeed = 0f;
                if (Minus)
                    r.BlendMeshState = Blendings.Negative; // RENDER_DARK
            }
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        protected override void OnRelease()
        {
            _circleCenter = CasterPosition;
            _circleYaw = CasterYawDegrees;
            _circleLife = 20f;

            if (TargetAlive)
            {
                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                Vector3 position = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 100f * factor);
                _rings[0] = new Ring { Live = true, Position = position, LifeTime = 34f, Scale = 0.1f };
                _rings[1] = new Ring { Live = true, Position = position, LifeTime = 35f, Scale = 0.15f };
            }

            if (SkillKind == Skill.Berserker)
                SoundController.Instance.PlayBuffer(BerserkerSound);
        }

        protected override void TickRelease(float factor)
        {
            if (_circleLife > 0f)
                _circleLife -= factor;

            bool alive = TargetAlive;
            for (int i = 0; i < 2; i++)
            {
                ref Ring ring = ref _rings[i];
                if (!ring.Live)
                    continue;
                if (!alive)
                {
                    ring.Live = false;
                    continue;
                }

                ring.Position = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 100f * factor);
                if (ring.LifeTime > 20f)
                {
                    ring.Alpha += 0.05f * factor;
                    ring.BlendMeshLight += 0.05f * factor;
                }
                else
                {
                    ring.Alpha -= 0.05f * factor;
                    ring.BlendMeshLight -= 0.05f * factor;
                    if (ring.Alpha < 0f)
                        ring.Live = false;
                }
                ring.Yaw += (i == 0 ? 8f : -8f) * factor;
                ring.Scale += 0.035f * factor;

                for (int j = 0; j < 3; j++)
                {
                    var angle = new Vector3(MuGame.Random.Next(90), 0f, MuGame.Random.Next(360));
                    Vector3 start = ring.Position - SourceJointMath.Rotate(new Vector3(0f, -200f, 0f), angle);
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnJoint(i, start, angle);
                }

                ring.LifeTime -= factor;
                if (ring.LifeTime <= 0f)
                    ring.Live = false;
            }

            MoveJoints(factor);
        }

        private void SpawnJoint(int ring, Vector3 start, Vector3 angle)
        {
            int maxTails = SourceJointMath.ScaledMaxTails(2);
            for (int i = 0; i < SharedJoints.Length; i++)
            {
                ref Healing h = ref SharedJoints[i];
                if (h.Owner != null)
                    continue;
                h.Owner = this;
                h.Ring = ring;
                h.Position = start;
                h.Angle = angle;
                h.Light = _jointLight;
                h.Velocity = 0f;
                h.LifeTime = 10f;
                if (h.Trail == null || h.Trail.MaxTails != maxTails)
                    h.Trail = new SourceTrail(maxTails);
                else
                    h.Trail.Clear();
                h.Trail.Push(start, angle, 5f);
                _jointCount++;
                return;
            }
        }

        private void MoveJoints(float factor)
        {
            if (_jointCount == 0)
                return;
            float fade = MathF.Pow(1f / 1.08f, factor);
            for (int i = 0; i < SharedJoints.Length; i++)
            {
                ref Healing h = ref SharedJoints[i];
                if (!ReferenceEquals(h.Owner, this))
                    continue;

                SourceJointMath.AngleBasis(h.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                h.Position += -forward * (h.Velocity * factor);
                h.Velocity += factor * 4f;
                SourceJointMath.MoveHumming(ref h.Position, ref h.Angle, _rings[h.Ring].Position, 10f, factor);
                h.Light *= fade;
                h.Trail.Push(h.Position, right, up, 5f);
                h.LifeTime -= factor;
                if (h.LifeTime < 0f)
                {
                    h.Owner = null;
                    _jointCount--;
                }
            }
        }

        protected override bool HasReleaseContent() => _circleLife > 0f || _rings[0].Live || _rings[1].Live || _jointCount > 0;

        protected override void DrawReleaseWorld()
        {
            var terrain = World?.Terrain;
            if (_circleLife > 0f && Batch.Begin(_magicTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float luminosity = _circleLife < 5f ? 1f - (5f - _circleLife) * 0.2f : 1f;
                float size = (20f - _circleLife) * 0.15f;
                Batch.AddTerrainDecal(terrain, _circleCenter.X, _circleCenter.Y, size, size, _circleLight * luminosity, -_circleYaw);
                Batch.End();
            }

            if (_jointCount > 0 && Batch.Begin(_healingTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SharedJoints.Length; i++)
                {
                    ref Healing h = ref SharedJoints[i];
                    if (ReferenceEquals(h.Owner, this))
                        Batch.AddTrail(h.Trail, SourceQuadBatch.TrailFaces.Both, h.Light, SourceQuadBatch.TrailShade.Flat, h.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            for (int i = 0; i < 2; i++)
            {
                ref Ring ring = ref _rings[i];
                SourceModelRenderer? r = _ringModels[i];
                if (!ring.Live || r == null)
                    continue;
                Vector3 light = (terrain?.EvaluateTerrainLight(ring.Position.X, ring.Position.Y) ?? Vector3.Zero) + _ringLight;
                r.DrawInstance(ring.Position, new Vector3(0f, 0f, MathHelper.ToRadians(ring.Yaw)), ring.Scale, light,
                    MathF.Round(ring.BlendMeshLight * 50f) / 50f);
            }
        }

        protected override void DrawReleaseSprites()
        {
            bool anyRing = _rings[0].Live || _rings[1].Live;
            if (anyRing && Batch.Begin(_lightTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < 2; i++)
                {
                    ref Ring ring = ref _rings[i];
                    if (!ring.Live)
                        continue;
                    Vector3 light = _flareLight * ring.Alpha;
                    Batch.AddSprite(ring.Position, 5f, light, 0f);
                    Batch.AddSprite(ring.Position, 5f, light, 0f);
                }
                Batch.End();
            }

            if (anyRing && Batch.Begin(ShinyTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float rot = (float)(FPSCounter.Instance.WorldTime * 0.0006 * 360.0 % 360.0);
                for (int i = 0; i < 2; i++)
                {
                    ref Ring ring = ref _rings[i];
                    if (!ring.Live)
                        continue;
                    Vector3 light = _shinyLight * ring.Alpha;
                    Batch.AddSprite(ring.Position, 2f, light, rot);
                    Batch.AddSprite(ring.Position, 1f, light, -rot);
                }
                Batch.End();
            }

            if (_jointCount > 0 && Batch.Begin(_shiny1Texture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SharedJoints.Length; i++)
                {
                    ref Healing h = ref SharedJoints[i];
                    if (ReferenceEquals(h.Owner, this))
                        Batch.AddSprite(_rings[h.Ring].Position, (MuGame.Random.Next(4) + 4) * 0.2f, h.Light, MuGame.Random.Next(360));
                }
                Batch.End();
            }
        }

        protected override void DrawReleaseParticles()
        {
        }

        protected override void DisposeEffect()
        {
            base.DisposeEffect();
            if (_jointCount == 0)
                return;
            for (int i = 0; i < SharedJoints.Length; i++)
                if (ReferenceEquals(SharedJoints[i].Owner, this))
                    SharedJoints[i].Owner = null;
            _jointCount = 0;
        }
    }
}
