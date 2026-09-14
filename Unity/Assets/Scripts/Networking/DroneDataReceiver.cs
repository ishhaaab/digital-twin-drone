using System;
using System.Globalization;
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

    // Satellite count. -1 = "unknown" (bridge sends -1 until it has GPS data);
    // the signal-bars widget falls back to a latency-based proxy in that case.
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

    [Tooltip("Where commands are sent. Keep on the same machine as the bridge.")]
    public string commandTargetIP = "127.0.0.1";

    [Header("Live Data")]
    public DroneData    latestData       = new DroneData();
    public LatencyStats latencyStats     = new LatencyStats();
    public bool         newDataAvailable = false;
    public bool         isConnected      = false;

    [Header("Connection Watchdog")]
    [Tooltip("Mark disconnected after this many seconds without a packet.")]
    public float connectionTimeout = 2f;

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
    private volatile bool stopRequested = false;

    // Monotonic clock shared by the rx thread and main thread (safe to read
    // from a background thread, unlike Time.realtimeSinceStartup).
    private static readonly System.Diagnostics.Stopwatch clock =
        System.Diagnostics.Stopwatch.StartNew();
    private double lastPacketSeconds = -1.0;   // written by rx thread

    // Position deltas for derived velocity / speed (main thread only).
    private Vector3 prevPos;
    private double  prevPosSeconds = -1.0;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        try
        {
            rxClient = new UdpClient(listenPort);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Drone] Cannot open UDP port {listenPort} — is another instance of the app running? " + e.Message);
            enabled = false;      // stop Update; Start failures are unrecoverable
            return;
        }
        txClient = new UdpClient();

        stopRequested = false;
        rxThread = new Thread(ReceiveLoop) { IsBackground = true };
        rxThread.Start();

        Debug.Log($"[Drone] Listening on UDP {listenPort}, commands → {commandTargetIP}:{commandPort}");
    }

    // ─────────────────────────────────────────────────────────────────────
    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        while (!stopRequested)
        {
            try
            {
                byte[] data = rxClient.Receive(ref ep);
                lock (lockObj)
                {
                    latestRaw          = Encoding.UTF8.GetString(data);
                    lastPacketSeconds  = clock.Elapsed.TotalSeconds;
                }
                isConnected = true;
            }
            catch (ObjectDisposedException)
            {
                return;               // socket closed on shutdown — normal exit
            }
            catch (SocketException e)
            {
                if (stopRequested) return;
                Debug.LogWarning($"[Drone] UDP receive error: {e.Message}");
                Thread.Sleep(50);     // avoid hot-looping on transient errors
            }
            catch (Exception e)
            {
                if (stopRequested) return;
                Debug.LogWarning($"[Drone] Unexpected UDP error: {e.Message}");
                Thread.Sleep(50);     // don't spin — log and keep listening
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        // Connection watchdog: last packet seen within connectionTimeout?
        if (lastPacketSeconds >= 0.0 &&
            (clock.Elapsed.TotalSeconds - lastPacketSeconds) > connectionTimeout)
        {
            isConnected = false;
        }

        string raw;
        lock (lockObj)
        {
            if (string.IsNullOrEmpty(latestRaw)) return;
            raw       = latestRaw;
            latestRaw = "";
        }

        // ── Parse packet ─────────────────────────────────────────────────
        // Index map (see Tools/MAVLink/mavlink_bridge.py for the sender):
        //   0  = x
        //   1  = y
        //   2  = z
        //   3  = roll
        //   4  = pitch
        //   5  = yaw
        //   6  = status flag (always "1")
        //   7  = battery %
        //   8  = vibration_x
        //   9  = vibration_y
        //   10 = vibration_z
        //   11 = send timestamp (Unix seconds)
        //   12 = flight_mode string
        //   13 = armed (0 or 1)
        //   14 = voltage (V)
        //   15 = current (A)
        //   16 = satellites
        //   17 = lat
        //   18 = lon
        //   19 = gps_alt
        try
        {
            string[] p = raw.Trim().Split(',');

            if (p.Length < 8)
            {
                Debug.LogWarning("[Drone] Short packet: " + raw);
                return;
            }

            latestData.x     = ParseFloat(p[0]);
            latestData.y     = ParseFloat(p[1]);
            latestData.z     = ParseFloat(p[2]);
            latestData.roll  = ParseFloat(p[3]);
            latestData.pitch = ParseFloat(p[4]);
            latestData.yaw   = ParseFloat(p[5]);
            // p[6] = status flag — skip
            latestData.battery_remaining = Mathf.Clamp((int)ParseFloat(p[7]), 0, 100);

            if (p.Length >= 12)
            {
                latestData.vibration_x = ParseFloat(p[8]);
                latestData.vibration_y = ParseFloat(p[9]);
                latestData.vibration_z = ParseFloat(p[10]);

                double sendTs    = ParseDouble(p[11]);
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

            if (p.Length >= 16)
            {
                latestData.voltage = ParseFloat(p[14]);
                latestData.current = ParseFloat(p[15]);
            }

            if (p.Length >= 17)
            {
                latestData.satellites = (int)ParseFloat(p[16]);
            }

            if (p.Length >= 20)
            {
                latestData.lat      = ParseFloat(p[17]);
                latestData.lon      = ParseFloat(p[18]);
                latestData.gps_alt  = ParseFloat(p[19]);
            }

            // Derived velocity / speed from position deltas (approximate).
            ComputeVelocity();

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
    /// Estimates vx/vy/vz and speed from successive position samples,
    /// using the monotonic clock so it works even when the bridge sends no
    /// timestamps. Skips the first sample and large gaps (bridge stalls).
    void ComputeVelocity()
    {
        double now = clock.Elapsed.TotalSeconds;

        if (prevPosSeconds >= 0.0)
        {
            float dt = (float)(now - prevPosSeconds);
            if (dt > 0.001f && dt < 2f)
            {
                Vector3 pos = new Vector3(latestData.x, latestData.y, latestData.z);
                Vector3 vel = (pos - prevPos) / dt;
                latestData.vx     = vel.x;
                latestData.vy     = vel.y;
                latestData.vz     = vel.z;
                latestData.speed  = vel.magnitude;
            }
            else
            {
                // First sample or a long gap — no meaningful delta yet.
                latestData.vx = latestData.vy = latestData.vz = latestData.speed = 0f;
            }
        }

        prevPos            = new Vector3(latestData.x, latestData.y, latestData.z);
        prevPosSeconds     = now;
    }

    /// Culture-invariant float parse; NaN/±Infinity collapse to 0 so a bad
    /// value can never silently defeat threshold comparisons downstream.
    static float ParseFloat(string s)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        return 0f;
    }

    static double ParseDouble(string s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        return 0.0;
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Send a text command to the Python bridge (e.g. "LAND", "STABILIZE").</summary>
    public void SendCommand(string cmd)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(cmd);
            txClient.Send(bytes, bytes.Length, commandTargetIP, commandPort);
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
        // Cooperative shutdown: stop flag → close socket → join the thread.
        stopRequested = true;
        if (rxClient != null) rxClient.Close();
        if (txClient != null) txClient.Close();
        try
        {
            if (rxThread != null && rxThread.IsAlive) rxThread.Join(1000);
        }
        catch (ThreadStateException) { }
    }
}