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
    /// Fire Scream (AT_SKILL_DARK_SCREAM = 78, masters AT_SKILL_FIRE_SCREAM_UP..+4), SourceMain5.2:
    /// packet   WSclient.cpp:4513 / 4910  PLAYER_ATTACK_STRIKE (ride / Fenrir), SOUND_FIRE_SCREAM (packet map)
    /// gate     ZzzCharacter.cpp:2877     no case: default, AnimationFrame &gt;= 5
    /// release  ZzzCharacter.cpp:4349     3 x (MODEL_DARK_SCREAM + MODEL_DARK_SCREAM_FIRE): centre at yaw, (80,0,0) at
    ///          yaw + 10, (-80,0,0) at yaw - 10
    /// model    ZzzEffect.cpp:1693 (arrow family: Velocity 1, LifeTime 19, Position += rot(-10,-60,135); Scale 2.3,
    ///          Direction (0,-35,0); DARK_SCREAM: Scale 0.9, Z = terrain + 3, BITMAP_JOINT_FORCE sub 7 (150),
    ///          Z += 20F, += rot(0,-20,0), BITMAP_BLUE_BLUR sub 1), 11902 (Scale -= 0.04F / 0.14F, &lt; 0.1 -&gt; 0,
    ///          Z = terrain + 3, BITMAP_FLAME sub 8 FpsChecked (Scale - 0.4) * 3.5), epilogue MoveParticle(o, true),
    ///          ZzzObject.cpp:1531 (DARK_SCREAM RENDER_TEXTURE, FIRE RENDER_TEXTURE | RENDER_BRIGHT, BlendMeshLight 1)
    /// joint    ZzzEffectJoint.cpp:2326 (sub 7: Velocity 10, Direction (3.5, 0, 1), MaxTails 13, MultiUse 5, BITMAP_INFERNO,
    ///          LifeTime 20), 6380 (Velocity += Direction z F, Direction z += 0.5F / 3.5F, tails stop at MaxTails - 1,
    ///          LifeTime &lt; 5: Light / 1.3^F, Z = terrain + 3), prologue flight (0,-Velocity,0)
    /// flame    ZzzEffectParticle.cpp:603 / 4869 (sub 8: LifeTime 33, rand scale / gravity / drift, Rotation +-2F,
    ///          Z += Gravity / 2 F, Scale -= Gravity F / 95, Light / 1.007^F)
    /// blur     ZzzEffectParticle.cpp:945 / 4016 (sub 1: PoundingBall, LifeTime 30, Z -= 45F, Light LifeTime / 20,
    ///          Scale += 0.19F, Z += 5F)
    /// </summary>
    internal sealed class FireScreamEffect : SourceSkillEffectBase
    {
        private const string DarkModelPath = "Skill/darkfirescrem02.bmd";  // MODEL_DARK_SCREAM
        private const string FireModelPath = "Skill/darkfirescrem01.bmd";  // MODEL_DARK_SCREAM_FIRE
        private const string FlameTexturePath = "Effect/Flame01.jpg";      // BITMAP_FLAME
        private const string BlurTexturePath = "Effect/PoundingBall.jpg";  // BITMAP_POUNDING_BALL (BLUE_BLUR sub 1)
        private const string JointTexturePath = "Effect/Inferno.jpg";      // BITMAP_INFERNO
        private const int PairCount = 3;
        private const int JointMaxTails = 13;

        private struct ScreamModel
        {
            public bool Live;
            public bool Fire;
            public Vector3 Position;
            public float Yaw;
            public float Scale;
            public float Frame;
            public float LifeTime;
        }

        private struct ForceJoint
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Velocity;
            public float DirectionX;
            public float DirectionZ;
            public bool CreateTails;
            public int Pushes;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Light;
            public float StartX;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private readonly ScreamModel[] _models = new ScreamModel[PairCount * 2];
        private readonly ForceJoint[] _joints = new ForceJoint[PairCount];
        private readonly Particle[] _blurs = new Particle[PairCount];
        private readonly Particle[] _flames = new Particle[192];
        private SourceModelRenderer? _darkRenderer;
        private SourceModelRenderer? _fireRenderer;
        private Texture2D? _flameTexture;
        private Texture2D? _blurTexture;
        private Texture2D? _jointTexture;

        public FireScreamEffect(WalkerObject caster)
            : base(caster, triggerFrame: 5f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 192 + PairCount * 200 * 2 + 8)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _darkRenderer = await LoadModel(DarkModelPath, -1);
            _fireRenderer = await LoadModel(FireModelPath, -2); // RENDER_BRIGHT: every mesh additive
            if (_darkRenderer != null)
            {
                _darkRenderer.AnimationSpeed = 0f;
                _darkRenderer.UseNormalBlend();
            }
            if (_fireRenderer != null)
                _fireRenderer.AnimationSpeed = 0f;
            _flameTexture = await LoadTexture(FlameTexturePath);
            _blurTexture = await LoadTexture(BlurTexturePath);
            _jointTexture = await LoadTexture(JointTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            float yaw = CasterYawDegrees;
            SpawnPair(0, origin, yaw);
            SpawnPair(1, origin + SourceJointMath.Rotate(new Vector3(80f, 0f, 0f), new Vector3(0f, 0f, yaw + 10f)), yaw + 10f);
            SpawnPair(2, origin + SourceJointMath.Rotate(new Vector3(-80f, 0f, 0f), new Vector3(0f, 0f, yaw - 10f)), yaw - 10f);
        }

        private void SpawnPair(int index, Vector3 position, float yaw)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            var angle = new Vector3(0f, 0f, yaw);
            Vector3 start = position + SourceJointMath.Rotate(new Vector3(-10f, -60f, 135f), angle);

            // MODEL_DARK_SCREAM
            Vector3 dark = start;
            dark.Z = TerrainHeight(dark) + 3f;
            _joints[index] = SpawnJoint(dark, angle);
            dark.Z += 20f * factor;
            dark += SourceJointMath.Rotate(new Vector3(0f, -20f, 0f), angle);
            _blurs[index] = new Particle
            {
                Live = true,
                Position = dark - Vector3.UnitZ * (45f * factor),
                Light = new Vector3(0f, 1f, 0f),
                Scale = (MuGame.Random.Next(64) + 64) * 0.01f,
                Rotation = MuGame.Random.Next(360),
                Gravity = (MuGame.Random.Next(32) + 60) * 0.1f,
                LifeTime = 30f,
            };
            _models[index * 2] = new ScreamModel { Live = true, Position = dark, Yaw = yaw, Scale = 0.9f, LifeTime = 19f };

            // MODEL_DARK_SCREAM_FIRE
            _models[index * 2 + 1] = new ScreamModel { Live = true, Fire = true, Position = start, Yaw = yaw, Scale = 2.3f, LifeTime = 19f };
        }

        private static ForceJoint SpawnJoint(Vector3 position, Vector3 angle) => new()
        {
            Live = true,
            Position = position,
            Angle = angle,
            Light = Vector3.One,
            Velocity = 10f,
            DirectionX = 3.5f,
            DirectionZ = 1f,
            CreateTails = true,
            LifeTime = 20f,
            Trail = new SourceTrail(SourceJointMath.ScaledMaxTails(JointMaxTails)),
        };

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _models.Length; i++)
            {
                ref ScreamModel m = ref _models[i];
                if (!m.Live)
                    continue;
                m.Scale -= (m.Fire ? 0.14f : 0.04f) * factor;
                if (m.Scale < 0.1f)
                    m.Scale = 0f;
                m.Position.Z = TerrainHeight(m.Position) + 3f;
                if (FpsCheck(1, factor))
                    SpawnFlame(m.Position, new Vector3(0f, 0f, m.Yaw), (m.Scale - 0.4f) * 3.5f);

                // epilogue: PlayAnimation(Velocity 1), MoveParticle(o, true) with Direction (0,-35,0)
                m.Frame += factor;
                m.Position += SourceJointMath.Rotate(new Vector3(0f, -35f, 0f), new Vector3(0f, 0f, m.Yaw)) * factor;
                m.LifeTime -= factor;
                if (m.LifeTime <= 0f)
                    m.Live = false;
            }

            for (int i = 0; i < _joints.Length; i++)
            {
                ref ForceJoint j = ref _joints[i];
                if (!j.Live)
                    continue;
                j.Position += SourceJointMath.Rotate(new Vector3(0f, -j.Velocity, 0f), j.Angle) * factor;
                j.Velocity += factor * j.DirectionZ;
                j.DirectionZ += (j.LifeTime < 20f ? 0.5f : j.DirectionX) * factor;
                if (j.Pushes - 1 >= j.Trail.MaxTails - 1)
                    j.CreateTails = false;
                if (j.LifeTime < 5f)
                    j.Light *= MathF.Pow(1f / 1.3f, factor);
                j.Position.Z = TerrainHeight(j.Position) + 3f;
                if (j.CreateTails)
                {
                    j.Trail.Push(j.Position, j.Angle, 150f);
                    j.Pushes++;
                }
                j.LifeTime -= factor;
                if (j.LifeTime < 0f)
                    j.Live = false;
            }

            for (int i = 0; i < _blurs.Length; i++)
            {
                ref Particle p = ref _blurs[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                p.Light = new Vector3(p.LifeTime / 20f);
                p.Scale += factor * 0.19f;
                p.Position.Z += 5f * factor;
            }

            for (int i = 0; i < _flames.Length; i++)
            {
                ref Particle p = ref _flames[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                p.Position += p.Velocity * factor;
                p.Rotation += (p.StartX < p.Position.X ? 2f : -2f) * factor;
                p.Position.Z += p.Gravity / 2f * factor;
                p.Scale -= p.Gravity * factor / 95f;
                if (p.Scale <= 0f) { p.Live = false; continue; }
                p.Light *= MathF.Pow(1f / 1.007f, factor);
            }
        }

        private void SpawnFlame(Vector3 position, Vector3 angle, float scale)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            for (int i = 0; i < _flames.Length; i++)
            {
                ref Particle p = ref _flames[i];
                if (p.Live)
                    continue;
                scale = -MuGame.Random.Next(20) / 100f + scale;
                var pos = position;
                float startX = pos.X;
                pos.X += MuGame.Random.Next(2) / 2f * scale * factor;
                float gravity = (MuGame.Random.Next(100) / 100f + 1.8f) * scale;
                float drift = 0f;
                if (MuGame.Random.Next(10) >= 3)
                    drift = (MuGame.Random.Next(20) / 10f - 1f) * scale;
                pos.Y += (pos.X - startX) * factor;
                p = new Particle
                {
                    Live = true,
                    Position = pos,
                    Velocity = SourceJointMath.Rotate(new Vector3(0f, drift, 0f), angle),
                    Light = Vector3.One,
                    StartX = startX,
                    Scale = scale,
                    Gravity = gravity,
                    Rotation = MuGame.Random.Next(360) - 180f,
                    LifeTime = 33f,
                };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < _models.Length; i++) if (_models[i].Live) return true;
            for (int i = 0; i < _joints.Length; i++) if (_joints[i].Live) return true;
            for (int i = 0; i < _blurs.Length; i++) if (_blurs[i].Live) return true;
            for (int i = 0; i < _flames.Length; i++) if (_flames[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_jointTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _joints.Length; i++)
                {
                    ref ForceJoint j = ref _joints[i];
                    if (j.Live)
                        Batch.AddTrail(j.Trail, SourceQuadBatch.TrailFaces.Both, j.Light, SourceQuadBatch.TrailShade.Flat, j.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            var terrain = World?.Terrain;
            for (int i = 0; i < _models.Length; i++)
            {
                ref ScreamModel m = ref _models[i];
                SourceModelRenderer? renderer = m.Fire ? _fireRenderer : _darkRenderer;
                if (!m.Live || renderer == null || m.Scale <= 0f)
                    continue;
                renderer.SetAnimationTime(m.Frame);
                renderer.Update(LastGameTime);
                // BodyLight = RequestTerrainLight + o->Light (1,1,1)
                Vector3 light = (terrain?.EvaluateTerrainLight(m.Position.X, m.Position.Y) ?? Vector3.Zero) + Vector3.One;
                renderer.DrawInstance(m.Position, new Vector3(0f, 0f, MathHelper.ToRadians(m.Yaw)), m.Scale, light, 1f);
            }

            if (Batch.Begin(_blurTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _blurs.Length; i++)
                {
                    ref Particle p = ref _blurs[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }

            if (Batch.Begin(_flameTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flames.Length; i++)
                {
                    ref Particle p = ref _flames[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _darkRenderer?.Dispose();
            _fireRenderer?.Dispose();
        }
    }
}
