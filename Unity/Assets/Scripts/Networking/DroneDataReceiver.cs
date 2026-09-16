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

    public int   battery_remaining = -1;
    public float voltage = -1f;
    public float current = -1f;

    // Satellite count. -1 means the GPS source did not provide a count.
    public int   satellites = -1;

    // Protocol-v2 metadata. Source ages are measured by the bridge at send
    // time; -1 means that MAVLink message has never been observed.
    public int protocolVersion;
    public float positionAge = -1f;
    public float attitudeAge = -1f;
    public float systemStatusAge = -1f;
    public float vibrationAge = -1f;
    public float heartbeatAge = -1f;
    public float gpsAge = -1f;
    public int gpsFixType;
    public uint sensorsPresent;
    public uint sensorsEnabled;
    public uint sensorsHealth;

    public bool homeValid;
    public float homeLat, homeLon, homeAlt;
    public float homeNorth, homeEast;

    public float vibration_x, vibration_y, vibration_z;
    public bool  armed;
    public string flight_mode = "UNKNOWN";
}

public enum DroneCommandState
{
    None,
    Sent,
    Accepted,
    InProgress,
    Completed,
    Rejected,
    TimedOut,
    Failed
}

[Serializable]
public class DroneCommandStatus
{
    public string requestId = "";
    public string command = "";
    public DroneCommandState state = DroneCommandState.None;
    public int mavResult = -1;
    public int progress = -1;
    public int resultParam2;
    public string detail = "";
    public double updatedTimestamp;
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
        min = float.MaxValue;
        max = float.MinValue;
        foreach (float sample in samples)
        {
            if (sample < min) min = sample;
            if (sample > max) max = sample;
        }
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

    [Tooltip("Local interface used for telemetry. Loopback is the safe default.")]
    public string listenAddress = "127.0.0.1";

    [Tooltip("Only datagrams from this exact source address are accepted.")]
    public string trustedTelemetrySourceIP = "127.0.0.1";

    [Tooltip("Where commands are sent. Keep on the same machine as the bridge.")]
    public string commandTargetIP = "127.0.0.1";

    [Header("Live Data")]
    public DroneData    latestData       = new DroneData();
    public LatencyStats latencyStats     = new LatencyStats();
    // Written and consumed on the main thread. Volatile preserves compatibility
    // with existing inspectors that watch these fields while the RX thread runs.
    public volatile bool newDataAvailable = false;
    public volatile bool isConnected      = false;

    [Header("Connection Watchdog")]
    [Tooltip("Mark disconnected after this many seconds without a packet.")]
    public float connectionTimeout = 2f;

    [Header("Telemetry Freshness")]
    public float positionFreshnessTimeout = 1f;
    public float attitudeFreshnessTimeout = 1f;
    public float systemStatusFreshnessTimeout = 3f;
    public float vibrationFreshnessTimeout = 2f;
    public float heartbeatFreshnessTimeout = 3f;
    public float gpsFreshnessTimeout = 3f;

    [Header("Vibration Alert")]
    [Tooltip("If any fresh vibration axis exceeds this value, a critical alert is shown. The dashboard never triggers an automatic flight action.")]
    public float vibrationThreshold = 60f;

    public bool vibrationAlarmActive { get; private set; } = false;

    [Header("Command Lifecycle")]
    [Tooltip("Seconds to wait for MAVLink COMMAND_ACK.")]
    public float commandAckTimeout = 3f;

    [Tooltip("Seconds to wait for telemetry to confirm an accepted command completed.")]
    public float commandCompletionTimeout = 30f;

    [Tooltip("Landing can legitimately take much longer than a mode change.")]
    public float landingCompletionTimeout = 180f;

    public DroneCommandStatus commandStatus = new DroneCommandStatus();

    public const int ProtocolVersion = 2;
    public const uint Sensor3dGyro = 1u << 0;
    public const uint Sensor3dAccel = 1u << 1;
    public const uint Sensor3dMag = 1u << 2;
    public const uint SensorAbsolutePressure = 1u << 3;
    public const uint SensorGps = 1u << 5;

    /// Seconds since last packet, or -1 if never received. UI presentation helper — not a safety threshold.
    /// Derived from the monotonic Stopwatch clock.
    public double LastPacketAgeSeconds
    {
        get
        {
            double lp = lastPacketSeconds;
            if (lp < 0) return -1.0;
            return clock.Elapsed.TotalSeconds - lp;
        }
    }

