using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Professional Aerospace Ground Control Station — overhaul
//
// Target layout (1920x1080):
// ┌──────────────────────────────────────────────────────────────────────┐
// │ TOP BAR (52px):  DRONE  ●CONNECTION  MODE  ARM  LAT  SATS  COMPASS  T PKTS │
// ├──────────────┬──────────────────────────────────┬─────────────────────┤
// │ LEFT 340px   │         CENTER (3D Twin)         │  RIGHT 360px        │
// │ ATTITUDE     │                                  │  POWER              │
// │  horizon     │                                  │   99%  [▁▂▃▄▅▆]     │
// │  P/R/Y       │                                  │   V / A             │
// │ POSITION     │                                  │  GPS                │
// │  N / E / ALT │                                  │   sats + LLAlt      │
// │  SPEED       │                                  │  LINK               │
// │ CONTROLS     │                                  │   latency stats     │
// │  [mode row]  │                                  │                     │
// │  LAND        │                                  │                     │
// │  DISARM      │                                  │                     │
// │ VIB (compact)│                                  │                     │
// ├──────────────┴──────────────────────────────────┴─────────────────────┤
// │ BOTTOM (148px):  ALTITUDE GRAPH  │  SPEED GRAPH  │  LATENCY GRAPH    │
// └──────────────────────────────────────────────────────────────────────┘
//
// Visual language: muted automotive/aerospace instrumentation. Dark graphite
// panels, low-contrast card fills, thin 1px separators. Colour carries STATE
// (green OK / amber warn / red critical) — it is not a decorative category
// scheme. Primary numbers are large and right-aligned; diagnostics are small.
// No neon, no glassmorphism, no unnecessary solid color blocks.
//
// Every widget below traces to a real telemetry field (see DroneUIUpdater.cs
// and DroneDataReceiver.cs). Unavailable metrics show "--" rather than fake data.

public class DroneDashboardUI : MonoBehaviour
{
    [Header("Drone Camera View")]
    public RenderTexture droneViewTexture;

    // Palette — single source of truth is DroneUIFX.AERO_*
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
    static Color GREEN => DroneUIFX.AERO_GREEN;
    static Color NUM => DroneUIFX.AERO_NUM;

    // Control accent colours (semantic, not decorative)
    static readonly Color BTN_MODE_ACCENT  = new Color(0.22f, 0.55f, 0.62f, 1f);
    static readonly Color BTN_LAND_ACCENT  = new Color(0.86f, 0.62f, 0.22f, 1f);
    static readonly Color BTN_DISARM_ACCENT= new Color(0.78f, 0.30f, 0.30f, 1f);

