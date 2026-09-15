using UnityEngine;
using TMPro;

// DroneUIUpdater — aerospace GCS revision
// Reads DroneDataReceiver every frame and pushes verified telemetry to:
//   (a) legacy plain-text fields (kept intact for backward compat)
//   (b) aerospace widgets (rings, bars, horizon, ribbon, graphs, pills)
// All smoothing/colour lives in DroneUIFX/TelemetryGraph; this script is strictly "read → hand off".
// Every field displayed here is traced to a real source (see header comments per section).
public class DroneUIUpdater : MonoBehaviour
{
    public DroneDataReceiver dataReceiver;

    // ── TOP BAR (legacy kept) ──────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI connectionText;   // now alias to connectionPill.label
    [HideInInspector] public TextMeshProUGUI latencyText;      // alias to latencyPill.label / ring value
    [HideInInspector] public TextMeshProUGUI uptimeText;
    [HideInInspector] public TextMeshProUGUI packetCountText;  // total pkts
    [HideInInspector] public TextMeshProUGUI frameCountText;   // alias to packetCountText for compat
    // New top-bar fields (verified sources noted below)
    [HideInInspector] public TextMeshProUGUI flightModeText;   // source: DroneData.flight_mode <- HEARTBEAT.custom_mode via mavlink_bridge.py:212
    [HideInInspector] public TextMeshProUGUI armedText;         // source: DroneData.armed <- HEARTBEAT.base_mode armed flag
    [HideInInspector] public TextMeshProUGUI satTopText;        // source: DroneData.satellites <- GPS_RAW_INT.satellites_visible
    [HideInInspector] public TextMeshProUGUI gpsSatText;        // right-panel satellite count (same source as satTopText)
    [HideInInspector] public TextMeshProUGUI topHeadingText;
    [HideInInspector] public DroneUIFX.AeroStatusPill connectionPill;
    [HideInInspector] public DroneUIFX.AeroStatusPill latencyPill;

    // ── RIGHT/LEFT PANEL text ──────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI pitchText;   // ATTITUDE.pitch -> DroneData.pitch (deg)
    [HideInInspector] public TextMeshProUGUI rollText;    // ATTITUDE.roll  -> DroneData.roll
    [HideInInspector] public TextMeshProUGUI yawText;     // ATTITUDE.yaw   -> DroneData.yaw

    [HideInInspector] public TextMeshProUGUI latText;     // GPS_RAW_INT.lat -> DroneData.lat
    [HideInInspector] public TextMeshProUGUI lonText;     // GPS_RAW_INT.lon -> DroneData.lon
    [HideInInspector] public TextMeshProUGUI gpsAltText;  // GPS_RAW_INT.alt -> DroneData.gps_alt

    [HideInInspector] public TextMeshProUGUI voltageText; // SYS_STATUS.voltage_battery -> DroneData.voltage
    [HideInInspector] public TextMeshProUGUI currentText; // SYS_STATUS.current_battery -> DroneData.current

    [HideInInspector] public TextMeshProUGUI xText; // LOCAL_POSITION_NED.x -> DroneData.x (NED N)
    [HideInInspector] public TextMeshProUGUI yText; // LOCAL_POSITION_NED.y -> DroneData.y (NED E)
    [HideInInspector] public TextMeshProUGUI zText; // LOCAL_POSITION_NED.z sign-flipped -> DroneData.z (up+)
    [HideInInspector] public TextMeshProUGUI speedText; // derived ComputeVelocity -> DroneData.speed
    [HideInInspector] public TextMeshProUGUI verticalSpeedText; // derived ComputeVelocity -> DroneData.vz
    [HideInInspector] public TextMeshProUGUI batteryPercentText; // SYS_STATUS.battery_remaining -> DroneData.battery_remaining

    [HideInInspector] public TextMeshProUGUI vibXText; // VIBRATION.vibration_x -> DroneData.vibration_x
    [HideInInspector] public TextMeshProUGUI vibYText;
    [HideInInspector] public TextMeshProUGUI vibZText;
    [HideInInspector] public TextMeshProUGUI vibStatusText;

