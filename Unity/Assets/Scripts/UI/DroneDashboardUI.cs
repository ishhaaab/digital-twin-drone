using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Layout (1920x1080 reference):
//
//  +------------------+------------------------------+------------------+
//  |   LEFT (330px)   |      TOP BAR (64px tall)     |  RIGHT (330px)   |
//  |                  +------------------------------+                  |
//  |  [COMMANDS]      |                              |  [ATTITUDE]      |
//  |  [VIBRATION]     |      CENTER (camera)         |  [GPS]           |
//  |  [LATENCY]       |                              |  [ELECTRICAL]    |
//  |                  |                              |                  |
//  +------------------+------------------------------+------------------+
//  |                 BOTTOM BAR (200px tall)                            |
//  |         X   |   Y   |   Z   |   SPD   |   BATTERY RING             |
//  +--------------------------------------------------------------------+
//
// VISUAL PASS — what's in this build:
//   • Battery card              → large circular radial ring (green→yellow→red as it depletes)
//   • Attitude (pitch/roll/yaw) → big artificial-horizon disk above the numeric rows
//   • GPS row                   → signal bars + satellite ring above the numeric rows
//   • Vibration X/Y/Z rows      → smooth gradient bars (cyan→amber→red)
//   • Top bar                   → compass heading ribbon + latency radial gauge
//   • Command buttons           → glassmorphic style with hover/press glow
//
// Flight Status (armed badge / flight-mode cards) and the four Motor rings have been
// removed by request, freeing up space so the remaining widgets — horizon, GPS, vibration,
// latency, and the bottom-bar numbers/battery ring — could all be sized up considerably.
//
// Everything else (panel structure, SendCommand plumbing, DroneUIUpdater wiring,
// alarm banner, camera view) is untouched.
public class DroneDashboardUI : MonoBehaviour
{
    [Header("Drone Camera View")]
    public RenderTexture droneViewTexture;

    // ── Palette ───────────────────────────────────────────────────────────
    static readonly Color BG        = new Color(0.04f, 0.06f, 0.10f, 1f);
    static readonly Color PANEL     = new Color(0.07f, 0.10f, 0.15f, 1f);
    static readonly Color CARD      = new Color(0.11f, 0.15f, 0.22f, 1f);
    static readonly Color DIVLINE   = new Color(0.25f, 0.35f, 0.50f, 0.3f);

    static readonly Color BLUE      = new Color(0.30f, 0.65f, 1.00f, 1f);
    static readonly Color GREEN     = new Color(0.20f, 0.90f, 0.50f, 1f);
    static readonly Color PURPLE    = new Color(0.65f, 0.50f, 1.00f, 1f);
    static readonly Color LBLUE     = new Color(0.55f, 0.75f, 1.00f, 1f);
    static readonly Color DGRAY     = new Color(0.45f, 0.52f, 0.62f, 1f);

    static readonly Color BTN_LAND   = new Color(0.90f, 0.25f, 0.25f, 1f);
    static readonly Color BTN_MODE   = new Color(0.30f, 0.65f, 1.00f, 1f);
    static readonly Color BTN_DISARM = new Color(0.85f, 0.20f, 0.20f, 1f);

    const float LEFT_W   = 330f;
    const float RIGHT_W  = 330f;
    const float TOP_H    = 64f;
    const float BOTTOM_H = 200f; // taller still — this is where the big glanceable numbers live

    // ── Internal refs ─────────────────────────────────────────────────────
    DroneUIUpdater    ui;
    DroneDataReceiver rx;
    DroneUIFX.UIFXAnimator fx;

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        // Grab or auto-add sibling components so the UI never silently fails
        ui = GetComponent<DroneUIUpdater>();
        if (ui == null)
        {
            Debug.LogWarning("[DashboardUI] DroneUIUpdater not found on this GameObject — adding it automatically. " +
                             "For best results add it manually in the Inspector.");
            ui = gameObject.AddComponent<DroneUIUpdater>();
        }

        rx = GetComponent<DroneDataReceiver>();
        if (rx == null)
        {
            Debug.LogWarning("[DashboardUI] DroneDataReceiver not found on this GameObject — adding it automatically. " +
                             "For best results add it manually in the Inspector.");
            rx = gameObject.AddComponent<DroneDataReceiver>();
        }

