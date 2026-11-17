#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Devias
{
    /// <summary>
    /// Devias map objects which have object-specific effects in SourceMain5.2.
    /// Types 30 and 66 emit the original fire/smoke particles; type 100 hides
    /// its carrier mesh and renders the two lightning sprites from its bone 0.
    /// </summary>
    public sealed class DeviasObject : MapTileObject
    {
        private readonly DeviasObjectEffect _effect;

        protected override bool AllowMapObjectInstancing => true;

        public DeviasObject()
        {
            LightEnabled = true;
            _effect = new DeviasObjectEffect(this);
            Children.Add(_effect);
        }

        public override async Task Load()
        {
            // SourceMain5.2 initializes these blend meshes in CreateObject
            // before the first render pass.
            if (Type == 54 || Type == 56)
                BlendMesh = 1;
            else if (Type == 92 || Type == 93)
                BlendMesh = 0;

            await base.Load();

            // SourceMain5.2: type 100 has HiddenMesh = -2 and is represented
            // only by the lightning sprites emitted from the model bone.
            if (Type == 100)
                HiddenMesh = -2;
        }
    }

    internal sealed class DeviasObjectEffect : EffectObject
    {
        private const int MaxParticles = 48;

        private readonly DeviasObject _owner;
        private readonly Particle[] _particles = new Particle[MaxParticles];

        private Texture2D? _fireTexture;
        private Texture2D? _trueFireTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _lightningTexture;
        private SpriteBatch? _spriteBatch;
        private int _particleCount;
        private MapObjectGlowLight? _fireLight;

        private enum ParticleKind
        {
            Fire,
            TrueFire,
            Smoke
        }

        private struct Particle
        {
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public float Gravity;
            public float Life;
            public float Scale;
            public float Rotation;
            public int Frame;
            public Vector3 Light;
        }

        public DeviasObjectEffect(DeviasObject owner)
        {
            _owner = owner;
            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-500f, -500f, -100f),
                new Vector3(500f, 500f, 700f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _fireTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire01.jpg");
            _trueFireTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/fantaF.jpg");
            _smokeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/smoke01.jpg");
            _lightningTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/lightning2.jpg");
            _spriteBatch = GraphicsManager.Instance.Sprite;

            // Type 66 is CreateFire(0) (AddTerrainLight range 4); type 30 is a BITMAP_TRUE_FIRE
            // brazier which the source leaves unlit - it gets a slightly redder fire light here.
            if (_owner.Type == 30 || _owner.Type == 66)
            {
                _fireLight = new MapObjectGlowLight(this, Constants.TERRAIN_SCALE * 4f);
                _fireLight.Register();
            }
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
            EmitSourceParticles();
            UpdateFireLight(gameTime);
        }

        private void UpdateFireLight(GameTime gameTime)
        {
            if (_fireLight == null)
                return;

            if (!_fireLight.IsPositioned)
            {
                Vector3 offset = _owner.Type == 30
                    ? new Vector3(0f, 0f, 160f)
                    : Vector3.TransformNormal(
                        new Vector3(0f, 0f, 50f),
                        Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(_owner.Angle)));
                _fireLight.SetPosition(_owner.Position + offset);
            }

            if (!_fireLight.Step(gameTime))
                return;

            float luminosity = MuGame.Random.Next(6, 12) * 0.1f;
            _fireLight.SetColor(_owner.Type == 30
                ? new Vector3(luminosity, luminosity * 0.5f, luminosity * 0.25f)
                : new Vector3(luminosity, luminosity * 0.6f, luminosity * 0.4f));
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_spriteBatch == null || Camera.Instance == null || !_owner.Visible ||
                _owner.Status != GameControlStatus.Ready)
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                if (_owner.Type == 100)
                {
                    using var scope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone);
                    DrawType100Lightning(gameTime);
                }
                else
                {
                    using (var scope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.Fire);
                        DrawParticleKind(ParticleKind.TrueFire);
                    }

                    using (var smokeScope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.Negative,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.Smoke);
                    }
                }
            }
            else
            {
                if (_owner.Type == 100)
                {
                    DrawType100Lightning(gameTime);
                }
                else
                {
                    DrawParticleKind(ParticleKind.Fire);
                    DrawParticleKind(ParticleKind.TrueFire);
                    DrawParticleKind(ParticleKind.Smoke);
                }
            }
        }

        private void DrawParticleKind(ParticleKind kind)
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly Particle particle = ref _particles[i];
                if (particle.Kind != kind)
                    continue;

                Texture2D? texture = particle.Kind switch
                {
                    ParticleKind.TrueFire => _trueFireTexture,
                    ParticleKind.Smoke => _smokeTexture,
                    _ => _fireTexture
                };
                if (texture == null)
                    continue;

                Rectangle? sourceRectangle = null;
                float rotationDegrees = particle.Rotation;
                if (particle.Kind == ParticleKind.Fire)
                {
                    sourceRectangle = GetFireSourceRectangle(texture, particle.Frame);
                    rotationDegrees = MathHelper.ToDegrees(particle.Angle.X);
                }

                DrawWorldSprite(
                    texture,
                    particle.Position,
                    particle.Light,
                    rotationDegrees,
                    particle.Scale,
                    sourceRectangle);
            }
        }

        private void DrawType100Lightning(GameTime gameTime)
        {
            if (_lightningTexture == null)
                return;

            Matrix[] bones = _owner.GetBoneTransforms();
            Vector3 position = _owner.Position + new Vector3(0f, 0f, 150f);
            if (bones.Length > 0)
            {
                Vector3 localPosition = Vector3.Transform(
                    new Vector3(0f, 0f, 150f),
                    bones[0]);
                position = Vector3.Transform(localPosition, _owner.WorldPosition);
            }

            float rotation = (int)(gameTime.TotalGameTime.TotalMilliseconds * 0.1f) % 360;
            DrawWorldSprite(_lightningTexture, position, Vector3.One, rotation, 2.5f);
            DrawWorldSprite(_lightningTexture, position, Vector3.One, -rotation, 2.5f);
        }

        private void EmitSourceParticles()
        {
            if (_owner.Type == 30)
            {
                Vector3 position = _owner.Position + new Vector3(0f, 0f, 160f);
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnTrueFire(position, _owner.Scale);
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnSmoke(position, 0.5f + MuGame.Random.Next(9) * 0.1f);
            }
            else if (_owner.Type == 66)
            {
                Matrix rotation = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(_owner.Angle));
                Vector3 position = _owner.Position +
                    Vector3.TransformNormal(new Vector3(0f, 0f, 50f), rotation) +
                    new Vector3(
                        MuGame.Random.Next(-8, 8),
                        MuGame.Random.Next(-8, 8),
                        MuGame.Random.Next(-8, 8));
                float luminosity = MuGame.Random.Next(6, 12) * 0.1f;

                if (FPSCounter.Instance.RandFPSCheck(2))
                {
                    SpawnFire(
                        position,
                        new Vector3(luminosity, luminosity * 0.6f, luminosity * 0.4f));
                }
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
                    case ParticleKind.Fire:
                        Matrix fireRotation = Matrix.CreateFromQuaternion(
                            MathUtils.AngleQuaternion(particle.Angle));
                        particle.Position += Vector3.TransformNormal(
                            particle.Velocity,
                            fireRotation) * frameFactor;
                        particle.Gravity += 0.004f * frameFactor;
                        particle.Position.Z += particle.Gravity * 10f * frameFactor;
                        particle.Scale -= 0.04f * frameFactor;
                        particle.Frame = (int)((23f - particle.Life) / 6f);
                        break;
                    case ParticleKind.TrueFire:
                        Matrix trueFireRotation = Matrix.CreateFromQuaternion(
                            MathUtils.AngleQuaternion(particle.Angle));
                        particle.Position += Vector3.TransformNormal(
                            particle.Velocity,
                            trueFireRotation) * frameFactor;
                        float damping = MathF.Pow(0.95f, frameFactor);
                        particle.Velocity.X *= damping;
                        particle.Velocity.Y *= damping;
                        particle.Position.Z += frameFactor;
                        particle.Scale -= 0.02f * frameFactor;
                        particle.Light = new Vector3(particle.Life / 25f);
                        break;
                    case ParticleKind.Smoke:
                        particle.Gravity -= 0.1f * frameFactor;
                        particle.Position.X -= particle.Gravity * 0.2f * frameFactor;
                        particle.Position.Z += particle.Gravity * frameFactor;
                        particle.Scale -= 0.01f * frameFactor;
                        particle.Light = new Vector3(particle.Life / 50f);
                        break;
                }

                if (particle.Life > 0f && particle.Scale > 0f)
                    _particles[writeIndex++] = particle;
            }

            _particleCount = writeIndex;
        }

        private void SpawnFire(Vector3 position, Vector3 light)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            int subType = MuGame.Random.Next(4);
            particle.Kind = ParticleKind.Fire;
            particle.Position = position;
            particle.Angle = _owner.Angle;
            particle.Velocity = subType is 0 or 1
                ? new Vector3(0f, -MuGame.Random.Next(32, 48) * 0.1f, 0f)
                : new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
            particle.Life = 24f;
            particle.Scale = subType switch
            {
                0 => MuGame.Random.Next(128, 192) * 0.01f,
                1 => MuGame.Random.Next(10, 14) * 0.01f,
                _ => 1f,
            };
            particle.Rotation = MuGame.Random.Next(360);
            particle.Frame = 0;
            particle.Light = light;
            AddParticle(particle);
        }

        private void SpawnTrueFire(Vector3 position, float scale)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.TrueFire;
            particle.Position = position;
            particle.Angle = _owner.Angle;
            particle.Velocity = new Vector3(
                (MuGame.Random.Next(10) - 5) * 0.4f,
                0f,
                (MuGame.Random.Next(10) + 5) * 0.2f);
            particle.Life = 24f;
            particle.Scale = scale;
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnSmoke(Vector3 position, float scale)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.Smoke;
            particle.Position = position;
            particle.Angle = _owner.Angle;
            particle.Life = 80f;
            particle.Scale = scale * (MuGame.Random.Next(64) + 64) * 0.005f;
            particle.Gravity = (MuGame.Random.Next(32) + 60) * 0.1f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Light = Vector3.One;
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

        private static Rectangle GetFireSourceRectangle(Texture2D texture, int frame)
        {
            int frameWidth = texture.Width / 4;
            return new Rectangle(
                Math.Clamp(frame, 0, 3) * frameWidth,
                0,
                frameWidth,
                texture.Height);
        }

        private void DrawWorldSprite(
            Texture2D texture,
            Vector3 position,
            Vector3 light,
            float rotationDegrees,
            float scale,
            Rectangle? sourceRectangle = null)
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

            Vector2 origin = sourceRectangle.HasValue
                ? new Vector2(
                    sourceRectangle.Value.Width * 0.5f,
                    sourceRectangle.Value.Height * 0.5f)
                : new Vector2(texture.Width * 0.5f, texture.Height * 0.5f);

            _spriteBatch!.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                sourceRectangle,
                new Color(light),
                -MathHelper.ToRadians(rotationDegrees),
                origin,
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }
    }
}