    public bool PositionFresh => IsSourceFresh(lastPositionSourceSeconds, positionFreshnessTimeout);
    public bool AttitudeFresh => IsSourceFresh(lastAttitudeSourceSeconds, attitudeFreshnessTimeout);
    public bool SystemStatusFresh => IsSourceFresh(lastSystemStatusSourceSeconds, systemStatusFreshnessTimeout);
    public bool VibrationFresh => IsSourceFresh(lastVibrationSourceSeconds, vibrationFreshnessTimeout);
    public bool HeartbeatFresh => IsSourceFresh(lastHeartbeatSourceSeconds, heartbeatFreshnessTimeout);
    public bool GpsFresh => IsSourceFresh(lastGpsSourceSeconds, gpsFreshnessTimeout);
    public bool GpsFixValid => GpsFresh && latestData.gpsFixType >= 3
        && IsValidCoordinate(latestData.lat, latestData.lon);
    public bool HomePositionValid => latestData.protocolVersion == ProtocolVersion
        && latestData.homeValid && IsValidCoordinate(latestData.homeLat, latestData.homeLon);
    public bool HasActiveCommand => commandStatus.state == DroneCommandState.Sent
        || commandStatus.state == DroneCommandState.Accepted
        || commandStatus.state == DroneCommandState.InProgress;
    public bool CanSendCommands => isConnected && HeartbeatFresh && !HasActiveCommand && txClient != null;

    // ── Internals ─────────────────────────────────────────────────────────
    private UdpClient rxClient;
    private UdpClient txClient;
    private Thread    rxThread;
    private string    latestRaw = "";
    private readonly Queue<string> commandEvents = new Queue<string>();
    private object    lockObj   = new object();
    private volatile bool stopRequested = false;
    private IPAddress trustedTelemetrySourceAddress;
    private bool warnedUntrustedSource;
    private bool warnedProtocolMismatch;
    private bool loggedFirstPacket;

    // Monotonic clock shared by the rx thread and main thread (safe to read
    // from a background thread, unlike Time.realtimeSinceStartup).
    private static readonly System.Diagnostics.Stopwatch clock =
        System.Diagnostics.Stopwatch.StartNew();
    private double lastPacketSeconds = -1.0;

    // Source timestamps reconstructed from the ages carried in each packet.
    private double lastPositionSourceSeconds = -1.0;
    private double lastAttitudeSourceSeconds = -1.0;
    private double lastSystemStatusSourceSeconds = -1.0;
    private double lastVibrationSourceSeconds = -1.0;
    private double lastHeartbeatSourceSeconds = -1.0;
    private double lastGpsSourceSeconds = -1.0;

    private double commandSentSeconds = -1.0;

    // Position deltas for derived velocity / speed (main thread only).
    private Vector3 prevPos;
    private double  prevPositionSourceSeconds = -1.0;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        if (latestData == null) latestData = new DroneData();
        if (commandStatus == null) commandStatus = new DroneCommandStatus();
        if (!IPAddress.TryParse(listenAddress, out IPAddress bindAddress))
        {
            Debug.LogError($"[Drone] Invalid telemetry listen address: {listenAddress}");
            enabled = false;
            return;
        }
        if (!IPAddress.TryParse(trustedTelemetrySourceIP, out trustedTelemetrySourceAddress))
        {
            Debug.LogError($"[Drone] Invalid trusted telemetry source: {trustedTelemetrySourceIP}");
            enabled = false;
            return;
        }

