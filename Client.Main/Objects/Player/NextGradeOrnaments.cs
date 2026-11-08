using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Player
{
    /// <summary>
    /// MODEL_15GRADE_ARMOR_OBJ_* ornaments shown on +15 armor parts (NextGradeObjectRender, ZzzObject.cpp). Each is
    /// linked to a character bone with the link matrix that RenderLinkObject (ZzzCharacter.cpp) builds for its
    /// type and rendered at level 0. They are appended to the decorated part's model as meshes of that bone, so
    /// the part draws them with its own meshes (without its item material).
    /// </summary>
    internal static class NextGradeOrnaments
    {
        internal const int Helm = 0, Armor = 1, Pants = 2, Gloves = 3, Boots = 4;

        private readonly struct Ornament
        {
            public Ornament(string modelName, int bone, Vector3 angleDegrees, Vector3 translation)
            {
                ModelPath = $"Item/{modelName}.bmd";
                Bone = bone;
                Link = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(new Vector3(
                           MathHelper.ToRadians(angleDegrees.X),
                           MathHelper.ToRadians(angleDegrees.Y),
                           MathHelper.ToRadians(angleDegrees.Z)))) *
                       Matrix.CreateTranslation(translation);
            }

            public string ModelPath { get; }
            public int Bone { get; }
            public Matrix Link { get; }
        }

        private static readonly Ornament[][] s_ornaments =
        {
            new[] { new Ornament("class15_head", 20, new(180f, -90f, 0f), new(28f, 20f, 0f)) },
            new[]
            {
                new Ornament("class15_bodyleft", 35, new(0f, -90f, 0f), new(5f, -20f, 0f)),
                new Ornament("class15_bodyright", 26, new(0f, -90f, 0f), new(5f, -20f, 0f)),
            },
            new[]
            {
                new Ornament("class15_pantleft", 3, new(0f, 90f, 180f), new(25f, 5f, -5f)),
                new Ornament("class15_pantright", 10, new(0f, 90f, 180f), new(25f, 5f, 5f)),
            },
            new[]
            {
                new Ornament("class15_armleft", 36, new(0f, -90f, 0f), new(30f, 0f, 20f)),
                new Ornament("class15_armright", 27, new(0f, -90f, 0f), new(30f, 0f, -20f)),
            },
            new[]
            {
                new Ornament("class15_bootleft", 4, new(0f, 90f, 180f), new(20f, 15f, -10f)),
                new Ornament("class15_bootright", 11, new(0f, 90f, 180f), new(20f, 15f, 10f)),
            },
        };

        private static readonly Task<BMD>[][] s_models = new Task<BMD>[s_ornaments.Length][];

        /// <summary>
        /// The part model with the ornaments of <paramref name="slot"/> appended; null while the ornament models
        /// are still loading.
        /// </summary>
        public static BMD GetDecoratedModel(BMD partModel, int slot)
        {
            Ornament[] ornaments = s_ornaments[slot];
            Task<BMD>[] models = s_models[slot] ??= LoadModels(ornaments);

            var sources = new AttachedMeshSource[ornaments.Length];
            for (int i = 0; i < ornaments.Length; i++)
            {
                if (!models[i].IsCompleted)
                    return null;
                BMD model = models[i].IsCompletedSuccessfully ? models[i].Result : null;
                sources[i] = new AttachedMeshSource(model, ornaments[i].Bone, ornaments[i].Link);
            }

            return BMDLoader.Instance.GetModelWithAttachedMeshes(partModel, "class15", sources);
        }

        private static Task<BMD>[] LoadModels(Ornament[] ornaments)
        {
            var models = new Task<BMD>[ornaments.Length];
            for (int i = 0; i < ornaments.Length; i++)
                models[i] = BMDLoader.Instance.Prepare(ornaments[i].ModelPath);
            return models;
        }
    }
}
