using UnityEngine;

public class WorldControl : MonoBehaviour
{
    private TerrainControl terrainControl;

    private void Awake()
    {
        if(terrainControl == null)
        {
            terrainControl = GetComponent<TerrainControl>();
        }
    }
    public void LoadWorld(short index)
    {
        if (terrainControl == null)
        {
            Debug.Log("TerrainControl is not assigned!");
        }

        terrainControl.WorldIndex = (short)index;
        terrainControl.StopAllCoroutines();
        terrainControl.StartCoroutine(terrainControl.Load());
    }
}
