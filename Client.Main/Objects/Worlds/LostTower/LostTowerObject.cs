using System;
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

namespace Client.Main.Objects.Worlds.LostTower
{
    /// <summary>
    /// Implements the map-specific object animation from SourceMain5.2's
    /// WD_4LOSTTOWER branch. Map object indices are zero-based, while the
    /// original client addresses the corresponding ObjectNN model by NN.
    /// </summary>
    public class LostTowerObject : MapTileObject
    {
        private readonly LostTowerObjectVisualEffect _visualEffect;
        private Texture2D _chromeTexture;
        private Vector3 _skullDirection;
        private Vector3 _skullHeadAngle;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;

        public override bool IsStaticForCaching => false;

        public LostTowerObject()
        {
            LightEnabled = true;
            _visualEffect = new LostTowerObjectVisualEffect(this);
            Children.Add(_visualEffect);
        }

        public override async Task Load()
        {
            BlendMeshState = BlendState.Additive;
            await base.Load();
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            int sourceType = Type;
            if (sourceType == 3 || sourceType == 4 || sourceType == 19 || sourceType == 20)
                _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Chrome01.jpg");
        }

        public override void Update(GameTime gameTime)
        {
            int sourceType = Type;
            ApplySourceAnimation(sourceType, gameTime);

            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            // SourceMain5.2 calls CreateEffect(BITMAP_FLAME) with a 1/64
            // frame chance. ScrollOfFlameEffect is the client's exact
            // BITMAP_FLAME implementation and is intentionally non-damaging.
            float flameChance = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds * 25f / 64f,
                0f,
                1f);
            if (sourceType == 24 && MuGame.Random.NextDouble() < flameChance && World != null)
            {
                var flame = new ScrollOfFlameEffect(Position, false, false, Angle.Z);
                World.Objects.Add(flame);
                _ = flame.Load();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
                return;

            DrawChromePass(gameTime);
            base.Draw(gameTime);
        }

        private void DrawChromePass(GameTime gameTime)
        {
            if (_chromeTexture == null || Model?.Meshes == null)
                return;

            int sourceType = Type;
            if (sourceType != 3 && sourceType != 4 && sourceType != 19 && sourceType != 20)
                return;

            int previousOffsetMesh = TextureCoordinateOffsetMeshIndex;
            Vector2 previousOffset = TextureCoordinateOffset;
            int streamMesh = sourceType == 3 || sourceType == 4 ? 1 : 2;
            try
            {
                TextureCoordinateOffsetMeshIndex = streamMesh;
                TextureCoordinateOffset = new Vector2(GetScrollOffset(gameTime), 0f);

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
                TextureCoordinateOffsetMeshIndex = previousOffsetMesh;
                TextureCoordinateOffset = previousOffset;
            }
        }

        private void ApplySourceAnimation(int sourceType, GameTime gameTime)
        {
            HiddenMesh = sourceType == 24 || sourceType == 25 ? -2 : -1;

            switch (sourceType)
            {
                case 3:
                case 4:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = Vector2.Zero;
                    break;

                case 19:
                case 20:
                    BlendMesh = 4;
                    TextureCoordinateOffsetMeshIndex = 4;
                    TextureCoordinateOffset = new Vector2(GetScrollOffset(gameTime), 0f);
                    break;

                case 18:
                case 23:
                    BlendMesh = 1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = Vector2.Zero;
                    break;

                default:
                    BlendMesh = -1;
                    TextureCoordinateOffsetMeshIndex = -1;
                    TextureCoordinateOffset = Vector2.Zero;
                    break;
            }

            if (sourceType == 38 || sourceType == 39)
                UpdateSkull(gameTime);
        }

        private static float GetScrollOffset(GameTime gameTime)
        {
            long milliseconds = (long)gameTime.TotalGameTime.TotalMilliseconds;
            return -(milliseconds % 1000L) * 0.001f;
        }

        private void UpdateSkull(GameTime gameTime)
        {
            if (World is WalkableWorldControl walkableWorld &&
                walkableWorld.Walker is { } walker &&
                IsSkullMovementAction(walker.CurrentAction))
            {
                Vector3 heroPosition = walker.Position;
                float dx = heroPosition.X - Position.X;
                float dy = heroPosition.Y - Position.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);

                if (_skullDirection.X < 0.1f && distance < 50f)
                {
                    _skullDirection = new Vector3(-dx * 0.4f, -dy * 0.4f, 0f);
                    _skullHeadAngle = new Vector3(-dy * 4f, -dx * 4f, 0f);
                }
            }

            _skullDirection *= 0.6f;
            _skullHeadAngle *= 0.6f;

            float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f;
            Position += _skullDirection * frameFactor;
            Angle += _skullHeadAngle;
        }