    [HideInInspector] public TextMeshProUGUI latMeanText; // LatencyStats.mean
    [HideInInspector] public TextMeshProUGUI latMinText;  // LatencyStats.min
    [HideInInspector] public TextMeshProUGUI latMaxText;  // LatencyStats.max
    [HideInInspector] public TextMeshProUGUI latVarText;  // LatencyStats.variance
    [HideInInspector] public TextMeshProUGUI packetRateText; // derived pkts/s
    [HideInInspector] public TextMeshProUGUI packetLossText; // unavailable without a packet sequence number

    // ── ALARM ──────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI alarmBannerText;
    [HideInInspector] public UnityEngine.UI.Image alarmBannerBg;
    [HideInInspector] public UnityEngine.UI.Image batteryCardBg; // legacy compile shim

    // ── HUD STRIP (center overlay) ─────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI hudHeadingText;
    [HideInInspector] public TextMeshProUGUI centerHeadingText;
    [HideInInspector] public TextMeshProUGUI hudAltText;
    [HideInInspector] public TextMeshProUGUI hudSpeedText;
    [HideInInspector] public TextMeshProUGUI hudModeText;
    [HideInInspector] public TextMeshProUGUI hudArmedText;
    [HideInInspector] public TextMeshProUGUI hudLatText;
    [HideInInspector] public TextMeshProUGUI hudLonText;
    [HideInInspector] public TextMeshProUGUI hudGpsAltText;
    [HideInInspector] public DroneMapView mapView;

    // ── GRAPH refs ─────────────────────────────────────────────────────
    [HideInInspector] public TelemetryGraph altitudeGraph;
    [HideInInspector] public TextMeshProUGUI altitudeGraphValue;
    [HideInInspector] public TelemetryGraph speedGraph;
    [HideInInspector] public TextMeshProUGUI speedGraphValue;
    [HideInInspector] public TelemetryGraph latencyGraph;
    [HideInInspector] public TextMeshProUGUI latencyGraphValue;
    [HideInInspector] public TelemetryGraph batteryGraph;
    [HideInInspector] public TextMeshProUGUI batteryGraphValue;

    // ── SYSTEM STATUS ──────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI telemetryStatusText;
    [HideInInspector] public TextMeshProUGUI gpsStatusText;
    [HideInInspector] public TextMeshProUGUI imuStatusText;
    [HideInInspector] public TextMeshProUGUI powerStatusText;
    [HideInInspector] public TextMeshProUGUI barometerStatusText;

    // ── FLIGHT MODE BUTTON STATES ───────────────────────────────────────
    [HideInInspector] public DroneUIFX.AeroButtonHoverFX stabilizeButtonStyle;
    [HideInInspector] public DroneUIFX.AeroButtonHoverFX altHoldButtonStyle;
    [HideInInspector] public DroneUIFX.AeroButtonHoverFX posHoldButtonStyle;
    [HideInInspector] public DroneUIFX.AeroButtonHoverFX landButtonStyle;
    [HideInInspector] public DroneUIFX.AeroButtonHoverFX forceDisarmButtonStyle;
    [HideInInspector] public TextMeshProUGUI commandStatusText;

    // ── FX widgets ─────────────────────────────────────────────────────
    [HideInInspector] public DroneUIFX.RadialGaugeFX   batteryRing;
    [HideInInspector] public DroneUIFX.HorizontalBarFX batteryBar;   // flat battery bar (text-first POWER readout)
    [HideInInspector] public DroneUIFX.RadialGaugeFX   latencyGauge;
    [HideInInspector] public DroneUIFX.RadialGaugeFX   satelliteRing;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibXBar;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibYBar;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibZBar;
    [HideInInspector] public DroneUIFX.CompassRibbonFX compassRibbon;
    [HideInInspector] public DroneUIFX.HorizonFX       horizon;
    [HideInInspector] public DroneUIFX.SignalBarsFX    gpsSignalBars;
    [HideInInspector] public DroneUIFX.SignalBarsFX    linkSignalBars;

    // ── Thresholds (UI presentation thresholds, NOT manufacturer safety limits) ──
    [Header("Battery Thresholds (UI presentation)")]
    public int batteryLowPercent = 30;
    public int batteryCriticalPercent = 15;

