using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Time Settings")]
    [Tooltip("The total number of real-world seconds it takes for one full 24-hour day to pass.")]
    [SerializeField] private float secondsPerDay = 120f;

    [Header("Rotation Axis")]
    [Tooltip("The axis the sun rotates around. Vector3.right simulates a standard East-to-West sun path.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.right;

    private void Update()
    {
        // Avoid division by zero bugs if secondsPerDay is accidentally set to 0 in the Inspector
        if (secondsPerDay <= 0f) return;

        // A full rotation is 360 degrees. 
        // We divide 360 by secondsPerDay to find out how many degrees to rotate per second.
        float degreesPerSecond = 360f / secondsPerDay;

        // Rotate the light smoothly based on the current frame time
        transform.Rotate(rotationAxis * degreesPerSecond * Time.deltaTime, Space.World);
    }
}