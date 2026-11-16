#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Chaotic Diseier (AT_SKILL_GAOTIC = 238), SourceMain5.2, all created at the packet:
    /// packet   WSclient.cpp:4634 / 5014  PLAYER_ATTACK_STRIKE (ride / Fenrir), 5 x BITMAP_SHINY+6 sub 3 (Light 0.5),
    ///          8 x BITMAP_2LINE_GHOST joint sub 0 (scale 20) at (x - 119 + rand%240, z + 49 + rand%60), CreateBomb on the
    ///          hero's selected target, SOUND_SKILL_CAOTIC (packet map)
    /// shiny    ZzzEffect.cpp:1287 (sub 3: LifeTime 24), 8294 (every frame: CreateSprite(SHINY+6, random bone + rot(0,-20,0),
    ///          2.0, Light, sub 1 = minus))
    /// ghost    ZzzEffectJoint.cpp:554 (minus, Velocity 40 + rand%20, LifeTime 67 / 75, MaxTails 26, Scale 20 + rand%200 + 1,
    ///          rand%3 &lt; 2: Light 0.5 + MODEL_DESAIR), 3659 ((int)LifeTime % 16 &lt;= 7 ? yaw + 10F : yaw - 10F,
    ///          BITMAP_SMOKE sub 59 FpsChecked), prologue flight (0,-Velocity,0)
    /// desair   ZzzEffect.cpp:442 (LifeTime 52, Scale 1.4), 8344 (follows the joint; (int)LifeTime % 10 == 0:
    ///          MODEL_FEATHER sub 2 + 3 FpsChecked, scale 1.4), ZzzObject.cpp:1525 (Light 1, RENDER_DARK)
    /// feather  ZzzEffect.cpp:4808 (LifeTime 100, +-40F jitter, Direction to the origin, random angles / spin),
    ///          15451 (Light, Scale *= 0.97^F, Angle += spin per frame), ZzzObject.cpp:1816 (RENDER_DARK)
    /// smoke 59 ZzzEffectParticle.cpp:1492 / 5677 / 9044 (LifeTime 40, Light LifeTime / 40 * 0.9, Scale += 0.09F, minus)
    /// bomb     ZzzEffect.cpp:6393 CreateBomb(Exp): +80 Z, 20 x BITMAP_SPARK sub 2, BITMAP_EXPLOTION (Light 0.7)
    /// </summary>
    internal sealed class ChaoticDiseierEffect : SourceSkillEffectBase
    {
        private const string GhostTexturePath = "Skill/2line_gost.jpg";     // BITMAP_2LINE_GHOST
        private const string ShinyTexturePath = "Effect/shiny05.jpg";       // BITMAP_SHINY + 6
        private const string SmokeTexturePath = "Effect/smoke01.jpg";       // BITMAP_SMOKE
        private const string DesairModelPath = "Skill/desair.bmd";          // MODEL_DESAIR
        private const string FeatherModelPath = "Skill/darkwing_hetachi.bmd"; // MODEL_FEATHER
        private const int GhostCount = 8;
        private const int GhostMaxTails = 26;
        private const int ShinyCount = 5;
        private const float ModelVelocity = 0.3f;

        private struct Ghost
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Velocity;
            public float Scale;
            public float LifeTime;
            public SourceTrail Trail;
            public int Desair; // index into _desairs or -1
        }

        private struct Desair
        {
            public bool Live;
            public int Ghost;
            public Vector3 Position;
            public Vector3 Angle;
            public float Frame;
            public float LifeTime;
        }

        private struct Feather
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Spin;
            public Vector3 Direction;
            public Vector3 Light;
            public float Scale;
            public float Frame;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly WalkerObject? _bombTarget;
        private readonly Ghost[] _ghosts = new Ghost[GhostCount];
        private readonly Desair[] _desairs = new Desair[GhostCount];
        private readonly Feather[] _feathers = new Feather[96];
        private readonly Particle[] _smokes = new Particle[GhostCount * 48];
        private readonly Vector3[] _shinyPositions = new Vector3[ShinyCount];
        private readonly SourceBombPool _bombs = new(1);
        private SourceModelRenderer? _desairRenderer;
        private SourceModelRenderer? _featherRenderer;
        private bool _staticFeatherPose;
        private Texture2D? _ghostTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _explosionTexture;
        private Texture2D? _sparkTexture;
        private float _shinyLife;
        private bool _shinyValid;

        public ChaoticDiseierEffect(WalkerObject caster, WalkerObject? bombTarget)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: GhostCount * 200 * 2 + 512)
        {
            _bombTarget = bombTarget;
            BoundingBoxLocal = new BoundingBox(new Vector3(-4000f, -4000f, -400f), new Vector3(4000f, 4000f, 1200f));
        }

        protected override async Task LoadEffectContent()
        {
            _ghostTexture = await LoadTexture(GhostTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _explosionTexture = await LoadTexture(SourceBombPool.ExplosionTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            _desairRenderer = await LoadModel(DesairModelPath);
            _featherRenderer = await LoadModel(FeatherModelPath);
            var featherActions = _featherRenderer?.Model?.Actions;
            _staticFeatherPose = featherActions != null && featherActions.Length > 0 &&
                                 featherActions[0].NumAnimationKeys <= 1;
            if (_staticFeatherPose && _featherRenderer != null)
                _featherRenderer.ContinuousAnimation = false;
            foreach (var r in new[] { _desairRenderer, _featherRenderer })
            {
                if (r == null)
                    continue;
                r.AnimationSpeed = 0f;
                r.BlendMeshState = Blendings.Negative; // RENDER_DARK on every mesh
            }
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            var angle = new Vector3(0f, 0f, CasterYawDegrees);
            int tails = SourceJointMath.ScaledMaxTails(GhostMaxTails);

            for (int i = 0; i < GhostCount; i++)
            {
                ref Ghost g = ref _ghosts[i];
                g.Live = true;
                g.Position = new Vector3(origin.X - 119f + MuGame.Random.Next(240), origin.Y, origin.Z + 49f + MuGame.Random.Next(60));
                g.Angle = angle;
                g.Velocity = 40f + MuGame.Random.Next(20);
                g.LifeTime = FpsCheck(2, SourceJointMath.FrameFactor) ? 67f : 75f;
                g.Scale = 20f + (MuGame.Random.Next(200) + 1);
                g.Light = Vector3.One;
                g.Trail = new SourceTrail(tails);
                g.Desair = -1;
                if (MuGame.Random.Next(3) < 2)
                {
                    g.Light = new Vector3(0.5f);
                    g.Desair = i;
                    _desairs[i] = new Desair { Live = true, Ghost = i, Position = g.Position, Angle = g.Angle, LifeTime = 52f };
                }
            }

            _shinyLife = 24f;

            if (_bombTarget != null)
                _bombs.Spawn(_bombTarget.WorldPosition.Translation);
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                ref Ghost g = ref _ghosts[i];
                if (!g.Live)
                    continue;
                g.Position += SourceJointMath.Rotate(new Vector3(0f, -g.Velocity, 0f), g.Angle) * factor;
                g.Angle.Z += ((int)g.LifeTime % 16 <= 7 ? 10f : -10f) * factor;
                if (FpsCheck(1, factor))
                    SpawnSmoke(g.Position);
                g.Trail.Push(g.Position, g.Angle, g.Scale);
                g.LifeTime -= factor;
                if (g.LifeTime < 0f)
                    g.Live = false;
            }

            for (int i = 0; i < _desairs.Length; i++)
            {
                ref Desair d = ref _desairs[i];
                if (!d.Live)
                    continue;
                ref Ghost g = ref _ghosts[d.Ghost];
                if (g.Live)
                {
                    d.Position = g.Position;
                    d.Angle = g.Angle;
                    if ((int)d.LifeTime % 10 == 0)
                    {
                        if (FpsCheck(1, factor)) SpawnFeather(d.Position);
                        if (FpsCheck(1, factor)) SpawnFeather(d.Position);
                    }
                }
                d.Frame += ModelVelocity * factor;
                d.LifeTime -= factor;
                if (d.LifeTime <= 0f)
                    d.Live = false;
            }

            float fade = MathF.Pow(0.97f, factor);
            for (int i = 0; i < _feathers.Length; i++)
            {
                ref Feather f = ref _feathers[i];
                if (!f.Live)
                    continue;
                f.Light *= fade;
                f.Scale *= fade;
                f.Angle += f.Spin; // per frame
                f.Frame += ModelVelocity * factor;
                f.Position += SourceJointMath.Rotate(f.Direction, f.Angle) * factor;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                    f.Live = false;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                p.Light = new Vector3(p.LifeTime / 40f * 0.9f);
                p.Scale += factor * 0.09f;
            }

            _bombs.Move(factor, World?.Terrain);

            if (_shinyLife > 0f)
                _shinyLife -= factor;
        }

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (p.Live)
                    continue;
                p = new Particle
                {
                    Live = true,
                    Position = position,
                    Light = new Vector3(0.9f),
                    Scale = (MuGame.Random.Next(64) + 64) * 0.01f,
                    Rotation = MuGame.Random.Next(360),
                    LifeTime = 40f,
                };
                return;
            }
        }

        private void SpawnFeather(Vector3 origin)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            for (int i = 0; i < _feathers.Length; i++)
            {
                ref Feather f = ref _feathers[i];
                if (f.Live)
                    continue;
                Vector3 position = origin + new Vector3(
                    (MuGame.Random.Next(20) - 10) * 4f * factor,
                    (MuGame.Random.Next(20) - 10) * 4f * factor,
                    (MuGame.Random.Next(20) - 10) * 4f * factor);
                Vector3 direction = origin - position;
                if (direction.LengthSquared() > 0f)
                    direction.Normalize();
                // iAddDirection = (int)((rand() % 10 - 5) * 0.08f) is always 0.
                f = new Feather
                {
                    Live = true,
                    Position = position,
                    Direction = direction,
                    Scale = 1.4f + (MuGame.Random.Next(20) - 10) * (1.4f * 0.03f),
                    Light = Vector3.One,
                    LifeTime = 100f,
                    Angle = new Vector3(MuGame.Random.Next(360), MuGame.Random.Next(360), MuGame.Random.Next(360)),
                    Spin = new Vector3(MuGame.Random.Next(10) - 5, MuGame.Random.Next(10) - 5, MuGame.Random.Next(10) - 5),
                };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_shinyLife > 0f || _bombs.HasLive)
                return true;
            for (int i = 0; i < _ghosts.Length; i++) if (_ghosts[i].Live) return true;
            for (int i = 0; i < _desairs.Length; i++) if (_desairs[i].Live) return true;
            for (int i = 0; i < _feathers.Length; i++) if (_feathers[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        private void UpdateShinyPositions()
        {
            // TransformByObjectBone on a random bone, + rot(0,-20,0) by the owner angle; a new bone every frame.
            _shinyValid = false;
            if (Caster is not PlayerObject player)
                return;
            Matrix[]? bones = player.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;
            Vector3 forward = SourceJointMath.Rotate(new Vector3(0f, -20f, 0f), new Vector3(0f, 0f, CasterYawDegrees));
            for (int i = 0; i < ShinyCount; i++)
                _shinyPositions[i] = Vector3.Transform(bones[MuGame.Random.Next(bones.Length)].Translation, player.WorldPosition) + forward;
            _shinyValid = true;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_ghostTexture, Blendings.Negative, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _ghosts.Length; i++)
                {
                    ref Ghost g = ref _ghosts[i];
                    if (g.Live)
                        Batch.AddTrail(g.Trail, SourceQuadBatch.TrailFaces.Both, g.Light, SourceQuadBatch.TrailShade.Flat, g.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (_desairRenderer != null)
            {
                bool updated = false;
                for (int i = 0; i < _desairs.Length; i++)
                {
                    ref Desair d = ref _desairs[i];
                    if (!d.Live)
                        continue;
                    // All DESAIR models are born together and have the same animation frame.
                    if (!updated)
                    {
                        _desairRenderer.SetAnimationTime(d.Frame);
                        _desairRenderer.Update(LastGameTime);
                        updated = true;
                    }
                    _desairRenderer.DrawInstance(d.Position, ToRadians(d.Angle), 1.4f, Vector3.One, 1f);
                }
            }

            if (_featherRenderer != null)
            {
                bool updated = false;
                for (int i = 0; i < _feathers.Length; i++)
                {
                    ref Feather f = ref _feathers[i];
                    if (!f.Live)
                        continue;
                    if (!_staticFeatherPose || !updated)
                    {
                        _featherRenderer.SetAnimationTime(f.Frame);
                        _featherRenderer.Update(LastGameTime);
                        updated = true;
                    }
                    // BodyLight = RequestTerrainLight + o->Light
                    Vector3 light = (terrain?.EvaluateTerrainLight(f.Position.X, f.Position.Y) ?? Vector3.Zero) + f.Light;
                    _featherRenderer.DrawInstance(f.Position, ToRadians(f.Angle), f.Scale, light, 1f);
                }
            }


            if (_shinyLife > 0f)
            {
                UpdateShinyPositions();
                if (_shinyValid && Batch.Begin(_shinyTexture, Blendings.Negative, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                {
                    for (int i = 0; i < ShinyCount; i++)
                        Batch.AddSprite(_shinyPositions[i], 2f, new Vector3(0.5f));
                    Batch.End();
                }
            }

            if (Batch.Begin(_smokeTexture, Blendings.Negative, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Particle p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }

            _bombs.Draw(Batch, _sparkTexture, _explosionTexture);
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _desairRenderer?.Dispose();
            _featherRenderer?.Dispose();
        }
    }
}