    [Header("Vibration Thresholds (m/s²) — UI presentation")]
    public float vibHighThreshold = 30f;
    [Tooltip("Display CRITICAL threshold. Kept in lockstep with DroneDataReceiver.vibrationThreshold at runtime.")]
    public float vibCriticalThreshold = 60f;
    public float vibGaugeMax = 80f;

    [Header("Link / Latency (UI presentation)")]
    public float latencyGaugeMaxMs = 300f;
    public float latencyHighMs = 180f;      // amber threshold for pill
    public float latencyCriticalMs = 300f;  // red threshold for pill

    [Header("GPS quality")]
    public float satellitesForFullBars = 12f;
    public int gpsDegradedSats = 6;         // below this = degraded (when sats >=0)

    [Header("Connection UI thresholds (seconds, UI presentation only)")]
    [Tooltip("TELEMETRY DELAYED after this many seconds without a packet (isConnected uses connectionTimeout=2s).")]
    public float delayedThreshold = 5f;

    static readonly Color BG_ALARM = new Color(0.78f, 0.30f, 0.30f, 0.94f);
    static readonly Color BG_WARN  = new Color(0.86f, 0.62f, 0.22f, 0.92f);
    static readonly Color COL_STALE = new Color(0.48f, 0.53f, 0.60f);

    float uptime = 0f;
    int packets = 0;
    float alarmFlashTimer = 0f;

    // packet rate — sliding 1s window
    float rateWindow = 0f;
    int rateCount = 0;
    float displayedRate = 0f;
    const float GraphSamplePeriod = 0.2f;
    float graphSampleElapsed = GraphSamplePeriod;

