using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Professional Aerospace Ground Control Station — overhaul
//
// Target layout (1920x1080):
// ┌──────────────────────────────────────────────────────────────────────┐
// │ TOP BAR (52px):  DRONE  ●CONNECTION  MODE  ARMED  LAT  SATS  TIME PKTS │
// ├──────────────┬──────────────────────────────────┬─────────────────────┤
// │ LEFT 340px   │         CENTER (3D Twin)         │  RIGHT 360px        │
// │ FLIGHT       │                                  │  POWER              │
// │  horizon     │                                  │   battery ring      │
// │  P/R/Y       │                                  │   V/A               │
// │ POSITION     │                                  │  GPS                │
// │  X Y Z SPD   │                                  │   bars + sat + LLAlt│
// │ CONTROLS     │                                  │  LINK               │
// │  5 buttons   │                                  │   latency stats     │
// │ VIBRATION    │                                  │  ATTITUDE (compact) │
// │  bars+STAT   │                                  │                     │
// ├──────────────┴──────────────────────────────────┴─────────────────────┤
// │ BOTTOM (148px):  ALTITUDE GRAPH  │  SPEED GRAPH  │  LATENCY GRAPH    │
// └──────────────────────────────────────────────────────────────────────┘
//
// Style: muted aerospace — graphite/navy/slate, flat borders, high-contrast
// mono numbers, NO neon glow / glassmorphism. All widgets trace to real telemetry.

public class DroneDashboardUI : MonoBehaviour
{
    [Header("Drone Camera View")]
    public RenderTexture droneViewTexture;

    // Palette taken from DroneUIFX.AERO_* (single source of truth there)
    static Color BG => DroneUIFX.AERO_BG;
    static Color PANEL => DroneUIFX.AERO_PANEL;
    static Color CARD => DroneUIFX.AERO_CARD;
    static Color CARD2 => DroneUIFX.AERO_CARD2;
    static Color BORDER => DroneUIFX.AERO_BORDER;
    static Color DIV => DroneUIFX.AERO_DIVIDER;
    static Color TXT => DroneUIFX.AERO_TEXT;
    static Color TXT_SEC => DroneUIFX.AERO_TEXT_SEC;
    static Color TXT_DIM => DroneUIFX.AERO_TEXT_DIM;
    static Color ACCENT => DroneUIFX.AERO_ACCENT;
    static Color AMBER => DroneUIFX.AERO_AMBER;
    static Color RED => DroneUIFX.AERO_RED;

    static readonly Color BTN_LAND_COL = new Color(0.78f, 0.30f, 0.30f, 1f);
    static readonly Color BTN_MODE_COL = new Color(0.22f, 0.55f, 0.62f, 1f);
    static readonly Color BTN_DISARM_COL = new Color(0.62f, 0.28f, 0.28f, 1f);

    const float LEFT_W = 340f;
    const float RIGHT_W = 360f;
    const float TOP_H = 52f;
    const float BOTTOM_H = 148f;

    DroneUIUpdater ui;
    DroneDataReceiver rx;
    DroneUIFX.UIFXAnimator fx;

    void Awake()
    {
        ui = GetComponent<DroneUIUpdater>();
        if (ui == null)
        {
            Debug.LogWarning("[DashboardUI] DroneUIUpdater not found — adding automatically.");
            ui = gameObject.AddComponent<DroneUIUpdater>();
        }
        rx = GetComponent<DroneDataReceiver>();
        if (rx == null)
        {
            Debug.LogWarning("[DashboardUI] DroneDataReceiver not found — adding automatically.");
            rx = gameObject.AddComponent<DroneDataReceiver>();
        }
        if (ui.dataReceiver == null) ui.dataReceiver = rx;
        BuildUI();
    }

