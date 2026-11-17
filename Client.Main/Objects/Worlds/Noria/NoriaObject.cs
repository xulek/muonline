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

namespace Client.Main.Objects.Worlds.Noria
{
    /// <summary>
    /// Shared implementation of the map-object animation/effects from the
    /// WD_3NORIA branches in SourceMain5.2.
    /// </summary>
    public class NoriaObject : MapTileObject
    {
        private readonly NoriaObjectVisualEffect _visualEffect;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;

        public override bool IsStaticForCaching => false;

        public NoriaObject()
        {
            LightEnabled = true;
            BlendMeshState = Blendings.OneOneAdditive;

            _visualEffect = new NoriaObjectVisualEffect(this);
            Children.Add(_visualEffect);
        }

        internal virtual Matrix EffectWorldPosition => WorldPosition;

        internal virtual Matrix[] GetEffectBoneTransforms() => GetBoneTransforms();

        internal virtual int ResolveEffectBoneIndex(int sourceBoneIndex) => sourceBoneIndex;

        public override async Task Load()
        {
            ApplySourceCreateState();
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            ApplySourceAnimation(gameTime);
            base.Update(gameTime);
        }

        private void ApplySourceCreateState()
        {
            BlendMesh = -1;
            TextureCoordinateOffsetMeshIndex = -1;
            TextureCoordinateOffset = Vector2.Zero;

            switch (Type)
            {
                case 1:
                    BlendMesh = 1;
                    break;
                case 9:
                    BlendMesh = 3;
                    break;
                case 17:
                case 19:
                case 37:
                    BlendMesh = 0;
                    break;
                case 18:
                    BlendMesh = 2;
                    TextureCoordinateOffsetMeshIndex = 2;
                    break;
                case 39:
                    BlendMesh = 1;
                    break;
                case 41:
                    BlendMesh = 0;
                    TextureCoordinateOffsetMeshIndex = 0;
                    break;
                case 42:
                case 43:
                    TextureCoordinateOffsetMeshIndex = 0;
                    break;
            }
        }

        private void ApplySourceAnimation(GameTime gameTime)
        {
            long worldTime = (long)gameTime.TotalGameTime.TotalMilliseconds;

            switch (Type)
            {
                case 18:
                    TextureCoordinateOffset = new Vector2(
                        0f,
                        (worldTime % 1000L) * 0.001f);
                    break;
                case 41:
                    TextureCoordinateOffset = new Vector2(
                        0f,
                        (worldTime % 2000L) * 0.0005f);
                    break;
                case 42:
                    TextureCoordinateOffset = new Vector2(
                        -(worldTime % 500L) * 0.002f,
                        0f);
                    break;
                case 43:
                    TextureCoordinateOffset = new Vector2(
                        (worldTime % 500L) * 0.002f,
                        0f);
                    break;
            }
        }
    }

    /// <summary>
    /// Recreates SourceMain5.2 RenderObjectVisual for Noria's animated light
    /// objects. Sprites are projected in camera space exactly like the source
    /// RenderSprite helper, so their position/size does not depend on a
    /// distance approximation.
    /// </summary>
    internal sealed class NoriaObjectVisualEffect : EffectObject
    {
        private const float ReferenceFps = 25f;
        private const int MaxShinyParticles = 32;
        private const int MaxSparkParticles = 16;
        private const int MaxJointSparks = 16;

        private readonly NoriaObject _owner;
        private readonly ShinyParticle[] _shinyParticles = new ShinyParticle[MaxShinyParticles];
        private readonly SparkParticle[] _sparkParticles = new SparkParticle[MaxSparkParticles];
        private readonly JointSparkParticle[] _jointSparks = new JointSparkParticle[MaxJointSparks];

        private Texture2D? _lightTexture;
        private Texture2D? _lightningTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _sparkTexture;
        private Texture2D? _jointSparkTexture;
        private SpriteBatch? _spriteBatch;
        private MapObjectGlowLight? _glowLight;

        private struct ShinyParticle
        {
            public bool Active;
            public int SubType;
            public Vector3 Position;
            public Vector3 Light;
            public float LifeTicks;
            public float Scale;
            public float Rotation;
        }

        private struct SparkParticle
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 SecondaryVelocity;
            public Vector3 Light;
            public float Gravity;
            public float LifeTicks;
            public float Scale;
        }

