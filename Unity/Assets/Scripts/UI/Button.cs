using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// DroneCommandButtons
// Attach this to any GameObject (e.g. your Canvas).
// Drag your DroneDataReceiver GameObject into the "receiver" slot in Inspector.
// Then wire each Button's OnClick() to the matching method below.
// ─────────────────────────────────────────────────────────────────────────────
public class DroneCommandButtons : MonoBehaviour
{
    [Tooltip("Drag the GameObject that has DroneDataReceiver attached here.")]
    public DroneDataReceiver receiver;

    public void SendLand()        => receiver.SendCommand("LAND");
    public void SendStabilize()   => receiver.SendCommand("STABILIZE");
    public void SendAltHold()     => receiver.SendCommand("ALT_HOLD");
    public void SendPosHold()     => receiver.SendCommand("POSHOLD");
    public void SendForceDisarm() => receiver.SendCommand("FORCE_DISARM");
}