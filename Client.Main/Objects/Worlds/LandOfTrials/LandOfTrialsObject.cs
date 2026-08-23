#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.LandOfTrials
{
    /// <summary>
    /// GMHuntingGround.cpp map-object behaviours:
    /// Types 27/54 mesh light pulse (BodyLight *= sinf(Timer+WT*0.0012)*0.5+0.9),
    /// Type 42 warm flicker terrain light, Type 49 torch bone-3 red flame sprite.
    /// </summary>
    public class LandOfTrialsObject : MapTileObject
    {
        private readonly Controls.DynamicLight? _warmLight;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool AllowMapObjectInstancing => false;
        public override bool IsStaticForCaching => false;

        public LandOfTrialsObject()
        {
            if (Type == 42)
            {
                _warmLight = new Controls.DynamicLight
                {
                    Owner = this,
                    Radius = Constants.TERRAIN_SCALE * 4f,
                    Intensity = 1f
                };
            }
        }

        public override async Task Load()
        {
            await base.Load();

            if (_warmLight != null && World?.Terrain != null)
                World.Terrain.AddDynamicLight(_warmLight);
        }

        public override void Update(GameTime gameTime)
        {
            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;

            switch (Type)
            {
                case 27 or 54:
                    BlendMeshLight = MathF.Sin(totalMs * 0.0012f) * 0.5f + 0.9f;
                    break;

                case 42:
                    {
                        // Luminosity=(rand()%3+5)*0.1f -> (L*0.9, L*0.2, L*0.1) warm flicker
                        float luminosity = (MuGame.Random.Next(3) + 5) * 0.1f;
                        Light = new Vector3(luminosity * 0.9f, luminosity * 0.2f, luminosity * 0.1f);
                        if (_warmLight != null)
                        {
                            _warmLight.Position = WorldPosition.Translation;
                            _warmLight.Color = Light;
                        }

                        HiddenMesh = -2;
                    }
                    break;
            }

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;

            if (Type != 49 || !Visible || Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            // Torch bone 3: LIGHT sprite scale Lum*1.2+0.3 with Lum=sin(WT*0.002)*0.35+0.65, red tint
            float luminosity = MathF.Sin(totalMs * 0.002f) * 0.35f + 0.65f;

            Vector3 position = WorldPosition.Translation;
            var bones = GetBoneTransforms();
            if (bones != null && bones.Length > 3)
                position = Vector3.Transform(bones[3].Translation, WorldPosition);

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var device = GraphicsManager.Instance.GraphicsDevice;
            if (spriteBatch == null || device == null)
                return;

            var texture = Controllers.GraphicsManager.Instance.Pixel;
            Vector3 projected = device.Viewport.Project(
                position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            float scale = (luminosity * 1.2f + 0.3f) * 40f;
            var color = new Color(1f, luminosity * 0.3f, luminosity * 0.15f, 0.8f);

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
                    spriteBatch.Draw(texture, new Vector2(projected.X, projected.Y), null, color,
                        0f, Vector2.Zero, scale, SpriteEffects.None, projected.Z);
                }
            }
            else
            {
                spriteBatch.Draw(texture, new Vector2(projected.X, projected.Y), null, color,
                    0f, Vector2.Zero, scale, SpriteEffects.None, projected.Z);
            }
        }

        public override void Dispose()
        {
            if (_warmLight != null && World?.Terrain != null)
                World.Terrain.RemoveDynamicLight(_warmLight);

            base.Dispose();
        }
    }
}




