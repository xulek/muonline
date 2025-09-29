using UnityEngine;

public class MoveList : MonoBehaviour
{
    private WorldControl worldControl;

    private void Awake()
    {
        if(worldControl == null)
        {
            worldControl = GetComponent<WorldControl>();
        }
    }

    private void OnGUI()
    {
        if (GUI.Button(new Rect(10f, 10f, 100f, 20f), "Lorencia"))
        {
            worldControl.LoadWorld(1);
        }
        if (GUI.Button(new Rect(10f, 40f, 100f, 20f), "Devias"))
        {
            worldControl.LoadWorld(3);
        }
        if (GUI.Button(new Rect(10f, 70f, 100f, 20f), "Noria"))
        {
            worldControl.LoadWorld(4);
        }
    }
}
