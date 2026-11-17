#nullable enable
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Common
{
    /// <summary>
    /// Plain map object whose type is a catalogued fire / glow source (see
    /// <see cref="MapObjectLightCatalog"/>): renders like <see cref="MapTileObject"/> and adds
    /// one static terrain light or a CreateFire emitter.
    /// </summary>
    public class MapLightObject : MapTileObject
    {
        private MapObjectLight? _mapLight;

        public override async Task Load()
        {
            _mapLight ??= MapObjectLight.TryAttach(this);
            await base.Load();
            _mapLight?.Register();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _mapLight?.Update(gameTime);
        }

        public override void Dispose()
        {
            _mapLight?.Unregister();
            base.Dispose();
        }
    }
}
