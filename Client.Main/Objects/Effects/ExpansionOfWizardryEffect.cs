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
    /// Expansion of Wizardry (AT_SKILL_SWELL_OF_MAGICPOWER = 233), SourceMain5.2:
    /// packet  WSclient.cpp:4618       PLAYER_SKILL_SWELL_OF_MP, CreateEffect(MODEL_SWELL_OF_MAGICPOWER, Light (0.3,0.2,0.9)), sound
    /// effect  ZzzEffect.cpp:682 (LifeTime 45), 15258 (move: SHOCK_WAVE 14 x2 + TWLIGHT 3 at 45/35/25, bone sprites for
    ///         LifeTime &lt;= 20, MODEL_ARROWSRE06 sub 1 at bones 28/37, 2 x BITMAP_2LINE_GHOST sub 1 while LifeTime &gt;= 30)
    /// rings   ZzzEffect.cpp:3631/3715 (init), 13308/13405 (move: follow owner, Scale -0.15, PKKey ramp), 19396 (decals)
    /// flares  ZzzEffect.cpp:689/15330 (ARROWSRE06 sub 1), ghost ZzzEffectJoint.cpp:575 (init), 3659 (move)
    /// </summary>
    internal sealed class ExpansionOfWizardryEffect : SourceSkillEffectBase
    {
        private const string BodyModelPath = "Effect/magic_powerup.bmd";   // MODEL_SWELL_OF_MAGICPOWER
        private const string FlareModelPath = "Effect/arrowsre06.bmd";     // MODEL_ARROWSRE06
        private const string ShockTexturePath = "Effect/ShockWave.jpg";    // BITMAP_SHOCK_WAVE
        private const string TwlightTexturePath = "Skill/twlighthik01.jpg";// BITMAP_TWLIGHT
        private const string GhostTexturePath = "Skill/2line_gost.jpg";    // BITMAP_2LINE_GHOST
        private const string LightTexturePath = "Effect/flare01.jpg";      // BITMAP_LIGHT
        private const float ModelVelocity = 0.3f;

        private static readonly Vector3 RingLight = new(0.4f, 0.3f, 0.9f);
        private static readonly Vector3 BoneLight = new(0.7f, 0.3f, 0.9f);
        private static readonly Vector3 FlareLight = new(0.2f, 0.2f, 0.9f);
        private static readonly Vector3 GhostLight = new(0.3f, 0.2f, 0.9f);
        private static readonly Vector3 AuraGlowColor = new(0.5f, 0.3f, 1f);

        private struct Ring
        {
            public bool Live;
            public bool Twlight;
            public float Scale;
            public float AngleZ;
            public int PKKey; // short in the original: += factor truncates
            public float LifeTime;
        }

        private struct Flare
        {
            public bool Live;
            public int Bone;
            public float Scale;
            public float Alpha;
            public float LifeTime;
        }

        private struct Ghost
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 TargetPosition;
            public Vector3 Angle;
            public float Velocity;
            public float Scale;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly Ring[] _rings = new Ring[12];
        private readonly Flare[] _flares = new Flare[4];
        private readonly Ghost[] _ghosts = new Ghost[96];
        private SourceModelRenderer? _bodyModel;
        private SourceModelRenderer? _flareModel;
        private Texture2D? _shockTexture;
        private Texture2D? _twlightTexture;
        private Texture2D? _ghostTexture;
        private Texture2D? _lightTexture;
        private float _lifeTime = 45f;
        private float _frame;
        private float _flareFrame;
        private float _lightTime;

        public ExpansionOfWizardryEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 2048)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _shockTexture = await LoadTexture(ShockTexturePath);
            _twlightTexture = await LoadTexture(TwlightTexturePath);
            _ghostTexture = await LoadTexture(GhostTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            // BlendMesh = 0 (ZzzObject.cpp:1906): mesh 0 is additive, so its black texture background drops out.
            _bodyModel = await LoadModel(BodyModelPath, 0);
            if (_bodyModel != null)
            {
                _bodyModel.AnimationSpeed = 0f;
                _bodyModel.UseNormalBlend();
            }
            _flareModel = await LoadModel(FlareModelPath, 0); // BlendMesh = 0 (ZzzObject.cpp:1917)
            if (_flareModel != null)
            {
                _flareModel.AnimationSpeed = 0f;
                _flareModel.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            if (Caster is not PlayerObject)
            {
                _lifeTime = 0f;
                return;
            }

            // Violet aura on the caster, pulsing with each of the three ring waves (every 10 frames).
            _lightTime += factor * 0.04f;
            float wave = _lightTime % 0.4f;
            float pulse = _lightTime < 1.2f ? EffectLights.Flash(wave, 0.4f) * MathHelper.Clamp(wave / 0.05f, 0f, 1f) : 0f;
            float aura = EffectLights.Envelope(_lightTime, 2f, 0.15f, 0.8f) * (0.7f + 0.6f * pulse);
            Lights.Set(0, CasterPosition + new Vector3(0f, 0f, 60f), AuraGlowColor, 340f, aura);

            if (_lifeTime > 0f)
            {
                int lt = (int)_lifeTime;
                if (lt == 45 || lt == 35 || lt == 25)
                {
                    if (FpsCheck(1, factor)) SpawnRing(false);
                    if (FpsCheck(1, factor)) SpawnRing(false);
                    if (FpsCheck(1, factor)) SpawnRing(true);
                }
                if (lt == 45)
                {
                    if (FpsCheck(1, factor)) SpawnFlare(28);
                    if (FpsCheck(1, factor)) SpawnFlare(37);
                }
                if (_lifeTime >= 30f)
                {
                    for (int i = 0; i < 2; i++)
                        if (FpsCheck(1, factor))
                            SpawnGhost();
                }

                _frame += ModelVelocity * factor;
                _lifeTime -= factor;
            }

            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring r = ref _rings[i];
                if (!r.Live)
                    continue;

                r.Scale = MathF.Max(0f, r.Scale - 0.15f * factor);
                if (r.Twlight)
                    r.AngleZ += 10f * factor;
                if (r.LifeTime >= 20f)
                    r.PKKey = (int)(r.PKKey + factor);
                else if (r.LifeTime <= 10f)
                    r.PKKey = (int)(r.PKKey - factor);

                r.LifeTime -= factor;
                if (r.LifeTime <= 0f)
                    r.Live = false;
            }

            bool anyFlare = false;
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (!f.Live)
                    continue;

                anyFlare = true;
                f.Scale *= MathF.Pow(f.LifeTime >= 15f ? 1.05f : 0.95f, factor);
                if (f.LifeTime <= 10f)
                    f.Alpha *= MathF.Pow(0.95f, factor);
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                    f.Live = false;
            }
            if (anyFlare)
                _flareFrame += ModelVelocity * factor;

            MoveGhosts(factor);
        }

        private void SpawnRing(bool twlight)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring r = ref _rings[i];
                if (r.Live)
                    continue;

                r = new Ring { Live = true, Twlight = twlight, Scale = twlight ? 6f : 5f, AngleZ = CasterYawDegrees, LifeTime = 30f };
                return;
            }
        }

        private void SpawnFlare(int bone)
        {
            for (int i = 0; i < _flares.Length; i++)
            {
                ref Flare f = ref _flares[i];
                if (f.Live)
                    continue;

                if (!_flares[0].Live && !_flares[1].Live)
                    _flareFrame = 0f;
                f = new Flare { Live = true, Bone = bone, Scale = 0.9f, Alpha = 1f, LifeTime = 40f };
                return;
            }
        }

        private void SpawnGhost()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                ref Ghost g = ref _ghosts[i];
                if (g.Live)
                    continue;

                Vector3 owner = CasterPosition;
                Vector3 offset = SourceJointMath.Rotate(new Vector3(0f, 300f, 0f), new Vector3(0f, 0f, MuGame.Random.Next(360)));
                g.Live = true;
                g.Scale = 20f + MuGame.Random.Next(10);
                g.LifeTime = 25 + MuGame.Random.Next(10);
                g.Velocity = 20f + MuGame.Random.Next(10);
                g.Position = new Vector3(owner.X + offset.X, owner.Y + offset.Y, MuGame.Random.Next(100) + 200);
                g.TargetPosition = owner;
                g.TargetPosition.Z = g.Position.Z - (MuGame.Random.Next(100) - 100) * (MuGame.Random.Next(2) == 0 ? 1 : -1);
                g.Angle = new Vector3(0f, 0f, SourceJointMath.CreateAngle2D(g.Position, g.TargetPosition));
                int maxTails = SourceJointMath.ScaledMaxTails(15);
                if (g.Trail == null || g.Trail.MaxTails != maxTails)
                    g.Trail = new SourceTrail(maxTails);
                else
                    g.Trail.Clear();
                g.Trail.Push(g.Position, g.Angle, g.Scale);
                return;
            }
        }

        private void MoveGhosts(float factor)
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                ref Ghost g = ref _ghosts[i];
                if (!g.Live)
                    continue;

                SourceJointMath.AngleBasis(g.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                g.Position += -forward * (g.Velocity * factor);

                float horizontal = SourceJointMath.CreateAngle2D(g.Position, g.TargetPosition);
                g.Angle.Z = horizontal - 65f;
                float dx = g.Position.X - g.TargetPosition.X;
                float dy = g.Position.Y - g.TargetPosition.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 60f)
                {
                    float vertical = 360f - SourceJointMath.CreateAngle(g.Position.Z, distance, g.TargetPosition.Z, 0f);
                    g.Angle.X = SourceJointMath.TurnAngle2(g.Angle.X, vertical, 3f); // not factor-scaled in the original
                }
                else
                {
                    g.Angle.Z = horizontal - 90f;
                }

                g.Trail.Push(g.Position, right, up, g.Scale);
                g.LifeTime -= factor;
                if (g.LifeTime < 0f)
                    g.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f)
                return true;
            for (int i = 0; i < _rings.Length; i++) if (_rings[i].Live) return true;
            for (int i = 0; i < _flares.Length; i++) if (_flares[i].Live) return true;
            for (int i = 0; i < _ghosts.Length; i++) if (_ghosts[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;
            Vector3 owner = CasterPosition;

            DrawRings(_shockTexture, false, owner);
            DrawRings(_twlightTexture, true, owner);

            if (Batch.Begin(_ghostTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _ghosts.Length; i++)
                {
                    ref Ghost g = ref _ghosts[i];
                    if (g.Live)
                        Batch.AddTrail(g.Trail, SourceQuadBatch.TrailFaces.Both, GhostLight, SourceQuadBatch.TrailShade.Flat, g.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            Vector3 terrainLight = terrain?.EvaluateTerrainLight(owner.X, owner.Y) ?? Vector3.One;
            var yaw = new Vector3(0f, 0f, MathHelper.ToRadians(CasterYawDegrees));
            if (_lifeTime > 0f && _bodyModel != null)
            {
                _bodyModel.SetAnimationTime(_frame);
                _bodyModel.Update(LastGameTime);
                _bodyModel.DrawInstance(owner, yaw, 0.9f, terrainLight * new Vector3(0.3f, 0.2f, 0.9f), 1f);
            }

            if (_flareModel != null)
            {
                bool updated = false;
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (!f.Live)
                        continue;
                    if (!updated)
                    {
                        _flareModel.SetAnimationTime(_flareFrame);
                        _flareModel.Update(LastGameTime);
                        updated = true;
                    }
                    _flareModel.DrawInstance(GetCasterBonePosition(f.Bone), yaw, f.Scale, FlareLight, 1f, MathF.Max(f.Alpha, 0.003f));
                }
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _flares.Length; i++)
                {
                    ref Flare f = ref _flares[i];
                    if (!f.Live)
                        continue;
                    Vector3 pos = GetCasterBonePosition(f.Bone);
                    Batch.AddSprite(pos, f.Scale, FlareLight);
                    Batch.AddSprite(pos, f.Scale * 0.8f, FlareLight);
                }

                // LifeTime <= 20: every bone glows with a BITMAP_LIGHT sprite (1.5, light * LifeTime * 0.05).
                if (_lifeTime > 0f && _lifeTime <= 20f && Caster is PlayerObject player)
                {
                    Matrix[]? bones = player.GetBoneTransforms();
                    if (bones != null)
                    {
                        Matrix world = player.WorldPosition;
                        Vector3 light = BoneLight * (_lifeTime * 0.05f);
                        for (int b = 0; b < bones.Length; b++)
                            Batch.AddSprite(Vector3.Transform(bones[b].Translation, world), 1.5f, light);
                    }
                }
                Batch.End();
            }
        }

        private void DrawRings(Texture2D? texture, bool twlight, Vector3 owner)
        {
            if (!Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring r = ref _rings[i];
                if (!r.Live || r.Twlight != twlight)
                    continue;
                Vector3 light = RingLight * (r.PKKey * 0.1f);
                Batch.AddTerrainDecal(World?.Terrain, owner.X, owner.Y, r.Scale, r.Scale, light, -r.AngleZ);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            _bodyModel?.Dispose();
            _flareModel?.Dispose();
        }
    }
}
