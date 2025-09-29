
using Client.Data.BMD;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Client.Main.Content
{
    public class BMDLoader : MonoBehaviour
    {
        public static BMDLoader Instance { get; private set; }
        private TerrainControl terrainControl;
        private readonly BMDReader _reader = new();
        private readonly Dictionary<string, Task<BMD>> _bmds = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
        }

        public async Task<GameObject> LoadBMDModelSingleObject(string path)
        {
            Debug.Log("This is from LoadBMDModelSingleObject: " + path);
            BMD bmd = await LoadBMDAsync(path);
            
            if (bmd == null)
            {
                Debug.LogError($"Failed to load BMD from path: {path}");
                return null;
            }

            string bmdDirectory = System.IO.Path.GetDirectoryName(path); //<---------------------
            Debug.Log("bmdDirectory: " + bmdDirectory);                                                    //<---------------------

            GameObject rootObject = new GameObject($"BMD_Model_{bmd.Name}");

            Transform[] boneTransforms = BuildBoneHierarchy(bmd, rootObject);

            Mesh skinnedMesh = BuildSkinnedMesh(bmd, boneTransforms, rootObject);

            List<Material> materials = new List<Material>();
            
            foreach (var meshData in bmd.Meshes)
            {
                Debug.Log("Info about textures: " + meshData.TexturePath);

                string combinedTexturePath = System.IO.Path.Combine(bmdDirectory, meshData.TexturePath);//<---------------------
                Debug.Log("New addition:::::::::: " + combinedTexturePath);//<---------------------

                Texture2D tex = await TextureLoader.Instance.PrepareAndGetTexture(combinedTexturePath);

                Material mat = tex != null
                        ? new Material(Shader.Find("Custom/SimpleTextureShader")) { mainTexture = tex }
                        : new Material(Shader.Find("Standard"));

                materials.Add(mat);
            }

            var smr = rootObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = skinnedMesh;
            smr.bones = boneTransforms;
            smr.materials = materials.ToArray();

            var animator = rootObject.AddComponent<BMDAnimator>();
            animator.bmd = bmd;
            animator.actionIndex = 0;

            //rootObject.transform.Rotate(Vector3.up * 90);      // Y-axis rotation
            //rootObject.transform.Rotate(Vector3.forward * 90); // Z-axis rotation

            return rootObject;
        }

        private async Task<BMD> LoadBMDAsync(string path)
        {
            Debug.Log("This is from LoadBMDAsync: " + path);
            if (_bmds.TryGetValue(path, out var task))
                return await task;

            var loadTask = Task.Run(() =>
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                return _reader.ReadPublic(data);
            });

            _bmds[path] = loadTask;
            Debug.Log("This is from end LoadBMDAsync: " + path);
            return await loadTask;
        }

        private Transform[] BuildBoneHierarchy(BMD bmd, GameObject rootObject)
        {
            Transform[] boneTransforms = new Transform[bmd.Bones.Length];

            for (int i = 0; i < bmd.Bones.Length; i++)
            {
                var bone = bmd.Bones[i];
                GameObject boneGO = new GameObject(bone.Name);
                boneTransforms[i] = boneGO.transform;

                if (bone.Parent >= 0 && bone.Parent < bmd.Bones.Length)
                    boneGO.transform.parent = boneTransforms[bone.Parent];
                else
                    boneGO.transform.parent = rootObject.transform;

                boneGO.transform.localPosition = Vector3.zero;
                boneGO.transform.localRotation = Quaternion.identity;
            }

            return boneTransforms;
        }

        public BMD GetLoadedBMD(string path)
        {
            if (_bmds.TryGetValue(path, out var bmdTask))
            {
                if (bmdTask.IsCompletedSuccessfully)
                    return bmdTask.Result;
            }

            return null;
        }

        private Mesh BuildSkinnedMesh(BMD bmd, Transform[] boneTransforms, GameObject rootObject)
        {
            Mesh mesh = new Mesh();

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<BoneWeight> boneWeights = new List<BoneWeight>();

            List<List<int>> submeshTriangles = new();

            int vertexOffset = 0;

            for (int meshIndex = 0; meshIndex < bmd.Meshes.Length; meshIndex++)
            {
                var meshData = bmd.Meshes[meshIndex];
                submeshTriangles.Add(new List<int>());

                foreach (var triangle in meshData.Triangles)
                {
                    for (int j = 0; j < triangle.Polygon; j++)
                    {
                        int vi = triangle.VertexIndex[j];
                        int ni = triangle.NormalIndex[j];
                        int ti = triangle.TexCoordIndex[j];

                        var vertex = meshData.Vertices[vi];
                        vertices.Add(vertex.Position);

                        var normal = meshData.Normals[ni].Normal;
                        normals.Add(normal);

                        var texCoord = meshData.TexCoords[ti];
                        uvs.Add(new Vector2(texCoord.U, texCoord.V));

                        submeshTriangles[meshIndex].Add(vertexOffset);

                        boneWeights.Add(new BoneWeight
                        {
                            boneIndex0 = Mathf.Clamp(vertex.Node, 0, boneTransforms.Length - 1),
                            weight0 = 1f
                        });

                        vertexOffset++;
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = boneWeights.ToArray();

            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(submeshTriangles[i], i);
            }

            Matrix4x4[] bindPoses = new Matrix4x4[boneTransforms.Length];

            for (int i = 0; i < boneTransforms.Length; i++)
            {
                bindPoses[i] = boneTransforms[i].worldToLocalMatrix * rootObject.transform.localToWorldMatrix;
            }
            mesh.bindposes = bindPoses;

            return mesh;
        }
    }
}
