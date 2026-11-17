using Client.Main.Objects.Worlds.Lorencia;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Dungeon
{
    /// <summary>
    /// Dungeon wall torches (source WD_1DUNGEON types 41/42): CreateFire(0) at a local
    /// offset, i.e. the BITMAP_FIRE emitter plus its range-4 terrain light.
    /// </summary>
    public sealed class DungeonTorchObject : MapTileObject
    {
        private readonly LorenciaObjectParticleEffect _fireEffect;

        public DungeonTorchObject()
        {
            LightEnabled = true;
            _fireEffect = new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                Vector3.Zero);
            Children.Add(_fireEffect);
        }

        public override async Task Load()
        {
            _fireEffect.LocalOffset = Type == 41
                ? new Vector3(0f, -30f, 240f)
                : new Vector3(0f, 0f, 190f);

            await base.Load();
        }
    }
}