    void Update()
    {
        uptime += Time.deltaTime;
        graphSampleElapsed += Time.deltaTime;
        SetText(uptimeText, FormatTime(uptime));
        if (dataReceiver == null) return;

        vibCriticalThreshold = dataReceiver.vibrationThreshold;
        var d = dataReceiver.latestData;
        var ls = dataReceiver.latencyStats;
        double packetAge = dataReceiver.LastPacketAgeSeconds;
        bool never = packetAge < 0.0;
        float age = (float)packetAge;

        string connLabel; Color connBg; Color connDot;
        if (never || age > delayedThreshold)
        {
            connLabel = "CONNECTION LOST"; connBg = new Color(0.78f,0.30f,0.30f,0.16f); connDot = DroneUIFX.AERO_RED;
        }
        else if (!dataReceiver.isConnected)
        {
            connLabel = "TELEMETRY DELAYED"; connBg = new Color(0.86f,0.62f,0.22f,0.14f); connDot = DroneUIFX.AERO_AMBER;
        }
        else
        {
            connLabel = "CONNECTED"; connBg = new Color(0.26f,0.74f,0.52f,0.14f); connDot = DroneUIFX.AERO_GREEN;
        }
        connectionPill?.SetState(connBg, connDot, connLabel);
        SetText(connectionText, connLabel);

        bool heartbeatFresh = dataReceiver.HeartbeatFresh;
        bool armed = heartbeatFresh && d.armed;
        string mode = heartbeatFresh ? d.flight_mode : "UNKNOWN";
        SetText(flightModeText, mode);
        SetText(hudModeText, mode);
        SetText(armedText, heartbeatFresh ? armed ? "ARMED" : "DISARMED" : "UNKNOWN");
        SetText(hudArmedText, heartbeatFresh ? armed ? "ARMED" : "DISARMED" : "UNKNOWN");
        SetTextColor(armedText, !heartbeatFresh ? COL_STALE : armed ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_AMBER);
        SetTextColor(hudArmedText, !heartbeatFresh ? COL_STALE : armed ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_AMBER);
        string normalizedMode = heartbeatFresh ? (d.flight_mode ?? "").ToUpperInvariant() : "";
        stabilizeButtonStyle?.SetSelected(normalizedMode == "STABILIZE");
        altHoldButtonStyle?.SetSelected(normalizedMode == "ALT_HOLD");
        posHoldButtonStyle?.SetSelected(normalizedMode == "POSHOLD");

        bool linkFresh = dataReceiver.isConnected && ls.min < float.MaxValue;
        if (linkFresh)
        {
            SetText(latMeanText, $"{ls.mean:F0}");
            SetText(latMinText,  $"{ls.min:F1}");
            SetText(latMaxText,  $"{ls.max:F1}");
            SetText(latVarText,  $"{Mathf.Sqrt(Mathf.Max(0f, ls.variance)):F1}");
            string latLabel = $"{ls.mean:F0} ms";
            SetText(latencyText, latLabel);
            Color latBg, latDot;
            if (ls.mean >= latencyCriticalMs) { latBg = new Color(0.78f,0.30f,0.30f,0.14f); latDot = DroneUIFX.AERO_RED; }
            else if (ls.mean >= latencyHighMs) { latBg = new Color(0.86f,0.62f,0.22f,0.14f); latDot = DroneUIFX.AERO_AMBER; }
            else { latBg = new Color(0.26f,0.74f,0.52f,0.12f); latDot = DroneUIFX.AERO_GREEN; }
            latencyPill?.SetState(latBg, latDot, latLabel);
            linkSignalBars?.SetQuality01(1f - Mathf.Clamp01(ls.mean / latencyGaugeMaxMs));
            if (latencyGauge != null)
            {
                latencyGauge.SetTarget01(1f - Mathf.Clamp01(ls.mean / latencyGaugeMaxMs));
                if (latencyGauge.valueText != null) latencyGauge.valueText.text = $"{ls.mean:F0}";
            }
            if (latencyGraphValue != null)
            {
                latencyGraphValue.text = $"{ls.mean:F0} ms";
                latencyGraphValue.color = ls.mean >= latencyCriticalMs ? DroneUIFX.AERO_RED
                    : ls.mean >= latencyHighMs ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;
            }
        }
        else
        {
            latencyPill?.SetState(new Color(0.14f,0.17f,0.22f,1f), COL_STALE, "-- ms");
            SetText(latencyText, "-- ms");
            SetText(latMeanText, "--"); SetText(latMinText, "--");
            SetText(latMaxText, "--"); SetText(latVarText, "--");
            if (latencyGauge?.valueText != null) latencyGauge.valueText.text = "--";
            if (latencyGraphValue != null) latencyGraphValue.text = "-- ms";
            linkSignalBars?.SetQuality01(0f);
        }
        latencyGauge?.SetAvailable(linkFresh);

        bool hasNewData = dataReceiver.newDataAvailable;
        if (hasNewData)
        {
            dataReceiver.newDataAvailable = false;
            packets++;
            rateCount++;
            SetText(packetCountText, packets.ToString());
            SetText(frameCountText, packets.ToString());
        }
        rateWindow += Time.deltaTime;
        if (rateWindow >= 1f)
        {
            displayedRate = rateCount / rateWindow;
            rateWindow = 0f;
            rateCount = 0;
            SetText(packetRateText, $"{displayedRate:F1}");
        }
        SetText(packetLossText, "--");

        bool gpsFresh = dataReceiver.GpsFresh;
        bool gpsValid = dataReceiver.GpsFixValid;
        if (gpsFresh && d.satellites >= 0)
        {
            string satStr = d.satellites.ToString();
            SetText(satTopText, satStr + " SAT");
            SetText(gpsSatText, satStr);
            Color satelliteColor = gpsValid && d.satellites >= gpsDegradedSats
                ? DroneUIFX.AERO_NUM : DroneUIFX.AERO_AMBER;
            SetTextColor(satTopText, satelliteColor);
            SetTextColor(gpsSatText, satelliteColor);
        }
        else
        {
            SetText(satTopText, "-- SAT");
            SetText(gpsSatText, "--");
            SetTextColor(satTopText, COL_STALE);
            SetTextColor(gpsSatText, COL_STALE);
        }

        string telemetryState = dataReceiver.isConnected ? "OK" : never || age > delayedThreshold ? "LOST" : "STALE";
        Color telemetryColor = dataReceiver.isConnected ? DroneUIFX.AERO_GREEN
            : telemetryState == "LOST" ? DroneUIFX.AERO_RED : DroneUIFX.AERO_AMBER;
        SetStatus(telemetryStatusText, telemetryState, telemetryColor);
        UpdateGpsStatus(d, gpsFresh, gpsValid);
        UpdateSensorStatus(imuStatusText, DroneDataReceiver.Sensor3dGyro | DroneDataReceiver.Sensor3dAccel);
        UpdateSensorStatus(barometerStatusText, DroneDataReceiver.SensorAbsolutePressure);
        UpdateCommandPresentation(armed);

        bool positionFresh = dataReceiver.PositionFresh;
        SetTelemetryValue(xText, positionFresh, $"{d.x:F2}");
        SetTelemetryValue(yText, positionFresh, $"{d.y:F2}");
        SetTelemetryValue(zText, positionFresh, $"{d.z:F2}");
        SetTelemetryValue(speedText, positionFresh, $"{d.speed:F2}");
        SetTelemetryValue(verticalSpeedText, positionFresh, $"{d.vz:F2}");
        SetText(hudAltText, positionFresh ? $"{d.z:F1} m" : "-- m");
        SetText(hudSpeedText, positionFresh ? $"{d.speed:F1} m/s" : "-- m/s");
        SetTextColor(hudAltText, !positionFresh ? COL_STALE
            : d.z < 0.15f ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM);

        bool attitudeFresh = dataReceiver.AttitudeFresh;
        SetTelemetryValue(pitchText, attitudeFresh, $"{d.pitch:F1}°");
        SetTelemetryValue(rollText, attitudeFresh, $"{d.roll:F1}°");
        SetTelemetryValue(yawText, attitudeFresh, $"{d.yaw:F1}°");
        string heading = attitudeFresh ? $"{WrapYaw(d.yaw):000}°" : "---°";
        SetText(hudHeadingText, heading);
        SetText(topHeadingText, heading);
        SetText(centerHeadingText, heading);
        SetTextColor(topHeadingText, attitudeFresh ? DroneUIFX.AERO_NUM : COL_STALE);
        if (attitudeFresh)
        {
            horizon?.SetAttitude(d.pitch, d.roll);
            compassRibbon?.SetHeading(d.yaw);
        }
        horizon?.SetAvailable(attitudeFresh);
        compassRibbon?.SetAvailable(attitudeFresh);

        mapView?.SetTelemetry(
            d.x, d.y, d.lat, d.lon, d.yaw,
            positionFresh, gpsValid, attitudeFresh,
            dataReceiver.HomePositionValid,
            d.homeNorth, d.homeEast, d.homeLat, d.homeLon);

        SetTelemetryValue(latText, gpsValid, $"{d.lat:F4}");
        SetTelemetryValue(lonText, gpsValid, $"{d.lon:F4}");
        SetTelemetryValue(gpsAltText, gpsValid, $"{d.gps_alt:F1}");
        SetText(hudLatText, gpsValid ? $"{d.lat:F4}" : "--");
        SetText(hudLonText, gpsValid ? $"{d.lon:F4}" : "--");
        SetText(hudGpsAltText, gpsValid ? $"{d.gps_alt:F1} m" : "-- m");

        float gpsQuality = ComputeGpsQuality(d);
        gpsSignalBars?.SetQuality01(gpsQuality);
        if (satelliteRing != null)
        {
            if (gpsFresh && d.satellites >= 0)
            {
                satelliteRing.SetTarget01(gpsQuality);
                if (satelliteRing.valueText != null)
                {
                    satelliteRing.valueText.text = d.satellites.ToString();
                    satelliteRing.valueText.color = gpsValid && d.satellites >= gpsDegradedSats
                        ? Color.white : DroneUIFX.AERO_AMBER;
                }
            }
            else if (satelliteRing.valueText != null)
            {
                satelliteRing.SetTarget01(0f);
                satelliteRing.valueText.text = "--";
                satelliteRing.valueText.color = COL_STALE;
            }
        }
        satelliteRing?.SetAvailable(gpsFresh);

        bool systemFresh = dataReceiver.SystemStatusFresh;
        bool voltageValid = systemFresh && d.voltage >= 0f;
        bool currentValid = systemFresh && d.current >= 0f;
        SetTelemetryValue(voltageText, voltageValid, $"{d.voltage:F2}");
        SetTelemetryValue(currentText, currentValid, $"{d.current:F1}");
        if (voltageText != null && voltageValid)
            voltageText.color = d.voltage < 14.0f ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;

        bool sampleGraphs = hasNewData && graphSampleElapsed >= GraphSamplePeriod;
        if (sampleGraphs) graphSampleElapsed %= GraphSamplePeriod;
        if (altitudeGraph != null)
        {
            if (sampleGraphs && positionFresh) altitudeGraph.Push(d.z);
            if (altitudeGraphValue != null) altitudeGraphValue.text = positionFresh ? $"{d.z:F1} m" : "-- m";
        }
        if (speedGraph != null)
        {
            if (sampleGraphs && positionFresh) speedGraph.Push(d.speed);
            if (speedGraphValue != null) speedGraphValue.text = positionFresh ? $"{d.speed:F2} m/s" : "-- m/s";
        }
        if (latencyGraph != null && sampleGraphs && linkFresh) latencyGraph.Push(ls.mean);

        bool batteryValid = systemFresh && d.battery_remaining >= 0;
        int batt = d.battery_remaining;
        SetText(batteryPercentText, batteryValid ? $"{batt}%" : "--%");
        if (batteryValid)
        {
            batteryRing?.SetTarget01(batt / 100f);
            batteryBar?.SetValue01(batt / 100f);
        }
        batteryRing?.SetAvailable(batteryValid);
        batteryBar?.SetAvailable(batteryValid);
        if (batteryGraph != null)
        {
            if (sampleGraphs && batteryValid) batteryGraph.Push(batt);
            if (batteryGraphValue != null) batteryGraphValue.text = batteryValid ? $"{batt}%" : "--%";
        }
        if (batteryPercentText != null)
        {
            batteryPercentText.color = !batteryValid ? COL_STALE
                : batt <= batteryCriticalPercent ? DroneUIFX.AERO_RED
                : batt <= batteryLowPercent ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;
        }

        bool vibrationFresh = dataReceiver.VibrationFresh;
        vibXBar?.SetAvailable(vibrationFresh);
        vibYBar?.SetAvailable(vibrationFresh);
        vibZBar?.SetAvailable(vibrationFresh);
        if (vibrationFresh)
        {
            vibXBar?.SetValue(d.vibration_x);
            vibYBar?.SetValue(d.vibration_y);
            vibZBar?.SetValue(d.vibration_z);
        }
        UpdateVibrationField(vibXText, d.vibration_x, vibrationFresh);
        UpdateVibrationField(vibYText, d.vibration_y, vibrationFresh);
        UpdateVibrationField(vibZText, d.vibration_z, vibrationFresh);
        float maxVib = Mathf.Max(Mathf.Abs(d.vibration_x), Mathf.Abs(d.vibration_y), Mathf.Abs(d.vibration_z));
        if (vibStatusText != null)
        {
            if (!vibrationFresh) { vibStatusText.text = "UNKNOWN"; vibStatusText.color = COL_STALE; }
            else if (maxVib >= vibCriticalThreshold) { vibStatusText.text = "CRITICAL"; vibStatusText.color = DroneUIFX.AERO_RED; }
            else if (maxVib >= vibHighThreshold) { vibStatusText.text = "HIGH"; vibStatusText.color = DroneUIFX.AERO_AMBER; }
            else { vibStatusText.text = "OK"; vibStatusText.color = DroneUIFX.AERO_GREEN; }
        }

        UpdateAlarm(d, ls, never, age, batteryValid, gpsFresh, gpsValid, linkFresh);
    }

