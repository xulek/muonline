using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Data.BMD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Main.Controls.UI.Game.Inventory;

namespace Client.Main.Objects.Wings
{
    public class CustomEffect
    {
        public int BoneID { get; set; }
        public EffectType EffectID { get; set; }
        public Vector3 Angle { get; set; }
        public Vector3 Position { get; set; }
        public float Scale { get; set; }
        public Vector3 Color { get; set; }
        public SpriteObject Effect { get; set; }
    }

    public class WingObject : ModelObject
    {
        public List<CustomEffect> _effects { get; set; } = new List<CustomEffect>();

        private const int DefaultWingBoneLink = 47;
        private const int CapeBoneLink = 19;
        private const float CapeRigidAnimationSpeed = 0.25f * 25f;

        private readonly CapeClothObject _capeCloth;
        private bool _isCapeLike;
        private bool _savedBaseState;
        private Vector3 _savedPosition;
        private Vector3 _savedAngle;
        private int _savedParentBoneLink;
        private bool _savedLinkParentAnimation;
        private int _savedHiddenMesh;
        private float _savedAnimationSpeed;
        private bool _savedContinuousAnimation;
        private bool _savedFreezeAnimationPose;
        private int _changeVersion;

        // SourceMain5.2 item indices (group 12) with dedicated passes in RenderPartObjectBody.
        private const short WingsOfElf = 0, WingsOfSpirits = 3, WingsOfDarkness = 6, WingOfStorm = 36,
            WingOfRuin = 39, WingsOfDespair = 42, WingOfDimension = 43, SmallWingsOfElf = 132;

        private Texture2D _overlayTexture;

        // ───────────── MODEL_FENRIR_THUNDER subtype 1 (Wing of Storm) ─────────────

        private sealed class LightningBolt : ModelObject
        {
            private float _fade = 0f;
            private bool _fadingOut;
            private float _accumulator;

            public LightningBolt()
            {
                // RenderPartObjectBody: BodyLight = o->Light (0.6, 0.6, 0.9), unlit RENDER_TEXTURE.
                LightEnabled = false;
                Light = new Vector3(0.6f, 0.6f, 0.9f);
                RenderShadow = false;
                IsTransparent = true;
                BlendState = BlendState.AlphaBlend;
                LinkParentAnimation = false;
                Alpha = 0f;
            }

            public bool Done { get; private set; }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                _accumulator += (float)gameTime.ElapsedGameTime.TotalSeconds;
                while (_accumulator >= 0.04f && !Done)
                {
                    _accumulator -= 0.04f;
                    // Alpha +0.3 per tick up to 1, then -0.3 per tick; angles +0.15 per tick.
                    _fade += _fadingOut ? -0.3f : 0.3f;
                    if (!_fadingOut && _fade >= 1f) { _fade = 1f; _fadingOut = true; }
                    if (_fadingOut && _fade <= 0f) { _fade = 0f; Done = true; }
                    Angle += new Vector3(MathHelper.ToRadians(0.15f));
                }
                Alpha = _fade;
                Hidden = Done;
            }
        }

        private static BMD s_lightningModel;
        private static bool s_lightningLoading;
        private readonly List<LightningBolt> _bolts = new();

        internal void SpawnLightning(Vector3 localPosition, float scale)
        {
            if (s_lightningModel == null)
            {
                if (!s_lightningLoading)
                {
                    s_lightningLoading = true;
                    _ = LoadLightningAsync();
                }
                return;
            }

            LightningBolt bolt = _bolts.Find(b => b.Done);
            if (bolt == null)
            {
                if (_bolts.Count >= 8)
                    return;
                bolt = new LightningBolt { Model = s_lightningModel };
                _bolts.Add(bolt);
                Children.Add(bolt);
            }
            else
            {
                bolt = new LightningBolt { Model = s_lightningModel };
                int index = _bolts.FindIndex(b => b.Done);
                Children.Remove(_bolts[index]);
                _bolts[index].Dispose();
                _bolts[index] = bolt;
                Children.Add(bolt);
            }

            var random = MuGame.Random;
            bolt.Position = localPosition;
            bolt.Scale = scale;
            bolt.Angle = new Vector3(
                MathHelper.ToRadians(random.Next(360)),
                MathHelper.ToRadians(random.Next(360)),
                MathHelper.ToRadians(random.Next(360)));
        }

