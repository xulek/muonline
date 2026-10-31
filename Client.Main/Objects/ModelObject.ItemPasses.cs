using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Client.Main.Objects
{
    public partial class ModelObject
    {
        private static readonly ConcurrentDictionary<string, Texture2D> s_itemPassTextures = new();

        /// <summary>Loads (once) and returns an effect texture used by item passes; null until ready.</summary>
        protected static Texture2D GetItemPassTexture(string path)
        {
            if (s_itemPassTextures.TryGetValue(path, out var texture))
                return texture;

            s_itemPassTextures[path] = null;
            _ = LoadItemPassTextureAsync(path);
            return null;
        }

        private static async Task LoadItemPassTextureAsync(string path)
        {
            try
            {
                s_itemPassTextures[path] = await TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                // A missing effect texture only skips the pass.
            }
        }

        /// <summary>
        /// RenderPartObjectBody (ZzzObject.cpp): Storm Crow, Thunder Hawk and Hurricane armor parts
        /// first draw an opaque RENDER_TEXTURE | RENDER_CHROME pass with BITMAP_CHROME+1 (bab2), then
        /// the regular texture on top, so the chrome shows through the cut-out parts.
        /// </summary>
        protected void DrawArmorSetChromeUnderlay()
        {
            if (MaterialItemGroup < 8 || MaterialItemGroup > 11 || Hidden || !Visible || Model?.Meshes == null)
                return;

            Vector3 tint;
            switch (MaterialItemIndex)
            {
                case 15: tint = Vector3.One; break;                       // Storm Crow
                case 20: tint = new Vector3(0.85f, 0.85f, 1.2f); break;   // Thunder Hawk
                case 23: tint = new Vector3(0.3f, 0.8f, 1f); break;       // Hurricane
                default: return;
            }

            Texture2D chrome = GetItemPassTexture("Effect/bab2.jpg");
            if (chrome == null)
                return;

            for (int mesh = 0; mesh < Model.Meshes.Length; mesh++)
                DrawMeshItemOverlay(mesh, ItemOverlayMode.Chrome, tint, chrome, blend: ItemOverlayBlend.Opaque);
        }

        // ───────────── SourceMain ItemObjectAttribute (ZzzObject.cpp) ─────────────

        private (int Group, int Index) _attributeItem = (-1, -1);

        /// <summary>
        /// Blend mesh (texture index drawn additively, -2 = every mesh), its animated light and
        /// texture jitter, as set every frame by ItemObjectAttribute for special weapons.
        /// </summary>
        protected void ApplyItemObjectAttribute(double timeMs)
        {
            int group = MaterialItemGroup, index = MaterialItemIndex;
            float pulse3 = (float)Math.Sin(timeMs * 0.004) * 0.3f + 0.7f;
            float pulse2 = (float)Math.Sin(timeMs * 0.004) * 0.2f + 0.9f;
            var random = MuGame.Random;

            int blendMesh = -1;
            float light = 1f;
            Vector2 uvJitter = Vector2.Zero;
            switch ((group, index))
            {
                case (4, 16): // Saint Crossbow
                case (5, 8):  // Staff of Destruction
                    blendMesh = -2; light = pulse2; break;
                case (5, 7):  // Chaos Lightning Staff
                    blendMesh = 1; light = random.Next(11) * 0.1f; break;
                case (5, 6):  // Staff of Resurrection
                case (4, 6):  // Chaos Nature Bow
                case (4, 13): // Bluewing Crossbow
                case (4, 14): // Aquagold Crossbow
                    blendMesh = -2; light = pulse3; break;
                case (5, 11): // Staff of Kundun
                    blendMesh = 2; light = pulse3; break;
                case (2, 4):  // Crystal Morning Star
                    blendMesh = 1; light = (float)Math.Sin(timeMs * 0.004) * 0.2f + 0.8f; break;
                case (2, 5):  // Crystal Sword
                    blendMesh = 0; break;
                case (2, 6):  // Chaos Dragon Axe
                case (0, 10): // Light Saber
                case (3, 0):  // Spear
                case (6, 11): // Serpent Shield
                case (6, 12): // Bronze Shield
                case (6, 13): // Dragon Shield
                    blendMesh = 1; light = pulse3; break;
                case (0, 14): // Lighting Sword
                    blendMesh = 1; light = pulse3; uvJitter = new Vector2(random.Next(10) * 0.1f, 0f); break;
                case (6, 14): // Legendary Shield
                    blendMesh = 1; uvJitter = new Vector2(random.Next(10) * 0.1f, random.Next(10) * 0.1f); break;
                case (5, 5):  // Legendary Staff
                    blendMesh = 2; uvJitter = new Vector2(random.Next(10) * 0.1f, random.Next(10) * 0.1f); break;
                case (5, 9):  // Dragon Soul Staff
                case (0, 5):  // Blade
                case (0, 13): // Double Blade
                case (13, 0): // Guardian Angel
                    blendMesh = 1; break;
                case (5, 0):  // Skull Staff
                    blendMesh = 2; break;
                case (14, 17): // Devil's Eye: BlendMesh 1, BlendMeshLight = sin * 10 + 15.65, V scroll
                    blendMesh = 1;
                    light = MathF.Min(4f, (float)Math.Sin(timeMs * 0.002) * 10f + 15.65f);
                    uvJitter = new Vector2(0f, (float)((long)timeMs % 2000 * 0.0005));
                    break;
                default:
                    if (_attributeItem != (group, index))
                    {
                        _attributeItem = (group, index);
                        BlendMesh = -1;
                        BlendMeshLight = 1f;
                        TextureCoordinateOffset = Vector2.Zero;
                        TextureCoordinateOffsetMeshIndex = -1;
                    }
                    return;
            }

            _attributeItem = (group, index);
            BlendMesh = blendMesh;
            // Quantised so the baked vertex light is not rebuilt for imperceptible changes.
            BlendMeshLight = MathF.Round(light * 50f) / 50f;
            TextureCoordinateOffset = uvJitter;
            TextureCoordinateOffsetMeshIndex = uvJitter == Vector2.Zero ? -1 : blendMesh;
        }


        // ───────────── RenderPartObjectBody passes of special items ─────────────

        private Player.WeaponMeshPasses.Spec SpecialItemSpec => Player.WeaponMeshPasses.Find(MaterialItemGroup, MaterialItemIndex);

        /// <summary>Meshes the original never draws in the base pass of a special item.</summary>
        protected bool IsSpecialItemOverlayOnlyMesh(int mesh) => SpecialItemSpec?.OverlayOnly(mesh) == true;

        /// <summary>Draws the extra RENDER_BRIGHT / chrome / metal passes of special items.</summary>
        protected void DrawSpecialItemPasses(GameTime gameTime)
        {
            var spec = SpecialItemSpec;
            if (spec == null || Hidden || !Visible || Model?.Meshes == null || Status != Models.GameControlStatus.Ready)
                return;

            double time = gameTime.TotalGameTime.TotalMilliseconds;
            foreach (var pass in spec.Passes)
            {
                if (pass.Mesh < 0 || pass.Mesh >= Model.Meshes.Length)
                    continue;

                Texture2D texture = pass.Texture != null ? GetItemPassTexture(pass.Texture) : null;
                if (pass.Texture != null && texture == null)
                    continue;

                ItemOverlayMode mode = pass.Mode switch
                {
                    Player.WeaponMeshPasses.Mode.Chrome => ItemOverlayMode.Chrome,
                    Player.WeaponMeshPasses.Mode.ClampedTexture => ItemOverlayMode.ClampedTexture,
                    Player.WeaponMeshPasses.Mode.Metal => ItemOverlayMode.Metal,
                    Player.WeaponMeshPasses.Mode.Chrome4 => ItemOverlayMode.Chrome4,
                    Player.WeaponMeshPasses.Mode.Chrome5 => ItemOverlayMode.Chrome5,
                    _ => ItemOverlayMode.Texture,
                };
                ItemOverlayBlend blend = pass.Opaque ? ItemOverlayBlend.Opaque
                    : pass.BlendAlpha >= 0f ? ItemOverlayBlend.Alpha
                    : ItemOverlayBlend.Additive;
                DrawMeshItemOverlay(pass.Mesh, mode, pass.Color(time), texture, pass.Uv?.Invoke(time) ?? Vector2.Zero,
                    blend, pass.BlendAlpha >= 0f ? pass.BlendAlpha : 1f);
            }
        }

        // ───────────── PC-room helper plate set (o->m_bpcroom) ─────────────

        /// <summary>
        /// Plate helm/armor/pants/gloves/boots of PC-room helpers (Leo, Luke): mesh 0 gets
        /// RENDER_BRIGHT | RENDER_CHROME and RENDER_BRIGHT | RENDER_METAL passes with eff_lighting.
        /// </summary>
        /// <remarks>Only mesh 0 is drawn, with BodyLight (0.9, 0.7, 1.0).</remarks>
        public bool PcRoomEffect
        {
            get => _pcRoomEffect;
            set
            {
                if (_pcRoomEffect == value)
                    return;
                _pcRoomEffect = value;
                InvalidateBuffers(BufferFlagAll);
            }
        }

        private bool _pcRoomEffect;

        protected void DrawPcRoomPasses()
        {
            if (!PcRoomEffect || Hidden || !Visible || Model?.Meshes == null || Model.Meshes.Length == 0 ||
                Status != Models.GameControlStatus.Ready)
                return;

            var texture = GetItemPassTexture("Effect/eff_lighting.jpg");
            if (texture == null)
                return;

            var tint = new Vector3(0.9f, 0.7f, 1f);
            DrawMeshItemOverlay(0, ItemOverlayMode.Chrome, tint, texture);
            DrawMeshItemOverlay(0, ItemOverlayMode.Metal, tint, texture);
        }

        // ───────────── Pulsing texture-script ("_R") blend meshes ─────────────

        /// <summary>
        /// Meshes flagged bright by their texture script render as blend meshes with o->BlendMeshLight.
        /// RenderPartObjectEffect / RenderCharacter animate that value for a few parts:
        /// Red Spirit helm/pants sin(t*0.001)*0.4+0.6, Divine helm/gloves/boots 1, Dark Lord helms
        /// sin(t*0.001)*0.1+0.7, Wings of Dragon (sin(t*0.001)+1)/4, Wings of Soul sin(t*0.001)+1.1.
        /// </summary>
        protected void ApplyScriptBlendLight(double timeMs, bool darkLordHelm = false)
        {
            if (_meshes == null)
                return;

            bool hasScriptBlend = false;
            for (int i = 0; i < _meshes.Length && !hasScriptBlend; i++)
                hasScriptBlend = _meshes[i].BlendByScript;
            if (!hasScriptBlend)
                return;

            float s = (float)Math.Sin(timeMs * 0.001);
            float light = (MaterialItemGroup, MaterialItemIndex) switch
            {
                (7, 24) or (9, 24) => s * 0.4f + 0.6f,
                (12, 5) => (s + 1f) / 4f,
                (12, 4) => s + 1.1f,
                _ when darkLordHelm => s * 0.1f + 0.7f,
                _ => 1f,
            };
            // Quantised so the baked vertex light is not rebuilt for imperceptible changes.
            BlendMeshLight = MathF.Round(light * 50f) / 50f;
        }
    }
}
