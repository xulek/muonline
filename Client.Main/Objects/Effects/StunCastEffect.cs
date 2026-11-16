#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Stun (AT_SKILL_STUN = 67), SourceMain5.2:
    /// packet   WSclient.cpp:4218       PLAYER_SKILL_VITALITY (ride / Fenrir variants)
    /// gate     ZzzCharacter.cpp:2753   case without a release: 14 ticks
    /// release  ZzzCharacter.cpp:4048   CreateJoint(BITMAP_FLASH sub 7), SOUND_BMS_STUN (loaded only in Battle Castle,
    ///          MapManager.cpp:278)
    /// flash 7  ZzzEffectJoint.cpp:2607 3 x BITMAP_FLASH sub 6 at rot((i - 1) * 100, -50, 0), scale 40
    /// flash 6  ZzzEffectJoint.cpp:2588 (MaxTails 30, LifeTime 25, Velocity (70 + rand%3) deg, Light (0.8,0.8,1),
    ///          Direction (0, -(40 + rand%4), sin * 10), Y -= 20, Z += 130, CreateTailAxis 1, m_byReverseUV 3),
    ///          6722 (Direction y += (rand%5 - 2)F, z -= 10F, first 10 ticks: BITMAP_EXPLOTION+1 at the start (3) and
    ///          Scale 100, ground: JOINT_THUNDER sub 4 (60) + ADV_SMOKE+1 sub 2 at +30, bounce sin * 20, Z = ground + 5,
    ///          Light / 1.3^F below Weapon / 2)
    /// explo+1  ZzzEffectParticle.cpp:2511 / 4308 (LifeTime 12, Frame (12 - LifeTime) / 3, 4x1 strip, SOUND_EXPLOTION01)
    /// smoke    ZzzEffectParticle.cpp:3356 (falls into TRUE_FIRE: LifeTime 24, Scale 0.5), 8280, 9060 (GL_ADD + blend 3)
    /// </summary>
    internal sealed class StunCastEffect : SourceSkillEffectBase
    {
        private const string FlashTexturePath = "Effect/Flashing.jpg";       // BITMAP_FLASH
        private const string ExplosionTexturePath = "Effect/DinoE.jpg";      // BITMAP_EXPLOTION + 1 (4x1)
        private const string SmokeTexturePath = "Effect/fi02.tga";           // BITMAP_ADV_SMOKE + 1
        private const string ExplosionSound = "Sound/eExplosion.wav";        // SOUND_EXPLOTION01 (1 channel)
        private const string StunSound = "Sound/BattleCastle/sDStun.wav";    // SOUND_BMS_STUN
        private const short BattleCastleWorldIndex = 31;                     // WD_30BATTLECASTLE + 1
        private const int BoltCount = 3;
        private const int BoltMaxTails = 30;

        private struct Bolt
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Angle;
            public Vector3 Direction;
            public Vector3 Light;
            public float Velocity;
            public float Scale;
            public float Weapon;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Bolt[] _bolts = new Bolt[BoltCount];
        private readonly Particle[] _explosions = new Particle[32];
        private readonly Particle[] _smokes = new Particle[96];
        private readonly SourceThunderPool _thunders = new(80);
        private Texture2D? _flashTexture;
        private Texture2D? _explosionTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _thunderTexture;
        private Texture2D? _shinyTexture;

        public StunCastEffect(WalkerObject caster)
            : base(caster, triggerFrame: float.MaxValue, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 80 * 200 * 2 + 512)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _flashTexture = await LoadTexture(FlashTexturePath);
            _explosionTexture = await LoadTexture(ExplosionTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _thunderTexture = await LoadTexture(SourceThunderPool.TexturePath);
            _shinyTexture = await LoadTexture(SourceThunderPool.ShinyTexturePath);
            await SoundController.Instance.PreloadSoundAsync(ExplosionSound);
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            var angle = new Vector3(0f, 0f, CasterYawDegrees);
            int tails = SourceJointMath.ScaledMaxTails(BoltMaxTails);

            for (int i = 0; i < BoltCount; i++)
            {
                Vector3 pos = origin + SourceJointMath.Rotate(new Vector3((i - 1) * 100f, -50f, 0f), angle);
                float velocity = (70 + MuGame.Random.Next(3)) * MathF.PI / 180f;
                pos.Y -= 20f;
                pos.Z += 130f;
                ref Bolt b = ref _bolts[i];
                b = new Bolt
                {
                    Live = true,
                    Position = pos,
                    StartPosition = pos,
                    Angle = angle,
                    Direction = new Vector3(0f, -(40f + MuGame.Random.Next(4)), MathF.Sin(velocity) * 10f),
                    Light = new Vector3(0.8f, 0.8f, 1f),
                    Velocity = velocity,
                    Scale = 40f,
                    Weapon = 25f,
                    LifeTime = 25f,
                    Trail = new SourceTrail(tails),
                };
                b.Trail.PushAxisY(b.Position, b.Angle, b.Scale);
            }

            if (World?.WorldIndex == BattleCastleWorldIndex)
                SoundController.Instance.PlayBuffer(StunSound);
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt b = ref _bolts[i];
                if (!b.Live)
                    continue;

                b.Direction.Y += (MuGame.Random.Next(5) - 2) * factor;
                b.Direction.Z -= 10f * factor;
                b.Position += SourceJointMath.Rotate(b.Direction, b.Angle) * factor;

                if (b.Weapon - b.LifeTime < 10f)
                {
                    if (FpsCheck(1, factor))
                        SpawnExplosion(b.StartPosition, b.Angle, b.Light);
                    b.Scale = 100f;
                }
                else
                {
                    b.Scale = 40f;
                }

                float height = TerrainHeight(b.Position);
                if (b.Position.Z <= height)
                {
                    Vector3 hit = b.Position;
                    if (FpsCheck(1, factor))
                        _thunders.Spawn(b.Position, b.Position, b.Angle, 4, 60f, 20f, 60f, 10, new Vector3(0.5f, 0.5f, 1f));
                    if (b.Direction.Z <= 0f)
                        b.Direction.Z = MathF.Sin(b.Velocity) * 20f;
                    b.Position.Z = height + 5f;
                    hit.Z = b.Position.Z + 30f;
                    if (FpsCheck(1, factor))
                        SpawnSmoke(hit, b.Angle);
                }

                b.Trail.PushAxisY(b.Position, b.Angle, b.Scale);

                if (b.LifeTime < b.Weapon / 2f)
                    b.Light *= MathF.Pow(1f / 1.3f, factor);

                b.LifeTime -= factor;
                if (b.LifeTime < 0f)
                    b.Live = false;
            }

            _thunders.Move(factor);

            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor; // MovePosition
                p.Position += p.Velocity * factor;
                float decay = MathF.Pow(0.95f, factor);
                p.Velocity.X *= decay;
                p.Velocity.Y *= decay;
                p.Velocity.Z += 0.6f * factor;
                p.Position += new Vector3(MuGame.Random.Next(4) - 2, MuGame.Random.Next(4) - 2, (MuGame.Random.Next(4) - 2) * 0.8f) * factor;
                p.Scale += factor * 0.05f;
                p.Light = new Vector3(p.LifeTime / 25f * 2f - 1f);
                p.Rotation += (1f + MuGame.Random.Next(2)) * factor;
            }
        }

        private void SpawnExplosion(Vector3 position, Vector3 angle, Vector3 light)
        {
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Particle p = ref _explosions[i];
                if (p.Live)
                    continue;
                p = new Particle { Live = true, Position = position, Angle = angle, Light = light, Scale = 3f, LifeTime = 12f };
                SoundController.Instance.PlayBufferSingleChannel(ExplosionSound);
                return;
            }
        }

        private void SpawnSmoke(Vector3 position, Vector3 angle)
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
                    Angle = angle,
                    Velocity = new Vector3((MuGame.Random.Next(10) - 5) * 0.4f, 0f, (MuGame.Random.Next(10) + 5) * 0.2f),
                    Scale = 0.5f,
                    Rotation = MuGame.Random.Next(360),
                    LifeTime = 24f,
                };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_thunders.HasLive)
                return true;
            for (int i = 0; i < _bolts.Length; i++) if (_bolts[i].Live) return true;
            for (int i = 0; i < _explosions.Length; i++) if (_explosions[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_flashTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _bolts.Length; i++)
                {
                    ref Bolt b = ref _bolts[i];
                    if (b.Live)
                        Batch.AddTrail(b.Trail, SourceQuadBatch.TrailFaces.Both, b.Light, SourceQuadBatch.TrailShade.Flat,
                            b.Trail.MaxTails - 1, reverseUV: 3);
                }
                Batch.End();
            }

            _thunders.Draw(Batch, _thunderTexture);
            _thunders.DrawSprites(Batch, _shinyTexture);

            if (_explosionTexture != null &&
                Batch.Begin(_explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _explosions.Length; i++)
                {
                    ref Particle p = ref _explosions[i];
                    if (!p.Live)
                        continue;
                    int frame = (int)((12f - p.LifeTime) / 3f);
                    Batch.AddSpriteRegion(p.Position, _explosionTexture.Width * p.Scale * 0.25f, _explosionTexture.Height * p.Scale,
                        p.Light, p.Angle.X, frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            // GL_ADD + EnableAlphaBlend3: one batched tex * a pass, then +/- Light * a per particle.
            if (Batch.Begin(_smokeTexture, Blendings.Alpha, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Particle p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, Vector3.One, p.Rotation);
                }
                Batch.End();
            }
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (p.Live)
                    Batch.DrawSpriteTexEnvAddLight(_smokeTexture, p.Position, p.Scale, p.Light.X, p.Rotation);
            }
        }
    }
}