    void BuildUI()
    {
        var cgo = new GameObject("DroneCanvas");
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        var root = canvas.transform;
        fx = DroneUIFX.GetAnimator(root);

        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        MakeImage("BG", root, BG, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var topBar = MakeImage("TopBar", root, PANEL,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -TOP_H), new Vector2(0, 0));
        // top bar border bottom
        var topBorder = new GameObject("BorderB", typeof(RectTransform), typeof(Image));
        topBorder.transform.SetParent(topBar.transform, false);
        topBorder.GetComponent<Image>().color = BORDER;
        var tbrt = (RectTransform)topBorder.transform;
        tbrt.anchorMin = new Vector2(0, 0); tbrt.anchorMax = new Vector2(1, 0);
        tbrt.offsetMin = Vector2.zero; tbrt.offsetMax = new Vector2(0, 1);
        BuildTopBar(topBar);

        var leftOuter = MakeImage("LeftPanel", root, PANEL,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(0, BOTTOM_H), new Vector2(LEFT_W, -TOP_H));
        AddPanelBorder(leftOuter, true, false);
        BuildLeftPanel(leftOuter);

        var rightOuter = MakeImage("RightPanel", root, PANEL,
            new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(-RIGHT_W, BOTTOM_H), new Vector2(0, -TOP_H));
        AddPanelBorder(rightOuter, false, true);
        BuildRightPanel(rightOuter);

        var center = MakeImage("Center", root, new Color(0.05f, 0.07f, 0.10f, 1f),
            new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(LEFT_W, BOTTOM_H), new Vector2(-RIGHT_W, -TOP_H));
        BuildCameraView(center);
        BuildAlarmBanner(center);
        BuildCenterHud(center);

        var bottomBar = MakeImage("BottomBar", root, PANEL,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 0), new Vector2(0, BOTTOM_H));
        var botBorder = new GameObject("BorderT", typeof(RectTransform), typeof(Image));
        botBorder.transform.SetParent(bottomBar.transform, false);
        botBorder.GetComponent<Image>().color = BORDER;
        var bbrt = (RectTransform)botBorder.transform;
        bbrt.anchorMin = new Vector2(0, 1); bbrt.anchorMax = new Vector2(1, 1);
        bbrt.offsetMin = new Vector2(0, -1); bbrt.offsetMax = Vector2.zero;
        BuildBottomBar(bottomBar);