        private static async Task LoadLightningAsync()
        {
            s_lightningModel = await BMDLoader.Instance.Prepare("Effect/lightning_type01.bmd");
        }

        private short _type;
        public new short Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    int changeVersion = Interlocked.Increment(ref _changeVersion);
                    _ = OnChangeType(_type, changeVersion);
                }
            }
        }

        private short itemIndex = -1;
        public short ItemIndex
        {
            get => itemIndex;
            set
            {
                if (itemIndex == value)
                    return;

                itemIndex = value;
                int changeVersion = Interlocked.Increment(ref _changeVersion);
                _ = OnChangeIndex(itemIndex, changeVersion);
            }
        }

        public WingObject()
        {
            RenderShadow = true;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.AlphaBlend;
            BlendMesh = -1;
            BlendMeshState = BlendState.Additive;
            Alpha = 1f;
            LinkParentAnimation = false;
            ParentBoneLink = DefaultWingBoneLink;

            _capeCloth = new CapeClothObject();
            Children.Add(_capeCloth);

            // RenderPartObjectEffect sprite/particle effects of wings (group 12).
            AddItemEffect(new ItemLinkEffect());
        }

        internal bool IsCapeLike => _isCapeLike;

        protected override bool ForceTwoSidedMeshes => _isCapeLike;
        protected override bool AllowGpuSkinning => !_isCapeLike;
        protected override bool AllowDynamicLightingShader => !_isCapeLike;
        protected override bool PreserveBlendMeshesInLowQuality => _isCapeLike;

        protected override float GetDepthBias()
        {
            return -0.000012f;
        }

        private bool IsCurrentChangeVersion(int changeVersion) =>
            Volatile.Read(ref _changeVersion) == changeVersion;

        private void UpdateStatusAfterAsyncResolve(bool modelResolved)
        {
            if (Status is not (GameControlStatus.Ready or GameControlStatus.Error))
                return;

            Status = modelResolved ? GameControlStatus.Ready : GameControlStatus.Error;
        }

        private async Task OnChangeType(short requestedType, int changeVersion)
        {
            if (!IsCurrentChangeVersion(changeVersion))
                return;

            if (requestedType <= 0)
            {
                if (ItemIndex < 0)
                {
                    ApplyWingState();
                    Model = null;
                }
                return;
            }

            string modelPath = Path.Combine("Item", $"Wing{requestedType:D2}.bmd");
            BMD resolvedModel = await BMDLoader.Instance.Prepare(modelPath);

            if (resolvedModel == null)
            {
                modelPath = Path.Combine("Item", $"Wing{requestedType}.bmd");
                resolvedModel = await BMDLoader.Instance.Prepare(modelPath);
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            ApplyWingState();
            Model = resolvedModel;
            UpdateStatusAfterAsyncResolve(resolvedModel != null);
        }

        private async Task OnChangeIndex(short requestedItemIndex, int changeVersion)
        {
            if (!IsCurrentChangeVersion(changeVersion))
                return;

            if (requestedItemIndex < 0)
            {
                if (Type <= 0)
                {
                    ApplyWingState();
                    Model = null;
                }
                return;
            }

            ItemDefinition itemDefinition = ItemDatabase.GetItemDefinition(12, requestedItemIndex);
            string modelPath = itemDefinition?.TexturePath;
            BMD resolvedModel = null;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                resolvedModel = requestedItemIndex switch
                {
                    30 or 130 => await BMDLoader.Instance.Prepare("Item/DarkLordRobe.bmd")
                                 ?? await BMDLoader.Instance.Prepare("Item/DarkLordRobe02.bmd"),
                    40 => await BMDLoader.Instance.Prepare("Item/DarkLordRobe02.bmd"),
                    49 or 135 => await BMDLoader.Instance.Prepare("Item/Wing50.bmd"),
                    50 => await BMDLoader.Instance.Prepare("Item/Wing51.bmd"),
                    _ => null
                };
            }
            else
            {
                string normalized = NormalizeItemModelPath(modelPath);
                modelPath = normalized;
                resolvedModel = await BMDLoader.Instance.Prepare(normalized);

                if (resolvedModel == null && normalized.EndsWith("DarkLordRobe.bmd", StringComparison.OrdinalIgnoreCase))
                {
                    string robe02Path = normalized.Replace("DarkLordRobe.bmd", "DarkLordRobe02.bmd");
                    resolvedModel = await BMDLoader.Instance.Prepare(robe02Path);
                }
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            Texture2D overlayTexture = requestedItemIndex switch
            {
                WingsOfDarkness => await TextureLoader.Instance.PrepareAndGetTexture("Effect/bab2.jpg"),
                WingsOfDespair => await TextureLoader.Instance.PrepareAndGetTexture("Effect/Chrome06.jpg"),
                WingOfRuin => await TextureLoader.Instance.PrepareAndGetTexture("Item/msword01_r.jpg"),
                _ => null,
            };

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            _overlayTexture = overlayTexture;
            MaterialItemGroup = 12;
            MaterialItemIndex = requestedItemIndex;

            if (CapeClothProfile.TryCreate(requestedItemIndex, modelPath, out CapeClothProfile capeProfile) &&
                resolvedModel != null)
            {
                Model = resolvedModel;
                bool clothReady = await _capeCloth.ConfigureAsync(capeProfile, resolvedModel);

                if (!IsCurrentChangeVersion(changeVersion))
                    return;

                ApplyCapeState(capeProfile, clothReady);
            }
            else
            {
                ApplyWingState();
                Model = resolvedModel;
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            UpdateStatusAfterAsyncResolve(resolvedModel != null);
        }

        private static string NormalizeItemModelPath(string modelPath)
        {
            string normalized = modelPath.Replace("\\", "/");
            if (normalized.Contains("Item/Wing/", StringComparison.OrdinalIgnoreCase))
                normalized = Path.Combine("Item", Path.GetFileName(normalized)).Replace("\\", "/");
            if (normalized.Contains("DarkLordRobe01", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Replace("DarkLordRobe01", "DarkLordRobe");
            return normalized;
        }

        private void SaveBaseState()
        {
            if (_savedBaseState)
                return;

            _savedPosition = Position;
            _savedAngle = Angle;
            _savedParentBoneLink = ParentBoneLink;
            _savedLinkParentAnimation = LinkParentAnimation;
            _savedHiddenMesh = HiddenMesh;
            _savedAnimationSpeed = AnimationSpeed;
            _savedContinuousAnimation = ContinuousAnimation;
            _savedFreezeAnimationPose = FreezeAnimationPose;
            _savedBaseState = true;
        }

        private void ApplyCapeState(CapeClothProfile profile, bool clothReady)
        {
            SaveBaseState();
            _isCapeLike = true;
            LinkParentAnimation = false;
            ParentBoneLink = CapeBoneLink;
            HiddenMesh = profile.RigidAttachment.Visible || !clothReady ? -1 : -2;
            Position = profile.RigidAttachment.Position;
            Angle = profile.RigidAttachment.Angle;
            AnimationSpeed = CapeRigidAnimationSpeed;
            ContinuousAnimation = true;
            FreezeAnimationPose = false;
            BlendState = BlendState.AlphaBlend;
            BlendMeshState = BlendState.Additive;
            RenderShadow = profile.RigidAttachment.Visible || !clothReady;
            InvalidateBuffers(BufferFlagAll);
        }

        private void ApplyWingState()
        {
            _capeCloth.Disable();

            if (_savedBaseState)
            {
                Position = _savedPosition;
                Angle = _savedAngle;
                ParentBoneLink = _savedParentBoneLink;
                LinkParentAnimation = _savedLinkParentAnimation;
                HiddenMesh = _savedHiddenMesh;
                AnimationSpeed = _savedAnimationSpeed;
                ContinuousAnimation = _savedContinuousAnimation;
                FreezeAnimationPose = _savedFreezeAnimationPose;
                _savedBaseState = false;
            }
            else
            {
                ParentBoneLink = DefaultWingBoneLink;
                HiddenMesh = -1;
                FreezeAnimationPose = false;
            }

            RenderShadow = true;
            _isCapeLike = false;

            // ItemObjectAttribute: Wings of Elf / Spirits draw texture 0 as an additive blend mesh.
            BlendMesh = ItemIndex is WingsOfElf or WingsOfSpirits or SmallWingsOfElf ? 0 : -1;
            InvalidateBuffers(BufferFlagAll);
        }

        // Meshes that SourceMain only draws in additive passes (RenderPartObjectBody).
        protected override bool IsOverlayOnlyMesh(int mesh)
        {
            if (_isCapeLike)
                return false;

            return ItemIndex switch
            {
                // RenderPartObjectBody only draws meshes 0-2 (Storm) / 0-1 (Ruin); the base texture
                // pass covers mesh 2 (Storm) and mesh 1 (Ruin), the rest is additive only.
                WingOfStorm => mesh != 2,
                WingOfRuin => mesh != 1,
                _ => false,
            };
        }

        // Wing of Storm: RenderMesh(2, RENDER_TEXTURE) runs with BodyLight (1, 0.7, 0.5).
        protected override Vector3 GetMeshBodyLight(int mesh) =>
            !_isCapeLike && ItemIndex == WingOfStorm && mesh == 2 ? new Vector3(1f, 0.7f, 0.5f) : base.GetMeshBodyLight(mesh);

        public override void Draw(GameTime gameTime)
        {
            bool ready = !_isCapeLike && !Hidden && Visible && Model?.Meshes != null && Status == GameControlStatus.Ready;

            // Wings of Darkness: RENDER_BRIGHT | RENDER_CHROME (bab2, BodyLight 0.8/0.6/1.0) is drawn
            // first without depth writes; the texture pass then covers it, so the chrome only
            // shows through the alpha-cut parts of the feathers.
            if (ready && ItemIndex == WingsOfDarkness && _overlayTexture != null)
            {
                for (int mesh = 0; mesh < Model.Meshes.Length; mesh++)
                    DrawMeshItemOverlay(mesh, ItemOverlayMode.Chrome, new Vector3(0.8f, 0.6f, 1f), _overlayTexture);
            }

            base.Draw(gameTime);

            if (!ready)
                return;

            float time = (float)gameTime.TotalGameTime.TotalSeconds;
            switch (ItemIndex)
            {

                case WingOfStorm:
                {
                    DrawMeshItemOverlay(0, ItemOverlayMode.Texture, new Vector3(1f, 0.7f, 0.5f));
                    // s_iTexAni steps once per 25 fps frame and wraps after 16; every 4 steps
                    // the U coordinate advances by a quarter of the strip.
                    int step = (int)(time * 25f) % 16;
                    float u = (step / 4) * 0.25f;
                    DrawMeshItemOverlay(1, ItemOverlayMode.Texture, new Vector3(0.9f, 0.6f, 0.3f), uvOffset: new Vector2(u, 0f));
                    break;
                }

                case WingOfRuin:
                {
                    float pulse = MathF.Abs(MathF.Sin(time));
                    float low = 0.1f + pulse * 0.3f;
                    DrawMeshItemOverlay(0, ItemOverlayMode.Texture, new Vector3(low));
                    if (_overlayTexture != null)
                        DrawMeshItemOverlay(1, ItemOverlayMode.Texture, new Vector3(pulse * 0.8f), _overlayTexture);
                    break;
                }

                case WingsOfDespair:
                    if (_overlayTexture != null)
                        DrawMeshItemOverlay(1, ItemOverlayMode.Chrome6, Vector3.One, _overlayTexture);
                    break;

                case WingOfDimension:
                    DrawMeshItemOverlay(1, ItemOverlayMode.Chrome, Vector3.One);
                    break;
            }
        }


        internal void UpdateCapePhysics(GameTime gameTime)
        {
            if (_isCapeLike)
                _capeCloth.UpdateAfterOwner(gameTime);
        }

        public override async Task Load()
        {
            if (_effects.Count > 0)
            {
                foreach (CustomEffect effect in _effects)
                {
                    effect.Effect = Utils.GetEffectByCode(effect.EffectID);
                    effect.Effect.Light = effect.Color;
                    effect.Effect.Scale = effect.Scale;
                    Children.Add(effect.Effect);
                }
            }

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            // Wings of Dragon / Soul pulse their texture-script blend meshes (RenderPartObjectEffect).
            if (!_isCapeLike)
                ApplyScriptBlendLight(gameTime.TotalGameTime.TotalMilliseconds);
            base.Update(gameTime);

            if (_effects.Count > 0 && BoneTransform != null)
            {
                foreach (CustomEffect effect in _effects)
                {
                    if ((uint)effect.BoneID >= (uint)BoneTransform.Length)
                        continue;
                    effect.Effect.Position = effect.Position + BoneTransform[effect.BoneID].Translation;
                    effect.Effect.Angle = effect.Angle + Angle;
                }
            }
        }
    }
}
