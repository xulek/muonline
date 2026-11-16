#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Crescent Moon Slash (AT_SKILL_RUSH = 44) and Charge (AT_SKILL_OCCUPY = 269), SourceMain5.2:
    /// packet   WSclient.cpp:4773       PLAYER_ATTACK_RUSH, SOUND_SKILL_SWORD2 (packet sound map)
    /// stage    ZzzCharacter.cpp:2770   every tick until release: 4 x BITMAP_JOINT_SPARK + BITMAP_FIRE sub 2 (RUSH) / 18 (OCCUPY),
    ///          Scale 1.5, at the owner + (rand%30-15, rand%30-15, 20), angle (rand%60+150, 0, yaw); release at AnimationFrame &gt; 5
    /// release  ZzzCharacter.cpp:4543   MODEL_SWORD_FORCE sub 0 / 2, SOUND_BCS_RUSH
    /// force    ZzzEffect.cpp:4647 (LifeTime 15, Scale 0, z + 100, Direction (0,-10,0), BlendMesh 0), 14646 (LifeTime &gt; 12:
    ///          Scale += 0.9F, afterimage sub 1 / 3; else Scale -= 0.05F, Light = LifeTime / 18, 4 x spark + fire 100 below;
    ///          Direction.y -= 2F), 17936 (MoveParticle), ZzzObject.cpp:2584 (sub 2 / 3: BITMAP_KNIGHTST_BLUE)
    ///          afterimage: LifeTime 5, Scale 3.5, Light = LifeTime / 10
    /// fire     ZzzEffectParticle.cpp:495 / 504 (LifeTime 24, Velocity.y -(rand%32-16)*0.1; sub 18: ground_smoke.tga, Light 1),
    ///          4710 / 4721 (Gravity += 0.004F, Scale += Gravity*F, Velocity *= 0.98, z += Gravity*10F; sub 18: Light *= 0.95),
    ///          9109 (sub 18: EnableAlphaBlend3 sprite) / 9116 (4-frame strip, rotation Angle[0])
    /// rings    MonkSystem.cpp:1276 (OCCUPY, hero cast only): SourceMultiShotPool
    /// Not ported: AddTerrainLight.
    /// </summary>
    internal sealed class SwordForceRushEffect : SourceSkillEffectBase
    {
        private const string SwordForcePath = "Skill/SwordForce.bmd";               // MODEL_SWORD_FORCE
        private const string BlueTexturePath = "Effect/knightSt_blue.jpg";          // BITMAP_KNIGHTST_BLUE
        private const string FireTexturePath = "Effect/Fire01.jpg";                 // BITMAP_FIRE
        private const string SmokeTexturePath = "Effect/ground_smoke.tga";          // BITMAP_GROUND_SMOKE
        private const string RushSoundPath = "Sound/battlecastle/sCHaveyBlow.wav";  // SOUND_BCS_RUSH

        private struct Fire
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private struct Afterimage
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
        }

        private readonly bool _occupy;
        private readonly Fire[] _fires = new Fire[160];
        private readonly Afterimage[] _afterimages = new Afterimage[8];
        private readonly SourceSparkPool _sparks = new(96, 0);
        private readonly SourceMultiShotPool? _rings;
        private SourceModelRenderer? _forceRenderer;
        private Texture2D? _fireTexture;
        private Texture2D? _sparkTexture;

        private float _attackTime = 1f;
        private bool _released;
        private bool _forceLive;
        private Vector3 _forcePosition;
        private Vector3 _forceAngle;       // degrees
        private Vector3 _forceLight;
        private float _forceDirection;
        private float _forceScale;
        private float _forceLife;
        private int _fireCount;
        private int _afterimageCount;

        public SwordForceRushEffect(WalkerObject caster, bool occupy)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _occupy = occupy;
            if (occupy && caster is PlayerObject { IsMainWalker: true })
                _rings = new SourceMultiShotPool(1);
        }

        protected override async Task LoadEffectContent()
        {
            _forceRenderer = await LoadModel(SwordForcePath);
            if (_forceRenderer != null)
            {
                _forceRenderer.AnimationSpeed = 0f;
                if (_occupy)
                    _forceRenderer.OverrideAllTextures(await LoadTexture(BlueTexturePath));
            }

            _fireTexture = await LoadTexture(_occupy ? SmokeTexturePath : FireTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.JointTexturePath);
            if (_rings != null)
                await _rings.LoadAsync(World);
            await SoundController.Instance.PreloadSoundAsync(RushSoundPath);
        }

        protected override void OnStart()
        {
            _rings?.SpawnSet(CasterPosition, OwnerAngleDegrees, new Vector3(0.8f, 0.9f, 1.6f));
        }

        private Vector3 OwnerAngleDegrees => new(
            MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

        protected override void OnTick(float factor)
        {
            if (!_released)
            {
                bool release = CasterFrame > 5f || (int)_attackTime >= 15;
                if (FpsCheck(1, factor))
                {
                    Vector3 position = CasterPosition;
                    position.X += MuGame.Random.Next(30) - 15f;
                    position.Y += MuGame.Random.Next(30) - 15f;
                    position.Z += 20f;
                    for (int i = 0; i < 4; i++)
                    {
                        var angle = new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, CasterYawDegrees);
                        _sparks.SpawnJoint(position, angle);
                        SpawnFire(position, angle, Vector3.One);
                    }
                }

                _attackTime += factor;
                if (release)
                    Release(factor);
            }
            else
            {
                TickForce(factor);
            }

            TickAfterimages(factor);
            TickFires(factor);
            _sparks.Move(factor, World?.Terrain);
            _rings?.Move(factor);
        }

        private void Release(float factor)
        {
            _released = true;
            _forceLive = true;
            _forcePosition = CasterPosition;
            _forcePosition.Z += 100f * factor;
            _forceAngle = OwnerAngleDegrees;
            _forceLight = Vector3.One;
            _forceDirection = -10f;
            _forceScale = 0f;
            _forceLife = 15f;
            SoundController.Instance.PlayBuffer(RushSoundPath);
        }

        private void TickForce(float factor)
        {
            if (!_forceLive)
                return;

            if (_forceLife > 12f)
            {
                _forceScale += 0.9f * factor;
                _forceDirection -= 2f * factor;
                if (FpsCheck(1, factor))
                    SpawnAfterimage(_forcePosition);
            }
            else
            {
                _forceScale -= 0.05f * factor;
                _forceLight = new Vector3(_forceLife / 18f);
                _forceDirection -= 2f * factor;

                Vector3 position = _forcePosition;
                position.X += MuGame.Random.Next(30) - 15f;
                position.Y += MuGame.Random.Next(30) - 15f;
                position.Z -= 100f;
                for (int i = 0; i < 4; i++)
                {
                    var angle = new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, _forceAngle.Z);
                    if (FpsCheck(1, factor))
                        _sparks.SpawnJoint(position, angle);
                    if (FpsCheck(1, factor))
                        SpawnFire(position, angle, _forceLight);
                }
            }

            _forcePosition += SourceJointMath.Rotate(new Vector3(0f, _forceDirection, 0f), _forceAngle) * factor;
            _forceLife -= factor;
            if (_forceLife <= 0f)
                _forceLive = false;
        }

        private void SpawnAfterimage(Vector3 position)
        {
            for (int i = 0; i < _afterimages.Length; i++)
            {
                ref Afterimage a = ref _afterimages[i];
                if (a.Live)
                    continue;
                a = new Afterimage { Live = true, Position = position, LifeTime = 5f };
                _afterimageCount++;
                return;
            }
        }

        private void TickAfterimages(float factor)
        {
            if (_afterimageCount == 0)
                return;

            for (int i = 0; i < _afterimages.Length; i++)
            {
                ref Afterimage a = ref _afterimages[i];
                if (!a.Live)
                    continue;
                a.LifeTime -= factor;
                if (a.LifeTime <= 0f)
                {
                    a.Live = false;
                    _afterimageCount--;
                }
            }
        }

        private void SpawnFire(Vector3 position, Vector3 angleDeg, Vector3 light)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (f.Live)
                    continue;

                f.Live = true;
                f.Position = position;
                f.Angle = angleDeg;
                f.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                f.Light = _occupy ? Vector3.One : light;
                f.Scale = 1.5f;
                f.Rotation = MuGame.Random.Next(360);
                f.Gravity = 0f;
                f.LifeTime = 24f;
                _fireCount++;
                return;
            }
        }

        private void TickFires(float factor)
        {
            if (_fireCount == 0)
                return;

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;

                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                {
                    f.Live = false;
                    _fireCount--;
                    continue;
                }

                f.Position += SourceJointMath.Rotate(f.Velocity, f.Angle) * factor;
                f.Gravity += 0.004f * factor;
                f.Scale += f.Gravity * factor;
                f.Velocity *= 0.98f; // per frame
                f.Position.Z += f.Gravity * 10f * factor;
                if (_occupy)
                    f.Light *= 0.95f; // per frame
            }
        }

        protected override bool HasActiveContent() =>
            !_released || _forceLive || _afterimageCount > 0 || _fireCount > 0 || _sparks.HasLive || (_rings?.HasLive ?? false);

        protected override void OnDraw()
        {
            _sparks.DrawJoints(Batch, _sparkTexture);

            if (_forceRenderer != null && (_forceLive || _afterimageCount > 0))
            {
                // BodyLight = RequestTerrainLight + o->Light; the mesh is the blend mesh (x BlendMeshLight = Light).
                var terrain = World?.Terrain;
                Vector3 angle = new(MathHelper.ToRadians(_forceAngle.X), MathHelper.ToRadians(_forceAngle.Y), MathHelper.ToRadians(_forceAngle.Z));
                if (_forceLive)
                {
                    Vector3 body = (terrain?.EvaluateTerrainLight(_forcePosition.X, _forcePosition.Y) ?? Vector3.Zero) + _forceLight;
                    _forceRenderer.DrawInstance(_forcePosition, angle, _forceScale, body * (_forceLife > 12f ? 1f : _forceLife / 18f), 1f);
                }

                for (int i = 0; i < _afterimages.Length; i++)
                {
                    ref Afterimage a = ref _afterimages[i];
                    if (!a.Live)
                        continue;
                    float fade = a.LifeTime / 10f;
                    Vector3 body = (terrain?.EvaluateTerrainLight(a.Position.X, a.Position.Y) ?? Vector3.Zero) + new Vector3(fade);
                    _forceRenderer.DrawInstance(a.Position, angle, 3.5f, body * fade, 1f);
                }
            }

            _rings?.Draw(LastGameTime);

            if (_fireCount > 0 && _fireTexture != null)
            {
                if (_occupy)
                {
                    if (Batch.Begin(_fireTexture, Blendings.Alpha, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                    {
                        float halfWidth = _fireTexture.Width * 0.5f;
                        float halfHeight = _fireTexture.Height * 0.5f;
                        for (int i = 0; i < _fires.Length; i++)
                        {
                            ref Fire f = ref _fires[i];
                            if (!f.Live)
                                continue;
                            // RenderSprite on a 4-component texture: RGBA = (Light, Light[0]).
                            var color = new Color(f.Light.X, f.Light.Y, f.Light.Z, f.Light.X);
                            Batch.AddBillboard(f.Position, halfWidth * f.Scale, halfHeight * f.Scale, MathHelper.ToRadians(f.Rotation), color);
                        }
                        Batch.End();
                    }
                }
                else if (Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                {
                    for (int i = 0; i < _fires.Length; i++)
                    {
                        ref Fire f = ref _fires[i];
                        if (!f.Live)
                            continue;
                        int frame = (int)((23f - f.LifeTime) / 6f);
                        Batch.AddSpriteRegion(f.Position, _fireTexture.Width * f.Scale * 0.25f, _fireTexture.Height * f.Scale, f.Light, f.Angle.X,
                            frame % 4 * 0.25f, 0f, 0.25f, 1f);
                    }
                    Batch.End();
                }
            }
        }

        protected override void DisposeEffect()
        {
            _forceRenderer?.Dispose();
            _rings?.Dispose();
        }
    }
}
