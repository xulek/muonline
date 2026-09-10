#nullable enable
using System;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Faithful port of the SourceMain5.2 JOINT primitive (ZzzEffectJoint.cpp) for the
    /// families used by monster attacks: JOINT_SPIRIT, JOINT_THUNDER, JOINT_LASER+1 and
    /// BITMAP_FLARE. One instance mirrors one C++ JOINT object: forward flight along
    /// Angle, MoveHumming homing, tail-ribbon history, frame-based lifetime.
    /// </summary>
    public sealed class SourceJointEffect : EffectObject
    {
        public enum JointFamily { Spirit, Thunder, Laser1, Flare }

        private const int MaxRingCapacity = 50;

        private Texture2D? _texture;
        private readonly Vector3[][] _rings = new Vector3[MaxRingCapacity][];
        private int _ringHead;
        private int _ringCount;

        public JointFamily Family { get; set; }
        public int SubType { get; set; }

        public Func<Vector3>? TargetProvider { get; set; }
        public Vector3 TargetPosition { get; set; }
        public float Velocity { get; set; }
        public float LifeTimeFrames { get; set; }
        public float ScaleValue { get; set; } = 10f;
        public int MaxTails { get; set; }
        public Vector3 LightTint { get; set; } = Vector3.One;
        public string JointTexturePath { get; set; } = "Effect/JointThunder01.jpg";

        // C++ working state
        private Vector3 _directionWobble;
        private Vector3 _startPosition;
        private bool _collision;
        private float _luminosityBoost;

        private SourceJointEffect()
        {
            for (int i = 0; i < MaxRingCapacity; i++)
                _rings[i] = new Vector3[4];

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-5000f, -5000f, -2000f),
                new Vector3(5000f, 5000f, 4000f));
        }

        // ---- factories mirroring CreateJoint call sites -----------------------

        /// <summary>BITMAP_JOINT_SPIRIT sub1 with NULL target: radial burst streaks
        /// (Phantom Knight 36x, scale 60, Velocity 70, LifeTime 49).</summary>
        public static SourceJointEffect SpiritBurst(Vector3 origin, float yawDeg, float pitchDeg, float scale)
        {
            return new SourceJointEffect
            {
                Family = JointFamily.Spirit,
                SubType = 1,
                Position = origin,
                TargetPosition = origin,
                Velocity = 70f,
                LifeTimeFrames = 49f,
                ScaleValue = scale,
                MaxTails = 6,
                JointTexturePath = "Effect/JointSpirit01.jpg",
                _angle = new Vector3(pitchDeg, 0f, yawDeg),
                _startPosition = origin
            };
        }

        /// <summary>JOINT_SPIRIT sub3 / SPIRIT2: fast homing spirit (Velocity 140,
        /// LifeTime 49, MaxTails 10); SPIRIT2 renders white, SPIRIT orange.</summary>
        public static SourceJointEffect SpiritHoming(Func<Vector3> originProvider, Func<Vector3> targetProvider, bool whiteSpirit, float scale)
        {
            var joint = new SourceJointEffect
            {
                Family = JointFamily.Spirit,
                SubType = 3,
                TargetProvider = targetProvider,
                Velocity = 140f,
                LifeTimeFrames = 49f,
                ScaleValue = scale,
                MaxTails = 10,
                JointTexturePath = "Effect/JointSpirit01.jpg",
                _originProvider = originProvider,
                LightTint = whiteSpirit ? Vector3.One : new Vector3(1f, 0.5f, 0.1f)
            };
            return joint;
        }

        /// <summary>JOINT_THUNDER sub2-style homing arc toward a live target
        /// (Velocity 50, jagged tails).</summary>
        public static SourceJointEffect ThunderHoming(Vector3 origin, Func<Vector3> targetProvider, float scale, float lifeFrames = 8f)
        {
            var target = targetProvider();
            return new SourceJointEffect
            {
                Family = JointFamily.Thunder,
                SubType = 2,
                Position = origin,
                TargetPosition = target + Vector3.UnitZ * 80f,
                TargetProvider = targetProvider,
                Velocity = 50f,
                LifeTimeFrames = lifeFrames,
                ScaleValue = scale,
                MaxTails = 50,
                JointTexturePath = "Effect/JointThunder01.jpg",
                LightTint = new Vector3(1f, 0.6f, 0.2f)
            };
        }

        /// <summary>BITMAP_JOINT_LASER+1 swarm beam: per-frame humming at speed 25
        /// with jittered tails (BeamKnight/Devil energy bolts).</summary>
        public static SourceJointEffect LaserSwarm(Vector3 origin, Func<Vector3> targetProvider, float scale, bool redTint)
        {
            return new SourceJointEffect
            {
                Family = JointFamily.Laser1,
                SubType = redTint ? 1 : 0,
                Position = origin,
                TargetPosition = targetProvider() + Vector3.UnitZ * 80f,
                TargetProvider = targetProvider,
                Velocity = 40f,
                LifeTimeFrames = 20f,
                ScaleValue = scale,
                MaxTails = 20,
                JointTexturePath = "Effect/JointLaser01.jpg",
                LightTint = redTint ? new Vector3(1f, 0.35f, 0.35f) : new Vector3(1f, 0.75f, 0.55f)
            };
        }

        private Func<Vector3>? _originProvider;
        private Vector3 _angle = new(0f, 0f, 0f);
        private float _ageFrames;

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            _texture = await TextureLoader.Instance.PrepareAndGetTexture(JointTexturePath);

            // Initial tail ring: cross of +-Scale*0.5 around the position (CreateJoint).
            PushRing(Position);
        }

        private void PushRing(Vector3 center)
        {
            SourceJointMath.AngleBasis(_angle, out var right, out _, out var up);
            float h = ScaleValue * 0.5f;
            var ring = _rings[_ringHead];
            ring[0] = center + right * h;
            ring[1] = center - right * h;
            ring[2] = center + up * h;
            ring[3] = center - up * h;
            _ringHead = (_ringHead + 1) % MaxRingCapacity;
            if (_ringCount < MaxRingCapacity)
                _ringCount++;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float f = SourceJointMath.FrameFactor;
            _ageFrames += f;

            // Live target refresh (C++ copies o->Target->Position each move).
            if (TargetProvider != null)
            {
                TargetPosition = TargetProvider();
                if (Family != JointFamily.Flare)
                    TargetPosition += Vector3.UnitZ * 80f;
            }

            switch (Family)
            {
                case JointFamily.Spirit:
                    MoveSpirit(f);
                    break;
                case JointFamily.Thunder:
                    MoveThunderHoming(f);
                    break;
                case JointFamily.Laser1:
                    MoveLaserSwarm(f);
                    break;
                case JointFamily.Flare:
                    // FLARE sub7 has no movement case: generic flight with Velocity 0
                    // leaves it coiling at the spawn point.
                    break;
            }

            // Generic pre-switch forward flight (skipped when handled above already moved).
            if (Velocity != 0f && Family == JointFamily.Spirit && SubType == 1)
            {
                Vector3 local = new(0f, -Velocity, 0f);
                Position += SourceJointMath.Rotate(local, _angle) * f;
            }

            if (LifeTimeFrames > 0 || Family != JointFamily.Spirit)
                PushRing(Position);

            LifeTimeFrames -= f;
            if (LifeTimeFrames < 0f || _ageFrames > 12f && _ringCount == 0)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private void MoveSpirit(float f)
        {
            if (SubType == 3 && TargetProvider != null)
            {
                // sub3: MoveHumming(10) toward target + wobble, terrain clamps.
                Vector3 posS3 = Position; Vector3 angS3 = _angle;
                SourceJointMath.MoveHumming(ref posS3, ref angS3, TargetPosition, 10f, f);
                Position = posS3; _angle = angS3;
                _directionWobble.X += (MuGame.Random.Next(32) - 16) * 0.2f;
                _directionWobble.Z += (MuGame.Random.Next(32) - 16) * 0.8f;
                _angle.X += _directionWobble.X * f;
                _angle.Z += _directionWobble.Z * f;
                _directionWobble.X *= 0.6f;
                _directionWobble.Z *= 0.8f;

                if (World?.Terrain != null)
                {
                    float height = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);
                    if (Position.Z < height + 100f) { _directionWobble.X = 0f; _angle.X = -5f; }
                    if (Position.Z > height + 400f) { _directionWobble.X = 0f; _angle.X = 5f; }
                }

                Vector3 local = new(0f, -Velocity, 0f);
                Position += SourceJointMath.Rotate(local, _angle) * f;
            }
            else if (SubType == 1)
            {
                // NULL-target burst: straight flight only (generic mover), plus the
                // per-frame BITMAP_LIGHT twinkle from the C++ `else if (1 == o->SubType)`
                // branch — reproduced in Draw as a flickering sprite at the head.
                _luminosityBoost = 0.9f + (float)MuGame.Random.NextDouble() * 0.1f;
            }
        }

        private void MoveThunderHoming(float f)
        {
            if (TargetProvider != null)
            {
                Vector3 posT2 = Position;
                Vector3 angT2 = _angle;
                SourceJointMath.MoveHumming(ref posT2, ref angT2, TargetPosition, 50f, f);
                Position = posT2;
                _angle = angT2;
            }

            Vector3 local = new(0f, -Velocity, 0f);
            Position += SourceJointMath.Rotate(local, _angle) * f;
        }

        private void MoveLaserSwarm(float f)
        {
            // C++ loops j < MaxTails doing humming(25)+jitter per iteration; one
            // iteration per frame keeps the same visual pace without overdraw.
            Vector3 posL1 = Position; Vector3 angL1 = _angle;
            SourceJointMath.MoveHumming(ref posL1, ref angL1, TargetPosition, 25f, f);
            Position = posL1; _angle = angL1;
            _directionWobble.X += f * (MuGame.Random.Next(256) - 128) / MathF.Max(ScaleValue, 1f);
            _directionWobble.Z += f * (MuGame.Random.Next(256) - 128) / MathF.Max(ScaleValue, 1f);
            _directionWobble *= MathF.Pow(0.8f, f);
            _angle.X += _directionWobble.X;
            _angle.Z += _directionWobble.Z;

            Vector3 local = new(0f, -Velocity, 0f);
            Position += SourceJointMath.Rotate(local, _angle) * f;

            float distance = Vector3.Distance(Position, TargetPosition);
            if (!_collision && distance <= Velocity * 2f * f)
                _collision = true; // arrival flash point (fire particle in C++)
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (Hidden || Status != GameControlStatus.Ready || _texture == null || _ringCount < 2)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            bool ownBatch = !SpriteBatchScope.BatchIsBegun;
            if (ownBatch)
            {
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.DepthRead, RasterizerState.CullNone))
                    DrawRibbon(gameTime, spriteBatch);
            }
            else
                DrawRibbon(gameTime, spriteBatch);

            base.DrawAfter(gameTime);
        }

        private void DrawRibbon(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var camera = Camera.Instance;
            var viewport = GraphicsDevice.Viewport;
            if (camera == null)
                return;

            float scroll = (float)((long)gameTime.TotalGameTime.TotalMilliseconds % 1000L) * 0.001f;
            float fade = MathHelper.Clamp(LifeTimeFrames / 10f, 0f, 1f);

            int oldest = (_ringHead - _ringCount + MaxRingCapacity) % MaxRingCapacity;
            Vector3 prevCenter = (_rings[oldest][0] + _rings[oldest][1]) * 0.5f;

            for (int j = 1; j < _ringCount; j++)
            {
                int idx = (oldest + j) % MaxRingCapacity;
                Vector3 center = (_rings[idx][0] + _rings[idx][1]) * 0.5f;

                DrawSegment(spriteBatch, viewport, camera, prevCenter, center,
                    v0: (j - 1) / (float)Math.Max(MaxTails - 1, 1),
                    v1: j / (float)Math.Max(MaxTails - 1, 1),
                    scroll: scroll,
                    fade: fade);

                prevCenter = center;
            }

            // Head sprite + SPIRIT sub1 twinkle.
            if (Family == JointFamily.Spirit && SubType == 1)
            {
                if (TryProject(Position, viewport, camera, out var sp))
                {
                    float scale = 4f * ScreenScale(Position) * _luminosityBoost;
                    spriteBatch.Draw(
                        _texture,
                        sp,
                        null,
                        Color.White * 0.7f * fade,
                        MuGame.Random.Next(360) * MathHelper.Pi / 180f,
                        new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f),
                        scale,
                        SpriteEffects.None,
                        0.45f);
                }
            }
        }

        private void DrawSegment(SpriteBatch spriteBatch, Viewport viewport, Camera camera,
            Vector3 from, Vector3 to, float v0, float v1, float scroll, float fade)
        {
            Vector3 midpoint = (from + to) * 0.5f;
            if (!TryProject(midpoint, viewport, camera, out var screen))
                return;

            Vector3 fromS = ProjectOrClamp(from, viewport, camera);
            Vector3 toS = ProjectOrClamp(to, viewport, camera);

            float dx = toS.X - fromS.X;
            float dy = toS.Y - fromS.Y;
            float lengthPx = MathF.Sqrt(dx * dx + dy * dy);
            if (lengthPx < 1f)
                return;

            float widthPx = ScaleValue * ScreenScale(midpoint) * 0.9f;
            float angle = MathF.Atan2(dy, dx);

            // V progress along the ribbon with thunder-style scroll (RenderJoints).
            float light1 = v1 * 2f - scroll;
            float light2 = v0 * 2f - scroll;
            _ = light1; _ = light2; // texture sampling is single-frame; tint carries the glow

            var tint = new Color(
                LightTint.X * fade,
                LightTint.Y * fade,
                LightTint.Z * fade,
                fade);

            spriteBatch.Draw(
                _texture,
                screen,
                null,
                tint,
                angle,
                new Vector2(0f, _texture.Height * 0.5f),
                new Vector2(lengthPx / _texture.Width, widthPx / _texture.Height),
                SpriteEffects.None,
                0.45f);
        }

        private static Vector3 ProjectOrClamp(Vector3 worldPos, Viewport viewport, Camera camera)
        {
            Vector3 p = viewport.Project(worldPos, camera.Projection, camera.View, Matrix.Identity);
            if (p.Z < 0f || p.Z > 1f)
                p = new Vector3(-10000f, -10000f, 1f);
            return p;
        }

        private static bool TryProject(Vector3 worldPos, Viewport viewport, Camera camera, out Vector2 screen)
        {
            Vector3 p = viewport.Project(worldPos, camera.Projection, camera.View, Matrix.Identity);
            if (p.Z < 0f || p.Z > 1f)
            {
                screen = default;
                return false;
            }
            screen = new Vector2(p.X, p.Y);
            return true;
        }

        private static float ScreenScale(Vector3 worldPos)
        {
            float distance = Vector3.Distance(Camera.Instance.Position, worldPos);
            return 1f / (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) * Constants.RENDER_SCALE;
        }
    }
}
