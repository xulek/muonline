using Client.Data.OBJS;
using Org.BouncyCastle.Utilities;
using System.IO;
using UnityEngine;

public class Test1 : MonoBehaviour
{
    void Start()
    {
        //string encryptedBuffer = Application.dataPath + "C:\\Users\\Windows-Desktop\\Unity\\Mu Online\\Assets\\StreamingAssets\\Data\\World1\\EncTerrain1.obj";
        string filename = "EncTerrain1.obj";
        string path = Path.Combine(Application.streamingAssetsPath, "Data/World1", filename);

        if (File.Exists(path))
        {
            Debug.Log("File found!");
            byte[] fileBytes = File.ReadAllBytes(path);
            var objReader = new OBJReader();
            OBJ objData = objReader.ReadPublic(fileBytes);

            Debug.Log($"OBJ Version: {objData.Version}, MapNumber: {objData.MapNumber}, Object Count: {objData.Objects.Length}");
            foreach (var obj in objData.Objects)
            {
                Debug.Log($"Type: {obj.Type}, Pos: {obj.Position}, Rot: {obj.Angle}, Scale: {obj.Scale}, Type: {obj.Type}");
            }

        }
        else
        {
            Debug.Log("File not found!");
        }

        

        
    }


}