    float WrapYaw(float y) { y %= 360f; if (y < 0) y += 360f; return y; }

    float ComputeGpsQuality(DroneData d)
    {
        if (!dataReceiver.GpsFixValid || d.satellites < 0) return 0f;
        return Mathf.Clamp01(d.satellites / satellitesForFullBars);
    }

    void UpdateGpsStatus(DroneData d, bool gpsFresh, bool gpsValid)
    {
        if (!gpsFresh)
        {
            SetStatus(gpsStatusText, "UNKNOWN", COL_STALE);
            return;
        }
        bool healthReported = dataReceiver.IsSensorPresent(DroneDataReceiver.SensorGps);
        if (healthReported && !dataReceiver.IsSensorEnabled(DroneDataReceiver.SensorGps))
        {
            SetStatus(gpsStatusText, "DISABLED", DroneUIFX.AERO_AMBER);
            return;
        }
        if (healthReported && !dataReceiver.IsSensorHealthy(DroneDataReceiver.SensorGps))
        {
            SetStatus(gpsStatusText, "FAULT", DroneUIFX.AERO_RED);
            return;
        }
        if (!gpsValid)
        {
            SetStatus(gpsStatusText, "NO FIX", DroneUIFX.AERO_AMBER);
            return;
        }
        bool degraded = d.satellites >= 0 && d.satellites < gpsDegradedSats;
        string state = !healthReported ? "FIX ONLY" : degraded ? "DEGRADED" : "OK";
        Color color = !healthReported || degraded ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_GREEN;
        SetStatus(gpsStatusText, state, color);
    }

