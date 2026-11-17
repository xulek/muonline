using Client.Data;
using Client.Main;
using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Client.Data.BMD;
using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Player
{
    public class WeaponObject : ModelObject
    {
        private int _type;
        private new ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<WeaponObject>();
        private readonly bool _monsterBlurEnabled;
        private Monsters.MonsterWeaponBlur _monsterBlur;

        public new int Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    _ = OnChangeTypeAsync();
                }
            }
        }

        public bool IsRightHand { get; set; }
        public string TexturePath { get; set; }
        public byte ItemGroup { get; set; }

        /// <summary>BlendMeshTexCoordU/V of the blend mesh, set every frame (ZzzObject.cpp:5333-5337, legendary shield).</summary>
        internal void SetBlendMeshTexCoordOffset(int mesh, Vector2 offset)
        {
            TextureCoordinateOffset = offset;
            TextureCoordinateOffsetMeshIndex = mesh;
        }

        /// <param name="monsterBlur">Run the non-player CreateWeaponBlur branch (<see cref="Monsters.MonsterWeaponBlur"/>)
        /// when the owner is a monster. Players pass false: they use <see cref="PlayerWeaponBlur"/>.</param>
        public WeaponObject(bool monsterBlur = true)
        {
            RenderShadow = true;
            LinkParentAnimation = true;

            // SourceMain5.2 RenderLinkObject sprite/particle effects of special items.
            AddItemEffect(new ItemLinkEffect());

            _monsterBlurEnabled = monsterBlur && Constants.ENABLE_WEAPON_TRAIL;
        }

        // ───────────── SourceMain RenderPartObjectBody passes of special weapons ─────────────

        // Chromatic Staff: CreateEffect(MODEL_MOONHARVEST_MOON, Zx08, ..., 2, ..., 0.12f) every tick,
        // i.e. the small "chysukmoon" model is always shown at bone 8.
        private ModelObject _chromaticMoon;
        private bool _chromaticMoonLoading;

        private void UpdateChromaticMoon()
        {
            bool wanted = MaterialItemGroup == 5 && MaterialItemIndex == 33 && Model != null;
            if (!wanted)
            {
                if (_chromaticMoon != null)
                    _chromaticMoon.Hidden = true;
                return;
            }

            if (_chromaticMoon != null)
            {
                _chromaticMoon.Hidden = Hidden;
                return;
            }

            if (_chromaticMoonLoading)
                return;
            _chromaticMoonLoading = true;
            _ = LoadChromaticMoonAsync();
        }

        private async Task LoadChromaticMoonAsync()
        {
            var bmd = await BMDLoader.Instance.Prepare("Effect/chysukmoon.bmd");
            if (bmd == null)
                return;
            var moon = new EffectModelObject
            {
                Model = bmd,
                ParentBoneLink = 8,
                LinkParentAnimation = false,
                Scale = 0.12f,
            };
            MuGame.ScheduleOnMainThread(() =>
            {
                Children.Add(moon);
                _chromaticMoon = moon;
            });
        }

        private sealed class EffectModelObject : ModelObject
        {
            public EffectModelObject()
            {
                RenderShadow = false;
            }
        }

        protected override bool IsOverlayOnlyMesh(int mesh) => IsSpecialItemOverlayOnlyMesh(mesh);

        public override void Draw(GameTime gameTime)
        {
            if (Parent is PlayerObject { IsCloaked: true })
                return;
            base.Draw(gameTime);
            DrawSpecialItemPasses(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (Parent is PlayerObject { IsCloaked: true })
                return;
            base.DrawAfter(gameTime);
            if (Visible && GraphicsDevice != null)
                _monsterBlur?.Draw(GraphicsDevice);
        }

        public override int DrawShadowCaster(Effect shadowEffect, Matrix lightViewProjection) =>
            Parent is PlayerObject { IsCloaked: true } ? 0 : base.DrawShadowCaster(shadowEffect, lightViewProjection);

        private async Task OnChangeTypeAsync()
        {
            ParentBoneLink = IsRightHand ? 10 : 15;

            string modelPath = GetWeaponPath(Type);
            if (string.IsNullOrEmpty(modelPath))
            {
                Model = null;
                return;
            }

            Model = await BMDLoader.Instance.Prepare(modelPath);
            if (Model == null)
            {
                _logger?.LogWarning("WeaponObject: Failed to load model for Type {Type}. Path: {Path}", Type, modelPath);
                Status = Models.GameControlStatus.Error;
            }
        }

        private static string GetWeaponPath(int type)
        {
            var modelType = (ModelType)type;
            int groupBase = type / 512 * 512;
            int id = type - groupBase;

            string category = ((ModelType)groupBase).ToString().Replace("ITEM_GROUP_", "").Split('_')[0];

            if (category == "MACE") category = "Mace";

            if (id >= 0)
            {
                return $"Item/{category}{id + 1:D2}.bmd";
            }

            return null;
        }

        public override void Update(GameTime gameTime)
        {
            UpdateChromaticMoon();
            ApplyItemObjectAttribute(gameTime.TotalGameTime.TotalMilliseconds);
            base.Update(gameTime);
            // Force invalidation is now handled at parent level in ModelObject.Update()

            // CreateWeaponBlur + MoveBlurs run after the owner's animation step.
            if (_monsterBlurEnabled && Parent is MonsterObject monster)
                (_monsterBlur ??= new Monsters.MonsterWeaponBlur()).Update(this, monster);
        }

        public override void Dispose()
        {
            _monsterBlur?.Dispose();
            base.Dispose();
        }
    }
}
