using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 20f;         // Speed of camera movement
    public float panBorderThickness = 10f; // Edge scrolling threshold
    public float zoomSpeed = 10f;        // Speed of zooming
    public float minY = 10f, maxY = 100f; // Zoom height limits
    public float rotationSpeed = 100f;   // Rotation speed
    public float rotationAngle = 45f;    // Default rotation angle

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
        //transform.rotation = Quaternion.Euler(rotationAngle, 0f, 0f); // Comment to prevent camera change Y rotation on start
    }

    void Update()
    {
        HandleMovement();
        HandleZoom();
        HandleRotation();
    }

    void HandleMovement()
    {
        Vector3 move = Vector3.zero;

        if (Input.GetKey("w"))
            move.z += panSpeed * Time.deltaTime;

        if (Input.GetKey("s"))
            move.z -= panSpeed * Time.deltaTime;

        if (Input.GetKey("d"))
            move.x += panSpeed * Time.deltaTime;

        if (Input.GetKey("a"))
            move.x -= panSpeed * Time.deltaTime;

        transform.Translate(move, Space.World);

        float minX = -2560f, maxX = 25600f, minZ = -2560f, maxZ = 25600f;
        transform.position = new Vector3(
            Mathf.Clamp(transform.position.x, minX, maxX),
            transform.position.y,
            Mathf.Clamp(transform.position.z, minZ, maxZ)
        );
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        Vector3 position = transform.position;

        position.y -= scroll * zoomSpeed * 100f * Time.deltaTime;
        position.y = Mathf.Clamp(position.y, minY, maxY);

        transform.position = position;
    }

    void HandleRotation()
    {
        if (Input.GetMouseButton(2)) // Middle mouse button
        {
            float rotate = Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
            transform.Rotate(Vector3.up, rotate, Space.World);
        }
    }
}