    void UpdateSensorStatus(TextMeshProUGUI target, uint mask)
    {
        if (!dataReceiver.SystemStatusFresh)
        {
            SetStatus(target, "UNKNOWN", COL_STALE);
        }
        else if (!dataReceiver.IsSensorPresent(mask))
        {
            SetStatus(target, "NOT PRESENT", COL_STALE);
        }
        else if (!dataReceiver.IsSensorEnabled(mask))
        {
            SetStatus(target, "DISABLED", DroneUIFX.AERO_AMBER);
        }
        else if (!dataReceiver.IsSensorHealthy(mask))
        {
            SetStatus(target, "FAULT", DroneUIFX.AERO_RED);
        }
        else
        {
            SetStatus(target, "OK", DroneUIFX.AERO_GREEN);
        }
    }

    void UpdateCommandPresentation(bool armed)
    {
        bool imuReady = dataReceiver.IsSensorPresent(DroneDataReceiver.Sensor3dGyro | DroneDataReceiver.Sensor3dAccel)
            && dataReceiver.IsSensorEnabled(DroneDataReceiver.Sensor3dGyro | DroneDataReceiver.Sensor3dAccel)
            && dataReceiver.IsSensorHealthy(DroneDataReceiver.Sensor3dGyro | DroneDataReceiver.Sensor3dAccel);
        bool barometerReady = dataReceiver.IsSensorPresent(DroneDataReceiver.SensorAbsolutePressure)
            && dataReceiver.IsSensorEnabled(DroneDataReceiver.SensorAbsolutePressure)
            && dataReceiver.IsSensorHealthy(DroneDataReceiver.SensorAbsolutePressure);
        bool gpsReady = dataReceiver.IsSensorPresent(DroneDataReceiver.SensorGps)
            && dataReceiver.IsSensorEnabled(DroneDataReceiver.SensorGps)
            && dataReceiver.IsSensorHealthy(DroneDataReceiver.SensorGps)
            && dataReceiver.GpsFixValid;
        bool ready = dataReceiver.CanSendCommands;
        stabilizeButtonStyle?.SetInteractable(ready && imuReady);
        altHoldButtonStyle?.SetInteractable(ready && imuReady && barometerReady);
        posHoldButtonStyle?.SetInteractable(ready && imuReady && barometerReady && gpsReady);
        landButtonStyle?.SetInteractable(ready && armed);
        forceDisarmButtonStyle?.SetInteractable(ready && armed);

        if (commandStatusText == null) return;
        DroneCommandStatus status = dataReceiver.commandStatus;
        if (status.state == DroneCommandState.None)
        {
            commandStatusText.text = ready ? "COMMAND LINK READY" : "COMMANDS LOCKED";
            commandStatusText.color = ready ? DroneUIFX.AERO_GREEN : COL_STALE;
            return;
        }

        string state = status.state.ToString().ToUpperInvariant();
        commandStatusText.text = $"{status.command}  /  {state}  /  {status.detail}";
        switch (status.state)
        {
            case DroneCommandState.Completed:
                commandStatusText.color = DroneUIFX.AERO_GREEN;
                break;
            case DroneCommandState.Sent:
            case DroneCommandState.Accepted:
            case DroneCommandState.InProgress:
                commandStatusText.color = DroneUIFX.AERO_AMBER;
                break;
            default:
                commandStatusText.color = DroneUIFX.AERO_RED;
                break;
        }
    }

