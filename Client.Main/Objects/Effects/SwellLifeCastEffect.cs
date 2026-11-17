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
    /// Swell Life (AT_SKILL_VITALITY = 48) cast burst, SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2678  (int)AttackTime &gt; 9 with PLAYER_SKILL_VITALITY
    /// release ZzzCharacter.cpp:4021  36 x BITMAP_JOINT_SPIRIT sub 2, 2 x BITMAP_MAGIC + 1 sub 4, SOUND_SWELLLIFE
    /// spirit  ZzzEffectJoint.cpp:601 (init), 3847 (move)
    /// flare   ZzzEffectJoint.cpp:1739 (init, BITMAP_FLARE sub 2), 5402 (move)
    /// decal   ZzzEffect.cpp:1224 (init), 19154 (RenderEffectShadows)
    /// </summary>
    internal sealed class SwellLifeCastEffect : SourceSkillEffectBase
    {
        private const string SpiritTexturePath = "Effect/JointSpirit01.jpg";  // BITMAP_JOINT_SPIRIT
        private const string FlareTexturePath = "Effect/Flare.jpg";           // BITMAP_FLARE
        private const string LightTexturePath = "Effect/flare01.jpg";         // BITMAP_LIGHT
        private const string CircleTexturePath = "Effect/Magic_Ground2.jpg";  // BITMAP_MAGIC + 1
        private const string SoundPath = "Sound/eSwellLife.wav";              // SOUND_SWELLLIFE

        private const int SkillId = 48;
        // AttackTime starts at 1 and the gate needs (int)AttackTime > 9: nine 25 Hz frames.
        private const float ReleaseDelaySeconds = 9f * 0.04f;
        private const int SpiritCount = 36;
        private const int MaxFlares = 64;

        private static readonly Vector3 SpiritSpriteLight = new(1f, 0.5f, 0.1f);

        private struct Spirit
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Direction;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public Vector3 SpriteLight;
            public float SpriteScale;
            public float SpriteRotation;
            public SourceTrail Trail;
        }

        private struct Flare
        {
            public bool Live;
            public bool Moving;
            public Vector3 Position;
            public Vector3 Angle;
            public float Rise;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Circle
        {
            public bool Live;
            public Vector3 Position;
            public float AngleZ;
            public float Scale;
            public float LifeTime;
        }

        private readonly Spirit[] _spirits = new Spirit[SpiritCount];
        private readonly Flare[] _flares = new Flare[MaxFlares];
        private readonly Circle[] _circles = new Circle[2];
        private Texture2D? _spiritTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _circleTexture;
        private static readonly Vector3 LifeColor = new(1f, 0.55f, 0.15f);
        private Vector3 _origin;
        private float _age;

        public SwellLifeCastEffect(WalkerObject caster)
            : base(caster, float.MaxValue, ReleaseDelaySeconds, 1024)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _spiritTexture = await LoadTexture(SpiritTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _circleTexture = await LoadTexture(CircleTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            _origin = origin;
            Vector3 start = origin + new Vector3(0f, 0f, 100f);
            int maxTails = SourceJointMath.ScaledMaxTails(3);
            int circle = 0;

            for (int i = 0; i < SpiritCount; i++)
            {
                var angle = new Vector3(-10f, 0f, i * 10f);
                SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);

                ref Spirit spirit = ref _spirits[i];
                spirit.Live = true;
                spirit.Position = start;
                spirit.StartPosition = start;
                spirit.Direction = -forward;
                spirit.Right = right;
                spirit.Up = up;
                spirit.Light = new Vector3(0.5f);
                spirit.Velocity = 50f;
                spirit.LifeTime = 20f;
                spirit.SpriteScale = 0f;
                spirit.Trail = new SourceTrail(maxTails);
                spirit.Trail.Push(start, right, up, 60f);

                if (i % 20 == 0)
                {
                    ref Circle c = ref _circles[circle++];
                    c.Live = true;
                    c.Position = origin;
                    c.AngleZ = angle.Z;
                    c.Scale = (MuGame.Random.Next(50) + 50) / 100f * 4f;
                    c.LifeTime = 40f;
                }
            }

            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            float fade = MathF.Pow(1f / 1.2f, factor);

            // slot 0: expanding burst as the 36 spirits fly out (20 frames); slot 1: ground circle glow (40 frames).
            _age += factor;
            float spread = MathHelper.Clamp(_age / 20f, 0f, 1f);
            Lights.Set(0, _origin + Vector3.UnitZ * 100f, LifeColor, 260f + 200f * spread, 1.6f * EffectLights.Flash(_age, 20f, 1.3f));
            Lights.Set(1, _origin + Vector3.UnitZ * 50f, LifeColor, 340f, 0.8f * EffectLights.Envelope(_age, 40f, 4f, 12f));

            for (int i = 0; i < SpiritCount; i++)
            {
                ref Spirit spirit = ref _spirits[i];
                if (!spirit.Live)
                    continue;

                spirit.Position += spirit.Direction * (spirit.Velocity * factor);

                if (spirit.LifeTime < 10f)
                {
                    spirit.Light *= fade;
                    spirit.SpriteLight = spirit.Light;
                }
                else
                {
                    spirit.SpriteLight = SpiritSpriteLight;
                    if (spirit.LifeTime > 18f && spirit.LifeTime < 20f && FpsCheck(1, factor))
                    {
                        SpawnFlare(new Vector3(
                            spirit.StartPosition.X + MuGame.Random.Next(200) - 100,
                            spirit.StartPosition.Y + MuGame.Random.Next(200) - 100,
                            spirit.StartPosition.Z - 200f), factor);
                    }
                }

                spirit.SpriteScale = 4f + (20f - spirit.LifeTime) / 5f;
                spirit.SpriteRotation = MuGame.Random.Next(360);
                spirit.Velocity += factor * 5f;

                spirit.Trail.Push(spirit.Position, spirit.Right, spirit.Up, 60f);

                spirit.LifeTime -= factor;
                if (spirit.LifeTime < 0f)
                    spirit.Live = false;
            }

            for (int i = 0; i < MaxFlares; i++)
            {
                ref Flare flare = ref _flares[i];
                if (!flare.Live)
                    continue;

                if (flare.LifeTime <= 25f)
                {
                    flare.Moving = true;
                    flare.Rise += 5f * factor;
                    flare.Position.Z += flare.Rise * factor;
                }

                flare.Trail.Push(flare.Position, flare.Angle, 40f);

                flare.LifeTime -= factor;
                if (flare.LifeTime < 0f)
                    flare.Live = false;
            }

            for (int i = 0; i < _circles.Length; i++)
            {
                ref Circle c = ref _circles[i];
                if (!c.Live)
                    continue;

                c.LifeTime -= factor;
                if (c.LifeTime <= 0f)
                    c.Live = false;
            }
        }

        private void SpawnFlare(Vector3 position, float factor)
        {
            for (int i = 0; i < MaxFlares; i++)
            {
                ref Flare flare = ref _flares[i];
                if (flare.Live)
                    continue;

                flare.Live = true;
                flare.Moving = false;
                flare.Position = position;
                flare.Angle = new Vector3(0f, 0f, MuGame.Random.Next(360));
                flare.Rise = MuGame.Random.Next(20) + 35;
                flare.LifeTime = 25 + MuGame.Random.Next(50);

                int maxTails = SourceJointMath.ScaledMaxTails(20);
                if (flare.Trail == null || flare.Trail.MaxTails != maxTails)
                    flare.Trail = new SourceTrail(maxTails);
                else
                    flare.Trail.Clear();
                flare.Trail.Push(position, flare.Angle, 40f);
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < SpiritCount; i++)
                if (_spirits[i].Live) return true;
            for (int i = 0; i < MaxFlares; i++)
                if (_flares[i].Live) return true;
            for (int i = 0; i < _circles.Length; i++)
                if (_circles[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_circleTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _circles.Length; i++)
                {
                    ref Circle c = ref _circles[i];
                    if (!c.Live)
                        continue;

                    float luminosity = 1f;
                    if (c.LifeTime < 5f)
                        luminosity -= (5f - c.LifeTime) * 0.2f;
                    if (luminosity == 1f)
                        luminosity = MathF.Sin((60f - c.LifeTime) * 0.05f) + 0.5f;

                    var light = new Vector3(luminosity, luminosity * 0.5f, luminosity * 0.1f);
                    Batch.AddTerrainDecal(World?.Terrain, c.Position.X, c.Position.Y, c.Scale, c.Scale, light, -c.AngleZ);
                }
                Batch.End();
            }

            if (Batch.Begin(_spiritTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SpiritCount; i++)
                {
                    ref Spirit spirit = ref _spirits[i];
                    if (!spirit.Live)
                        continue;

                    Batch.AddTrail(spirit.Trail, SourceQuadBatch.TrailFaces.Two, spirit.Light,
                        SourceQuadBatch.TrailShade.Flat, spirit.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < MaxFlares; i++)
                {
                    ref Flare flare = ref _flares[i];
                    // All tails coincide until the flare starts rising: nothing to draw.
                    if (!flare.Live || !flare.Moving)
                        continue;

                    Batch.AddTrail(flare.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One,
                        SourceQuadBatch.TrailShade.Flat, flare.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SpiritCount; i++)
                {
                    ref Spirit spirit = ref _spirits[i];
                    if (!spirit.Live || spirit.SpriteScale <= 0f)
                        continue;

                    Batch.AddSprite(spirit.Position, spirit.SpriteScale, spirit.SpriteLight, spirit.SpriteRotation);
                }
                Batch.End();
            }
        }
    }
}