        // Also wire DroneUIUpdater → DroneDataReceiver if not already set
        if (ui.dataReceiver == null)
            ui.dataReceiver = rx;

        BuildUI();
    }

    // ─────────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────
        var cgo    = new GameObject("DroneCanvas");
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;

        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        cgo.AddComponent<GraphicRaycaster>();
        var root = canvas.transform;

        // Single animation driver for every glow/ring/bar/ribbon widget on this canvas
        fx = DroneUIFX.GetAnimator(root);

        // Ensure an EventSystem exists
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Debug.Log("[DashboardUI] EventSystem created automatically.");
        }

        // ── Full BG ───────────────────────────────────────────────────────
        MakeImage("BG", root, BG, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── TOP BAR ───────────────────────────────────────────────────────
        var topBar = MakeImage("TopBar", root, PANEL,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -TOP_H), new Vector2(0, 0));
        BuildTopBar(topBar);

        // ── LEFT PANEL ────────────────────────────────────────────────────
        var leftOuter = MakeImage("LeftPanel", root, PANEL,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(0, BOTTOM_H), new Vector2(LEFT_W, -TOP_H));
        BuildLeftPanel(leftOuter);

        // ── RIGHT PANEL ───────────────────────────────────────────────────
        var rightOuter = MakeImage("RightPanel", root, PANEL,
            new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(-RIGHT_W, BOTTOM_H), new Vector2(0, -TOP_H));
        BuildRightPanel(rightOuter);

        // ── CENTER (camera view) ──────────────────────────────────────────
        var center = MakeImage("Center", root, Color.black,
            new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(LEFT_W, BOTTOM_H), new Vector2(-RIGHT_W, -TOP_H));
        BuildCameraView(center);
        BuildAlarmBanner(center);

        // ── BOTTOM BAR ────────────────────────────────────────────────────
        var bottomBar = MakeImage("BottomBar", root, PANEL,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 0), new Vector2(0, BOTTOM_H));
        BuildBottomBar(bottomBar);

        Debug.Log("[DashboardUI] UI built successfully.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Section builders
    // ═════════════════════════════════════════════════════════════════════

    void BuildTopBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding              = new RectOffset(20, 20, 8, 8);
        hl.spacing              = 24;
        hl.childAlignment       = TextAnchor.MiddleLeft;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth  = false;
        hl.childControlHeight     = true;

        ui.connectionText  = TopBarItem(bar, "Waiting...", GREEN);
        TopBarSep(bar);
        ui.uptimeText      = TopBarItem(bar, "00:00:00",   Color.white);
        TopBarSep(bar);
        ui.packetCountText = TopBarItem(bar, "0 pkts",     DGRAY);
        TopBarSep(bar);

        // NEW: glassmorphism compass heading ribbon, centred in the remaining space
        var compassSlot = new GameObject("CompassSlot", typeof(RectTransform));
        compassSlot.transform.SetParent(bar.transform, false);
        var slotLE = compassSlot.AddComponent<LayoutElement>();
        slotLE.flexibleWidth = 1;
        slotLE.preferredHeight = TOP_H - 16;
        var slotHL = compassSlot.AddComponent<HorizontalLayoutGroup>();
        slotHL.childAlignment = TextAnchor.MiddleCenter;
        slotHL.childForceExpandWidth = false;
        var ribbon = DroneUIFX.CreateCompassRibbon(compassSlot.transform, 460f, TOP_H - 14);
        fx.widgets.Add(ribbon);
        ui.compassRibbon = ribbon;

        TopBarSep(bar);

        // NEW: small latency radial gauge (glows red as latency rises)
        var latGaugeSlot = new GameObject("LatencyGaugeSlot", typeof(RectTransform));
        latGaugeSlot.transform.SetParent(bar.transform, false);
        latGaugeSlot.AddComponent<LayoutElement>().preferredWidth = TOP_H - 6;
        var latGauge = DroneUIFX.CreateRadialRing(
            latGaugeSlot.transform, "LatencyGauge", TOP_H - 10, new Color(1,1,1,0.08f), PANEL,
            "", 0, "-- ms", 15);
        latGauge.colorRamp = DroneUIFX.RampBadHigh;
        fx.widgets.Add(latGauge);
        ui.latencyGauge = latGauge;
        ui.latencyText = TopBarItem(bar, "-- ms", BLUE); // keep the plain-text mirror too
    }

    void BuildLeftPanel(GameObject outer)
    {
        // ── Simple VerticalLayoutGroup directly on outer (same pattern as BuildRightPanel) ──
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(10, 10, 10, 10);
        vlg.spacing               = 6;
        vlg.childAlignment        = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlHeight    = true;
        vlg.childControlWidth     = true;

        // ── COMMANDS (now glassmorphic) ──────────────────────────────────
        SectionHeader(outer, "COMMANDS");
        MakeButton(outer, "LAND",         BTN_LAND,   () => SendCmd("LAND"));
        MakeButton(outer, "STABILIZE",    BTN_MODE,   () => SendCmd("STABILIZE"));
        MakeButton(outer, "ALT HOLD",     BTN_MODE,   () => SendCmd("ALT_HOLD"));
        MakeButton(outer, "POS HOLD",     BTN_MODE,   () => SendCmd("POSHOLD"));
        MakeButton(outer, "FORCE DISARM", BTN_DISARM, () => SendCmd("FORCE_DISARM"));

        Spacer(outer, 8);

        // ── VIBRATION — now gradient bars instead of plain numeric rows ────
        SectionHeader(outer, "VIBRATION  (m/s2)");
        var vibX = DroneUIFX.CreateGradientBar(outer.transform, "Vib X");
        var vibY = DroneUIFX.CreateGradientBar(outer.transform, "Vib Y");
        var vibZ = DroneUIFX.CreateGradientBar(outer.transform, "Vib Z");
        vibX.maxAbsValue = vibY.maxAbsValue = vibZ.maxAbsValue = ui.vibGaugeMax;
        fx.widgets.Add(vibX); fx.widgets.Add(vibY); fx.widgets.Add(vibZ);
        ui.vibXBar = vibX; ui.vibYBar = vibY; ui.vibZBar = vibZ;
        ui.vibXText = vibX.valueText; ui.vibYText = vibY.valueText; ui.vibZText = vibZ.valueText;
        ui.vibStatusText = LeftStatRow(outer, "STAT");

        Spacer(outer, 8);

        // ── LATENCY (detailed stats — unchanged; the glanceable gauge lives in the top bar) ──
        SectionHeader(outer, "LATENCY  (ms)");
        ui.latMeanText = LeftStatRow(outer, "\u03BC");   // μ mean
        ui.latMinText  = LeftStatRow(outer, "MIN");
        ui.latMaxText  = LeftStatRow(outer, "MAX");
        ui.latVarText  = LeftStatRow(outer, "\u03C3\u00B2"); // σ² variance

        Spacer(outer, 10);
    }

    void BuildRightPanel(GameObject outer)
    {
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(10, 10, 10, 10);
        vlg.spacing               = 6;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlHeight    = true;
        vlg.childControlWidth     = true;

        // ── ATTITUDE — artificial horizon + numeric rows ────────────────
        SectionHeader(outer, "ATTITUDE");
        var horizonSlot = new GameObject("HorizonSlot", typeof(RectTransform));
        horizonSlot.transform.SetParent(outer.transform, false);
        horizonSlot.AddComponent<LayoutElement>().preferredHeight = 220;
        var horizonHL = horizonSlot.AddComponent<HorizontalLayoutGroup>();
        horizonHL.childAlignment = TextAnchor.MiddleCenter;
        var horizon = DroneUIFX.CreateArtificialHorizon(horizonSlot.transform, 210f);
        fx.widgets.Add(horizon);
        ui.horizon = horizon;

        ui.pitchText = RightStatRow(outer, "\u03B8"); // θ pitch
        ui.rollText  = RightStatRow(outer, "\u03C6"); // φ roll
        ui.yawText   = RightStatRow(outer, "\u03C8"); // ψ yaw

        RightDivider(outer);

        // ── GPS — signal bars + satellite ring + numeric rows ───────────
        SectionHeader(outer, "GPS");
        var gpsRow = new GameObject("GpsSignalRow", typeof(RectTransform));
        gpsRow.transform.SetParent(outer.transform, false);
        gpsRow.AddComponent<LayoutElement>().preferredHeight = 96;
        var gpsHL = gpsRow.AddComponent<HorizontalLayoutGroup>();
        gpsHL.childAlignment = TextAnchor.MiddleLeft;
        gpsHL.spacing = 24;
        gpsHL.padding = new RectOffset(6, 6, 6, 6);

        var signalBars = DroneUIFX.CreateSignalBars(gpsRow.transform, 5, 46f);
        fx.widgets.Add(signalBars);
        ui.gpsSignalBars = signalBars;

        var satRing = DroneUIFX.CreateRadialRing(
            gpsRow.transform, "SatRing", 82f, new Color(1,1,1,0.08f), PANEL,
            "", 0, "--", 22);
        satRing.colorRamp = DroneUIFX.RampGoodHigh;
        fx.widgets.Add(satRing);
        ui.satelliteRing = satRing;

        ui.latText    = RightStatRow(outer, "Lat");
        ui.lonText    = RightStatRow(outer, "Lon");
        ui.gpsAltText = RightStatRow(outer, "Alt");

        RightDivider(outer);

        SectionHeader(outer, "ELECTRICAL");
        ui.voltageText = RightStatRow(outer, "V");
        ui.currentText = RightStatRow(outer, "A");
    }

    void BuildCameraView(GameObject center)
    {
        if (droneViewTexture == null)
        {
            center.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f);
            var lbl = new GameObject("NoTexLabel");
            lbl.transform.SetParent(center.transform, false);
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text      = "Assign Drone View Texture in Inspector";
            t.fontSize  = 22;
            t.color     = DGRAY;
            t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.1f, 0.4f);
            rt.anchorMax = new Vector2(0.9f, 0.6f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return;
        }

        var go  = new GameObject("DroneView");
        go.transform.SetParent(center.transform, false);
        var img = go.AddComponent<RawImage>();
        img.texture = droneViewTexture;
        var rt2 = img.rectTransform;
        rt2.anchorMin = Vector2.zero;
        rt2.anchorMax = Vector2.one;
        rt2.offsetMin = rt2.offsetMax = Vector2.zero;
    }

    void BuildAlarmBanner(GameObject center)
    {
        var go = new GameObject("AlarmBanner");
        go.transform.SetParent(center.transform, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        ui.alarmBannerBg = img;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.88f);
        rt.anchorMax = new Vector2(1, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var txt = new GameObject("AlarmText");
        txt.transform.SetParent(go.transform, false);
        var t = txt.AddComponent<TextMeshProUGUI>();
        t.text      = "";
        t.fontSize  = 28;
        t.fontStyle = FontStyles.Bold;
        t.color     = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        ui.alarmBannerText = t;

        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    void BuildBottomBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(24, 24, 16, 16);
        hl.spacing               = 24;
        hl.childAlignment        = TextAnchor.MiddleCenter;
        hl.childForceExpandHeight= true;
        hl.childForceExpandWidth = false;
        hl.childControlHeight    = true;

        ui.xText     = BottomCard(bar, "X",   BLUE);
        ui.yText     = BottomCard(bar, "Y",   BLUE);
        ui.zText     = BottomCard(bar, "Z",   BLUE);
        ui.speedText = BottomCard(bar, "SPD", PURPLE);

        // NEW: battery radial ring in place of the flat text card — sized up since
        // the motor rings that used to share this bar are gone.
        var battSlot = BottomSlot(bar, "BatteryRing");
        var battRing = DroneUIFX.CreateRadialRing(
            battSlot.transform, "BatteryRing", BOTTOM_H - 24, new Color(1,1,1,0.08f), PANEL,
            "\u26A1", 30, "100%", 32);
        battRing.colorRamp = DroneUIFX.RampGoodHigh;
        fx.widgets.Add(battRing);
        ui.batteryRing = battRing;
        ui.batteryPercentText = battRing.valueText;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Primitive helpers
    // ═════════════════════════════════════════════════════════════════════

    GameObject MakeImage(string name, Transform parent, Color col,
                         Vector2 ancMin, Vector2 ancMax,
                         Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = col;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin;
        rt.anchorMax = ancMax;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
        return go;
    }

    TextMeshProUGUI TopBarItem(GameObject bar, string txt, Color col)
    {
        var go = new GameObject("TopItem");
        go.transform.SetParent(bar.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 170;
        le.flexibleWidth  = 0;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text      = txt;
        t.fontSize  = 24;
        t.color     = col;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
    }

    void TopBarSep(GameObject bar)
    {
        var go = new GameObject("Sep");
        go.transform.SetParent(bar.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 1;
        le.flexibleWidth  = 0;
        go.AddComponent<Image>().color = DIVLINE;
    }

    void SectionHeader(GameObject parent, string title)
    {
        var go = new GameObject("Hdr_" + title);
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 30;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text      = title;
        t.fontSize  = 14;
        t.color     = LBLUE;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.MidlineLeft;
    }

    TextMeshProUGUI LeftStatRow(GameObject parent, string label)
    {
        var row = new GameObject(label + "Row");
        row.transform.SetParent(parent.transform, false);

        row.AddComponent<LayoutElement>().preferredHeight = 40;
        row.AddComponent<Image>().color = CARD;

        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(10, 10, 4, 4);
        hl.spacing               = 6;
        hl.childForceExpandWidth = true;
        hl.childAlignment        = TextAnchor.MiddleLeft;

        // Label — kept short/symbolic on purpose so the value below gets maximum size
        var lbl = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lt = lbl.AddComponent<TextMeshProUGUI>();
        lt.text      = label;
        lt.fontSize  = 16;
        lt.fontStyle = FontStyles.Bold;
        lt.color     = DGRAY;
        lt.alignment = TextAlignmentOptions.MidlineLeft;
        var lle = lbl.AddComponent<LayoutElement>();
        lle.preferredWidth = 56;
        lle.flexibleWidth  = 0;

        // Value — this is the number that needs to be readable at a glance
        var val = new GameObject("Value");
        val.transform.SetParent(row.transform, false);
        var vt = val.AddComponent<TextMeshProUGUI>();
        vt.text      = "--";
        vt.fontSize  = 20;
        vt.color     = Color.white;
        vt.fontStyle = FontStyles.Bold;
        vt.alignment = TextAlignmentOptions.MidlineRight;
        vt.enableAutoSizing = true;
        vt.fontSizeMin = 14;
        vt.fontSizeMax = 20;
        return vt;
    }

    TextMeshProUGUI RightStatRow(GameObject parent, string label)
    {
        var row = new GameObject(label + "Row");
        row.transform.SetParent(parent.transform, false);

        row.AddComponent<LayoutElement>().preferredHeight = 48;
        row.AddComponent<Image>().color = CARD;

        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(12, 12, 6, 6);
        hl.spacing               = 8;
        hl.childForceExpandWidth = true;
        hl.childAlignment        = TextAnchor.MiddleLeft;

        var lbl = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lt = lbl.AddComponent<TextMeshProUGUI>();
        lt.text      = label;
        lt.fontSize  = 19;
        lt.fontStyle = FontStyles.Bold;
        lt.color     = DGRAY;
        lt.alignment = TextAlignmentOptions.MidlineLeft;
        var lle = lbl.AddComponent<LayoutElement>();
        lle.preferredWidth = 52;
        lle.flexibleWidth  = 0;

        var val = new GameObject("Value");
        val.transform.SetParent(row.transform, false);
        var vt = val.AddComponent<TextMeshProUGUI>();
        vt.text      = "--";
        vt.fontSize  = 28;
        vt.color     = Color.white;
        vt.fontStyle = FontStyles.Bold;
        vt.alignment = TextAlignmentOptions.MidlineRight;
        // Auto-size down (never up) so long readouts like GPS lat/lon never clip while
        // short ones (e.g. "23.4°") render at the full, most-readable size.
        vt.enableAutoSizing = true;
        vt.fontSizeMin = 18;
        vt.fontSizeMax = 28;
        return vt;
    }

    void RightDivider(GameObject parent)
    {
        var go = new GameObject("Div");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 10;
    }

    void MakeButton(GameObject parent, string label, Color col,
                    UnityEngine.Events.UnityAction cb)
    {
        var go = new GameObject(label + "Btn");
        go.transform.SetParent(parent.transform, false);

        // ── Stretch the button rect to fill its layout slot ───────────────
        var brt = go.GetComponent<RectTransform>();
        if (brt == null) brt = go.AddComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 52;
        le.flexibleWidth   = 1;   // fill the full width of the content container

        var img = go.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = true; // ensure clicks register

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cs  = btn.colors;
        cs.normalColor      = Color.white; // glass style handles its own colouring via ApplyGlassStyle
        cs.highlightedColor = Color.white;
        cs.pressedColor     = Color.white;
        cs.fadeDuration     = 0.05f;
        btn.colors = cs;
        btn.onClick.AddListener(cb);

        // Button label text — stretched to fill button so it's centered
        var txt = new GameObject("Text");
        txt.transform.SetParent(go.transform, false);
        var t = txt.AddComponent<TextMeshProUGUI>();
        t.text            = label;
        t.fontSize        = 18;
        t.fontStyle       = FontStyles.Bold;
        t.color           = Color.white;
        t.alignment       = TextAlignmentOptions.Center;
        t.raycastTarget   = false; // text should not block button clicks

        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        // NEW: glassmorphic restyle — translucent fill, neon border, hover/press glow.
        // This runs AFTER onClick is wired so the command binding is completely unaffected.
        DroneUIFX.ApplyGlassStyle(go, col);
        txt.transform.SetAsLastSibling(); // keep label above the border/fill layers just added
    }

    TextMeshProUGUI BottomCard(GameObject parent, string label, Color accent,
                                out Image bgOut)
    {
        var go = new GameObject(label + "Card");
        go.transform.SetParent(parent.transform, false);

        var img = go.AddComponent<Image>();
        img.color = CARD;
        bgOut     = img;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 260;
        le.flexibleWidth  = 1;
        le.flexibleHeight = 1;

        var vl = go.AddComponent<VerticalLayoutGroup>();
        vl.padding        = new RectOffset(10, 10, 10, 10);
        vl.spacing        = 4;
        vl.childAlignment = TextAnchor.MiddleCenter;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;
        vl.childControlHeight     = true;

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(go.transform, false);
        lblGO.AddComponent<LayoutElement>().preferredHeight = 24;
        var lt = lblGO.AddComponent<TextMeshProUGUI>();
        lt.text      = label;
        lt.fontSize  = 18;
        lt.fontStyle = FontStyles.Bold;
        lt.color     = DGRAY;
        lt.alignment = TextAlignmentOptions.Center;

        var valGO = new GameObject("Value");
        valGO.transform.SetParent(go.transform, false);
        valGO.AddComponent<LayoutElement>().flexibleHeight = 1;
        var vt = valGO.AddComponent<TextMeshProUGUI>();
        vt.text      = "0";
        vt.fontSize  = 48;
        vt.fontStyle = FontStyles.Bold;
        vt.color     = accent;
        vt.alignment = TextAlignmentOptions.Center;
        // Big by default, but shrinks gracefully for longer numbers (e.g. "-128.45") instead of clipping
        vt.enableAutoSizing = true;
        vt.fontSizeMin = 28;
        vt.fontSizeMax = 48;

        return vt;
    }

    TextMeshProUGUI BottomCard(GameObject parent, string label, Color accent)
    {
        Image dummy;
        return BottomCard(parent, label, accent, out dummy);
    }

    /// A plain, unstyled bottom-bar slot (background-free) sized to match BottomCard,
    /// used as a container for the new radial-gauge widgets.
    GameObject BottomSlot(GameObject parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = 220;
        le.flexibleWidth   = 1;
        le.flexibleHeight  = 1;
        return go;
    }

    void Spacer(GameObject parent, float h)
    {
        var go = new GameObject("Spacer");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = h;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void SendCmd(string cmd)
    {
        if (rx != null)
        {
            rx.SendCommand(cmd);
            Debug.Log("[DashboardUI] Command sent: " + cmd);
        }
        else
        {
            Debug.LogError("[DashboardUI] Cannot send command — DroneDataReceiver is null!");
        }
    }
}