        try
        {
            rxClient = new UdpClient(new IPEndPoint(bindAddress, listenPort));
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

        Debug.Log($"[Drone] Listening on UDP {listenAddress}:{listenPort} from trusted source {trustedTelemetrySourceIP}, commands -> {commandTargetIP}:{commandPort}");
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
                if (!ep.Address.Equals(trustedTelemetrySourceAddress))
                {
                    if (!warnedUntrustedSource)
                    {
                        warnedUntrustedSource = true;
                        Debug.LogWarning($"[Drone] Ignoring telemetry from untrusted source {ep.Address}.");
                    }
                    continue;
                }
                if (data.Length > 4096)
                {
                    Debug.LogWarning($"[Drone] Ignoring oversized UDP datagram ({data.Length} bytes).");
                    continue;
                }

                string raw = Encoding.UTF8.GetString(data);
                lock (lockObj)
                {
                    if (raw.StartsWith("ACK,", StringComparison.Ordinal))
                    {
                        if (commandEvents.Count >= 32) commandEvents.Dequeue();
                        commandEvents.Enqueue(raw);
                    }
                    else
                    {
                        latestRaw = raw;
                    }
                }
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
        ProcessCommandEvents();

        // Connection watchdog: last packet seen within connectionTimeout?
        if (lastPacketSeconds >= 0.0 &&
            (clock.Elapsed.TotalSeconds - lastPacketSeconds) > connectionTimeout)
        {
            if (isConnected)
            {
                Debug.LogWarning($"[Drone] Telemetry link lost after {LastPacketAgeSeconds:F1} seconds without a valid packet.");
                latencyStats.Reset();
            }
            isConnected = false;
        }
        if (!VibrationFresh) vibrationAlarmActive = false;
        UpdateCommandLifecycle();

        string raw;
        lock (lockObj)
        {
            if (string.IsNullOrEmpty(latestRaw)) return;
            raw       = latestRaw;
            latestRaw = "";
        }

        // ── Parse packet ─────────────────────────────────────────────────
        // Index map (see Tools/MAVLink/telemetry_protocol.py for the sender):
        //   0  = x
        //   1  = y
        //   2  = z
        //   3  = roll
        //   4  = pitch
        //   5  = yaw
        //   6  = status
        //   7  = battery
        //   8  = vibration_x
        //   9  = vibration_y
        //   10 = vibration_z
        //   11 = send_ts
        //   12 = flight_mode
        //   13 = armed
        //   14 = voltage
        //   15 = current
        //   16 = satellites
        //   17 = lat
        //   18 = lon
        //   19 = gps_alt
        //   20 = protocol_version
        //   21 = position_age
        //   22 = attitude_age
        //   23 = system_status_age
        //   24 = vibration_age
        //   25 = heartbeat_age
        //   26 = gps_age
        //   27 = gps_fix_type
        //   28 = sensors_present
        //   29 = sensors_enabled
        //   30 = sensors_health
        //   31 = home_valid
        //   32 = home_lat
        //   33 = home_lon
        //   34 = home_alt
        //   35 = home_north
        //   36 = home_east
        try
        {
            string[] p = raw.Trim().Split(',');

            if (p.Length != 37)
            {
                Debug.LogWarning($"[Drone] Rejected telemetry packet with {p.Length} fields; protocol v{ProtocolVersion} requires 37.");
                return;
            }
            int protocolVersion = ParseInt(p[20]);
            if (protocolVersion != ProtocolVersion)
            {
                if (!warnedProtocolMismatch)
                {
                    warnedProtocolMismatch = true;
                    Debug.LogWarning($"[Drone] Rejected telemetry protocol version {protocolVersion}; expected {ProtocolVersion}.");
                }
                return;
            }
            if (p[6].Trim() != "1")
                throw new FormatException("Invalid telemetry status flag: " + p[6]);

            var next = new DroneData
            {
                x = ParseFloat(p[0]),
                y = ParseFloat(p[1]),
                z = ParseFloat(p[2]),
                roll = ParseFloat(p[3]),
                pitch = ParseFloat(p[4]),
                yaw = ParseFloat(p[5]),
                battery_remaining = ParseIntInRange(p[7], -1, 100, "battery"),
                vibration_x = ParseFloat(p[8]),
                vibration_y = ParseFloat(p[9]),
                vibration_z = ParseFloat(p[10]),
                flight_mode = ParseFlightMode(p[12]),
                armed = ParseFlag(p[13], "armed"),
                voltage = ParseUnknownOrNonnegative(p[14], "voltage"),
                current = ParseUnknownOrNonnegative(p[15], "current"),
                satellites = ParseIntInRange(p[16], -1, 254, "satellites"),
                lat = ParseFloatInRange(p[17], -90f, 90f, "latitude"),
                lon = ParseFloatInRange(p[18], -180f, 180f, "longitude"),
                gps_alt = ParseFloat(p[19]),
                protocolVersion = protocolVersion,
                positionAge = ParseSourceAge(p[21], "position age"),
                attitudeAge = ParseSourceAge(p[22], "attitude age"),
                systemStatusAge = ParseSourceAge(p[23], "system status age"),
                vibrationAge = ParseSourceAge(p[24], "vibration age"),
                heartbeatAge = ParseSourceAge(p[25], "heartbeat age"),
                gpsAge = ParseSourceAge(p[26], "GPS age"),
                gpsFixType = ParseIntInRange(p[27], 0, 255, "GPS fix type"),
                sensorsPresent = ParseUInt(p[28]),
                sensorsEnabled = ParseUInt(p[29]),
                sensorsHealth = ParseUInt(p[30]),
                homeValid = ParseFlag(p[31], "home valid"),
                homeLat = ParseFloatInRange(p[32], -90f, 90f, "home latitude"),
                homeLon = ParseFloatInRange(p[33], -180f, 180f, "home longitude"),
                homeAlt = ParseFloat(p[34]),
                homeNorth = ParseFloat(p[35]),
                homeEast = ParseFloat(p[36])
            };

            double sendTs = ParseDouble(p[11]);
            double nowTs = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            float latencyMs = (float)((nowTs - sendTs) * 1000.0);
            if (latencyMs >= 0f && latencyMs < 5000f)
                latencyStats.AddSample(latencyMs);

            double now = clock.Elapsed.TotalSeconds;
            lastPositionSourceSeconds = SourceTimestamp(next.positionAge, now);
            lastAttitudeSourceSeconds = SourceTimestamp(next.attitudeAge, now);
            lastSystemStatusSourceSeconds = SourceTimestamp(next.systemStatusAge, now);
            lastVibrationSourceSeconds = SourceTimestamp(next.vibrationAge, now);
            lastHeartbeatSourceSeconds = SourceTimestamp(next.heartbeatAge, now);
            lastGpsSourceSeconds = SourceTimestamp(next.gpsAge, now);

            // Derived velocity / speed from position deltas (approximate).
            ComputeVelocity(next, lastPositionSourceSeconds);

            next.timestamp = nowTs;
            latestData = next;
            lastPacketSeconds = now;
            isConnected = true;
            if (!loggedFirstPacket)
            {
                loggedFirstPacket = true;
                Debug.Log($"[Drone] Accepted telemetry protocol v{protocolVersion} ({p.Length} fields).");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Drone] Parse error: " + raw + "\n" + e.Message);
            return;
        }

        // Vibration is an operator alert. Flight-controller failsafes own any
        // automatic action; untrusted or malformed telemetry can never LAND.
        float maxVib = Mathf.Max(
            Mathf.Abs(latestData.vibration_x),
            Mathf.Abs(latestData.vibration_y),
            Mathf.Abs(latestData.vibration_z)
        );

        bool wasVibrationAlarmActive = vibrationAlarmActive;
        vibrationAlarmActive = VibrationFresh && maxVib >= vibrationThreshold;
        if (vibrationAlarmActive && !wasVibrationAlarmActive)
            Debug.LogWarning($"[Drone] Vibration critical ({maxVib:F1} m/s2); operator action required.");
        else if (!vibrationAlarmActive && wasVibrationAlarmActive)
            Debug.Log("[Drone] Vibration returned below the critical threshold.");

        newDataAvailable = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    /// Estimates velocity only when LOCAL_POSITION_NED has a new source time.
    /// Repeated snapshots retain the last value instead of alternating between
    /// zero and an exaggerated delta at the aggregate packet rate.
    void ComputeVelocity(DroneData data, double positionSourceSeconds)
    {
        Vector3 pos = new Vector3(data.x, data.y, data.z);
        if (positionSourceSeconds < 0.0)
        {
            data.vx = data.vy = data.vz = data.speed = 0f;
            return;
        }

        if (prevPositionSourceSeconds < 0.0)
        {
            prevPos = pos;
            prevPositionSourceSeconds = positionSourceSeconds;
            data.vx = data.vy = data.vz = data.speed = 0f;
            return;
        }

        float dt = (float)(positionSourceSeconds - prevPositionSourceSeconds);
        if (dt <= 0.01f)
        {
            data.vx = latestData.vx;
            data.vy = latestData.vy;
            data.vz = latestData.vz;
            data.speed = latestData.speed;
            return;
        }

        if (dt < 2f)
        {
            Vector3 vel = (pos - prevPos) / dt;
            data.vx = vel.x;
            data.vy = vel.y;
            data.vz = vel.z;
            data.speed = vel.magnitude;
        }
        else
        {
            data.vx = data.vy = data.vz = data.speed = 0f;
        }

        prevPos = pos;
        prevPositionSourceSeconds = positionSourceSeconds;
    }

    /// Culture-invariant finite-number parse. A malformed field rejects the
    /// entire snapshot rather than becoming a plausible zero.
    static float ParseFloat(string s)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
        {
            if (!float.IsNaN(v) && !float.IsInfinity(v)) return v;
        }
        throw new FormatException("Invalid finite float: " + s);
    }

