#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Client.Main.Objects.Worlds.Events;

namespace Client.Main.Objects.Worlds.BloodCastle
{
    /// <summary>
    /// Blood Castle map placement objects with the object-specific behavior
    /// implemented in SourceMain5.2's WD_11..WD_17/WD_52 branches.
    /// </summary>
    public sealed class BloodCastleObject : MapTileObject
    {
        private readonly BloodCastleObjectVisualEffect _visualEffect;
        private float _actionTime = -1f;
        private float _actionVelocity;
        private bool _isOpening;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;

        public override bool IsStaticForCaching => false;

        public BloodCastleObject()
        {
            LightEnabled = true;
            _visualEffect = new BloodCastleObjectVisualEffect(this);
            Children.Add(_visualEffect);
        }

        public override async Task Load()
        {
            // SourceMain5.2 keeps the two bridge-side meshes hidden until the
            // server signals the bridge action.
            if (Type == 9 || Type == 10)
                HiddenMesh = -2;

            // The original client renders only mesh 2 as a ground shadow for
            // these two Blood Castle map objects. The current renderer's
            // terrain-conformed model shadow is the closest supported path.
            if (Type == 28 || Type == 29)
            {
                RenderShadow = true;
                ShadowOpacity = 0.8f;
            }

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            UpdateBridgeAction(gameTime);
            base.Update(gameTime);
        }

        public static void BeginBridgeOpening(WorldControl world)
        {
            if (world == null || (world.WorldIndex != 12 && world.WorldIndex != 52))
                return;

            foreach (WorldObject worldObject in world.Objects)
            {
                if (worldObject is BloodCastleObject bloodCastleObject)
                    bloodCastleObject.BeginBridgeOpening();
            }
        }

        public static void AttachAmbientEffect(WalkableWorldControl world)
        {
            foreach (WorldObject worldObject in world.Objects)
            {
                if (worldObject is BloodCastleAmbientEffect)
                    return;
            }

            var effect = new BloodCastleAmbientEffect(world);
            world.Objects.Add(effect);
            _ = effect.Load();

            // GOBoid fauna: crows patrol Blood Castle (MODEL_CROW, Object12/Crow01)
            var crowFlock = new AmbientFlockSystem(world, "Object12/Crow01.bmd", 0.8f, BoidFlightStyle.FlyingHigh, maxBoids: 4);
            world.Objects.Add(crowFlock);
        }

        private void BeginBridgeOpening()
        {
            if (Type != 9 && Type != 10 && Type != 36)
                return;

            _isOpening = true;
            _actionTime = 20f;
            _actionVelocity = 1f;

            if (Type == 36)
            {
                Vector3 angle = Angle;
                angle.X = MathHelper.ToRadians(35f);
                Angle = angle;
                HiddenMesh = -1;
            }
        }

        private void UpdateBridgeAction(GameTime gameTime)
        {
            if (!_isOpening)
                return;

            float frameFactor = MathF.Max(
                (float)gameTime.ElapsedGameTime.TotalSeconds * 25f,
                0f);
            if (frameFactor <= 0f)
                return;

            if (Type == 9 || Type == 10)
            {
                _actionTime -= frameFactor;
                if (_actionTime <= 0f)
                {
                    HiddenMesh = -1;
                    _isOpening = false;
                }

                return;
            }

            if (Type != 36)
                return;

            Vector3 angle = Angle;
            float angleDegrees = MathHelper.ToDegrees(angle.X);
            angleDegrees += _actionVelocity * frameFactor;
            _actionVelocity += 1.5f * frameFactor;

            if (angleDegrees >= 90f)
            {
                angleDegrees -= MathF.Max(_actionTime, 0f);
                _actionVelocity = 2f;
            }

            _actionTime -= frameFactor;
            if (_actionTime <= 0f)
            {
                angleDegrees = 90f;
                HiddenMesh = -2;
                _isOpening = false;
            }

            angle.X = MathHelper.ToRadians(angleDegrees);
            Angle = angle;
        }
    }