    void UpdateAlarm(DroneData d, LatencyStats ls, bool never, float packetAge,
        bool batteryValid, bool gpsFresh, bool gpsValid, bool linkFresh)
    {
        bool vibrationCritical = dataReceiver.VibrationFresh && dataReceiver.vibrationAlarmActive;
        bool batteryCritical = batteryValid && d.battery_remaining <= batteryCriticalPercent;
        bool batteryLow = batteryValid && d.battery_remaining <= batteryLowPercent && !batteryCritical;
        bool latencyHigh = linkFresh && ls.mean >= latencyCriticalMs;
        bool gpsNoFix = gpsFresh && !gpsValid;
        bool gpsStale = dataReceiver.isConnected && !gpsFresh;
        bool connectionLost = never || packetAge > delayedThreshold;

        bool anyCritical = vibrationCritical || batteryCritical || connectionLost;
        bool anyWarning = batteryLow || latencyHigh || gpsNoFix || gpsStale;
        if (alarmBannerBg == null) return;

        if (!anyCritical && !anyWarning)
        {
            alarmFlashTimer = 0f;
            alarmBannerBg.color = new Color(0, 0, 0, 0);
            SetText(alarmBannerText, "");
            return;
        }

        alarmFlashTimer += Time.deltaTime;
        bool flash = anyCritical
            ? (int)(alarmFlashTimer * 4) % 2 == 0
            : (int)(alarmFlashTimer * 2) % 2 == 0;
        Color background = anyCritical ? BG_ALARM : BG_WARN;
        alarmBannerBg.color = flash ? background : new Color(0, 0, 0, 0);
        var message = new System.Text.StringBuilder();
        if (vibrationCritical) message.Append("VIBRATION CRITICAL   ");
        if (batteryCritical) message.Append("BATTERY CRITICAL - LAND NOW   ");
        else if (batteryLow) message.Append("BATTERY LOW   ");
        if (connectionLost) message.Append("LINK LOST   ");
        else if (latencyHigh) message.Append("HIGH LATENCY   ");
        if (gpsNoFix) message.Append("GPS NO FIX   ");
        else if (gpsStale) message.Append("GPS TELEMETRY STALE   ");
        SetText(alarmBannerText, (anyCritical ? "!!  " : "!  ") + message.ToString().Trim());
        if (alarmBannerText != null) alarmBannerText.fontSize = anyCritical ? 13 : 12;
    }

