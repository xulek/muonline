using Client.Data.OBJS;
using Client.Main;
using Client.Main.Content;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class ObjectControl : MonoBehaviour
{

    private async void Start()
    {
        await SpawnAsync();
    }

    [ContextMenu("Spawn Map Objects")]
    public async Task SpawnAsync()
    {
        TerrainControl terrain = FindObjectOfType<TerrainControl>();
        StringBuilder logBuilder = new StringBuilder();

        //Find correct file path
        var file = $"EncTerrain{terrain.WorldIndex}.obj";
        var worldFolder = $"World{terrain.WorldIndex}";
        string path = Path.Combine(Constants.DataPath, worldFolder, file);

        Debug.Log("-----------------------------------------------------------------------------------------");
        Debug.Log("file: " + file);
        Debug.Log("worldFolder: " + worldFolder);
        Debug.Log("path: " + path);
        Debug.Log("-----------------------------------------------------------------------------------------");

        string objPath = Path.Combine(Constants.DataPath, $"World{terrain.WorldIndex}", $"EncTerrain{terrain.WorldIndex}.obj"); //Path for EncTerrainX.obj
        string objectPath = Path.Combine(Constants.DataPath2, $"Object{terrain.WorldIndex}");                      //Path for ObjectX fodler

        Debug.Log("objPath: " + objPath);
        Debug.Log("objectPath: " + objectPath);

        if (!File.Exists(objPath))
        {
            Debug.LogError($"OBJ file not found: {objPath}");
            return;
        }

        byte[] buffer = File.ReadAllBytes(objPath);
        OBJReader reader = new OBJReader();
        OBJ objData = reader.ReadPublic(buffer);

        GameObject parent = new GameObject($"MapObjects_{worldFolder}");

        for (int i = 0; i < objData.Objects.Length; i++)
        {
            IMapObject mapObj = objData.Objects[i];

            int fileIndex = mapObj.Type + 1;
            string bmdFile = Path.Combine(objectPath, $"Object{fileIndex:D2}.bmd");

            if (!File.Exists(bmdFile))
            {
                Debug.LogWarning($"Missing BMD model: {bmdFile}");
                continue;
            }

            GameObject model = await BMDLoader.Instance.LoadBMDModelSingleObject(bmdFile);

            if (model == null)
            {
                Debug.LogWarning($"Failed to load model: {bmdFile}");
                continue;
            }

            //model.transform.SetParent(parent.transform);

            //Position
            Vector3 pos = mapObj.Position;
            float posX = pos.x;
            float posY = pos.z;
            float posZ = pos.y;

            model.transform.position = new Vector3(posX, posY, posZ);

            //Rotation
            Vector3 rot = mapObj.Angle;
            float rotX = -90f; //Have to be -90 cause all items will be lying down
            float rotY = rot.z;
            float rotZ = rot.y;

            model.transform.rotation = Quaternion.Euler(rotX, rotY, rotZ);
            model.transform.localScale = Vector3.one * mapObj.Scale;

            //string logLine = $"Pos: {mapObj.Position}, Rot: {mapObj.Angle}, Scale: {mapObj.Scale}";
            //string logline2 = $"{" PosX: " + model.transform.position.x} " +
            //                  $"{" PosY: " + model.transform.position.y} " +
            //                  $"{" PosZ: " + model.transform.position.z} || " +
            //                  $"{" RotX: " + model.transform.rotation.x} " +
            //                  $"{" RotY: " + model.transform.rotation.y} " +
            //                  $"{" RotZ: " + model.transform.rotation.z}";
            //string logline3 = $"--------------------------------------------------------------------------------------------------------------------------------------------";

            //Debug.Log(logLine);
            //Debug.Log(logline2);
            //Debug.Log(logline3);

            //logBuilder.AppendLine(logLine);
            //logBuilder.AppendLine(logline2);
            //logBuilder.AppendLine(logline3);
            //string logFilePath = Path.Combine(Application.persistentDataPath, $"MapObjectsLog_World{terrain.WorldIndex}.txt");

            //try
            //{
            //    File.WriteAllText(logFilePath, logBuilder.ToString());
            //    Debug.Log($"Exported map objects log to: {logFilePath}");
            //}
            //catch (System.Exception ex)
            //{
            //    Debug.LogError($"Failed to write log file: {ex.Message}");
            //}

        }

        Debug.Log($"Spawned {objData.Objects.Length} map objects for {worldFolder}");
    }
}