using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Main.Objects.Effects.Cloth;

namespace Client.Main.Objects.Wings
{
    internal enum CapeClothKind
    {
        None,
        CapeOfLord,
        SmallCapeOfLord,
        CapeOfEmperor,
        CapeOfFighter,
        SmallCapeOfFighter,
        CapeOfOverrule
    }

    internal readonly struct CapeRigidAttachment
    {
        public CapeRigidAttachment(bool visible, Vector3 position, Vector3 angle)
        {
            Visible = visible;
            Position = position;
            Angle = angle;
        }

        public bool Visible { get; }
        public Vector3 Position { get; }
        public Vector3 Angle { get; }
    }

    internal readonly struct CapeClothProfile
    {
        private CapeClothProfile(
            CapeClothKind kind,
            float width,
            float height,
            Vector3 offset,
            bool heavy,
            float upperSphereRadius,
            float lowerSphereRadius,
            string[] textureCandidates,
            CapeRigidAttachment rigidAttachment)
        {
            Kind = kind;
            Width = width;
            Height = height;
            Offset = offset;
            Heavy = heavy;
            UpperSphereRadius = upperSphereRadius;
            LowerSphereRadius = lowerSphereRadius;
            TextureCandidates = textureCandidates;
            RigidAttachment = rigidAttachment;
        }

        public CapeClothKind Kind { get; }
        public float Width { get; }
        public float Height { get; }
        public Vector3 Offset { get; }
        public bool Heavy { get; }
        public float UpperSphereRadius { get; }
        public float LowerSphereRadius { get; }
        public string[] TextureCandidates { get; }
        public CapeRigidAttachment RigidAttachment { get; }
        public bool HasSideRibbons => Kind is CapeClothKind.CapeOfEmperor or CapeClothKind.CapeOfOverrule;
        public bool IsRageFighterCape => Kind is CapeClothKind.CapeOfFighter or CapeClothKind.SmallCapeOfFighter or CapeClothKind.CapeOfOverrule;

        public static bool TryCreate(short itemIndex, string modelPath, out CapeClothProfile profile)
        {
            CapeClothKind kind = itemIndex switch
            {
                30 => CapeClothKind.CapeOfLord,
                40 => CapeClothKind.CapeOfEmperor,
                49 => CapeClothKind.CapeOfFighter,
                50 => CapeClothKind.CapeOfOverrule,
                130 => CapeClothKind.SmallCapeOfLord,
                135 => CapeClothKind.SmallCapeOfFighter,
                _ => DetectKindFromPath(modelPath)
            };

            profile = kind switch
            {
                CapeClothKind.CapeOfLord => new CapeClothProfile(
                    kind, 180f, 180f, new Vector3(0f, 8f, 10f), false, 25f, 27f,
                    new[] { "Player/DarklordRobe.tga" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.SmallCapeOfLord => new CapeClothProfile(
                    kind, 100f, 100f, new Vector3(0f, 8f, 10f), false, 25f, 27f,
                    new[] { "Player/DarklordRobe.tga" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.CapeOfEmperor => new CapeClothProfile(
                    kind, 180f, 180f, new Vector3(0f, 8f, 10f), true, 25f, 27f,
                    new[] { "Item/dl_redwings02.tga" },
                    new CapeRigidAttachment(
                        false,
                        new Vector3(-47f, -7f, 0f),
                        new Vector3(0f, MathHelper.ToRadians(90f), 0f))),

                CapeClothKind.CapeOfFighter => new CapeClothProfile(
                    kind, 180f, 170f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/NCcape.tga", "Item/NCcape.jpg", "Item/NCcape.ozj" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.SmallCapeOfFighter => new CapeClothProfile(
                    kind, 150f, 130f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/NCcape.tga", "Item/NCcape.jpg", "Item/NCcape.ozj" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.CapeOfOverrule => new CapeClothProfile(
                    kind, 180f, 170f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/monke_manto.TGA", "Item/monke_manto.tga", "Item/monke_manto.jpg" },
                    new CapeRigidAttachment(
                        true,
                        new Vector3(10f, -15f, 15f),
                        new Vector3(0f, MathHelper.ToRadians(90f), 0f))),

                _ => default
            };

            return kind != CapeClothKind.None;
        }

        private static CapeClothKind DetectKindFromPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return CapeClothKind.None;

            string path = modelPath.Replace('\\', '/');
            if (path.Contains("DarkLordRobe02", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfEmperor;
            if (path.Contains("DarkLordRobe", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfLord;
            if (path.Contains("Wing51", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfOverrule;
            if (path.Contains("Wing50", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfFighter;

            return CapeClothKind.None;
        }
    }

    /// <summary>
    /// Physics capes of Dark Lord / Rage Fighter wings, the original CPhysicsCloth set of RenderCharacter
    /// (ZzzCharacter.cpp:9505-9633), simulated by <see cref="SourceClothSet"/>:
    ///   Cape of Lord     Create(o, 19, 0, 8, 10, 10, 10, 180, 180, BITMAP_ROBE+7, PCT_CURVED | PCT_SHORT_SHOULDER | PCT_MASK_ALPHA)   9548
    ///   small (WING+130) same, 100 x 100                                                                                        9539
    ///   Cape of Emperor  same + PCT_HEAVY, BITMAP_ROBE+9; ribbons Create(o, 19, +-30, 15|20, 10, 2, 5, 12, 200, BITMAP_ROBE+10,
    ///                    PCT_FLAT | PCT_COTTON | PCT_MASK_ALPHA)                                                                9531, 9577-9583
    ///   DL spheres (+-10, -10, -10, 25, 17), (+-10, -10, 20, 27, 17); ribbon spheres (0, -15, -20, 30, 2), (0, 0, 0, 35, 17)
    ///   Cape of Fighter  Create(o, 19, 0, 15, 5, 10, 10, 180, 170, BITMAP_NCCAPE, PCT_CURVED | PCT_SHORT_SHOULDER | PCT_HEAVY | PCT_MASK_ALPHA) 9613
    ///   small (WING+135) same, 150 x 130                                                                                        9605
    ///   Cape of Overrule same, BITMAP_MANTOE; ribbons Create(o, 19, +-25, 15, 2, 2, 5, 12, 180, BITMAP_MANTO01,
    ///                    PCT_FLAT | PCT_COTTON | PCT_ELASTIC_RAGE_L / _R | PCT_MASK_ALPHA)                                      9597, 9622-9628
    ///   RF spheres (+-10, -10, -10, 35, 17), (+-10, -10, 20, 37, 17); ribbon spheres (0, -15, -20, 35, 2), (0, 0, 0, 45 | 50, 17)
    /// Moved with Move2(0.005, 5) once per frame after the owner's skeleton update, rendered with CloakLight = white.
    /// Not ported: Dark Lord hair cloths [0] [1] and armour skirt [3], stun / sleep freeze (Flag = 0).
    /// </summary>
    internal sealed class CapeClothObject : EffectObject
    {
        private const int CapeBone = 19;

        private static readonly ClothCollisionSphere[] LordSpheres =
        {
            new(new Vector3(-10f, -10f, -10f), 25f, 17),
            new(new Vector3(10f, -10f, -10f), 25f, 17),
            new(new Vector3(-10f, -10f, 20f), 27f, 17),
            new(new Vector3(10f, -10f, 20f), 27f, 17),
        };

        private static readonly ClothCollisionSphere[] LordRibbonSpheres =
        {
            new(new Vector3(0f, -15f, -20f), 30f, 2),
            new(new Vector3(0f, 0f, 0f), 35f, 17),
        };

        private static readonly ClothCollisionSphere[] FighterSpheres =
        {
            new(new Vector3(-10f, -10f, -10f), 35f, 17),
            new(new Vector3(10f, -10f, -10f), 35f, 17),
            new(new Vector3(-10f, -10f, 20f), 37f, 17),
            new(new Vector3(10f, -10f, 20f), 37f, 17),
        };

        private static readonly ClothCollisionSphere[] OverruleRibbonLeftSpheres =
        {
            new(new Vector3(0f, -15f, -20f), 35f, 2),
            new(new Vector3(0f, 0f, 0f), 45f, 17),
        };

        private static readonly ClothCollisionSphere[] OverruleRibbonRightSpheres =
        {
            new(new Vector3(0f, -15f, -20f), 35f, 2),
            new(new Vector3(0f, 0f, 0f), 50f, 17),
        };

        private SourceClothSet _set;
        private int _configurationVersion;
        private bool _configured;

        public CapeClothObject()
        {
            Hidden = true;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.NonPremultiplied;
            DepthState = DepthStencilState.Default;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-260f, -260f, -260f),
                new Vector3(260f, 260f, 300f));
        }

        public async Task<bool> ConfigureAsync(CapeClothProfile profile, BMD model)
        {
            int version = Interlocked.Increment(ref _configurationVersion);
            ClothDefinition[] definitions = BuildDefinitions(profile);
            var textures = new Texture2D[definitions.Length];
            Texture2D mainTexture = await ResolveTextureAsync(profile.TextureCandidates, model, 0).ConfigureAwait(false);
            textures[0] = mainTexture;
            for (int i = 1; i < definitions.Length; i++)
            {
                textures[i] = await ResolveTextureAsync(new[] { definitions[i].FrontTexture }, model, 1).ConfigureAwait(false)
                    ?? mainTexture;
            }

            if (version != Volatile.Read(ref _configurationVersion))
                return false;

            if (MuGame.IsMainThread)
                return ApplyConfiguration(version, definitions, textures);

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MuGame.ScheduleOnMainThread(static state =>
            {
                var (cloth, requestedVersion, requestedDefinitions, requestedTextures, source) = state;
                try
                {
                    source.TrySetResult(cloth.ApplyConfiguration(requestedVersion, requestedDefinitions, requestedTextures));
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            },
            (this, version, definitions, textures, completion),
            name: "CapeCloth.Configure");

            return await completion.Task.ConfigureAwait(false);
        }

        private static ClothDefinition[] BuildDefinitions(CapeClothProfile profile)
        {
            const PhysicsClothType capeShape = PhysicsClothType.Curved | PhysicsClothType.ShortShoulder | PhysicsClothType.DrawAlpha;
            const PhysicsClothType ribbonShape = PhysicsClothType.Flat | PhysicsClothType.ShapeNormal | PhysicsClothType.Cotton | PhysicsClothType.DrawAlpha;

            switch (profile.Kind)
            {
                case CapeClothKind.CapeOfLord:
                case CapeClothKind.SmallCapeOfLord:
                    return new[] { Cape(new Vector3(0f, 8f, 10f), profile.Width, profile.Height, capeShape, LordSpheres) };

                case CapeClothKind.CapeOfEmperor:
                    return new[]
                    {
                        Cape(new Vector3(0f, 8f, 10f), 180f, 180f, capeShape | PhysicsClothType.Heavy, LordSpheres),
                        Ribbon(new Vector3(30f, 15f, 10f), 200f, ribbonShape, "Item/dl_redwings03.tga", LordRibbonSpheres),
                        Ribbon(new Vector3(-30f, 20f, 10f), 200f, ribbonShape, "Item/dl_redwings03.tga", LordRibbonSpheres),
                    };

                case CapeClothKind.CapeOfFighter:
                case CapeClothKind.SmallCapeOfFighter:
                    return new[] { Cape(new Vector3(0f, 15f, 5f), profile.Width, profile.Height, capeShape | PhysicsClothType.Heavy, FighterSpheres) };

                case CapeClothKind.CapeOfOverrule:
                    return new[]
                    {
                        Cape(new Vector3(0f, 15f, 5f), 180f, 170f, capeShape | PhysicsClothType.Heavy, FighterSpheres),
                        Ribbon(new Vector3(25f, 15f, 2f), 180f, ribbonShape | PhysicsClothType.ElasticRageL, "Item/monk_manto01.TGA", OverruleRibbonLeftSpheres),
                        Ribbon(new Vector3(-25f, 15f, 2f), 180f, ribbonShape | PhysicsClothType.ElasticRageR, "Item/monk_manto01.TGA", OverruleRibbonRightSpheres),
                    };

                default:
                    return Array.Empty<ClothDefinition>();
            }

            static ClothDefinition Cape(Vector3 offset, float width, float height, PhysicsClothType type, ClothCollisionSphere[] spheres) => new()
            {
                Bone = CapeBone,
                Offset = offset,
                NumHor = 10,
                NumVer = 10,
                Width = width,
                Height = height,
                Type = type,
                Spheres = spheres,
            };

            static ClothDefinition Ribbon(Vector3 offset, float height, PhysicsClothType type, string texture, ClothCollisionSphere[] spheres) => new()
            {
                Bone = CapeBone,
                Offset = offset,
                NumHor = 2,
                NumVer = 5,
                Width = 12f,
                Height = height,
                Type = type,
                FrontTexture = texture,
                Spheres = spheres,
            };
        }

        private bool ApplyConfiguration(int version, ClothDefinition[] definitions, Texture2D[] textures)
        {
            if (version != Volatile.Read(ref _configurationVersion))
                return false;

            var set = new SourceClothSet(definitions);
            for (int i = 0; i < textures.Length; i++)
                set.SetTextures(i, textures[i], textures[i]);

            _set = set;
            _configured = definitions.Length > 0 && textures[0] != null;
            Hidden = !_configured;
            return _configured;
        }

        public void Disable()
        {
            Interlocked.Increment(ref _configurationVersion);
            _configured = false;
            _set = null;
            Hidden = true;
        }

        protected override void OnWorldChanged(WorldControl newWorld, WorldControl prevWorld)
        {
            base.OnWorldChanged(newWorld, prevWorld);
            _set?.Reset();
        }

        public override void Update(GameTime gameTime)
        {
            // The owning PlayerObject invokes UpdateAfterOwner after movement and skeleton updates.
        }

        internal void UpdateAfterOwner(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !_configured || Hidden || _set == null)
                return;

            PlayerObject player = GetOwner();
            if (player == null || TotalAlpha <= 0.001f || player.GetBoneTransforms() is not { Length: > 0 })
                return;

            _set.Move(player, 0.005f);
        }

        public override void Draw(GameTime gameTime)
        {
            // Cloth uses alpha-tested textures and is rendered in the after pass.
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || !_configured || _set == null)
                return;

            PlayerObject player = GetOwner();
            if (player == null || player.Hidden || player.World == null)
                return;

            _set.Draw(GraphicsDevice, Vector3.One, TotalAlpha);
            DrawChildrenAfterOnly(gameTime);
        }

        private PlayerObject GetOwner()
        {
            if (Parent is WingObject wing && wing.Parent is PlayerObject player)
                return player;
            return null;
        }

        private static async Task<Texture2D> ResolveTextureAsync(
            IReadOnlyList<string> candidates,
            BMD model,
            int preferredMeshIndex)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Texture2D texture = await TryLoadTextureAsync(candidates[i]).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            if (model?.Meshes == null || model.Meshes.Length == 0)
                return null;

            if ((uint)preferredMeshIndex < (uint)model.Meshes.Length)
            {
                string preferred = BMDLoader.Instance.GetTexturePath(model, model.Meshes[preferredMeshIndex].TexturePath);
                Texture2D texture = await TryLoadTextureAsync(preferred).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            // The cloth surface is usually the largest mesh in the legacy cape BMD.
            bool[] attempted = new bool[model.Meshes.Length];
            if ((uint)preferredMeshIndex < (uint)attempted.Length)
                attempted[preferredMeshIndex] = true;

            for (int pass = 0; pass < model.Meshes.Length; pass++)
            {
                int bestIndex = -1;
                int bestVertexCount = -1;
                for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
                {
                    if (attempted[meshIndex])
                        continue;
                    int vertexCount = model.Meshes[meshIndex].Vertices?.Length ?? 0;
                    if (vertexCount > bestVertexCount)
                    {
                        bestVertexCount = vertexCount;
                        bestIndex = meshIndex;
                    }
                }

                if (bestIndex < 0)
                    break;

                attempted[bestIndex] = true;
                string path = BMDLoader.Instance.GetTexturePath(model, model.Meshes[bestIndex].TexturePath);
                Texture2D texture = await TryLoadTextureAsync(path).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private static async Task<Texture2D> TryLoadTextureAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return await TextureLoader.Instance.PrepareAndGetTexture(path).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }
    }
}
