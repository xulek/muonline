using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Login
{
    public class TorchObject : ModelObject
    {
        private new ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<ModelObject>();

        // The flare sprite had no terrain light; one warm light per torch, static at the flare.
        private readonly MapObjectGlowLight _glowLight;

        public TorchObject()
        {
            BoundingBoxColor = Color.Blue;
            ParentBoneLink = 1;
            Children.Add(new FlareEffect()
            {
                Scale = 4f
            });
            _glowLight = new MapObjectGlowLight(this, 350f, 0.9f);
        }

        public override async Task Load()
        {
            var modelPath = Path.Join($"Object74", $"Object38.bmd");

            Model = await BMDLoader.Instance.Prepare(modelPath);

            if (Model == null)
                _logger?.LogDebug($"Can't load MapObject for model: {modelPath}");

            await base.Load();

            _glowLight.Register();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            if (!_glowLight.IsPositioned)
            {
                Matrix[] bones = GetBoneTransforms();
                Vector3 local = bones != null && bones.Length > 1 ? bones[1].Translation : Vector3.Zero;
                _glowLight.SetPosition(Vector3.Transform(local, WorldPosition));
            }

            if (!_glowLight.Step(gameTime))
                return;

            // Source flare pulse: (sin(WorldTime * 0.039) + 1) * 0.2 + 0.6.
            float luminosity = (MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * 0.039f) + 1f) * 0.2f + 0.6f;
            luminosity *= (MuGame.Random.Next(10) + 90) * 0.01f;
            _glowLight.SetColor(new Vector3(luminosity, luminosity * 0.7f, luminosity * 0.4f));
        }
    }
}