    /// <summary>
    /// Recreates SourceMain5.2 MoveObjectSetting's flare particles which are
    /// emitted around the hero while any Blood Castle map is active.
    /// </summary>
    internal sealed class BloodCastleAmbientEffect : EffectObject
    {
        private const int MaxParticles = 32;
        private readonly WalkableWorldControl _world;
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private Texture2D? _texture;
        private SpriteBatch? _spriteBatch;
        private int _particleCount;

        private struct Particle
        {
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Light;
            public float VelocityX;
            public float Gravity;
            public float Life;
            public float Scale;
            public float Rotation;
        }

        public BloodCastleAmbientEffect(WalkableWorldControl world)
        {
            _world = world;
            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-20000f, -20000f, -1000f),
                new Vector3(20000f, 20000f, 20000f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Flare.jpg");
            _spriteBatch = GraphicsManager.Instance.Sprite;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_world.Walker == null || !_world.Walker.Visible)
                return;

            float frameFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            UpdateParticles(frameFactor);

            if (FPSCounter.Instance.RandFPSCheck(4))
            {
                Vector3 position = _world.Walker.Position + new Vector3(
                    MuGame.Random.Next(-300, 600),
                    MuGame.Random.Next(-300, 600),
                    MuGame.Random.Next(250, 300));
                Spawn(position);
            }
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_texture == null || _spriteBatch == null || Camera.Instance == null ||
                _particleCount == 0)
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using var scope = new SpriteBatchScope(
                    _spriteBatch,
                    SpriteSortMode.Deferred,
                    Blendings.OneOneAdditive,
                    SamplerState.LinearClamp,
                    DepthState,
                    RasterizerState.CullNone);
                DrawParticles();
            }
            else
            {
                DrawParticles();
            }
        }

        private void Spawn(Vector3 position)
        {
            if (_particleCount >= _particles.Length)
                return;

            _particles[_particleCount++] = new Particle
            {
                Position = position,
                StartPosition = position,
                Light = Vector3.One,
                VelocityX = MuGame.Random.Next(-150, 150),
                Gravity = MuGame.Random.Next(100) * 0.04f + 1f,
                Life = 60f,
                Scale = 0.19f + MuGame.Random.Next(6) * 0.01f,
                Rotation = MuGame.Random.Next(360)
            };
        }

        private void UpdateParticles(float frameFactor)
        {
            int writeIndex = 0;
            for (int i = 0; i < _particleCount; i++)
            {
                Particle particle = _particles[i];
                particle.Life -= frameFactor;
                if (particle.Life <= 0f || particle.Scale <= 0f)
                    continue;

                float count = (particle.VelocityX + particle.Life) * 0.1f;
                particle.Position.X = particle.StartPosition.X + MathF.Sin(count) * 40f;
                particle.Position.Y = particle.StartPosition.Y - MathF.Cos(count) * 40f;
                particle.Position.Z += particle.Gravity * frameFactor;
                particle.Scale -= 0.002f * frameFactor;
                if (particle.Life <= 20f)
                    particle.Light *= MathF.Pow(1f / 1.1f, frameFactor);

                _particles[writeIndex++] = particle;
            }

            _particleCount = writeIndex;
        }

        private void DrawParticles()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly Particle particle = ref _particles[i];
                DrawWorldSprite(
                    particle.Position,
                    particle.Light,
                    particle.Rotation,
                    particle.Scale);
            }
        }

        private void DrawWorldSprite(
            Vector3 position,
            Vector3 light,
            float rotationDegrees,
            float scale)
        {
            Vector3 projected = GraphicsDevice.Viewport.Project(
                position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;
            Vector3 projectedWidth = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Right * (_texture!.Width * scale),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector3 projectedHeight = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Up * (_texture.Height * scale),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector2 spriteScale = new(
                MathF.Abs(projectedWidth.X - projected.X) / _texture.Width,
                MathF.Abs(projectedHeight.Y - projected.Y) / _texture.Height);
            if (!float.IsFinite(spriteScale.X) || !float.IsFinite(spriteScale.Y) ||
                spriteScale.X <= 0f || spriteScale.Y <= 0f)
                return;

            _spriteBatch!.Draw(
                _texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(new Vector4(light, 1f)),
                -MathHelper.ToRadians(rotationDegrees),
                new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f),
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }
    }

    /// <summary>
    /// Recreates the transient sprites and particles emitted by Blood Castle
    /// map object types 11, 13 and 37 in the original client.
    /// </summary>
    internal sealed class BloodCastleObjectVisualEffect : EffectObject
    {
        private const int MaxParticles = 24;

        private readonly BloodCastleObject _owner;
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private Texture2D? _lightTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _cloudTexture;
        private Texture2D? _advancedSmokeTexture;
        private Texture2D? _advancedSmokeAlphaTexture;
        private SpriteBatch? _spriteBatch;
        private int _particleCount;
        private int _timer;

        private enum ParticleKind
        {
            AdvancedSmoke,
            AdvancedSmokeAlpha,
            Cloud,
            Flare
        }

        private struct Particle
        {
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Gravity;
            public float Life;
            public float Scale;
            public float Rotation;
        }

        public BloodCastleObjectVisualEffect(BloodCastleObject owner)
        {
            _owner = owner;
            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-500f, -500f, -200f),
                new Vector3(500f, 500f, 700f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (_owner.Type == 11)
                _lightTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/flare01.jpg");
            else if (_owner.Type == 13)
                _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Flare.jpg");
            else if (_owner.Type == 37)
            {
                _cloudTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/clouds.jpg");
                _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Flare.jpg");
                _advancedSmokeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/fi01.jpg");
                _advancedSmokeAlphaTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/fi02.tga");
            }

            _spriteBatch = GraphicsManager.Instance.Sprite;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!_owner.Visible || _owner.Status != GameControlStatus.Ready)
            {
                _particleCount = 0;
                return;
            }

            float frameFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            UpdateParticles(frameFactor);
            if (_owner.Type == 37)
                EmitType37Particles();
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_spriteBatch == null || Camera.Instance == null ||
                !_owner.Visible || _owner.Status != GameControlStatus.Ready)
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                if (_owner.Type == 37)
                {
                    using (var alphaScope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.Alpha,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.AdvancedSmokeAlpha);
                    }

                    using (var additiveScope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.AdvancedSmoke);
                        DrawParticleKind(ParticleKind.Cloud);
                        DrawParticleKind(ParticleKind.Flare);
                    }
                }
                else
                {
                    using var scope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone);
                    DrawObjectSprites(gameTime);
                }
            }
            else if (_owner.Type == 37)
            {
                DrawParticleKind(ParticleKind.AdvancedSmokeAlpha);
                DrawParticleKind(ParticleKind.AdvancedSmoke);
                DrawParticleKind(ParticleKind.Cloud);
                DrawParticleKind(ParticleKind.Flare);
            }
            else
            {
                DrawObjectSprites(gameTime);
            }
        }

        private void DrawObjectSprites(GameTime gameTime)
        {
            Matrix[] bones = _owner.GetBoneTransforms();
            if (bones == null)
                return;

            float milliseconds = (float)gameTime.TotalGameTime.TotalMilliseconds;
            if (_owner.Type == 11 && _lightTexture != null)
            {
                int[] lightBones = [1, 2, 4, 6, 9, 10, 11];
                float luminosity = MathF.Sin(
                    (MathHelper.ToDegrees(_owner.Angle.Z) * 20f + milliseconds) * 0.001f) * 0.5f + 0.5f;
                Vector3 light = new(luminosity, luminosity * 0.5f, 0f);

                foreach (int boneIndex in lightBones)
                    DrawBoneSprite(bones, boneIndex, new Vector3(0f, 0f, 2f), _lightTexture, 0.5f, light, 0f);
            }
            else if (_owner.Type == 13 && _flareTexture != null && bones.Length > 3)
            {
                float luminosity = MathF.Sin(milliseconds * 0.001f) * 0.3f + 0.7f;
                DrawBoneSprite(
                    bones,
                    3,
                    Vector3.Zero,
                    _flareTexture,
                    luminosity + 0.5f,
                    new Vector3(luminosity),
                    0f);
            }
        }

        private void DrawBoneSprite(
            Matrix[] bones,
            int boneIndex,
            Vector3 localOffset,
            Texture2D texture,
            float scale,
            Vector3 light,
            float rotation)
        {
            if ((uint)boneIndex >= (uint)bones.Length)
                return;

            Vector3 localPosition = Vector3.Transform(localOffset, bones[boneIndex]);
            Vector3 position = Vector3.Transform(localPosition, _owner.WorldPosition);
            DrawWorldSprite(texture, position, light, rotation, scale);
        }

        private void EmitType37Particles()
        {
            Vector3 light = Vector3.One;

            if (FPSCounter.Instance.RandFPSCheck(2) && ((_timer++ + 2) % 4 == 0))
            {
                SpawnAdvancedSmokeAlpha(_owner.Position);
                SpawnAdvancedSmoke(_owner.Position, 0);
            }

            if (FPSCounter.Instance.RandFPSCheck(2) && (_timer++ % 4 == 0))
            {
                SpawnCloud(_owner.Position);
                SpawnAdvancedSmoke(_owner.Position, 1);
                SpawnFlare(_owner.Position, new Vector3(1f, 0.8f, 0.8f));
            }
        }

        private void UpdateParticles(float frameFactor)
        {
            int writeIndex = 0;
            for (int i = 0; i < _particleCount; i++)
            {
                Particle particle = _particles[i];
                particle.Life -= frameFactor;
                if (particle.Life <= 0f || particle.Scale <= 0f)
                    continue;

                switch (particle.Kind)
                {
                    case ParticleKind.AdvancedSmoke:
                        particle.Position += particle.Velocity * frameFactor;
                        particle.Velocity.X *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Y *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Z += 0.3f * frameFactor;
                        particle.Light = new Vector3(particle.Life / 10f);
                        particle.Scale += 0.07f * frameFactor;
                        break;
                    case ParticleKind.AdvancedSmokeAlpha:
                        particle.Position += particle.Velocity * frameFactor;
                        particle.Velocity.X *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Y *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Z += 0.6f * frameFactor;
                        particle.Position += new Vector3(
                            MuGame.Random.Next(-2, 2),
                            MuGame.Random.Next(-2, 2),
                            MuGame.Random.Next(-2, 2) * 0.8f) * frameFactor;
                        particle.Light = new Vector3(particle.Life / 25f);
                        particle.Scale += 0.05f * frameFactor;
                        particle.Rotation += (1f + MuGame.Random.Next(2)) * frameFactor;
                        break;
                    case ParticleKind.Cloud:
                        particle.Position += particle.Velocity * frameFactor;
                        particle.Velocity.X *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Y *= MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.Z += 0.6f * frameFactor;
                        particle.Position += new Vector3(
                            MuGame.Random.Next(-2, 2),
                            MuGame.Random.Next(-2, 2),
                            MuGame.Random.Next(-2, 2) * 0.8f) * frameFactor;
                        particle.Light = new Vector3(particle.Life / 50f);
                        particle.Scale += 0.05f * frameFactor;
                        particle.Rotation += (1f + MuGame.Random.Next(2)) * frameFactor;
                        break;
                    case ParticleKind.Flare:
                        float count = (particle.Velocity.X + particle.Life) * 0.1f;
                        particle.Position.X = particle.StartPosition.X + MathF.Sin(count) * 40f;
                        particle.Position.Y = particle.StartPosition.Y - MathF.Cos(count) * 40f;
                        particle.Position.Z += particle.Gravity * frameFactor;
                        particle.Scale -= 0.004f * frameFactor;
                        if (particle.Life <= 30f)
                            particle.Light *= MathF.Pow(1f / 1.1f, frameFactor);
                        break;
                }

                if (particle.Life > 0f && particle.Scale > 0f)
                    _particles[writeIndex++] = particle;
            }

            _particleCount = writeIndex;
        }

        private void SpawnAdvancedSmokeAlpha(Vector3 position)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.AdvancedSmokeAlpha;
            particle.Position = position;
            particle.Life = 25f + MuGame.Random.Next(5);
            particle.Scale = 0.5f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Velocity = new Vector3(
                (MuGame.Random.Next(10) + 5) * 0.4f,
                0f,
                (MuGame.Random.Next(10) + 5) * 0.2f);
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnAdvancedSmoke(Vector3 position, int subType)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.AdvancedSmoke;
            particle.Position = position;
            particle.Life = 20f + MuGame.Random.Next(5);
            particle.Scale = subType == 0
                ? 0.5f + MuGame.Random.Next(10) * 0.02f
                : 1f + MuGame.Random.Next(10) * 0.1f;
            particle.Velocity = subType == 0
                ? new Vector3(
                    (MuGame.Random.Next(10) + 5) * 0.4f,
                    (MuGame.Random.Next(10) - 5) * 0.4f,
                    (MuGame.Random.Next(10) + 5) * 0.2f)
                : new Vector3(
                    (MuGame.Random.Next(10) + 5) * 0.2f,
                    (MuGame.Random.Next(10) - 5) * 0.2f,
                    (MuGame.Random.Next(10) + 5) * 0.1f);
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnCloud(Vector3 position)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.Cloud;
            particle.Position = position;
            particle.Life = 25f + MuGame.Random.Next(5);
            particle.Scale = 0.2f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Velocity = new Vector3(
                (MuGame.Random.Next(10) + 5) * 0.4f,
                0f,
                (MuGame.Random.Next(10) + 5) * 0.2f);
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnFlare(Vector3 position, Vector3 light)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.Flare;
            particle.Position = position;
            particle.StartPosition = position;
            particle.Life = 40f;
            particle.Gravity = MuGame.Random.Next(100) * 0.05f + 1f;
            particle.Velocity.X = MuGame.Random.Next(300) - 150;
            particle.Scale = 0.19f + MuGame.Random.Next(6) * 0.01f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Light = light;
            AddParticle(particle);
        }

        private bool TryAddParticle(out Particle particle)
        {
            particle = default;
            return _particleCount < _particles.Length;
        }

        private void AddParticle(Particle particle)
        {
            _particles[_particleCount++] = particle;
        }

        private void DrawParticleKind(ParticleKind kind)
        {
            Texture2D? texture = kind switch
            {
                ParticleKind.AdvancedSmoke => _advancedSmokeTexture,
                ParticleKind.AdvancedSmokeAlpha => _advancedSmokeAlphaTexture,
                ParticleKind.Cloud => _cloudTexture,
                ParticleKind.Flare => _flareTexture,
                _ => null
            };
            if (texture == null)
                return;

            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly Particle particle = ref _particles[i];
                if (particle.Kind != kind)
                    continue;

                float alpha = kind == ParticleKind.AdvancedSmokeAlpha
                    ? MathHelper.Clamp(particle.Light.X, 0f, 1f)
                    : 1f;
                DrawWorldSprite(
                    texture,
                    particle.Position,
                    particle.Light,
                    particle.Rotation,
                    particle.Scale,
                    alpha);
            }
        }

        private void DrawWorldSprite(
            Texture2D texture,
            Vector3 position,
            Vector3 light,
            float rotationDegrees,
            float scale,
            float alpha = 1f)
        {
            Vector3 projected = GraphicsDevice.Viewport.Project(
                position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;
            Vector3 projectedWidth = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Right * (texture.Width * scale),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector3 projectedHeight = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Up * (texture.Height * scale),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            Vector2 spriteScale = new(
                MathF.Abs(projectedWidth.X - projected.X) / texture.Width,
                MathF.Abs(projectedHeight.Y - projected.Y) / texture.Height);
            if (!float.IsFinite(spriteScale.X) || !float.IsFinite(spriteScale.Y) ||
                spriteScale.X <= 0f || spriteScale.Y <= 0f)
                return;

            _spriteBatch!.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(new Vector4(light, alpha)) * _owner.TotalAlpha,
                -MathHelper.ToRadians(rotationDegrees),
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }
    }
}

