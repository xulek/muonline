#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    public enum SourceProjectileKind
    {
        /// <summary>BITMAP_BOSS_LASER (Hydra boss ring): blue spark trail, scale 16, life 20.</summary>
        BossLaser,
        /// <summary>BITMAP_BOSS_LASER+1: orange-red variant.</summary>
        BossLaserOrange,
        /// <summary>MODEL_STAFF_OF_DESTRUCTION: purple orb, +280Z launch hop, spin,
        /// terrain impact burst with stones (DeathBeamKnight fan).</summary>
        StaffOfDestruction,
        /// <summary>MODEL_FIRE sub2: falling fireball cone shot (Red Dragon).</summary>
        FireCone
    }

    /// <summary>
    /// Port of the SourceMain5.2 sprite/model projectile effects used by monster
    /// attacks. Movement mirrors MoveEffect: Direction rotated by Angle each frame
    /// plus per-kind gravity/terrain rules; rendering mirrors the sprite trails.
    /// </summary>
    public sealed class SourceProjectileEffect : EffectObject
    {
        private const float TrailSpriteCount = 20f;

        private Texture2D? _trailTexture;
        private readonly SourceProjectileKind _kind;

        public Vector3 Direction { get; set; }
        public float ScaleValue { get; set; }
        public float LifeTimeFrames { get; set; }

        private float _gravity;
        private Vector3 _angle;
        private Vector3 _light = Vector3.One;

        // Dynamic light: only the first projectile spawned in a frame is lit; later
        // same-frame siblings just bump its counter so radial fans (Hydra 9x,
        // DeathBeamKnight 18x) get one origin flash instead of a light per shot.
        private const int BurstSiblingThreshold = 3;
        private const float BurstFlashSeconds = 0.5f;
        private const float ImpactFlashSeconds = 0.4f;
        private static SourceProjectileEffect? s_frameLead;
        private static int s_frameLeadId = -1;
        private EffectLights? _lights;
        private Vector3 _lightColor;
        private Vector3 _origin;
        private int _siblings;
        private float _ageFrames;
        private bool _ended;
        private bool _impact;
        private float _endFrames;
        private Vector3 _impactPosition;

        private SourceProjectileEffect(SourceProjectileKind kind)
        {
            _kind = kind;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-3000f, -3000f, -2000f),
                new Vector3(3000f, 3000f, 4000f));
        }

        public static SourceProjectileEffect Create(SourceProjectileKind kind, Vector3 position, Vector3 angleDeg)
        {
            var e = new SourceProjectileEffect(kind)
            {
                Position = position,
                _angle = angleDeg
            };

            switch (kind)
            {
                case SourceProjectileKind.BossLaser:
                case SourceProjectileKind.BossLaserOrange:
                    // CreateEffect BITMAP_BOSS_LASER(+1): LifeTime 20, p1=(0,-50,0)
                    // rotated into Direction; +1 tint (1,0.4,0.2), base (0.5,0.7,1).
                    e.LifeTimeFrames = 20f;
                    e.ScaleValue = 16f;
                    e.Direction = SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), angleDeg);
                    e._light = kind == SourceProjectileKind.BossLaser
                        ? new Vector3(0.5f, 0.7f, 1f)
                        : new Vector3(1f, 0.4f, 0.2f);
                    break;

                case SourceProjectileKind.StaffOfDestruction:
                    // MODEL_STAFF_OF_DESTRUCTION init: LifeTime 30, +280Z launch hop
                    // applied in Update, Angle.X spin 20/f, Direction (0,-80,-10).
                    e.LifeTimeFrames = 30f;
                    e.ScaleValue = 1f;
                    e.Direction = new Vector3(0f, -80f, -10f);
                    break;

                case SourceProjectileKind.FireCone:
                    // MODEL_FIRE sub2 init: LifeTime 40, Scale rand(1.0..1.7),
                    // Direction (0,0,-50).
                    e.LifeTimeFrames = 40f;
                    e.ScaleValue = (MuGame.Random.Next(8) + 10) * 0.1f;
                    e.Direction = new Vector3(0f, 0f, -50f);
                    break;
            }

            e._origin = position;
            e._lightColor = kind switch
            {
                SourceProjectileKind.StaffOfDestruction => new Vector3(0.65f, 0.3f, 1f),
                SourceProjectileKind.FireCone => new Vector3(1f, 0.45f, 0.15f),
                _ => e._light
            };

            int frame = MuGame.FrameIndex;
            if (s_frameLead == null || s_frameLeadId != frame)
            {
                s_frameLeadId = frame;
                s_frameLead = e;
                e._lights = new EffectLights(e, 1);
            }
            else
            {
                s_frameLead._siblings++;
            }

            return e;
        }

        public override async Task Load()        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            string texturePath = _kind switch
            {
                SourceProjectileKind.BossLaser or SourceProjectileKind.BossLaserOrange => "Effect/Spark02.jpg",
                SourceProjectileKind.StaffOfDestruction => "Effect/JointEnergy01.jpg",
                _ => "Effect/Fire01.jpg"
            };
            _trailTexture = await TextureLoader.Instance.PrepareAndGetTexture(texturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float f = SourceJointMath.FrameFactor;
            _ageFrames += f;

            if (_ended)
            {
                _endFrames += f;
                if (!UpdateLight())
                {
                    World?.RemoveObject(this);
                    Dispose();
                }
                return;
            }

            // Generic flyer motion (MoveEffect): rotate Direction by Angle each frame;
            // Direction is per-frame units, damped 0.9^factor.
            Position += SourceJointMath.Rotate(Direction, _angle) * f;
            Direction *= MathF.Pow(0.9f, f);

            if (_kind == SourceProjectileKind.StaffOfDestruction)
            {
                // Launch hop + nose spin from the init case.
                Position += Vector3.UnitZ * (280f * f);
                _angle.X += 20f * f;
            }

            LifeTimeFrames -= f;

            bool dead = LifeTimeFrames <= 0f;
            bool terrainHit = false;

            if (_kind == SourceProjectileKind.StaffOfDestruction && World?.Terrain != null)
            {
                float height = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);
                if (Position.Z < height)
                    terrainHit = true;
            }
            else if (_kind == SourceProjectileKind.FireCone && Position.Z < -200f)
            {
                terrainHit = true;
            }

            if (dead || terrainHit)
            {
                // Lit instance lingers (invisible, motionless) until its light has faded.
                if (_lights != null)
                {
                    _ended = true;
                    _impact = terrainHit && _siblings < BurstSiblingThreshold;
                    if (_impact)
                    {
                        float ground = World?.Terrain != null
                            ? World.Terrain.RequestTerrainHeight(Position.X, Position.Y)
                            : Position.Z;
                        _impactPosition = new Vector3(Position.X, Position.Y, ground + 50f);
                    }
                    if (UpdateLight())
                        return;
                }

                World?.RemoveObject(this);
                Dispose();
                return;
            }

            UpdateLight();
        }

        /// <summary>Drives the single light slot; returns false once it has nothing left to show.</summary>
        private bool UpdateLight()
        {
            if (_lights == null)
                return false;

            float ageS = _ageFrames / 25f;
            if (_siblings >= BurstSiblingThreshold)
            {
                // Radial fan: one flash at the launch point.
                if (ageS >= BurstFlashSeconds)
                {
                    _lights.Hide(0);
                    return !_ended;
                }
                float burst = 1.2f * EffectLights.Flash(ageS, BurstFlashSeconds) * MathHelper.Clamp(ageS / 0.06f, 0f, 1f);
                _lights.Set(0, _origin + Vector3.UnitZ * 60f, _lightColor, 380f, burst);
                return true;
            }

            if (_ended)
            {
                if (!_impact)
                    return false;
                float endS = _endFrames / 25f;
                if (endS >= ImpactFlashSeconds)
                {
                    _lights.Hide(0);
                    return false;
                }
                float k = endS / ImpactFlashSeconds;
                _lights.Set(0, _impactPosition, _lightColor, MathHelper.Lerp(300f, 200f, k),
                    1.2f * EffectLights.Flash(endS, ImpactFlashSeconds));
                return true;
            }

            // Single shot: light rides the projectile head, fading out with its life.
            float head = MathHelper.Clamp(ageS / 0.08f, 0f, 1f) * MathHelper.Clamp(LifeTimeFrames / 6f, 0f, 1f);
            _lights.Set(0, Position, _lightColor, 220f, 1.0f * head);
            return true;
        }

        public override void Dispose()
        {
            _lights?.Dispose();
            if (ReferenceEquals(s_frameLead, this))
                s_frameLead = null;
            base.Dispose();
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (Hidden || _ended || Status != GameControlStatus.Ready || _trailTexture == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            bool ownBatch = !SpriteBatchScope.BatchIsBegun;
            if (ownBatch)
            {
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.DepthRead, RasterizerState.CullNone))
                    DrawTrail(gameTime, spriteBatch);
            }
            else
                DrawTrail(gameTime, spriteBatch);

            base.DrawAfter(gameTime);
        }

        private void DrawTrail(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var camera = Camera.Instance;
            var viewport = GraphicsDevice.Viewport;
            if (camera == null)
                return;

            // RenderEffect BOSS_LASER family: 20 sprites marching along Direction.
            Vector3 step = Direction;
            Vector3 trailPos = Position;

            Color tint = new Color(_light.X, _light.Y, _light.Z, 1f);

            for (int j = 0; j < TrailSpriteCount; j++)
            {
                Vector3 p = viewport.Project(trailPos, camera.Projection, camera.View, Matrix.Identity);
                if (p.Z >= 0f && p.Z <= 1f)
                {
                    float distance = Vector3.Distance(camera.Position, trailPos);
                    float screenScale = ScaleValue *
                        (1f / (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE)) *
                        Constants.RENDER_SCALE * 0.35f;

                    spriteBatch.Draw(
                        _trailTexture,
                        new Vector2(p.X, p.Y),
                        null,
                        tint,
                        0f,
                        new Vector2(_trailTexture.Width * 0.5f, _trailTexture.Height * 0.5f),
                        screenScale,
                        SpriteEffects.None,
                        0.45f);
                }

                trailPos += step * 0.35f; // compressed spacing keeps the bolt readable
            }
        }
    }
}
