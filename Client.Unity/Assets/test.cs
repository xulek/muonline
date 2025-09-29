using Client.Data.BMD;
using Client.Main.Content;
using System.IO;
using UnityEditor.SearchService;
using UnityEngine;
public class test : MonoBehaviour
{
    async void Start()
    {

        string bmdPath = "D:/App/MU_Red_1_20_61_Full/Data/Object1/bigwolf.bmd"; // relative to Application.dataPath or full path
        // string bmdPath = "D:/App/MU_Red_1_20_61_Full/Data/NPC/allied.bmd"; // relative to Application.dataPath or full path
        //string bmdPath1 = "D:/App/MU_Red_1_20_61_Full/Data/Object2/Object01.bmd";
        //string bmdPath2 = "D:/App/MU_Red_1_20_61_Full/Data/Object3/Object01.bmd";
        //string bmdPath3 = "D:/App/MU_Red_1_20_61_Full/Data/NPC/chaos_npc.bmd";

        if (BMDLoader.Instance == null)
        {
            Debug.LogError("BMDLoader instance not found in scene!");
            return;
        }

        byte[] bmdBytes = File.ReadAllBytes(bmdPath);
        BMDReader reader = new BMDReader();
        BMD bmd = reader.ReadPublic(bmdBytes);

        PrintBMD(bmd);

        GameObject model = await BMDLoader.Instance.LoadBMDModelSingleObject(bmdPath);
        //GameObject model1 = await BMDLoader.Instance.LoadBMDModelSingleObject(bmdPath1);
        //GameObject model2 = await BMDLoader.Instance.LoadBMDModelSingleObject(bmdPath);

        //var animator = model2.GetComponent<BMDAnimator>();
        //animator.animationSpeed = 0.3f;


        //Debug.Log("Model loaded and animation started.");
        //Debug.Log("Model loaded and rendered.");


    }
/// <summary>
/// 输出模型白模的法线,Mesh,骨骼
/// </summary>
/// <param name="bmd"></param>
    void PrintBMD(BMD bmd)
    {
        Debug.LogWarning($"BMD Model Name: {bmd.Name}, Version: {bmd.Version}");

        // Print Meshes
        for (int i = 0; i < bmd.Meshes.Length; i++)
        {
            var mesh = bmd.Meshes[i];
            Debug.LogWarning($"Mesh[{i}] TextureIndex: {mesh.Texture}, TexturePath: {mesh.TexturePath}");
            Debug.LogWarning($"Vertices count: {mesh.Vertices.Length}");
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                var vert = mesh.Vertices[v];
                Debug.LogWarning($"  Vertex[{v}] Node: {vert.Node}, Pos: {vert.Position}");
            }

            Debug.LogWarning($"Normals count: {mesh.Normals.Length}");
            for (int n = 0; n < mesh.Normals.Length; n++)
            {
                var norm = mesh.Normals[n];
                Debug.LogWarning($"  Normal[{n}] Node: {norm.Node}, Normal: {norm.Normal}, BindVertex: {norm.BindVertex}");
            }

            Debug.LogWarning($"Triangles count: {mesh.Triangles.Length}");
            for (int t = 0; t < mesh.Triangles.Length; t++)
            {
                var tri = mesh.Triangles[t];
                Debug.LogWarning($"  Triangle[{t}] Polygon: {tri.Polygon}, VertexIndices: {string.Join(", ", tri.VertexIndex)}");
            }
        }
        Debug.LogWarning("-------------------------------------------------------------------------------------------------");

        Debug.LogWarning($"Bones count: {bmd.Bones.Length}");
        for (int i = 0; i < bmd.Bones.Length; i++)
        {
            var bone = bmd.Bones[i];
            if (bone == null)
            {
                Debug.Log($"Bone[{i}]: null");
                continue;
            }
            Debug.LogWarning($"Bone[{i}] Name: {bone.Name}, Parent: {bone.Parent}, Matrixes length: {bone.Matrixes?.Length ?? 0}");

            if (bone.Matrixes != null)
            {
                for (int m = 0; m < bone.Matrixes.Length; m++)
                {
                    var matrix = bone.Matrixes[m];
                    Debug.LogWarning($"  Action[{m}] Positions keys: {matrix.Position?.Length ?? 0}, Rotations keys: {matrix.Rotation?.Length ?? 0}");
                }
            }
        }
        Debug.LogWarning("-------------------------------------------------------------------------------------------------");
        Debug.LogWarning($"Actions count: {bmd.Actions.Length}");
        for (int i = 0; i < bmd.Actions.Length; i++)
        {
            var action = bmd.Actions[i];
            Debug.LogWarning($"Action[{i}] NumAnimationKeys: {action.NumAnimationKeys}, LockPositions: {action.LockPositions}, PlaySpeed: {action.PlaySpeed}");
            if (action.LockPositions && action.Positions != null)
            {
                for (int p = 0; p < action.Positions.Length; p++)
                {
                    Debug.LogWarning($"  Position[{p}]: {action.Positions[p]}");
                }
            }
        }
    }
}
    