    static double ParseDouble(string s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            if (!double.IsNaN(v) && !double.IsInfinity(v)) return v;
        }
        throw new FormatException("Invalid finite double: " + s);
    }

    static int ParseInt(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
        throw new FormatException("Invalid integer: " + s);
    }

    static uint ParseUInt(string s)
    {
        if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v)) return v;
        throw new FormatException("Invalid unsigned integer: " + s);
    }

    static int ParseIntInRange(string s, int min, int max, string field)
    {
        int value = ParseInt(s);
        if (value < min || value > max)
            throw new FormatException($"Invalid {field}: {s}");
        return value;
    }

    static float ParseFloatInRange(string s, float min, float max, string field)
    {
        float value = ParseFloat(s);
        if (value < min || value > max)
            throw new FormatException($"Invalid {field}: {s}");
        return value;
    }

    static float ParseSourceAge(string s, string field)
    {
        float value = ParseFloat(s);
        if (value < 0f && value != -1f)
            throw new FormatException($"Invalid {field}: {s}");
        return value;
    }

    static float ParseUnknownOrNonnegative(string s, string field)
    {
        float value = ParseFloat(s);
        if (value < 0f && value != -1f)
            throw new FormatException($"Invalid {field}: {s}");
        return value;
    }

    static bool ParseFlag(string s, string field)
    {
        int value = ParseInt(s);
        if (value != 0 && value != 1)
            throw new FormatException($"Invalid {field}: {s}");
        return value == 1;
    }

    static string ParseFlightMode(string value)
    {
        string mode = (value ?? "").Trim().ToUpperInvariant();
        if (mode.Length == 0 || mode.Length > 32) return "UNKNOWN";
        for (int i = 0; i < mode.Length; i++)
        {
            char c = mode[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return "UNKNOWN";
        }
        return mode;
    }

    static bool IsValidCoordinate(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) && !double.IsInfinity(latitude)
            && !double.IsNaN(longitude) && !double.IsInfinity(longitude)
            && latitude >= -90.0 && latitude <= 90.0
            && longitude >= -180.0 && longitude <= 180.0;
    }

    static double SourceTimestamp(float sourceAge, double now)
    {
        return sourceAge < 0f ? -1.0 : now - sourceAge;
    }

    bool IsSourceFresh(double sourceTimestamp, float timeout)
    {
        return isConnected && sourceTimestamp >= 0.0
            && clock.Elapsed.TotalSeconds - sourceTimestamp <= Mathf.Max(0.1f, timeout);
    }

    public bool IsSensorPresent(uint mask) => SystemStatusFresh
        && (latestData.sensorsPresent & mask) == mask;

    public bool IsSensorEnabled(uint mask) => SystemStatusFresh
        && (latestData.sensorsEnabled & mask) == mask;

    public bool IsSensorHealthy(uint mask) => SystemStatusFresh
        && (latestData.sensorsHealth & mask) == mask;

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Send one correlated command when the trusted telemetry path is ready.</summary>
    public bool SendCommand(string cmd)
    {
        string command = ParseFlightMode(cmd);
        if (!IsSupportedCommand(command))
        {
            Debug.LogWarning("[Drone] Rejected unsupported command: " + cmd);
            return false;
        }
        if (!CanSendCommands)
        {
            Debug.LogWarning($"[Drone] Command {command} blocked: telemetry/heartbeat unavailable or another command is pending.");
            return false;
        }

        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            string payload = $"CMD,{ProtocolVersion},{requestId},{command}";
            byte[] bytes = Encoding.ASCII.GetBytes(payload);
            txClient.Send(bytes, bytes.Length, commandTargetIP, commandPort);
            commandSentSeconds = clock.Elapsed.TotalSeconds;
            commandStatus.requestId = requestId;
            commandStatus.command = command;
            commandStatus.mavResult = -1;
            commandStatus.progress = -1;
            commandStatus.resultParam2 = 0;
            SetCommandState(DroneCommandState.Sent, "AWAITING ACK");
            Debug.Log($"[Drone] Command sent: {command} ({requestId})");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[Drone] SendCommand failed: " + e.Message);
            commandStatus.requestId = requestId;
            commandStatus.command = command;
            SetCommandState(DroneCommandState.Failed, "LOCAL SEND FAILED");
            return false;
        }
    }

    static bool IsSupportedCommand(string command)
    {
        return command == "LAND" || command == "FORCE_DISARM"
            || command == "STABILIZE" || command == "ALT_HOLD"
            || command == "POSHOLD";
    }

    void ProcessCommandEvents()
    {
        List<string> events = null;
        lock (lockObj)
        {
            if (commandEvents.Count > 0)
            {
                events = new List<string>(commandEvents);
                commandEvents.Clear();
            }
        }
        if (events == null) return;

        foreach (string raw in events)
        {
            string[] p = raw.Trim().Split(',');
            if (p.Length != 7 || p[0] != "ACK") continue;
            if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                || version != ProtocolVersion) continue;
            if (p[2] != commandStatus.requestId || p[3] != commandStatus.command) continue;
            if (!int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                || !int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int progress)
                || !int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int resultParam2))
                continue;

            commandStatus.mavResult = result;
            commandStatus.progress = progress == 255 ? -1 : progress;
            commandStatus.resultParam2 = resultParam2;
            if (result == 0)
                SetCommandState(DroneCommandState.Accepted, "ACKNOWLEDGED");
            else if (result == 5)
                SetCommandState(DroneCommandState.InProgress,
                    commandStatus.progress >= 0 ? $"IN PROGRESS {commandStatus.progress}%" : "IN PROGRESS");
            else
                SetCommandState(DroneCommandState.Rejected, MavResultLabel(result));
            Debug.Log($"[Drone] Command ACK: {commandStatus.command} -> {commandStatus.state} ({commandStatus.detail})");
        }
    }

    void UpdateCommandLifecycle()
    {
        if (!HasActiveCommand || commandSentSeconds < 0.0) return;
        double elapsed = clock.Elapsed.TotalSeconds - commandSentSeconds;
        if (commandStatus.state == DroneCommandState.Sent && elapsed > commandAckTimeout)
        {
            SetCommandState(DroneCommandState.TimedOut, "ACK TIMEOUT");
            Debug.LogWarning($"[Drone] Command ACK timed out: {commandStatus.command}");
            return;
        }

        if (commandStatus.state != DroneCommandState.Accepted
            && commandStatus.state != DroneCommandState.InProgress) return;

        if (IsCommandCompletionObserved())
        {
            SetCommandState(DroneCommandState.Completed, "CONFIRMED BY TELEMETRY");
            Debug.Log($"[Drone] Command completed: {commandStatus.command} (confirmed by telemetry)");
        }
        else
        {
            float completionTimeout = commandStatus.command == "LAND"
                ? landingCompletionTimeout : commandCompletionTimeout;
            if (elapsed > completionTimeout)
            {
                SetCommandState(DroneCommandState.TimedOut, "COMPLETION TIMEOUT");
                Debug.LogWarning($"[Drone] Command completion timed out: {commandStatus.command}");
            }
        }
    }

    bool IsCommandCompletionObserved()
    {
        if (!HeartbeatFresh) return false;
        if (commandStatus.command == "LAND" || commandStatus.command == "FORCE_DISARM")
            return !latestData.armed;
        return string.Equals(latestData.flight_mode, commandStatus.command, StringComparison.OrdinalIgnoreCase);
    }

    void SetCommandState(DroneCommandState state, string detail)
    {
        commandStatus.state = state;
        commandStatus.detail = detail;
        commandStatus.updatedTimestamp =
            (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
    }

    static string MavResultLabel(int result)
    {
        switch (result)
        {
            case 1: return "TEMPORARILY REJECTED";
            case 2: return "DENIED";
            case 3: return "UNSUPPORTED";
            case 4: return "FAILED";
            case 6: return "CANCELLED";
            default: return "REJECTED (" + result + ")";
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
