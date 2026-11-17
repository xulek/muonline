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
    /// Weapon glow of MODEL_CRUST monsters (Mega Crust, SourceMain5.2 RenderCharacter weapon loop, ZzzCharacter.cpp:9885-10100),
    /// evaluated at render time every frame, as in the original:
    ///   Weapon[0] MODEL_THUNDER_BLADE, LinkBone 36 (ZzzCharacter.cpp:11751), Level 5:
    ///     scale S = sin(WorldTime * 0.004) * 0.3 + 0.3                          10055
    ///     CreateSprite(BITMAP_SHINY+1 = Effect/Shiny02.jpg, p = bone36 * (0,-20,15), S + 1, colour S * (0.2, 0.2, 1))   10056-10057
    ///     target = bone36 * (0,-133,7), width = S * 20 + 20                       10059-10062
    ///     3x CreateJoint(BITMAP_JOINT_THUNDER, p, target, Angle, sub 10, NULL, width)   10063-10065
    ///       sub 10 init (ZEJ 1148-1185): bTileMapping, MaxTails 10, Light (0.3, 0.3, 1), Angle 0 after the start tail,
    ///       step SP = (target - p) / MaxTails, 9 tails at P + SP + jitter (X/Y rand % 8 - 4, Z from SP)
    ///       rendered this frame; the joint moves and dies on the next tick, so only the init shape is visible.
    ///       Texture Effect/JointThunder01.jpg, GL_REPEAT (ZzzOpenData.cpp), additive.
    ///   Weapon[1] MODEL_LEGENDARY_SHIELD, LinkBone 45 (ZzzCharacter.cpp:11752), Level 0:
    ///     CreateSprite(BITMAP_SHINY+1, bone45 * (20,0,0), 1.5, L * (0.4, 0.6, 1.5))   10089-10092, L = (rand % 30 + 70) * 0.01 (9799)
    /// Drawn joints first, then sprites (RenderJoints before RenderSprites). Start tail of each bolt uses the monster angle,
    /// its width uses the bolt width (the original start tail reads a stale o->Scale).
    /// </summary>
    public sealed class MonsterMegaCrustGlowEffect : EffectObject
    {
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";          // BITMAP_SHINY + 1
        private const string ThunderTexturePath = "Effect/JointThunder01.jpg"; // BITMAP_JOINT_THUNDER

        private const int BladeLinkBone = 36;
        private const int ShieldLinkBone = 45;
        private const int BoltCount = 3;
        private const int BoltMaxTails = 10;
        private const int MaxSprites = 2;
        private static readonly Vector3 BoltLight = new(0.3f, 0.3f, 1f);

        private struct SpriteSpec
        {
            public Vector3 Position;
            public float Scale;
            public Vector3 Light;
        }

        private readonly SpriteSpec[] _sprites = new SpriteSpec[MaxSprites];
        private readonly SourceTrail[] _bolts = new SourceTrail[BoltCount];
        private readonly SourceQuadBatch _shinyBatch = new(MaxSprites);
        private readonly SourceQuadBatch _thunderBatch = new(BoltCount * 2 * BoltMaxTails);
        private int _spriteCount;
        private int _boltCount;
        private Texture2D? _shinyTexture;
        private Texture2D? _thunderTexture;

        public MonsterMegaCrustGlowEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-300f, -300f, -100f), new Vector3(300f, 300f, 300f));
            for (int i = 0; i < BoltCount; i++)
                _bolts[i] = new SourceTrail(BoltMaxTails);
        }

        public override async Task Load()
        {
            await base.Load();
            _shinyTexture = await TextureLoader.Instance.PrepareAndGetTexture(ShinyTexturePath);
            _thunderTexture = await TextureLoader.Instance.PrepareAndGetTexture(ThunderTexturePath);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            _shinyBatch.Initialize(GraphicsDevice);
            _thunderBatch.Initialize(GraphicsDevice);

            if (Parent is ModelObject parent && parent.Model != null && !parent.Hidden &&
                parent is not MonsterObject { IsDead: true } &&
                parent.GetBoneTransforms() is { Length: > 0 } bones)
            {
                _spriteCount = 0;
                _boltCount = 0;
                BuildBladeGlow(parent, bones);
                BuildShieldGlow(parent, bones);
                DrawBolts();
                DrawSprites();
            }

            base.DrawAfter(gameTime);
        }

        private static Vector3 BonePoint(ModelObject parent, Matrix[] bones, int bone, Vector3 local)
        {
            return Vector3.Transform(Vector3.Transform(local, bones[bone]), parent.WorldPosition);
        }

        private void BuildBladeGlow(ModelObject parent, Matrix[] bones)
        {
            if (BladeLinkBone >= bones.Length)
                return;

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            float worldTime = (float)FPSCounter.Instance.WorldTime;
            float s = MathF.Sin(worldTime * 0.004f) * 0.3f + 0.3f;

            Vector3 start = BonePoint(parent, bones, BladeLinkBone, new Vector3(0f, -20f, 15f));
            Vector3 target = BonePoint(parent, bones, BladeLinkBone, new Vector3(0f, -133f, 7f));
            AddSprite(start, s + 1f, new Vector3(s * 0.2f, s * 0.2f, s * 1f));

            float width = s * 20f + 20f;
            float yawDegrees = MathHelper.ToDegrees(parent.Angle.Z);
            for (int i = 0; i < BoltCount; i++)
                BuildBolt(start, target, width, factor, yawDegrees);
        }

        // CreateJoint(BITMAP_JOINT_THUNDER, p, target, Angle, 10): start tail, then the sub 10 init loop.
        private void BuildBolt(Vector3 start, Vector3 target, float width, float factor, float yawDegrees)
        {
            if (_boltCount >= BoltCount)
                return;

            SourceTrail trail = _bolts[_boltCount++];
            trail.Reset(BoltMaxTails);
            trail.Push(start, new Vector3(0f, 0f, yawDegrees), width);

            Vector3 step = (target - start) / BoltMaxTails;
            Vector3 cursor = start;
            for (int i = 0; i < BoltMaxTails - 1; i++)
            {
                float jitterX = MuGame.Random.Next(8) - 4f;
                float jitterY = MuGame.Random.Next(8) - 4f;
                var local = new Vector3(step.X + jitterX, step.Y + jitterY, step.Z);
                cursor += local * factor;
                trail.Push(cursor, Vector3.Zero, width);
                cursor = cursor - local + step;
            }
        }

        private void BuildShieldGlow(ModelObject parent, Matrix[] bones)
        {
            if (ShieldLinkBone >= bones.Length)
                return;

            float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;
            Vector3 center = BonePoint(parent, bones, ShieldLinkBone, new Vector3(20f, 0f, 0f));
            AddSprite(center, 1.5f, new Vector3(luminosity * 0.4f, luminosity * 0.6f, luminosity * 1.5f));
        }

        private void AddSprite(Vector3 position, float scale, Vector3 light)
        {
            if (_spriteCount >= MaxSprites)
                return;

            _sprites[_spriteCount++] = new SpriteSpec { Position = position, Scale = scale, Light = light };
        }

        private void DrawBolts()
        {
            if (_thunderTexture == null || _boltCount == 0)
                return;

            if (!_thunderBatch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
                return;

            // bTileMapping thunder U (ZEJ 7023-7060), same mapping as DeathStabEffect.
            float factor = SourceJointMath.FrameFactor;
            float pow2 = MathF.Pow(2f, factor);
            float denominator = 16f / (pow2 * pow2);
            float scroll = (float)((int)FPSCounter.Instance.WorldTime % 1000) * 0.001f;
            float uScroll = 2f * scroll * pow2;
            for (int i = 0; i < _boltCount; i++)
            {
                _thunderBatch.AddTrail(
                    _bolts[i],
                    SourceQuadBatch.TrailFaces.Both,
                    BoltLight,
                    SourceQuadBatch.TrailShade.Flat,
                    denominator,
                    uScroll,
                    false,
                    uScroll);
            }
            _thunderBatch.End();
        }

        private void DrawSprites()
        {
            if (_shinyTexture == null || _spriteCount == 0)
                return;

            if (!_shinyBatch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _spriteCount; i++)
                _shinyBatch.AddSprite(_sprites[i].Position, _sprites[i].Scale, _sprites[i].Light);
            _shinyBatch.End();
        }

        public override void Dispose()
        {
            _shinyBatch.Dispose();
            _thunderBatch.Dispose();
            _shinyTexture = null;
            _thunderTexture = null;
            base.Dispose();
        }
    }
}
