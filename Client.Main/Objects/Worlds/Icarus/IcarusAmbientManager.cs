#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// GOBoid.cpp CreateDragon (WD_10HEAVEN, slots 0-2), verbatim:
    /// Scale=(rand%3+6)*0.05 ; Velocity=(rand%10+10)*0.02 ; Gravity=(rand%10+10)*0.05 deg/frame ;
    /// CurrentAction=MONSTER01_DIE+1 (soaring pose, client action 7) played at PlaySpeed 0.5 ;
    /// spawn Hero±2000 / Z-600 with random heading ; movement via MoveBoidGroup Velocity*25 u/frame
    /// with Direction[2]=0 (constant altitude) ; no fading, despawn only at planar Range >= 4000.
    /// </summary>
    public sealed class IcarusDragonBoid : ModelObject
    {
        private const float LegacyFramesPerSecond = 25f;
        private const int SoaringAction = 7; // MONSTER01_DIE + 1

        private float _gravityDegrees;

        public float Velocity { get; private set; }

        public IcarusDragonBoid()
        {
            RenderShadow = false;

            // Source CreateDragon: AlphaEnable=false, Alpha=1 — fully OPAQUE meshes.
            // Transparency on a multi-mesh model causes see-through artifacts, so the
            // distant/dim look is achieved with a darkened BodyLight instead.
            Alpha = 1f;
            LightEnabled = true;
            Light = new Vector3(0.42f, 0.46f, 0.55f);
        }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare("Monster/Monster32.bmd");
            if (Model != null)
            {
                // CreateDragon: SetAction(MONSTER01_DIE + 1); loop plays at PlaySpeed 0.5
                // Client mapping: _animTime += dt * action.PlaySpeed * AnimationSpeed must equal
                // source's 0.5 * REFERENCE_FPS(25) = 12.5 animation keys per second.
                CurrentAction = SoaringAction;
                AnimationSpeed = 10f;
            }
            await base.Load();
        }

        public void BeginLife(Vector3 heroPosition)
        {
            // Vector(Hero ±(rand%4000-2000), ..., Hero.Z - 600)
            Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(4000) - 2000f,
                heroPosition.Y + MuGame.Random.Next(4000) - 2000f,
                heroPosition.Z - 600f);

            Angle = new Vector3(0f, 0f, MathHelper.ToRadians(MuGame.Random.Next(360)));
            Scale = (MuGame.Random.Next(3) + 6) * 0.05f;
            Velocity = (MuGame.Random.Next(10) + 10) * 0.02f;

            // o->Gravity = (rand%10+10)*0.05 -> 0.5..0.95 deg/frame bank rate
            _gravityDegrees = (MuGame.Random.Next(10) + 10) * 0.05f;
            if (MuGame.Random.Next(2) == 0)
                _gravityDegrees = -_gravityDegrees;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            float legacyFrames = dt * LegacyFramesPerSecond;

            // Flock steering: Gravity degrees per legacy frame (source MoveBoid has no flips —
            // the bank direction stays constant for the dragon's whole flight)
            Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + MathHelper.ToRadians(_gravityDegrees) * legacyFrames);

            // MoveBoidGroup: Position += rotate(Vector(Velocity*25, 0, Direction[2]=0)) * FPS.
            // Empirically verified in-game: Monster32.bmd is authored facing -Y (standard
            // monster authoring), so travel uses the repo forward (sinθ, -cosθ).
            Vector3 forward = new(MathF.Sin(Angle.Z), -MathF.Cos(Angle.Z), 0f);
            Position += forward * (Velocity * 25f * LegacyFramesPerSecond) * dt;
        }
    }

    /// <summary>
    /// GOBoid.cpp CreateDragon slots 3-12 / MoveHeavenBug, verbatim:
    /// MODEL_SPEARSKILL joint (approximated as a soft additive glow billboard),
    /// Velocity fixed 2.2, drift X+=V*sin(A), Y-=V*cos(A) per frame, steering
    /// A += 0.01*cos((34571+iFrame+index*41273)*3e-4)*sin((17732+iFrame+index*5161)*3e-4)
    /// (radians per frame), LifeTime 240*40 frames, rand_fps_check(5120), Range >= 1500.
    /// </summary>
    public sealed class IcarusHeavenBug : EffectObject
    {
        private const float LegacyFramesPerSecond = 25f;
        private const string FlareTexturePath = "Effect/flare01.jpg";
        private const float JointSize = 25f;

        private Texture2D? _flareTexture;
        private bool _textureRequested;
        private readonly int _index;
        private float _lifeFrames = 9600f; // 240*40

        public bool IsFadedOut { get; private set; }

        public IcarusHeavenBug(int index)
        {
            _index = index;
            Alpha = 1f;
            Scale = 0.8f;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (!_textureRequested)
            {
                _textureRequested = true;
                _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture(FlareTexturePath);
            }
        }

        public void BeginLife(Vector3 heroPosition)
        {
            // Hero ±512, Z = Hero.Z exactly; heading rand 0-360
            Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(1024) - 512f,
                heroPosition.Y + MuGame.Random.Next(1024) - 512f,
                heroPosition.Z);
            Angle = new Vector3(0f, 0f, MathHelper.ToRadians(MuGame.Random.Next(360)));
            _lifeFrames = 9600f;
            IsFadedOut = false;
            Alpha = 1f;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || Camera.Instance == null || World == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            float legacyFrames = dt * LegacyFramesPerSecond;
            double iFrame = gameTime.TotalGameTime.TotalMilliseconds / 40.0;

            const float velocity = 2.2f;
            Position += new Vector3(
                velocity * MathF.Sin(Angle.Z),
                -velocity * MathF.Cos(Angle.Z),
                0f) * legacyFrames;

            double steer = 0.01
                * Math.Cos((34571.0 + iFrame + _index * 41273.0) * 0.0003)
                * Math.Sin((17732.0 + iFrame + _index * 5161.0) * 0.0003);
            Angle = new Vector3(Angle.X, Angle.Y, Angle.Z + (float)(steer * legacyFrames));

            _lifeFrames -= legacyFrames;

            var walker = (World as WalkableWorldControl)?.Walker;
            if (walker != null)
            {
                float range = Vector2.Distance(
                    new Vector2(Position.X, Position.Y),
                    new Vector2(walker.Position.X, walker.Position.Y));
                if (range >= 1500f)
                    IsFadedOut = true;
            }

            if ((float)MuGame.Random.NextDouble() < legacyFrames / 5120f)
                IsFadedOut = true;

            if (_lifeFrames <= 0f)
                IsFadedOut = true;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || Camera.Instance == null || _flareTexture == null)
                return;

            var spriteBatch = Controllers.GraphicsManager.Instance.Sprite;
            var device = Controllers.GraphicsManager.Instance.GraphicsDevice;
            if (spriteBatch == null || device == null)
                return;

            Vector3 projected = device.Viewport.Project(
                Position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            Vector3 edge = device.Viewport.Project(
                Position + Camera.Instance.Right * JointSize,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            float radiusPx = MathF.Abs(edge.X - projected.X);

            void drawFlare()
            {
                spriteBatch.Draw(
                    _flareTexture,
                    new Vector2(projected.X, projected.Y),
                    null,
                    Color.White,
                    0f,
                    new Vector2(_flareTexture.Width * 0.5f, _flareTexture.Height * 0.5f),
                    radiusPx / _flareTexture.Width,
                    SpriteEffects.None,
                    projected.Z);
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    drawFlare();
                }
            }
            else
            {
                drawFlare();
            }
        }
    }

    /// <summary>
    /// Heaven ambient pool: cap 13 — slots 0-2 dragons, slots 3-12 heaven bugs.
    /// Dragons respawn continuously like the source slot refill (no lifetime).
    /// </summary>
    public sealed class IcarusAmbientManager
    {
        private const int MaxDragons = 3;
        private const int MaxHeavenBugs = 10;
        private const float DragonDespawnDistance = 4000f;
        private const float BugDespawnDistance = 1500f;

        private readonly WalkableWorldControl _world;
        private readonly List<IcarusDragonBoid> _dragons = new();
        private readonly List<IcarusHeavenBug> _bugs = new();
        private float _bugCooldown = 1f;

        public IcarusAmbientManager(WalkableWorldControl world)
        {
            _world = world;
        }

        public void Update(GameTime gameTime)
        {
            if (_world.Status != GameControlStatus.Ready || _world.Walker == null)
                return;

            Vector3 heroPosition = _world.Walker.Position;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            for (int i = _dragons.Count - 1; i >= 0; i--)
            {
                var dragon = _dragons[i];
                float dist = Vector2.Distance(
                    new Vector2(dragon.Position.X, dragon.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));
                if (dragon.Status == GameControlStatus.Disposed || dist > DragonDespawnDistance)
                {
                    _world.Objects.Remove(dragon);
                    dragon.Dispose();
                    _dragons.RemoveAt(i);
                }
            }

            for (int i = _bugs.Count - 1; i >= 0; i--)
            {
                var bug = _bugs[i];
                float dist = Vector2.Distance(
                    new Vector2(bug.Position.X, bug.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));
                if (bug.IsFadedOut || bug.Status == GameControlStatus.Disposed || dist > BugDespawnDistance)
                {
                    _world.Objects.Remove(bug);
                    bug.Dispose();
                    _bugs.RemoveAt(i);
                }
            }

            // Slot refill: dragons live until out of range, then the slot respawns
            if (_dragons.Count < MaxDragons)
            {
                var dragon = new IcarusDragonBoid();
                dragon.BeginLife(heroPosition);
                _dragons.Add(dragon);
                _world.Objects.Add(dragon);
                _ = dragon.Load();
            }

            _bugCooldown -= dt;
            if (_bugCooldown <= 0f && _bugs.Count < MaxHeavenBugs)
            {
                _bugCooldown = 1.5f;

                var bug = new IcarusHeavenBug(MuGame.Random.Next(1024));
                bug.BeginLife(heroPosition);
                _bugs.Add(bug);
                _world.Objects.Add(bug);
                _ = bug.LoadContent();
            }
        }

        public void Clear()
        {
            foreach (var dragon in _dragons) { _world.Objects.Remove(dragon); dragon.Dispose(); }
            _dragons.Clear();
            foreach (var bug in _bugs) { _world.Objects.Remove(bug); bug.Dispose(); }
            _bugs.Clear();
        }
    }
}