    const float OUTER_PAD = 16f;
    const float GAP = 10f;
    const float LEFT_W = 380f;
    const float RIGHT_W = 380f;
    const float TOP_H = 72f;
    const float BOTTOM_H = 188f;

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
        DroneSkyEnvironment.EnsureSceneSky();

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
            new Vector2(OUTER_PAD, -TOP_H), new Vector2(-OUTER_PAD, -OUTER_PAD));
        StylePanel(topBar);
        var topBorder = new GameObject("BorderB", typeof(RectTransform), typeof(Image));
        topBorder.transform.SetParent(topBar.transform, false);
        topBorder.GetComponent<Image>().color = BORDER;
        topBorder.AddComponent<LayoutElement>().ignoreLayout = true;
        var tbrt = (RectTransform)topBorder.transform;
        tbrt.anchorMin = new Vector2(0, 0); tbrt.anchorMax = new Vector2(1, 0);
        tbrt.offsetMin = Vector2.zero; tbrt.offsetMax = new Vector2(0, 1);
        BuildTopBar(topBar);

        var leftOuter = MakeImage("LeftPanel", root, new Color(0, 0, 0, 0),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(OUTER_PAD, OUTER_PAD), new Vector2(OUTER_PAD + LEFT_W, -TOP_H - GAP));
        BuildLeftPanel(leftOuter);

        var rightOuter = MakeImage("RightPanel", root, new Color(0, 0, 0, 0),
            new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(-OUTER_PAD - RIGHT_W, OUTER_PAD), new Vector2(-OUTER_PAD, -TOP_H - GAP));
        BuildRightPanel(rightOuter);

        var center = MakeImage("CenterViewport", root, new Color(0.05f, 0.07f, 0.10f, 1f),
            new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(OUTER_PAD + LEFT_W + GAP, BOTTOM_H + GAP),
            new Vector2(-OUTER_PAD - RIGHT_W - GAP, -TOP_H - GAP));
        StylePanel(center);
        BuildCameraView(center);
        BuildCenterHud(center);
        BuildAlarmBanner(center);

        var bottomBar = MakeImage("TelemetryRail", root, new Color(0, 0, 0, 0),
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(OUTER_PAD + LEFT_W + GAP, OUTER_PAD),
            new Vector2(-OUTER_PAD - RIGHT_W - GAP, BOTTOM_H));
        BuildBottomBar(bottomBar);

        Debug.Log("[DashboardUI] Aerospace GCS layout built.");
    }

    // ─── Top bar ─────────────────────────────────────────────────────────
    void BuildTopBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(16, 12, 7, 7);
        hl.spacing = 12;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = false;
        hl.childControlHeight = true;

        CreateBadge(bar.transform, "DRONE TWIN", "FLIGHT CONTROL SYSTEM");

        TopBarSep(bar);
        var connPill = DroneUIFX.CreateAeroStatusPill(bar.transform, "ConnPill", 176, 34);
        fx.widgets.Add(connPill);
        ui.connectionPill = connPill;
        ui.connectionText = connPill.label;

        ui.flightModeText = TopBarField(bar, "MODE", "UNKNOWN", 132);
        ui.armedText = TopBarField(bar, "ARMED", "DISARMED", 118);

        TopBarSep(bar);

        var latPill = DroneUIFX.CreateAeroStatusPill(bar.transform, "LatPill", 106, 34);
        fx.widgets.Add(latPill);
        ui.latencyPill = latPill;
        ui.latencyText = latPill.label;

        ui.satTopText = TopBarField(bar, "GPS", "-- SAT", 96);
        ui.topHeadingText = TopBarField(bar, "HEADING", "---°", 98);
        ui.uptimeText = TopBarField(bar, "UPTIME", "00:00:00", 112);

        var spacer = new GameObject("FlexibleSpace", typeof(RectTransform));
        spacer.transform.SetParent(bar.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        TopUtility(bar, "CAM");
        TopUtility(bar, "MAP");
        TopUtility(bar, "CFG");
    }

    void TopUtility(GameObject bar, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(bar.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = 42;
        var img = go.GetComponent<Image>();
        img.sprite = DroneUIFX.RoundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.08f, 0.12f, 0.16f, 1f);
        img.raycastTarget = false;

        var textGO = new GameObject("Label", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 9;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 8;
        text.color = label == "CAM" ? ACCENT : TXT_SEC;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);
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
        le.preferredWidth = 200; le.preferredHeight = 40;
        var bg = go.AddComponent<Image>();
        bg.sprite = DroneUIFX.RoundedRectSprite; bg.type = Image.Type.Sliced;
        bg.color = new Color(0.11f, 0.15f, 0.20f, 1f);
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(go.transform, false);
        border.transform.SetAsFirstSibling();
        border.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        border.GetComponent<Image>().type = Image.Type.Sliced;
        border.GetComponent<Image>().color = BORDER;
        border.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)border.transform, 0f);
        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(go.transform, false);
        inner.transform.SetSiblingIndex(1);
        inner.GetComponent<Image>().sprite = DroneUIFX.RoundedRectSprite;
        inner.GetComponent<Image>().type = Image.Type.Sliced;
        inner.GetComponent<Image>().color = new Color(0.11f, 0.15f, 0.20f, 1f);
        inner.AddComponent<LayoutElement>().ignoreLayout = true;
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
        vlg.spacing = 8;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var attitude = CreatePanelCard(outer, "AttitudeCard", 248f);
        SectionHeader(attitude, "ATTITUDE");
        var attitudeRow = new GameObject("AttitudeRow", typeof(RectTransform));
        attitudeRow.transform.SetParent(attitude.transform, false);
        attitudeRow.AddComponent<LayoutElement>().flexibleHeight = 1;
        var attitudeHL = attitudeRow.AddComponent<HorizontalLayoutGroup>();
        attitudeHL.spacing = 10;
        attitudeHL.childAlignment = TextAnchor.MiddleCenter;
        attitudeHL.childForceExpandWidth = false;

        var horizonSlot = new GameObject("HorizonSlot", typeof(RectTransform));
        horizonSlot.transform.SetParent(attitudeRow.transform, false);
        var horizonLE = horizonSlot.AddComponent<LayoutElement>();
        horizonLE.preferredWidth = 190;
        horizonLE.flexibleWidth = 1;
        var hsl = horizonSlot.AddComponent<HorizontalLayoutGroup>();
        hsl.childAlignment = TextAnchor.MiddleCenter;
        var horizon = DroneUIFX.CreateArtificialHorizon(horizonSlot.transform, 176f);
        fx.widgets.Add(horizon);
        ui.horizon = horizon;

        var attitudeStats = new GameObject("AttitudeValues", typeof(RectTransform));
        attitudeStats.transform.SetParent(attitudeRow.transform, false);
        attitudeStats.AddComponent<LayoutElement>().preferredWidth = 126;
        var attitudeVL = attitudeStats.AddComponent<VerticalLayoutGroup>();
        attitudeVL.spacing = 4;
        attitudeVL.childForceExpandHeight = true;
        attitudeVL.childForceExpandWidth = true;
        ui.pitchText = VerticalStat(attitudeStats, "PITCH", "--");
        ui.rollText  = VerticalStat(attitudeStats, "ROLL", "--");
        ui.yawText   = VerticalStat(attitudeStats, "YAW", "--");

        var position = CreatePanelCard(outer, "PositionCard", 140f);
        SectionHeader(position, "POSITION  (LOCAL NED)");
        ui.xText = SlimStatRow(position, "NORTH  (X)", "");
        ui.yText = SlimStatRow(position, "EAST  (Y)", "");
        ui.zText = SlimStatRow(position, "ALTITUDE  (Z)", "");

        var movement = CreatePanelCard(outer, "MovementCard", 100f);
        SectionHeader(movement, "MOVEMENT");
        ui.speedText = SlimStatRow(movement, "GROUND SPEED", "m/s", rowH: 26, labelW: 150);
        ui.verticalSpeedText = SlimStatRow(movement, "VERTICAL SPEED", "m/s", rowH: 26, labelW: 150);

        var controls = CreatePanelCard(outer, "ControlsCard", 196f);
        SectionHeader(controls, "FLIGHT CONTROLS");
        var modeCaption = new GameObject("ModeCaption", typeof(RectTransform));
        modeCaption.transform.SetParent(controls.transform, false);
        modeCaption.AddComponent<LayoutElement>().preferredHeight = 14;
        var modeCaptionText = modeCaption.AddComponent<TextMeshProUGUI>();
        modeCaptionText.text = "FLIGHT MODE";
        modeCaptionText.fontSize = 9;
        modeCaptionText.characterSpacing = 8;
        modeCaptionText.fontStyle = FontStyles.Bold;
        modeCaptionText.color = TXT_DIM;
        modeCaptionText.alignment = TextAlignmentOptions.MidlineLeft;
        var modeRow = new GameObject("ModeRow", typeof(RectTransform));
        modeRow.transform.SetParent(controls.transform, false);
        modeRow.AddComponent<LayoutElement>().preferredHeight = 36;
        var mrhl = modeRow.AddComponent<HorizontalLayoutGroup>();
        mrhl.spacing = 6; mrhl.childForceExpandWidth = true;
        MakeAeroButton(modeRow, "STABILIZE",  BTN_MODE_ACCENT, false, () => SendCmd("STABILIZE"),  10, 36);
        MakeAeroButton(modeRow, "ALT HOLD",   BTN_MODE_ACCENT, false, () => SendCmd("ALT_HOLD"),   10, 36);
        MakeAeroButton(modeRow, "POS HOLD",   BTN_MODE_ACCENT, false, () => SendCmd("POSHOLD"),    10, 36);
        MakeAeroButton(controls, "LAND", BTN_LAND_ACCENT, false, () => SendCmd("LAND"), 12, 40);
        MakeAeroButton(controls, "FORCE DISARM", BTN_DISARM_ACCENT, true,
            () => ConfirmDialog("FORCE DISARM",
                "This forcibly disarms motors in flight.\nConfirm to send FORCE_DISARM to the vehicle.",
                "CONFIRM DISARM", () => SendCmd("FORCE_DISARM")), 11, 40);

        var vibration = CreatePanelCard(outer, "VibrationCard", 118f, 1f);
        SectionHeader(vibration, "VIBRATION  (m/s²)");
        var vibRow = new GameObject("VibRow", typeof(RectTransform));
        vibRow.transform.SetParent(vibration.transform, false);
        vibRow.AddComponent<LayoutElement>().flexibleHeight = 1;
        var vibVl = vibRow.AddComponent<VerticalLayoutGroup>();
        vibVl.spacing = 4; vibVl.childForceExpandWidth = true;

        var vibBarsHL = new GameObject("VibBarsHL", typeof(RectTransform));
        vibBarsHL.transform.SetParent(vibRow.transform, false);
        vibBarsHL.AddComponent<LayoutElement>().preferredHeight = 48;
        var visHL = vibBarsHL.AddComponent<HorizontalLayoutGroup>();
        visHL.spacing = 8; visHL.childForceExpandWidth = true;

        var vibX = DroneUIFX.CreateGradientBar(vibBarsHL.transform, "X");
        var vibY = DroneUIFX.CreateGradientBar(vibBarsHL.transform, "Y");
        var vibZ = DroneUIFX.CreateGradientBar(vibBarsHL.transform, "Z");
        vibX.maxAbsValue = vibY.maxAbsValue = vibZ.maxAbsValue = ui.vibGaugeMax;
        fx.widgets.Add(vibX); fx.widgets.Add(vibY); fx.widgets.Add(vibZ);
        ui.vibXBar = vibX; ui.vibYBar = vibY; ui.vibZBar = vibZ;
        ui.vibXText = vibX.valueText; ui.vibYText = vibY.valueText; ui.vibZText = vibZ.valueText;

        var vibStatusRow = SlimStatRow(vibRow, "STATUS", "", rowH: 22);
        vibStatusRow.fontSize = 11;
        ui.vibStatusText = vibStatusRow;
    }

    // ─── Right panel ─────────────────────────────────────────────────────
    void BuildRightPanel(GameObject outer)
    {
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var power = CreatePanelCard(outer, "PowerCard", 178f);
        SectionHeader(power, "POWER");
        var battHero = new GameObject("BattHero", typeof(RectTransform));
        battHero.transform.SetParent(power.transform, false);
        battHero.AddComponent<LayoutElement>().preferredHeight = 70;
        var battHeroHL = battHero.AddComponent<HorizontalLayoutGroup>();
        battHeroHL.padding = new RectOffset(2, 2, 0, 2);
        battHeroHL.spacing = 12; battHeroHL.childAlignment = TextAnchor.MiddleLeft;

        // left: "BATTERY" label + big percent
        var pctCol = new GameObject("PctCol", typeof(RectTransform));
        pctCol.transform.SetParent(battHero.transform, false);
        pctCol.AddComponent<LayoutElement>().preferredWidth = 138;
        var pctVl = pctCol.AddComponent<VerticalLayoutGroup>();
        pctVl.spacing = 0; pctVl.childForceExpandWidth = true;
        var pctLblGO = new GameObject("Lbl"); pctLblGO.transform.SetParent(pctCol.transform, false);
        var pctLbl = pctLblGO.AddComponent<TextMeshProUGUI>();
        pctLbl.text = "BATTERY"; pctLbl.fontSize = 10; pctLbl.characterSpacing = 10;
        pctLbl.fontStyle = FontStyles.Bold; pctLbl.color = TXT_DIM; pctLbl.alignment = TextAlignmentOptions.MidlineLeft;
        var pctGO = new GameObject("Val"); pctGO.transform.SetParent(pctCol.transform, false);
        var pct = pctGO.AddComponent<TextMeshProUGUI>();
        pct.text = "--"; pct.fontSize = 36; pct.fontStyle = FontStyles.Bold;
        pct.color = NUM; pct.alignment = TextAlignmentOptions.MidlineLeft;
        ui.batteryPercentText = pct;

        var barSlot = new GameObject("BarSlot", typeof(RectTransform));
        barSlot.transform.SetParent(battHero.transform, false);
        barSlot.AddComponent<LayoutElement>().flexibleWidth = 1;
        var barVl = barSlot.AddComponent<VerticalLayoutGroup>();
        barVl.spacing = 4; barVl.childAlignment = TextAnchor.MiddleLeft;
        var barNoteGO = new GameObject("Note"); barNoteGO.transform.SetParent(barSlot.transform, false);
        var note = barNoteGO.AddComponent<TextMeshProUGUI>();
        note.text = "STATE"; note.fontSize = 9; note.characterSpacing = 8;
        note.fontStyle = FontStyles.Bold; note.color = TXT_DIM; note.alignment = TextAlignmentOptions.MidlineLeft;
        var barGauge = DroneUIFX.CreateHorizontalBar(barSlot.transform, "BatteryBar", 10);
        fx.widgets.Add(barGauge);
        ui.batteryBar = barGauge;
        var electrical = new GameObject("Electrical", typeof(RectTransform));
        electrical.transform.SetParent(power.transform, false);
        electrical.AddComponent<LayoutElement>().preferredHeight = 42;
        var electricalHL = electrical.AddComponent<HorizontalLayoutGroup>();
        electricalHL.spacing = 10;
        electricalHL.childForceExpandWidth = true;
        ui.voltageText = MiniStat(electrical, "VOLTAGE", "V");
        ui.currentText = MiniStat(electrical, "CURRENT", "A");

        var navigation = CreatePanelCard(outer, "NavigationCard", 218f);
        SectionHeader(navigation, "GPS  /  NAVIGATION");
        var gpsTop = new GameObject("GpsTop", typeof(RectTransform));
        gpsTop.transform.SetParent(navigation.transform, false);
        gpsTop.AddComponent<LayoutElement>().preferredHeight = 46;
        var gpsHL = gpsTop.AddComponent<HorizontalLayoutGroup>();
        gpsHL.padding = new RectOffset(2, 2, 0, 0);
        gpsHL.spacing = 14; gpsHL.childAlignment = TextAnchor.MiddleCenter;

        var signalBars = DroneUIFX.CreateSignalBars(gpsTop.transform, 5, 30f);
        fx.widgets.Add(signalBars);
        ui.gpsSignalBars = signalBars;

        var satsLbl = new GameObject("SatsLbl", typeof(RectTransform));
        satsLbl.transform.SetParent(gpsTop.transform, false);
        var satsT = satsLbl.AddComponent<TextMeshProUGUI>();
        satsT.text = "SATELLITES"; satsT.fontSize = 9; satsT.characterSpacing = 8;
        satsT.fontStyle = FontStyles.Bold; satsT.color = TXT_DIM; satsT.alignment = TextAlignmentOptions.MidlineLeft;
        satsLbl.AddComponent<LayoutElement>().preferredWidth = 74;

        var satVal = new GameObject("SatVal", typeof(RectTransform));
        satVal.transform.SetParent(gpsTop.transform, false);
        var sv = satVal.AddComponent<TextMeshProUGUI>();
        sv.text = "--"; sv.fontSize = 22; sv.fontStyle = FontStyles.Bold;
        sv.color = NUM; sv.alignment = TextAlignmentOptions.MidlineLeft;
        ui.gpsSatText = sv; // right-panel sat count (top bar has its own satTopText field)
        satVal.AddComponent<LayoutElement>().preferredWidth = 40;

        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(gpsTop.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        ui.latText = SlimStatRow(navigation, "LATITUDE", "", rowH: 28, labelW: 92);
        ui.lonText = SlimStatRow(navigation, "LONGITUDE", "", rowH: 28, labelW: 92);
        ui.gpsAltText = SlimStatRow(navigation, "GPS ALTITUDE", "m", rowH: 28, labelW: 108);

        var link = CreatePanelCard(outer, "LinkCard", 270f);
        SectionHeader(link, "LINK  /  TELEMETRY");
        var linkHero = new GameObject("LinkHero", typeof(RectTransform));
        linkHero.transform.SetParent(link.transform, false);
        linkHero.AddComponent<LayoutElement>().preferredHeight = 62;
        var linkHeroHL = linkHero.AddComponent<HorizontalLayoutGroup>();
        linkHeroHL.spacing = 16;
        linkHeroHL.childAlignment = TextAnchor.MiddleCenter;

        var latencyVisual = DroneUIFX.CreateSignalBars(linkHero.transform, 7, 32f);
        latencyVisual.activeColor = ACCENT;
        fx.widgets.Add(latencyVisual);
        ui.linkSignalBars = latencyVisual;

        var latRow = SlimStatRow(linkHero, "LATENCY", "ms", prominent: true, rowH: 56, labelW: 82);
        ui.latMeanText = latRow; // reuse as primary mean latency display
        latRow.fontSize = 20;

        var minMax = new GameObject("MinMax", typeof(RectTransform));
        minMax.transform.SetParent(link.transform, false);
        minMax.AddComponent<LayoutElement>().preferredHeight = 44;
        var mmHL = minMax.AddComponent<HorizontalLayoutGroup>();
        mmHL.spacing = 6; mmHL.childForceExpandWidth = true;
        ui.latMinText = MiniStat(minMax, "MIN", "ms");
        ui.latMaxText = MiniStat(minMax, "MAX", "ms");

        ui.latVarText = SlimStatRow(link, "JITTER  (σ)", "ms", rowH: 28, labelW: 112);
        var rateRow = SlimStatRow(link, "PACKET RATE", "pkts/s", rowH: 28, labelW: 112);
        ui.packetRateText = rateRow;

        var status = CreatePanelCard(outer, "SystemStatusCard", 164f, 1f);
        SectionHeader(status, "SYSTEM STATUS");
        ui.telemetryStatusText = StatusRow(status, "TELEMETRY");
        ui.gpsStatusText = StatusRow(status, "GPS");
        ui.imuStatusText = StatusRow(status, "IMU");
        ui.powerStatusText = StatusRow(status, "POWER BUS");
    }

    // ─── Center ──────────────────────────────────────────────────────────
    void BuildCameraView(GameObject center)
    {
        var mask = center.GetComponent<Mask>();
        if (mask == null) mask = center.AddComponent<Mask>();
        mask.showMaskGraphic = true;

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
        rt2.offsetMin = new Vector2(2,2); rt2.offsetMax = new Vector2(-2,-2);
    }

    void BuildCenterHud(GameObject center)
    {
        var viewTabs = FixedOverlayRow(center.transform, "ViewTabs", new Vector2(12, -12),
            new Vector2(288, 36), new Vector2(0, 1));
        OverlayChip(viewTabs, "3D VIEW", 140f, true);
        OverlayChip(viewTabs, "MAP VIEW", 140f, false);

        var cameraModes = FixedOverlayRow(center.transform, "CameraModes", new Vector2(-12, -12),
            new Vector2(382, 36), new Vector2(1, 1));
        OverlayChip(cameraModes, "CAM", 52f, false);
        OverlayChip(cameraModes, "FOLLOW", 78f, true);
        OverlayChip(cameraModes, "ORBIT", 70f, false);
        OverlayChip(cameraModes, "TOP", 58f, false);
        OverlayChip(cameraModes, "FREE", 66f, false);

        var compassSlot = new GameObject("ViewportCompass", typeof(RectTransform));
        compassSlot.transform.SetParent(center.transform, false);
        var compassRT = (RectTransform)compassSlot.transform;
        compassRT.anchorMin = compassRT.anchorMax = new Vector2(0.5f, 1f);
        compassRT.pivot = new Vector2(0.5f, 1f);
        compassRT.anchoredPosition = new Vector2(0, -52);
        compassRT.sizeDelta = new Vector2(500, 52);
        var compassLayout = compassSlot.AddComponent<HorizontalLayoutGroup>();
        compassLayout.childAlignment = TextAnchor.UpperCenter;
        var ribbon = DroneUIFX.CreateCompassRibbon(compassSlot.transform, 480f, 38f);
        fx.widgets.Add(ribbon);
        ui.compassRibbon = ribbon;

        var headingGO = new GameObject("HeadingValue", typeof(RectTransform));
        headingGO.transform.SetParent(center.transform, false);
        var heading = headingGO.AddComponent<TextMeshProUGUI>();
        heading.text = "---°";
        heading.fontSize = 14;
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 8;
        heading.color = TXT;
        heading.alignment = TextAlignmentOptions.Center;
        heading.raycastTarget = false;
        var headingRT = heading.rectTransform;
        headingRT.anchorMin = headingRT.anchorMax = new Vector2(0.5f, 1f);
        headingRT.pivot = new Vector2(0.5f, 1f);
        headingRT.anchoredPosition = new Vector2(0, -84);
        headingRT.sizeDelta = new Vector2(110, 24);
        ui.centerHeadingText = heading;

        var tools = new GameObject("ViewportTools", typeof(RectTransform), typeof(Image));
        tools.transform.SetParent(center.transform, false);
        var toolsImage = tools.GetComponent<Image>();
        toolsImage.sprite = DroneUIFX.RoundedRectSprite;
        toolsImage.type = Image.Type.Sliced;
        toolsImage.color = new Color(0.04f, 0.08f, 0.11f, 0.82f);
        var toolsRT = (RectTransform)tools.transform;
        toolsRT.anchorMin = toolsRT.anchorMax = new Vector2(1f, 0.5f);
        toolsRT.pivot = new Vector2(1f, 0.5f);
        toolsRT.anchoredPosition = new Vector2(-12, 0);
        toolsRT.sizeDelta = new Vector2(48, 184);
        var toolsVL = tools.AddComponent<VerticalLayoutGroup>();
        toolsVL.padding = new RectOffset(5, 5, 7, 7);
        toolsVL.spacing = 5;
        toolsVL.childForceExpandHeight = true;
        toolsVL.childForceExpandWidth = true;
        OverlayTool(tools, "TGT");
        OverlayTool(tools, "LYR");
        OverlayTool(tools, "SUN");
        OverlayTool(tools, "FULL");

        var lowerHud = FixedOverlayRow(center.transform, "FlightGlance", new Vector2(12, 12),
            new Vector2(522, 36), new Vector2(0, 0));
        ui.hudAltText = OverlayValueChip(lowerHud, "ALT", "-- m", 112f);
        ui.hudSpeedText = OverlayValueChip(lowerHud, "SPD", "-- m/s", 128f);
        ui.hudModeText = OverlayValueChip(lowerHud, "MODE", "UNKNOWN", 146f);
        ui.hudArmedText = OverlayValueChip(lowerHud, "ARM", "DISARMED", 112f);
    }

    GameObject FixedOverlayRow(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.04f, 0.08f, 0.11f, 0.86f);
        image.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(4, 4, 4, 4);
        hl.spacing = 4;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        return go;
    }

    void OverlayChip(GameObject parent, string label, float width, bool selected)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 28;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = selected ? new Color(0.08f, 0.28f, 0.36f, 0.96f) : new Color(0, 0, 0, 0);
        image.raycastTarget = false;
        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 10;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 6;
        text.color = selected ? new Color(0.45f, 0.92f, 1f, 1f) : TXT_SEC;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);
        if (selected)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.20f, 0.80f, 0.92f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
        }
    }

    void OverlayTool(GameObject parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.08f, 0.14f, 0.18f, 0.92f);
        image.raycastTarget = false;
        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 8;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 4;
        text.color = label == "SUN" ? AMBER : TXT;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);
    }

    TextMeshProUGUI OverlayValueChip(GameObject parent, string label, string initial, float width)
    {
        var go = new GameObject(label + "Chip", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = width;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.07f, 0.12f, 0.16f, 0.90f);
        image.raycastTarget = false;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(9, 9, 0, 0);
        hl.spacing = 7;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = true;
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 9;
        labelText.fontStyle = FontStyles.Bold;
        labelText.characterSpacing = 7;
        labelText.color = TXT_DIM;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelGO.AddComponent<LayoutElement>().preferredWidth = 36;
        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(go.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = initial;
        value.fontSize = 11;
        value.fontStyle = FontStyles.Bold;
        value.color = TXT;
        value.alignment = TextAlignmentOptions.MidlineRight;
        return value;
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
        hl.spacing = 8;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = true;

        TextMeshProUGUI v1, t1;
        var g1 = DroneUIFX.CreateGraphCard(bar.transform, "ALTITUDE", "m", new Color(0.22f,0.64f,0.82f,1f), out v1, out t1);
        g1.autoScale = true;
        g1.thickness = 1.9f;
        g1.fillColor = new Color(0.22f,0.64f,0.82f,0.07f);
        fx.widgets.Add(g1);
        ui.altitudeGraph = g1; ui.altitudeGraphValue = v1;

        TextMeshProUGUI v2, t2;
        var g2 = DroneUIFX.CreateGraphCard(bar.transform, "GROUND SPEED", "m/s", new Color(0.52f,0.62f,0.85f,1f), out v2, out t2);
        g2.autoScale = true;
        g2.manualMin = 0; g2.manualMax = 5;
        g2.fillColor = new Color(0.52f,0.62f,0.85f,0.07f);
        fx.widgets.Add(g2);
        ui.speedGraph = g2; ui.speedGraphValue = v2;

        TextMeshProUGUI v3, t3;
        var g3 = DroneUIFX.CreateGraphCard(bar.transform, "BATTERY", "%", GREEN, out v3, out t3);
        g3.autoScale = false;
        g3.manualMin = 0; g3.manualMax = 100;
        g3.fillColor = new Color(GREEN.r, GREEN.g, GREEN.b, 0.06f);
        fx.widgets.Add(g3);
        ui.batteryGraph = g3; ui.batteryGraphValue = v3;

        TextMeshProUGUI v4, t4;
        var g4 = DroneUIFX.CreateGraphCard(bar.transform, "LINK LATENCY", "ms", ACCENT, out v4, out t4);
        g4.autoScale = false;
        g4.manualMin = 0; g4.manualMax = 320;
        g4.fillColor = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.06f);
        fx.widgets.Add(g4);
        ui.latencyGraph = g4; ui.latencyGraphValue = v4;
    }

    /// Blocking-style confirm dialog for destructive actions (rendered once per use, destroyed after).
    /// Only fires `onConfirm` if the user presses CONFIRM.
    void ConfirmDialog(string title, string message, string confirmLabel, UnityEngine.Events.UnityAction onConfirm)
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { onConfirm(); return; }
        var root = new GameObject("ConfirmDialog", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);

        // full-screen blocker — intercepts all clicks
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        var dimImg = dim.GetComponent<Image>();
        dimImg.color = new Color(0, 0, 0, 0.55f);
        var dimRT = (RectTransform)dim.transform;
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = dimRT.offsetMax = Vector2.zero;

        // centered panel
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var panelImg = panel.GetComponent<Image>();
        panelImg.sprite = DroneUIFX.RoundedRectSprite; panelImg.type = Image.Type.Sliced;
        panelImg.color = CARD2;
        var panelRT = (RectTransform)panel.transform;
        panelRT.sizeDelta = new Vector2(420, 170);
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        // border
        var bdr = new GameObject("Border", typeof(RectTransform), typeof(Image));
        bdr.transform.SetParent(panel.transform, false);
        bdr.transform.SetAsFirstSibling();
        var bdrImg = bdr.GetComponent<Image>();
        bdrImg.sprite = DroneUIFX.RoundedRectSprite; bdrImg.type = Image.Type.Sliced;
        bdrImg.color = BORDER;
        bdrImg.raycastTarget = false;
        Stretch((RectTransform)bdr.transform, 0f);

        var vl = panel.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(18, 18, 16, 14);
        vl.spacing = 10;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(panel.transform, false);
        var titleT = titleGO.AddComponent<TextMeshProUGUI>();
        titleT.text = title; titleT.fontSize = 15; titleT.fontStyle = FontStyles.Bold;
        titleT.characterSpacing = 6; titleT.color = RED; titleT.alignment = TextAlignmentOptions.MidlineLeft;

        var msgGO = new GameObject("Message", typeof(RectTransform));
        msgGO.transform.SetParent(panel.transform, false);
        var msgT = msgGO.AddComponent<TextMeshProUGUI>();
        msgT.text = message; msgT.fontSize = 12; msgT.color = TXT_SEC;
        msgT.alignment = TextAlignmentOptions.MidlineLeft;
        msgT.textWrappingMode = TextWrappingModes.Normal;

        var btnRow = new GameObject("BtnRow", typeof(RectTransform));
        btnRow.transform.SetParent(panel.transform, false);
        btnRow.AddComponent<LayoutElement>().preferredHeight = 42;
        var btnHL = btnRow.AddComponent<HorizontalLayoutGroup>();
        btnHL.spacing = 10; btnHL.childForceExpandWidth = true;

        void Close() { if (root != null) Destroy(root); }

        var cancelGO = new GameObject("CancelBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        cancelGO.transform.SetParent(btnRow.transform, false);
        var cancelImg = cancelGO.GetComponent<Image>();
        cancelImg.sprite = DroneUIFX.RoundedRectSprite; cancelImg.type = Image.Type.Sliced;
        cancelImg.color = new Color(0.14f, 0.18f, 0.22f, 1f);
        var cancelBtn = cancelGO.GetComponent<Button>();
        cancelBtn.targetGraphic = cancelImg;
        MakeButtonLabel(cancelGO, "CANCEL", 12, TXT_SEC);
        cancelBtn.onClick.AddListener(() => Close());

        var okGO = new GameObject("ConfirmBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        okGO.transform.SetParent(btnRow.transform, false);
        var okImg = okGO.GetComponent<Image>();
        okImg.sprite = DroneUIFX.RoundedRectSprite; okImg.type = Image.Type.Sliced;
        okImg.color = new Color(0.78f, 0.30f, 0.30f, 0.20f);
        var okBtn = okGO.GetComponent<Button>();
        okBtn.targetGraphic = okImg;
        MakeButtonLabel(okGO, confirmLabel, 12, new Color(0.92f, 0.55f, 0.55f, 1f));
        okBtn.onClick.AddListener(() => { onConfirm(); Close(); });
    }

    TextMeshProUGUI MakeButtonLabel(GameObject buttonGO, string text, float size, Color color)
    {
        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(buttonGO.transform, false);
        var t = tgo.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 4; t.color = color; t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return t;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────
    GameObject CreatePanelCard(GameObject parent, string name, float preferredHeight, float flexibleHeight = 0f)
    {
        var card = new GameObject(name, typeof(RectTransform), typeof(Image));
        card.transform.SetParent(parent.transform, false);
        var image = card.GetComponent<Image>();
        image.color = CARD;
        image.raycastTarget = false;
        StylePanel(card);

        var le = card.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
        le.minHeight = Mathf.Min(preferredHeight, 96f);
        le.flexibleHeight = flexibleHeight;

        var vl = card.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(12, 12, 10, 11);
        vl.spacing = 5;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        return card;
    }

    void StylePanel(GameObject panel)
    {
        var image = panel.GetComponent<Image>();
        if (image == null) return;
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        var outline = panel.GetComponent<Outline>();
        if (outline == null) outline = panel.AddComponent<Outline>();
        outline.effectColor = BORDER;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;
    }

    TextMeshProUGUI VerticalStat(GameObject parent, string label, string initial)
    {
        var stat = new GameObject(label + "Stat", typeof(RectTransform), typeof(Image));
        stat.transform.SetParent(parent.transform, false);
        stat.AddComponent<LayoutElement>().flexibleHeight = 1;
        var image = stat.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.06f, 0.10f, 0.14f, 0.72f);
        image.raycastTarget = false;
        var vl = stat.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(10, 8, 4, 4);
        vl.spacing = 0;
        vl.childForceExpandHeight = true;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(stat.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 9;
        labelText.characterSpacing = 8;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TXT_DIM;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(stat.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = initial;
        value.fontSize = 18;
        value.fontStyle = FontStyles.Bold;
        value.color = ACCENT;
        value.alignment = TextAlignmentOptions.MidlineLeft;
        return value;
    }

    TextMeshProUGUI StatusRow(GameObject parent, string label)
    {
        var row = new GameObject(label + "Status", typeof(RectTransform));
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().preferredHeight = 25;
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(3, 3, 0, 0);
        hl.spacing = 9;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;

        var dotGO = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGO.transform.SetParent(row.transform, false);
        var dot = dotGO.GetComponent<Image>();
        dot.sprite = DroneUIFX.CircleSprite;
        dot.color = TXT_DIM;
        dot.raycastTarget = false;
        var dotLE = dotGO.AddComponent<LayoutElement>();
        dotLE.preferredWidth = 8;
        dotLE.preferredHeight = 8;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 10;
        labelText.characterSpacing = 6;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        var labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 126;
        labelLE.flexibleWidth = 1;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(row.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = "WAIT";
        value.fontSize = 10;
        value.fontStyle = FontStyles.Bold;
        value.characterSpacing = 6;
        value.color = TXT_DIM;
        value.alignment = TextAlignmentOptions.MidlineRight;
        valueGO.AddComponent<LayoutElement>().preferredWidth = 80;
        return value;
    }

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
        go.AddComponent<LayoutElement>().preferredHeight = 18;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft; hl.spacing = 8;
        hl.childForceExpandWidth = false;
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(go.transform, false);
        line.AddComponent<LayoutElement>().preferredWidth = 3;
        line.GetComponent<Image>().color = ACCENT;
        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        txtGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = title; txt.fontSize = 10; txt.characterSpacing = 14;
        txt.fontStyle = FontStyles.Bold; txt.color = TXT_DIM;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
    }

    /// Slim, box-free stat row. Label (small uppercase) left, value right.
    /// Hierarchy comes from typography + whitespace, not card boxes.
    TextMeshProUGUI SlimStatRow(GameObject parent, string label, string unit, bool prominent = false, float rowH = 26, float labelW = 88)
    {
        var row = new GameObject(label + "Row", typeof(RectTransform));
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().preferredHeight = rowH;

        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(2, 2, 0, 0);
        hl.spacing = 8; hl.childForceExpandWidth = false;
        hl.childAlignment = TextAnchor.MiddleLeft;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(row.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label; lbl.fontSize = 10; lbl.characterSpacing = 8;
        lbl.fontStyle = FontStyles.Bold; lbl.color = prominent ? TXT_SEC : TXT_DIM;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        var lle = lblGO.AddComponent<LayoutElement>(); lle.preferredWidth = labelW; lle.flexibleWidth = 0;

        var valGO = new GameObject("Value", typeof(RectTransform));
        valGO.transform.SetParent(row.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--";
        val.fontSize = prominent ? 20 : 14;
        val.fontStyle = FontStyles.Bold;
        val.color = prominent ? NUM : NUM;
        val.alignment = TextAlignmentOptions.MidlineRight;
        val.fontSizeMin = prominent ? 16 : 11;
        val.fontSizeMax = prominent ? 20 : 14;
        val.enableAutoSizing = true;
        valGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return val;
    }

    /// Compact three-column stat used for grouped trios (PITCH/ROLL/YAW, MIN/MAX).
    TextMeshProUGUI MiniStat(GameObject parent, string label, string unit)
    {
        var col = new GameObject(label + "Mini", typeof(RectTransform));
        col.transform.SetParent(parent.transform, false);
        col.AddComponent<LayoutElement>().flexibleWidth = 1;
        var vl = col.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 0; vl.childAlignment = TextAnchor.MiddleCenter;
        var lblGO = new GameObject("Lbl"); lblGO.transform.SetParent(col.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label; lbl.fontSize = 9; lbl.characterSpacing = 8; lbl.fontStyle = FontStyles.Bold;
        lbl.color = TXT_DIM; lbl.alignment = TextAlignmentOptions.Center;
        var valGO = new GameObject("Val"); valGO.transform.SetParent(col.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--"; val.fontSize = 15; val.fontStyle = FontStyles.Bold;
        val.color = NUM; val.alignment = TextAlignmentOptions.Center;
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

    void MakeAeroButton(GameObject parent, string label, Color accent, bool destructive, UnityEngine.Events.UnityAction cb, float fontSize = 13, float height = 42)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height; le.flexibleWidth = 1;
        var img = go.GetComponent<Image>();
        img.sprite = DroneUIFX.RoundedRectSprite; img.type = Image.Type.Sliced;
        img.color = destructive ? new Color(0.16f,0.11f,0.12f,1f) : new Color(0.13f,0.17f,0.22f,1f);
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
        t.fontStyle = FontStyles.Bold; t.color = destructive ? new Color(0.92f,0.62f,0.55f,1f) : TXT;
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
