#nullable enable
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects.Cloth
{
    /// <summary>
    /// Physics cloth attached to its parent <see cref="ModelObject"/> (any object with a skeleton): the port of
    /// o->m_pCloth for monsters / NPCs. Moved and drawn at render time, once per frame, like RenderCharacter
    /// (create on first use, Move2(0.005, 5), Render; DeleteCloth and re-create when it tears).
    /// </summary>
    public sealed class PhysicsClothObject : EffectObject
    {
        private readonly SourceClothSet _set;

        public PhysicsClothObject(params ClothDefinition[] definitions)
        {
            _set = new SourceClothSet(definitions);
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-400f, -400f, -400f), new Vector3(400f, 400f, 400f));
        }

        /// <summary>glColor of CPhysicsCloth::Render; white unless the caller passes a colour (CloakLight).</summary>
        public Vector3 Color { get; set; } = Vector3.One;

        /// <summary>Move2 time step: 0.005, 0 freezes the cloth (stun / sleep).</summary>
        public float TimeStep { get; set; } = 0.005f;

        public override async Task Load()
        {
            await base.Load();
            await _set.LoadAsync();
        }

        protected override void OnWorldChanged(Client.Main.Controls.WorldControl newWorld, Client.Main.Controls.WorldControl prevWorld)
        {
            base.OnWorldChanged(newWorld, prevWorld);
            _set.Reset();
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_set.HasTextures && Parent is ModelObject owner && owner.Model != null && !owner.Hidden && owner.Visible &&
                owner.GetBoneTransforms() is { Length: > 0 })
            {
                _set.Move(owner, TimeStep);
                _set.Draw(GraphicsDevice, Color, TotalAlpha);
            }

            base.DrawAfter(gameTime);
        }
    }
}
