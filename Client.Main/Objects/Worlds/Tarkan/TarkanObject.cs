using System;
using System.IO;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements the complete map-specific object animations and visual effects
    /// from SourceMain5.2's WD_8TARKAN branch (ZzzObject.cpp).
    /// </summary>
    public class TarkanObject : MapTileObject
    {
        private readonly TarkanObjectVisualEffect _visualEffect;
        private Texture2D _chromeTexture;
        private TarkanSmokeParticleSystem _smokeParticleSystem;
        private TarkanVentParticleSystem _ventParticleSystem;

        // SourceMain AddTerrainLight: type 4 -> white range 3; type 7 -> warm range 3;
        // types 61/65/66 -> warm range 2
        private readonly Controls.DynamicLight? _terrainLight;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        protected override bool ForceTwoSidedMeshes =>
            Type == 8 || Type == 6 || Type == 11 || Type == 12 || Type == 13 ||
            Type == 15 || Type == 16 || Type == 17 || Type == 18 || Type == 19 ||
            Type == 22 || Type == 33 || Type == 35 || Type == 73 || Type == 75 || Type == 79;

        public TarkanObject()
        {
            LightEnabled = true;
            _visualEffect = new TarkanObjectVisualEffect(this);
            Children.Add(_visualEffect);

            float lightRangeTiles = Type == 4 || Type == 7 ? 3f : Type is 61 or 65 or 66 ? 2f : 0f;
            if (lightRangeTiles > 0f)
            {
                _terrainLight = new Controls.DynamicLight
                {
                    Owner = this,
                    Radius = lightRangeTiles * Constants.TERRAIN_SCALE,
                    Intensity = 1f
                };
            }
        }

        public override async Task Load()
        {
            BlendMeshState = BlendState.Additive;

            // Type 8 (Flag): 4x animation speed and NonPremultiplied alpha
            if (Type == 8)
            {
                AnimationSpeed = 4.0f;
                BlendState = BlendState.NonPremultiplied;
            }
            // Foliage, grass quads, dead trees, cactus, and scrolling sand layers: NonPremultiplied alpha
            else if (Type == 6 || Type == 11 || Type == 12 || Type == 13 ||
                     Type == 15 || Type == 16 || Type == 17 || Type == 18 || Type == 19 ||
                     Type == 22 || Type == 33 || Type == 35 || Type == 73 || Type == 75 || Type == 79)
            {
                BlendState = BlendState.NonPremultiplied;
            }

            await base.Load();

            if (_terrainLight != null && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_terrainLight);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            int sourceType = Type;
            if (sourceType == 81)
            {
                _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Chrome01.jpg");
            }
            else if (sourceType == 60 || sourceType == 70 || sourceType == 76)
            {
                if (_smokeParticleSystem == null)
                {
                    _smokeParticleSystem = new TarkanSmokeParticleSystem(this);
                    Children.Add(_smokeParticleSystem);
                }
            }
            else if (sourceType == 83)
            {
                if (_ventParticleSystem == null)
                {
                    _ventParticleSystem = new TarkanVentParticleSystem(this);
                    Children.Add(_ventParticleSystem);
                }
            }
        }

        public override void Update(GameTime gameTime)
        {
            ApplySourceAnimation(Type, gameTime);
            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
                return;

            base.Draw(gameTime);

            if (Type == 81)
                DrawChromePass(gameTime);
        }

        private void DrawChromePass(GameTime gameTime)
        {
            if (_chromeTexture == null || Model?.Meshes == null)
                return;

            var prevBlend = GraphicsDevice.BlendState;
            try
            {
                GraphicsDevice.BlendState = BlendState.Additive;
                for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
                {
                    Texture2D originalTexture = GetMeshTexture(meshIndex);
                    try
                    {
                        SetMeshTextureOverride(meshIndex, _chromeTexture);
                        DrawMesh(meshIndex);
                    }
                    finally
                    {
                        if (originalTexture != null)
                            SetMeshTextureOverride(meshIndex, originalTexture);
                        else
                            ClearMeshTextureOverride(meshIndex);
                    }
                }
            }
            finally
            {
                GraphicsDevice.BlendState = prevBlend;
            }
        }

        private void UpdateTerrainLight(Vector3 light)
        {
            if (_terrainLight == null)
                return;

            _terrainLight.Position = WorldPosition.Translation;
            _terrainLight.Color = new Vector3(
                MathF.Min(1f, light.X),
                MathF.Min(1f, light.Y),
                MathF.Min(1f, light.Z));
        }

        public override void Dispose()
        {
            if (_terrainLight != null && World?.Terrain != null)
                World.Terrain.RemoveDynamicLight(_terrainLight);

            base.Dispose();
        }

        private void ApplySourceAnimation(int sourceType, GameTime gameTime)
        {
            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float angleDeg = MathHelper.ToDegrees(Angle.Z);

            switch (sourceType)
            {
                case 2:
                    BlendMesh = 0;
                    BlendMeshState = BlendState.Additive;
                    TextureCoordinateOffsetMeshIndex = 0;
                    TextureCoordinateOffset = new Vector2(-((int)totalMs % 1000) * 0.001f, 0f);
                    break;

                case 4:
                    {
                        float sine = MathF.Sin(totalMs * 0.002f) * 0.35f + 0.65f;
                        BlendMesh = 0;
                        BlendMeshLight = sine;
                        BlendMeshState = BlendState.Additive;
                        TextureCoordinateOffsetMeshIndex = 0;
                        TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0001f);
                        Light = new Vector3(sine, sine, sine);
                        UpdateTerrainLight(Light);
                    }
                    break;

                case 7:
                    {
                        float sine = MathF.Sin((totalMs + angleDeg * 100f) * 0.002f) * 0.35f + 0.65f;
                        BlendMesh = 0;
                        BlendMeshLight = sine;
                        BlendMeshState = BlendState.Additive;
                        TextureCoordinateOffsetMeshIndex = -1;
                        TextureCoordinateOffset = Vector2.Zero;
                        Light = new Vector3(sine, sine * 0.6f, sine * 0.2f);
                        UpdateTerrainLight(Light);
                    }
                    break;

                case 11:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 12:
                    {
                        float uv12 = -((int)totalMs % 50000) * 0.00005f;
                        BlendMesh = -1;
                        TextureCoordinateOffsetMeshIndex = -1;
                        TextureCoordinateOffset = new Vector2(uv12, uv12);
                    }
                    break;

                case 13:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 60:
                case 70:
                case 76:
                case 83:
                    HiddenMesh = -2;
                    break;

                case 61:
                case 65:
                case 66:
                    {
                        BlendMesh = 1;
                        BlendMeshState = BlendState.Additive;
                        TextureCoordinateOffsetMeshIndex = 1;
                        TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 1000) * 0.001f);
                        float sine = MathF.Sin(totalMs * 0.002f) * 0.35f + 0.65f;
                        BlendMeshLight = sine;
                        Light = new Vector3(sine, sine * 0.6f, sine * 0.2f);
                        UpdateTerrainLight(Light);
                    }
                    break;

                case 63:
                case 64:
                    HiddenMesh = -2;
                    break;

                case 72:
                    BlendMesh = 0;
                    BlendMeshState = BlendState.Additive;
                    TextureCoordinateOffsetMeshIndex = 0;
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 73:
                case 75:
                case 79:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;

                case 81:
                    BlendMesh = 0;
                    BlendMeshState = BlendState.Additive;
                    break;

                case 82:
                    BlendMesh = 0;
                    BlendMeshState = BlendState.Additive;
                    Light = Vector3.One;
                    break;

                default:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = Vector2.Zero;
                    break;
            }
        }
    }

    /// <summary>
    /// Renders glowing pulsing billboard sprites for Tarkan objects 63 and 64 (BITMAP_IMPACT).
    /// </summary>
    internal sealed class TarkanObjectVisualEffect : EffectObject
    {
        private readonly TarkanObject _owner;
        private Texture2D _impactTexture;

        public TarkanObjectVisualEffect(TarkanObject owner)
        {
            _owner = owner;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            IsTransparent = true;
            AffectedByTransparency = true;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            int sourceType = _owner.Type;
            if (sourceType == 63 || sourceType == 64)
            {
                _impactTexture = await TextureLoader.Instance.PrepareAndGetTexture("Object9/Impack03.jpg");
            }
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready ||
                _owner.Status != GameControlStatus.Ready || Camera.Instance == null)
            {
                return;
            }

            int sourceType = _owner.Type;
            if ((sourceType != 63 && sourceType != 64) || _impactTexture == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
                return;

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float angleDeg = MathHelper.ToDegrees(_owner.Angle.Z);
            float luminosity = MathF.Sin((totalMs + angleDeg * 5f) * 0.002f) * 0.3f + 0.7f;
            float scale = luminosity * 1.5f;

            Vector3 lightColor = sourceType == 64
                ? new Vector3(luminosity, luminosity * 0.32f, luminosity * 0.32f) // Red/orange
                : new Vector3(luminosity / 1.7f, luminosity, luminosity);          // Cyan/white

            // Position at Bone 2 or Object position
            Vector3 position = _owner.WorldPosition.Translation;
            var bones = _owner.GetBoneTransforms();
            if (bones != null && bones.Length > 2)
            {
                position = Vector3.Transform(bones[2].Translation, _owner.WorldPosition);
            }

            void drawSprite()
            {
                DrawBillboard(spriteBatch, _impactTexture, position, scale, lightColor, 0f);
            }

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState,
                    SamplerState.LinearClamp,
                    DepthState,
                    RasterizerState.CullNone))
                {
                    drawSprite();
                }
            }
            else
            {
                drawSprite();
            }
        }

        private void DrawBillboard(
            SpriteBatch spriteBatch,
            Texture2D texture,
            Vector3 position,
            float scale,
            Vector3 light,
            float rotation)
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

            Vector2 spriteScale = new Vector2(
                MathF.Abs(projectedWidth.X - projected.X) / texture.Width,
                MathF.Abs(projectedHeight.Y - projected.Y) / texture.Height);

            if (!float.IsFinite(spriteScale.X) || !float.IsFinite(spriteScale.Y) ||
                spriteScale.X <= 0f || spriteScale.Y <= 0f)
                return;

            spriteBatch.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(light) * _owner.TotalAlpha,
                -MathHelper.ToRadians(rotation),
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }
    }
}
