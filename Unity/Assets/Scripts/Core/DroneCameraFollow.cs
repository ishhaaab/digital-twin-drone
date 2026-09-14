using UnityEngine;

public class DroneCameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("Camera Distance")]
    public float distance = 6f;
    public float height = 2f;

    [Header("Smoothing")]
    public float followSpeed = 8f;
    public float rotationSpeed = 6f;

    void LateUpdate()
    {
        if (target == null) return;

        // 🔥 Use ONLY the drone's yaw — ignore pitch and roll completely
        // Extract yaw-only rotation from drone so camera stays horizontal
        float droneYaw = target.eulerAngles.y;
        Quaternion yawOnly = Quaternion.Euler(0f, droneYaw, 0f);

        // Stay behind drone based on yaw direction only
        Vector3 behindOffset = yawOnly * Vector3.back * distance;
        Vector3 desiredPosition = target.position + behindOffset + Vector3.up * height;

        // Smooth follow
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            Time.deltaTime * followSpeed
        );

        // Always look at drone, but keep camera perfectly horizontal (no roll)
        Vector3 lookDir = target.position - transform.position;
        Quaternion lookRotation = Quaternion.LookRotation(lookDir, Vector3.up); // 🔥 up = world up, locks roll to 0

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            lookRotation,
            Time.deltaTime * rotationSpeed
        );
    }
}