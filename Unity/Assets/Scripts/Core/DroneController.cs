using UnityEngine;

public class DroneController : MonoBehaviour
{
    [Header("Data Source")]
    public DroneDataReceiver dataReceiver;

    // ─────────────────────────────────────────────
    [Header("Position Scaling")]
    [Tooltip("Scale for East movement (NED Y)")]
    public float xScale = 0.25f;

    [Tooltip("Scale for Altitude movement (NED Z). Keep at 1.0")]
    public float yScale = 1.0f;

    [Tooltip("Scale for North movement (NED X)")]
    public float zScale = 0.25f;

    // ─────────────────────────────────────────────
    [Header("Position Axis Flip")]
    [Tooltip("Flip East axis")]
    public bool invertX = false;

    [Tooltip("Flip altitude. Python already inverts Z (down→up), so leave UNCHECKED unless drone goes wrong way")]
    public bool invertY = false;

    [Tooltip("Flip North axis")]
    public bool invertZ = false;

    // ─────────────────────────────────────────────
    [Header("Rotation Remap")]
    [Tooltip("Which channel drives Unity Pitch (X rotation)")]
    public RotationSource unityPitch = RotationSource.Pitch;

    [Tooltip("Which channel drives Unity Yaw (Y rotation)")]
    public RotationSource unityYaw = RotationSource.Yaw;

    [Tooltip("Which channel drives Unity Roll (Z rotation)")]
    public RotationSource unityRoll = RotationSource.Roll;

    [Space]
    public bool invertPitch = true;
    public bool invertYaw   = false;
    public bool invertRoll  = true;

    // ─────────────────────────────────────────────
    [Header("Smoothing")]
    public float positionSmoothTime = 0.3f;
    public float rotationSmooth = 6f;

    [Header("Stability")]
    public float maxDistance = 50f;
    public float deadzone = 0.001f;

    public enum RotationSource { Roll, Pitch, Yaw }

    private Vector3 velocity = Vector3.zero;
    private Vector3 lastTarget;

    void Update()
    {
        if (dataReceiver == null || !dataReceiver.isConnected)
            return;

        var d = dataReceiver.latestData;

        // =============================
        // POSITION
        // NED → Unity:
        //   Unity X = East  = NED Y
        //   Unity Y = Up    = NED Z  (Python already did -msg.z so d.z is positive-up)
        //   Unity Z = North = NED X
        // =============================

        float posX = d.y * xScale;   // East
        float posY = d.z * yScale;   // Up — d.z already positive-up from Python
        float posZ = d.x * zScale;   // North

        if (invertX) posX = -posX;
        if (invertY) posY = -posY;
        if (invertZ) posZ = -posZ;

        Vector3 target = new Vector3(posX, posY, posZ);

        target = Vector3.ClampMagnitude(target, maxDistance);

        if (Vector3.Distance(target, lastTarget) < deadzone)
            target = lastTarget;

        lastTarget = target;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            target,
            ref velocity,
            positionSmoothTime
        );

        // =============================
        // ROTATION
        // =============================
        float rPitch = GetRotation(d, unityPitch);
        float rYaw   = GetRotation(d, unityYaw);
        float rRoll  = GetRotation(d, unityRoll);

        if (invertPitch) rPitch = -rPitch;
        if (invertYaw)   rYaw   = -rYaw;
        if (invertRoll)  rRoll  = -rRoll;

        Quaternion targetRot = Quaternion.Euler(rPitch, rYaw, rRoll);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            Time.deltaTime * rotationSmooth
        );

        // 🔥 Uncomment to debug in Console:
        // Debug.Log($"d.z={d.z:F2}  posY={posY:F2}  target.y={target.y:F2}");
    }

    float GetRotation(DroneData d, RotationSource src)
    {
        switch (src)
        {
            case RotationSource.Roll:  return d.roll;
            case RotationSource.Pitch: return d.pitch;
            case RotationSource.Yaw:   return d.yaw;
            default: return 0f;
        }
    }
}