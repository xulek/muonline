#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Mana Rays (AT_SKILL_DEATH_CANNON = 73) and Fire Blast (AT_SKILL_SPACE_SPLIT = 74), SourceMain5.2:
    /// packet   WSclient.cpp:4165 (PLAYER_ATTACK_DEATH_CANNON) / 4176 (strike actions by mount)
    /// gate     ZzzCharacter.cpp:2826 / 2816   AnimationFrame &gt;= 3 in those actions (else 14 ticks)
    /// release  ZzzCharacter.cpp:4882   BITMAP_JOINT_FORCE sub 4 at the caster + 130, angle (0,0,yaw), Scale 40, SOUND_BCS_DEATH_CANON
    ///          ZzzCharacter.cpp:4891   MODEL_PIER_PART sub 2 homing on the target, SOUND_BCS_SPACE_SPLIT
    /// pier     ZzzEffect.cpp:3507 (LifeTime 20, Velocity 50, hidden, z - 20, Direction (0,-40,0)), 13204 (MoveHumming, Velocity += 2.4F,
    ///          (int)LifeTime % 3 == 0: BITMAP_JOINT_FORCE sub 2 angle (-90,0,yaw), Scale 150)
    /// force    ZzzEffectJoint.cpp:2276 (Velocity 8, no start tail; sub 4: LifeTime 20, MaxTails 13, BITMAP_HOLE, Direction (15.5,,3),
    ///          MultiUse 10; sub 2: z + 10, LifeTime 15, MaxTails 12, BITMAP_INFERNO, Direction (5,,5), MultiUse 5), 6345 (Velocity +=
    ///          Direction.z, Direction.z += 0.5 / Direction.x, Light /= 1.3 below MultiUse; sub 2: MODEL_SKILL_INFERNO sub 6 every 5 ticks;
    ///          sub 4: BITMAP_FLARE_BLUE joint sub 6 + BITMAP_LIGHT 1.6 / BITMAP_SHINY+1 1.5 sprites (0.1,0.6,1))
    /// flare    ZzzEffectJoint.cpp:1853 (LifeTime 15, MaxTails 15, Scale 30), 5524 (spiral radius LifeTime * 2 around a drifting centre)
    /// inferno  ZzzEffect.cpp:1469 (LifeTime 5, Scale 0.2, mesh 0 hidden, BlendMesh -2), 9655 (Scale += 0.01F, BlendMeshLight = LifeTime / 50)
    /// Not ported: AddTerrainLight.
    /// </summary>
    internal sealed class ForceJointSkillEffect : SourceSkillEffectBase
    {
        private const string HoleTexturePath = "Effect/Hole.jpg";                       // BITMAP_HOLE
        private const string InfernoTexturePath = "Effect/Inferno.jpg";                 // BITMAP_INFERNO
        private const string FlareBlueTexturePath = "Effect/flareBlue.jpg";             // BITMAP_FLARE_BLUE
        private const string LightTexturePath = "Effect/flare01.jpg";                   // BITMAP_LIGHT
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";                   // BITMAP_SHINY + 1
        private const string InfernoModelPath = "Skill/Inferno01.bmd";                  // MODEL_SKILL_INFERNO
        private const string ManaRaysSoundPath = "Sound/battlecastle/sCMW.wav";         // SOUND_BCS_DEATH_CANON
        private const string FireBlastSoundPath = "Sound/battlecastle/sCDarkAttack.wav"; // SOUND_BCS_SPACE_SPLIT
        private static readonly Vector3 RayLight = new(0.1f, 0.6f, 1f);

        private struct Force
        {
            public bool Live;
            public bool CreateTails;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Light;
            public float Yaw;
            public float Velocity;
            public float DirectionX;
            public float DirectionZ;
            public float LifeTime;
            public int MaxTails;
            public int LastMark;
        }

        private struct Flare
        {
            public bool Live;
            public Vector3 Center;
            public Vector3 Right;
            public Vector3 Up;
            public float Yaw;
            public float Phase;
            public float DriftX;
            public float DriftY;
            public float LifeTime;
            public int MaxTails;
        }

        private struct Ring
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Yaw;
            public float Scale;
            public float Frame;
            public float LifeTime;
        }

        private readonly bool _fireBlast;
        private readonly WalkerObject? _target;
        private readonly Force[] _forces = new Force[12];
        private readonly SourceTrail[] _forceTrails = new SourceTrail[12];
        private readonly Flare[] _flares = new Flare[24];
        private readonly SourceTrail[] _flareTrails = new SourceTrail[24];
        private readonly Ring[] _rings = new Ring[32];
        private SourceModelRenderer? _ringRenderer;
        private Texture2D? _forceTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _shinyTexture;

        private bool _pierLive;
        private Vector3 _pierPosition;
        private Vector3 _pierAngle;        // degrees
        private float _pierVelocity;
        private float _pierLife;
        private int _liveCount;

        public ForceJointSkillEffect(WalkerObject caster, WalkerObject? target, bool fireBlast)
            : base(caster, triggerFrame: 3f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 512)
        {
            _fireBlast = fireBlast;
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            if (_fireBlast)
            {
                _forceTexture = await LoadTexture(InfernoTexturePath);
                _ringRenderer = await LoadModel(InfernoModelPath, -2, 0);
                if (_ringRenderer != null)
                    _ringRenderer.AnimationSpeed = 0f;
                await SoundController.Instance.PreloadSoundAsync(FireBlastSoundPath);
            }
            else
            {
                _forceTexture = await LoadTexture(HoleTexturePath);
                _flareTexture = await LoadTexture(FlareBlueTexturePath);
                _lightTexture = await LoadTexture(LightTexturePath);
                _shinyTexture = await LoadTexture(ShinyTexturePath);
                await SoundController.Instance.PreloadSoundAsync(ManaRaysSoundPath);
            }
        }

        protected override void OnStart()
        {
            int forceTails = SourceJointMath.ScaledMaxTails(13);
            for (int i = 0; i < _forceTrails.Length; i++)
                _forceTrails[i] = new SourceTrail(forceTails);

            if (_fireBlast)
            {
                SoundController.Instance.PlayBuffer(FireBlastSoundPath);
                if (!TargetAlive)
                    return;

                _pierLive = true;
                _pierPosition = CasterPosition;
                _pierPosition.Z -= 20f * SourceJointMath.FrameFactor;
                _pierAngle = new Vector3(0f, 0f, CasterYawDegrees);
                _pierVelocity = 50f;
                _pierLife = 20f;
            }
            else
            {
                int flareTails = SourceJointMath.ScaledMaxTails(15);
                for (int i = 0; i < _flareTrails.Length; i++)
                    _flareTrails[i] = new SourceTrail(flareTails);

                Vector3 position = CasterPosition;
                position.Z += 130f;
                SpawnForce(position, new Vector3(0f, 0f, CasterYawDegrees));
                SoundController.Instance.PlayBuffer(ManaRaysSoundPath);
            }
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        private void SpawnForce(Vector3 position, Vector3 angleDeg)
        {
            for (int i = 0; i < _forces.Length; i++)
            {
                ref Force f = ref _forces[i];
                if (f.Live)
                    continue;

                SourceJointMath.AngleBasis(angleDeg, out f.Right, out Vector3 forward, out f.Up);
                f.Live = true;
                f.CreateTails = true;
                f.Forward = -forward;
                f.Yaw = angleDeg.Z;
                f.Velocity = 8f;
                f.Light = Vector3.One;
                f.LastMark = -1;
                if (_fireBlast)
                {
                    position.Z += 10f;
                    f.LifeTime = 15f;
                    f.DirectionX = 5f;
                    f.DirectionZ = 5f;
                    f.MaxTails = SourceJointMath.ScaledMaxTails(12);
                }
                else
                {
                    f.LifeTime = 20f;
                    f.DirectionX = 15.5f;
                    f.DirectionZ = 3f;
                    f.MaxTails = SourceJointMath.ScaledMaxTails(13);
                }
                f.Position = position;
                f.StartPosition = position;
                _forceTrails[i].Reset(f.MaxTails);
                _liveCount++;
                return;
            }
        }

        protected override void OnTick(float factor)
        {
            TickPier(factor);
            TickForces(factor);
            TickFlares(factor);
            TickRings(factor);
        }

        private void TickPier(float factor)
        {
            if (!_pierLive)
                return;

            if (TargetAlive)
            {
                SourceJointMath.MoveHumming(ref _pierPosition, ref _pierAngle, _target!.WorldPosition.Translation, _pierVelocity, factor);
                _pierVelocity += 2.4f * factor;
                _pierPosition += SourceJointMath.Rotate(new Vector3(0f, -40f, 0f), _pierAngle) * factor;
                if ((int)_pierLife % 3 == 0 && FpsCheck(1, factor))
                    SpawnForce(_pierPosition, new Vector3(-90f, 0f, _pierAngle.Z));
            }

            _pierLife -= factor;
            if (_pierLife <= 0f)
                _pierLive = false;
        }

        private void TickForces(float factor)
        {
            float decay = MathF.Pow(1f / 1.3f, factor);
            float scale = _fireBlast ? 150f : 40f;
            for (int i = 0; i < _forces.Length; i++)
            {
                ref Force f = ref _forces[i];
                if (!f.Live)
                    continue;

                f.Position += f.Forward * (f.Velocity * factor);
                f.Velocity += factor * f.DirectionZ;
                f.DirectionZ += (f.LifeTime < 15f ? 0.5f : f.DirectionX) * factor;

                SourceTrail trail = _forceTrails[i];
                if (_fireBlast && trail.SegmentCount >= f.MaxTails - 1)
                    f.CreateTails = false;
                if (f.LifeTime < (_fireBlast ? 5f : 10f))
                    f.Light *= decay;

                if (_fireBlast)
                {
                    int mark = (int)f.LifeTime;
                    if (mark % 5 == 0 && mark != f.LastMark)
                        SpawnRing(f.StartPosition, f.Yaw, f.Light);
                    f.LastMark = mark;
                }
                else if (FpsCheck(1, factor))
                {
                    SpawnFlare(f.Position, f.Yaw);
                }

                if (f.CreateTails)
                    trail.Push(f.Position, f.Right, f.Up, scale);

                f.LifeTime -= factor;
                if (f.LifeTime < 0f)
                {
                    f.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnFlare(Vector3 position, float yaw)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;

                float radians = -yaw * MathHelper.Pi / 180f;
                f.Live = true;
                f.Center = position;
                f.Yaw = yaw;
                f.Phase = MuGame.Random.Next(360);
                f.DriftX = -2f * MathF.Sin(radians);
                f.DriftY = -2f * MathF.Cos(radians);
                f.LifeTime = 15f;
                f.MaxTails = SourceJointMath.ScaledMaxTails(15);
                SourceJointMath.AngleBasis(new Vector3(0f, 0f, yaw), out f.Right, out _, out f.Up);
                _flareTrails[i].Reset(f.MaxTails);
                _flareTrails[i].Push(position, f.Right, f.Up, 30f);
                _liveCount++;
                return;
            }
        }

        private void TickFlares(float factor)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;

                float count = f.Phase + f.LifeTime * 0.8f;
                float radius = MathF.Max(f.LifeTime, 1f) * 2f;
                float x = -MathF.Cos(count) * radius;
                float z = MathF.Sin(count) * radius;
                f.Center.X += f.DriftX * factor;
                f.Center.Y += f.DriftY * factor;
                float side = (90f - f.Yaw) * MathHelper.Pi / 180f;
                var position = new Vector3(x * MathF.Sin(side) + f.Center.X, x * MathF.Cos(side) + f.Center.Y, z + f.Center.Z);
                _flareTrails[i].Push(position, f.Right, f.Up, 30f);

                f.LifeTime -= factor;
                if (f.LifeTime < 0f)
                {
                    f.Live = false;
                    _liveCount--;
                }
            }
        }

        private void SpawnRing(Vector3 position, float yaw, Vector3 light)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring r = ref _rings[i];
                if (r.Live)
                    continue;

                r = new Ring { Live = true, Position = position, Light = light, Yaw = yaw, Scale = 0.2f, LifeTime = 5f };
                _liveCount++;
                return;
            }
        }

        private void TickRings(float factor)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring r = ref _rings[i];
                if (!r.Live)
                    continue;

                r.Scale += 0.01f * factor;
                r.Frame += 0.1f * factor;
                r.LifeTime -= factor;
                if (r.LifeTime <= 0f)
                {
                    r.Live = false;
                    _liveCount--;
                }
            }
        }

        protected override bool HasActiveContent() => _pierLive || _liveCount > 0;

        protected override void OnDraw()
        {
            SamplerState sampler = _fireBlast ? SamplerState.LinearClamp : SamplerState.LinearWrap;
            if (Batch.Begin(_forceTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, sampler))
            {
                for (int i = 0; i < _forces.Length; i++)
                {
                    ref Force f = ref _forces[i];
                    if (f.Live)
                        Batch.AddTrail(_forceTrails[i], SourceQuadBatch.TrailFaces.Both, f.Light, SourceQuadBatch.TrailShade.Flat, f.MaxTails - 1);
                }
                Batch.End();
            }

            if (_fireBlast)
            {
                if (_ringRenderer == null)
                    return;

                var terrain = World?.Terrain;
                for (int i = 0; i < _rings.Length; i++)
                {
                    ref Ring r = ref _rings[i];
                    if (!r.Live)
                        continue;
                    _ringRenderer.SetAnimationTime(r.Frame);
                    _ringRenderer.Update(LastGameTime);
                    // BodyLight = RequestTerrainLight + o->Light, blend meshes x BlendMeshLight (LifeTime / 5 * 0.1).
                    Vector3 body = (terrain?.EvaluateTerrainLight(r.Position.X, r.Position.Y) ?? Vector3.Zero) + r.Light;
                    _ringRenderer.DrawInstance(r.Position, new Vector3(0f, 0f, MathHelper.ToRadians(r.Yaw)), r.Scale, body * (r.LifeTime / 5f * 0.1f), 1f);
                }
                return;
            }

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (f.Live)
                        Batch.AddTrail(_flareTrails[i], SourceQuadBatch.TrailFaces.Both, Vector3.One, SourceQuadBatch.TrailShade.Flat, f.MaxTails - 1);
                }
                Batch.End();
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _forces.Length; i++)
                    if (_forces[i].Live)
                        Batch.AddSprite(_forces[i].Position, 1.6f, RayLight);
                Batch.End();
            }

            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float rotation = (float)(FPSCounter.Instance.WorldTime * 0.1 % 360.0);
                for (int i = 0; i < _forces.Length; i++)
                    if (_forces[i].Live)
                        Batch.AddSprite(_forces[i].Position, 1.5f, RayLight, rotation);
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _ringRenderer?.Dispose();
        }
    }
}