        private static bool IsSkullMovementAction(int action)
        {
            return action >= (int)PlayerAction.PlayerWalkMale &&
                   action <= (int)PlayerAction.PlayerRunRideWeapon ||
                   action == (int)PlayerAction.PlayerRageUniRun ||
                   action == (int)PlayerAction.PlayerRageUniRunOneRight;
        }
    }

    /// <summary>
    /// Recreates SourceMain5.2 CreateSprite calls for Lost Tower objects.
    /// The source creates transient sprites every render pass, so this class
    /// projects and draws them directly instead of accumulating child objects.
    /// </summary>
    internal sealed class LostTowerObjectVisualEffect : EffectObject
    {
        private readonly LostTowerObject _owner;
        private Texture2D _magicTexture;
        private Texture2D _lightningTexture;
        private MapObjectGlowLight _glowLight;

        public LostTowerObjectVisualEffect(LostTowerObject owner)
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
            if (sourceType == 19)
                _magicTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Magic_Ground2.jpg");

            if (sourceType == 20 || sourceType == 40)
                _lightningTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/lightning2.jpg");

            // The magic (19, red) / lightning (20, blue; 40, white) sprite glows get one terrain light.
            if (sourceType == 19 || sourceType == 20 || sourceType == 40)
            {
                _glowLight = new MapObjectGlowLight(this, Constants.TERRAIN_SCALE * 3f, sourceType == 40 ? 0.8f : 0.9f);
                _glowLight.Register();
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_glowLight == null || !_owner.Visible || _owner.Status != GameControlStatus.Ready)
                return;

            int sourceType = _owner.Type;
            if (!_glowLight.IsPositioned)
            {
                if (sourceType == 40)
                {
                    _glowLight.SetPosition(_owner.WorldPosition.Translation + new Vector3(0f, 0f, 260f));
                }
                else
                {
                    // Bone 21 carries the large (scale 1.5) sprite pair.
                    Matrix[] bones = _owner.GetBoneTransforms();
                    if (bones == null || bones.Length <= 21)
                        return;
                    _glowLight.SetPosition(Vector3.Transform(bones[21].Translation, _owner.WorldPosition));
                }
            }

            if (!_glowLight.Step(gameTime))
                return;

            float luminosity = (MuGame.Random.Next(15) + 85) * 0.01f;
            _glowLight.SetColor(sourceType == 19
                ? new Vector3(luminosity, luminosity * 0.25f, luminosity * 0.05f)
                : sourceType == 20
                    ? new Vector3(luminosity * 0.4f, luminosity * 0.8f, luminosity)
                    : new Vector3(luminosity * 0.85f, luminosity * 0.9f, luminosity));
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
            Texture2D texture = sourceType == 19 ? _magicTexture : _lightningTexture;
            if ((sourceType != 19 && sourceType != 20 && sourceType != 40) || texture == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
                return;

            float rotation = (int)(gameTime.TotalGameTime.TotalSeconds * 100f) % 360;
            float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;
            Vector3 light = sourceType == 19
                ? new Vector3(luminosity, luminosity * 0.2f, 0f)
                : sourceType == 20
                    ? new Vector3(luminosity * 0.4f, luminosity * 0.8f, luminosity)
                    : new Vector3(luminosity);

            void drawSprites()
            {
                if (sourceType == 40)
                {
                    Vector3 position = _owner.WorldPosition.Translation + new Vector3(0f, 0f, 260f);
                    DrawBillboard(spriteBatch, texture, position, 2.5f, light, rotation);
                    DrawBillboard(spriteBatch, texture, position, 2.5f, light, -rotation);
                    return;
                }

                Matrix[] bones = _owner.GetBoneTransforms();
                if (bones == null)
                    return;

                DrawBoneSprites(spriteBatch, texture, bones, 15, 0.3f, light, rotation);
                DrawBoneSprites(spriteBatch, texture, bones, 19, 0.3f, light, rotation);
                DrawBoneSprites(spriteBatch, texture, bones, 21, 1.5f, light, rotation);
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
                    drawSprites();
                }
            }
            else
            {
                drawSprites();
            }
        }

        private void DrawBoneSprites(
            SpriteBatch spriteBatch,
            Texture2D texture,
            Matrix[] bones,
            int boneIndex,
            float scale,
            Vector3 light,
            float rotation)
        {
            if ((uint)boneIndex >= (uint)bones.Length)
                return;

            Vector3 position = Vector3.Transform(
                bones[boneIndex].Translation,
                _owner.WorldPosition);

            DrawBillboard(spriteBatch, texture, position, scale, light, rotation);
            DrawBillboard(spriteBatch, texture, position, scale, light, -rotation);
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

            // Source RenderSprite builds the quad in camera space from the
            // bitmap's pixel dimensions. Project those camera-space offsets
            // instead of using a distance heuristic; the latter changes the
            // effect size and position with camera zoom/FOV.
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
