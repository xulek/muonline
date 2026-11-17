using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Atlans
{
    public class PortalObject : ModelObject
    {
        // SourceMain sets Velocity to 0.05 on a 25 Hz update loop.
        private const float PortalAnimationFramesPerSecond = 0.05f * 25f;

        // Sprite/blend-mesh glow only in the source; a soft cyan light follows the blend pulse.
        private readonly MapObjectGlowLight _glowLight;

        public PortalObject()
        {
            _glowLight = new MapObjectGlowLight(this, Constants.TERRAIN_SCALE * 3f, 0.8f);
        }

        protected override bool AllowMapObjectInstancing => false;
        protected override bool RequiresPerFrameAnimation => true;
        protected override bool PreserveBlendMeshesInLowQuality => true;

        public override bool IsStaticForCaching => false;

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            BlendState = BlendState.NonPremultiplied;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = true;
            Model = await BMDLoader.Instance.Prepare($"Object8/Object{idx}.bmd");
            RemoveDuplicateTerminalFrame();

            CurrentAction = 0;
            AnimationSpeed = PortalAnimationFramesPerSecond;
            ContinuousAnimation = true;

            await base.Load();

            _glowLight.SetPosition(WorldPosition.Translation + new Vector3(0f, 0f, 100f));
            _glowLight.Register();
        }

        public override void Update(GameTime gameTime)
        {
            if (Status == GameControlStatus.Ready)
            {
                float worldTimeMilliseconds = (float)gameTime.TotalGameTime.TotalMilliseconds;
                BlendMeshLight = MathF.Sin(worldTimeMilliseconds * 0.004f) * 0.3f + 0.5f;
                if (_glowLight.Step(gameTime))
                {
                    float glow = 0.4f + BlendMeshLight * 0.6f;
                    _glowLight.SetColor(new Vector3(glow * 0.3f, glow * 0.65f, glow));
                }
            }

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }

        private void RemoveDuplicateTerminalFrame()
        {
            var action = Model?.Actions is { Length: > 0 } actions ? actions[0] : null;
            if (action == null || action.NumAnimationKeys <= 1 || Model?.Bones == null)
                return;

            int lastFrame = action.NumAnimationKeys - 1;
            bool hasBoneData = false;
            foreach (var bone in Model.Bones)
            {
                if (bone?.Matrixes is not { Length: > 0 } matrices)
                    continue;

                var matrix = matrices[0];
                if (matrix.Position == null || matrix.Rotation == null ||
                    matrix.Position.Length <= lastFrame || matrix.Rotation.Length <= lastFrame)
                {
                    continue;
                }

                hasBoneData = true;
                if (!AreEqual(matrix.Position[0], matrix.Position[lastFrame]) ||
                    !AreEqual(matrix.Rotation[0], matrix.Rotation[lastFrame]))
                {
                    return;
                }
            }

            // Object41 stores the first pose again as the terminal key. The animation
            // sampler loops with modulo NumAnimationKeys, so retaining that key creates
            // one full interval with no rotation at the loop boundary.
            if (hasBoneData)
                action.NumAnimationKeys = lastFrame;
        }

        private static bool AreEqual(System.Numerics.Vector3 first, System.Numerics.Vector3 second)
        {
            return MathF.Abs(first.X - second.X) < 0.0001f &&
                   MathF.Abs(first.Y - second.Y) < 0.0001f &&
                   MathF.Abs(first.Z - second.Z) < 0.0001f;
        }
    }
}
