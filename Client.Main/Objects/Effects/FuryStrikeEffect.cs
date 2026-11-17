#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Data.ATT;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Rageful Blow (AT_SKILL_FURY_STRIKE = 42, masters AT_SKILL_ANGER_SWORD_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4948       PLAYER_ATTACK_SKILL_FURY_STRIKE
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: FURY_STRIKE action, AnimationFrame >= 1 -> AttackTime = 15
    /// release ZzzCharacter.cpp:3985   MODEL_SKILL_FURY_STRIKE, SOUND_FURY_STRIKE1
    /// fury    ZzzEffect.cpp:1930 (init), 9903 (move: swing, LifeTime 13 tails, 11 impact, 10 cracks), 18027 RenderFuryStrike
    /// models  ZzzEffect.cpp:1945-2005 (EarthQuake01-08 init), 10133-10290 (move), MODEL_WAVE 1377/9591,
    ///         MODEL_TAIL 1386/9609, MODEL_STONE1/2 sub 0 2760/10953 (BITMAP_FIRE 1..3 children)
    /// particles BITMAP_EXPLOTION sub 0 (ZzzEffectParticle.cpp:2477/4231/8994), BITMAP_FIRE sub 1..3 (398/4721/9112)
    /// </summary>
    internal sealed class FuryStrikeEffect : SourceSkillEffectBase
    {
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg"; // BITMAP_EXPLOTION (4x4)
        private const string FireTexturePath = "Effect/Fire01.jpg";           // BITMAP_FIRE (4x1)
        private const string Sound1 = "Sound/eRageBlow_1.wav";
        private const string Sound2 = "Sound/eRageBlow_2.wav";
        private const string Sound3 = "Sound/eRageBlow_3.wav";
        private const string ExplosionSound = "Sound/eExplosion.wav";          // SOUND_EXPLOTION01 (CreateParticle)

        private const int SkillId = 42;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f; // CreateEffect default o->Velocity = PlayAnimation speed

        private enum Kind { Quake1, Quake2, Quake3, Quake4, Quake5, Quake7, Quake8, Wave, Tail, Stone1, Stone2, Count }

        private static readonly string[] ModelPaths =
        {
            "Skill/EarthQuake01.bmd", "Skill/EarthQuake02.bmd", "Skill/EarthQuake03.bmd", "Skill/EarthQuake04.bmd",
            "Skill/EarthQuake05.bmd", "Skill/EarthQuake07.bmd", "Skill/EarthQuake08.bmd",
            "Skill/flashing.bmd", "Skill/tail.bmd", "Skill/Stone01.bmd", "Skill/Stone02.bmd",
        };

        // o->BlendMesh per type (-1 none, 0 mesh 0, -2 all meshes).
        private static readonly int[] ModelBlendMesh = { 0, 0, -1, 0, 0, 0, 0, 0, -2, -1, -1 };

        private struct ModelFx
        {
            public bool Live;
            public Kind Kind;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public float Gravity;
            public float BlendMeshLight;
            public float TexCoordU;
        }

        private struct Particle
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private readonly ModelFx[] _models = new ModelFx[192];
        private readonly Particle[] _explosions = new Particle[8];
        private readonly Particle[] _fires = new Particle[128];
        private readonly SourceSparkPool _sparkPool = new(96, 32);
        private readonly SourceModelRenderer?[] _renderers = new SourceModelRenderer?[(int)Kind.Count];
        private readonly float[] _kindFrame = new float[(int)Kind.Count];

        private Texture2D? _explosionTexture;
        private Texture2D? _fireTexture;
        private Texture2D? _sparkJointTexture;
        private Texture2D? _sparkTexture;
        private SourceModelRenderer? _weapon;

        // MODEL_SKILL_FURY_STRIKE
        private bool _furyLive;
        private float _furyLifeTime;
        private Vector3 _furyPosition;
        private Vector3 _furyStart;
        private Vector3 _furyAngle;
        private Vector3 _furyHeadAngle;
        private float _furyGravity;
        private int _furySubType;
        private bool _gravityFlipped;
        private bool _sound2Played;
        private float _impactAge = -1f;
        private Vector3 _impactPos;
        private static readonly Vector3 BlastGlow = new(1f, 0.55f, 0.25f);
        private static readonly Vector3 EmberGlow = new(1f, 0.4f, 0.12f);

        // o->Kind: 0 = Rageful Blow (weapon swing, tails, sparks, wave); 2 = Dark Horse Earthshake (GOBoid.cpp:771).
        private readonly int _kind;

        public FuryStrikeEffect(WalkerObject caster, int kind = 0)
            : base(caster, kind == 0 ? 1f : 0f, kind == 0 ? ReleaseDelaySeconds : 0f, 1024)
        {
            _kind = kind;
        }

        protected override async Task LoadEffectContent()
        {
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _fireTexture = await LoadTexture(FireTexturePath);
            _sparkJointTexture = await LoadTexture(SourceSparkPool.JointTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                var renderer = await LoadModel(ModelPaths[k], ModelBlendMesh[k]);
                if (renderer == null)
                    continue;

                renderer.AnimationSpeed = 0f;
                if (ModelBlendMesh[k] != -2)
                    renderer.UseNormalBlend();
                _renderers[k] = renderer;
            }

            if (Caster is PlayerObject player && player.Weapon1?.Model != null)
            {
                WeaponObject weapon = player.Weapon1;
                var renderer = new SourceModelRenderer(weapon.Model)
                {
                    ItemLevel = weapon.ItemLevel,
                    MaterialItemGroup = weapon.MaterialItemGroup,
                    MaterialItemIndex = weapon.MaterialItemIndex,
                };
                if (await renderer.LoadInto(World))
                    _weapon = renderer;
            }
        }

        protected override void OnStart()
        {
            Vector3 angle = new(0f, 0f, CasterYawDegrees);
            _furyLive = true;
            _furyPosition = CasterPosition;
            _furyStart = _furyPosition;
            _furyHeadAngle = angle + new Vector3(80f, 0f, 180f);
            _furyAngle = angle + new Vector3(0f, 0f, 330f);
            _furyLifeTime = 20f;
            _furySubType = MuGame.Random.Next(100);
            _furyGravity = 50f;
            if (_kind == 0)
                PlayUnmappedSound(SkillId, Sound1);
        }

        protected override void OnTick(float factor)
        {
            if (_furyLive)
                MoveFury(factor);

            MoveModels(factor);
            MoveParticles(factor);
            _sparkPool.Move(factor, World?.Terrain);

            // Dynamic lights: blast flash at the ground impact, then a flickering ember glow from the cracks.
            if (_impactAge >= 0f)
            {
                _impactAge += factor;
                Vector3 pos = _impactPos + new Vector3(0f, 0f, 50f);
                Lights.Set(0, pos, BlastGlow, MathF.Max(200f, 440f - _impactAge * 8f),
                    2.3f * MathF.Min(1f, _impactAge / 1.5f) * EffectLights.Flash(_impactAge, 16f, 1.5f));
                Lights.Set(1, pos, EmberGlow, 380f,
                    0.85f * EffectLights.Envelope(_impactAge, 40f, 4f, 16f) * EffectLights.Flicker(_impactAge * 0.04f, 1.7f, 0.15f));
            }
        }

        // ------------------------------------------------------------------ MODEL_SKILL_FURY_STRIKE

        private void MoveFury(float factor)
        {
            Vector3 ownerAngle = new(0f, 0f, CasterYawDegrees);
            int lifeTime = (int)_furyLifeTime;

            if (lifeTime == 11)
            {
                _furyStart = _furyPosition + SourceJointMath.Rotate(new Vector3(-25f, -80f, 0f), ownerAngle);
                _furyStart.Z = TerrainHeight(_furyStart) + 25f;
                if (_impactAge < 0f)
                {
                    _impactAge = 0f;
                    _impactPos = _furyStart;
                }

                if (FpsCheck(1, factor))
                    SpawnExplosion(_furyStart, 0.5f);

                for (int j = 0; j < 8 && _kind == 0; j++)
                {
                    if (!FpsCheck(1, factor))
                        continue;

                    var angle = new Vector3(MuGame.Random.Next(60) - 60f, 0f, MuGame.Random.Next(30) + 90f) + _furyAngle;
                    var pos = new Vector3(_furyStart.X + MuGame.Random.Next(20) - 10, _furyStart.Y + MuGame.Random.Next(20) - 10, _furyStart.Z);
                    if (FpsCheck(1, factor))
                        _sparkPool.SpawnJoint(pos, angle);
                    if (FpsCheck(8, factor) && FpsCheck(1, factor))
                        _sparkPool.SpawnSpark(pos, angle);
                }

                if (_kind == 0 && FpsCheck(1, factor))
                    SpawnModel(Kind.Wave, _furyStart, Vector3.Zero, 0f);

                _furyStart.Z -= 27f;

                if (FpsCheck(1, factor)) SpawnModel(Kind.Quake3, _furyStart, Vector3.Zero, 1.5f);
                if (FpsCheck(1, factor)) SpawnModel(Kind.Quake1, _furyStart, Vector3.Zero, 1.5f);
                if (FpsCheck(1, factor)) SpawnModel(Kind.Quake2, _furyStart, Vector3.Zero, 1.5f);

                for (int i = 0; i < 5; i++)
                {
                    var p = new Vector3(0f, MuGame.Random.Next(150) + 100, 0f);
                    Vector3 position = SourceJointMath.Rotate(p, new Vector3(0f, 0f, _furySubType + i * 72f)) + _furyStart;
                    position.Z += 3f;
                    float scale = (MuGame.Random.Next(50) + 40) / 100f;
                    var angle = new Vector3(0f, 0f, 45 + (float)(MuGame.Random.Next(30) - 15));

                    if (IsBlockedTile(position, requireAll: false))
                        continue;

                    position.Z = TerrainHeight(position) + 3f;
                    if (FpsCheck(1, factor)) SpawnModel(Kind.Quake4, position, angle, scale);
                    if (FpsCheck(1, factor)) SpawnModel(Kind.Quake5, position, angle, scale);
                }
            }
            else if (lifeTime == 10)
            {
                SpawnCracks(factor);
                _furyLifeTime = 0f;
                SoundController.Instance.PlayBuffer(Sound3);
            }
            else
            {
                float count = _furyLifeTime;
                float addAngle = 15f;
                if (_furyLifeTime > 9f && _furyLifeTime < 16f)
                {
                    count = 12.5f;
                    addAngle = 18f;
                    // Gravity *= pow(-1, FPS_ANIMATION_FACTOR): one sign flip per swing.
                    if (lifeTime == 15 && !_gravityFlipped)
                    {
                        _furyGravity = -_furyGravity;
                        _gravityFlipped = true;
                    }
                }

                if (_kind == 0 && lifeTime == 13)
                {
                    Vector3 pos = _furyPosition + SourceJointMath.Rotate(new Vector3(-25f, -40f, 0f), ownerAngle);
                    for (int i = 0; i < 4; i++)
                        if (FpsCheck(1, factor))
                            SpawnModel(Kind.Tail, new Vector3(pos.X, pos.Y, pos.Z - i * 50), Vector3.Zero, 1f);

                    pos.X += MuGame.Random.Next(30) + 20;
                    pos.Z += MuGame.Random.Next(500) - 250;
                    for (int i = 0; i < 4; i++)
                        if (FpsCheck(1, factor))
                            SpawnModel(Kind.Tail, new Vector3(pos.X, pos.Y, pos.Z - i * 30), Vector3.Zero, 1f);

                    if (!_sound2Played)
                    {
                        SoundController.Instance.PlayBuffer(Sound2);
                        _sound2Played = true;
                    }
                }

                float angleDeg = (20f - count) * addAngle;
                _furyAngle.X += 80f * factor;
                float direction1 = MathF.Sin(MathHelper.ToRadians(angleDeg)) * 260f;
                if (count != 12.5f)
                    _furyGravity += 8f * factor;
                else
                    _furyGravity -= 8f * factor;

                _furyPosition = _furyStart + SourceJointMath.Rotate(new Vector3(0f, direction1, 0f), _furyHeadAngle);
                _furyPosition.Z += _furyGravity + 200f;
            }

            _furyLifeTime -= factor;
            if (_furyLifeTime <= 0f)
                _furyLive = false;
        }

        private void SpawnCracks(float factor)
        {
            Span<Vector3> pos = stackalloc Vector3[5];
            Span<float> ang = stackalloc float[5];
            for (int j = 0; j < 5; j++)
            {
                pos[j] = _furyStart;
                ang[j] = 0f;
            }

            int count = 0;
            for (int j = 0; j < 4; j++)
            {
                var p = new Vector3(0f, MuGame.Random.Next(15) + 85f, 0f);
                if (j >= 3)
                    count = MuGame.Random.Next();

                for (int i = 0; i < 5; i++)
                {
                    int random = count % 2 == 0 ? MuGame.Random.Next(30) + 50 : -(MuGame.Random.Next(30) + 50);
                    ang[i] += random;
                    float angleZ = ang[i] + i * (MuGame.Random.Next(10) + 62);
                    pos[i] += SourceJointMath.Rotate(p, new Vector3(0f, 0f, angleZ));

                    // The original tests (!NOMOVE || !NOGROUND || !WATER): only a tile with all three flags is skipped.
                    if (IsBlockedTile(pos[i], requireAll: true))
                        continue;

                    pos[i].Z = TerrainHeight(pos[i]) + 3f;
                    var angle = new Vector3(0f, 0f, angleZ + 270f);
                    if (FpsCheck(1, factor)) SpawnModel(Kind.Quake7, pos[i], angle, 1f);
                    if (FpsCheck(1, factor)) SpawnModel(Kind.Quake8, pos[i], angle, 1f);
                }

                count++;
            }
        }

        private bool IsBlockedTile(Vector3 position, bool requireAll)
        {
            var terrain = World?.Terrain;
            if (terrain == null)
                return false;

            var flags = terrain.RequestTerrainFlag((int)(position.X / Constants.TERRAIN_SCALE), (int)(position.Y / Constants.TERRAIN_SCALE));
            bool noMove = flags.HasFlag(TWFlags.NoMove);
            bool noGround = flags.HasFlag(TWFlags.NoGround);
            bool water = flags.HasFlag(TWFlags.Water);
            return requireAll ? noMove && noGround && water : noMove || noGround || water;
        }

        // ------------------------------------------------------------------ model children

        private void SpawnModel(Kind kind, Vector3 position, Vector3 angle, float scale)
        {
            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelFx m = ref _models[i];
                if (m.Live)
                    continue;

                if (!AnyLive(kind))
                    _kindFrame[(int)kind] = 0f;

                m = default;
                m.Live = true;
                m.Kind = kind;
                m.Position = position;
                m.Angle = angle;
                m.Scale = scale;
                m.Light = Vector3.One;
                m.BlendMeshLight = 1f;

                switch (kind)
                {
                    case Kind.Quake1: m.LifeTime = 35f; break;
                    case Kind.Quake2: m.LifeTime = 20f; break;
                    case Kind.Quake3: m.LifeTime = 35f; break;
                    case Kind.Quake4: m.LifeTime = 35f; break;
                    case Kind.Quake5:
                    case Kind.Quake7:
                    case Kind.Quake8: m.LifeTime = 40f; break;
                    case Kind.Wave:
                        m.BlendMeshLight = 1.5f;
                        m.Scale = 0.5f;
                        m.LifeTime = 15f;
                        m.Position.Z -= 15f * SourceJointMath.FrameFactor;
                        break;
                    case Kind.Tail:
                        m.LifeTime = 6f;
                        m.Scale = 1f;
                        m.Gravity = 80f;
                        m.Light = new Vector3(0.5f);
                        m.Angle = new Vector3(0f, 0f, 45f);
                        break;
                    case Kind.Stone1:
                    case Kind.Stone2:
                        m.LifeTime = MuGame.Random.Next(16) + 32;
                        m.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                        m.Angle.Z = MuGame.Random.Next(360);
                        m.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f), m.Angle);
                        m.Gravity = MuGame.Random.Next(16) + 8;
                        break;
                }
                return;
            }
        }

        private bool AnyLive(Kind kind)
        {
            for (int i = 0; i < _models.Length; i++)
                if (_models[i].Live && _models[i].Kind == kind) return true;
            return false;
        }

        private void MoveModels(float factor)
        {
            for (int k = 0; k < (int)Kind.Count; k++)
                _kindFrame[k] += ModelVelocity * factor;

            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelFx m = ref _models[i];
                if (!m.Live)
                    continue;

                float lt = m.LifeTime;
                switch (m.Kind)
                {
                    case Kind.Quake1:
                        if (lt > 15f && (int)lt % 3 == 0)
                            Client.Main.Graphics.Camera.Instance.SetEarthQuake((MuGame.Random.Next(8) - 4) * 0.1f); // MODEL_SKILL_FURY_STRIKE + 1, ZzzEffect.cpp:10139
                        goto case Kind.Quake4;
                    case Kind.Quake4:
                    case Kind.Quake7:
                        m.BlendMeshLight = lt * 0.1f / 3f;
                        if (lt < 10f) m.Position.Z -= 0.5f * factor;
                        break;
                    case Kind.Quake3:
                        m.BlendMeshLight = lt * 0.1f / 10f;
                        if (lt < 13f) m.Position.Z -= 0.5f * factor;
                        break;
                    case Kind.Quake2:
                        if (m.Scale > 50f) m.LifeTime = 0f;
                        if (m.LifeTime >= 10f)
                        {
                            m.BlendMeshLight = (20f - m.LifeTime) * 0.1f;
                        }
                        else
                        {
                            if (m.LifeTime < 5f) m.Position.Z -= 0.5f * factor;
                            m.BlendMeshLight = m.LifeTime * 0.1f;
                            if (m.LifeTime >= 5f && FpsCheck(10, factor))
                                SpawnStone(m.Position, m.Angle, factor);
                        }
                        m.TexCoordU = -(int)m.LifeTime * 0.01f;
                        break;
                    case Kind.Quake5:
                        if (lt >= 30f)
                        {
                            m.BlendMeshLight = (40f - lt) * 0.1f;
                        }
                        else
                        {
                            if (lt < 15f) m.Position.Z -= 0.5f * factor;
                            m.BlendMeshLight = lt * 0.1f;
                            if (lt >= 5f && FpsCheck(15, factor))
                                SpawnStone(m.Position, m.Angle, factor);
                        }
                        m.TexCoordU = -(int)lt * 0.01f;
                        break;
                    case Kind.Quake8:
                        m.BlendMeshLight = lt >= 30f ? (40f - lt) * 0.1f : lt * 0.1f;
                        if (lt < 15f) m.Position.Z -= 0.5f * factor;
                        m.TexCoordU = -(int)lt * 0.01f;
                        break;
                    case Kind.Wave:
                        if (m.Scale > 2f)
                        {
                            m.Scale += 0.1f * factor;
                            m.Position.X -= 1f * factor;
                            m.Position.Z -= 1.5f * factor;
                            m.BlendMeshLight = lt / 30f;
                        }
                        else
                        {
                            m.Scale += 1.2f * factor;
                            m.Position.X -= 1.2f * factor;
                            m.Position.Z -= 1.8f * factor;
                        }
                        break;
                    case Kind.Tail:
                        m.Position.Z -= m.Gravity * factor;
                        m.Gravity += 60f * factor;
                        m.BlendMeshLight = lt / 20f;
                        break;
                    case Kind.Stone1:
                    case Kind.Stone2:
                        MoveStone(ref m, factor);
                        break;
                }

                m.LifeTime -= factor;
                if (m.LifeTime <= 0f)
                    m.Live = false;
            }
        }

        private void SpawnStone(Vector3 origin, Vector3 angle, float factor)
        {
            if (!FpsCheck(1, factor))
                return;

            Vector3 offset = SourceJointMath.Rotate(new Vector3(0f, MuGame.Random.Next(150), 0f), new Vector3(0f, 0f, MuGame.Random.Next(360)));
            SpawnModel(MuGame.Random.Next(2) == 0 ? Kind.Stone1 : Kind.Stone2, origin + offset, angle, 0f);
        }

        private void MoveStone(ref ModelFx m, float factor)
        {
            m.Position += m.Direction * factor; // sub 0: Direction was rotated at init (ZzzEffect.cpp:11012)
            m.Direction *= 0.9f; // per frame, not scaled by the factor
            m.Position.Z += m.Gravity * factor;
            m.Gravity -= 3f * factor;

            float height = TerrainHeight(m.Position);
            if (m.Position.Z < height)
            {
                m.Position.Z = height;
                m.Gravity = -m.Gravity * 0.5f;
                m.LifeTime -= 4f * factor;
                m.Angle.X -= m.Scale * 128f * factor;
            }
            else
            {
                m.Angle.X -= m.Scale * 32f * factor;
            }

            if (FpsCheck(10, factor) && FpsCheck(1, factor))
                SpawnFire(m.Position, m.Angle, m.Light, 1 + MuGame.Random.Next(3));
        }

        // ------------------------------------------------------------------ particles

        private void SpawnExplosion(Vector3 position, float scale)
        {
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.Light = Vector3.One;
                p.Scale = scale;
                p.LifeTime = 20f;
                SoundController.Instance.PlayBuffer(ExplosionSound);
                return;
            }
        }

        private void SpawnFire(Vector3 position, Vector3 angle, Vector3 light, int subType)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.SubType = subType;
                p.Position = position;
                p.Angle = angle;
                p.Light = light;
                p.LifeTime = 24f;
                p.Rotation = MuGame.Random.Next(360);
                if (subType == 1)
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                    p.Scale = (MuGame.Random.Next(4) + 10) * 0.01f;
                }
                else
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                    p.Scale = 1f;
                }
                return;
            }
        }

        private void MoveParticles(float factor)
        {
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Gravity += 0.004f * factor;
                p.Scale += p.Gravity * factor;
                p.Velocity *= 0.98f; // per frame
                p.Position.Z += p.Gravity * 10f * factor;
            }
        }

        // ------------------------------------------------------------------ lifecycle / draw

        protected override bool HasActiveContent()
        {
            if (_furyLive || _sparkPool.HasLive)
                return true;
            for (int i = 0; i < _models.Length; i++)
                if (_models[i].Live) return true;
            for (int i = 0; i < _explosions.Length; i++)
                if (_explosions[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++)
                if (_fires[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            _sparkPool.DrawJoints(Batch, _sparkJointTexture);

            // RenderFuryStrike: the caster weapon while LifeTime > 10 (Kind 0).
            if (_kind == 0 && _furyLive && _furyLifeTime > 10f && _weapon != null)
            {
                _weapon.DrawInstance(_furyPosition, ToRadians(_furyAngle), 1f, TerrainLight(_furyPosition) + Vector3.One, 1f);
            }

            DrawModels();

            if (Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _explosions.Length; i++)
                {
                    ref Particle p = ref _explosions[i];
                    if (!p.Live || _explosionTexture == null)
                        continue;

                    int frame = (int)((20f - p.LifeTime) / 2f);
                    Batch.AddSpriteRegion(p.Position, _explosionTexture.Width * p.Scale, _explosionTexture.Height * p.Scale, p.Light, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                }
                Batch.End();
            }

            if (Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Particle p = ref _fires[i];
                    if (!p.Live || _fireTexture == null)
                        continue;

                    int frame = (int)((23f - p.LifeTime) / 6f);
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, p.Angle.X,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            _sparkPool.DrawSparks(Batch, _sparkTexture);
        }

        private void DrawModels()
        {
            for (int k = 0; k < (int)Kind.Count; k++)
            {
                SourceModelRenderer? renderer = _renderers[k];
                if (renderer == null || !AnyLive((Kind)k))
                    continue;

                renderer.SetAnimationTime(_kindFrame[k]);
                renderer.Update(LastGameTime);

                for (int i = 0; i < _models.Length; i++)
                {
                    ref ModelFx m = ref _models[i];
                    if (!m.Live || (int)m.Kind != k)
                        continue;

                    // Quantized so instances spawned together share one vertex-colour bake.
                    renderer.ScrollU = m.TexCoordU;
                    float blendMeshLight = MathF.Round(m.BlendMeshLight * 50f) / 50f;
                    Vector3 light = TerrainLight(m.Position) * m.Light;
                    renderer.DrawInstance(m.Position, ToRadians(m.Angle), m.Scale, light,
                        ModelBlendMesh[k] == -1 ? 1f : MathF.Max(blendMeshLight, 0.003f));
                }
            }
        }

        private Vector3 TerrainLight(Vector3 position) =>
            World?.Terrain?.EvaluateTerrainLight(position.X, position.Y) ?? Vector3.One;

        private static Vector3 ToRadians(Vector3 degrees) =>
            new(MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.Z));

        protected override void DisposeEffect()
        {
            _weapon?.Dispose();
            for (int k = 0; k < _renderers.Length; k++)
                _renderers[k]?.Dispose();
        }
    }
}
