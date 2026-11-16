#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    public enum ArrowVolleyKind : byte
    {
        Normal,
        TripleShot,
        IceArrow,
        Penetration,
        DeepImpact,
        MultiShot
    }

    /// <summary>
    /// Bow / crossbow projectiles of SourceMain5.2: CreateArrows / CreateArrow (ZzzEffectMagicSkill.cpp:250 / 171)
    /// called from the release switch (ZzzCharacter.cpp:4667-4762, default gate AnimationFrame >= 5) and from
    /// MODEL_BLADE_SKILL sub 1 for Multi-Shot (WSclient.cpp:5063, ZzzEffect.cpp:7060).
    /// init    ZzzEffect.cpp:1605 (BEST_CROSSBOW), 1624 (DOUBLE), 1643 (HOLY), 1684 (ARROW .. GAMBLE), 1863 (BOMB),
    ///         3237 (DRILL), 4609 (IMPACT), 1551 (MODEL_PIERCING), 3292 (MODEL_WAVES), 4808 (MODEL_FEATHER)
    /// move    ZzzEffect.cpp:11710-12140, 12822 (DRILL), 14593 (IMPACT), 11662 (PIERCING), 12883 (WAVES 3 / 4),
    ///         15466 (FEATHER 0 / 1), epilogue 17884 (PlayAnimation, MoveParticle(o, true)), MoveJump 6381,
    ///         CheckClientArrow 6621, CheckCharacterRange 38
    /// render  default RenderObject; ZzzEffect.cpp:18332 (HOLY: no model), ZzzObject.cpp:1539 (SPARK), 1800
    ///         (DARKSTINGER), 1823 (GAMBLE), 2622 (DRILL sprites)
    /// joints  ZzzEffectJoint.cpp:2021 / 5898 (BITMAP_FLARE + 1 sub 0..3, 12..15, 18), 242 / 3042 (JOINT_ENERGY sub 5),
    ///         1923 / 5564 (BITMAP_FLARE sub 11 / 25), 2531 / 6630 (BITMAP_FLASH sub 2 / 3 / 4)
    /// particles ZzzEffectParticle.cpp: BITMAP_FIRE 0, BITMAP_FLARE 5 (776 / 5055), FLOWER01.. (164 / 3954),
    ///         BUBBLE 1 (204 / 4187 / 8978), SMOKE 0 / 13 / 24 / 34 / 35, SPARK + 1 sub 10 / 30, SHINY 3
    /// One-shot spawns behind (int)LifeTime tests use the 25 Hz behaviour; F-scaled spawn offsets use F = 1.
    /// </summary>
    internal sealed class ArrowProjectileEffect : SourceSkillEffectBase
    {
        private const int MaxArrows = 8;
        private const int MaxJoints = MaxArrows * 5;
        private const int MaxWaves = 96;
        private const int MaxFeathers = 64;
        private const float GateFrame = 5f;
        private const float GateTimeoutSeconds = 14f * 0.04f;

        private const string FlareTrailTexture = "Effect/flare02.jpg";    // BITMAP_FLARE + 1 (GL_REPEAT)
        private const string EnergyTexture = "Effect/JointLaser01.jpg";   // BITMAP_JOINT_ENERGY
        private const string FlareTexture = "Effect/Flare.jpg";           // BITMAP_FLARE
        private const string FlareBlueTexture = "Effect/flareBlue.jpg";   // BITMAP_FLARE_BLUE
        private const string FlareForceTexture = "Effect/NSkill.jpg";     // BITMAP_FLARE_FORCE
        private const string LightTexture = "Effect/flare01.jpg";         // BITMAP_LIGHT
        private const string LightningTexture = "Effect/lightning2.jpg";  // BITMAP_LIGHTNING + 1
        private const string ShinyTexture = "Effect/Shiny01.jpg";         // BITMAP_SHINY
        private const string Shiny1Texture = "Effect/Shiny02.jpg";        // BITMAP_SHINY + 1
        private const string Shiny2Texture = "Effect/Shiny03.jpg";        // BITMAP_SHINY + 2
        private const string FireTexture = "Effect/Fire01.jpg";           // BITMAP_FIRE
        private const string SmokeTexture = "Effect/smoke01.jpg";         // BITMAP_SMOKE
        private const string BubbleTexture = "Object8/drop01.jpg";        // BITMAP_BUBBLE (MapManager.cpp:48)
        private const string PiercingModel = "Skill/Piercing.bmd";        // MODEL_PIERCING
        private const string WavesModel = "Skill/m_waves.bmd";            // MODEL_WAVES
        private const string FeatherModel = "Skill/darkwing_hetachi.bmd"; // MODEL_FEATHER
        private static readonly string[] FlowerTextures = { "Skill/flower1.tga", "Skill/flower2.tga", "Skill/flower3.tga" };

        private const string SoundBow = "Sound/eBow.wav";                               // SOUND_BOW01
        private const string SoundIceArrow = "Sound/eIceArrow.wav";                     // SOUND_ICEARROW
        private const string SoundFlash = "Sound/sAquaFlash.wav";                       // SOUND_FLASH
        private const string SoundDeepImpact = "Sound/BattleCastle/sCFireArrow.wav";    // SOUND_BCS_DEEP_IMPACT
        private const string SoundMeteorite = "Sound/eMeteorite.wav";                   // SOUND_METEORITE01

        private static readonly float[] TripleSpread = { 0f, 15f, -15f };
        private static readonly float[] QuadSpread = { 5f, 15f, -5f, -15f };
        private static readonly float[] MultiShotYaw = { 7f, 21f, 35f, -7f, -21f, -35f };

        private enum ProjectileStyle : byte
        {
            Basic, Steel, Saw, Laser, Thunder, V, Nature, Holy, Lace, Wing, Bomb, Double, BestCrossbow, Drill,
            Spark, Ring, DarkStinger, Gamble, Impact
        }

        private enum JointKind : byte { Energy, Trail, Spiral, Holy, FlashTrail, FlashStrike }

        // Trail draw passes (texture, blend).
        private const int PassFlareAdd = 0, PassFlareOther = 1, PassEnergy = 2, PassHoly = 3, PassBlue = 4, PassForce = 5, PassCount = 6;

        private enum ParticleKind : byte
        {
            Fire, Flare, Flower, Bubble, Smoke0, Smoke13, Smoke24, Smoke34, Smoke35, Spark10, Spark30, Shiny, KindCount
        }

        private struct Arrow
        {
            public bool Live;
            public bool Piercing;      // o->Kind = 1
            public bool BombDone;
            public bool PierceLive;
            public bool PierceVisible;
            public int SubType;
            public int LastEvent;
            public int LastGeneration;
            public Vector3 Position, Angle, Direction, StartPosition, EyeLeft, Light;
            public Vector3 Bx, By, Bz;  // AngleMatrix(Angle) columns for particles spawned this tick
            public Vector3 PiercePosition, PierceStart, PierceHeadAngle;
            public float Scale, LifeTime, Gravity, BlendMeshLight, Frame, PierceGravity, PierceLife;
        }

        private struct Joint
        {
            public bool Live;
            public bool CreateTails;
            public bool ReverseV;
            public JointKind Kind;
            public byte Pass;
            public int Arrow, SubType, Skill, Index, Generation;
            public SourceTrail Trail;
            public Vector3 Position, Angle, HeadAngle, Light, Direction, TargetPosition;
            public float Scale, LifeTime, Velocity, Seed;
        }

        private struct Particle
        {
            public bool Live;
            public ParticleKind Kind;
            public byte Texture;
            public int Frame;
            public Vector3 Position, Velocity, StartPosition, Light, Bx, By, Bz;
            public float Scale, LifeTime, Rotation, Gravity, Alpha;
        }

        private struct Wave
        {
            public bool Live;
            public int SubType;
            public Vector3 Position, Angle, Light;
            public float Scale, Gravity, BlendMeshLight, LifeTime, Frame;
        }

        private struct Feather
        {
            public bool Live;
            public int SubType;
            public Vector3 Position, Direction, Angle, Spin, Light;
            public float Scale, Alpha, LifeTime, Frame;
        }

        // Trails are rented: a volley at a high frame rate needs MaxTails / F tails per joint.
        private static readonly Stack<SourceTrail> TrailPool = new();

        private readonly PlayerObject _shooter;
        private readonly WalkableWorldControl _walkableWorld;
        private readonly ushort _targetId;
        private readonly ArrowVolleyKind _volleyKind;
        private readonly ProjectileStyle _style;
        private readonly Arrow[] _arrows = new Arrow[MaxArrows];
        private readonly Joint[] _joints = new Joint[MaxJoints];
        private readonly int[] _passCounts = new int[PassCount];
        private readonly int[] _kindCounts = new int[(int)ParticleKind.KindCount];
        private readonly SourceBombPool? _bombs;
        private readonly SourceMultiShotPool? _rings;
        private readonly SourceTerrainLights? _terrainLights;
        private readonly SourceSparkPool? _sparks;
        private readonly Vector3 _terrainLightColor;
        private Particle[]? _particles;
        private int _particleCapacity;
        private Wave[]? _waves;
        private Feather[]? _feathers;

        private SourceModelRenderer? _model;
        private SourceModelRenderer? _model2;       // DARKSTINGER mesh 1
        private SourceModelRenderer? _piercing;
        private SourceModelRenderer? _wavesModel;
        private SourceModelRenderer? _featherModel;
        private int _modelKeys = 1;
        private Texture2D? _sparkJointTex;
        private Texture2D? _flareTrailTex, _energyTex, _flareTex, _flareBlueTex, _flareForceTex, _lightTex, _lightningTex,
            _shinyTex, _shiny1Tex, _shiny2Tex, _spark1Tex, _fireTex, _smokeTex, _bubbleTex, _sparkTex, _explosionTex;
        private readonly Texture2D?[] _flowerTex = new Texture2D?[3];

        private int _liveArrows;
        private int _liveJoints;
        private int _liveParticles;
        private int _liveWaves;
        private int _liveFeathers;
        private float _bladeLife;
        private int _bladeLastTick = -1;
        private int _bladeLastYaw = -1;
        private Vector3 _bladePosition;
        private Vector3 _casterAngle;
        private bool _meteoriteSound;

        public ArrowProjectileEffect(
            PlayerObject shooter,
            WalkableWorldControl world,
            ushort targetId,
            Vector3? targetPosition,
            ArrowVolleyKind volleyKind)
            : base(shooter, volleyKind == ArrowVolleyKind.MultiShot ? 0f : GateFrame, GateTimeoutSeconds, 256)
        {
            _ = targetPosition;
            _shooter = shooter;
            _walkableWorld = world ?? throw new ArgumentNullException(nameof(world));
            _targetId = (ushort)(targetId & 0x7FFF);
            _volleyKind = volleyKind;
            _style = volleyKind == ArrowVolleyKind.DeepImpact ? ProjectileStyle.Impact : ResolveEquippedProjectileStyle(shooter);

            if (_style is ProjectileStyle.Bomb or ProjectileStyle.Drill or ProjectileStyle.Holy)
                _bombs = new SourceBombPool(MaxArrows);
            if (volleyKind == ArrowVolleyKind.MultiShot)
                _rings = new SourceMultiShotPool(1);
            if (_style == ProjectileStyle.Drill)
                _sparks = new SourceSparkPool(32, 192); // 15 per volley + the bone particle of each arrow

            // AddTerrainLight(o->Position, Luminosity * colour, 2, PrimaryTerrainLight) of the move cases.
            _terrainLightColor = _style switch
            {
                ProjectileStyle.BestCrossbow or ProjectileStyle.Drill => new Vector3(1f, 0.4f, 0.2f),
                ProjectileStyle.Double or ProjectileStyle.Holy => new Vector3(0.2f, 0.4f, 1f),
                ProjectileStyle.Basic => new Vector3(0.8f, 0.5f, 0.2f),
                ProjectileStyle.Nature or ProjectileStyle.Wing or ProjectileStyle.Bomb => new Vector3(0.6f, 0.8f, 0.8f),
                ProjectileStyle.Lace or ProjectileStyle.Ring => new Vector3(0.6f, 0.2f, 0.8f),
                ProjectileStyle.Gamble => new Vector3(0.2f, 0.8f, 0.5f),
                _ => Vector3.Zero
            };
            if (_terrainLightColor != Vector3.Zero)
                _terrainLights = new SourceTerrainLights(this, MaxArrows);

            // Upper bounds: spawns per tick x LifeTime x arrows of one volley.
            _particleCapacity = _style switch
            {
                ProjectileStyle.Basic => 24 * MaxArrows,
                ProjectileStyle.Nature => 100 * 6,
                ProjectileStyle.Wing => 128 * 5,
                ProjectileStyle.Bomb => 128 * 5,
                ProjectileStyle.Lace => 64 * 6,
                ProjectileStyle.Spark => 96 * 6,
                ProjectileStyle.Ring => 60 * 6,
                ProjectileStyle.Gamble => 96 * 6,
                ProjectileStyle.Drill => 60 * 6,
                _ => 0
            };
        }

        protected override async Task LoadEffectContent()
        {
            switch (_style)
            {
                case ProjectileStyle.Basic: _model = await LoadArrowModel("Skill/Arrow01.bmd", 1); break;
                case ProjectileStyle.Steel: _model = await LoadArrowModel("Skill/ArrowSteel01.bmd", -1); break;
                case ProjectileStyle.Thunder: _model = await LoadArrowModel("Skill/ArrowThunder01.bmd", 0); break;
                case ProjectileStyle.Laser: _model = await LoadArrowModel("Skill/ArrowLaser01.bmd", 0); break;
                case ProjectileStyle.V: _model = await LoadArrowModel("Skill/ArrowV01.bmd", 0); break;
                case ProjectileStyle.Saw: _model = await LoadArrowModel("Skill/ArrowSaw01.bmd", -1); break;
                case ProjectileStyle.Nature: _model = await LoadArrowModel("Skill/ArrowNature01.bmd", -2); break;
                case ProjectileStyle.Wing: _model = await LoadArrowModel("Skill/ArrowWing01.bmd", 0); break;
                case ProjectileStyle.Bomb: _model = await LoadArrowModel("Skill/ArrowBomb01.bmd", 0); break;
                case ProjectileStyle.Lace: _model = await LoadArrowModel("Skill/LaceArrow.bmd", 1); break;
                case ProjectileStyle.Spark: _model = await LoadArrowModel("Skill/Arrow_Spark.bmd", -2); break; // RENDER_BRIGHT
                case ProjectileStyle.Ring: _model = await LoadArrowModel("Skill/CW_Bow_Skill.bmd", 0); break;
                case ProjectileStyle.DarkStinger:
                    _model = await LoadArrowModel("Skill/sketbows_arrows.bmd", -2, 1);
                    _model2 = await LoadArrowModel("Skill/sketbows_arrows.bmd", -2, 2);
                    break;
                case ProjectileStyle.Gamble: _model = await LoadArrowModel("Skill/gamble_arrows01.bmd", -2, 1); break;
                case ProjectileStyle.BestCrossbow: _model = await LoadArrowModel("Skill/kcross.bmd", -2); break;
                case ProjectileStyle.Double: _model = await LoadArrowModel("Skill/ArrowDouble01.bmd", -2); break;
                case ProjectileStyle.Drill: _model = await LoadArrowModel("Skill/Carow.bmd", -2); break;
                case ProjectileStyle.Impact: _model = await LoadArrowModel("Skill/ArrowImpact.bmd", -2); break;
            }

            _modelKeys = _model?.Model?.Actions is { Length: > 0 } actions ? Math.Max(1, actions[0].NumAnimationKeys) : 1;

            bool piercing = _volleyKind == ArrowVolleyKind.Penetration;
            if (piercing)
            {
                _piercing = await LoadArrowModel(PiercingModel, 0);
                _flareTrailTex = await LoadTexture(FlareTrailTexture);
            }

            switch (_style)
            {
                case ProjectileStyle.Basic:
                    _fireTex = await LoadTexture(FireTexture);
                    break;
                case ProjectileStyle.Nature:
                    _lightningTex = await LoadTexture(LightningTexture);
                    for (int i = 0; i < 3; i++)
                        _flowerTex[i] = await LoadTexture(FlowerTextures[i]);
                    break;
                case ProjectileStyle.Wing:
                    _lightningTex = await LoadTexture(LightningTexture);
                    _bubbleTex = await LoadOptionalTexture(BubbleTexture);
                    _smokeTex = await LoadTexture(SmokeTexture);
                    break;
                case ProjectileStyle.Bomb:
                    _lightTex = await LoadTexture(LightTexture);
                    _bubbleTex = await LoadOptionalTexture(BubbleTexture);
                    break;
                case ProjectileStyle.Lace:
                    _flareTrailTex = await LoadTexture(FlareTrailTexture);
                    _flareTex = await LoadTexture(FlareTexture);
                    _wavesModel = await LoadArrowModel(WavesModel, -2);
                    break;
                case ProjectileStyle.Spark:
                    _flareTrailTex = await LoadTexture(FlareTrailTexture);
                    _spark1Tex = await LoadTexture(SourceSparkPool.Spark1TexturePath);
                    _smokeTex = await LoadTexture(SmokeTexture);
                    break;
                case ProjectileStyle.Ring:
                    _flareTrailTex = await LoadTexture(FlareTrailTexture);
                    _shinyTex = await LoadTexture(ShinyTexture);
                    _wavesModel = await LoadArrowModel(WavesModel, -2);
                    break;
                case ProjectileStyle.DarkStinger:
                    _flareTrailTex = await LoadTexture(FlareTrailTexture);
                    _lightTex = await LoadTexture(LightTexture);
                    _featherModel = await LoadArrowModel(FeatherModel, -2, 1);
                    break;
                case ProjectileStyle.Gamble:
                    _spark1Tex = await LoadTexture(SourceSparkPool.Spark1TexturePath);
                    _smokeTex = await LoadTexture(SmokeTexture);
                    break;
                case ProjectileStyle.BestCrossbow:
                case ProjectileStyle.Double:
                    _energyTex = await LoadTexture(EnergyTexture);
                    break;
                case ProjectileStyle.Drill:
                    _energyTex = await LoadTexture(EnergyTexture);
                    _smokeTex = await LoadTexture(SmokeTexture);
                    _shiny1Tex = await LoadTexture(Shiny1Texture);
                    _lightTex = await LoadTexture(LightTexture);
                    break;
                case ProjectileStyle.Holy:
                    _flareTex = await LoadTexture(FlareTexture);
                    _shiny1Tex = await LoadTexture(Shiny1Texture);
                    _lightTex = await LoadTexture(LightTexture);
                    if (piercing)
                        _piercing ??= await LoadArrowModel(PiercingModel, 0);
                    break;
                case ProjectileStyle.Impact:
                    _flareBlueTex = await LoadTexture(FlareBlueTexture);
                    _flareForceTex = await LoadTexture(FlareForceTexture);
                    _shiny2Tex = await LoadTexture(Shiny2Texture);
                    break;
            }

            if (_sparks != null)
                _sparkJointTex = await LoadTexture(SourceSparkPool.JointTexturePath);

            if (_bombs != null || _sparks != null)
            {
                _sparkTex = await LoadTexture(SourceSparkPool.ParticleTexturePath);
                _explosionTex = await LoadTexture(SourceBombPool.ExplosionTexturePath);
            }

            if (_rings != null)
            {
                await _rings.LoadAsync(World);
                _shiny1Tex ??= await LoadTexture(Shiny1Texture);
            }
        }

        private async Task<SourceModelRenderer?> LoadArrowModel(string path, int blendMesh, int meshMask = -1)
        {
            if (!await Content.BMDLoader.Instance.AssestExist(path))
                return null;

            var renderer = new SourceModelRenderer(path, blendMesh < -1 ? -2 : blendMesh) { AnimationSpeed = 0f, VisibleMeshMask = meshMask };
            if (_style == ProjectileStyle.Spark && path.Contains("Arrow_Spark", StringComparison.OrdinalIgnoreCase))
            {
                // ZzzObject.cpp:1539: RenderBody(RENDER_TEXTURE | RENDER_CHROME5 | RENDER_BRIGHT)
                renderer.ChromeBrightPass = true;
                renderer.ChromePass5 = true;
            }
            if (blendMesh >= -1)
                renderer.UseNormalBlend();
            return await renderer.LoadInto(World) ? renderer : null;
        }

        private static async Task<Texture2D?> LoadOptionalTexture(string path)
        {
            try
            {
                return await Content.TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                return null; // map-specific texture (BITMAP_BUBBLE)
            }
        }

        // ───────────────────────────── start ─────────────────────────────

        protected override void OnStart()
        {
            _casterAngle = new Vector3(0f, 0f, CasterYawDegrees);
            if (_particleCapacity > 0)
            {
                _particles = ArrayPool<Particle>.Shared.Rent(_particleCapacity);
                Array.Clear(_particles, 0, _particles.Length);
            }
            if (_style is ProjectileStyle.Lace or ProjectileStyle.Ring)
                _waves = new Wave[MaxWaves];
            if (_style == ProjectileStyle.DarkStinger)
                _feathers = new Feather[MaxFeathers];

            bool hasTarget = ResolveTarget() != null;
            switch (_volleyKind)
            {
                case ArrowVolleyKind.MultiShot:
                    // WSclient.cpp:5063: ring set + MODEL_BLADE_SKILL sub 1 (LifeTime 14) at rot(0, -120, 145).
                    _rings?.SpawnSet(CasterPosition, _casterAngle, new Vector3(0.8f, 0.9f, 1.6f));
                    _bladePosition = CasterPosition + SourceJointMath.Rotate(new Vector3(0f, -120f, 145f), _casterAngle);
                    _bladeLife = 14f;
                    break;

                case ArrowVolleyKind.TripleShot:
                {
                    // CreateArrows, Skill == 1: 4 arrows for the listed bows, 3 otherwise.
                    float[] spread = UsesFourProjectileMultiShot(_style) ? QuadSpread : TripleSpread;
                    for (int i = 0; i < spread.Length; i++)
                        CreateArrow(spread[i], 0);
                    break;
                }

                case ArrowVolleyKind.Penetration:
                    // With a target the release switch calls CreateArrows twice (ZzzCharacter.cpp:4702, 4755).
                    for (int i = 0; i < (hasTarget ? 2 : 1); i++)
                    {
                        CreateArrow(0f, 2);
                        SoundController.Instance.PlayBuffer(SoundBow);
                    }
                    break;

                case ArrowVolleyKind.IceArrow:
                    for (int i = 0; i < (hasTarget ? 2 : 1); i++)
                    {
                        CreateArrow(0f, 0);
                        PlayUnmappedSound(51, SoundIceArrow);
                    }
                    break;

                case ArrowVolleyKind.DeepImpact:
                    for (int i = 0; i < (hasTarget ? 2 : 1); i++)
                        CreateArrow(0f, 0);
                    SoundController.Instance.PlayBuffer(SoundDeepImpact);
                    break;

                default:
                    CreateArrow(0f, 0);
                    break;
            }

            // CreateArrows tail (ZzzEffectMagicSkill.cpp:296): Great Reign Crossbow sparks at Weapon[0] bone 33 (0, 10, -130).
            if (_sparks != null && _volleyKind is ArrowVolleyKind.Normal or ArrowVolleyKind.TripleShot &&
                _shooter.TryGetBoneWorldMatrix(33, out Matrix hand))
            {
                Vector3 sparkPosition = Vector3.Transform(new Vector3(0f, 10f, -130f), hand);
                for (int i = 0; i < 15; i++)
                {
                    var angle = new Vector3(MuGame.Random.Next(360), 0f, 0f);
                    if (FpsCheck(2, SourceJointMath.FrameFactor))
                        _sparks.SpawnJoint(sparkPosition, angle, 3);
                    _sparks.SpawnSpark(sparkPosition, angle);
                }
            }
        }

        private WalkerObject? ResolveTarget()
        {
            if (_targetId == 0)
                return null;
            return _walkableWorld.WalkerObjectsById.TryGetValue(_targetId, out var walker) &&
                   walker.Status != GameControlStatus.Disposed ? walker : null;
        }

        /// <summary>CreateArrow + the CreateEffect init case of the arrow type.</summary>
        private void CreateArrow(float yawOffset, int subType)
        {
            int slot = -1;
            for (int i = 0; i < MaxArrows; i++)
            {
                if (!_arrows[i].Live && !_arrows[i].PierceLive) { slot = i; break; }
            }
            if (slot < 0)
                return;

            Vector3 angle = new(0f, 0f, CasterYawDegrees + yawOffset);
            Vector3 position = CasterPosition;
            if (IsFenrirArrowAction(_shooter.CurrentAction))
                position.Z += 30f;

            ref Arrow a = ref _arrows[slot];
            a = new Arrow
            {
                Live = true,
                SubType = subType,
                Angle = angle,
                Light = Vector3.One,
                Scale = 0.8f,
                LifeTime = 30f,
                BlendMeshLight = 1f,
                LastEvent = -1,
                LastGeneration = ++_generation,
            };

            switch (_style)
            {
                case ProjectileStyle.BestCrossbow:
                case ProjectileStyle.Double:
                case ProjectileStyle.Drill:
                    a.Scale = 1f;
                    if (_style == ProjectileStyle.Drill)
                        a.Gravity = -10f;
                    position.Z += 130f;
                    a.Direction = new Vector3(0f, -70f, 0f);
                    a.Position = position;
                    a.EyeLeft = position;
                    SpawnJoint(JointKind.Energy, 5, slot, position, angle, 100f, float.MaxValue, 10, Vector3.One, PassEnergy);
                    if (subType != 0)
                        CreatePiercing(ref a, slot, 0);
                    break;

                case ProjectileStyle.Holy:
                    a.Scale = 1f;
                    position.Z += 130f;
                    a.Direction = new Vector3(0f, -60f, 0f);
                    a.Position = position;
                    a.StartPosition = position + SourceJointMath.Rotate(new Vector3(-10f, -100f, 15f), angle);
                    for (int i = 0; i < 4; i++)
                    {
                        int joint = SpawnJoint(JointKind.Holy, 11, slot, a.StartPosition, angle, 30f, 30f, 15,
                            new Vector3(0.2f, 0.2f, 1f), PassHoly);
                        if (joint >= 0)
                        {
                            _joints[joint].Skill = i == 1 ? 1 : 0;
                            _joints[joint].Seed = MuGame.Random.Next(3000);
                            // TargetPosition is never assigned for a joint with a Target: the creation point is used.
                            _joints[joint].TargetPosition = a.StartPosition;
                        }
                    }
                    break;

                case ProjectileStyle.Bomb:
                    a.Position = position + SourceJointMath.Rotate(new Vector3(-10f, -60f, 135f), angle);
                    a.Scale = 1f;
                    a.Direction = new Vector3(0f, -30f, 0f);
                    a.Gravity = -10f;
                    a.LifeTime = 40f;
                    a.Piercing = subType == 2;
                    break;

                case ProjectileStyle.Impact:
                    a.LifeTime = 20f;
                    a.Scale = 1.8f;
                    a.Direction = new Vector3(0f, -30f, 0f);
                    a.Light = new Vector3(0.3f, 0.8f, 1f);
                    a.Position = position + SourceJointMath.Rotate(new Vector3(-10f, -80f, 200f), angle);
                    a.Angle.X = -30f;
                    SpawnJoint(JointKind.FlashTrail, 4, slot, a.Position, a.Angle, 40f, 30f, 20, Vector3.One, PassBlue);
                    break;

                default:
                    a.Position = position + SourceJointMath.Rotate(new Vector3(-10f, -60f, 135f), angle);
                    a.Direction = new Vector3(0f, -70f, 0f);
                    switch (_style)
                    {
                        case ProjectileStyle.Wing:
                            a.Scale = 1.8f;
                            a.Direction.Y = -50f;
                            a.Gravity = -10f;
                            break;
                        case ProjectileStyle.Spark:
                            a.Scale = 1f;
                            SpawnJoint(JointKind.Trail, 14, slot, a.Position, a.Angle, 50f, 40f, 16, Vector3.One, PassFlareAdd);
                            break;
                    }

                    if (subType == 2)
                        CreatePiercing(ref a, slot, 0);

                    if (_style == ProjectileStyle.Lace)
                    {
                        a.Angle.Y = MuGame.Random.Next(360);
                        SpawnJoint(JointKind.Trail, 12, slot, a.Position, a.Angle, 40f, 80f, 16, new Vector3(0.6f, 0.2f, 0.8f), PassFlareOther);
                    }
                    else if (_style == ProjectileStyle.Ring)
                    {
                        a.Scale = 1f;
                        a.Angle.Y = MuGame.Random.Next(360);
                        SpawnJoint(JointKind.Trail, 15, slot, a.Position, a.Angle, 50f, 40f, 16, new Vector3(0.4f, 0.9f, 0.5f), PassFlareAdd);
                    }
                    break;
            }

            UpdateBasis(ref a);
            _liveArrows++;
        }

        private static void UpdateBasis(ref Arrow a)
        {
            a.Bx = SourceJointMath.Rotate(Vector3.UnitX, a.Angle);
            a.By = SourceJointMath.Rotate(Vector3.UnitY, a.Angle);
            a.Bz = SourceJointMath.Rotate(Vector3.UnitZ, a.Angle);
        }

        /// <summary>CreateEffect(MODEL_PIERCING, sub, owner = arrow): four spiral joints + SOUND_FLASH.</summary>
        private void CreatePiercing(ref Arrow a, int slot, int subType)
        {
            a.Piercing = true;
            a.PierceLive = true;
            a.PierceVisible = subType != 3;
            a.PierceLife = 100f;
            a.PierceGravity = 0f;
            a.PiercePosition = a.Position;
            a.PierceStart = a.Position;
            a.PierceHeadAngle = a.Angle;

            float paramScale = subType == 3 ? 10f : 12f;
            Vector3 light = subType == 3 ? new Vector3(0.9f, 0.4f, 0.6f) : new Vector3(0.5f);
            for (int i = 0; i < 4; i++)
            {
                int joint = SpawnJoint(JointKind.Spiral, i, slot, a.Position, a.Angle, 20f, 30f, 50, light, PassFlareAdd);
                if (joint < 0)
                    continue;
                _joints[joint].Skill = subType;
                _joints[joint].Velocity = i switch { 0 => 0f, 1 => 90f, 2 => 180f, _ => 240f };
                _joints[joint].Direction = new Vector3(paramScale * 1.5f, 0f, 0f);
            }

            SoundController.Instance.PlayBuffer(SoundFlash);
        }

        private int SpawnJoint(JointKind kind, int subType, int arrow, Vector3 position, Vector3 angle, float scale,
            float lifeTime, int maxTails, Vector3 light, byte pass)
        {
            for (int i = 0; i < MaxJoints; i++)
            {
                ref Joint j = ref _joints[i];
                if (j.Live)
                    continue;

                int tails = SourceJointMath.ScaledMaxTails(maxTails);
                SourceTrail trail = RentTrail(tails);
                trail.Push(position, angle, scale); // CreateJoint start tail
                j = new Joint
                {
                    Live = true,
                    CreateTails = true,
                    Kind = kind,
                    Pass = pass,
                    Arrow = arrow,
                    SubType = subType,
                    Index = MuGame.Random.Next(1, 500), // pool slot of the original joint
                    Generation = _arrows[arrow].LastGeneration,
                    Trail = trail,
                    Position = position,
                    Angle = angle,
                    HeadAngle = angle,
                    Light = light,
                    Scale = scale,
                    LifeTime = lifeTime,
                    Velocity = kind == JointKind.Trail ? 70f : 0f,
                };
                _liveJoints++;
                _passCounts[pass]++;
                return i;
            }

            return -1;
        }

        private static SourceTrail RentTrail(int maxTails)
        {
            if (TrailPool.Count > 0)
            {
                SourceTrail trail = TrailPool.Pop();
                trail.Reset(200);
                if (trail.MaxTails >= maxTails)
                {
                    trail.Reset(maxTails);
                    return trail;
                }
            }

            var created = new SourceTrail(200); // MAX_TAILS
            created.Reset(maxTails);
            return created;
        }

        private void KillJoint(ref Joint j)
        {
            if (!j.Live)
                return;
            j.Live = false;
            _liveJoints--;
            _passCounts[j.Pass]--;
            if (TrailPool.Count < 64)
                TrailPool.Push(j.Trail);
            j.Trail = null!;
        }

        // ───────────────────────────── tick ─────────────────────────────

        protected override void OnTick(float factor)
        {
            TickBlade(factor);

            for (int i = 0; i < MaxArrows; i++)
            {
                if (_arrows[i].Live)
                    TickArrow(ref _arrows[i], i, factor);
            }

            // MODEL_PIERCING (MoveEffect 11662), created after its arrow and therefore moved after it.
            for (int i = 0; i < MaxArrows; i++)
            {
                ref Arrow a = ref _arrows[i];
                if (!a.PierceLive)
                    continue;
                if (!a.Live)
                {
                    a.PierceLive = false;
                    continue;
                }

                a.PierceGravity += 90f * factor;
                a.PierceStart = a.PiercePosition;
                a.PiercePosition = a.Position;
                a.PierceHeadAngle = new Vector3(a.Angle.X, a.PierceGravity, a.Angle.Z);
                a.PierceLife -= factor;
                if (a.PierceLife <= 0f)
                    a.PierceLive = false;
            }

            if (_liveJoints > 0)
                TickJoints(factor);
            if (_liveParticles > 0)
                TickParticles(factor);
            if (_liveWaves > 0)
                TickWaves(factor);
            if (_liveFeathers > 0)
                TickFeathers(factor);

            if (_terrainLights != null)
            {
                var terrain = World?.Terrain;
                for (int i = 0; i < MaxArrows; i++)
                {
                    ref Arrow a = ref _arrows[i];
                    if (!a.Live)
                    {
                        _terrainLights.Clear(i);
                        continue;
                    }

                    float luminosity = (MuGame.Random.Next(4) + 7) * 0.1f;
                    _terrainLights.Set(i, terrain, _style == ProjectileStyle.Holy ? a.StartPosition : a.Position,
                        _terrainLightColor * luminosity, 2);
                }
            }

            _pierceFrame += 0.3f * factor;
            _sparks?.Move(factor, World?.Terrain);
            _bombs?.Move(factor, World?.Terrain);
            _rings?.Move(factor);
        }

        private float _pierceFrame;
        private int _generation;

        /// <summary>MODEL_BLADE_SKILL sub 1 (ZzzEffect.cpp:7069): one arrow on every even tick while LifeTime >= 3.</summary>
        private void TickBlade(float factor)
        {
            if (_bladeLife <= 0f)
                return;

            int tick = (int)_bladeLife;
            if (_bladeLife >= 3f && tick % 2 == 0 && tick != _bladeLastTick)
            {
                _bladeLastTick = tick;
                int yaw;
                do
                {
                    yaw = MuGame.Random.Next(6);
                }
                while (yaw == _bladeLastYaw);
                _bladeLastYaw = yaw;
                CreateArrow(MultiShotYaw[yaw], 0);
            }

            _bladeLife -= factor;
        }

        private void KillArrow(ref Arrow a)
        {
            if (!a.Live)
                return;
            a.Live = false;
            _liveArrows--;
        }

        private void TickArrow(ref Arrow a, int slot, float factor)
        {
            float luminosity = (MuGame.Random.Next(4) + 7) * 0.1f;
            float velocity = 1f; // PlayAnimation speed (o->Velocity)

            switch (_style)
            {
                case ProjectileStyle.BestCrossbow:
                    velocity = 0.3f;
                    a.BlendMeshLight = a.LifeTime > 25f ? (30f - a.LifeTime) / 40f : 1f;
                    a.Angle.Y += (MuGame.Random.Next(60) + 30f) * factor;
                    a.EyeLeft = a.Position + SourceJointMath.Rotate(a.Direction, a.Angle);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Double:
                    velocity = 0.3f;
                    a.Angle.Y += 30f * factor;
                    a.EyeLeft = a.Position;
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Holy:
                    velocity = 0.3f;
                    a.Angle.Y += 30f * factor;
                    if (a.SubType != 0)
                    {
                        int life = (int)a.LifeTime;
                        if (life == 13 && a.LastEvent != 13)
                        {
                            a.LastEvent = 13;
                            CreatePiercing(ref a, slot, 0);
                        }
                        else if (life == 30)
                        {
                            a.Piercing = true;
                        }
                    }
                    break;

                case ProjectileStyle.Basic:
                    if (FpsCheck(1, factor))
                        SpawnParticle(ParticleKind.Fire, a.Position, new Vector3(luminosity * 0.8f, luminosity * 0.5f, luminosity * 0.2f), 1f, ref a);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Nature:
                    for (int j = 0; j < 4; j++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnParticle(ParticleKind.Flower, a.Position + RandomOffset(32, 64, 32), a.Light, 1f, ref a);
                    }
                    a.Angle.Y += 30f * factor;
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Lace:
                    a.Angle.Y += 60f * factor;
                    if ((int)a.LifeTime % 2 == 0)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            if (FpsCheck(1, factor))
                                SpawnParticle(ParticleKind.Flare, a.Position + RandomOffset(32, 64, 32), new Vector3(0.4f, 0.2f, 1f), 0.2f, ref a);
                        }
                    }
                    a.EyeLeft = a.Position;
                    if (FpsCheck(1, factor))
                        SpawnWave(a.Position, a.Angle.Z, a.Light, 3);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Wing:
                    for (int j = 0; j < 4; j++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnParticle(ParticleKind.Bubble, a.Position + RandomOffset(16, 16, 16), a.Light, 1f, ref a);
                    }
                    if (FpsCheck(1, factor))
                        SpawnParticle(ParticleKind.Smoke0, a.Position, a.Light, 1f, ref a);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Spark:
                    a.Angle.Y += 35f * factor;
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Ring:
                    a.EyeLeft = a.Position;
                    a.Light = new Vector3(0f, 1f, 0.1f);
                    if (FpsCheck(1, factor))
                        SpawnWave(a.Position, a.Angle.Z, a.Light, 4);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Bomb:
                    for (int j = 0; j < 4; j++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnParticle(ParticleKind.Bubble, a.Position + RandomOffset(16, 16, 16), a.Light, 1f, ref a);
                    }

                    // MoveJump
                    a.Position.Z += a.Gravity * factor;
                    a.Gravity -= factor;
                    float ground = TerrainHeight(a.Position);
                    if (a.Position.Z < ground)
                    {
                        a.Position.Z = ground;
                        a.Gravity = -a.Gravity * 0.4f;
                        a.Direction.Y *= 0.4f;
                    }

                    if ((int)a.LifeTime == 1 && !a.BombDone)
                    {
                        a.BombDone = true;
                        _bombs?.Spawn(a.Position);
                    }
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.DarkStinger:
                    CheckClientArrow(ref a);
                    if (a.Live)
                        TickDarkStinger(ref a, slot, factor);
                    break;

                case ProjectileStyle.Gamble:
                    a.Angle.Y += 60f * factor;
                    for (int j = 0; j < 2; j++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnParticle(ParticleKind.Spark30, a.Position + RandomOffset(32, 64, 32), new Vector3(0.2f, 0.8f, 0.5f), 1f, ref a);
                    }
                    a.EyeLeft = a.Position;
                    if (FpsCheck(1, factor))
                        SpawnParticle(ParticleKind.Smoke24, a.Position, new Vector3(0.2f, 0.8f, 0.5f), 1f, ref a);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Drill:
                    velocity = 0.3f;
                    a.Angle.Y += 30f * factor;
                    for (int j = 0; j < 3; j++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnParticle(ParticleKind.Smoke13, a.Position, Vector3.One, 1f, ref a);
                    }
                    if ((int)a.LifeTime == 1 && !a.BombDone)
                    {
                        a.BombDone = true;
                        _bombs?.Spawn(a.Position);
                    }
                    a.EyeLeft = a.Position + SourceJointMath.Rotate(a.Direction, a.Angle);
                    // ZzzObject.cpp:2631: CreateParticleFpsChecked(BITMAP_SPARK, bone 1, ...) per render
                    if (FpsCheck(1, factor))
                        _sparks?.SpawnSpark(ArrowBone(ref a, 1), a.Angle);
                    CheckClientArrow(ref a);
                    break;

                case ProjectileStyle.Impact:
                    a.Angle.X -= 5f * factor;
                    a.Direction.Y -= 8f * factor;
                    if (a.SubType == 1)
                    {
                        KillArrow(ref a);
                    }
                    else if (a.LifeTime < 2f && a.SubType == 0)
                    {
                        WalkerObject? target = ResolveTarget();
                        if (target != null)
                        {
                            a.Angle.X = 90f;
                            a.SubType = 1;
                            Vector3 strike = target.WorldPosition.Translation + new Vector3(
                                MuGame.Random.Next(100) - 50f, MuGame.Random.Next(100) - 50f, 1200f);
                            SpawnFlashStrike(strike, 2, slot);
                            SpawnFlashStrike(strike, 3, slot);
                        }
                    }
                    break;

                default: // STEEL, THUNDER, LASER, V, SAW
                    CheckClientArrow(ref a);
                    break;
            }

            if (!a.Live)
                return;

            // MoveEffect epilogue.
            a.Frame += velocity * factor;
            if (_style == ProjectileStyle.Holy)
                a.StartPosition += SourceJointMath.Rotate(a.Direction, a.Angle) * factor;
            else
                a.Position += SourceJointMath.Rotate(a.Direction, a.Angle) * factor;

            UpdateBasis(ref a);
            a.LifeTime -= factor;
            if (a.LifeTime <= 0f)
                KillArrow(ref a);
        }

        private void TickDarkStinger(ref Arrow a, int slot, float factor)
        {
            int life = (int)a.LifeTime;
            if (life is 30 or 28 or 26 or 24)
            {
                Vector3 bone = ArrowBone(ref a, 1);
                int count = MuGame.Random.Next(3);
                for (int i = 0; i < count; i++)
                {
                    if (FpsCheck(1, factor)) SpawnFeather(bone, 0);
                    if (FpsCheck(1, factor)) SpawnFeather(bone, 1);
                }
            }

            if (life == 30 && a.LastEvent != 30)
            {
                a.LastEvent = 30;
                Vector3 bone = ArrowBone(ref a, 1);
                SpawnJoint(JointKind.Trail, 18, slot, bone, a.Angle, 90f, 40f, 7, new Vector3(0.4f, 0.4f, 0.9f), PassFlareAdd);
            }
        }

        private Vector3 ArrowBone(ref Arrow a, int bone)
        {
            if (_model != null && _model.TryGetInstanceBoneAt(WrapFrame(a.Frame), bone, a.Position, ToRadians(a.Angle), a.Scale, out Vector3 world))
                return world;
            return a.Position;
        }

        private double WrapFrame(float frame) => _modelKeys > 1 ? frame % _modelKeys : 0d;

        private static Vector3 RandomOffset(int x, int y, int z) => new(
            MuGame.Random.Next(x) - x / 2, MuGame.Random.Next(y) - y / 2, MuGame.Random.Next(z) - z / 2);

        private static Vector3 ToRadians(Vector3 degrees) => new(
            MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.Z));

        /// <summary>CheckClientArrow for Kind == 0 arrows: stopped by any other character within 100 units.</summary>
        private void CheckClientArrow(ref Arrow a)
        {
            if (a.Piercing || !CharacterInRange(a.Position))
                return;

            KillArrow(ref a);
            if (_style == ProjectileStyle.Bomb || (_style == ProjectileStyle.Holy && a.SubType != 1))
                _bombs?.Spawn(a.Position);
        }

        /// <summary>CheckCharacterRange(o, 100): live characters except the owner and the hero.</summary>
        private bool CharacterInRange(Vector3 position)
        {
            WalkerObject? hero = _walkableWorld.Walker;
            foreach (var pair in _walkableWorld.WalkerObjectsById)
            {
                WalkerObject walker = pair.Value;
                if (ReferenceEquals(walker, _shooter) || ReferenceEquals(walker, hero) || walker.Status != GameControlStatus.Ready)
                    continue;
                if (walker is MonsterObject { IsDead: true } || walker is PlayerObject { IsDead: true })
                    continue;

                Vector3 other = walker.WorldPosition.Translation;
                float dx = position.X - other.X;
                float dy = position.Y - other.Y;
                if (dx * dx + dy * dy <= 100f * 100f)
                    return true;
            }

            return false;
        }

        // ───────────────────────────── joints ─────────────────────────────

        private void SpawnFlashStrike(Vector3 position, int subType, int slot)
        {
            Vector3 down = new(90f, 0f, 0f);
            int joint = SpawnJoint(JointKind.FlashStrike, subType, slot, position, down, 50f, 40f, 10, Vector3.One,
                subType == 2 ? (byte)PassForce : (byte)PassBlue);
            if (joint < 0)
                return;
            _joints[joint].Velocity = 30f;
            _joints[joint].ReverseV = MuGame.Random.Next(2) == 1;
        }

        private void TickJoints(float factor)
        {
            for (int i = 0; i < MaxJoints; i++)
            {
                ref Joint j = ref _joints[i];
                if (!j.Live)
                    continue;

                ref Arrow a = ref _arrows[j.Arrow];
                bool ownerLive = a.Live && a.LastGeneration == j.Generation;

                switch (j.Kind)
                {
                    case JointKind.Energy:
                        if (!ownerLive) { KillJoint(ref j); continue; }
                        j.Position = a.EyeLeft;
                        break;

                    case JointKind.Trail:
                        j.Position += SourceJointMath.Rotate(new Vector3(0f, -j.Velocity, 0f), j.Angle) * factor; // prologue
                        if (!ownerLive) { KillJoint(ref j); continue; }
                        if (j.SubType == 14)
                        {
                            if (FpsCheck(1, factor)) SpawnParticle(ParticleKind.Smoke34, j.Position, j.Light, 1f, j.Angle);
                            if (FpsCheck(1, factor)) SpawnParticle(ParticleKind.Smoke35, j.Position, j.Light, 1f, j.Angle);
                            if (FpsCheck(1, factor)) SpawnParticle(ParticleKind.Spark10, j.Position, new Vector3(0.7f, 0.3f, 0.3f), 4f, j.Angle);
                            j.Light = new Vector3(1f, 1f, 0.2f);
                        }
                        else if (j.SubType == 15)
                        {
                            if (FpsCheck(1, factor)) SpawnParticle(ParticleKind.Shiny, j.Position, j.Light, 0.5f, j.Angle);
                            if (FpsCheck(1, factor)) SpawnParticle(ParticleKind.Shiny, j.Position, j.Light, 0.5f, j.Angle);
                        }
                        if (j.LifeTime < 15f)
                            j.Light *= MathF.Pow(1f / 1.2f, factor);
                        break;

                    case JointKind.Spiral:
                    {
                        if (!(ownerLive && a.PierceLive)) { KillJoint(ref j); continue; }
                        Vector3 step = (a.PiercePosition - a.PierceStart) / 3f;
                        Vector3 center = a.PierceStart;
                        Vector3 angle = a.PierceHeadAngle;
                        angle.Y += (j.Velocity - 90f) * factor;
                        for (int k = 0; k < 3; k++)
                        {
                            angle.Y += 30f * factor;
                            center += step;
                            j.Position = center + SourceJointMath.Rotate(j.Direction, angle);
                            if (j.Trail.SegmentCount < j.Trail.MaxTails - 1 || j.Skill != 0)
                                j.Trail.Push(j.Position, j.Angle, j.Scale);
                        }
                        if (j.LifeTime < 15f)
                            j.Light *= MathF.Pow(1f / 1.5f, factor);
                        break;
                    }

                    case JointKind.Holy:
                        if (!ownerLive) { KillJoint(ref j); continue; }
                        TickHolyJoint(ref j, ref a);
                        break;

                    case JointKind.FlashTrail:
                        if (!ownerLive) { KillJoint(ref j); continue; }
                        j.Position = a.Position;
                        if (j.LifeTime < 15f)
                            j.Light *= MathF.Pow(1f / 1.3f, factor);
                        break;

                    case JointKind.FlashStrike:
                        j.Position += SourceJointMath.Rotate(new Vector3(0f, -j.Velocity, 0f), j.Angle) * factor; // prologue
                        if (j.SubType == 2)
                            j.Angle.Z += (MuGame.Random.Next(10) + 10f) * factor;
                        j.Velocity += factor * 10f;
                        if (j.CreateTails)
                            j.Trail.Push(j.Position, j.Angle, j.Scale);
                        j.Velocity += factor * 10f;
                        j.Position += SourceJointMath.Rotate(new Vector3(0f, -j.Velocity * factor, 0f), j.HeadAngle) * factor;
                        float height = TerrainHeight(j.Position) - 300f; // MultiUse
                        if (j.Position.Z < height)
                        {
                            j.Position.Z = height;
                            j.CreateTails = false;
                            j.Light *= MathF.Pow(1f / 1.2f, factor);
                        }
                        break;
                }

                // MoveJoint epilogue.
                if (j.CreateTails)
                    j.Trail.Push(j.Position, j.Angle, j.Scale);
                j.LifeTime -= factor;
                if (j.LifeTime < 0f)
                    KillJoint(ref j);
            }
        }

        /// <summary>BITMAP_FLARE sub 11 (ZzzEffectJoint.cpp:5564): orbit closing in on the arrow's StartPosition.</summary>
        private void TickHolyJoint(ref Joint j, ref Arrow a)
        {
            int frame = (int)(FPSCounter.Instance.WorldTime / 40.0);
            frame = ((j.Index % 2) != 0 ? frame : -frame) + j.Index * 53731;

            const float s0 = 0.048f * 1.5f, s1 = 0.0613f * 1.5f, s2 = 0.1113f * 1.5f;
            float sin0 = MathF.Sin((frame + 55555) * s0);
            float t0 = sin0 * MathF.Cos(frame * s1);
            float t1 = sin0 * MathF.Sin(frame * s1);
            float t2 = MathF.Cos((frame + 55555) * s0);
            float sinAdd = MathF.Sin((frame + 11111) * s2);
            float cosAdd = MathF.Cos((frame + 11111) * s2);
            var dir = new Vector3(cosAdd * t1 - sinAdd * t2, sinAdd * t1 + cosAdd * t2, t0);

            float life = j.LifeTime * 40f / 30f;
            float pos = life < 20f ? life * 4f : life + 60f;
            if ((int)j.LifeTime == 30 && !_meteoriteSound)
            {
                _meteoriteSound = true;
                SoundController.Instance.PlayBuffer(SoundMeteorite);
            }

            float circle = MathF.Min(MathF.Max(0f, life - 10f) * 5f, 150f);
            Vector3 position = j.TargetPosition + dir * circle;
            int phase = j.Index * 51231 + frame / 10;
            position.X = (pos * position.X + (100f - pos) * (a.StartPosition.X + 25f * MathF.Cos(phase * 0.01f))) * 0.01f;
            position.Y = (pos * position.Y + (100f - pos) * (a.StartPosition.Y + 25f * MathF.Cos((phase + 3711) * 0.01f))) * 0.01f;
            position.Z = (pos * position.Z + (100f - pos) * (a.StartPosition.Z + 25f * MathF.Cos((phase + 7422) * 0.01f))) * 0.01f;
            j.Position = position;

            if (j.Skill == 1)
            {
                a.Position = position;
                CheckClientArrow(ref a);
            }
        }

        // ───────────────────────────── particles ─────────────────────────────

        private void SpawnParticle(ParticleKind kind, Vector3 position, Vector3 light, float scale, ref Arrow a) =>
            SpawnParticle(kind, position, light, scale, a.Bx, a.By, a.Bz);

        private void SpawnParticle(ParticleKind kind, Vector3 position, Vector3 light, float scale, Vector3 angle)
        {
            if (kind == ParticleKind.Shiny)
                angle.X = 45f; // o->Angle[0] = 45
            SpawnParticle(kind, position, light, scale,
                SourceJointMath.Rotate(Vector3.UnitX, angle), SourceJointMath.Rotate(Vector3.UnitY, angle), SourceJointMath.Rotate(Vector3.UnitZ, angle));
        }

        private void SpawnParticle(ParticleKind kind, Vector3 position, Vector3 light, float scale, Vector3 bx, Vector3 by, Vector3 bz)
        {
            Particle[]? pool = _particles;
            if (pool == null)
                return;

            for (int i = 0; i < _particleCapacity; i++)
            {
                ref Particle p = ref pool[i];
                if (p.Live)
                    continue;

                var random = MuGame.Random;
                p = new Particle { Live = true, Kind = kind, Position = position, StartPosition = position, Light = light, Scale = scale,
                    Bx = bx, By = by, Bz = bz, LifeTime = 2f, Alpha = 1f };
                switch (kind)
                {
                    case ParticleKind.Fire:
                        p.LifeTime = 24f;
                        p.Velocity = new Vector3(0f, -(random.Next(16) + 32) * 0.1f, 0f);
                        p.Scale = (random.Next(64) + 128) * 0.01f;
                        break;
                    case ParticleKind.Flare:
                        p.LifeTime = 40f;
                        p.Gravity = random.Next(100) / 100f * 5f + 1f;
                        p.Velocity = new Vector3(random.Next(300) - 150, 0f, 0f); // orbit phase, not a velocity
                        p.Scale = scale + random.Next(6) * 0.01f;
                        p.Rotation = random.Next(360);
                        break;
                    case ParticleKind.Flower:
                        p.Texture = (byte)random.Next(3);
                        p.LifeTime = 15 + random.Next(10);
                        p.Scale = (random.Next(8) + 4) * 0.03f;
                        p.Light = Vector3.One;
                        p.Velocity = new Vector3((random.Next(32) - 16) * 0.1f, (random.Next(32) - 16) * 0.1f, (random.Next(32) - 32) * 0.1f);
                        break;
                    case ParticleKind.Bubble:
                        p.LifeTime = 30 + random.Next(10);
                        p.Scale = (random.Next(6) + 4) * 0.03f;
                        p.Light = Vector3.One;
                        break;
                    case ParticleKind.Smoke0:
                        p.LifeTime = 16f;
                        p.Scale = (random.Next(32) + 48) * 0.01f;
                        p.Rotation = (int)FPSCounter.Instance.WorldTime % 360;
                        break;
                    case ParticleKind.Smoke13:
                        p.LifeTime = 20f;
                        p.Scale = (random.Next(64) + 64) * 0.01f;
                        p.Rotation = random.Next(360);
                        break;
                    case ParticleKind.Smoke24:
                        p.LifeTime = 32f;
                        p.Scale = scale + (random.Next(32) + 48) * 0.01f * scale;
                        p.Rotation = (int)FPSCounter.Instance.WorldTime % 360;
                        break;
                    case ParticleKind.Smoke34:
                    case ParticleKind.Smoke35:
                        p.Light = new Vector3(1f, 0.2f, 0.2f);
                        p.LifeTime = 30f;
                        p.Rotation = random.Next(360);
                        if (kind == ParticleKind.Smoke35)
                            p.Velocity = new Vector3(0f, -5f, 0f);
                        break;
                    case ParticleKind.Spark10:
                        p.Velocity = new Vector3(-4f + random.Next(8), 0f, -4f + random.Next(8));
                        p.LifeTime = 20f;
                        p.Scale = random.Next(5) / 10f + 1f;
                        break;
                    case ParticleKind.Spark30:
                        p.LifeTime = random.Next(5) + 50;
                        p.Scale = random.Next(10) / 10f + 0.2f;
                        p.Velocity = new Vector3(-2f + random.Next(5), -2f + random.Next(5), -2f + random.Next(5));
                        break;
                    case ParticleKind.Shiny:
                        p.Velocity = new Vector3(-4f + random.Next(8), random.Next(4), random.Next(4));
                        p.LifeTime = 30f;
                        p.Scale = random.Next(10) / 20f + 0.5f;
                        break;
                }

                _liveParticles++;
                _kindCounts[(int)kind]++;
                return;
            }
        }

        private void TickParticles(float factor)
        {
            Particle[] pool = _particles!;
            var random = MuGame.Random;
            for (int i = 0; i < _particleCapacity; i++)
            {
                ref Particle p = ref pool[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                bool dead = p.LifeTime <= 0f;
                if (!dead)
                {
                    // MovePosition(Position, Angle, Velocity)
                    if (p.Kind != ParticleKind.Flare && p.Velocity != Vector3.Zero)
                        p.Position += (p.Bx * p.Velocity.X + p.By * p.Velocity.Y + p.Bz * p.Velocity.Z) * factor;

                    switch (p.Kind)
                    {
                        case ParticleKind.Fire:
                            p.Gravity += 0.004f * factor;
                            p.Scale -= 0.04f * factor;
                            p.Position.Z += p.Gravity * 10f * factor;
                            break;
                        case ParticleKind.Flare:
                        {
                            float count = (p.Velocity.X + p.LifeTime) * 0.1f;
                            p.Position.X = p.StartPosition.X + MathF.Sin(count) * 40f;
                            p.Position.Y = p.StartPosition.Y - MathF.Cos(count) * 40f;
                            p.Position.Z -= p.Gravity * factor;
                            p.Scale -= 0.002f * factor;
                            p.StartPosition.X += 2.5f * factor;
                            if (p.LifeTime <= 20f)
                                p.Light *= MathF.Pow(1f / 1.1f, factor);
                            break;
                        }
                        case ParticleKind.Flower:
                            if (p.Frame == 0)
                            {
                                p.Velocity += new Vector3(
                                    (random.Next(32) - 16) * 0.1f, (random.Next(32) - 16) * 0.1f, (random.Next(32) - 16) * 0.1f) * factor;
                                p.Position += p.Velocity * factor;
                            }
                            float height = TerrainHeight(p.Position);
                            if (p.Position.Z < height)
                            {
                                p.Position.Z = height;
                                p.Velocity = Vector3.Zero;
                                p.Frame = 1;
                            }
                            p.Rotation += random.Next(16) * factor;
                            break;
                        case ParticleKind.Bubble:
                            p.Frame++; // per rendered frame
                            p.Position.X += (random.Next(30) - 15) * p.Scale * factor;
                            p.Position.Y += (random.Next(30) - 15) * p.Scale * factor;
                            p.Position.Z += (random.Next(20) + 10) * 0.3f * p.Scale * factor;
                            p.Scale += factor * 0.002f;
                            break;
                        case ParticleKind.Smoke0:
                            p.Light = new Vector3(p.LifeTime / 8f);
                            p.Gravity += 0.2f * factor;
                            p.Position.Z += p.Gravity * factor;
                            p.Scale += factor * 0.05f;
                            break;
                        case ParticleKind.Smoke13:
                        {
                            float l = p.LifeTime / 20f;
                            p.Light = new Vector3(l, l * 0.5f, l * 0.1f);
                            p.Scale += factor * 0.09f;
                            break;
                        }
                        case ParticleKind.Smoke24:
                        {
                            float l = p.LifeTime / 32f;
                            p.Light = new Vector3(l * 0.2f, l * 0.5f, l * 0.35f);
                            p.Gravity += 0.1f * factor;
                            p.Position.Z += p.Gravity * factor;
                            p.Scale += factor * 0.05f;
                            break;
                        }
                        case ParticleKind.Smoke34:
                        case ParticleKind.Smoke35:
                            p.Light *= MathF.Pow(1f / 1.04f, factor);
                            p.Scale += factor * 0.06f;
                            break;
                        case ParticleKind.Spark10:
                            p.Light *= MathF.Pow(1f / 1.08f, factor);
                            p.Scale -= factor * 0.03f;
                            p.Alpha -= factor * 0.001f;
                            dead = p.Alpha <= 0f;
                            break;
                        case ParticleKind.Spark30:
                            if (p.LifeTime <= 45f)
                            {
                                p.Velocity.X += MathF.Sin(MathHelper.ToRadians(random.Next(360))) * factor;
                                p.Velocity.Y += MathF.Cos(MathHelper.ToRadians(random.Next(360))) * factor;
                            }
                            p.Light *= MathF.Pow(1f / 1.05f, factor);
                            p.Scale -= factor * 0.01f;
                            p.LifeTime -= factor;
                            dead = p.LifeTime <= 0f;
                            break;
                        case ParticleKind.Shiny:
                            p.Light *= MathF.Pow(1f / 1.05f, factor);
                            p.Scale -= factor * 0.04f;
                            p.Alpha -= factor * 0.001f;
                            dead = p.Alpha <= 0f;
                            break;
                    }
                }

                if (dead)
                {
                    p.Live = false;
                    _liveParticles--;
                    _kindCounts[(int)p.Kind]--;
                }
            }
        }

        // ───────────────────────────── waves / feathers ─────────────────────────────

        /// <summary>CreateEffect(MODEL_WAVES, sub 3 / 4, PKKey 0): Scale starts at PKKey * 0.05 = 0.</summary>
        private void SpawnWave(Vector3 position, float yaw, Vector3 light, int subType)
        {
            Wave[]? pool = _waves;
            if (pool == null)
                return;
            for (int i = 0; i < pool.Length; i++)
            {
                ref Wave w = ref pool[i];
                if (w.Live)
                    continue;
                w = new Wave { Live = true, SubType = subType, Position = position, Angle = new Vector3(90f, 0f, yaw), Light = light,
                    Scale = 0f, Gravity = 0.01f, BlendMeshLight = 1f, LifeTime = 15f };
                _liveWaves++;
                return;
            }
        }

        private void TickWaves(float factor)
        {
            Wave[] pool = _waves!;
            for (int i = 0; i < pool.Length; i++)
            {
                ref Wave w = ref pool[i];
                if (!w.Live)
                    continue;
                bool three = w.SubType == 3;
                w.Scale += w.Gravity * factor;
                w.Gravity += (three ? 0.005f : 0.002f) * factor;
                w.Scale = MathF.Min(w.Scale, three ? 1.5f : 2.5f);
                w.BlendMeshLight *= MathF.Pow(1f / (three ? 1.3f : 1.2f), factor);
                w.Angle.Y += 45f * factor;
                w.Frame += 0.3f * factor;
                w.LifeTime -= factor;
                if (w.LifeTime <= 0f)
                {
                    w.Live = false;
                    _liveWaves--;
                }
            }
        }

        /// <summary>CreateEffect(MODEL_FEATHER, sub 0 / 1, scale 0.6), Light (0.6, 0.7, 0.9).</summary>
        private void SpawnFeather(Vector3 origin, int subType)
        {
            Feather[]? pool = _feathers;
            if (pool == null)
                return;
            var random = MuGame.Random;
            for (int i = 0; i < pool.Length; i++)
            {
                ref Feather f = ref pool[i];
                if (f.Live)
                    continue;
                Vector3 position = origin + new Vector3(
                    (random.Next(20) - 10) * 4f, (random.Next(20) - 10) * 4f, (random.Next(20) - 10) * 4f);
                Vector3 direction = origin - position;
                if (direction.LengthSquared() > 0f)
                    direction.Normalize();
                // iAddDirection = (int)((rand() % 10 - 5) * 0.08f) is always 0.
                f = new Feather
                {
                    Live = true,
                    SubType = subType,
                    Position = position,
                    Direction = direction,
                    Scale = 0.6f + (random.Next(20) - 10) * (0.6f * 0.03f),
                    LifeTime = 30 + (random.Next(20) - 10),
                    Alpha = 0.6f + random.Next(10) * 0.02f,
                    Light = new Vector3(0.6f, 0.7f, 0.9f),
                    Angle = new Vector3(random.Next(360), random.Next(360), random.Next(360)),
                    Spin = new Vector3(random.Next(10) - 5, random.Next(10) - 5, random.Next(10) - 5),
                };
                _liveFeathers++;
                return;
            }
        }

        private void TickFeathers(float factor)
        {
            Feather[] pool = _feathers!;
            float fade = MathF.Pow(0.97f, factor);
            float shrink = MathF.Pow(0.99f, factor);
            for (int i = 0; i < pool.Length; i++)
            {
                ref Feather f = ref pool[i];
                if (!f.Live)
                    continue;
                f.Light *= fade;
                f.Alpha *= fade;
                if (f.SubType == 1)
                    f.Scale *= shrink;
                f.Angle += f.Spin; // per frame
                f.Frame += 0.3f * factor;
                f.Position += SourceJointMath.Rotate(f.Direction, f.Angle) * factor;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                {
                    f.Live = false;
                    _liveFeathers--;
                }
            }
        }

        protected override bool HasActiveContent() =>
            _liveArrows > 0 || _liveJoints > 0 || _liveParticles > 0 || _liveWaves > 0 || _liveFeathers > 0 || _bladeLife > 0f ||
            (_bombs?.HasLive ?? false) || (_rings?.HasLive ?? false) || (_sparks?.HasLive ?? false);

        // ───────────────────────────── draw ─────────────────────────────

        protected override void OnDraw()
        {
            if (_liveJoints > 0)
                DrawTrails();
            DrawModels();
            DrawSprites();
            if (_liveParticles > 0)
                DrawParticles();
            _bombs?.Draw(Batch, _sparkTex, _explosionTex);
            if (_sparks != null && _sparks.HasLive)
            {
                _sparks.DrawJoints(Batch, _sparkJointTex);
                _sparks.DrawSparks(Batch, _sparkTex);
            }
        }

        private void DrawTrails()
        {
            for (int pass = 0; pass < PassCount; pass++)
            {
                if (_passCounts[pass] == 0)
                    continue;

                Texture2D? texture = pass switch
                {
                    PassFlareAdd or PassFlareOther => _flareTrailTex,
                    PassEnergy => _energyTex,
                    PassHoly => _flareTex,
                    PassBlue => _flareBlueTex,
                    _ => _flareForceTex
                };
                BlendState blend = pass == PassFlareOther ? Blendings.InverseSourceColorAdditive : Blendings.OneOneAdditive;
                SamplerState sampler = pass <= PassEnergy ? SamplerState.LinearWrap : SamplerState.LinearClamp;
                if (texture == null || !Batch.Begin(texture, blend, DepthStencilState.DepthRead, sampler))
                    continue;

                for (int i = 0; i < MaxJoints; i++)
                {
                    ref Joint j = ref _joints[i];
                    if (!j.Live || j.Pass != pass)
                        continue;
                    Batch.AddTrail(j.Trail, SourceQuadBatch.TrailFaces.Both, j.Light, SourceQuadBatch.TrailShade.Flat,
                        j.Trail.MaxTails - 1, reverseV: j.ReverseV);
                }
                Batch.End();
            }
        }

        private static int KeyCount(SourceModelRenderer? renderer) =>
            renderer?.Model?.Actions is { Length: > 0 } actions ? Math.Max(1, actions[0].NumAnimationKeys) : 1;

        private void DrawModels()
        {
            if (_model != null && _liveArrows > 0)
            {
                bool updated = false;
                for (int i = 0; i < MaxArrows; i++)
                {
                    ref Arrow a = ref _arrows[i];
                    if (!a.Live)
                        continue;

                    if (!updated || _modelKeys > 1)
                    {
                        double frame = WrapFrame(a.Frame);
                        _model.SetAnimationTime(frame);
                        _model.Update(LastGameTime);
                        if (_model2 != null)
                        {
                            _model2.SetAnimationTime(frame);
                            _model2.Update(LastGameTime);
                        }
                        updated = true;
                    }

                    Vector3 angle = ToRadians(a.Angle);
                    if (_style == ProjectileStyle.DarkStinger)
                    {
                        _model.DrawInstance(a.Position, angle, a.Scale, new Vector3(0.7f, 0.7f, 0.9f), 1f);
                        _model2?.DrawInstance(a.Position, angle, a.Scale, new Vector3(0.3f, 0.4f, 0.9f), 1f);
                    }
                    else
                    {
                        _model.DrawInstance(a.Position, angle, a.Scale, a.Light, a.BlendMeshLight);
                    }
                }
            }

            if (_piercing != null)
            {
                bool updated = false;
                for (int i = 0; i < MaxArrows; i++)
                {
                    ref Arrow a = ref _arrows[i];
                    if (!a.PierceLive || !a.PierceVisible)
                        continue;
                    if (!updated)
                    {
                        _piercing.SetAnimationTime(_pierceFrame % KeyCount(_piercing));
                        _piercing.Update(LastGameTime);
                        updated = true;
                    }
                    _piercing.DrawInstance(a.PiercePosition, ToRadians(a.Angle), 1f, Vector3.One, 1f);
                }
            }

            if (_wavesModel != null && _liveWaves > 0)
            {
                Wave[] pool = _waves!;
                int keys = KeyCount(_wavesModel);
                bool updated = false;
                for (int i = 0; i < pool.Length; i++)
                {
                    ref Wave w = ref pool[i];
                    if (!w.Live)
                        continue;
                    if (!updated || keys > 1)
                    {
                        _wavesModel.SetAnimationTime(w.Frame % keys);
                        _wavesModel.Update(LastGameTime);
                        updated = true;
                    }
                    _wavesModel.DrawInstance(w.Position, ToRadians(w.Angle), w.Scale, w.Light * w.BlendMeshLight, 1f);
                }
            }

            if (_featherModel != null && _liveFeathers > 0)
            {
                Feather[] pool = _feathers!;
                int keys = KeyCount(_featherModel);
                bool updated = false;
                for (int i = 0; i < pool.Length; i++)
                {
                    ref Feather f = ref pool[i];
                    if (!f.Live)
                        continue;
                    if (!updated || keys > 1)
                    {
                        _featherModel.SetAnimationTime(f.Frame % keys);
                        _featherModel.Update(LastGameTime);
                        updated = true;
                    }
                    // RenderMesh(0, RENDER_TEXTURE, Alpha, BlendMesh 0, BlendMeshLight = Alpha)
                    _featherModel.DrawInstance(f.Position, ToRadians(f.Angle), f.Scale, f.Light * f.Alpha, 1f);
                }
            }

            _rings?.Draw(LastGameTime);
        }

        private bool BeginSprites(Texture2D? texture) =>
            texture != null && Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp);

        private void DrawSprites()
        {
            var random = MuGame.Random;
            float rotation = (int)FPSCounter.Instance.WorldTime * 0.1f;

            switch (_style)
            {
                case ProjectileStyle.Nature:
                case ProjectileStyle.Wing:
                    if (_liveArrows > 0 && BeginSprites(_lightningTex))
                    {
                        for (int i = 0; i < MaxArrows; i++)
                        {
                            ref Arrow a = ref _arrows[i];
                            if (!a.Live) continue;
                            Batch.AddSprite(a.Position, 0.5f, a.Light, rotation);
                            Batch.AddSprite(a.Position, 1f, a.Light, -rotation);
                        }
                        Batch.End();
                    }
                    break;

                case ProjectileStyle.Bomb:
                    if (_liveArrows > 0 && BeginSprites(_lightTex))
                    {
                        var light = new Vector3(1f, 0.6f, 0.4f);
                        for (int i = 0; i < MaxArrows; i++)
                        {
                            ref Arrow a = ref _arrows[i];
                            if (!a.Live) continue;
                            Batch.AddSprite(a.Position, 1f, light, rotation);
                            Batch.AddSprite(a.Position, 2f, light, -rotation);
                        }
                        Batch.End();
                    }
                    break;

                case ProjectileStyle.DarkStinger:
                    if (_liveArrows > 0 && BeginSprites(_lightTex))
                    {
                        var light = new Vector3(0.6f, 0.7f, 0.9f);
                        for (int i = 0; i < MaxArrows; i++)
                        {
                            ref Arrow a = ref _arrows[i];
                            if (!a.Live) continue;
                            Vector3 position = ArrowBone(ref a, 0) + SourceJointMath.Rotate(a.Direction, a.Angle);
                            Batch.AddSprite(position, 3f, light);
                            Batch.AddSprite(position, 2f, light);
                        }
                        Batch.End();
                    }
                    break;

                case ProjectileStyle.Drill:
                    if (_liveArrows > 0)
                    {
                        var light = new Vector3(1f, 0.6f, 0.4f);
                        float scale = (float)Math.Sin(FPSCounter.Instance.WorldTime * 0.002) * 0.3f + 1.3f;
                        if (BeginSprites(_shiny1Tex))
                        {
                            for (int i = 0; i < MaxArrows; i++)
                            {
                                ref Arrow a = ref _arrows[i];
                                if (!a.Live) continue;
                                Vector3 position = ArrowBone(ref a, 1);
                                Batch.AddSprite(position, scale, light, random.Next(360));
                                Batch.AddSprite(position, scale, light, random.Next(360));
                            }
                            Batch.End();
                        }
                        if (BeginSprites(_lightTex))
                        {
                            for (int i = 0; i < MaxArrows; i++)
                            {
                                ref Arrow a = ref _arrows[i];
                                if (!a.Live) continue;
                                Batch.AddSprite(ArrowBone(ref a, 1), 1.5f, light, 90f);
                            }
                            Batch.End();
                        }
                    }
                    break;

                case ProjectileStyle.Spark:
                    if (_liveJoints > 0 && BeginSprites(_spark1Tex))
                    {
                        for (int i = 0; i < MaxJoints; i++)
                        {
                            ref Joint j = ref _joints[i];
                            if (j.Live && j.Kind == JointKind.Trail && j.SubType == 14)
                                Batch.AddSprite(j.Position, 5f, j.Light);
                        }
                        Batch.End();
                    }
                    break;

                case ProjectileStyle.Holy:
                    if (_liveJoints > 0)
                    {
                        var light = new Vector3(0.3f, 0.3f, 0.5f);
                        if (BeginSprites(_shiny1Tex))
                        {
                            for (int i = 0; i < MaxJoints; i++)
                            {
                                ref Joint j = ref _joints[i];
                                if (j.Live && j.Kind == JointKind.Holy)
                                    Batch.AddSprite(j.Position, (random.Next(2) + 8) * 0.10f, light, random.Next(360));
                            }
                            Batch.End();
                        }
                        if (BeginSprites(_lightTex))
                        {
                            for (int i = 0; i < MaxJoints; i++)
                            {
                                ref Joint j = ref _joints[i];
                                if (!j.Live || j.Kind != JointKind.Holy) continue;
                                Batch.AddSprite(j.Position, (random.Next(2) + 8) * 0.18f, light, random.Next(360));
                                Batch.AddSprite(j.Position, (random.Next(2) + 8) * 0.18f, light, random.Next(360));
                            }
                            Batch.End();
                        }
                    }
                    break;

                case ProjectileStyle.Impact:
                    if (_liveJoints > 0 && BeginSprites(_shiny2Tex))
                    {
                        var light = new Vector3(0f, 1f, 1f);
                        for (int i = 0; i < MaxJoints; i++)
                        {
                            ref Joint j = ref _joints[i];
                            if (j.Live && j.Kind == JointKind.FlashStrike)
                                Batch.AddSprite(j.Position, 1.6f, light, random.Next(360));
                        }
                        Batch.End();
                    }
                    break;
            }

            // MODEL_BLADE_SKILL sub 1: sprites of every frame of a firing tick.
            if (_bladeLife >= 3f && (int)_bladeLife % 2 == 0 && BeginSprites(_shiny1Tex))
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 position = _bladePosition + new Vector3(0f, random.Next(200) - 100, random.Next(200) - 100);
                    Batch.AddSprite(position, (random.Next(8) + 8) * 0.2f, new Vector3(0.2f, 0.3f, 1f), random.Next(360));
                    Batch.AddSprite(position, (random.Next(8) + 8) * 0.07f, Vector3.One, random.Next(360));
                }
                Batch.End();
            }
        }

        private void DrawParticles()
        {
            if (_kindCounts[(int)ParticleKind.Fire] > 0)
                DrawParticlePass(_fireTex, Blendings.OneOneAdditive, ParticleKind.Fire, ParticleKind.Fire, -1);
            if (_kindCounts[(int)ParticleKind.Flare] > 0)
                DrawParticlePass(_flareTex, Blendings.OneOneAdditive, ParticleKind.Flare, ParticleKind.Flare, -1);
            if (_kindCounts[(int)ParticleKind.Flower] > 0)
            {
                for (int t = 0; t < 3; t++)
                    DrawParticlePass(_flowerTex[t], BlendState.NonPremultiplied, ParticleKind.Flower, ParticleKind.Flower, t);
            }
            if (_kindCounts[(int)ParticleKind.Bubble] > 0)
                DrawParticlePass(_bubbleTex, Blendings.OneOneAdditive, ParticleKind.Bubble, ParticleKind.Bubble, -1);
            if (_kindCounts[(int)ParticleKind.Smoke0] + _kindCounts[(int)ParticleKind.Smoke13] + _kindCounts[(int)ParticleKind.Smoke24] +
                _kindCounts[(int)ParticleKind.Smoke34] + _kindCounts[(int)ParticleKind.Smoke35] > 0)
                DrawParticlePass(_smokeTex, Blendings.OneOneAdditive, ParticleKind.Smoke0, ParticleKind.Smoke35, -1);
            if (_kindCounts[(int)ParticleKind.Spark10] + _kindCounts[(int)ParticleKind.Spark30] > 0)
                DrawParticlePass(_spark1Tex, Blendings.OneOneAdditive, ParticleKind.Spark10, ParticleKind.Spark30, -1);
            if (_kindCounts[(int)ParticleKind.Shiny] > 0)
                DrawParticlePass(_shinyTex, Blendings.OneOneAdditive, ParticleKind.Shiny, ParticleKind.Shiny, -1);
        }

        private void DrawParticlePass(Texture2D? texture, BlendState blend, ParticleKind first, ParticleKind last, int flowerTexture)
        {
            if (texture == null || !Batch.Begin(texture, blend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            Particle[] pool = _particles!;
            for (int i = 0; i < _particleCapacity; i++)
            {
                ref Particle p = ref pool[i];
                if (!p.Live || p.Kind < first || p.Kind > last)
                    continue;

                switch (p.Kind)
                {
                    case ParticleKind.Fire:
                    {
                        int frame = (int)((23f - p.LifeTime) / 6f);
                        Batch.AddSpriteRegion(p.Position, texture.Width * p.Scale * 0.25f, texture.Height * p.Scale, p.Light, 0f,
                            frame % 4 * 0.25f, 0f, 0.25f, 1f);
                        break;
                    }
                    case ParticleKind.Bubble:
                    {
                        int frame = p.Frame % 9;
                        Batch.AddSpriteRegion(p.Position, texture.Width * p.Scale, texture.Height * p.Scale, p.Light, 0f,
                            frame % 3 * 0.25f + 0.005f, frame / 3 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                        break;
                    }
                    case ParticleKind.Flower:
                        if (p.Texture == flowerTexture)
                            Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                        break;
                    default:
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                        break;
                }
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            for (int i = 0; i < MaxJoints; i++)
                KillJoint(ref _joints[i]);

            if (_particles != null)
            {
                ArrayPool<Particle>.Shared.Return(_particles);
                _particles = null;
                _particleCapacity = 0;
            }

            _terrainLights?.Dispose();
            _model?.Dispose();
            _model2?.Dispose();
            _piercing?.Dispose();
            _wavesModel?.Dispose();
            _featherModel?.Dispose();
            _rings?.Dispose();
        }

        // ───────────────────────────── weapon → arrow type ─────────────────────────────

        private static bool IsFenrirArrowAction(PlayerAction action) => action is
            PlayerAction.PlayerFenrirAttackBow or
            PlayerAction.PlayerFenrirAttackCrossbow;


        private static ProjectileStyle ResolveEquippedProjectileStyle(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);

            // Item group 4 indices are stable protocol data and therefore take precedence
            // over localized item names. Ammunition entries 7 (bolt) and 15 (arrows) are
            // deliberately excluded.
            if (TryResolveStyleByItem(right, out ProjectileStyle rightStyle, out bool rightCrossbow) && rightCrossbow)
                return rightStyle;
            if (TryResolveStyleByItem(left, out ProjectileStyle leftStyle, out bool leftCrossbow) && !leftCrossbow)
                return leftStyle;
            if (TryResolveStyleByItem(right, out rightStyle, out _))
                return rightStyle;
            if (TryResolveStyleByItem(left, out leftStyle, out _))
                return leftStyle;

            string weaponName = ResolveWeaponName(shooter);
            return ResolveStyleByName(weaponName, shooter.CurrentAction);
        }

        private static bool TryResolveStyleByItem(
            ItemDefinition? item,
            out ProjectileStyle style,
            out bool isCrossbow)
        {
            style = ProjectileStyle.Basic;
            isCrossbow = false;
            if (item == null || item.Group != 4)
                return false;

            switch (item.Id)
            {
                // Bows
                case 0: // Short Bow
                case 1: // Bow
                case 3: // Battle Bow
                case 4: // Tiger Bow
                case 5: // Silver Bow
                    style = ProjectileStyle.Basic;
                    return true;
                case 2:
                    style = ProjectileStyle.V;       // Elven Bow
                    return true;
                case 6:
                    style = ProjectileStyle.Nature;  // Chaos Nature Bow
                    return true;
                case 17:
                    style = ProjectileStyle.Holy;    // Celestial Bow
                    return true;
                case 20:
                    style = ProjectileStyle.Lace;    // Arrow Viper Bow
                    return true;
                case 21:
                    style = ProjectileStyle.Spark;   // Sylph Wind Bow
                    return true;
                case 22:
                    style = ProjectileStyle.Ring;    // Albatross Bow
                    return true;
                case 23:
                    style = ProjectileStyle.DarkStinger; // Stinger Bow
                    return true;
                case 24:
                    style = ProjectileStyle.Gamble;  // Air Lyn Bow
                    return true;

                // Crossbows
                case 8:  // Crossbow
                case 9:  // Golden Crossbow
                    style = ProjectileStyle.Steel;
                    isCrossbow = true;
                    return true;
                case 10:
                    style = ProjectileStyle.Saw;     // Arquebus
                    isCrossbow = true;
                    return true;
                case 11:
                    style = ProjectileStyle.Laser;   // Light Crossbow
                    isCrossbow = true;
                    return true;
                case 12:
                    style = ProjectileStyle.Thunder; // Serpent Crossbow
                    isCrossbow = true;
                    return true;
                case 13:
                    style = ProjectileStyle.Wing;    // Bluewing Crossbow
                    isCrossbow = true;
                    return true;
                case 14:
                    style = ProjectileStyle.Bomb;    // Aquagold Crossbow
                    isCrossbow = true;
                    return true;
                case 16:
                    style = ProjectileStyle.Double;  // Saint Crossbow
                    isCrossbow = true;
                    return true;
                case 18:
                    style = ProjectileStyle.BestCrossbow; // Divine Crossbow of Archangel
                    isCrossbow = true;
                    return true;
                case 19:
                    style = ProjectileStyle.Drill;   // Great Reign Crossbow
                    isCrossbow = true;
                    return true;
                default:
                    return false;
            }
        }

        private static string ResolveWeaponName(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);

            if (right?.Name?.Contains("Crossbow", StringComparison.OrdinalIgnoreCase) == true ||
                right?.Name?.Contains("Kusza", StringComparison.OrdinalIgnoreCase) == true)
            {
                return right.Name;
            }

            if (left?.Name != null && left.Group == 4)
                return left.Name;
            if (right?.Name != null && right.Group == 4)
                return right.Name;

            return shooter.CurrentAction.ToString();
        }

        private static ItemDefinition? TryGetWeaponDefinition(WeaponObject weapon)
        {
            if (weapon.MaterialItemGroup >= 0 && weapon.MaterialItemIndex >= 0)
            {
                return ItemDatabase.GetItemDefinition(
                    (byte)weapon.MaterialItemGroup,
                    (short)weapon.MaterialItemIndex);
            }
            return null;
        }

        private static ProjectileStyle ResolveStyleByName(string weaponName, PlayerAction action)
        {
            string name = weaponName ?? string.Empty;

            if (Contains(name, "Great Reign")) return ProjectileStyle.Drill;
            if (Contains(name, "Divine Crossbow")) return ProjectileStyle.BestCrossbow;
            if (Contains(name, "Saint Crossbow")) return ProjectileStyle.Double;
            if (Contains(name, "Aquagold Crossbow")) return ProjectileStyle.Bomb;
            if (Contains(name, "Bluewing Crossbow")) return ProjectileStyle.Wing;
            if (Contains(name, "Serpent Crossbow")) return ProjectileStyle.Thunder;
            if (Contains(name, "Light Crossbow")) return ProjectileStyle.Laser;
            if (Contains(name, "Arquebus")) return ProjectileStyle.Saw;
            if (Contains(name, "Golden Crossbow") || Contains(name, "Crossbow") || Contains(name, "Kusza"))
                return ProjectileStyle.Steel;

            if (Contains(name, "Air Lyn")) return ProjectileStyle.Gamble;
            if (Contains(name, "Stinger")) return ProjectileStyle.DarkStinger;
            if (Contains(name, "Albatross")) return ProjectileStyle.Ring;
            if (Contains(name, "Sylph Wind")) return ProjectileStyle.Spark;
            if (Contains(name, "Arrow Viper")) return ProjectileStyle.Lace;
            if (Contains(name, "Celestial")) return ProjectileStyle.Holy;
            if (Contains(name, "Chaos Nature")) return ProjectileStyle.Nature;
            if (Contains(name, "Elven Bow")) return ProjectileStyle.V;

            return IsCrossbowAction(action) ? ProjectileStyle.Steel : ProjectileStyle.Basic;
        }


        private static bool UsesFourProjectileMultiShot(ProjectileStyle style) => style is
            ProjectileStyle.BestCrossbow or
            ProjectileStyle.Drill or
            ProjectileStyle.Ring or
            ProjectileStyle.DarkStinger or
            ProjectileStyle.Gamble;

        public static bool UsesCrossbow(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);
            if (TryResolveStyleByItem(right, out _, out bool rightCrossbow))
                return rightCrossbow;
            if (TryResolveStyleByItem(left, out _, out bool leftCrossbow))
                return leftCrossbow;

            string weaponName = ResolveWeaponName(shooter);
            return weaponName.Contains("Crossbow", StringComparison.OrdinalIgnoreCase) ||
                   weaponName.Contains("Kusza", StringComparison.OrdinalIgnoreCase) ||
                   IsCrossbowAction(shooter.CurrentAction);
        }

        public static bool IsBowAttackAction(PlayerAction action) => action is
            PlayerAction.PlayerAttackBow or
            PlayerAction.PlayerAttackCrossbow or
            PlayerAction.PlayerAttackFlyBow or
            PlayerAction.PlayerAttackFlyCrossbow or
            PlayerAction.PlayerAttackRideBow or
            PlayerAction.PlayerAttackRideCrossbow or
            PlayerAction.PlayerFenrirAttackBow or
            PlayerAction.PlayerFenrirAttackCrossbow or
            PlayerAction.PlayerAttackBowUp or
            PlayerAction.PlayerAttackCrossbowUp or
            PlayerAction.PlayerAttackFlyBowUp or
            PlayerAction.PlayerAttackFlyCrossbowUp or
            PlayerAction.PlayerAttackRideBowUp or
            PlayerAction.PlayerAttackRideCrossbowUp;

        private static bool IsCrossbowAction(PlayerAction action) => action is
            PlayerAction.PlayerAttackCrossbow or
            PlayerAction.PlayerAttackFlyCrossbow or
            PlayerAction.PlayerAttackRideCrossbow or
            PlayerAction.PlayerFenrirAttackCrossbow or
            PlayerAction.PlayerAttackCrossbowUp or
            PlayerAction.PlayerAttackFlyCrossbowUp or
            PlayerAction.PlayerAttackRideCrossbowUp or
            PlayerAction.PlayerSkillMultishotCrossbowStand or
            PlayerAction.PlayerSkillMultishotCrossbowFlying;

        private static bool Contains(string value, string expected) =>
            value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    }

    public static class ArrowProjectileSpawner
    {
        public static bool SpawnNormal(PlayerObject shooter, ushort targetId)
        {
            if (shooter?.World is not WalkableWorldControl world)
                return false;
            if (!ArrowProjectileEffect.IsBowAttackAction(shooter.CurrentAction))
                return false;

            var effect = new ArrowProjectileEffect(shooter, world, targetId, targetPosition: null, ArrowVolleyKind.Normal);
            world.Objects.Add(effect);
            QueueLoad(effect);
            return true;
        }

        public static bool IsArrowSkill(ushort skillId) => skillId is 24 or 25 or 46 or 51 or 52 or 235;

        public static ArrowVolleyKind GetVolleyKind(ushort skillId) => skillId switch
        {
            24 => ArrowVolleyKind.TripleShot,
            46 => ArrowVolleyKind.DeepImpact,
            51 => ArrowVolleyKind.IceArrow,
            52 => ArrowVolleyKind.Penetration,
            235 => ArrowVolleyKind.MultiShot,
            _ => ArrowVolleyKind.Normal
        };

        private static void QueueLoad(WorldObject effect)
        {
            if (effect.Status != GameControlStatus.NonInitialized)
                return;

            MuGame.TaskScheduler?.QueueTask(
                async () => await effect.Load(),
                Client.Main.Controllers.TaskScheduler.Priority.High,
                $"ArrowProjectile.Load.{effect.GetType().Name}");
        }
    }
}
