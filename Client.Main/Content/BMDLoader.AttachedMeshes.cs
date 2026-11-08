using Client.Data.BMD;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Content
{
    /// <summary>A rigid model linked to a bone of the skeleton a skinned model is drawn with.</summary>
    public readonly struct AttachedMeshSource
    {
        public AttachedMeshSource(BMD model, int bone, Matrix link)
        {
            Model = model;
            Bone = bone;
            Link = link;
        }

        public BMD Model { get; }
        /// <summary>Skeleton bone the model is linked to.</summary>
        public int Bone { get; }
        /// <summary>Model space to bone space (the link object's scale, angle and position).</summary>
        public Matrix Link { get; }
    }

    // Models with rigid meshes of other models appended (the +15 armor ornaments): the appended meshes are
    // baked into the bone space of their link bone, so they are skinned and drawn with the model's own meshes.
    public partial class BMDLoader
    {
        private sealed class AttachedModelEntry
        {
            public required BMD Model { get; init; }
            public required int FirstAttachedMesh { get; init; }
        }

        private readonly Dictionary<(BMD Base, string Key), AttachedModelEntry> _attachedModels = new();
        private readonly ConditionalWeakTable<BMD, AttachedModelEntry> _attachedModelInfo = new();
        private readonly ConditionalWeakTable<BMD, BMD> _attachedModelBase = new();

        /// <summary>
        /// Returns <paramref name="baseModel"/> with the meshes of <paramref name="sources"/> appended, built once per
        /// base model and <paramref name="key"/>. The base model itself is not modified.
        /// </summary>
        public BMD GetModelWithAttachedMeshes(BMD baseModel, string key, IReadOnlyList<AttachedMeshSource> sources)
        {
            if (baseModel == null || sources == null || sources.Count == 0)
                return baseModel;

            lock (_attachedModels)
            {
                if (_attachedModels.TryGetValue((baseModel, key), out AttachedModelEntry cached))
                    return cached.Model;
            }

            var meshes = new List<BMDTextureMesh>(baseModel.Meshes);
            int firstAttached = meshes.Count;
            var texturePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            lock (_texturePathMap)
            {
                if (_texturePathMap.TryGetValue(baseModel, out var basePaths))
                    foreach (var pair in basePaths)
                        texturePaths[pair.Key] = pair.Value;

                for (int i = 0; i < sources.Count; i++)
                {
                    AttachedMeshSource source = sources[i];
                    if (source.Model?.Meshes == null)
                        continue;

                    // The attached meshes' textures resolve from their own model's folder: give their names a
                    // per-source prefix (keeping the extension), as they may repeat the base model's names.
                    _texturePathMap.TryGetValue(source.Model, out var sourcePaths);
                    Matrix[] rest = ComputeRestPose(source.Model);
                    for (int m = 0; m < source.Model.Meshes.Length; m++)
                    {
                        BMDTextureMesh sourceMesh = source.Model.Meshes[m];
                        string textureName = $"attached{i}/{sourceMesh.TexturePath}";
                        if (sourcePaths != null &&
                            sourcePaths.TryGetValue(sourceMesh.TexturePath.ToLowerInvariant(), out string fullPath))
                            texturePaths[textureName.ToLowerInvariant()] = fullPath;
                        meshes.Add(BakeAttachedMesh(sourceMesh, textureName, rest, source.Link, source.Bone));
                    }
                }
            }

            var combined = new BMD
            {
                Version = baseModel.Version,
                Name = baseModel.Name,
                Bones = baseModel.Bones,
                Actions = baseModel.Actions,
                Meshes = meshes.ToArray(),
            };

            lock (_texturePathMap)
                _texturePathMap[combined] = texturePaths;

            var entry = new AttachedModelEntry { Model = combined, FirstAttachedMesh = firstAttached };
            lock (_attachedModels)
            {
                if (_attachedModels.TryGetValue((baseModel, key), out AttachedModelEntry raced))
                    return raced.Model;
                _attachedModels[(baseModel, key)] = entry;
                _attachedModelInfo.AddOrUpdate(combined, entry);
                _attachedModelBase.AddOrUpdate(combined, baseModel);
            }
            return combined;
        }

        /// <summary>Index of the first appended mesh of a model built by GetModelWithAttachedMeshes; int.MaxValue otherwise.</summary>
        public int GetFirstAttachedMesh(BMD model) =>
            model != null && _attachedModelInfo.TryGetValue(model, out AttachedModelEntry entry)
                ? entry.FirstAttachedMesh
                : int.MaxValue;

        /// <summary>The model the attached meshes were appended to, or <paramref name="model"/> itself.</summary>
        public BMD GetAttachedMeshBaseModel(BMD model) =>
            model != null && _attachedModelBase.TryGetValue(model, out BMD baseModel) ? baseModel : model;

        // First frame of the first action, as ModelObject.GenerateBoneMatrix computes it.
        private static Matrix[] ComputeRestPose(BMD model)
        {
            var bones = model.Bones ?? Array.Empty<BMDTextureBone>();
            var pose = new Matrix[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                Matrix world = Matrix.Identity;
                if (bone != BMDTextureBone.Dummy && bone.Matrixes is { Length: > 0 })
                {
                    var bm = bone.Matrixes[0];
                    if (bm.Position is { Length: > 0 } && bm.Quaternion is { Length: > 0 })
                    {
                        Matrix local = Matrix.CreateFromQuaternion(bm.Quaternion[0]);
                        local.Translation = bm.Position[0];
                        world = bone.Parent >= 0 && bone.Parent < i ? local * pose[bone.Parent] : local;
                    }
                }
                pose[i] = world;
            }
            return pose;
        }

        private static BMDTextureMesh BakeAttachedMesh(BMDTextureMesh source, string texturePath, Matrix[] rest,
            Matrix link, int bone)
        {
            var vertices = new BMDTextureVertex[source.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                BMDTextureVertex v = source.Vertices[i];
                Matrix toBone = BoneOf(rest, v.Node) * link;
                Vector3 position = Vector3.Transform(v.Position, toBone);
                vertices[i] = new BMDTextureVertex { Node = (short)bone, Position = position.ToNumerics() };
            }

            var normals = new BMDTextureNormal[source.Normals.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                BMDTextureNormal n = source.Normals[i];
                Matrix toBone = BoneOf(rest, n.Node) * link;
                Vector3 normal = Vector3.TransformNormal(n.Normal, toBone);
                if (normal.LengthSquared() > 1e-12f)
                    normal.Normalize();
                normals[i] = new BMDTextureNormal { Node = (short)bone, Normal = normal.ToNumerics(), BindVertex = n.BindVertex };
            }

            return new BMDTextureMesh
            {
                Vertices = vertices,
                Normals = normals,
                TexCoords = source.TexCoords,
                Triangles = source.Triangles,
                // The texture slot selects the model's blend mesh; appended meshes never are one.
                Texture = -1,
                TexturePath = texturePath,
                BlendingMode = source.BlendingMode,
            };
        }

        private static Matrix BoneOf(Matrix[] pose, int node) =>
            (uint)node < (uint)pose.Length ? pose[node] : Matrix.Identity;
    }
}
