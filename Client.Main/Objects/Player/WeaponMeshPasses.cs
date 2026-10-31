using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Player
{
    /// <summary>
    /// Fixed extra mesh passes of special weapons and shields, ported from RenderPartObjectBody
    /// (ZzzObject.cpp, SourceMain5.2). Each pass is an additive RENDER_BRIGHT draw of one mesh.
    /// </summary>
    internal static class WeaponMeshPasses
    {
        internal enum Mode
        {
            Texture,         // RENDER_TEXTURE | RENDER_BRIGHT or BlendMesh == mesh
            Chrome,          // RENDER_BRIGHT | RENDER_CHROME (Chrome01 or explicit texture)
            ClampedTexture,  // RENDER_TEXTURE | RENDER_BRIGHT with an explicit clamped effect texture
            Metal,           // RENDER_TEXTURE | RENDER_METAL without RENDER_BRIGHT (opaque)
            Chrome4,         // RENDER_BRIGHT | RENDER_CHROME4
            Chrome5,         // RENDER_BRIGHT | RENDER_CHROME5
        }

        internal sealed class Pass
        {
            public int Mesh;
            public Mode Mode;
            public Func<double, Vector3> Color = static _ => Vector3.One;
            public Func<double, Vector2> Uv;
            public string Texture;
            public bool Opaque;
            /// <summary>&gt;= 0: alpha-blended pass with this alpha (EnableAlphaTest).</summary>
            public float BlendAlpha = -1f;
        }

        internal sealed class Spec
        {
            /// <summary>Meshes that the original does not draw in the base pass.</summary>
            public Func<int, bool> OverlayOnly = static _ => false;
            public Pass[] Passes = Array.Empty<Pass>();
        }

        private static float Sin(double x) => (float)Math.Sin(x);

        private static Pass Bright(int mesh, Func<double, Vector3> color = null, Func<double, Vector2> uv = null) =>
            new() { Mesh = mesh, Mode = Mode.Texture, Color = color ?? (static _ => Vector3.One), Uv = uv };

        private static Pass Chrome(int mesh, Func<double, Vector3> color = null, string texture = null) =>
            new() { Mesh = mesh, Mode = Mode.Chrome, Color = color ?? (static _ => Vector3.One), Texture = texture };

        private static Pass Sweep(int mesh, string texture, Func<double, Vector2> uv, Func<double, Vector3> color = null) =>
            new() { Mesh = mesh, Mode = Mode.ClampedTexture, Texture = texture, Uv = uv, Color = color ?? (static _ => Vector3.One) };

        private static Vector3 Grey(float value) => new(value);

        /// <summary>
        /// RenderMesh(mesh, RENDER_TEXTURE | RENDER_BRIGHT, ..., fLumi, ..., BITMAP_*_R) with
        /// fLumi = (sin(WorldTime * speed) + offset) * scale.
        /// </summary>
        private static Pass GlowPass(int mesh, string texture, double speed, float offset, float scale) =>
            new() { Mesh = mesh, Mode = Mode.Texture, Texture = texture, Color = t => Grey(MathF.Max(0f, (Sin(t * speed) + offset) * scale)) };

        private static Spec Glow(int mesh, string texture, double speed, float offset, float scale, bool brightMesh1 = false) =>
            new()
            {
                Passes = brightMesh1
                    ? new[] { GlowPass(mesh, texture, speed, offset, scale), Bright(1) }
                    : new[] { GlowPass(mesh, texture, speed, offset, scale) },
            };

        private static Pass Chrome4(int mesh, Func<double, Vector3> color = null) =>
            new() { Mesh = mesh, Mode = Mode.Chrome4, Color = color ?? (static _ => Vector3.One) };

        private static Pass Chrome5(int mesh, Func<double, Vector3> color = null) =>
            new() { Mesh = mesh, Mode = Mode.Chrome5, Color = color ?? (static _ => Vector3.One) };

        private static Pass Metal(int mesh) => new() { Mesh = mesh, Mode = Mode.Metal, Opaque = true };

        // RENDER_LIGHTMAP | RENDER_TEXTURE with BlendMesh == mesh texture: BMD::RenderMesh takes the
        // blend-mesh branch first, i.e. an additive pass of the given texture with scrolled UVs.
        private static Pass ScrollChrome(int mesh, Func<double, Vector2> uv, Func<double, Vector3> color = null) =>
            new() { Mesh = mesh, Mode = Mode.Texture, Texture = "Effect/Chrome01.jpg", Uv = uv, Color = color ?? (static _ => Vector3.One) };

        private static readonly Dictionary<(int Group, int Index), Spec> Specs = new()
        {
            // Swords
            [(0, 19)] = new Spec // Divine Sword of Archangel
            {
                OverlayOnly = m => m == 0,
                Passes = new[] { Metal(0), ScrollChrome(0, t => new Vector2(0f, (float)(t * 0.0001))) },
            },
            [(0, 20)] = new Spec // Knight Blade
            {
                Passes = new[]
                {
                    Bright(2, t => Grey(Sin(t * 0.0008) * 0.7f + 0.5f)),
                    Bright(1),
                    Chrome(3, _ => new Vector3(0.43f, 0.14f, 0.6f)),
                },
            },
            [(0, 21)] = new Spec // Dark Reign Blade
            {
                Passes = new[] { Bright(0, uv: t => new Vector2((float)(t * 0.0005), (float)(t * 0.0005))) },
            },
            [(0, 22)] = new Spec // Bone Blade
            {
                OverlayOnly = m => m != 0,
                Passes = new[]
                {
                    Sweep(0, "Effect/Chrome07.jpg", t => new Vector2((float)((long)t % 4000 * 0.0004 - 0.7), 0f),
                        _ => new Vector3(1f, 0.7f, 0.4f)),
                },
            },
            [(0, 23)] = new Spec // Explosion Blade
            {
                Passes = new[]
                {
                    Bright(0),
                    Bright(2, t => new Vector3(Sin(t * 0.0015) * 0.03f + 0.3f, Sin(t * 0.0015) * 0.03f + 0.3f, Sin(t * 0.0015) * 0.03f + 0.4f)),
                    Bright(1, _ => new Vector3(0.2f, 0.2f, 0.4f)),
                },
            },
            [(0, 25)] = new Spec // Sword Dancer
            {
                OverlayOnly = m => m == 1,
                Passes = new[]
                {
                    // RenderMesh(1, RENDER_TEXTURE, 0.5f, 0, ..., BITMAP_LAVA): 50 % alpha-blended lava.
                    new Pass { Mesh = 1, Mode = Mode.Texture, Texture = "Effect/Lava.jpg", BlendAlpha = 0.5f },
                    Bright(1, _ => Grey(0.7f), t => new Vector2(0f, (float)(t * 0.0009))),
                },
            },
            [(0, 26)] = new Spec // Flamberge
            {
                OverlayOnly = m => m == 1 || m == 3 || m == 4 || m == 5,
                Passes = new[]
                {
                    Bright(1, _ => new Vector3(1f, 0f, 0.2f)),
                    Chrome(1),
                    Bright(3),
                    Bright(4),
                    Bright(5, uv: t => new Vector2(0f, ((int)(t * 0.05) % 16 / 4) * 0.25f)),
                },
            },
            [(0, 27)] = new Spec { Passes = new[] { Chrome(1) } }, // Sword Breaker
            [(0, 28)] = new Spec // Imperial Sword
            {
                Passes = new[] { Bright(2), Bright(1, t => Grey(Sin(t * 0.001) * 0.6f + 0.4f)) },
            },
            [(0, 31)] = new Spec // Rune Blade
            {
                OverlayOnly = m => m == 0 || m == 2,
                Passes = new[]
                {
                    Bright(1, t => Grey(MathF.Max(0f, Sin(t * 0.01)))),
                    Chrome(0),
                    Bright(2, t => Grey(Sin(t * 0.001) * 0.5f + 0.5f), t => new Vector2((float)(t * 0.0001), (float)(-t * 0.0005))),
                },
            },

            // Maces / scepters
            [(2, 7)] = new Spec // Elemental Mace
            {
                Passes = new[] { Bright(2, t => Grey(MathF.Max(0f, Sin(t * 0.001) * 0.5f + 0.3f)), t => new Vector2((float)(t * 0.001), (float)(-t * 0.0005))) },
            },
            [(2, 8)] = new Spec { Passes = new[] { Bright(0, t => Grey(Sin(t * 0.001) * 0.6f + 0.4f)) } }, // Battle Scepter
            [(2, 9)] = new Spec // Master Scepter
            {
                Passes = new[]
                {
                    Bright(0, t => new Vector3(0.1f, 0.3f, 1f) * (Sin(t * 0.001) * 0.6f + 0.4f)),
                    new Pass { Mesh = 1, Mode = Mode.Texture, Texture = "Effect/Chrome01.jpg", Color = _ => new Vector3(0.6f, 0.8f, 1f), Uv = t => new Vector2(0f, (float)(t * 0.0003)) },
                },
            },
            [(2, 10)] = new Spec // Great Scepter
            {
                OverlayOnly = m => m == 2,
                Passes = new[] { Bright(2, uv: t => new Vector2((float)(t * 0.0005), (float)(t * 0.0005))) },
            },
            [(2, 11)] = new Spec { Passes = new[] { Chrome(1, t => Grey(Sin(t * 0.001) * 0.3f + 0.7f)) } }, // Lord Scepter
            [(2, 13)] = new Spec // Divine Scepter of Archangel
            {
                Passes = new[] { ScrollChrome(0, t => new Vector2(0f, (float)(-t * 0.0001))) },
            },
            [(2, 14)] = new Spec // Soleil Scepter
            {
                OverlayOnly = m => m != 0,
                Passes = new[] { Sweep(0, "Effect/Chrome06.jpg", t => new Vector2((float)((long)t % 4000 * 0.0002 - 0.3), (float)((long)t % 4000 * 0.0002 - 0.3))) },
            },
            [(2, 16)] = new Spec // Frost Mace
            {
                OverlayOnly = m => m == 1,
                Passes = new[] { Bright(1), Bright(3), Bright(2) },
            },

            // Spears
            [(3, 10)] = new Spec // Dragon Spear
            {
                Passes = new[] { Bright(1, uv: t => new Vector2((float)(t * 0.0001), (float)(t * 0.0005))) },
            },

            // Bows
            [(4, 18)] = new Spec // Divine Crossbow of Archangel
            {
                OverlayOnly = m => m == 0,
                Passes = new[] { Metal(0), ScrollChrome(0, t => new Vector2(0f, (float)(-t * 0.0001))) },
            },
            [(4, 19)] = new Spec // Great Reign Crossbow
            {
                Passes = new[]
                {
                    ScrollChrome(0, t => new Vector2((float)(-t * 0.0002), 0f), _ => new Vector3(0.8f, 0f, 0.8f)),
                    Chrome(0, texture: "Effect/bab2.jpg"),
                },
            },
            [(4, 21)] = new Spec // Sylph Wind Bow
            {
                OverlayOnly = m => m != 0,
                Passes = new[] { Sweep(0, "Effect/Chrome06.jpg", t => new Vector2((float)((long)t % 4000 * 0.0002 - 0.3), (float)((long)t % 4000 * 0.0002 - 0.3))) },
            },
            [(4, 22)] = new Spec // Albatross Bow
            {
                OverlayOnly = m => m == 1,
                Passes = new[] { Bright(1), Bright(2), Chrome5(2, _ => Grey(0.5f)) },
            },

            [(2, 12)] = new Spec { Passes = new[] { Bright(1), Bright(3) } }, // Great Lord Scepter
            [(2, 18)] = new Spec // Stryker Scepter
            {
                OverlayOnly = m => m == 1,
                Passes = new[] { Bright(1, _ => Grey(0.5f)) },
            },
            [(3, 11)] = new Spec // Beuroba
            {
                OverlayOnly = m => m <= 1,
                Passes = new[] { Bright(0, _ => Grey(0.4f)), Bright(1, _ => Grey(0.8f)) },
            },
            [(4, 20)] = new Spec // Arrow Viper Bow
            {
                OverlayOnly = m => m == 3 || m == 4,
                Passes = new[]
                {
                    Bright(3, t => Grey(Sin(t * 0.002) * 0.3f + 0.5f)),
                    Bright(4, t => Grey(Sin(t * 0.002) * 0.3f + 0.5f)),
                },
            },

            // Staffs
            [(5, 11)] = new Spec // Staff of Kundun
            {
                Passes = new[]
                {
                    Chrome(1, _ => Grey(0.2f)),
                    Bright(0, t => Grey(MathF.Max(0f, Sin(t * 0.005)))),
                    Chrome4(0),
                },
            },
            [(5, 18)] = new Spec { Passes = new[] { Bright(1) } }, // Demonic Stick
            [(5, 19)] = new Spec // Storm Blitz Stick
            {
                Passes = new[] { Bright(1, t => Grey(MathF.Max(0f, (Sin(t * 0.002) + 0.5f) * 0.5f))) },
            },
            [(5, 10)] = new Spec // Divine Staff of Archangel
            {
                OverlayOnly = m => m == 0,
                Passes = new[] { Metal(0), ScrollChrome(0, t => new Vector2(0f, (float)(-t * 0.0001))) },
            },
            [(5, 12)] = new Spec // Grand Viper Staff
            {
                OverlayOnly = m => m != 0,
                Passes = new[] { Sweep(0, "Effect/energy01.jpg", t => new Vector2((float)((long)t % 2000 * 0.0002 - 0.3), (float)((long)t % 2000 * 0.0002 - 0.3))) },
            },
            [(5, 13)] = new Spec // Platina Staff
            {
                OverlayOnly = m => m == 1,
                Passes = new[] { Bright(1, uv: t => new Vector2((float)(t * 0.0009), (float)(t * 0.0009))) },
            },
            [(5, 30)] = new Spec { Passes = new[] { Bright(1, _ => new Vector3(1f, 0.5f, 0.5f)) } }, // Deadly Staff
            [(5, 31)] = new Spec // Imperial Staff
            {
                Passes = new[]
                {
                    Bright(1),
                    // Three additive BITMAP_SOCKETSTAFF passes on mesh 0, BlendMeshLight = |sin| + 0.1.
                    new Pass { Mesh = 0, Mode = Mode.Texture, Texture = "Item/soketmagic_stape02.jpg",
                        Color = t => Grey(3f * (MathF.Abs(Sin(t * 0.001)) + 0.1f)) },
                },
            },
            [(5, 32)] = new Spec { Passes = new[] { Chrome(0), Bright(2), Bright(3), Bright(3) } },

            // Special potions / helpers (cash-shop and quest items), RenderPartObjectBody.
            [(14, 53)] = Glow(0, "Item/partCharge1/bujuck01alpa.jpg", 0.0015, 1.5f, 0.5f),
            [(14, 54)] = Glow(0, "Item/partCharge1/juju_R.jpg", 0.0015, 1.2f, 0.4f, brightMesh1: true),
            [(14, 58)] = Glow(0, "Item/partCharge1/expensiveitem01_R.jpg", 0.0015, 1f, 0.5f, brightMesh1: true),
            [(14, 59)] = Glow(0, "Item/partCharge1/expensiveitem02a_R.jpg", 0.0015, 1f, 0.5f, brightMesh1: true),
            [(14, 60)] = Glow(0, "Item/partCharge1/expensiveitem02b_R.jpg", 0.0015, 1f, 0.5f, brightMesh1: true),
            [(14, 61)] = Glow(0, "Item/partCharge1/expensiveitem03a_R.jpg", 0.0015, 1f, 0.5f),
            [(14, 62)] = Glow(0, "Item/partCharge1/expensiveitem03b_R.jpg", 0.0015, 1f, 0.5f),
            [(14, 83)] = Glow(0, "Item/partCharge1/expensiveitem03b_R.jpg", 0.0015, 1f, 0.5f),
            [(14, 65)] = Glow(0, "Item/deathbeamstone_R.jpg", 0.0015, 1.5f, 0.4f),
            [(14, 66)] = Glow(0, "Item/hellhorn_R.jpg", 0.0015, 1.5f, 0.4f),
            [(14, 67)] = Glow(0, "Item/phoenixfeather_R.tga", 0.0015, 1.5f, 0.4f),
            [(14, 68)] = new Spec // Eye of Abyssal
            {
                Passes = new[]
                {
                    GlowPass(1, "Item/Deye_R.jpg", 0.0015, 1.5f, 0.4f),
                    Chrome(1, _ => Grey(0.2f)),
                },
            },
            [(14, 91)] = Glow(0, "Item/partCharge3/alicecard_R.tga", 0.0015, 1.2f, 0.4f, brightMesh1: true),
            [(14, 92)] = Glow(0, "Item/partCharge3/jujug_R.jpg", 0.0015, 1.2f, 0.4f, brightMesh1: true),
            [(14, 93)] = Glow(0, "Item/partCharge3/jujul_R.jpg", 0.0015, 1.2f, 0.4f, brightMesh1: true),
            [(14, 95)] = Glow(0, "Item/partCharge3/jujum_R.jpg", 0.0015, 1.2f, 0.4f, brightMesh1: true),
            [(14, 145)] = Glow(0, "Item/partCharge8/rareitem_ticket_7_body.jpg", 0.0015, 1f, 0.5f),
            [(14, 146)] = Glow(0, "Item/partCharge8/rareitem_ticket_8_body.jpg", 0.0015, 1f, 0.5f),
            [(14, 147)] = Glow(0, "Item/partCharge8/rareitem_ticket_9_body.jpg", 0.0015, 1f, 0.5f),
            [(14, 148)] = Glow(0, "Item/partCharge8/rareitem_ticket_10_body.jpg", 0.0015, 1f, 0.5f),
            [(14, 149)] = Glow(0, "Item/partCharge8/rareitem_ticket_11_body.jpg", 0.0015, 1f, 0.5f),
            [(14, 150)] = Glow(0, "Item/partCharge8/rareitem_ticket_12_body.jpg", 0.0015, 1f, 0.5f),
            [(13, 43)] = Glow(1, "Item/partCharge1/monmark01a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 93)] = Glow(1, "Item/partCharge1/monmark01a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 44)] = Glow(1, "Item/partCharge1/monmark02a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 94)] = Glow(1, "Item/partCharge1/monmark02a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 116)] = Glow(1, "Item/partCharge1/monmark02a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 45)] = Glow(1, "Item/partCharge1/monmark03a.jpg", 0.001, 1.5f, 0.25f, brightMesh1: true),
            [(13, 46)] = Glow(0, "Item/partCharge1/entrance_R.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 47)] = Glow(0, "Item/partCharge1/entrance_R.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 48)] = Glow(0, "Item/partCharge1/entrance_R.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 61)] = Glow(0, "Item/partCharge1/entrance_R.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 125)] = Glow(0, "Item/partCharge8/DoppelCard.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 126)] = Glow(0, "Item/partCharge8/BarcaCard.jpg", 0.0015, 1.2f, 0.3f),
            [(13, 127)] = Glow(0, "Item/partCharge8/Barca7Card.jpg", 0.0015, 1.2f, 0.3f),

            // Shields
            [(6, 15)] = new Spec // Grand Soul Shield
            {
                OverlayOnly = m => m == 1 || m == 2,
                Passes = new[]
                {
                    Chrome(2),
                    Bright(1, uv: _ => new Vector2(MuGame.Random.Next(10) * 0.1f, MuGame.Random.Next(10) * 0.1f)),
                    Bright(2, t => Grey(Sin(t * 0.001) * 0.4f + 0.6f), t => new Vector2((float)(t * 0.0001), (float)(t * 0.0005))),
                },
            },
            [(6, 16)] = new Spec // Elemental Shield
            {
                OverlayOnly = m => m == 2 || m == 3,
                Passes = new[]
                {
                    Bright(2, uv: t => new Vector2((float)(t * 0.0005), 0f)),
                    new Pass { Mesh = 3, Mode = Mode.Texture, BlendAlpha = 0.5f,
                        Uv = _ => new Vector2(MuGame.Random.Next(10) * 0.1f, MuGame.Random.Next(10) * 0.1f) },
                },
            },
            [(6, 17)] = new Spec { Passes = new[] { Bright(1), Chrome(2) } }, // Crimson Glory
            [(6, 18)] = new Spec { OverlayOnly = m => m == 1, Passes = new[] { Bright(1) } }, // Salamander Shield
            [(6, 19)] = new Spec // Frost Barrier
            {
                OverlayOnly = m => m != 0,
                Passes = new[] { Bright(1), Bright(2, t => Grey(MathF.Abs(Sin(t * 0.001)))) },
            },
            [(6, 20)] = new Spec { Passes = new[] { Bright(1) } }, // Guardian Shield
            [(6, 21)] = new Spec { OverlayOnly = m => m != 0, Passes = new[] { Chrome(1), Bright(2) } }, // Cross Shield
        };

        public static Spec Find(int group, int index) =>
            group >= 0 && Specs.TryGetValue((group, index), out var spec) ? spec : null;
    }
}
