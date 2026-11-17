using Client.Data;
using Client.Main.Content;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class MerchantAnimalObject : ModelObject
    {
        private readonly LorenciaLightSpriteEffect _firstLight = new();
        private readonly LorenciaLightSpriteEffect _secondLight = new();
        private readonly MapObjectGlowLight _lanternLight;

        public MerchantAnimalObject()
        {
            LightEnabled = true;
            Children.Add(_firstLight);
            Children.Add(_secondLight);
            _lanternLight = new MapObjectGlowLight(this, Constants.TERRAIN_SCALE * 3f, 0.8f);
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.MerchantAnimal01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/MerchantAnimal{idx}.bmd");
            await base.Load();
            _lanternLight.Register();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= 57)
                return;

            float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;
            Vector3 light = new(luminosity * 0.6f, luminosity * 0.3f, luminosity * 0.1f);
            float scale = luminosity * 5f;

            _firstLight.Position = Vector3.Transform(Vector3.Zero, bones[48]);
            _secondLight.Position = Vector3.Transform(Vector3.Zero, bones[57]);
            _firstLight.Light = light;
            _secondLight.Light = light;
            _firstLight.Scale = scale;
            _secondLight.Scale = scale;

            // One terrain light for both lantern sprites, static between them.
            if (!_lanternLight.IsPositioned)
                _lanternLight.SetPosition(Vector3.Transform(
                    (_firstLight.Position + _secondLight.Position) * 0.5f,
                    WorldPosition));
            if (_lanternLight.Step(gameTime))
                _lanternLight.SetColor(new Vector3(luminosity, luminosity * 0.55f, luminosity * 0.25f));
        }
    }
}
