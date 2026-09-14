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
    public float gpsGoodLatencyMs = 80f;
    public float gpsPoorLatencyMs = 400f;
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
    bool batteryCriticalLandSent = false;
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

        // ── Connection pill (3-state) — UI presentation thresholds, not safety requirements ──
        // isConnected = lastPacket within connectionTimeout (2s default). We add a second level:
        //   DELAYED = 2..5s without packet, LOST = >5s or never.
        string connLabel; Color connBg; Color connDot;
        bool never = dataReceiver.LastPacketAgeSeconds < 0; // no packet yet
        float age = (float)dataReceiver.LastPacketAgeSeconds;
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
        if (connectionPill != null) connectionPill.SetState(connBg, connDot, connLabel);
        // legacy text alias
        SetText(connectionText, connLabel);

        // Also push to HUD armed/mode even when no new packet (they are sticky)
        var d0 = dataReceiver.latestData;
        SetText(flightModeText, d0.flight_mode ?? "UNKNOWN");
        SetText(hudModeText, d0.flight_mode ?? "UNKNOWN");
        bool armed0 = d0.armed;
        SetText(armedText, armed0 ? "ARMED" : "DISARMED");
        SetText(hudArmedText, armed0 ? "ARMED" : "DISARMED");
        if (armedText != null) armedText.color = armed0 ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_AMBER;
        if (hudArmedText != null) hudArmedText.color = armed0 ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_AMBER;
        string normalizedMode = (d0.flight_mode ?? "").Replace(" ", "_").ToUpperInvariant();
        stabilizeButtonStyle?.SetSelected(normalizedMode == "STABILIZE");
        altHoldButtonStyle?.SetSelected(normalizedMode == "ALT_HOLD");
        posHoldButtonStyle?.SetSelected(normalizedMode == "POSHOLD" || normalizedMode == "POS_HOLD");

        // ── Latency pill + gauge ────────────────────────────────────────
        var ls = dataReceiver.latencyStats;
        if (ls.min < float.MaxValue)
        {
            // legacy rows — units baked in for right-panel text-first rows
            SetText(latMeanText, $"{ls.mean:F0}");
            SetText(latMinText,  $"{ls.min:F1}");
            SetText(latMaxText,  $"{ls.max:F1}");
            SetText(latVarText,  $"{Mathf.Sqrt(Mathf.Max(0f, ls.variance)):F1}");

            string latStr = $"{ls.mean:F0} ms";
            SetText(latencyText, latStr);
            // pill colour by latency (UI presentation)
            Color latBg, latDot; string latLabel = $"{ls.mean:F0} ms";
            if (ls.mean >= latencyCriticalMs) { latBg = new Color(0.78f,0.30f,0.30f,0.14f); latDot = DroneUIFX.AERO_RED; }
            else if (ls.mean >= latencyHighMs) { latBg = new Color(0.86f,0.62f,0.22f,0.14f); latDot = DroneUIFX.AERO_AMBER; }
            else { latBg = new Color(0.26f,0.74f,0.52f,0.12f); latDot = DroneUIFX.AERO_GREEN; }
            if (latencyPill != null) latencyPill.SetState(latBg, latDot, latLabel);
            linkSignalBars?.SetQuality01(1f - Mathf.Clamp01(ls.mean / latencyGaugeMaxMs));

            if (latencyGauge != null)
            {
                latencyGauge.SetTarget01(1f - Mathf.Clamp01(ls.mean / latencyGaugeMaxMs));
                if (latencyGauge.valueText != null) latencyGauge.valueText.text = $"{ls.mean:F0}";
            }
            if (latencyGraph != null && latencyGraphValue != null)
            {
                // push handled on new packet below; keep header updated even without new packet
                latencyGraphValue.text = $"{ls.mean:F0} ms";
                latencyGraphValue.color = ls.mean >= latencyCriticalMs ? DroneUIFX.AERO_RED : ls.mean >= latencyHighMs ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;
            }
        }
        else
        {
            if (latencyPill != null) latencyPill.SetState(new Color(0.14f,0.17f,0.22f,1f), COL_STALE, "-- ms");
            SetText(latencyText, "-- ms");
            linkSignalBars?.SetQuality01(0f);
        }

        // packet rate (pkts/s) — update once per second window
        rateWindow += Time.deltaTime;
        if (rateWindow >= 1f)
        {
            displayedRate = rateCount / rateWindow;
            rateWindow = 0f; rateCount = 0;
            SetText(packetRateText, $"{displayedRate:F1}");
        }
        SetText(packetLossText, "--");
        // sats top bar + right panel
        if (d0.satellites >= 0)
        {
            string satStr = d0.satellites.ToString();
            SetText(satTopText, satStr + " SAT");
            SetText(gpsSatText, satStr);
            Color sc = d0.satellites < gpsDegradedSats ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;
            if (satTopText != null) satTopText.color = sc;
            if (gpsSatText != null) gpsSatText.color = sc;
        }
        else
        {
            SetText(satTopText, "-- SAT");
            SetText(gpsSatText, "--");
            if (satTopText != null) satTopText.color = COL_STALE;
            if (gpsSatText != null) gpsSatText.color = COL_STALE;
        }

        SetStatus(telemetryStatusText, dataReceiver.isConnected ? "OK" : "LOST",
            dataReceiver.isConnected ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_RED);
        bool gpsFix = d0.satellites >= gpsDegradedSats;
        string gpsState = d0.satellites < 0 ? "WAIT" : gpsFix ? "OK" : "DEGRADED";
        Color gpsStateColor = d0.satellites < 0 ? COL_STALE : gpsFix ? DroneUIFX.AERO_GREEN : DroneUIFX.AERO_AMBER;
        SetStatus(gpsStatusText, gpsState, gpsStateColor);
        SetStatus(imuStatusText, dataReceiver.isConnected ? "OK" : "WAIT",
            dataReceiver.isConnected ? DroneUIFX.AERO_GREEN : COL_STALE);
        SetStatus(barometerStatusText, dataReceiver.isConnected ? "OK" : "WAIT",
            dataReceiver.isConnected ? DroneUIFX.AERO_GREEN : COL_STALE);

        if (!dataReceiver.newDataAvailable) return;
        dataReceiver.newDataAvailable = false;
        packets++; rateCount++;
        SetText(packetCountText, packets.ToString());
        SetText(frameCountText, packets.ToString());

        var d = dataReceiver.latestData;

        // ── Position (LOCAL_POSITION_NED) ───────────────────────────────
        SetText(xText, $"{d.x:F2}");
        SetText(yText, $"{d.y:F2}");
        SetText(zText, $"{d.z:F2}");

        // HUD altitude/speed
        SetText(hudAltText, $"{d.z:F1} m");
        SetText(hudSpeedText, $"{d.speed:F1} m/s");
        if (hudAltText != null) hudAltText.color = d.z < 0.15f ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;

        // ── Attitude ───────────────────────────────────────────────────
        SetText(pitchText, $"{d.pitch:F1}°");
        SetText(rollText,  $"{d.roll:F1}°");
        SetText(yawText,   $"{d.yaw:F1}°");
        string heading = $"{WrapYaw(d.yaw):000}°";
        SetText(hudHeadingText, heading);
        SetText(topHeadingText, heading);
        SetText(centerHeadingText, heading);
        horizon?.SetAttitude(d.pitch, d.roll);
        compassRibbon?.SetHeading(d.yaw);
        mapView?.SetTelemetry(d.x, d.y, d.lat, d.lon, d.yaw);

        // ── GPS ────────────────────────────────────────────────────────
        Color gpsCol = d.satellites >= 0 ? DroneUIFX.AERO_NUM : COL_STALE;
        SetText(latText,    $"{d.lat:F4}");
        SetText(lonText,    $"{d.lon:F4}");
        SetText(gpsAltText, $"{d.gps_alt:F1}");
        SetText(hudLatText, $"{d.lat:F4}");
        SetText(hudLonText, $"{d.lon:F4}");
        SetText(hudGpsAltText, $"{d.gps_alt:F1} m");
        SetTextColor(latText, gpsCol); SetTextColor(lonText, gpsCol); SetTextColor(gpsAltText, gpsCol);

        float gpsQuality = ComputeGpsQuality(d);
        gpsSignalBars?.SetQuality01(gpsQuality);
        if (satelliteRing != null)
        {
            if (d.satellites >= 0)
            {
                satelliteRing.SetTarget01(Mathf.Clamp01(d.satellites / satellitesForFullBars));
                if (satelliteRing.valueText != null) satelliteRing.valueText.text = d.satellites.ToString();
                if (satelliteRing.valueText != null) satelliteRing.valueText.color = d.satellites < gpsDegradedSats ? DroneUIFX.AERO_AMBER : Color.white;
            }
            else
            {
                satelliteRing.SetTarget01(gpsQuality);
                if (satelliteRing.valueText != null) satelliteRing.valueText.text = "--";
            }
        }

        // ── Electrical ─────────────────────────────────────────────────
        SetText(voltageText, $"{d.voltage:F2}");
        SetText(currentText, $"{d.current:F1}");
        if (voltageText != null) voltageText.color = d.voltage > 0.01f && d.voltage < 14.0f ? DroneUIFX.AERO_AMBER : DroneUIFX.AERO_NUM;

        // ── Speed ──────────────────────────────────────────────────────
        SetText(speedText, $"{d.speed:F2}");
        SetText(verticalSpeedText, $"{d.vz:F2}");

        // ── Graph headers update per packet; 5 Hz samples retain 60 seconds. ──
        bool sampleGraphs = graphSampleElapsed >= GraphSamplePeriod;
        if (sampleGraphs) graphSampleElapsed %= GraphSamplePeriod;
        if (altitudeGraph != null)
        {
            if (sampleGraphs) altitudeGraph.Push(d.z);
            if (altitudeGraphValue != null) altitudeGraphValue.text = $"{d.z:F1} m";
        }
        if (speedGraph != null)
        {
            if (sampleGraphs) speedGraph.Push(d.speed);
            if (speedGraphValue != null) speedGraphValue.text = $"{d.speed:F2} m/s";
        }
        if (latencyGraph != null)
        {
            float lat = ls.min < float.MaxValue ? ls.mean : 0f;
            if (sampleGraphs) latencyGraph.Push(lat);
            if (latencyGraphValue != null) latencyGraphValue.text = $"{lat:F0} ms";
        }

        // ── Battery ring + flat bar ────────────────────────────────────────
        int batt = d.battery_remaining;
        SetText(batteryPercentText, $"{batt}%");
        if (batteryRing != null) batteryRing.SetTarget01(batt / 100f);
        if (batteryBar != null) batteryBar.SetValue01(batt / 100f);
        if (batteryGraph != null)
        {
            if (sampleGraphs) batteryGraph.Push(batt);
            if (batteryGraphValue != null) batteryGraphValue.text = $"{batt}%";
        }
        if (batteryPercentText != null)
        {
            if (batt <= batteryCriticalPercent) batteryPercentText.color = DroneUIFX.AERO_RED;
            else if (batt <= batteryLowPercent) batteryPercentText.color = DroneUIFX.AERO_AMBER;
            else batteryPercentText.color = DroneUIFX.AERO_NUM;
        }
        if (batt <= batteryCriticalPercent && !batteryCriticalLandSent)
        {
            Debug.LogWarning($"[UI] Battery critical ({batt}%) → LAND (UI presentation threshold {batteryCriticalPercent}%)");
            dataReceiver.SendCommand("LAND");
            batteryCriticalLandSent = true;
        }
        if (batt > batteryCriticalPercent) batteryCriticalLandSent = false;

        // ── Vibration bars ─────────────────────────────────────────────
        vibXBar?.SetValue(d.vibration_x);
        vibYBar?.SetValue(d.vibration_y);
        vibZBar?.SetValue(d.vibration_z);
        UpdateVibrationField(vibXText, d.vibration_x);
        UpdateVibrationField(vibYText, d.vibration_y);
        UpdateVibrationField(vibZText, d.vibration_z);
        float maxVib = Mathf.Max(Mathf.Abs(d.vibration_x), Mathf.Abs(d.vibration_y), Mathf.Abs(d.vibration_z));
        if (vibStatusText != null)
        {
            if (maxVib >= vibCriticalThreshold) { vibStatusText.text = "CRITICAL"; vibStatusText.color = DroneUIFX.AERO_RED; }
            else if (maxVib >= vibHighThreshold) { vibStatusText.text = "HIGH"; vibStatusText.color = DroneUIFX.AERO_AMBER; }
            else { vibStatusText.text = "OK"; vibStatusText.color = DroneUIFX.AERO_GREEN; }
        }

        // ── Alarm banner — now covers 5 verified conditions (UI presentation) ──
        bool vibAlarm = dataReceiver.vibrationAlarmActive;
        bool battCrit = batt <= batteryCriticalPercent;
        bool battLow  = batt <= batteryLowPercent && !battCrit;
        bool linkHigh = ls.min < float.MaxValue && ls.mean >= latencyCriticalMs;
        bool gpsDegraded = d.satellites >= 0 && d.satellites < gpsDegradedSats;
        bool connLost = !dataReceiver.isConnected && dataReceiver.LastPacketAgeSeconds > delayedThreshold;

        bool anyCritical = vibAlarm || battCrit || connLost;
        bool anyWarn = battLow || linkHigh || gpsDegraded;

        if (alarmBannerBg != null)
        {
            if (anyCritical || anyWarn)
            {
                alarmFlashTimer += Time.deltaTime;
                bool flash = anyCritical ? ((int)(alarmFlashTimer * 4) % 2 == 0) : ((int)(alarmFlashTimer * 2) % 2 == 0);
                Color bgCol = anyCritical ? BG_ALARM : BG_WARN;
                alarmBannerBg.color = flash ? bgCol : new Color(0,0,0,0);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                if (vibAlarm) sb.Append("VIBRATION CRITICAL — AUTO LAND   ");
                if (battCrit) sb.Append("BATTERY CRITICAL — LANDING   ");
                else if (battLow) sb.Append("BATTERY LOW   ");
                if (connLost) sb.Append("LINK LOST   ");
                else if (linkHigh) sb.Append("HIGH LATENCY   ");
                if (gpsDegraded) sb.Append("GPS DEGRADED   ");
                string msg = sb.ToString().Trim();
                if (msg.Length == 0) msg = "CHECK TELEMETRY";
                SetText(alarmBannerText, (anyCritical ? "!!  " : "!  ") + msg);
                if (alarmBannerText != null) alarmBannerText.fontSize = anyCritical ? 13 : 12;
            }
            else
            {
                alarmFlashTimer = 0f;
                alarmBannerBg.color = new Color(0,0,0,0);
                SetText(alarmBannerText, "");
            }
        }
    }

    float WrapYaw(float y) { y %= 360f; if (y < 0) y += 360f; return y; }

    float ComputeGpsQuality(DroneData d)
    {
        if (!dataReceiver.isConnected) return 0f;
        if (d.satellites >= 0) return Mathf.Clamp01(d.satellites / satellitesForFullBars);
        var ls = dataReceiver.latencyStats;
        if (ls.min >= float.MaxValue) return 0.5f;
        float t = Mathf.InverseLerp(gpsPoorLatencyMs, gpsGoodLatencyMs, ls.mean);
        return Mathf.Clamp01(t);
    }

    void UpdateVibrationField(TextMeshProUGUI t, float val)
    {
        if (t == null) return;
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
