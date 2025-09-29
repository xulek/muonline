using UnityEngine;

public class AxisVisualizer : MonoBehaviour
{
    public float axisLength = 100.0f; // Increase this value for longer lines

    void OnDrawGizmos()
    {
        Vector3 position = transform.position;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(position, position + transform.right * axisLength);   // X-axis

        Gizmos.color = Color.green;
        Gizmos.DrawLine(position, position + transform.up * axisLength);      // Y-axis

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(position, position + transform.forward * axisLength); // Z-axis

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(position, position + Vector3.up);
    }
}