        private struct JointSparkParticle
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 PreviousPosition;
            public Vector3 Velocity;
            public Vector3 Light;
            public float LifeTicks;
            public float Scale;
            public bool HasMoved;
        }

        public NoriaObjectVisualEffect(NoriaObject owner)
        {
            _owner = owner;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-500f, -500f, -500f),
                new Vector3(500f, 500f, 500f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            int sourceType = _owner.Type;
            if (sourceType == 1 || sourceType == 9 || sourceType == 17 ||
                sourceType == 35 || sourceType == 39)
            {
                _lightTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/flare01.jpg");
            }

            if (sourceType == 39)
            {
                _lightningTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/lightning2.jpg");
                _shinyTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/Shiny01.jpg");
                _sparkTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/Spark02.jpg");
                _jointSparkTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/Spark01.jpg");
            }

            _spriteBatch = GraphicsManager.Instance.Sprite;

            // The source only draws BITMAP_LIGHT sprites for these lamps; one soft blue
            // terrain light per object makes the glow reach the ground.
            float glowRadiusTiles = sourceType switch
            {
                1 => 2f,
                9 or 17 or 35 or 39 => 3f,
                _ => 0f,
            };
            if (glowRadiusTiles > 0f)
            {
                _glowLight = new MapObjectGlowLight(this, Constants.TERRAIN_SCALE * glowRadiusTiles, 0.7f);
                _glowLight.Register();
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            UpdateGlowLight(gameTime);

            if (_owner.Type != 39 || !_owner.Visible ||
                _owner.Status != GameControlStatus.Ready)
            {
                ClearParticles();
                return;
            }

            float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * ReferenceFps;
            if (frameFactor <= 0f)
                return;

            UpdateShinyParticles(frameFactor);
            UpdateSparkParticles(frameFactor);
            UpdateJointSparks(frameFactor);
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_spriteBatch == null || Camera.Instance == null ||
                !_owner.Visible || _owner.Status != GameControlStatus.Ready)
            {
                return;
            }

            int sourceType = _owner.Type;
            if (sourceType != 1 && sourceType != 9 && sourceType != 17 &&
                sourceType != 35 && sourceType != 39)
            {
                return;
            }

            Matrix[] bones = _owner.GetEffectBoneTransforms();
            if (bones == null)
                return;

            float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;
            Vector3 light = new(
                luminosity * 0.4f,
                luminosity * (sourceType == 39 ? 0.8f : 0.7f),
                luminosity);

            void draw()
            {
                switch (sourceType)
                {
                    case 1:
                        DrawBoneSprite(bones, 2, 0.5f, light, 0f, _lightTexture);
                        DrawBoneSprite(bones, 4, 0.5f, light, 0f, _lightTexture);
                        DrawBoneSprite(bones, 6, 0.5f, light, 0f, _lightTexture);
                        break;
                    case 9:
                        DrawBoneSprite(bones, 1, 1.5f, light, 0f, _lightTexture);
                        break;
                    case 17:
                        DrawBoneSprite(bones, 4, 1f, light, 0f, _lightTexture);
                        DrawBoneSprite(bones, 7, 1f, light, 0f, _lightTexture);
                        DrawBoneSprite(bones, 10, 1f, light, 0f, _lightTexture);
                        DrawBoneSprite(bones, 13, 1f, light, 0f, _lightTexture);
                        break;
                    case 35:
                        DrawBoneSprite(bones, 3, 1.5f, light, 0f, _lightTexture);
                        break;
                    case 39:
                        DrawChaosMachineSprites(gameTime, bones, light);
                        EmitChaosMachineParticles(gameTime, bones);
                        break;
                }

                DrawParticles();
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    _spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState,
                    SamplerState.LinearClamp,
                    DepthState,
                    RasterizerState.CullNone))
                {
                    draw();
                }
            }
            else
            {
                draw();
            }
        }

        private void UpdateGlowLight(GameTime gameTime)
        {
            if (_glowLight == null || !_owner.Visible || _owner.Status != GameControlStatus.Ready)
                return;

            if (!_glowLight.IsPositioned)
            {
                Matrix[] bones = _owner.GetEffectBoneTransforms();
                if (bones == null)
                    return;

                // Source sprite bones: 1 -> 2/4/6, 9 -> 1, 17 -> 4/7/10/13, 35 -> 3, 39 -> 57.
                int boneIndex = _owner.Type switch
                {
                    1 => 4,
                    9 => 1,
                    17 => 7,
                    35 => 3,
                    _ => 57,
                };
                if (!TryGetBonePosition(bones, boneIndex, out Vector3 position))
                    position = _owner.EffectWorldPosition.Translation;
                _glowLight.SetPosition(position);
            }

            if (!_glowLight.Step(gameTime))
                return;

            float luminosity = (MuGame.Random.Next(10) + 85) * 0.01f;
            _glowLight.SetColor(_owner.Type == 39
                ? new Vector3(luminosity * 0.6f, luminosity * 0.8f, luminosity)
                : new Vector3(luminosity * 0.4f, luminosity * 0.7f, luminosity));
        }

        private void DrawChaosMachineSprites(
            GameTime gameTime,
            Matrix[] bones,
            Vector3 lightningLight)
        {
            float rotation = (int)(gameTime.TotalGameTime.TotalMilliseconds * 0.1f) % 360;

            DrawBoneSprite(bones, 57, 1f, lightningLight, rotation, _lightningTexture);
            DrawBoneSprite(bones, 57, 1f, lightningLight, -rotation, _lightningTexture);

            Vector3 white = Vector3.One;
            for (int bone = 61; bone <= 65; bone++)
            {
                DrawBoneSprite(bones, bone, 1f, white, 0f, _lightTexture);
            }
        }

        private void EmitChaosMachineParticles(GameTime gameTime, Matrix[] bones)
        {
            float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * ReferenceFps;
            float shinyChance = MathHelper.Clamp(frameFactor / 32f, 0f, 1f);
            for (int bone = 61; bone <= 65; bone++)
            {
                if (MuGame.Random.NextDouble() >= shinyChance ||
                    !TryGetBonePosition(bones, bone, out Vector3 position))
                {
                    continue;
                }

                SpawnShiny(position, 0);
                SpawnShiny(position, 1);
            }

            float jointChance = MathHelper.Clamp(frameFactor / 8f, 0f, 1f);
            if (MuGame.Random.NextDouble() >= jointChance ||
                !TryGetBonePosition(bones, 58, out Vector3 jointPosition))
            {
                return;
            }

            for (int i = 0; i < 8; i++)
            {
                Vector3 angle = new(
                    MuGame.Random.Next(60, 120),
                    140f,
                    MuGame.Random.Next(30));

                SpawnJointSpark(jointPosition, angle);
                SpawnSpark(jointPosition, angle);
            }
        }

        private void UpdateShinyParticles(float frameFactor)
        {
            for (int i = 0; i < _shinyParticles.Length; i++)
            {
                ref ShinyParticle particle = ref _shinyParticles[i];
                if (!particle.Active)
                    continue;

                particle.LifeTicks -= frameFactor;
                if (particle.LifeTicks <= 0f)
                {
                    particle.Active = false;
                    continue;
                }

                particle.Scale = MathF.Sin(
                    MathHelper.ToRadians(particle.LifeTicks * 10f));
                if (particle.SubType == 1)
                {
                    particle.Scale *= MathF.Pow(0.75f, frameFactor);
                    particle.Rotation -= 12f * frameFactor;
                }
            }
        }

        private void UpdateSparkParticles(float frameFactor)
        {
            for (int i = 0; i < _sparkParticles.Length; i++)
            {
                ref SparkParticle particle = ref _sparkParticles[i];
                if (!particle.Active)
                    continue;

                particle.LifeTicks -= frameFactor;
                if (particle.LifeTicks <= 0f)
                {
                    particle.Active = false;
                    continue;
                }

                // MoveParticles first applies the generic MovePosition call
                // and BITMAP_SPARK then adds Velocity once more.
                particle.Position += particle.SecondaryVelocity * frameFactor;
                particle.Light = new Vector3(particle.LifeTicks / 16f);
                particle.Position.Z += particle.Gravity * frameFactor;
                particle.Gravity -= 2f * frameFactor;

                if (_owner.World?.Terrain != null)
                {
                    float terrainHeight = _owner.World.Terrain.RequestTerrainHeight(
                        particle.Position.X,
                        particle.Position.Y);
                    if (particle.Position.Z < terrainHeight)
                    {
                        particle.Position.Z = terrainHeight;
                        particle.Gravity = -particle.Gravity * 0.6f;
                        particle.LifeTicks -= 4f * frameFactor;
                    }
                }

                particle.Position += particle.Velocity * frameFactor;
            }
        }

        private void UpdateJointSparks(float frameFactor)
        {
            for (int i = 0; i < _jointSparks.Length; i++)
            {
                ref JointSparkParticle particle = ref _jointSparks[i];
                if (!particle.Active)
                    continue;

                particle.LifeTicks -= frameFactor;
                if (particle.LifeTicks < 0f)
                {
                    particle.Active = false;
                    continue;
                }

                particle.PreviousPosition = particle.Position;
                particle.Position += particle.Velocity * frameFactor;
                particle.HasMoved = true;
            }
        }

        private void DrawParticles()
        {
            if (_shinyTexture != null)
            {
                for (int i = 0; i < _shinyParticles.Length; i++)
                {
                    ref readonly ShinyParticle particle = ref _shinyParticles[i];
                    if (particle.Active && particle.Scale > 0f)
                    {
                        DrawWorldSprite(
                            _shinyTexture,
                            particle.Position,
                            particle.Light,
                            particle.Rotation,
                            new Vector2(particle.Scale));
                    }
                }
            }

            if (_sparkTexture != null)
            {
                for (int i = 0; i < _sparkParticles.Length; i++)
                {
                    ref readonly SparkParticle particle = ref _sparkParticles[i];
                    if (particle.Active)
                    {
                        DrawWorldSprite(
                            _sparkTexture,
                            particle.Position,
                            particle.Light,
                            0f,
                            new Vector2(particle.Scale));
                    }
                }
            }

            if (_jointSparkTexture != null)
            {
                for (int i = 0; i < _jointSparks.Length; i++)
                {
                    ref readonly JointSparkParticle particle = ref _jointSparks[i];
                    if (particle.Active && particle.HasMoved)
                    {
                        DrawJointSpark(particle);
                    }
                }
            }
        }

        private void SpawnShiny(Vector3 position, int subType)
        {
            for (int i = 0; i < _shinyParticles.Length; i++)
            {
                if (_shinyParticles[i].Active)
                    continue;

                _shinyParticles[i] = new ShinyParticle
                {
                    Active = true,
                    SubType = subType,
                    Position = position,
                    Light = Vector3.One,
                    LifeTicks = 18f,
                    Scale = 0f,
                    Rotation = 0f
                };
                return;
            }
        }

        private void SpawnSpark(Vector3 position, Vector3 angle)
        {
            for (int i = 0; i < _sparkParticles.Length; i++)
            {
                if (_sparkParticles[i].Active)
                    continue;

                float scale = (MuGame.Random.Next(4) + 4) * 0.1f;
                float lifeTicks = MuGame.Random.Next(16) + 24;
                float rotationZ = MuGame.Random.Next(360);
                float gravity = MuGame.Random.Next(16) + 6;
                float speed = (MuGame.Random.Next(20) + 20) * 0.1f;
                Matrix rotation = MathUtils.AngleMatrix(new Vector3(
                    angle.X,
                    angle.Y,
                    rotationZ));
                Vector3 velocity = MathUtils.VectorRotate(
                    new Vector3(0f, speed, 0f),
                    rotation);
                Vector3 secondaryVelocity = MathUtils.VectorRotate(velocity, rotation);

                _sparkParticles[i] = new SparkParticle
                {
                    Active = true,
                    Position = position,
                    Velocity = velocity,
                    SecondaryVelocity = secondaryVelocity,
                    Light = Vector3.One,
                    Gravity = gravity,
                    LifeTicks = lifeTicks,
                    Scale = scale
                };
                return;
            }
        }

        private void SpawnJointSpark(Vector3 position, Vector3 angle)
        {
            for (int i = 0; i < _jointSparks.Length; i++)
            {
                if (_jointSparks[i].Active)
                    continue;

                Matrix rotation = MathUtils.AngleMatrix(angle);
                Vector3 velocity = MathUtils.VectorRotate(
                    new Vector3(0f, -(MuGame.Random.Next(20) + 6), 0f),
                    rotation);

                _jointSparks[i] = new JointSparkParticle
                {
                    Active = true,
                    Position = position,
                    PreviousPosition = position,
                    Velocity = velocity,
                    Light = Vector3.One,
                    LifeTicks = MuGame.Random.Next(8) + 8,
                    Scale = 2f,
                    HasMoved = false
                };
                return;
            }
        }

        private void DrawJointSpark(in JointSparkParticle particle)
        {
            Vector3 previousProjected = GraphicsDevice.Viewport.Project(
                particle.PreviousPosition,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector3 currentProjected = GraphicsDevice.Viewport.Project(
                particle.Position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            if (previousProjected.Z < 0f || previousProjected.Z > 1f ||
                currentProjected.Z < 0f || currentProjected.Z > 1f)
            {
                return;
            }

            Vector2 previous = new(previousProjected.X, previousProjected.Y);
            Vector2 current = new(currentProjected.X, currentProjected.Y);
            Vector2 delta = current - previous;
            float length = delta.Length();
            if (length <= 0.01f)
                return;

            Vector3 midpoint = (particle.PreviousPosition + particle.Position) * 0.5f;
            Vector4 midpointClip = Vector4.Transform(
                midpoint,
                Camera.Instance.ViewProjection);
            if (midpointClip.W <= 0.001f)
                return;

            float projectionScale = GraphicsDevice.Viewport.Height *
                Camera.Instance.Projection.M22 * 0.5f / midpointClip.W;
            float width = MathF.Abs(particle.Scale * projectionScale);
            if (!float.IsFinite(width) || width <= 0f)
                return;

            Texture2D? texture = _jointSparkTexture;
            if (texture == null)
                return;

            _spriteBatch!.Draw(
                texture,
                (previous + current) * 0.5f,
                null,
                new Color(particle.Light),
                MathF.Atan2(delta.Y, delta.X),
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                new Vector2(
                    length / texture.Width,
                    width / texture.Height),
                SpriteEffects.None,
                MathHelper.Clamp(midpointClip.Z / midpointClip.W, 0f, 1f));
        }

        private void DrawBoneSprite(
            Matrix[] bones,
            int boneIndex,
            float scale,
            Vector3 light,
            float rotation,
            Texture2D? texture)
        {
            if (texture == null || !TryGetBonePosition(bones, boneIndex, out Vector3 position))
                return;

            DrawWorldSprite(texture, position, light, rotation, new Vector2(scale));
        }

        private bool TryGetBonePosition(Matrix[] bones, int boneIndex, out Vector3 position)
        {
            position = Vector3.Zero;
            boneIndex = _owner.ResolveEffectBoneIndex(boneIndex);
            if ((uint)boneIndex >= (uint)bones.Length)
                return false;

            position = Vector3.Transform(
                bones[boneIndex].Translation,
                _owner.EffectWorldPosition);
            return true;
        }

        private void DrawWorldSprite(
            Texture2D texture,
            Vector3 position,
            Vector3 light,
            float rotationDegrees,
            Vector2 scale)
        {
            Vector3 projected = GraphicsDevice.Viewport.Project(
                position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;
            Vector3 projectedWidth = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Right * (texture.Width * scale.X),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector3 projectedHeight = GraphicsDevice.Viewport.Project(
                position + Camera.Instance.Up * (texture.Height * scale.Y),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            Vector2 spriteScale = new(
                MathF.Abs(projectedWidth.X - projected.X) / texture.Width,
                MathF.Abs(projectedHeight.Y - projected.Y) / texture.Height);
            if (!float.IsFinite(spriteScale.X) || !float.IsFinite(spriteScale.Y) ||
                spriteScale.X <= 0f || spriteScale.Y <= 0f)
            {
                return;
            }

            _spriteBatch!.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(light),
                -MathHelper.ToRadians(rotationDegrees),
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }

        private void ClearParticles()
        {
            Array.Clear(_shinyParticles, 0, _shinyParticles.Length);
            Array.Clear(_sparkParticles, 0, _sparkParticles.Length);
            Array.Clear(_jointSparks, 0, _jointSparks.Length);
        }
    }
}
