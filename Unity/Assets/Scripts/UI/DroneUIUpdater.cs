using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────────────────────────────────────
// DroneUIUpdater
//   Reads DroneDataReceiver every frame and pushes values into:
//     (a) the original plain-text fields (kept 100% intact — nothing that used
//         to work stops working, and anything else in your project that reads
//         these TMP fields keeps reading real values), and
//     (b) the new DroneUIFX widgets (rings, glow badges, bars, horizon,
//         compass ribbon, signal bars) built by DroneDashboardUI.
//
//   All animation/smoothing/colour-ramping lives inside DroneUIFX — this
//   script's job is strictly "read telemetry, hand off target values".
// ─────────────────────────────────────────────────────────────────────────────
public class DroneUIUpdater : MonoBehaviour
{
    public DroneDataReceiver dataReceiver;

    // ── TOP BAR ──────────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI connectionText;
    [HideInInspector] public TextMeshProUGUI latencyText;       // now shows mean latency
    [HideInInspector] public TextMeshProUGUI uptimeText;
    [HideInInspector] public TextMeshProUGUI packetCountText;

    // ── RIGHT PANEL (plain text — unchanged) ────────────────────────────────
    // (Flight-mode and armed-state text/widgets removed by request — see DroneDashboardUI.)

    [HideInInspector] public TextMeshProUGUI pitchText;
    [HideInInspector] public TextMeshProUGUI rollText;
    [HideInInspector] public TextMeshProUGUI yawText;

    [HideInInspector] public TextMeshProUGUI latText;
    [HideInInspector] public TextMeshProUGUI lonText;
    [HideInInspector] public TextMeshProUGUI gpsAltText;

    [HideInInspector] public TextMeshProUGUI voltageText;
    [HideInInspector] public TextMeshProUGUI currentText;

    // ── BOTTOM CARDS (text refs now point INTO the new FX widgets — see
    //    DroneDashboardUI. Field names are unchanged so nothing that referenced
    //    them elsewhere breaks.) ──────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI xText;
    [HideInInspector] public TextMeshProUGUI yText;
    [HideInInspector] public TextMeshProUGUI zText;
    [HideInInspector] public TextMeshProUGUI speedText;
    [HideInInspector] public TextMeshProUGUI batteryPercentText;   // = battery ring's centre label

    // (Per-motor text fields removed by request — motor rings no longer exist.)

    // ── VIBRATION (text refs now point INTO the gradient bars) ─────────────────
    [HideInInspector] public TextMeshProUGUI vibXText;
    [HideInInspector] public TextMeshProUGUI vibYText;
    [HideInInspector] public TextMeshProUGUI vibZText;
    [HideInInspector] public TextMeshProUGUI vibStatusText;      // OK / HIGH / CRITICAL

    // ── LATENCY STATS ────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI latMeanText;
    [HideInInspector] public TextMeshProUGUI latMinText;
    [HideInInspector] public TextMeshProUGUI latMaxText;
    [HideInInspector] public TextMeshProUGUI latVarText;

    // ── ALARM BANNER ─────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI alarmBannerText;
    [HideInInspector] public UnityEngine.UI.Image alarmBannerBg;

    // ── LEGACY (kept only so any external code referencing this field still
    //    compiles; the battery ring now colours itself automatically via
    //    DroneUIFX, so this is intentionally left unassigned/unused). ──
    [HideInInspector] public UnityEngine.UI.Image batteryCardBg;

    // ═════════════════════════════════════════════════════════════════════════
    // NEW: FX widget references (assigned by DroneDashboardUI after it builds
    // the canvas). Any of these may be null if you strip a section out of the
    // layout — every call below is null-checked.
    // ═════════════════════════════════════════════════════════════════════════
    [HideInInspector] public DroneUIFX.RadialGaugeFX   batteryRing;
    [HideInInspector] public DroneUIFX.RadialGaugeFX   latencyGauge;
    [HideInInspector] public DroneUIFX.RadialGaugeFX   satelliteRing;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibXBar;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibYBar;
    [HideInInspector] public DroneUIFX.GradientBarFX   vibZBar;
    [HideInInspector] public DroneUIFX.CompassRibbonFX compassRibbon;
    [HideInInspector] public DroneUIFX.HorizonFX       horizon;
    [HideInInspector] public DroneUIFX.SignalBarsFX    gpsSignalBars;

    // ─────────────────────────────────────────────────────────────────────────
    // Thresholds
    // ─────────────────────────────────────────────────────────────────────────
    [Header("Battery Thresholds")]
    public int batteryLowPercent      = 30;
    public int batteryCriticalPercent = 15;

    [Header("Vibration Thresholds (m/s²)")]
    public float vibHighThreshold     = 30f;
    public float vibCriticalThreshold = 60f;   // must match DroneDataReceiver
    public float vibGaugeMax          = 80f;   // full-scale value for the gradient bars

