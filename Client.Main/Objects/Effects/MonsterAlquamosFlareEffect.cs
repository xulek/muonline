#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of the AT_SKILL_ENERGYBALL branch of AttackEffect for MONSTER_ALQUAMOS (ZzzCharacter.cpp:1979-1990),
    /// which is reached when the monster casts through ReceiveMagic (WSclient.cpp:3926, 0x19 skill 17).
    ///   create    4x CreateJoint(BITMAP_FLARE, o->Position, o->Position, Angle, 7, target, 50)   ZzzCharacter.cpp:1984
    ///             init sub 7: Light (0.2, 0.2, 1), MultiUse = rand() % 10, LifeTime = 30 + MultiUse,
    ///             MaxTails 15 (/ factor), Scale 30, Velocity 0, Direction[0] unused          ZzzEffectJoint.cpp:1884-1892
    ///             TexType BITMAP_FLARE = Effect/Flare.jpg                                   ZzzOpenData.cpp:5068
    ///   move      MoveJoint sub 7 block                                                     ZzzEffectJoint.cpp:5564-5699
    ///             orbit vDir from WorldTime (iFrame) and joint index, radius fCircle, lerp to target (fPos)  5576-5653
    ///             SOUND_METEORITE01 (eMeteorite.wav) when (int)LifeTime == 30               5627-5630
    ///             per tick: CreateSprite(BITMAP_SHINY+1, scale (rand%2+8)*0.10) and 2x CreateSprite(BITMAP_LIGHT,
    ///             scale (rand%2+8)*0.18), colour (0.5, 0.5, 1), rotation rand%360          5660-5690
    ///             CreateParticleFpsChecked(BITMAP_EXPLOTION, sub 1, scale 0.6) when (int)LifeTime == 1   5694-5699
    ///             epilogue: CreateTail at Position (Scale 30), LifeTime -= factor, dead below 0
    ///   render    RenderJoints default: colour Light, additive, both faces, U = (NumTails - j) / (MaxTails - 1)
    ///   explosion CreateParticle sub 1: LifeTime 20, Scale 0.6, sound SOUND_EXPLOTION01 (eExplosion.wav)  ZzzEffectParticle.cpp:2476-2496
    ///             MoveParticles: Frame = (20 - LifeTime) / 2                                 ZzzEffectParticle.cpp:4232
    ///             RenderParticles: sheet 4x4, u = Frame % 4 / 4 + 0.005, size 0.24, additive  ZzzEffectParticle.cpp:8993-8995
    /// Port choices (undefined in the original): the joint orientation reads the uninitialised local Angle
    /// (ZzzCharacter.cpp:1984) and is taken as zero; the start tail uses Scale 30 (stale o->Scale in the original);
    /// the orbit phase uses joint index 0..3 instead of the pool slot.
    /// </summary>
    public sealed class MonsterAlquamosFlareEffect : EffectObject
    {
        private const string FlareTexturePath = "Effect/Flare.jpg";            // BITMAP_FLARE (joint texture)
        private const string LightTexturePath = "Effect/flare01.jpg";          // BITMAP_LIGHT
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";          // BITMAP_SHINY + 1
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg";  // BITMAP_EXPLOTION (4x4 sheet)
        private const string MeteoriteSoundPath = "Sound/eMeteorite.wav";      // SOUND_METEORITE01
        private const string ExplosionSoundPath = "Sound/eExplosion.wav";      // SOUND_EXPLOTION01

        private const int JointCount = 4;
        private const int JointMaxTails = 15;
        private const float JointLifeTicks = 30f;
        private const float JointScale = 30f;
        private const int MaxSprites = JointCount * 3;
        private const int MaxExplosions = 8;
        private const float ExplosionLifeTicks = 20f;
        private const float ExplosionScale = 0.6f;
        private static readonly Vector3 JointLight = new(0.2f, 0.2f, 1f);
        private static readonly Vector3 SpriteLight = new(0.5f, 0.5f, 1f);

        private struct FlareJoint
        {
            public int Index;
            public int MultiUse;
            public float LifeTicks;
            public Vector3 Position;
            public SourceTrail Trail;
            public bool Live;
        }

        private struct SpriteSpec
        {
            public Vector3 Position;
            public float Scale;
            public float Rotation;
            public bool IsShiny;
        }

        private struct ExplosionParticle
        {
            public Vector3 Position;
            public float LifeTicks;
            public int Frame;
        }

        private readonly FlareJoint[] _joints = new FlareJoint[JointCount];
        private readonly SpriteSpec[] _sprites = new SpriteSpec[MaxSprites];
        private readonly ExplosionParticle[] _explosions = new ExplosionParticle[MaxExplosions];
        private readonly SourceQuadBatch _trailBatch = new(512);
        private readonly SourceQuadBatch _lightBatch = new(MaxSprites);
        private readonly SourceQuadBatch _shinyBatch = new(MaxSprites);
        private readonly SourceQuadBatch _explosionBatch = new(MaxExplosions);
        private readonly Vector3 _origin;
        private readonly WalkerObject? _target;
        private Vector3 _targetPosition;
        private int _spriteCount;
        private int _explosionCount;
        private bool _finished;
        private Texture2D? _flareTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _explosionTexture;

        public MonsterAlquamosFlareEffect(Vector3 origin, WalkerObject? target)
        {
            _origin = origin;
            _target = target;
            _targetPosition = target?.WorldPosition.Translation ?? origin;
            Position = origin;
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-400f, -400f, -100f), new Vector3(400f, 400f, 900f));

            for (int i = 0; i < JointCount; i++)
            {
                int multiUse = MuGame.Random.Next(10);
                var trail = new SourceTrail(SourceJointMath.ScaledMaxTails(JointMaxTails));
                trail.Push(origin, Vector3.Zero, JointScale); // CreateJoint start tail at the caster
                _joints[i] = new FlareJoint
                {
                    Index = i,
                    MultiUse = multiUse,
                    LifeTicks = JointLifeTicks + multiUse,
                    Position = origin,
                    Trail = trail,
                    Live = true,
                };
            }
        }

        public override async Task Load()
        {
            await base.Load();
            _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture(FlareTexturePath);
            _lightTexture = await TextureLoader.Instance.PrepareAndGetTexture(LightTexturePath);
            _shinyTexture = await TextureLoader.Instance.PrepareAndGetTexture(ShinyTexturePath);
            _explosionTexture = await TextureLoader.Instance.PrepareAndGetTexture(ExplosionTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_finished)
                return;

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            int iFrameBase = (int)(FPSCounter.Instance.WorldTime / 40.0);
            if (_target != null)
                _targetPosition = _target.WorldPosition.Translation;

            _spriteCount = 0;
            int liveJoints = 0;
            for (int i = 0; i < JointCount; i++)
            {
                if (!_joints[i].Live)
                    continue;

                MoveJoint(ref _joints[i], iFrameBase, factor);
                if (_joints[i].Live)
                    liveJoints++;
            }

            MoveExplosions(factor);

            if (liveJoints == 0 && _explosionCount == 0)
            {
                _finished = true;
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private void MoveJoint(ref FlareJoint joint, int iFrameBase, float factor)
        {
            int iIndex = joint.Index;

            // iFrame = ((iIndex % 2) ? iFrame : -iFrame) + iIndex * 53731
            int iFrame = ((iIndex % 2) != 0 ? iFrameBase : -iFrameBase) + iIndex * 53731;
            float a = (iFrame + 55555) * 0.048f;
            float b = iFrame * 0.0613f;
            float c = (iFrame + 11111) * 0.1113f;
            float t0 = MathF.Sin(a) * MathF.Cos(b);
            float t1 = MathF.Sin(a) * MathF.Sin(b);
            float t2 = MathF.Cos(a);
            float fSinAdd = MathF.Sin(c);
            float fCosAdd = MathF.Cos(c);
            var vDir = new Vector3(
                fCosAdd * t1 - fSinAdd * t2,
                fSinAdd * t1 + fCosAdd * t2,
                t0);

            float fLife = joint.LifeTicks * 40f / 30f;
            float fPos = fLife < 10f ? fLife * 7f : fLife + 60f;
            fPos = fPos / (30 + joint.MultiUse) * 30f;
            if ((int)joint.LifeTicks == 30)
                SoundController.Instance.PlayBuffer(MeteoriteSoundPath);

            float fCircle = MathF.Min(MathF.Max(0f, 40f - fLife) * 15f, 150f);
            Vector3 p = _origin + vDir * fCircle;

            // Position[k] = (fPos * Position[k] + (100 - fPos) * (Target->Position[k] + 25 cos(...))) * 0.01
            int phase = iFrame / 10;
            float jx = 25f * MathF.Cos((iIndex * 51231 + 0 * 3711 + phase) * 0.01f);
            float jy = 25f * MathF.Cos((iIndex * 51231 + 1 * 3711 + phase) * 0.01f);
            float jz = 25f * MathF.Cos((iIndex * 51231 + 2 * 3711 + phase) * 0.01f);
            p.X = (fPos * p.X + (100f - fPos) * (_targetPosition.X + jx)) * 0.01f;
            p.Y = (fPos * p.Y + (100f - fPos) * (_targetPosition.Y + jy)) * 0.01f;
            p.Z = (fPos * p.Z + (100f - fPos) * (_targetPosition.Z + jz)) * 0.01f;
            p.Z += 100f;

            AddSprite(p, (MuGame.Random.Next(2) + 8) * 0.10f, MuGame.Random.Next(360), isShiny: true);
            AddSprite(p, (MuGame.Random.Next(2) + 8) * 0.18f, MuGame.Random.Next(360), isShiny: false);
            AddSprite(p, (MuGame.Random.Next(2) + 8) * 0.18f, MuGame.Random.Next(360), isShiny: false);

            if ((int)joint.LifeTicks == 1 && FPSCounter.Instance.RandFPSCheck(1))
                SpawnExplosion(p);

            // Epilogue: CreateTail at the new position, then LifeTime -= factor.
            joint.Position = p;
            joint.Trail.Push(p, Vector3.Zero, JointScale);
            joint.LifeTicks -= factor;
            if (joint.LifeTicks < 0f)
                joint.Live = false;
        }

        private void MoveExplosions(float factor)
        {
            for (int i = 0; i < _explosionCount;)
            {
                ref ExplosionParticle e = ref _explosions[i];
                e.LifeTicks -= factor;
                if (e.LifeTicks <= 0f)
                {
                    // Swap-remove: draw order does not matter for additive sprites.
                    _explosions[i] = _explosions[--_explosionCount];
                    continue;
                }

                e.Frame = (int)((ExplosionLifeTicks - e.LifeTicks) / 2f);
                i++;
            }
        }

        private void AddSprite(Vector3 position, float scale, float rotation, bool isShiny)
        {
            if (_spriteCount >= MaxSprites)
                return;

            _sprites[_spriteCount++] = new SpriteSpec
            {
                Position = position,
                Scale = scale,
                Rotation = rotation,
                IsShiny = isShiny,
            };
        }

        private void SpawnExplosion(Vector3 position)
        {
            if (_explosionCount >= MaxExplosions)
                return; // full pool drops the spawn, as CreateParticle does

            _explosions[_explosionCount++] = new ExplosionParticle
            {
                Position = position,
                LifeTicks = ExplosionLifeTicks,
                Frame = 0,
            };
            SoundController.Instance.PlayBuffer(ExplosionSoundPath);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            _trailBatch.Initialize(GraphicsDevice);
            _lightBatch.Initialize(GraphicsDevice);
            _shinyBatch.Initialize(GraphicsDevice);
            _explosionBatch.Initialize(GraphicsDevice);

            DrawTrails();
            DrawSprites();
            DrawExplosions();
            base.DrawAfter(gameTime);
        }

        private void DrawTrails()
        {
            if (_flareTexture == null)
                return;

            for (int i = 0; i < JointCount; i++)
            {
                ref FlareJoint joint = ref _joints[i];
                if (!joint.Live || joint.Trail.SegmentCount == 0)
                    continue;

                if (!_trailBatch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                    return;

                _trailBatch.AddTrail(
                    joint.Trail,
                    SourceQuadBatch.TrailFaces.Both,
                    JointLight,
                    SourceQuadBatch.TrailShade.Flat,
                    MathF.Max(1f, joint.Trail.MaxTails - 1f));
                _trailBatch.End();
            }
        }

        private void DrawSprites()
        {
            if (_spriteCount == 0)
                return;

            if (_lightTexture != null &&
                _lightBatch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spriteCount; i++)
                {
                    if (!_sprites[i].IsShiny)
                        _lightBatch.AddSprite(_sprites[i].Position, _sprites[i].Scale, SpriteLight, _sprites[i].Rotation);
                }
                _lightBatch.End();
            }

            if (_shinyTexture != null &&
                _shinyBatch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spriteCount; i++)
                {
                    if (_sprites[i].IsShiny)
                        _shinyBatch.AddSprite(_sprites[i].Position, _sprites[i].Scale, SpriteLight, _sprites[i].Rotation);
                }
                _shinyBatch.End();
            }
        }

        private void DrawExplosions()
        {
            if (_explosionTexture == null || _explosionCount == 0)
                return;

            if (!_explosionBatch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            float width = _explosionTexture.Width * ExplosionScale;
            float height = _explosionTexture.Height * ExplosionScale;
            for (int i = 0; i < _explosionCount; i++)
            {
                int frame = _explosions[i].Frame;
                float u = frame % 4 * 0.25f + 0.005f;
                float v = frame / 4 * 0.25f + 0.005f;
                _explosionBatch.AddSpriteRegion(_explosions[i].Position, width, height, Vector3.One, 0f, u, v, 0.24f, 0.24f);
            }
            _explosionBatch.End();
        }

        public override void Dispose()
        {
            _trailBatch.Dispose();
            _lightBatch.Dispose();
            _shinyBatch.Dispose();
            _explosionBatch.Dispose();
            _flareTexture = null;
            _lightTexture = null;
            _shinyTexture = null;
            _explosionTexture = null;
            base.Dispose();
        }
    }
}