        Debug.Log("[DashboardUI] Aerospace GCS layout built.");
    }

    // ─── Top bar ─────────────────────────────────────────────────────────
    void BuildTopBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(14, 14, 6, 6);
        hl.spacing = 10;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = false;
        hl.childControlHeight = true;

        // DRONE badge
        var badge = CreateBadge(bar.transform, "DRONE  TWIN", "GCS 01");
        // connection pill (3-state)
        var connPill = DroneUIFX.CreateAeroStatusPill(bar.transform, "ConnPill", 150, 30);
        fx.widgets.Add(connPill);
        ui.connectionPill = connPill;
        // keep legacy text ref pointing at pill label for backward compat
        ui.connectionText = connPill.label;

        TopBarSep(bar);

        // Mode + Armed — re-introduced (were removed in prior revision)
        ui.flightModeText = TopBarField(bar, "MODE", "UNKNOWN", 148);
        ui.armedText = TopBarField(bar, "ARM", "DISARMED", 120);

        TopBarSep(bar);

        // Link / latency compact
        var latPill = DroneUIFX.CreateAeroStatusPill(bar.transform, "LatPill", 118, 30);
        // reuse as textual field — we drive it manually in Updater; add to fx so Tick runs but it's mainly text
        fx.widgets.Add(latPill);
        ui.latencyPill = latPill;
        ui.latencyText = latPill.label; // legacy alias

        // Sats
        ui.satTopText = TopBarField(bar, "SATS", "--", 88);

        TopBarSep(bar);

        // Compass ribbon — thin aerospace tape, not glassy
        var compassSlot = new GameObject("CompassSlot", typeof(RectTransform));
        compassSlot.transform.SetParent(bar.transform, false);
        var cle = compassSlot.AddComponent<LayoutElement>();
        cle.flexibleWidth = 1;
        cle.preferredHeight = TOP_H - 12;
        var slotHL = compassSlot.AddComponent<HorizontalLayoutGroup>();
        slotHL.childAlignment = TextAnchor.MiddleCenter;
        var ribbon = DroneUIFX.CreateCompassRibbon(compassSlot.transform, 420f, TOP_H - 14);
        // restyle ribbon glass to aero: recolor its background
        var glass = ribbon.tapeContent.parent.parent; // root->Viewport->Tape ; need root
        // find glass image sibling
        var ribbonRoot = compassSlot.transform.GetChild(0);
        foreach (Transform ch in ribbonRoot)
        {
            var img = ch.GetComponent<Image>();
            if (img != null && ch.name == "Glass") img.color = new Color(0.08f, 0.11f, 0.15f, 1f);
        }
        fx.widgets.Add(ribbon);
        ui.compassRibbon = ribbon;

        TopBarSep(bar);

        // Time + packets (right-aligned) — assign both legacy names for compat with renamed SampleScene field
        ui.uptimeText = TopBarField(bar, "T", "00:00:00", 96);
        var pkt = TopBarField(bar, "PKTS", "0", 84);
        ui.packetCountText = pkt;
        ui.frameCountText  = pkt; // alias: SampleScene.unity:744 now serializes as frameCountText
    }

    TextMeshProUGUI TopBarField(GameObject bar, string label, string initial, float width)
    {
        var go = new GameObject(label + "Field", typeof(RectTransform));
        go.transform.SetParent(bar.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 30;
        var vl = go.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.MiddleCenter;
        vl.spacing = 1;
        vl.childForceExpandWidth = true;
        vl.padding = new RectOffset(0, 0, 0, 0);

        var lblGO = new GameObject("Lbl");
        lblGO.transform.SetParent(go.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 9;
        lbl.characterSpacing = 12;
        lbl.fontStyle = FontStyles.Bold;
        lbl.color = TXT_DIM;
        lbl.alignment = TextAlignmentOptions.Center;

        var valGO = new GameObject("Val");
        valGO.transform.SetParent(go.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = initial;
        val.fontSize = 13;
        val.fontStyle = FontStyles.Bold;
        val.color = TXT;
        val.alignment = TextAlignmentOptions.Center;
        return val;
    }

    GameObject CreateBadge(Transform parent, string line1, string line2)
    {
        var go = new GameObject("Badge", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 132; le.preferredHeight = 36;
        var bg = go.AddComponent<Image>();
        bg.sprite = DroneUIFX.RoundedRectSprite; bg.type = Image.Type.Sliced;
        bg.color = new Color(0.11f, 0.15f, 0.20f, 1f);
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(go.transform, false);
        border.transform.SetAsFirstSibling();
        border.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        border.GetComponent<Image>().type = Image.Type.Sliced;
        border.GetComponent<Image>().color = BORDER;
        Stretch((RectTransform)border.transform, 0f);
        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(go.transform, false);
        inner.transform.SetSiblingIndex(1);
        inner.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        inner.GetComponent<Image>().type = Image.Type.Sliced;
        inner.GetComponent<Image>().color = new Color(0.11f, 0.15f, 0.20f, 1f);
        Stretch((RectTransform)inner.transform, 1f);

        var vl = go.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(8, 8, 4, 4);
        vl.spacing = 1;
        vl.childAlignment = TextAnchor.MiddleLeft;

        var t1GO = new GameObject("L1"); t1GO.transform.SetParent(go.transform, false);
        var t1 = t1GO.AddComponent<TextMeshProUGUI>();
        t1.text = line1; t1.fontSize = 11; t1.fontStyle = FontStyles.Bold;
        t1.characterSpacing = 14; t1.color = TXT; t1.alignment = TextAlignmentOptions.MidlineLeft;
        var t2GO = new GameObject("L2"); t2GO.transform.SetParent(go.transform, false);
        var t2 = t2GO.AddComponent<TextMeshProUGUI>();
        t2.text = line2; t2.fontSize = 9; t2.color = TXT_DIM; t2.alignment = TextAlignmentOptions.MidlineLeft;
        return go;
    }

    void TopBarSep(GameObject bar)
    {
        var go = new GameObject("Sep", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(bar.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = 1;
        go.GetComponent<Image>().color = DIV;
    }

    // ─── Left panel ──────────────────────────────────────────────────────
    void BuildLeftPanel(GameObject outer)
    {
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 10;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // FLIGHT — horizon + P/R/Y
        SectionHeader(outer, "FLIGHT  ATTITUDE");
        var horizonSlot = new GameObject("HorizonSlot", typeof(RectTransform));
        horizonSlot.transform.SetParent(outer.transform, false);
        horizonSlot.AddComponent<LayoutElement>().preferredHeight = 170;
        var hl = horizonSlot.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter;
        var horizon = DroneUIFX.CreateArtificialHorizon(horizonSlot.transform, 168f);
        fx.widgets.Add(horizon);
        ui.horizon = horizon;
        ui.pitchText = AeroStatRow(outer, "PITCH", "θ");
        ui.rollText  = AeroStatRow(outer, "ROLL",  "φ");
        ui.yawText   = AeroStatRow(outer, "YAW",   "ψ  HDG");

        AeroDivider(outer, 6);

        // POSITION
        SectionHeader(outer, "POSITION  (LOCAL NED  m)");
        ui.xText = AeroStatRow(outer, "NORTH  X", "N");
        ui.yText = AeroStatRow(outer, "EAST   Y", "E");
        ui.zText = AeroStatRow(outer, "ALT    Z", "U");
        ui.speedText = AeroStatRow(outer, "SPEED", "m/s");
        // small ground-speed vs vertical split later if needed

        AeroDivider(outer, 6);

        // CONTROLS — flat aerospace buttons, no neon
        SectionHeader(outer, "FLIGHT CONTROLS");
        MakeAeroButton(outer, "LAND",         BTN_LAND_COL,   true,  () => SendCmd("LAND"));
        var modeRow = new GameObject("ModeRow", typeof(RectTransform));
        modeRow.transform.SetParent(outer.transform, false);
        modeRow.AddComponent<LayoutElement>().preferredHeight = 42;
        var mrhl = modeRow.AddComponent<HorizontalLayoutGroup>();
        mrhl.spacing = 8; mrhl.childForceExpandWidth = true;
        MakeAeroButton(modeRow, "STABILIZE", BTN_MODE_COL, false, () => SendCmd("STABILIZE"), 12);
        MakeAeroButton(modeRow, "ALT HOLD",  BTN_MODE_COL, false, () => SendCmd("ALT_HOLD"), 12);
        MakeAeroButton(modeRow, "POS HOLD",  BTN_MODE_COL, false, () => SendCmd("POSHOLD"), 12);
        MakeAeroButton(outer, "FORCE DISARM", BTN_DISARM_COL, true, () => SendCmd("FORCE_DISARM"));

        AeroDivider(outer, 6);

        // VIBRATION
        SectionHeader(outer, "VIBRATION  (m/s²)");
        var vibX = DroneUIFX.CreateGradientBar(outer.transform, "VIB X");
        var vibY = DroneUIFX.CreateGradientBar(outer.transform, "VIB Y");
        var vibZ = DroneUIFX.CreateGradientBar(outer.transform, "VIB Z");
        // recolor bars container to aero — find track images and set low alpha
        vibX.maxAbsValue = vibY.maxAbsValue = vibZ.maxAbsValue = ui.vibGaugeMax;
        fx.widgets.Add(vibX); fx.widgets.Add(vibY); fx.widgets.Add(vibZ);
        ui.vibXBar = vibX; ui.vibYBar = vibY; ui.vibZBar = vibZ;
        ui.vibXText = vibX.valueText; ui.vibYText = vibY.valueText; ui.vibZText = vibZ.valueText;
        ui.vibStatusText = AeroStatRow(outer, "STATUS", "—");
    }

    // ─── Right panel ─────────────────────────────────────────────────────
    void BuildRightPanel(GameObject outer)
    {
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 10;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;

        // POWER — battery ring + V/A
        SectionHeader(outer, "POWER");
        var battRow = new GameObject("BattRow", typeof(RectTransform));
        battRow.transform.SetParent(outer.transform, false);
        battRow.AddComponent<LayoutElement>().preferredHeight = 132;
        var brhl = battRow.AddComponent<HorizontalLayoutGroup>();
        brhl.spacing = 12; brhl.childAlignment = TextAnchor.MiddleLeft;
        brhl.padding = new RectOffset(4, 4, 4, 4);

        var battSlot = new GameObject("BattSlot", typeof(RectTransform));
        battSlot.transform.SetParent(battRow.transform, false);
        battSlot.AddComponent<LayoutElement>().preferredWidth = 120;
        var battRing = DroneUIFX.CreateRadialRing(
            battSlot.transform, "BatteryRing", 118f, new Color(1,1,1,0.06f), CARD,
            "◉", 18, "100%", 20);
        battRing.colorRamp = DroneUIFX.RampGoodHigh;
        fx.widgets.Add(battRing);
        ui.batteryRing = battRing;
        ui.batteryPercentText = battRing.valueText;

        var battStats = new GameObject("BattStats", typeof(RectTransform));
        battStats.transform.SetParent(battRow.transform, false);
        battStats.AddComponent<LayoutElement>().flexibleWidth = 1;
        var bsvl = battStats.AddComponent<VerticalLayoutGroup>();
        bsvl.spacing = 6; bsvl.childForceExpandWidth = true;
        ui.voltageText = AeroStatRow(battStats, "VOLTAGE", "V");
        ui.currentText = AeroStatRow(battStats, "CURRENT", "A");
        // low/critical thresholds shown subtly in value colour via Updater

        AeroDivider(outer, 6);

        // GPS — signal bars + sat ring + LLAlt
        SectionHeader(outer, "NAVIGATION  /  GPS");
        var gpsRow = new GameObject("GpsVisualRow", typeof(RectTransform));
        gpsRow.transform.SetParent(outer.transform, false);
        gpsRow.AddComponent<LayoutElement>().preferredHeight = 88;
        var gpsHL = gpsRow.AddComponent<HorizontalLayoutGroup>();
        gpsHL.spacing = 16; gpsHL.padding = new RectOffset(6, 6, 6, 6);
        gpsHL.childAlignment = TextAnchor.MiddleLeft;
        var signalBars = DroneUIFX.CreateSignalBars(gpsRow.transform, 5, 42f);
        fx.widgets.Add(signalBars);
        ui.gpsSignalBars = signalBars;
        var satRing = DroneUIFX.CreateRadialRing(
            gpsRow.transform, "SatRing", 78f, new Color(1,1,1,0.06f), CARD,
            "", 0, "--", 20);
        satRing.colorRamp = DroneUIFX.RampGoodHigh;
        fx.widgets.Add(satRing);
        ui.satelliteRing = satRing;

        ui.latText = AeroStatRow(outer, "LAT", "°");
        ui.lonText = AeroStatRow(outer, "LON", "°");
        ui.gpsAltText = AeroStatRow(outer, "GPS ALT", "m MSL");

        AeroDivider(outer, 6);

        // LINK — latency stats + packet info
        SectionHeader(outer, "LINK  /  TELEMETRY");
        // latency gauge (small) kept for glanceable, plus numeric rows
        var linkTop = new GameObject("LinkTop", typeof(RectTransform));
        linkTop.transform.SetParent(outer.transform, false);
        linkTop.AddComponent<LayoutElement>().preferredHeight = 62;
        var lthl = linkTop.AddComponent<HorizontalLayoutGroup>();
        lthl.spacing = 12; lthl.childAlignment = TextAnchor.MiddleCenter;
        var latGaugeSlot = new GameObject("LatGaugeSlot", typeof(RectTransform));
        latGaugeSlot.transform.SetParent(linkTop.transform, false);
        latGaugeSlot.AddComponent<LayoutElement>().preferredWidth = 62;
        var latGauge = DroneUIFX.CreateRadialRing(
            latGaugeSlot.transform, "LatencyGauge", 58f, new Color(1,1,1,0.06f), CARD,
            "", 0, "--", 13);
        latGauge.colorRamp = DroneUIFX.RampBadHigh;
        fx.widgets.Add(latGauge);
        ui.latencyGauge = latGauge;
        // mean latency text lives inside ring; keep external row too
        var latInfo = new GameObject("LatInfo", typeof(RectTransform));
        latInfo.transform.SetParent(linkTop.transform, false);
        latInfo.AddComponent<LayoutElement>().flexibleWidth = 1;
        var livl = latInfo.AddComponent<VerticalLayoutGroup>();
        livl.spacing = 2;
        ui.latMeanText = AeroMiniRow(latInfo, "MEAN");
        ui.latVarText  = AeroMiniRow(latInfo, "JITTER σ²");

        ui.latMinText = AeroStatRow(outer, "MIN", "ms");
        ui.latMaxText = AeroStatRow(outer, "MAX", "ms");
        ui.packetRateText = AeroStatRow(outer, "RATE", "pkts/s");
    }

    // ─── Center ──────────────────────────────────────────────────────────
    void BuildCameraView(GameObject center)
    {
        // subtle inner border
        var inner = new GameObject("InnerBorder", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(center.transform, false);
        var ibImg = inner.GetComponent<Image>();
        ibImg.sprite = DroneUIFX.RoundedRectSprite; ibImg.type = Image.Type.Sliced;
        ibImg.color = new Color(0,0,0,0);
        var ibRt = (RectTransform)inner.transform;
        ibRt.anchorMin = Vector2.zero; ibRt.anchorMax = Vector2.one;
        ibRt.offsetMin = new Vector2(1,1); ibRt.offsetMax = new Vector2(-1,-1);
        // thin outline via parent image already; this is just inset

        if (droneViewTexture == null)
        {
            center.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f);
            var lbl = new GameObject("NoTexLabel");
            lbl.transform.SetParent(center.transform, false);
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text = "Assign Drone View Texture in Inspector — or camera view will appear here when playing SampleScene.";
            t.fontSize = 15; t.color = TXT_DIM; t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.08f, 0.42f); rt.anchorMax = new Vector2(0.92f, 0.58f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return;
        }
        var go = new GameObject("DroneView");
        go.transform.SetParent(center.transform, false);
        var img = go.AddComponent<RawImage>();
        img.texture = droneViewTexture;
        var rt2 = img.rectTransform;
        rt2.anchorMin = Vector2.zero; rt2.anchorMax = Vector2.one;
        rt2.offsetMin = new Vector2(1,1); rt2.offsetMax = new Vector2(-1,-1);
    }

    void BuildCenterHud(GameObject center)
    {
        // Bottom HUD strip over camera: heading + altitude/speed glanceable
        var hud = new GameObject("HudStrip", typeof(RectTransform), typeof(Image));
        hud.transform.SetParent(center.transform, false);
        var hudImg = hud.GetComponent<Image>();
        hudImg.sprite = DroneUIFX.RoundedRectSprite; hudImg.type = Image.Type.Sliced;
        hudImg.color = new Color(0.07f, 0.10f, 0.14f, 0.88f);
        var hrt = (RectTransform)hud.transform;
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0);
        hrt.offsetMin = new Vector2(8, 8); hrt.offsetMax = new Vector2(-8, 8 + 38);
        // border top
        var hBorder = new GameObject("BorderT", typeof(RectTransform), typeof(Image));
        hBorder.transform.SetParent(hud.transform, false);
        hBorder.GetComponent<Image>().color = BORDER;
        var hbrt = (RectTransform)hBorder.transform;
        hbrt.anchorMin = new Vector2(0,1); hbrt.anchorMax = new Vector2(1,1);
        hbrt.offsetMin = new Vector2(0,-1); hbrt.offsetMax = Vector2.zero;

        var hl = hud.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(12, 12, 6, 6);
        hl.spacing = 18; hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;

        // heading
        ui.hudHeadingText = HudField(hud, "HDG", "---°", 110);
        HudSep(hud);
        ui.hudAltText = HudField(hud, "ALT", "-- m", 110);
        HudSep(hud);
        ui.hudSpeedText = HudField(hud, "SPD", "-- m/s", 110);
        HudSep(hud);
        ui.hudModeText = HudField(hud, "MODE", "UNKNOWN", 140);
        // spacer
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(hud.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
        ui.hudArmedText = HudField(hud, "ARM", "DISARMED", 110);
    }

    TextMeshProUGUI HudField(GameObject hud, string label, string initial, float width)
    {
        var go = new GameObject(label + "Hud", typeof(RectTransform));
        go.transform.SetParent(hud.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = width;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6; hl.childAlignment = TextAnchor.MiddleLeft;
        var lgo = new GameObject("Lbl"); lgo.transform.SetParent(go.transform, false);
        var l = lgo.AddComponent<TextMeshProUGUI>();
        l.text = label; l.fontSize = 10; l.characterSpacing = 10; l.fontStyle = FontStyles.Bold; l.color = TXT_DIM;
        var vgo = new GameObject("Val"); vgo.transform.SetParent(go.transform, false);
        var v = vgo.AddComponent<TextMeshProUGUI>();
        v.text = initial; v.fontSize = 13; v.fontStyle = FontStyles.Bold; v.color = TXT;
        return v;
    }
    void HudSep(GameObject hud)
    {
        var go = new GameObject("Sep", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(hud.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = 1;
        go.GetComponent<Image>().color = DIV;
    }

    void BuildAlarmBanner(GameObject center)
    {
        var go = new GameObject("AlarmBanner", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(center.transform, false);
        var img = go.GetComponent<Image>();
        img.sprite = DroneUIFX.RoundedRectSprite; img.type = Image.Type.Sliced;
        img.color = new Color(0,0,0,0);
        ui.alarmBannerBg = img;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 0.88f); rt.anchorMax = new Vector2(1, 1f);
        rt.offsetMin = new Vector2(8, -2); rt.offsetMax = new Vector2(-8, -8);
        var txt = new GameObject("AlarmText", typeof(RectTransform));
        txt.transform.SetParent(go.transform, false);
        var t = txt.AddComponent<TextMeshProUGUI>();
        t.text = ""; t.fontSize = 14; t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 6; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        ui.alarmBannerText = t;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    void BuildBottomBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 8, 8);
        hl.spacing = 10;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = true;

        // Altitude graph: range auto-scaled around current altitude but clamped at 0..maybe 50m visible
        TextMeshProUGUI v1, t1;
        var g1 = DroneUIFX.CreateGraphCard(bar.transform, "ALTITUDE", "m", new Color(0.22f,0.64f,0.82f,1f), out v1, out t1);
        g1.autoScale = true;
        g1.thickness = 1.9f;
        fx.widgets.Add(g1);
        ui.altitudeGraph = g1; ui.altitudeGraphValue = v1;

        TextMeshProUGUI v2, t2;
        var g2 = DroneUIFX.CreateGraphCard(bar.transform, "GROUND SPEED", "m/s", new Color(0.52f,0.62f,0.85f,1f), out v2, out t2);
        g2.autoScale = true;
        g2.manualMin = 0; g2.manualMax = 5; // fallback if autoScale off, but keep auto
        fx.widgets.Add(g2);
        ui.speedGraph = g2; ui.speedGraphValue = v2;

        TextMeshProUGUI v3, t3;
        var g3 = DroneUIFX.CreateGraphCard(bar.transform, "LINK LATENCY", "ms", new Color(0.86f,0.62f,0.22f,1f), out v3, out t3);
        g3.autoScale = false;
        g3.manualMin = 0; g3.manualMax = 320;
        fx.widgets.Add(g3);
        ui.latencyGraph = g3; ui.latencyGraphValue = v3;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────
    GameObject MakeImage(string name, Transform parent, Color col, Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = col;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return go;
    }

    void AddPanelBorder(GameObject panel, bool right, bool left)
    {
        // thin separator lines
        if (right)
        {
            var b = new GameObject("BorderR", typeof(RectTransform), typeof(Image));
            b.transform.SetParent(panel.transform, false);
            b.GetComponent<Image>().color = BORDER;
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(1,0); rt.anchorMax = new Vector2(1,1);
            rt.offsetMin = new Vector2(-1,0); rt.offsetMax = Vector2.zero;
        }
        if (left)
        {
            var b = new GameObject("BorderL", typeof(RectTransform), typeof(Image));
            b.transform.SetParent(panel.transform, false);
            b.GetComponent<Image>().color = BORDER;
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0,0); rt.anchorMax = new Vector2(0,1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(1,0);
        }
    }

    void SectionHeader(GameObject parent, string title)
    {
        var go = new GameObject("Hdr_" + title, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 20;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft; hl.spacing = 8;
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(go.transform, false);
        line.AddComponent<LayoutElement>().preferredWidth = 3;
        line.GetComponent<Image>().color = ACCENT;
        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = title; txt.fontSize = 10; txt.characterSpacing = 14;
        txt.fontStyle = FontStyles.Bold; txt.color = TXT_DIM;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
    }

    TextMeshProUGUI AeroStatRow(GameObject parent, string label, string unit)
    {
        var row = new GameObject(label + "Row", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().preferredHeight = 32;
        var bg = row.GetComponent<Image>();
        bg.sprite = DroneUIFX.RoundedRectSprite; bg.type = Image.Type.Sliced;
        bg.color = CARD;
        // border
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(row.transform, false);
        border.transform.SetAsFirstSibling();
        border.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        border.GetComponent<Image>().type = Image.Type.Sliced;
        border.GetComponent<Image>().color = BORDER;
        Stretch((RectTransform)border.transform, 0f);
        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(row.transform, false);
        inner.transform.SetSiblingIndex(1);
        inner.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        inner.GetComponent<Image>().type = Image.Type.Sliced;
        inner.GetComponent<Image>().color = CARD;
        Stretch((RectTransform)inner.transform, 1f);

        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 4, 4);
        hl.spacing = 8; hl.childForceExpandWidth = true;
        hl.childAlignment = TextAnchor.MiddleLeft;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(row.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label; lbl.fontSize = 10; lbl.characterSpacing = 8;
        lbl.fontStyle = FontStyles.Bold; lbl.color = TXT_DIM;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        var lle = lblGO.AddComponent<LayoutElement>(); lle.preferredWidth = 110; lle.flexibleWidth = 0;

        var valGO = new GameObject("Value", typeof(RectTransform));
        valGO.transform.SetParent(row.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--"; val.fontSize = 13; val.fontStyle = FontStyles.Bold;
        val.color = DroneUIFX.AERO_NUM; val.alignment = TextAlignmentOptions.MidlineRight;
        val.fontSizeMin = 11; val.fontSizeMax = 13; val.enableAutoSizing = true;

        // unit suffix is baked into value text by Updater (e.g. "1.23V"), so no separate unit object needed
        return val;
    }

    TextMeshProUGUI AeroMiniRow(GameObject parent, string label)
    {
        var row = new GameObject(label + "Mini", typeof(RectTransform));
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().preferredHeight = 18;
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8; hl.childForceExpandWidth = true;
        var lblGO = new GameObject("Lbl"); lblGO.transform.SetParent(row.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label; lbl.fontSize = 9; lbl.characterSpacing = 8; lbl.fontStyle = FontStyles.Bold; lbl.color = TXT_DIM;
        var lle = lblGO.AddComponent<LayoutElement>(); lle.preferredWidth = 56; lle.flexibleWidth = 0;
        var valGO = new GameObject("Val"); valGO.transform.SetParent(row.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--"; val.fontSize = 12; val.fontStyle = FontStyles.Bold; val.color = TXT;
        val.alignment = TextAlignmentOptions.MidlineRight;
        return val;
    }

    void AeroDivider(GameObject parent, float h)
    {
        var go = new GameObject("Div", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = h;
        go.GetComponent<Image>().color = new Color(0,0,0,0);
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(go.transform, false);
        line.GetComponent<Image>().color = DIV;
        var rt = (RectTransform)line.transform;
        rt.anchorMin = new Vector2(0,0.5f); rt.anchorMax = new Vector2(1,0.5f);
        rt.offsetMin = new Vector2(0,-0.5f); rt.offsetMax = new Vector2(0,0.5f);
    }

    void MakeAeroButton(GameObject parent, string label, Color accent, bool destructive, UnityEngine.Events.UnityAction cb, float fontSize = 13)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 42; le.flexibleWidth = 1;
        var img = go.GetComponent<Image>();
        img.sprite = DroneUIFX.RoundedRectSprite; img.type = Image.Type.Sliced;
        img.color = destructive ? new Color(0.18f,0.13f,0.14f,1f) : new Color(0.13f,0.17f,0.22f,1f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var cs = btn.colors;
        cs.normalColor = Color.white; cs.highlightedColor = Color.white; cs.pressedColor = Color.white; cs.fadeDuration = 0.05f;
        btn.colors = cs;
        btn.onClick.AddListener(cb);

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var t = txtGO.AddComponent<TextMeshProUGUI>();
        t.text = label; t.fontSize = fontSize; t.characterSpacing = 6;
        t.fontStyle = FontStyles.Bold; t.color = destructive ? new Color(0.92f,0.55f,0.55f,1f) : TXT;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;

        DroneUIFX.ApplyAeroButtonStyle(go, accent, destructive);
        txtGO.transform.SetAsLastSibling();
    }

    void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset,inset); rt.offsetMax = new Vector2(-inset,-inset);
    }
    void Stretch(RectTransform rt) => Stretch(rt, 0f);

    void SendCmd(string cmd)
    {
        if (rx != null) { rx.SendCommand(cmd); Debug.Log("[DashboardUI] CMD: " + cmd); }
        else Debug.LogError("[DashboardUI] No receiver for CMD " + cmd);
    }
}
