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
    /// Gigantic Storm (AT_SKILL_GIGANTIC_STORM = 237), SourceMain5.2:
    /// packet  WSclient.cpp:5222       PLAYER_SKILL_GIGANTICSTORM
    /// gate    ZzzCharacter.cpp:2837   AnimationFrame &gt; 7 -> AttackTime = 15
    /// release ZzzCharacter.cpp:4617   5 x CreateEffect(BITMAP_JOINT_THUNDER) at rot(0,200,0) by yaw + i * 72, SOUND_SKILL_GIGANTIC_STORM
    /// storm   ZzzEffect.cpp:1361 (LifeTime 20, StartPosition z + 800), 8521 (move: JOINT_THUNDER sub 16, BITMAP_MAGIC+1 sub 11,
    ///         BITMAP_SMOKE 54, MODEL_STONE sub 13 (Scale 0: invisible), BITMAP_CHROME_ENERGY2; Owner NULL: no bone sparks)
    /// decals  ZzzEffect.cpp:19186/19237 (MAGIC+1 sub 11), 19441 (CHROME_ENERGY2 1.5 x 1.5)
    /// </summary>
    internal sealed class GiganticStormEffect : SourceSkillEffectBase
    {
        private const string ThunderTexturePath = "Effect/JointThunder01.jpg"; // BITMAP_JOINT_THUNDER
        private const string MagicTexturePath = "Effect/Magic_Ground2.jpg";    // BITMAP_MAGIC + 1
        private const string SmokeTexturePath = "Effect/smoke01.jpg";          // BITMAP_SMOKE
        private const string EnergyTexturePath = "Effect/energy02.jpg";        // BITMAP_CHROME_ENERGY2
        private const string SoundPath = "Sound/gigantic_storm.wav";           // SOUND_SKILL_GIGANTIC_STORM

        private const int SkillId = 237;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private static readonly Vector3 StormLight = new(0.45f, 0.45f, 0.7f);

        private struct Storm
        {
            public Vector3 Position;
            public Vector3 Start;
        }

        private struct Thunder
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 TargetPosition;
            public float Velocity;
            public float Scale;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Decal
        {
            public bool Live;
            public bool Energy;
            public Vector3 Position;
            public Vector3 Light;
            public float AngleZ;
            public float LifeTime;
        }

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Storm[] _storms = new Storm[5];
        private readonly Thunder[] _thunders = new Thunder[64];
        private readonly Decal[] _decals = new Decal[96];
        private readonly Smoke[] _smokes = new Smoke[64];
        private Texture2D? _thunderTexture;
        private Texture2D? _magicTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _energyTexture;
        private float _lifeTime;
        private Vector3 _angle;

        public GiganticStormEffect(WalkerObject caster)
            : base(caster, 8f, ReleaseDelaySeconds, 2048)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _thunderTexture = await LoadTexture(ThunderTexturePath);
            _magicTexture = await LoadTexture(MagicTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _energyTexture = await LoadTexture(EnergyTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            float yaw = CasterYawDegrees;
            _angle = new Vector3(0f, 0f, yaw);
            for (int i = 0; i < _storms.Length; i++)
            {
                Vector3 p = origin + SourceJointMath.Rotate(new Vector3(0f, 200f, 0f), new Vector3(0f, 0f, yaw + i * 72f));
                _storms[i] = new Storm { Position = p, Start = p + new Vector3(0f, 0f, 800f) };
            }
            _lifeTime = 20f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_lifeTime > 0f)
            {
                for (int i = 0; i < _storms.Length; i++)
                {
                    ref Storm s = ref _storms[i];
                    float scale = (MuGame.Random.Next(400) + 100) / 10f;
                    Vector3 start = s.Start + new Vector3(MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50, 0f);
                    Vector3 end = s.Position + new Vector3(MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50, 0f);

                    if (FpsCheck(2, factor) && FpsCheck(1, factor))
                        SpawnThunder(start, end, scale);
                    if (_lifeTime > 10f && FpsCheck(2, factor) && FpsCheck(1, factor))
                        SpawnDecal(end, StormLight, false);
                    if (FpsCheck(4, factor) && FpsCheck(1, factor))
                        SpawnSmoke(end);
                    // rand_fps_check(4): MODEL_STONE1/2 sub 13 with Scale 0 (invisible, no children).
                    if (_lifeTime > 5f && FpsCheck(5, factor) && FpsCheck(1, factor))
                        SpawnDecal(end, new Vector3(0.15f, 0.15f, 0.4f), true);
                }
                _lifeTime -= factor;
            }

            MoveThunders(factor);
            MoveDecals(factor);
            MoveSmokes(factor);
        }

        // ------------------------------------------------------------------ BITMAP_JOINT_THUNDER sub 16

        private void SpawnThunder(Vector3 from, Vector3 to, float scale)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (t.Live)
                    continue;

                t.Live = true;
                t.Position = from;
                t.Angle = _angle;
                t.TargetPosition = to;
                t.Velocity = 20f + MuGame.Random.Next(10);
                t.LifeTime = MuGame.Random.Next(2) + 2;
                t.Scale = scale;
                int maxTails = SourceJointMath.ScaledMaxTails(50);
                if (t.Trail == null || t.Trail.MaxTails != maxTails)
                    t.Trail = new SourceTrail(maxTails);
                else
                    t.Trail.Clear();
                t.Trail.Push(from, _angle, scale);
                return;
            }
        }

        private void MoveThunders(float factor)
        {
            for (int i = 0; i < _thunders.Length; i++)
            {
                ref Thunder t = ref _thunders[i];
                if (!t.Live)
                    continue;

                SourceJointMath.AngleBasis(t.Angle, out Vector3 endRight, out Vector3 endForward, out Vector3 endUp);
                t.Position += -endForward * (t.Velocity * factor);

                int steps = t.Trail.MaxTails;
                for (int j = 0; j < steps; j++)
                {
                    float distance = SourceJointMath.MoveHumming(ref t.Position, ref t.Angle, t.TargetPosition, 50f, factor);
                    var angle = new Vector3(
                        t.Angle.X + (MuGame.Random.Next(1024) - 512) / t.Scale * factor,
                        t.Angle.Y,
                        t.Angle.Z + (MuGame.Random.Next(1024) - 512) / t.Scale * factor);
                    SourceJointMath.AngleBasis(angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                    t.Trail.Push(t.Position, right, up, t.Scale);

                    if (distance < t.Velocity * 1.5f)
                        break;

                    t.Position += -forward * (t.Velocity * factor * factor);
                }

                t.Trail.Push(t.Position, endRight, endUp, t.Scale);
                t.LifeTime -= factor;
                if (t.LifeTime < 0f)
                    t.Live = false;
            }
        }

        // ------------------------------------------------------------------ decals and smoke

        private void SpawnDecal(Vector3 position, Vector3 light, bool energy)
        {
            for (int i = 0; i < _decals.Length; i++)
            {
                ref Decal d = ref _decals[i];
                if (d.Live)
                    continue;

                d = new Decal { Live = true, Energy = energy, Position = position, Light = light, AngleZ = _angle.Z, LifeTime = energy ? 30f : 20f };
                return;
            }
        }

        private void MoveDecals(float factor)
        {
            float fade = MathF.Pow(0.8f, factor);
            for (int i = 0; i < _decals.Length; i++)
            {
                ref Decal d = ref _decals[i];
                if (!d.Live)
                    continue;

                if (d.Energy && d.LifeTime < 10f)
                    d.Light *= fade;
                d.LifeTime -= factor;
                if (d.LifeTime <= 0f)
                    d.Live = false;
            }
        }

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (s.Live)
                    continue;

                float scale = 2.8f;
                s.Live = true;
                s.Position = position;
                s.Light = StormLight;
                s.LifeTime = (int)(scale * 8f);
                s.Scale = scale + MuGame.Random.Next(50) * 0.01f * SourceJointMath.FrameFactor;
                s.Rotation = MuGame.Random.Next(360);
                s.Gravity = (MuGame.Random.Next(30) + 50) * 0.05f;
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
            float fade = MathF.Pow(1f / 1.02f, factor);
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (!s.Live)
                    continue;

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                {
                    s.Live = false;
                    continue;
                }

                s.Light *= fade;
                if (s.Light.X <= 0.05f)
                    s.LifeTime = 0f;
                s.Gravity -= 0.05f * factor;
                s.Position.Z += (s.Scale + s.Gravity) * 1.5f * factor;
                s.Scale += s.Scale * factor / 100f;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f)
                return true;
            for (int i = 0; i < _thunders.Length; i++) if (_thunders[i].Live) return true;
            for (int i = 0; i < _decals.Length; i++) if (_decals[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_magicTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _decals.Length; i++)
                {
                    ref Decal d = ref _decals[i];
                    if (!d.Live || d.Energy)
                        continue;
                    // BITMAP_MAGIC+1 sub 11: Scale (20 - LifeTime) * 0.15, Luminosity fades over the last 5 ticks.
                    float luminosity = d.LifeTime < 5f ? 1f - (5f - d.LifeTime) * 0.2f : 1f;
                    float size = (20f - d.LifeTime) * 0.15f;
                    Batch.AddTerrainDecal(terrain, d.Position.X, d.Position.Y, size, size, d.Light * luminosity, -d.AngleZ);
                }
                Batch.End();
            }

            if (Batch.Begin(_energyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _decals.Length; i++)
                {
                    ref Decal d = ref _decals[i];
                    if (d.Live && d.Energy)
                        Batch.AddTerrainDecal(terrain, d.Position.X, d.Position.Y, 1.5f, 1.5f, d.Light, 0f);
                }
                Batch.End();
            }

            if (Batch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
                float pow2 = MathF.Pow(2f, SourceJointMath.FrameFactor);
                for (int i = 0; i < _thunders.Length; i++)
                {
                    ref Thunder t = ref _thunders[i];
                    if (t.Live)
                        Batch.AddTrail(t.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat,
                            (t.Trail.MaxTails - 1) / pow2, scroll, false, 2f * scroll);
                }
                Batch.End();
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke s = ref _smokes[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, s.Light, s.Rotation);
                }
                Batch.End();
            }
        }
    }
}