    void SetTelemetryValue(TextMeshProUGUI target, bool valid, string value)
    {
        if (target == null) return;
        target.text = valid ? value : "--";
        target.color = valid ? DroneUIFX.AERO_NUM : COL_STALE;
    }

    void UpdateVibrationField(TextMeshProUGUI t, float val, bool valid)
    {
        if (t == null) return;
        if (!valid)
        {
            t.text = "--";
            t.color = COL_STALE;
            return;
        }
        t.text = $"{val:F2}";
        float a = Mathf.Abs(val);
        if (a >= vibCriticalThreshold) t.color = DroneUIFX.AERO_RED;
        else if (a >= vibHighThreshold) t.color = DroneUIFX.AERO_AMBER;
        else t.color = DroneUIFX.AERO_ACCENT;
    }

    void SetStatus(TextMeshProUGUI value, string text, Color color)
    {
        if (value == null) return;
        value.text = text;
        value.color = color;
        var dot = value.transform.parent.Find("Dot");
        if (dot != null)
        {
            var image = dot.GetComponent<UnityEngine.UI.Image>();
            if (image != null) image.color = color;
        }
    }

    void SetText(TextMeshProUGUI t, string v) { if (t != null) t.text = v; }
    void SetTextColor(TextMeshProUGUI t, Color c) { if (t != null) t.color = c; }
    string FormatTime(float t)
    {
        int h = (int)(t / 3600);
        int m = (int)((t % 3600) / 60);
        int s = (int)(t % 60);
        return $"{h:00}:{m:00}:{s:00}";
    }
}
