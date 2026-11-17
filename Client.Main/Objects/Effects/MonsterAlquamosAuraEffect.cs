#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of the SourceMain5.2 RenderCharacter MONSTER_ALQUAMOS branch (ZzzCharacter.cpp:8602-8623), evaluated every
    /// rendered frame. Sources:
    ///   Luminosity   (rand() % 30 + 70) * 0.01, drawn per frame                     ZzzCharacter.cpp:8604
    ///   sprites      9x CreateSprite(BITMAP_LIGHT, bone g_chStar[i], 0.6, (0.8L, 0.9L, 1.0L), rotation 0, sub 0)
    ///                i &lt; 9, g_chStar = {10,18,37,38,51,52,58,59,66,24}           ZzzCharacter.cpp:95-98, 8607-8611
    ///                BITMAP_LIGHT = Effect\flare01.jpg, GL_CLAMP                   ZzzOpenData.cpp:4966
    ///                RenderSprite: world size = texture * scale, billboard, additive   zzzeffectsprite.cpp:20-117, ZzzOpenglUtil.cpp:1005
    ///   particles    if rand_fps_check(1) (chance min(1, factor), ZzzAI.cpp:805):   ZzzCharacter.cpp:8615
    ///                3x: offset rand() % 20 - 10 per axis, bone rand() % NumBones,
    ///                CreateParticle(BITMAP_SPARK+1, pos, o->Angle, L * (0.6, 0.7, 0.8), sub 3)   ZzzCharacter.cpp:8617-8621
    ///                sub 3 init: LifeTime = 5, Scale = 1 (default), Velocity = 0      ZzzEffect.h:33, ZzzEffectParticle.cpp:2149-2151
    ///                move: LifeTime -= factor (dead at &lt;= 0), Scale *= powf(0.8, factor)  ZzzEffectParticle.cpp:3901, 6664-6666
    ///                render: default RenderSprite, JPG -> EnableAlphaBlend (additive)   ZzzEffectParticle.cpp:8954, 9297
    ///                BITMAP_SPARK+1 = Effect\Spark03.jpg, GL_CLAMP                   ZzzOpenData.cpp:4975
    ///   order        move (Update) -> sprites, spawn -> particles (ZzzScene: sprites before particles)
    /// The plain Alquamos attack has no visual in the original: AttackEffect case MONSTER_ALQUAMOS is empty
    /// The 4x BITMAP_FLARE joints (ZzzCharacter.cpp:1979-1990) belong to the AT_SKILL_ENERGYBALL cast and are
    /// ported in MonsterAlquamosFlareEffect, started from OnEnergyBallCast.
    /// </summary>
    public sealed class MonsterAlquamosAuraEffect : EffectObject
    {
        private const string LightTexturePath = "Effect/flare01.jpg"; // BITMAP_LIGHT
        private const string SparkTexturePath = "Effect/Spark03.jpg"; // BITMAP_SPARK + 1

        private static readonly int[] StarBones = { 10, 18, 37, 38, 51, 52, 58, 59, 66 };
        private const float LightSpriteScale = 0.6f;
        private const int ParticlesPerCheck = 3;
        private const float ParticleLifeTicks = 5f;
        private const float ParticleScaleDecay = 0.8f;
        private const int ParticleOffsetRange = 20;  // rand() % 20 - 10 -> [-10, 9]
        private const int ParticleOffsetMin = -10;
        private const int MaxParticles = 64;         // live count is about 3 x 5 = 15; full pool drops the spawn

        private readonly SourceQuadBatch _lightBatch = new(StarBones.Length);
        private readonly SourceQuadBatch _sparkBatch = new(MaxParticles);
        private readonly SparkParticle[] _particles = new SparkParticle[MaxParticles];
        private int _particleCount;
        private Texture2D? _lightTexture;
        private Texture2D? _sparkTexture;

        private struct SparkParticle
        {
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float LifeTicks;
        }

        public MonsterAlquamosAuraEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task Load()
        {
            await base.Load();
            _lightTexture = await TextureLoader.Instance.PrepareAndGetTexture(LightTexturePath);
            _sparkTexture = await TextureLoader.Instance.PrepareAndGetTexture(SparkTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // MoveParticles: LifeTime -= factor, dead at <= 0; BITMAP_SPARK+1 sub 3: Scale *= powf(0.8, factor).
            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            float scaleStep = MathF.Pow(ParticleScaleDecay, factor);
            for (int i = 0; i < _particleCount;)
            {
                ref SparkParticle p = ref _particles[i];
                p.LifeTicks -= factor;
                if (p.LifeTicks <= 0f)
                {
                    // Swap-remove: order does not matter for additive blending.
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                p.Scale *= scaleStep;
                i++;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            _lightBatch.Initialize(GraphicsDevice);
            _sparkBatch.Initialize(GraphicsDevice);

            if (Parent is ModelObject parent && parent.Model != null && !parent.Hidden &&
                parent is not MonsterObject { IsDead: true } &&
                parent.GetBoneTransforms() is { Length: > 0 } bones)
            {
                float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;

                DrawLightSprites(parent, bones, luminosity);

                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnSparkParticles(parent, bones, luminosity);
            }

            DrawSparkParticles();
            base.DrawAfter(gameTime);
        }

        private void DrawLightSprites(ModelObject parent, Matrix[] bones, float luminosity)
        {
            if (_lightTexture == null ||
                !_lightBatch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            var light = new Vector3(luminosity * 0.8f, luminosity * 0.9f, luminosity * 1f);
            for (int i = 0; i < StarBones.Length; i++)
            {
                int bone = StarBones[i];
                if (bone >= bones.Length)
                    continue;

                // TransformPosition(BoneTransform[g_chStar[i]], p = (0,0,0)) = bone origin.
                Vector3 center = Vector3.Transform(bones[bone].Translation, parent.WorldPosition);
                _lightBatch.AddSprite(center, LightSpriteScale, light);
            }

            _lightBatch.End();
        }

        private void SpawnSparkParticles(ModelObject parent, Matrix[] bones, float luminosity)
        {
            var light = new Vector3(luminosity * 0.6f, luminosity * 0.7f, luminosity * 0.8f);
            for (int i = 0; i < ParticlesPerCheck; i++)
            {
                if (_particleCount >= MaxParticles)
                    return;

                var offset = new Vector3(
                    MuGame.Random.Next(ParticleOffsetRange) + ParticleOffsetMin,
                    MuGame.Random.Next(ParticleOffsetRange) + ParticleOffsetMin,
                    MuGame.Random.Next(ParticleOffsetRange) + ParticleOffsetMin);
                int bone = MuGame.Random.Next(bones.Length);

                Vector3 local = Vector3.Transform(offset, bones[bone]);
                _particles[_particleCount++] = new SparkParticle
                {
                    Position = Vector3.Transform(local, parent.WorldPosition),
                    Light = light,
                    Scale = 1f,
                    LifeTicks = ParticleLifeTicks,
                };
            }
        }

        private void DrawSparkParticles()
        {
            if (_particleCount == 0 || _sparkTexture == null ||
                !_sparkBatch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _particleCount; i++)
                _sparkBatch.AddSprite(_particles[i].Position, _particles[i].Scale, _particles[i].Light);

            _sparkBatch.End();
        }

        public override void Dispose()
        {
            _lightBatch.Dispose();
            _sparkBatch.Dispose();
            _lightTexture = null;
            _sparkTexture = null;
            base.Dispose();
        }
    }
}
