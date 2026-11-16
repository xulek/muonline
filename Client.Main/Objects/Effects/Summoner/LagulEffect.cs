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
    /// Pollution (AT_SKILL_SUMMON_POLLUTION = 225), SourceMain5.2:
    /// spawn   SummonSystem.cpp:167 MODEL_SUMMONER_SUMMON_LAGUL sub 0 at the target corner (PKKey = level), {1, 2, 4} x
    ///         (BITMAP_JOINT_SPIRIT sub 24 Scale 100 + Scale 20) at angle (0, 0, i * 90), SOUND_SUMMOM_RARGLE
    /// sub 0   ZzzEffect.cpp:831 (LifeTime 160), 8045: rand() % {5, 4, 3} == 0 -> SMOKE 57 (target +/-250, 3.5),
    ///         BITMAP_CLOUD 0 (target +/-200, (0.6, 0.1, 1), 2) and TWINTAIL_WATER 1, all FpsChecked; not rendered
    /// spirit  ZzzEffectJoint.cpp:855 (FACE_TWO, additive, Velocity 10, LifeTime 160, MaxTails 40, Light 0, +/-150 jitter;
    ///         Scale / 70 &gt;= 0.9 creates LAGUL sub 1 with that scale), 4090 (dies without a live Target - the Scale 20
    ///         joints have none and die on their first move, so they are not spawned; MoveHumming 5 to TargetPosition + 80F,
    ///         Direction random walk, Light (0.7, 0.7, 0.9) x ramp, Target follows Position / Angle)
    /// lagul 1 ZzzEffect.cpp:840 (LifeTime 160, Alpha 0, JOINT_ENERGY 48..53 Scale 10), 8079 (action 0 restarted, Alpha ramps),
    ///         18296 render: SHINY+6 sprites at bones 54..59 (Scale*0.3, (0.2, 0.3, 1)), rand_fps_check(5) SMOKE 57 (0.6, 0.5, 0.9) 2
    ///         + TWINTAIL_WATER 1 (0.7, 0.7, 1) at a random bone; ZzzObject.cpp:1785 meshes 0, 1 additive x Alpha;
    ///         destructor 115 removes the energy joints
    /// energy  ZzzEffectJoint.cpp:340 (MaxTails 3, (0.5, 0.5, 0.9)), 3113 (bones 24, 28, 32, 44, 48, 52 of the Lagul)
    /// smoke57 ZzzEffectParticle.cpp:1366 / 5784; water 1127 / 5286 / 9035; cloud ZzzEffect.cpp:1163 / 8774 / 19269 (decal,
    ///         Light x (rand%4+8)*0.1 per frame)
    /// </summary>
    internal sealed class LagulEffect : SummonBookEffectBase
    {
        private const string LagulPath = "Skill/summon_lagul.bmd";
        private const string SpiritTexturePath = "Effect/JointSpirit01.jpg"; // BITMAP_JOINT_SPIRIT
        private const string EnergyTexturePath = "Effect/JointLaser01.jpg";  // BITMAP_JOINT_ENERGY
        private const string SmokeTexturePath = "Effect/smoke01.jpg";        // BITMAP_SMOKE
        private const string WaterTexturePath = "Effect/water.jpg";          // BITMAP_TWINTAIL_WATER
        private const string CloudTexturePath = "Effect/clouds.jpg";         // BITMAP_CLOUD
        private const string ShinyTexturePath = "Effect/shiny05.jpg";        // BITMAP_SHINY + 6
        private const string SummonSound = "Sound/Rargle.wav";
        private const int MaxSpirits = 4;
        private const int MaxParticles = 384;
        private const int MaxClouds = 64;
        private static readonly int[] EffectVolume = { 5, 4, 3 };
        private static readonly int[] SpiritCount = { 1, 2, 4 };
        private static readonly int[] EnergyBones = { 24, 28, 32, 44, 48, 52 };
        private static readonly Vector3[] Palette =
        {
            new(0.6f, 0.6f, 0.9f), new(0.6f, 0.3f, 0.9f), new(0.8f, 0.1f, 0.6f), new(0.9f, 0.1f, 1f),
        };

        private struct Spirit
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public Vector3 Light;
            public float LifeTime;
            public SourceTrail Trail;
            // Lagul sub 1 driven by this joint.
            public bool LagulLive;
            public float LagulLife;
            public float LagulAlpha;
            public float LagulFrame;
            public float LagulScale;
            public SourceTrail[] Energy;
            public Vector3 EnergyAngle;
        }

        private struct Particle
        {
            public bool Live;
            public bool Water;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private struct Cloud
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Spirit[] _spirits = new Spirit[MaxSpirits];
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly Cloud[] _clouds = new Cloud[MaxClouds];
        private readonly Vector3[] _shinySprites = new Vector3[MaxSpirits * 6];
        private int _shinyCount;
        private SourceModelRenderer? _lagul;
        private Texture2D? _spiritTexture, _energyTexture, _smokeTexture, _waterTexture, _cloudTexture, _shinyTexture;
        private int _lagulKeys = 13;
        private float _controllerLife;
        private int _particleCount, _cloudCount;

        public LagulEffect(WalkerObject caster, Vector3 targetTileCorner)
            : base(caster, targetTileCorner, 2048)
        {
            for (int i = 0; i < _spirits.Length; i++)
            {
                _spirits[i].Trail = new SourceTrail(200);
                _spirits[i].Energy = new SourceTrail[6];
                for (int e = 0; e < 6; e++)
                    _spirits[i].Energy[e] = new SourceTrail(200);
            }
        }

        protected override Vector3[] CastingPalette => Palette;

        protected override async Task LoadCreatureContent()
        {
            _spiritTexture = await LoadTexture(SpiritTexturePath);
            _energyTexture = await LoadTexture(EnergyTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _waterTexture = await LoadTexture(WaterTexturePath);
            _cloudTexture = await LoadTexture(CloudTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            var r = new SourceModelRenderer(LagulPath, -2) { VisibleMeshMask = 0b011, AnimationSpeed = 0f };
            _lagul = await r.LoadInto(World) ? r : null;
            if (_lagul?.Model?.Actions is { Length: > 0 } actions)
                _lagulKeys = actions[0].NumAnimationKeys;
        }

        protected override void OnSummon()
        {
            _controllerLife = 160f;
            Vector3 target = TargetCorner;
            int count = SpiritCount[SummonLevel];
            for (int i = 0; i < count; i++)
            {
                ref Spirit s = ref _spirits[i];
                s.Live = true;
                s.Position = target + new Vector3(MuGame.Random.Next(300) - 150, MuGame.Random.Next(300) - 150, 0f);
                s.Angle = new Vector3(0f, 0f, i * 90f);
                s.Direction = Vector3.Zero;
                s.Light = Vector3.Zero;
                s.LifeTime = 160f;
                s.Trail.Reset(SourceJointMath.ScaledMaxTails(40));
                s.Trail.Push(s.Position, s.Angle, 100f);

                s.LagulLive = true;
                s.LagulLife = 160f;
                s.LagulAlpha = 0f;
                s.LagulFrame = 0f;
                s.LagulScale = 100f / 70f;
                s.EnergyAngle = s.Angle;
                for (int e = 0; e < 6; e++)
                {
                    s.Energy[e].Reset(SourceJointMath.ScaledMaxTails(3));
                    s.Energy[e].Push(s.Position, s.Angle, 10f);
                }
            }
            SoundController.Instance.PlayBuffer(SummonSound);
        }

        protected override void TickCreature(float factor)
        {
            MoveParticles(factor);
            MoveClouds(factor);
            TickController(factor);
            for (int i = 0; i < _spirits.Length; i++)
                TickSpirit(ref _spirits[i], factor);
        }

        private void TickController(float factor)
        {
            if (_controllerLife <= 0f)
                return;

            if (MuGame.Random.Next(EffectVolume[SummonLevel]) == 0)
            {
                Vector3 target = TargetCorner;
                Vector3 smoke = target + new Vector3(MuGame.Random.Next(500) - 250, MuGame.Random.Next(500) - 250, 0f);
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnSmoke57(smoke, 3.5f);

                Vector3 cloud = target + new Vector3(MuGame.Random.Next(400) - 200, MuGame.Random.Next(400) - 200, 0f);
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnCloud(cloud, new Vector3(0.6f, 0.1f, 1f), 2f);
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnWater(cloud, new Vector3(0.6f, 0.1f, 1f), factor);
            }

            _controllerLife -= factor;
        }

        private void TickSpirit(ref Spirit s, float factor)
        {
            if (s.LagulLive)
            {
                if (s.LagulLife < 30f)
                    s.LagulAlpha = s.LagulLife / 40f;
                else if (s.LagulLife > 144f)
                    s.LagulAlpha = (160f - s.LagulLife) / 20f;
            }

            if (s.Live)
            {
                if (!s.LagulLive)
                {
                    s.Live = false;
                }
                else
                {
                    SourceJointMath.AngleBasis(s.Angle, out Vector3 endRight, out Vector3 endForward, out Vector3 endUp);
                    s.Position += -endForward * (10f * factor);

                    Vector3 aim = TargetCorner + new Vector3(0f, 0f, 80f * factor);
                    SourceJointMath.MoveHumming(ref s.Position, ref s.Angle, aim, 5f, factor);
                    s.Direction.X += (MuGame.Random.Next(32) - 16) * 0.2f * factor;
                    s.Direction.Z += (MuGame.Random.Next(32) - 16) * 0.8f * factor;
                    s.Angle.X += s.Direction.X * factor;
                    s.Angle.Z += s.Direction.Z * factor;
                    s.Direction.X *= MathF.Pow(0.6f, factor);
                    s.Direction.Z *= MathF.Pow(0.8f, factor);

                    float luminosity = s.LifeTime < 30f ? s.LifeTime / 30f : s.LifeTime > 144f ? (160f - s.LifeTime) / 15f : 1f;
                    s.Light = new Vector3(0.7f, 0.7f, 0.9f) * luminosity;

                    s.Trail.Push(s.Position, s.Angle, 100f);
                    s.LifeTime -= factor;
                    if (s.LifeTime < 0f)
                        s.Live = false;
                }
            }

            if (!s.LagulLive)
                return;

            // Energy joints follow the Lagul bones (MoveJoint), then push their tail.
            Matrix world = LagulWorld(s);
            float frame = LagulFrame(s);
            for (int e = 0; e < 6; e++)
            {
                if (_lagul != null && _lagul.TryComputeBoneAt(0, frame, EnergyBones[e], out Matrix bone))
                    s.Energy[e].Push(Vector3.Transform(bone.Translation, world), s.EnergyAngle, 10f);
            }

            // Epilogue: PlayAnimation(0.3); the ended action restarts on the next SetAction(o, 0).
            s.LagulFrame += 0.3f * factor;
            if (s.LagulFrame >= _lagulKeys)
                s.LagulFrame = 0f;
            s.LagulLife -= factor;
            if (s.LagulLife <= 0f)
                s.LagulLive = false;
        }

        private float LagulFrame(in Spirit s) => MathF.Min(s.LagulFrame, MathF.Max(0f, _lagulKeys - 1));

        private static Matrix LagulWorld(in Spirit s) =>
            Matrix.CreateScale(s.LagulScale)
            * Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(new Vector3(
                MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z))))
            * Matrix.CreateTranslation(s.Position);

        // ------------------------------------------------------------------ particles

        private int FreeParticle()
        {
            for (int i = 0; i < _particles.Length; i++)
                if (!_particles[i].Live)
                    return i;
            return -1;
        }

        private void SpawnSmoke57(Vector3 position, float scale)
        {
            int i = FreeParticle();
            if (i < 0) return;
            _particles[i] = new Particle
            {
                Live = true, Water = false, Position = position, LifeTime = 32f,
                Scale = scale + (MuGame.Random.Next(32) + 48) * 0.01f * scale,
                Rotation = (long)FPSCounter.Instance.WorldTime % 360,
            };
            _particleCount++;
        }

        private void SpawnWater(Vector3 position, Vector3 light, float factor)
        {
            int i = FreeParticle();
            if (i < 0) return;
            position.X += (MuGame.Random.Next(80) - 40) * factor;
            position.Y += (MuGame.Random.Next(80) - 40) * factor;
            position.Z += MuGame.Random.Next(80) * factor;
            _particles[i] = new Particle
            {
                Live = true, Water = true, Position = position, Light = light,
                LifeTime = 20 + MuGame.Random.Next(10), Scale = (MuGame.Random.Next(32) + 50) * 0.01f,
                Gravity = (MuGame.Random.Next(10) + 20) * 0.1f,
            };
            _particleCount++;
        }

        private void SpawnCloud(Vector3 position, Vector3 light, float scale)
        {
            for (int i = 0; i < _clouds.Length; i++)
            {
                if (_clouds[i].Live)
                    continue;
                _clouds[i] = new Cloud { Live = true, Position = position, Light = light, Scale = scale, Rotation = MuGame.Random.Next(360), LifeTime = 60f };
                _cloudCount++;
                return;
            }
        }

        private void MoveParticles(float factor)
        {
            if (_particleCount == 0)
                return;
            float waterDecay = MathF.Pow(1f / 1.02f, factor);
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }
                if (p.Water)
                {
                    p.Scale -= factor * 0.013f;
                    p.Position.Z += p.Gravity * factor;
                    p.Light *= waterDecay;
                }
                else
                {
                    float luminosity = p.LifeTime / 32f;
                    p.Light = new Vector3(luminosity * 0.5f, luminosity * 0.1f, luminosity * 0.8f);
                    p.Gravity += 0.1f * factor;
                    p.Position.Z += p.Gravity * factor;
                    p.Scale += factor * 0.05f;
                }
            }
        }

        private void MoveClouds(float factor)
        {
            if (_cloudCount == 0)
                return;
            float decay = MathF.Pow(1f / 1.05f, factor);
            for (int i = 0; i < _clouds.Length; i++)
            {
                ref Cloud c = ref _clouds[i];
                if (!c.Live)
                    continue;
                c.Light *= decay;
                c.Scale += 0.03f * factor;
                c.LifeTime -= factor;
                if (c.LifeTime <= 0f)
                {
                    c.Live = false;
                    _cloudCount--;
                }
            }
        }

        // ------------------------------------------------------------------ draw

        protected override bool HasCreatureContent()
        {
            if (_controllerLife > 0f || _particleCount > 0 || _cloudCount > 0)
                return true;
            for (int i = 0; i < _spirits.Length; i++)
                if (_spirits[i].Live || _spirits[i].LagulLive)
                    return true;
            return false;
        }

        protected override void DrawCreature()
        {
            var terrain = World?.Terrain;

            if (_cloudCount > 0 && Batch.Begin(_cloudTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _clouds.Length; i++)
                {
                    ref Cloud c = ref _clouds[i];
                    if (!c.Live)
                        continue;
                    float luminosity = (MuGame.Random.Next(4) + 8) * 0.1f;
                    Batch.AddTerrainDecal(terrain, c.Position.X, c.Position.Y, c.Scale, c.Scale, c.Light * luminosity, -c.Rotation);
                }
                Batch.End();
            }

            // Joints.
            if (Batch.Begin(_spiritTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live)
                        Batch.AddTrail(s.Trail, SourceQuadBatch.TrailFaces.Two, s.Light, SourceQuadBatch.TrailShade.Flat, s.Trail.MaxTails - 1);
                }
                Batch.End();
            }
            if (Batch.Begin(_energyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var energyLight = new Vector3(0.5f, 0.5f, 0.9f);
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (!s.LagulLive)
                        continue;
                    for (int e = 0; e < 6; e++)
                        Batch.AddTrail(s.Energy[e], SourceQuadBatch.TrailFaces.Both, energyLight, SourceQuadBatch.TrailShade.Flat, s.Energy[e].MaxTails - 1);
                }
                Batch.End();
            }

            // Lagul models + render-time sprites / particles.
            _shinyCount = 0;
            for (int i = 0; i < _spirits.Length; i++)
            {
                ref Spirit s = ref _spirits[i];
                if (!s.LagulLive || _lagul == null)
                    continue;

                float frame = LagulFrame(s);
                Matrix world = LagulWorld(s);
                for (int b = 54; b <= 59; b++)
                    if (_lagul.TryComputeBoneAt(0, frame, b, out Matrix bone))
                        _shinySprites[_shinyCount++] = Vector3.Transform(bone.Translation, world);

                if (FPSCounter.Instance.RandFPSCheck(5) && _lagul.Model?.Bones is { Length: > 0 } bones &&
                    _lagul.TryComputeBoneAt(0, frame, MuGame.Random.Next(bones.Length), out Matrix randomBone))
                {
                    Vector3 position = Vector3.Transform(randomBone.Translation, world);
                    SpawnSmoke57(position, 2f);
                    SpawnWater(position, new Vector3(0.7f, 0.7f, 1f), SourceJointMath.FrameFactor);
                }

                if (s.LagulAlpha >= 0.01f)
                {
                    _lagul.SetAnimationTime(frame);
                    _lagul.Update(LastGameTime);
                    Vector3 light = (terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.Zero) + new Vector3(0f, 0f, 0.1f);
                    _lagul.DrawInstance(s.Position,
                        new Vector3(MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z)),
                        s.LagulScale, light, MathF.Round(MathF.Min(s.LagulAlpha, 1f) * 50f) / 50f);
                }
            }

            if (_shinyCount > 0 && Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var shinyLight = new Vector3(0.2f, 0.3f, 1f);
                for (int i = 0; i < _shinyCount; i++)
                    Batch.AddSprite(_shinySprites[i], (100f / 70f) * 0.3f, shinyLight, 0f);
                Batch.End();
            }

            DrawParticles(false, _smokeTexture);
            DrawParticles(true, _waterTexture);
        }

        private void DrawParticles(bool water, Texture2D? texture)
        {
            if (_particleCount == 0 || !Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live && p.Water == water)
                    Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
            Batch.End();
        }
    }
}
