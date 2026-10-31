using Client.Main.Content;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Player
{
    /// <summary>
    /// MODEL_15GRADE_ARMOR_OBJ_* ornament shown on +15 armor parts (NextGradeObjectRender,
    /// ZzzObject.cpp). It is linked to a character bone with the link matrix that
    /// RenderLinkObject (ZzzCharacter.cpp) builds for each ornament type.
    /// </summary>
    internal sealed class NextGradeOrnamentObject : ModelObject
    {
        private readonly Func<ModelObject> _part;
        private readonly string _modelPath;
        private bool _loading;

        private NextGradeOrnamentObject(Func<ModelObject> part, string modelName, int bone,
            Vector3 angleDegrees, Vector3 translation)
        {
            _part = part;
            _modelPath = $"Item/{modelName}.bmd";
            ParentBoneLink = bone;
            LinkParentAnimation = false;
            Position = translation;
            Angle = new Vector3(
                MathHelper.ToRadians(angleDegrees.X),
                MathHelper.ToRadians(angleDegrees.Y),
                MathHelper.ToRadians(angleDegrees.Z));
            RenderShadow = false;
            Hidden = true;
        }

        public static NextGradeOrnamentObject[] CreateAll(PlayerObject player)
        {
            return new[]
            {
                new NextGradeOrnamentObject(() => player.Helm, "class15_head", 20, new(180f, -90f, 0f), new(28f, 20f, 0f)),
                new NextGradeOrnamentObject(() => player.Armor, "class15_bodyleft", 35, new(0f, -90f, 0f), new(5f, -20f, 0f)),
                new NextGradeOrnamentObject(() => player.Armor, "class15_bodyright", 26, new(0f, -90f, 0f), new(5f, -20f, 0f)),
                new NextGradeOrnamentObject(() => player.Pants, "class15_pantleft", 3, new(0f, 90f, 180f), new(25f, 5f, -5f)),
                new NextGradeOrnamentObject(() => player.Pants, "class15_pantright", 10, new(0f, 90f, 180f), new(25f, 5f, 5f)),
                new NextGradeOrnamentObject(() => player.Gloves, "class15_armleft", 36, new(0f, -90f, 0f), new(30f, 0f, 20f)),
                new NextGradeOrnamentObject(() => player.Gloves, "class15_armright", 27, new(0f, -90f, 0f), new(30f, 0f, -20f)),
                new NextGradeOrnamentObject(() => player.Boots, "class15_bootleft", 4, new(0f, 90f, 180f), new(20f, 15f, -10f)),
                new NextGradeOrnamentObject(() => player.Boots, "class15_bootright", 11, new(0f, 90f, 180f), new(20f, 15f, 10f)),
            };
        }

        public override void Update(GameTime gameTime)
        {
            ModelObject part = _part();
            bool wanted = part != null && !part.Hidden && part.Model != null && part.ItemLevel >= 15;
            if (wanted && Model == null && !_loading)
            {
                _loading = true;
                _ = LoadModelAsync();
            }

            Hidden = !wanted || Model == null;
            base.Update(gameTime);
        }

        private async Task LoadModelAsync()
        {
            var bmd = await BMDLoader.Instance.Prepare(_modelPath);
            if (bmd != null)
                MuGame.ScheduleOnMainThread(() => Model = bmd);
        }
    }
}