    [Header("Latency Gauge")]
    [Tooltip("Mean latency (ms) that maps to a full/red gauge.")]
    public float latencyGaugeMaxMs = 300f;

    [Header("GPS Signal (fallback quality when no satellite count is available)")]
    [Tooltip("Mean latency (ms) at/below which link quality is treated as excellent.")]
    public float gpsGoodLatencyMs = 80f;
    [Tooltip("Mean latency (ms) at/above which link quality is treated as poor.")]
    public float gpsPoorLatencyMs = 400f;
    [Tooltip("Satellite count that maps to a full signal-bar reading, when satellite data IS available.")]
    public float satellitesForFullBars = 12f;

    // ─────────────────────────────────────────────────────────────────────────
    // Colours (unchanged from the original script)
    // ─────────────────────────────────────────────────────────────────────────
    static readonly Color COL_GREEN    = new Color(0.2f,  0.9f,  0.5f);
    static readonly Color COL_YELLOW   = new Color(1.0f,  0.85f, 0.2f);
    static readonly Color COL_ORANGE   = new Color(1.0f,  0.55f, 0.1f);
    static readonly Color COL_RED      = new Color(1.0f,  0.25f, 0.25f);

    static readonly Color BG_ALARM    = new Color(0.8f,  0.1f,  0.1f,  0.85f);

    // ─────────────────────────────────────────────────────────────────────────
    float uptime  = 0f;
    int   packets = 0;
    bool  batteryCriticalLandSent = false;
    float alarmFlashTimer = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        uptime += Time.deltaTime;
        SetText(uptimeText, FormatTime(uptime));

        if (dataReceiver == null) return;

        // ── Connection status ─────────────────────────────────────────────
        SetText(connectionText, dataReceiver.isConnected ? "Connected" : "Waiting...");

        // ── Latency (top bar — show mean) ─────────────────────────────────
        var ls = dataReceiver.latencyStats;
        if (ls.min < float.MaxValue)
        {
            SetText(latencyText,   $"{ls.mean:F0} ms");
            SetText(latMeanText,   $"{ls.mean:F1}");
            SetText(latMinText,    $"{ls.min:F1}");
            SetText(latMaxText,    $"{ls.max:F1}");
            SetText(latVarText,    $"{ls.variance:F1}");

            // NEW: latency gauge — low latency = good (green), high = bad (red)
            if (latencyGauge != null)
            {
                latencyGauge.SetTarget01(1f - Mathf.Clamp01(ls.mean / latencyGaugeMaxMs));
                latencyGauge.valueText.text = $"{ls.mean:F0}";
            }
        }

        if (!dataReceiver.newDataAvailable) return;
        dataReceiver.newDataAvailable = false;
        packets++;

        var d = dataReceiver.latestData;

        // ── Packet counter ────────────────────────────────────────────────
        SetText(packetCountText, packets.ToString());

        // ── Position ─────────────────────────────────────────────────────
        SetText(xText, $"{d.x:F2}");
        SetText(yText, $"{d.y:F2}");
        SetText(zText, $"{-d.z:F2}");     // flip back for display (NED up)

        // ── Attitude ─────────────────────────────────────────────────────
        SetText(pitchText, $"{d.pitch:F1}°");
        SetText(rollText,  $"{d.roll:F1}°");
        SetText(yawText,   $"{d.yaw:F1}°");

        // NEW: artificial horizon + compass ribbon
        horizon?.SetAttitude(d.pitch, d.roll);
        compassRibbon?.SetHeading(d.yaw);

        // ── GPS ───────────────────────────────────────────────────────────
        SetText(latText,    $"{d.lat:F4}");
        SetText(lonText,    $"{d.lon:F4}");
        SetText(gpsAltText, $"{d.gps_alt:F1}");

        // NEW: signal bars + satellite ring
        float gpsQuality = ComputeGpsQuality(d);
        gpsSignalBars?.SetQuality01(gpsQuality);
        if (satelliteRing != null)
        {
            if (d.satellites >= 0)
            {
                satelliteRing.SetTarget01(Mathf.Clamp01(d.satellites / satellitesForFullBars));
                satelliteRing.valueText.text = d.satellites.ToString();
            }
            else
            {
                satelliteRing.SetTarget01(gpsQuality);
                satelliteRing.valueText.text = "--";
            }
        }

        // ── Electrical ───────────────────────────────────────────────────
        SetText(voltageText, $"{d.voltage:F2}V");
        SetText(currentText, $"{d.current:F1}A");

        // ── Speed ─────────────────────────────────────────────────────────
        SetText(speedText, $"{d.speed:F2}");

