using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    /// <summary>
    /// Shared Blood Castle saint-statue rendering. SourceMain5.2 renders the
    /// normal body and then two chrome/metal passes before the statue dies.
    /// </summary>
    public abstract class BloodCastleStatueObject : MonsterObject
    {
        private Texture2D _chromeTexture;

        /// <summary>
        /// SourceMain5.2 RenderCharacter attaches the Divine weapon of Archangel on bone 1
        /// (Staff11 / Sword20 / Bow19). Set in subclass constructors.
        /// </summary>
        protected string DivineWeaponModelPath { get; set; }

        /// <summary>
        /// Original renders the link at absolute object scale 0.7 (statues 1-2) or
        /// 0.9 (statue 3); converted here to child-local scale relative to the statue.
        /// </summary>
        protected float DivineWeaponScale { get; set; }

        private WeaponObject _divineWeapon;

        protected BloodCastleStatueObject(float scale)
        {
            Scale = scale;
            RenderShadow = false;
            Children.Add(new Effects.BloodCastleDeathFragmentEffect(
                this,
                "Object12/StoneCoffin01.bmd",
                "Object12/StoneCoffin02.bmd"));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster61.bmd");

            // SourceMain5.2 RenderCharacter(MONSTER_STATUE_OF_SAINT_1..3): Divine weapon of
            // Archangel linked on bone 1, action 1, PlaySpeed 0.2.
            if (!string.IsNullOrEmpty(DivineWeaponModelPath))
            {
                _divineWeapon = new WeaponObject
                {
                    LinkParentAnimation = false,
                    ParentBoneLink = 1,
                    Scale = DivineWeaponScale,
                    RenderShadow = false
                };
                _divineWeapon.Model = await BMDLoader.Instance.Prepare(DivineWeaponModelPath);
                Children.Add(_divineWeapon);
            }

            await base.Load();

            if (_divineWeapon?.Model?.Actions != null && _divineWeapon.Model.Actions.Length > 1 && _divineWeapon.Model.Actions[1] != null)
                _divineWeapon.Model.Actions[1].PlaySpeed = 0.2f;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Chrome01.jpg");
        }

        public override void Draw(GameTime gameTime)
        {
            if (CurrentAction != (int)MonsterActionType.Die)
                DrawChromePass();

            base.Draw(gameTime);
        }

        protected override void RecalculateWorldPosition()
        {
            base.RecalculateWorldPosition();
            if (Parent != null)
                return;

            Matrix worldPosition = WorldPosition;
            worldPosition.Translation += new Vector3(0f, 120f, 0f);
            WorldPosition = worldPosition;
        }

        private void DrawChromePass()
        {
            if (_chromeTexture == null || Model?.Meshes == null)
                return;

            BlendState previousBlendState = BlendState;
            BlendState = Blendings.OneOneAdditive;
            try
            {
                for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
                {
                    Texture2D originalTexture = GetMeshTexture(meshIndex);
                    try
                    {
                        SetMeshTextureOverride(meshIndex, _chromeTexture);
                        DrawMesh(meshIndex);
                    }
                    finally
                    {
                        if (originalTexture != null)
                            SetMeshTextureOverride(meshIndex, originalTexture);
                        else
                            ClearMeshTextureOverride(meshIndex);
                    }
                }
            }
            finally
            {
                BlendState = previousBlendState;
            }
        }
    }
}
