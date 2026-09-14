using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// DroneData  –  one telemetry snapshot
// ─────────────────────────────────────────────────────────────────────────────
[Serializable]
public class DroneData
{
    public double timestamp;

    public float x, y, z;
    public float vx, vy, vz, speed;

    public float lat, lon, gps_alt;

    public float roll, pitch, yaw;
    public float roll_rate, pitch_rate, yaw_rate;

    public int   battery_remaining;
    public float voltage, current;

    // Optional: satellite count. Not part of the original packet format — stays -1
    // (meaning "unknown") unless your Python bridge sends a 17th CSV field (index 16).
    // The new GPS signal-bars widget falls back to a connection/latency-based proxy
    // quality when this is -1, so nothing breaks if you never add it.
    public int   satellites = -1;

    public int motor1, motor2, motor3, motor4;

    public float vibration_x, vibration_y, vibration_z;
    public bool  armed;
    public string flight_mode = "UNKNOWN";
}

// ─────────────────────────────────────────────────────────────────────────────
// LatencyStats  –  rolling statistics over a sliding window
// ─────────────────────────────────────────────────────────────────────────────
[Serializable]
public class LatencyStats
{
    public float latest   = 0f;
    public float mean     = 0f;
    public float variance = 0f;
    public float min      = float.MaxValue;
    public float max      = float.MinValue;

    private const int WINDOW = 60;
    private Queue<float> samples = new Queue<float>();
    private float sum  = 0f;
    private float sum2 = 0f;

    public void AddSample(float ms)
    {
        latest = ms;

        if (ms < min) min = ms;
        if (ms > max) max = ms;

        samples.Enqueue(ms);
        sum  += ms;
        sum2 += ms * ms;

        if (samples.Count > WINDOW)
        {
            float old = samples.Dequeue();
            sum  -= old;
            sum2 -= old * old;
        }

        int n = samples.Count;
        mean     = sum / n;
        variance = (sum2 / n) - (mean * mean);
    }

    public void Reset()
    {
        samples.Clear();
        sum = sum2 = latest = mean = variance = 0f;
        min = float.MaxValue;
        max = float.MinValue;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DroneDataReceiver
// ─────────────────────────────────────────────────────────────────────────────
public class DroneDataReceiver : MonoBehaviour
{
    [Header("Network")]
    public int listenPort  = 5055;   // data IN  from Python
    public int commandPort = 5056;   // commands OUT to Python

    [Header("Live Data")]
    public DroneData    latestData       = new DroneData();
    public LatencyStats latencyStats     = new LatencyStats();
    public bool         newDataAvailable = false;
    public bool         isConnected      = false;

    [Header("Vibration Safety")]
    [Tooltip("If any vibration axis exceeds this value, an auto-land is triggered.")]
    public float vibrationThreshold = 60f;

    public bool vibrationAlarmActive { get; private set; } = false;

    // ── Internals ─────────────────────────────────────────────────────────
    private UdpClient rxClient;
    private UdpClient txClient;
    private Thread    rxThread;
    private string    latestRaw = "";
    private object    lockObj   = new object();
    private bool      autoLandSent = false;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        rxClient = new UdpClient(listenPort);
        txClient = new UdpClient();

        rxThread = new Thread(ReceiveLoop) { IsBackground = true };
        rxThread.Start();

        Debug.Log($"[Drone] Listening on UDP {listenPort}, commands → {commandPort}");
    }

    // ─────────────────────────────────────────────────────────────────────
    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] data = rxClient.Receive(ref ep);
                lock (lockObj)
                {
                    latestRaw   = Encoding.UTF8.GetString(data);
                    isConnected = true;
                }
            }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        string raw;
        lock (lockObj)
        {
            if (string.IsNullOrEmpty(latestRaw)) return;
            raw       = latestRaw;
            latestRaw = "";
        }

        // ── Parse packet ─────────────────────────────────────────────────
        // Index map:
        //  0  = x
        //  1  = y
        //  2  = z
        //  3  = roll
        //  4  = pitch
        //  5  = yaw
        //  6  = status flag (always "1")
        //  7  = battery %
        //  8  = vibration_x
        //  9  = vibration_y
        //  10 = vibration_z
        //  11 = send timestamp (Unix seconds)
        //  12 = flight_mode string
        //  13 = armed (0 or 1)
        //  14 = voltage (V)
        //  15 = current (A)
        try
        {
            string[] p = raw.Trim().Split(',');

            if (p.Length < 8)
            {
                Debug.LogWarning("[Drone] Short packet: " + raw);
                return;
            }

            latestData.x     = float.Parse(p[0]);
            latestData.y     = float.Parse(p[1]);
            latestData.z     = float.Parse(p[2]);
            latestData.roll  = float.Parse(p[3]);
            latestData.pitch = float.Parse(p[4]);
            latestData.yaw   = float.Parse(p[5]);
            // p[6] = status flag — skip
            latestData.battery_remaining = (int)float.Parse(p[7]);

            if (p.Length >= 12)
            {
                latestData.vibration_x = float.Parse(p[8]);
                latestData.vibration_y = float.Parse(p[9]);
                latestData.vibration_z = float.Parse(p[10]);

                double sendTs    = double.Parse(p[11]);
                double nowTs     = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                float  latencyMs = (float)((nowTs - sendTs) * 1000.0);
                if (latencyMs >= 0f && latencyMs < 5000f)
                    latencyStats.AddSample(latencyMs);
            }

            if (p.Length >= 14)
            {
                latestData.flight_mode = p[12].Trim();
                latestData.armed       = p[13].Trim() == "1";
            }

            // NEW: voltage and current
            if (p.Length >= 16)
            {
                latestData.voltage = float.Parse(p[14]);
                latestData.current = float.Parse(p[15]);
            }

            // NEW (optional): satellite count, if your bridge sends it as a 17th field
            if (p.Length >= 17)
            {
                latestData.satellites = (int)float.Parse(p[16]);
            }

            // Derived speed from position deltas (approximate)
            latestData.speed = Mathf.Sqrt(
                latestData.x * latestData.x +
                latestData.y * latestData.y +
                latestData.z * latestData.z
            );

            latestData.timestamp =
                (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }
        catch (Exception e)
        {
            Debug.LogError("[Drone] Parse error: " + raw + "\n" + e.Message);
            return;
        }

        // ── Vibration safety check ────────────────────────────────────────
        float maxVib = Mathf.Max(
            Mathf.Abs(latestData.vibration_x),
            Mathf.Abs(latestData.vibration_y),
            Mathf.Abs(latestData.vibration_z)
        );

        if (maxVib > vibrationThreshold && !autoLandSent)
        {
            Debug.LogWarning($"[Drone] VIBRATION CRITICAL ({maxVib:F1}) — auto-land!");
            SendCommand("LAND");
            autoLandSent         = true;
            vibrationAlarmActive = true;
        }
        else if (maxVib <= vibrationThreshold)
        {
            autoLandSent         = false;
            vibrationAlarmActive = false;
        }

        newDataAvailable = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Send a text command to the Python bridge (e.g. "LAND", "STABILIZE").</summary>
    public void SendCommand(string cmd)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(cmd);
            txClient.Send(bytes, bytes.Length, "127.0.0.1", commandPort);
            Debug.Log($"[Drone] CMD sent: {cmd}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Drone] SendCommand failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        rxClient?.Close();
        txClient?.Close();
        rxThread?.Abort();
    }
}