        // ══════════════════════════════════════════════════════════════════
        // BATTERY — ring gauge (colour + auto-land unchanged)
        // ══════════════════════════════════════════════════════════════════
        int batt = d.battery_remaining;
        SetText(batteryPercentText, $"{batt}%");
        if (batteryRing != null)
        {
            batteryRing.SetTarget01(batt / 100f);
            // NOTE: the ring auto-colours itself green→yellow→red as it depletes
            // (see DroneUIFX.RampGoodHigh), so no manual colour assignment here —
            // that avoids fighting the per-frame Tick() that owns fill.color.
        }

        // Auto-land on critical battery (unchanged logic)
        if (batt <= batteryCriticalPercent && !batteryCriticalLandSent)
        {
            Debug.LogWarning($"[UI] Battery critical ({batt}%) → LAND");
            dataReceiver.SendCommand("LAND");
            batteryCriticalLandSent = true;
        }
        if (batt > batteryCriticalPercent) batteryCriticalLandSent = false;

        // ══════════════════════════════════════════════════════════════════
        // VIBRATION — gradient bars (cyan → amber → red)
        // ══════════════════════════════════════════════════════════════════
        vibXBar?.SetValue(d.vibration_x);
        vibYBar?.SetValue(d.vibration_y);
        vibZBar?.SetValue(d.vibration_z);
        // Keep legacy coloured-text behaviour too, in case vibXText/Y/Z aren't
        // actually the bar's embedded label in some custom layout.
        UpdateVibrationField(vibXText, d.vibration_x);
        UpdateVibrationField(vibYText, d.vibration_y);
        UpdateVibrationField(vibZText, d.vibration_z);

        float maxVib = Mathf.Max(
            Mathf.Abs(d.vibration_x),
            Mathf.Abs(d.vibration_y),
            Mathf.Abs(d.vibration_z)
        );

        if (vibStatusText != null)
        {
            if (maxVib >= vibCriticalThreshold)
            { vibStatusText.text = "CRITICAL"; vibStatusText.color = COL_RED; }
            else if (maxVib >= vibHighThreshold)
            { vibStatusText.text = "HIGH";     vibStatusText.color = COL_ORANGE; }
            else
            { vibStatusText.text = "OK";       vibStatusText.color = COL_GREEN; }
        }

        // ══════════════════════════════════════════════════════════════════
        // ALARM BANNER (unchanged)
        // ══════════════════════════════════════════════════════════════════
        bool alarmOn = dataReceiver.vibrationAlarmActive ||
                       (batt <= batteryCriticalPercent);

        if (alarmBannerBg != null)
        {
            if (alarmOn)
            {
                alarmFlashTimer += Time.deltaTime;
                bool flash = ((int)(alarmFlashTimer * 4)) % 2 == 0;
                alarmBannerBg.color = flash ? BG_ALARM : new Color(0,0,0,0);

                string msg = "";
                if (dataReceiver.vibrationAlarmActive) msg += "⚠ VIBRATION CRITICAL — AUTO LAND  ";
                if (batt <= batteryCriticalPercent)     msg += "⚠ BATTERY CRITICAL — LANDING";
                SetText(alarmBannerText, msg.Trim());
            }
            else
            {
                alarmFlashTimer = 0f;
                alarmBannerBg.color = new Color(0,0,0,0);
                SetText(alarmBannerText, "");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// Produces a 0..1 "how good is the GPS/link right now" estimate.
    /// Prefers a real satellite count when your telemetry provides one; otherwise falls
    /// back to a latency-based proxy (lower latency + connected = better signal bars).
    /// This is clearly an approximation where no satellite count exists — replace with
    /// real HDOP/fix-type data if your MAVLink stream exposes GPS_RAW_INT.
    float ComputeGpsQuality(DroneData d)
    {
        if (!dataReceiver.isConnected) return 0f;

        if (d.satellites >= 0)
            return Mathf.Clamp01(d.satellites / satellitesForFullBars);

        var ls = dataReceiver.latencyStats;
        if (ls.min >= float.MaxValue) return 0.5f; // no samples yet — neutral
        float t = Mathf.InverseLerp(gpsPoorLatencyMs, gpsGoodLatencyMs, ls.mean);
        return Mathf.Clamp01(t);
    }

    // ─────────────────────────────────────────────────────────────────────────
    void UpdateVibrationField(TextMeshProUGUI t, float val)
    {
        if (t == null) return;
        t.text = $"{val:F2}";
        float a = Mathf.Abs(val);
        if      (a >= vibCriticalThreshold) t.color = COL_RED;
        else if (a >= vibHighThreshold)     t.color = COL_ORANGE;
        else                                t.color = COL_GREEN;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void SetText(TextMeshProUGUI t, string v)
    {
        if (t != null) t.text = v;
    }

    string FormatTime(float t)
    {
        int h = (int)(t / 3600);
        int m = (int)((t % 3600) / 60);
        int s = (int)(t % 60);
        return $"{h:00}:{m:00}:{s:00}";
    }
}