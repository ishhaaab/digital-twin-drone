using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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

    [Header("Map Tiles")]
    [Range(3, 19)] public int mapZoom = 19;
    public string mapTileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string satelliteMapTileUrlTemplate = "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public string hybridMapLabelsUrlTemplate = "https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Transportation/MapServer/tile/{z}/{y}/{x}";
    public string mapTileUserAgent = "DigitalTwinDrone/1.0 (+https://github.com/ishhaaab/digital-twin-drone)";

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
    static readonly Color BTN_MODE_ACCENT  = new Color(0.125f, 0.847f, 0.941f, 1f);
    static readonly Color BTN_LAND_ACCENT  = new Color(0.953f, 0.714f, 0.184f, 1f);
    static readonly Color BTN_DISARM_ACCENT= new Color(0.941f, 0.310f, 0.310f, 1f);

    const float OUTER_PAD = 8f;
    const float GAP = 7f;
    const float TOP_H = 54f;
    const float BOTTOM_H = 162f;
    const float LEFT_EDGE = 0.195f;
    const float RIGHT_EDGE = 0.805f;

    DroneUIUpdater ui;
    DroneDataReceiver rx;
    DroneUIFX.UIFXAnimator fx;
    Canvas dashboardCanvas;
    GameObject liveContentRoot;
    GameObject replayContentRoot;
    RectTransform centerViewport;
    DroneViewportCameraController viewportCamera;
    DroneMapView mapView;
    OpenStreetMapTileLayer mapTileLayer;
    GameObject droneViewLayer;
    GameObject mapViewLayer;
    GameObject cameraModesOverlay;
    GameObject mapControlsOverlay;
    GameObject layerPopover;
    GameObject layer3DOptions;
    GameObject layerMapOptions;
    GameObject worldAxesOverlay;
    GameObject settingsPanel;
    readonly List<DroneUIFX.AeroButtonHoverFX> cameraModeStyles = new List<DroneUIFX.AeroButtonHoverFX>();
    readonly List<DroneUIFX.AeroButtonHoverFX> viewModeStyles = new List<DroneUIFX.AeroButtonHoverFX>();
    DroneUIFX.AeroButtonHoverFX liveNavStyle;
    DroneUIFX.AeroButtonHoverFX replayNavStyle;
    DroneUIFX.AeroButtonHoverFX settingsStyle;
    DroneUIFX.AeroButtonHoverFX layersStyle;
    DroneUIFX.AeroButtonHoverFX fullscreenStyle;
    DroneUIFX.AeroButtonHoverFX gridLayerStyle;
    DroneUIFX.AeroButtonHoverFX trailLayerStyle;
    readonly List<DroneUIFX.AeroButtonHoverFX> mapStyleOptions = new List<DroneUIFX.AeroButtonHoverFX>();
    bool layerPopoverOpen;
    bool gridVisible = true;
    bool trailVisible = true;
    bool showingMap;
    bool viewportFullscreen;
    Vector2 viewportAnchorMin, viewportAnchorMax, viewportOffsetMin, viewportOffsetMax;
    int viewportSiblingIndex;

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
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        dashboardCanvas = canvas;
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

        liveContentRoot = new GameObject("LiveContent", typeof(RectTransform));
        liveContentRoot.transform.SetParent(root, false);
        Stretch((RectTransform)liveContentRoot.transform);

        var leftOuter = MakeImage("LeftPanel", liveContentRoot.transform, new Color(0, 0, 0, 0),
            new Vector2(0, 0), new Vector2(LEFT_EDGE, 1),
            new Vector2(OUTER_PAD, OUTER_PAD), new Vector2(-GAP * 0.5f, -TOP_H - GAP));
        BuildLeftPanel(leftOuter);

        var rightOuter = MakeImage("RightPanel", liveContentRoot.transform, new Color(0, 0, 0, 0),
            new Vector2(RIGHT_EDGE, 0), new Vector2(1, 1),
            new Vector2(GAP * 0.5f, OUTER_PAD), new Vector2(-OUTER_PAD, -TOP_H - GAP));
        BuildRightPanel(rightOuter);

        var center = MakeImage("CenterViewport", liveContentRoot.transform, new Color(0.05f, 0.07f, 0.10f, 1f),
            new Vector2(LEFT_EDGE, 0), new Vector2(RIGHT_EDGE, 1),
            new Vector2(GAP * 0.5f, BOTTOM_H + GAP),
            new Vector2(-GAP * 0.5f, -TOP_H - GAP));
        centerViewport = (RectTransform)center.transform;
        StylePanel(center);
        BuildCameraView(center);
        BuildMapView(center);
        BuildCenterHud(center);
        BuildAlarmBanner(center);

        var bottomBar = MakeImage("TelemetryRail", liveContentRoot.transform, new Color(0, 0, 0, 0),
            new Vector2(LEFT_EDGE, 0), new Vector2(RIGHT_EDGE, 0),
            new Vector2(GAP * 0.5f, OUTER_PAD),
            new Vector2(-GAP * 0.5f, BOTTOM_H));
        BuildBottomBar(bottomBar);

        replayContentRoot = new GameObject("ReplayContent", typeof(RectTransform));
        replayContentRoot.transform.SetParent(root, false);
        var replayRT = (RectTransform)replayContentRoot.transform;
        replayRT.anchorMin = Vector2.zero;
        replayRT.anchorMax = Vector2.one;
        replayRT.offsetMin = new Vector2(OUTER_PAD, OUTER_PAD);
        replayRT.offsetMax = new Vector2(-OUTER_PAD, -TOP_H - GAP);
        replayContentRoot.AddComponent<FlightRecordsView>().Build();
        replayContentRoot.SetActive(false);

        Debug.Log("[DashboardUI] Aerospace GCS layout built.");
    }

    // ─── Top bar ─────────────────────────────────────────────────────────
    void BuildTopBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(11, 9, 5, 5);
        hl.spacing = 8;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = false;
        hl.childControlHeight = true;

        CreateBrand(bar.transform);

        liveNavStyle = TopNavTab(bar, "LIVE", 58f, true, true, () => SetPrimaryView(false));
        TopNavTab(bar, "MISSION", 76f, false, false, null);
        replayNavStyle = TopNavTab(bar, "REPLAY", 70f, false, true, () => SetPrimaryView(true));
        TopNavTab(bar, "LOGS", 56f, false, false, null);

        TopBarSep(bar);
        var connPill = TopLiveField(bar, "MAVLINK", "CONNECTION LOST", 146, DroneUIFX.IconType.Connection, true);
        fx.widgets.Add(connPill);
        ui.connectionPill = connPill;
        ui.connectionText = connPill.label;

        TopBarSep(bar);
        ui.flightModeText = TopBarField(bar, "MODE", "UNKNOWN", 108, DroneUIFX.IconType.Mode);
        TopBarSep(bar);
        ui.armedText = TopBarField(bar, "ARMED", "DISARMED", 108, DroneUIFX.IconType.Lock);
        TopBarSep(bar);

        var latPill = TopLiveField(bar, "LINK", "-- ms", 92, DroneUIFX.IconType.Signal, false);
        fx.widgets.Add(latPill);
        ui.latencyPill = latPill;
        ui.latencyText = latPill.label;

        TopBarSep(bar);
        ui.satTopText = TopBarField(bar, "GPS", "-- SAT", 92, DroneUIFX.IconType.Gps);
        TopBarSep(bar);
        ui.topHeadingText = TopBarField(bar, "HEADING", "---°", 98, DroneUIFX.IconType.Heading);
        TopBarSep(bar);
        ui.uptimeText = TopBarField(bar, "UPTIME", "00:00:00", 116, DroneUIFX.IconType.Clock);

        var spacer = new GameObject("FlexibleSpace", typeof(RectTransform));
        spacer.transform.SetParent(bar.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        settingsStyle = TopUtility(bar, "Settings", DroneUIFX.IconType.Settings, false, ToggleSettingsPanel);
    }

    DroneUIFX.AeroButtonHoverFX TopNavTab(GameObject bar, string label, float width, bool selected,
        bool interactable, UnityEngine.Events.UnityAction callback)
    {
        var go = new GameObject(label + "Tab", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(bar.transform, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = 32;

        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0, 0, 0, 0);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (interactable && callback != null) button.onClick.AddListener(callback);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var text = labelGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 10.5f;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 4;
        text.color = interactable ? TXT_SEC : new Color(TXT_DIM.r, TXT_DIM.g, TXT_DIM.b, 0.55f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);

        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.normalTextColor = text.color;
        style.SetSelected(selected);
        style.SetInteractable(interactable);
        labelGO.transform.SetAsLastSibling();
        return style;
    }

    DroneUIFX.AeroButtonHoverFX TopUtility(GameObject bar, string name, DroneUIFX.IconType icon, bool active,
        UnityEngine.Events.UnityAction callback)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(bar.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 32;
        le.preferredHeight = 32;
        var img = go.GetComponent<Image>();
        img.sprite = DroneUIFX.RoundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = active ? new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.11f) : new Color(0, 0, 0, 0);
        var button = go.GetComponent<Button>();
        button.targetGraphic = img;
        if (callback != null) button.onClick.AddListener(callback);

        var iconImage = DroneUIFX.CreateIcon(go.transform, icon, 16f, active ? ACCENT : TXT_DIM);
        var iconRT = iconImage.rectTransform;
        iconRT.anchorMin = iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(16, 16);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.foreground = iconImage;
        style.normalForegroundColor = TXT_DIM;
        style.SetSelected(active);
        iconImage.transform.SetAsLastSibling();
        return style;
    }

    TextMeshProUGUI TopBarField(GameObject bar, string label, string initial, float width, DroneUIFX.IconType icon)
    {
        var go = new GameObject(label + "Field", typeof(RectTransform));
        go.transform.SetParent(bar.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 34;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.spacing = 7;
        hl.childForceExpandWidth = false;

        DroneUIFX.CreateIcon(go.transform, icon, 16f, TXT_SEC);
        var textCol = new GameObject("Text", typeof(RectTransform));
        textCol.transform.SetParent(go.transform, false);
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1;
        var vl = textCol.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.MiddleLeft;
        vl.spacing = -1;
        vl.childForceExpandWidth = true;

        var lblGO = new GameObject("Lbl");
        lblGO.transform.SetParent(textCol.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 9.5f;
        lbl.characterSpacing = 5;
        lbl.fontStyle = FontStyles.Bold;
        lbl.color = TXT_DIM;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        var valGO = new GameObject("Val");
        valGO.transform.SetParent(textCol.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = initial;
        val.fontSize = 13;
        val.fontStyle = FontStyles.Bold;
        val.color = TXT;
        val.alignment = TextAlignmentOptions.MidlineLeft;
        return val;
    }

    DroneUIFX.AeroStatusPill TopLiveField(GameObject bar, string label, string initial, float width,
        DroneUIFX.IconType icon, bool useDot)
    {
        var go = new GameObject(label + "LiveField", typeof(RectTransform));
        go.transform.SetParent(bar.transform, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = 34;
        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 7;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childForceExpandWidth = false;

        Image stateImage;
        if (useDot)
        {
            var dotGO = new GameObject("State", typeof(RectTransform), typeof(Image));
            dotGO.transform.SetParent(go.transform, false);
            stateImage = dotGO.GetComponent<Image>();
            stateImage.sprite = DroneUIFX.CircleSprite;
            stateImage.color = TXT_DIM;
            var dotLayout = dotGO.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = 8;
            dotLayout.preferredHeight = 8;
        }
        else
        {
            stateImage = DroneUIFX.CreateIcon(go.transform, icon, 16f, TXT_SEC);
        }

        var textCol = new GameObject("Text", typeof(RectTransform));
        textCol.transform.SetParent(go.transform, false);
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1;
        var col = textCol.AddComponent<VerticalLayoutGroup>();
        col.spacing = -1;
        col.childAlignment = TextAnchor.MiddleLeft;
        col.childForceExpandWidth = true;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(textCol.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = initial;
        value.fontSize = 13;
        value.fontStyle = FontStyles.Bold;
        value.color = TXT;
        value.alignment = TextAlignmentOptions.MidlineLeft;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(textCol.transform, false);
        var caption = labelGO.AddComponent<TextMeshProUGUI>();
        caption.text = label;
        caption.fontSize = 9.5f;
        caption.characterSpacing = 5;
        caption.fontStyle = FontStyles.Bold;
        caption.color = TXT_DIM;
        caption.alignment = TextAlignmentOptions.MidlineLeft;

        return new DroneUIFX.AeroStatusPill { dot = stateImage, label = value };
    }

    void CreateBrand(Transform parent)
    {
        var go = new GameObject("Brand", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 214;
        layout.preferredHeight = 36;
        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 9;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childForceExpandWidth = false;

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(go.transform, false);
        accent.GetComponent<Image>().color = ACCENT;
        var accentLayout = accent.AddComponent<LayoutElement>();
        accentLayout.preferredWidth = 2;
        accentLayout.preferredHeight = 28;

        DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.Drone, 20f, ACCENT);

        var textCol = new GameObject("Text", typeof(RectTransform));
        textCol.transform.SetParent(go.transform, false);
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1;
        var col = textCol.AddComponent<VerticalLayoutGroup>();
        col.spacing = -1;
        col.childAlignment = TextAnchor.MiddleLeft;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(textCol.transform, false);
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text = "DRONE TWIN";
        title.fontSize = 15;
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 5;
        title.color = TXT;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        var subtitleGO = new GameObject("Subtitle", typeof(RectTransform));
        subtitleGO.transform.SetParent(textCol.transform, false);
        var subtitle = subtitleGO.AddComponent<TextMeshProUGUI>();
        subtitle.text = "GCS 0.1  /  FLIGHT CONTROL SYSTEM";
        subtitle.fontSize = 8.5f;
        subtitle.characterSpacing = 2;
        subtitle.color = TXT_DIM;
        subtitle.alignment = TextAlignmentOptions.MidlineLeft;
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
        vlg.spacing = 7;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = true;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var attitude = CreatePanelCard(outer, "AttitudeCard", 252f);
        SectionHeader(attitude, "ATTITUDE", DroneUIFX.IconType.Attitude);
        var attitudeRow = new GameObject("AttitudeRow", typeof(RectTransform));
        attitudeRow.transform.SetParent(attitude.transform, false);
        attitudeRow.AddComponent<LayoutElement>().flexibleHeight = 1;
        var attitudeHL = attitudeRow.AddComponent<HorizontalLayoutGroup>();
        attitudeHL.spacing = 8;
        attitudeHL.childAlignment = TextAnchor.MiddleCenter;
        attitudeHL.childForceExpandWidth = false;

        var horizonSlot = new GameObject("HorizonSlot", typeof(RectTransform));
        horizonSlot.transform.SetParent(attitudeRow.transform, false);
        var horizonLE = horizonSlot.AddComponent<LayoutElement>();
        horizonLE.preferredWidth = 190;
        horizonLE.flexibleWidth = 1;
        var hsl = horizonSlot.AddComponent<HorizontalLayoutGroup>();
        hsl.childAlignment = TextAnchor.MiddleCenter;
        var horizon = DroneUIFX.CreateArtificialHorizon(horizonSlot.transform, 172f);
        fx.widgets.Add(horizon);
        ui.horizon = horizon;

        var attitudeStats = new GameObject("AttitudeValues", typeof(RectTransform));
        attitudeStats.transform.SetParent(attitudeRow.transform, false);
        attitudeStats.AddComponent<LayoutElement>().preferredWidth = 112;
        var attitudeVL = attitudeStats.AddComponent<VerticalLayoutGroup>();
        attitudeVL.spacing = 4;
        attitudeVL.childForceExpandHeight = true;
        attitudeVL.childForceExpandWidth = true;
        ui.pitchText = VerticalStat(attitudeStats, "PITCH", "--");
        ui.rollText  = VerticalStat(attitudeStats, "ROLL", "--");
        ui.yawText   = VerticalStat(attitudeStats, "YAW", "--");

        var position = CreatePanelCard(outer, "PositionCard", 124f);
        SectionHeader(position, "POSITION  /  LOCAL NED", DroneUIFX.IconType.Position);
        ui.xText = SlimStatRow(position, "NORTH  (X)", "m", rowH: 27, labelW: 112);
        ui.yText = SlimStatRow(position, "EAST  (Y)", "m", rowH: 27, labelW: 112);
        ui.zText = SlimStatRow(position, "ALTITUDE  (Z)", "m", rowH: 27, labelW: 112);

        var movement = CreatePanelCard(outer, "MovementCard", 86f);
        SectionHeader(movement, "MOVEMENT", DroneUIFX.IconType.Speed);
        ui.speedText = SlimStatRow(movement, "GROUND SPEED", "m/s", rowH: 27, labelW: 140);
        ui.verticalSpeedText = SlimStatRow(movement, "VERTICAL SPEED", "m/s", rowH: 27, labelW: 140);

        var controls = CreatePanelCard(outer, "ControlsCard", 194f);
        SectionHeader(controls, "FLIGHT CONTROLS", DroneUIFX.IconType.Controls);
        var modeCaption = new GameObject("ModeCaption", typeof(RectTransform));
        modeCaption.transform.SetParent(controls.transform, false);
        modeCaption.AddComponent<LayoutElement>().preferredHeight = 14;
        var modeCaptionText = modeCaption.AddComponent<TextMeshProUGUI>();
        modeCaptionText.text = "FLIGHT MODE";
        modeCaptionText.fontSize = 11;
        modeCaptionText.characterSpacing = 4;
        modeCaptionText.fontStyle = FontStyles.Bold;
        modeCaptionText.color = TXT_DIM;
        modeCaptionText.alignment = TextAlignmentOptions.MidlineLeft;
        var modeRow = new GameObject("ModeRow", typeof(RectTransform));
        modeRow.transform.SetParent(controls.transform, false);
        modeRow.AddComponent<LayoutElement>().preferredHeight = 30;
        var mrhl = modeRow.AddComponent<HorizontalLayoutGroup>();
        mrhl.spacing = 6; mrhl.childForceExpandWidth = true;
        ui.stabilizeButtonStyle = MakeAeroButton(modeRow, "STABILIZE", BTN_MODE_ACCENT, false, () => SendCmd("STABILIZE"), 10.5f, 30);
        ui.altHoldButtonStyle = MakeAeroButton(modeRow, "ALT HOLD", BTN_MODE_ACCENT, false, () => SendCmd("ALT_HOLD"), 10.5f, 30);
        ui.posHoldButtonStyle = MakeAeroButton(modeRow, "POS HOLD", BTN_MODE_ACCENT, false, () => SendCmd("POSHOLD"), 10.5f, 30);
        ui.landButtonStyle = MakeAeroButton(controls, "LAND", BTN_LAND_ACCENT, false, () => SendCmd("LAND"), 11, 32);
        ui.forceDisarmButtonStyle = MakeAeroButton(controls, "FORCE DISARM", BTN_DISARM_ACCENT, true,
            () => ConfirmDialog("FORCE DISARM",
                "This forcibly disarms motors in flight.\nConfirm to send FORCE_DISARM to the vehicle.",
                "CONFIRM DISARM", () => SendCmd("FORCE_DISARM")), 11, 32, DroneUIFX.IconType.Lock);

        var commandStatusGO = new GameObject("CommandStatus", typeof(RectTransform));
        commandStatusGO.transform.SetParent(controls.transform, false);
        commandStatusGO.AddComponent<LayoutElement>().preferredHeight = 18;
        var commandStatus = commandStatusGO.AddComponent<TextMeshProUGUI>();
        commandStatus.text = "COMMANDS LOCKED";
        commandStatus.fontSize = 9.5f;
        commandStatus.characterSpacing = 3;
        commandStatus.fontStyle = FontStyles.Bold;
        commandStatus.color = TXT_DIM;
        commandStatus.alignment = TextAlignmentOptions.Center;
        commandStatus.raycastTarget = false;
        ui.commandStatusText = commandStatus;

        var vibration = CreatePanelCard(outer, "VibrationCard", 96f);
        SectionHeader(vibration, "VIBRATION  (m/s²)", DroneUIFX.IconType.Telemetry);
        var vibRow = new GameObject("VibRow", typeof(RectTransform));
        vibRow.transform.SetParent(vibration.transform, false);
        vibRow.AddComponent<LayoutElement>().flexibleHeight = 1;
        var vibLayout = vibRow.AddComponent<HorizontalLayoutGroup>();
        vibLayout.padding = new RectOffset(4, 2, 0, 0);
        vibLayout.spacing = 10;
        vibLayout.childAlignment = TextAnchor.MiddleLeft;
        vibLayout.childForceExpandWidth = true;
        vibLayout.childForceExpandHeight = false;
        ui.vibXText = CompactInlineMetric(vibRow, "X");
        ui.vibYText = CompactInlineMetric(vibRow, "Y");
        ui.vibZText = CompactInlineMetric(vibRow, "Z");

        var statusGO = new GameObject("Status", typeof(RectTransform), typeof(Image));
        statusGO.transform.SetParent(vibRow.transform, false);
        var statusLayout = statusGO.AddComponent<LayoutElement>();
        statusLayout.preferredWidth = 54;
        statusLayout.preferredHeight = 30;
        statusLayout.flexibleWidth = 0;
        var statusImage = statusGO.GetComponent<Image>();
        statusImage.sprite = DroneUIFX.RoundedRectSprite;
        statusImage.type = Image.Type.Sliced;
        statusImage.color = new Color(GREEN.r, GREEN.g, GREEN.b, 0.12f);
        statusImage.raycastTarget = false;
        var statusTextGO = new GameObject("Value", typeof(RectTransform));
        statusTextGO.transform.SetParent(statusGO.transform, false);
        var statusText = statusTextGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "OK";
        statusText.fontSize = 12;
        statusText.fontStyle = FontStyles.Bold;
        statusText.color = GREEN;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.raycastTarget = false;
        Stretch(statusText.rectTransform);
        ui.vibStatusText = statusText;
    }

    // ─── Right panel ─────────────────────────────────────────────────────
    void BuildRightPanel(GameObject outer)
    {
        var vlg = outer.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 7;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = true;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var power = CreatePanelCard(outer, "PowerCard", 158f);
        SectionHeader(power, "POWER", DroneUIFX.IconType.Battery);
        var battHero = new GameObject("BattHero", typeof(RectTransform));
        battHero.transform.SetParent(power.transform, false);
        battHero.AddComponent<LayoutElement>().preferredHeight = 58;
        var battHeroHL = battHero.AddComponent<HorizontalLayoutGroup>();
        battHeroHL.padding = new RectOffset(2, 2, 0, 2);
        battHeroHL.spacing = 12; battHeroHL.childAlignment = TextAnchor.MiddleLeft;

        // left: "BATTERY" label + big percent
        var pctCol = new GameObject("PctCol", typeof(RectTransform));
        pctCol.transform.SetParent(battHero.transform, false);
        pctCol.AddComponent<LayoutElement>().preferredWidth = 122;
        var pctVl = pctCol.AddComponent<VerticalLayoutGroup>();
        pctVl.spacing = 0; pctVl.childForceExpandWidth = true;
        var pctLblGO = new GameObject("Lbl"); pctLblGO.transform.SetParent(pctCol.transform, false);
        var pctLbl = pctLblGO.AddComponent<TextMeshProUGUI>();
        pctLbl.text = "BATTERY"; pctLbl.fontSize = 11; pctLbl.characterSpacing = 5;
        pctLbl.fontStyle = FontStyles.Bold; pctLbl.color = TXT_SEC; pctLbl.alignment = TextAlignmentOptions.MidlineLeft;
        var pctGO = new GameObject("Val"); pctGO.transform.SetParent(pctCol.transform, false);
        var pct = pctGO.AddComponent<TextMeshProUGUI>();
        pct.text = "--"; pct.fontSize = 28; pct.fontStyle = FontStyles.Bold;
        pct.color = NUM; pct.alignment = TextAlignmentOptions.MidlineLeft;
        ui.batteryPercentText = pct;

        var barSlot = new GameObject("BarSlot", typeof(RectTransform));
        barSlot.transform.SetParent(battHero.transform, false);
        barSlot.AddComponent<LayoutElement>().flexibleWidth = 1;
        var barVl = barSlot.AddComponent<VerticalLayoutGroup>();
        barVl.spacing = 4; barVl.childAlignment = TextAnchor.MiddleLeft;
        var barNoteGO = new GameObject("Note"); barNoteGO.transform.SetParent(barSlot.transform, false);
        var note = barNoteGO.AddComponent<TextMeshProUGUI>();
        note.text = "STATE"; note.fontSize = 10.5f; note.characterSpacing = 4;
        note.fontStyle = FontStyles.Bold; note.color = TXT_SEC; note.alignment = TextAlignmentOptions.MidlineLeft;
        var barGauge = DroneUIFX.CreateHorizontalBar(barSlot.transform, "BatteryBar", 10);
        fx.widgets.Add(barGauge);
        ui.batteryBar = barGauge;
        var electrical = new GameObject("Electrical", typeof(RectTransform));
        electrical.transform.SetParent(power.transform, false);
        electrical.AddComponent<LayoutElement>().preferredHeight = 38;
        var electricalHL = electrical.AddComponent<HorizontalLayoutGroup>();
        electricalHL.spacing = 10;
        electricalHL.childForceExpandWidth = true;
        ui.voltageText = MiniStat(electrical, "VOLTAGE", "V");
        ui.currentText = MiniStat(electrical, "CURRENT", "A");

        var navigation = CreatePanelCard(outer, "NavigationCard", 190f);
        SectionHeader(navigation, "GPS  /  NAVIGATION", DroneUIFX.IconType.Gps);
        var gpsTop = new GameObject("GpsTop", typeof(RectTransform));
        gpsTop.transform.SetParent(navigation.transform, false);
        gpsTop.AddComponent<LayoutElement>().preferredHeight = 40;
        var gpsHL = gpsTop.AddComponent<HorizontalLayoutGroup>();
        gpsHL.padding = new RectOffset(2, 2, 0, 0);
        gpsHL.spacing = 14; gpsHL.childAlignment = TextAnchor.MiddleCenter;

        var signalBars = DroneUIFX.CreateSignalBars(gpsTop.transform, 5, 24f);
        fx.widgets.Add(signalBars);
        ui.gpsSignalBars = signalBars;

        var satsLbl = new GameObject("SatsLbl", typeof(RectTransform));
        satsLbl.transform.SetParent(gpsTop.transform, false);
        var satsT = satsLbl.AddComponent<TextMeshProUGUI>();
        satsT.text = "SATELLITES"; satsT.fontSize = 10.5f; satsT.characterSpacing = 4;
        satsT.fontStyle = FontStyles.Bold; satsT.color = TXT_SEC; satsT.alignment = TextAlignmentOptions.MidlineLeft;
        satsLbl.AddComponent<LayoutElement>().preferredWidth = 74;

        var satVal = new GameObject("SatVal", typeof(RectTransform));
        satVal.transform.SetParent(gpsTop.transform, false);
        var sv = satVal.AddComponent<TextMeshProUGUI>();
        sv.text = "--"; sv.fontSize = 19; sv.fontStyle = FontStyles.Bold;
        sv.color = NUM; sv.alignment = TextAlignmentOptions.MidlineLeft;
        ui.gpsSatText = sv; // right-panel sat count (top bar has its own satTopText field)
        satVal.AddComponent<LayoutElement>().preferredWidth = 40;

        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(gpsTop.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        ui.latText = SlimStatRow(navigation, "LATITUDE", "°", rowH: 27, labelW: 104);
        ui.lonText = SlimStatRow(navigation, "LONGITUDE", "°", rowH: 27, labelW: 104);
        ui.gpsAltText = SlimStatRow(navigation, "GPS ALTITUDE", "m", rowH: 27, labelW: 118);

        var link = CreatePanelCard(outer, "LinkCard", 242f);
        SectionHeader(link, "LINK  /  TELEMETRY", DroneUIFX.IconType.Telemetry);
        var linkHero = new GameObject("LinkHero", typeof(RectTransform));
        linkHero.transform.SetParent(link.transform, false);
        linkHero.AddComponent<LayoutElement>().preferredHeight = 48;
        var linkHeroHL = linkHero.AddComponent<HorizontalLayoutGroup>();
        linkHeroHL.spacing = 16;
        linkHeroHL.childAlignment = TextAnchor.MiddleCenter;

        var latencyVisual = DroneUIFX.CreateSignalBars(linkHero.transform, 7, 26f);
        latencyVisual.activeColor = ACCENT;
        fx.widgets.Add(latencyVisual);
        ui.linkSignalBars = latencyVisual;

        var latRow = SlimStatRow(linkHero, "LATENCY", "ms", prominent: true, rowH: 44, labelW: 76);
        ui.latMeanText = latRow; // reuse as primary mean latency display
        latRow.fontSize = 20;

        var rateRow = SlimStatRow(link, "PACKET RATE", "Hz", rowH: 26, labelW: 126);
        ui.packetRateText = rateRow;
        ui.latVarText = SlimStatRow(link, "JITTER  (σ)", "ms", rowH: 26, labelW: 126);
        ui.latMinText = SlimStatRow(link, "MIN LATENCY", "ms", rowH: 26, labelW: 126);
        ui.latMaxText = SlimStatRow(link, "MAX LATENCY", "ms", rowH: 26, labelW: 126);
        ui.packetLossText = SlimStatRow(link, "PACKET LOSS", "%", rowH: 26, labelW: 126);

        var status = CreatePanelCard(outer, "SystemStatusCard", 154f);
        SectionHeader(status, "SYSTEM STATUS", DroneUIFX.IconType.System);
        ui.telemetryStatusText = StatusRow(status, "TELEMETRY");
        ui.gpsStatusText = StatusRow(status, "GPS");
        ui.imuStatusText = StatusRow(status, "IMU");
        ui.barometerStatusText = StatusRow(status, "BAROMETER");
    }

    // ─── Center ──────────────────────────────────────────────────────────
    void BuildCameraView(GameObject center)
    {
        var mask = center.GetComponent<Mask>();
        if (mask == null) mask = center.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        droneViewLayer = new GameObject("DroneViewLayer", typeof(RectTransform));
        droneViewLayer.transform.SetParent(center.transform, false);
        Stretch((RectTransform)droneViewLayer.transform, 1f);

        if (droneViewTexture == null)
        {
            var lbl = new GameObject("NoTexLabel");
            lbl.transform.SetParent(droneViewLayer.transform, false);
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text = "Assign Drone View Texture in Inspector — or camera view will appear here when playing SampleScene.";
            t.fontSize = 15; t.color = TXT_DIM; t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.08f, 0.42f); rt.anchorMax = new Vector2(0.92f, 0.58f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return;
        }
        var go = new GameObject("DroneView");
        go.transform.SetParent(droneViewLayer.transform, false);
        var img = go.AddComponent<RawImage>();
        img.texture = droneViewTexture;
        img.raycastTarget = true;
        var cover = go.AddComponent<DroneViewportCover>();
        cover.image = img;
        var rt2 = img.rectTransform;
        rt2.anchorMin = Vector2.zero; rt2.anchorMax = Vector2.one;
        rt2.offsetMin = new Vector2(1, 1); rt2.offsetMax = new Vector2(-1, -1);

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i].targetTexture != droneViewTexture) continue;
            viewportCamera = cameras[i].GetComponent<DroneViewportCameraController>();
            if (viewportCamera == null) viewportCamera = cameras[i].gameObject.AddComponent<DroneViewportCameraController>();
            var controller = FindFirstObjectByType<DroneController>();
            if (controller != null) viewportCamera.Initialize(controller.transform);
            var input = go.AddComponent<DroneViewportInput>();
            input.controller = viewportCamera;
            break;
        }
    }

    void BuildMapView(GameObject center)
    {
        mapViewLayer = new GameObject("MapViewLayer", typeof(RectTransform), typeof(Image));
        mapViewLayer.transform.SetParent(center.transform, false);
        var background = mapViewLayer.GetComponent<Image>();
        background.color = new Color(0.025f, 0.073f, 0.096f, 1f);
        background.raycastTarget = false;
        Stretch((RectTransform)mapViewLayer.transform, 1f);

        var plot = new GameObject("MapPlot", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        plot.transform.SetParent(mapViewLayer.transform, false);
        var plotImage = plot.GetComponent<Image>();
        plotImage.color = new Color(0f, 0f, 0f, 0f);
        plotImage.raycastTarget = true;
        var plotRT = (RectTransform)plot.transform;
        plotRT.anchorMin = Vector2.zero;
        plotRT.anchorMax = Vector2.one;
        plotRT.offsetMin = new Vector2(18, 46);
        plotRT.offsetMax = new Vector2(-18, -18);

        var tileRoot = new GameObject("OpenStreetMapTiles", typeof(RectTransform));
        tileRoot.transform.SetParent(plot.transform, false);
        Stretch((RectTransform)tileRoot.transform);

        var mapOverlay = new GameObject("FlightOverlay", typeof(RectTransform), typeof(CanvasRenderer));
        mapOverlay.transform.SetParent(plot.transform, false);
        Stretch((RectTransform)mapOverlay.transform);
        mapView = mapOverlay.AddComponent<DroneMapView>();

        mapTileLayer = plot.AddComponent<OpenStreetMapTileLayer>();
        mapTileLayer.zoom = mapZoom;
        mapTileLayer.tileUrlTemplate = mapTileUrlTemplate;
        mapTileLayer.satelliteTileUrlTemplate = satelliteMapTileUrlTemplate;
        mapTileLayer.hybridLabelsTileUrlTemplate = hybridMapLabelsUrlTemplate;
        mapTileLayer.userAgent = mapTileUserAgent;
        mapTileLayer.tileRoot = (RectTransform)tileRoot.transform;
        mapTileLayer.overlay = mapView;
        mapView.tileLayer = mapTileLayer;

        CreateMapCardinal(mapViewLayer.transform, "N", new Vector2(0.5f, 1f), new Vector2(0, -92));
        CreateMapCardinal(mapViewLayer.transform, "E", new Vector2(1f, 0.5f), new Vector2(-29, 0));
        CreateMapCardinal(mapViewLayer.transform, "S", new Vector2(0.5f, 0f), new Vector2(0, 50));
        CreateMapCardinal(mapViewLayer.transform, "W", new Vector2(0f, 0.5f), new Vector2(29, 0));

        var mapTitleGO = new GameObject("MapTitle", typeof(RectTransform));
        mapTitleGO.transform.SetParent(mapViewLayer.transform, false);
        var mapTitle = mapTitleGO.AddComponent<TextMeshProUGUI>();
        mapTitle.text = "OPENSTREETMAP  /  WAITING FOR GPS";
        mapTitle.fontSize = 11;
        mapTitle.fontStyle = FontStyles.Bold;
        mapTitle.characterSpacing = 5;
        mapTitle.color = TXT_SEC;
        mapTitle.alignment = TextAlignmentOptions.MidlineLeft;
        var mapTitleRT = mapTitle.rectTransform;
        mapTitleRT.anchorMin = mapTitleRT.anchorMax = new Vector2(0, 1);
        mapTitleRT.pivot = new Vector2(0, 1);
        mapTitleRT.anchoredPosition = new Vector2(18, -50);
        mapTitleRT.sizeDelta = new Vector2(360, 20);
        mapTileLayer.statusText = mapTitle;

        var scaleGO = new GameObject("Scale", typeof(RectTransform));
        scaleGO.transform.SetParent(mapViewLayer.transform, false);
        var scale = scaleGO.AddComponent<TextMeshProUGUI>();
        scale.text = "GRID  2.5 m";
        scale.fontSize = 10.5f;
        scale.fontStyle = FontStyles.Bold;
        scale.color = ACCENT;
        scale.alignment = TextAlignmentOptions.MidlineRight;
        var scaleRT = scale.rectTransform;
        scaleRT.anchorMin = scaleRT.anchorMax = new Vector2(1, 1);
        scaleRT.pivot = new Vector2(1, 1);
        scaleRT.anchoredPosition = new Vector2(-18, -50);
        scaleRT.sizeDelta = new Vector2(130, 20);
        mapView.scaleText = scale;

        var readout = new GameObject("MapReadout", typeof(RectTransform), typeof(Image));
        readout.transform.SetParent(mapViewLayer.transform, false);
        var readoutImage = readout.GetComponent<Image>();
        readoutImage.sprite = DroneUIFX.RoundedRectSprite;
        readoutImage.type = Image.Type.Sliced;
        readoutImage.color = new Color(BG.r, BG.g, BG.b, 0.86f);
        readoutImage.raycastTarget = false;
        var readoutRT = (RectTransform)readout.transform;
        readoutRT.anchorMin = new Vector2(0, 0);
        readoutRT.anchorMax = new Vector2(1, 0);
        readoutRT.pivot = new Vector2(0.5f, 0);
        readoutRT.anchoredPosition = new Vector2(0, 10);
        readoutRT.sizeDelta = new Vector2(-20, 30);
        var readoutLayout = readout.AddComponent<HorizontalLayoutGroup>();
        readoutLayout.padding = new RectOffset(10, 10, 0, 0);
        readoutLayout.spacing = 12;
        readoutLayout.childAlignment = TextAnchor.MiddleLeft;
        readoutLayout.childForceExpandWidth = true;

        mapView.gpsText = CreateMapReadoutText(readout.transform, "LAT  --    LON  --", TextAlignmentOptions.MidlineLeft);
        mapView.localText = CreateMapReadoutText(readout.transform, "N  -- m    E  -- m", TextAlignmentOptions.MidlineRight);
        CreateMapAttribution(mapViewLayer.transform);
        mapViewLayer.SetActive(false);
        ui.mapView = mapView;
    }

    void CreateMapAttribution(Transform parent)
    {
        var go = new GameObject("BasemapAttribution", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(BG.r, BG.g, BG.b, 0.86f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Application.OpenURL(mapTileLayer.AttributionUrl));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-18, 46);
        rt.sizeDelta = new Vector2(220, 22);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = "© OpenStreetMap contributors";
        text.fontSize = 11;
        text.color = TXT;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);
        mapTileLayer.attributionText = text;
    }

    TextMeshProUGUI CreateMapReadoutText(Transform parent, string initial, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Value", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = initial;
        text.fontSize = 11;
        text.fontStyle = FontStyles.Bold;
        text.color = TXT;
        text.alignment = alignment;
        return text;
    }

    void CreateMapCardinal(Transform parent, string value, Vector2 anchor, Vector2 position)
    {
        var go = new GameObject("Cardinal" + value, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = 12;
        text.fontStyle = FontStyles.Bold;
        text.color = ACCENT;
        text.alignment = TextAlignmentOptions.Center;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(22, 18);
    }

    void BuildCenterHud(GameObject center)
    {
        var viewTabs = FixedOverlayRow(center.transform, "ViewTabs", new Vector2(10, -10),
            new Vector2(208, 32), new Vector2(0, 1));
        viewModeStyles.Clear();
        viewModeStyles.Add(OverlayChip(viewTabs, "3D VIEW", 98f, true, () => SetMapView(false), true));
        viewModeStyles.Add(OverlayChip(viewTabs, "MAP VIEW", 98f, false, () => SetMapView(true), true));

        var cameraModes = FixedOverlayRow(center.transform, "CameraModes", new Vector2(-10, -10),
            new Vector2(282, 32), new Vector2(1, 1));
        cameraModesOverlay = cameraModes;
        OverlayIconButton(cameraModes, DroneUIFX.IconType.Camera, () => SetMapView(false), false, true);
        cameraModeStyles.Clear();
        cameraModeStyles.Add(OverlayChip(cameraModes, "FOLLOW", 67f, true,
            () => SetViewportMode(DroneViewportCameraController.ViewMode.Follow, 0), true));
        cameraModeStyles.Add(OverlayChip(cameraModes, "ORBIT", 60f, false,
            () => SetViewportMode(DroneViewportCameraController.ViewMode.Orbit, 1), true));
        cameraModeStyles.Add(OverlayChip(cameraModes, "TOP", 48f, false,
            () => SetViewportMode(DroneViewportCameraController.ViewMode.Top, 2), true));
        cameraModeStyles.Add(OverlayChip(cameraModes, "FREE", 52f, false,
            () => SetViewportMode(DroneViewportCameraController.ViewMode.Free, 3), true));

        mapControlsOverlay = FixedOverlayRow(center.transform, "MapControls", new Vector2(-10, -10),
            new Vector2(92, 32), new Vector2(1, 1));
        OverlayChip(mapControlsOverlay, "-", 44f, false, () => mapTileLayer?.ZoomBy(-1), true);
        OverlayChip(mapControlsOverlay, "+", 44f, false, () => mapTileLayer?.ZoomBy(1), true);
        mapControlsOverlay.SetActive(false);

        var compassSlot = new GameObject("ViewportCompass", typeof(RectTransform));
        compassSlot.transform.SetParent(center.transform, false);
        var compassRT = (RectTransform)compassSlot.transform;
        compassRT.anchorMin = compassRT.anchorMax = new Vector2(0.5f, 1f);
        compassRT.pivot = new Vector2(0.5f, 1f);
        compassRT.anchoredPosition = new Vector2(0, -43);
        compassRT.sizeDelta = new Vector2(430, 40);
        var compassLayout = compassSlot.AddComponent<HorizontalLayoutGroup>();
        compassLayout.childAlignment = TextAnchor.UpperCenter;
        var ribbon = DroneUIFX.CreateCompassRibbon(compassSlot.transform, 420f, 30f);
        fx.widgets.Add(ribbon);
        ui.compassRibbon = ribbon;

        var headingGO = new GameObject("HeadingValue", typeof(RectTransform));
        headingGO.transform.SetParent(center.transform, false);
        var heading = headingGO.AddComponent<TextMeshProUGUI>();
        heading.text = "---°";
        heading.fontSize = 15;
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 8;
        heading.color = TXT;
        heading.alignment = TextAlignmentOptions.Center;
        heading.raycastTarget = false;
        var headingRT = heading.rectTransform;
        headingRT.anchorMin = headingRT.anchorMax = new Vector2(0.5f, 1f);
        headingRT.pivot = new Vector2(0.5f, 1f);
        headingRT.anchoredPosition = new Vector2(0, -73);
        headingRT.sizeDelta = new Vector2(110, 24);
        ui.centerHeadingText = heading;

        var tools = new GameObject("ViewportTools", typeof(RectTransform), typeof(Image));
        tools.transform.SetParent(center.transform, false);
        var toolsImage = tools.GetComponent<Image>();
        toolsImage.color = new Color(0, 0, 0, 0);
        toolsImage.raycastTarget = false;
        var toolsRT = (RectTransform)tools.transform;
        toolsRT.anchorMin = toolsRT.anchorMax = new Vector2(1f, 0.5f);
        toolsRT.pivot = new Vector2(1f, 0.5f);
        toolsRT.anchoredPosition = new Vector2(-10, 0);
        toolsRT.sizeDelta = new Vector2(38, 116);
        var toolsVL = tools.AddComponent<VerticalLayoutGroup>();
        toolsVL.padding = new RectOffset(2, 2, 2, 2);
        toolsVL.spacing = 5;
        toolsVL.childForceExpandHeight = false;
        toolsVL.childForceExpandWidth = true;
        OverlayIconButton(tools, DroneUIFX.IconType.Target, () =>
        {
            if (mapViewLayer != null && mapViewLayer.activeSelf)
                mapView?.Recenter();
            else
            {
                viewportCamera?.ResetView();
                SetViewportMode(DroneViewportCameraController.ViewMode.Follow, 0);
            }
        }, false, true);
        layersStyle = OverlayIconButton(tools, DroneUIFX.IconType.Layers, ToggleLayerPopover, false, true);
        fullscreenStyle = OverlayIconButton(tools, DroneUIFX.IconType.Fullscreen, ToggleViewportFullscreen, false, true);

        BuildLayerPopover(center);
        BuildWorldAxes(center);

        BuildViewportGpsReadout(center);

        BuildSettingsPanel(center);
        SetMapView(false);
    }

    void BuildLayerPopover(GameObject center)
    {
        layerPopover = new GameObject("LayerPopover", typeof(RectTransform), typeof(Image));
        layerPopover.transform.SetParent(center.transform, false);
        var image = layerPopover.GetComponent<Image>();
        image.color = new Color(CARD2.r, CARD2.g, CARD2.b, 0.97f);
        StylePanel(layerPopover);
        var rt = (RectTransform)layerPopover.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-54, 0);
        rt.sizeDelta = new Vector2(196, 112);

        layer3DOptions = CreateLayerOptions(layerPopover.transform, "3DLayers", "3D LAYERS");
        gridLayerStyle = OverlayChip(layer3DOptions, "WORLD GRID", 174f, true, ToggleGrid, true);
        trailLayerStyle = OverlayChip(layer3DOptions, "FLIGHT TRAIL", 174f, true, ToggleTrail, true);

        layerMapOptions = CreateLayerOptions(layerPopover.transform, "MapLayers", "MAP STYLE");
        mapStyleOptions.Clear();
        mapStyleOptions.Add(OverlayChip(layerMapOptions, "STREET", 174f, true,
            () => SetBasemap(OpenStreetMapTileLayer.BasemapStyle.Street, 0), true));
        mapStyleOptions.Add(OverlayChip(layerMapOptions, "SATELLITE", 174f, false,
            () => SetBasemap(OpenStreetMapTileLayer.BasemapStyle.Satellite, 1), true));
        mapStyleOptions.Add(OverlayChip(layerMapOptions, "HYBRID", 174f, false,
            () => SetBasemap(OpenStreetMapTileLayer.BasemapStyle.Hybrid, 2), true));

        layerMapOptions.SetActive(false);
        layerPopover.SetActive(false);
    }

    GameObject CreateLayerOptions(Transform parent, string name, string title)
    {
        var content = new GameObject(name, typeof(RectTransform));
        content.transform.SetParent(parent, false);
        Stretch((RectTransform)content.transform, 1f);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 5;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(content.transform, false);
        titleGO.AddComponent<LayoutElement>().preferredHeight = 18;
        var titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = title;
        titleText.fontSize = 11;
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 4;
        titleText.color = TXT_DIM;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.raycastTarget = false;
        return content;
    }

    void BuildWorldAxes(GameObject center)
    {
        worldAxesOverlay = new GameObject("WorldAxes", typeof(RectTransform), typeof(Image));
        worldAxesOverlay.transform.SetParent(center.transform, false);
        var image = worldAxesOverlay.GetComponent<Image>();
        image.color = new Color(BG.r, BG.g, BG.b, 0.78f);
        image.raycastTarget = false;
        StylePanel(worldAxesOverlay);
        var rt = (RectTransform)worldAxesOverlay.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-12, 12);
        rt.sizeDelta = new Vector2(98, 86);

        Vector2 origin = new Vector2(35, 22);
        CreateAxisArrow(worldAxesOverlay.transform, "X", origin, new Vector2(78, 22), new Color(0.95f, 0.28f, 0.28f));
        CreateAxisArrow(worldAxesOverlay.transform, "Y", origin, new Vector2(35, 68), new Color(0.25f, 0.90f, 0.48f));
        CreateAxisArrow(worldAxesOverlay.transform, "Z", origin, new Vector2(15, 43), new Color(0.28f, 0.55f, 1f));

        var originGO = new GameObject("Origin", typeof(RectTransform), typeof(Image));
        originGO.transform.SetParent(worldAxesOverlay.transform, false);
        var originImage = originGO.GetComponent<Image>();
        originImage.sprite = DroneUIFX.CircleSprite;
        originImage.color = TXT;
        originImage.raycastTarget = false;
        var originRT = originImage.rectTransform;
        originRT.anchorMin = originRT.anchorMax = Vector2.zero;
        originRT.pivot = new Vector2(0.5f, 0.5f);
        originRT.anchoredPosition = origin;
        originRT.sizeDelta = new Vector2(5, 5);
    }

    void CreateAxisArrow(Transform parent, string label, Vector2 start, Vector2 end, Color color)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x);
        CreateAxisSegment(parent, label + "Axis", start, end, 2.5f, color);
        CreateAxisSegment(parent, label + "ArrowA", end, end - direction * 7f + normal * 4f, 2.5f, color);
        CreateAxisSegment(parent, label + "ArrowB", end, end - direction * 7f - normal * 4f, 2.5f, color);

        var labelGO = new GameObject(label + "Label", typeof(RectTransform));
        labelGO.transform.SetParent(parent, false);
        var text = labelGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 11;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = end + direction * 7f;
        rt.sizeDelta = new Vector2(16, 14);
    }

    void CreateAxisSegment(Transform parent, string name, Vector2 start, Vector2 end, float width, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = start;
        Vector2 delta = end - start;
        rt.sizeDelta = new Vector2(delta.magnitude, width);
        rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    void BuildViewportGpsReadout(GameObject center)
    {
        var panel = new GameObject("ViewportGpsReadout", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(center.transform, false);
        panel.GetComponent<Image>().color = new Color(BG.r, BG.g, BG.b, 0.84f);
        StylePanel(panel);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(10, 10);
        rt.sizeDelta = new Vector2(220, 82);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 7, 7);
        layout.spacing = 2;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        ui.hudLatText = ViewportGpsRow(panel, "LAT", "--");
        ui.hudLonText = ViewportGpsRow(panel, "LON", "--");
        ui.hudGpsAltText = ViewportGpsRow(panel, "ALT (GPS)", "-- m");
    }

    TextMeshProUGUI ViewportGpsRow(GameObject parent, string label, string initial)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent.transform, false);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        labelGO.AddComponent<LayoutElement>().preferredWidth = 76;
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 10.5f;
        labelText.fontStyle = FontStyles.Bold;
        labelText.characterSpacing = 3;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(row.transform, false);
        valueGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = initial;
        value.fontSize = 13;
        value.fontStyle = FontStyles.Bold;
        value.color = TXT;
        value.alignment = TextAlignmentOptions.MidlineRight;
        return value;
    }

    GameObject FixedOverlayRow(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0, 0, 0, 0);
        image.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(0, 0, 0, 0);
        hl.spacing = 4;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        return go;
    }

    DroneUIFX.AeroButtonHoverFX OverlayChip(GameObject parent, string label, float width, bool selected,
        UnityEngine.Events.UnityAction callback, bool interactable)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 30;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (callback != null) button.onClick.AddListener(callback);
        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 10.5f;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 3;
        text.color = interactable ? TXT_SEC : TXT_DIM;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Stretch(text.rectTransform);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.SetSelected(selected);
        textGO.transform.SetAsLastSibling();
        return style;
    }

    DroneUIFX.AeroButtonHoverFX OverlayIconButton(GameObject parent, DroneUIFX.IconType icon, UnityEngine.Events.UnityAction callback,
        bool warning, bool interactable)
    {
        var go = new GameObject(icon.ToString(), typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 34;
        layout.preferredHeight = 34;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (callback != null) button.onClick.AddListener(callback);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, warning ? AMBER : ACCENT);
        var iconImage = DroneUIFX.CreateIcon(go.transform, icon, 17f,
            interactable ? warning ? AMBER : TXT : TXT_DIM);
        var iconRT = iconImage.rectTransform;
        iconRT.anchorMin = iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(17, 17);
        iconImage.transform.SetAsLastSibling();
        style.foreground = iconImage;
        style.normalForegroundColor = interactable ? warning ? AMBER : TXT : TXT_DIM;
        style.SetSelected(false);
        return style;
    }

    void SetViewportMode(DroneViewportCameraController.ViewMode mode, int selectedIndex)
    {
        viewportCamera?.SetMode(mode);
        for (int i = 0; i < cameraModeStyles.Count; i++)
            cameraModeStyles[i]?.SetSelected(i == selectedIndex);
        Debug.Log("[DashboardUI] Camera: " + mode);
    }

    void SetPrimaryView(bool showReplay)
    {
        if (showReplay && viewportFullscreen) ToggleViewportFullscreen();
        CloseLayerPopover();
        if (liveContentRoot != null) liveContentRoot.SetActive(!showReplay);
        if (replayContentRoot != null) replayContentRoot.SetActive(showReplay);
        liveNavStyle?.SetSelected(!showReplay);
        replayNavStyle?.SetSelected(showReplay);
        Debug.Log("[DashboardUI] Primary view: " + (showReplay ? "REPLAY" : "LIVE"));
    }

    void SetMapView(bool showMap)
    {
        showingMap = showMap;
        CloseLayerPopover();
        if (droneViewLayer != null) droneViewLayer.SetActive(!showMap);
        if (mapViewLayer != null) mapViewLayer.SetActive(showMap);
        if (showMap && mapView != null)
        {
            DroneData latest = rx != null ? rx.latestData : null;
            if (latest != null && latest.timestamp > 0.0)
                mapView.SetTelemetry(
                    latest.x, latest.y, latest.lat, latest.lon, latest.yaw,
                    rx.PositionFresh, rx.GpsFixValid, rx.AttitudeFresh,
                    rx.HomePositionValid,
                    latest.homeNorth, latest.homeEast, latest.homeLat, latest.homeLon);
            mapView.Refresh();
        }
        if (cameraModesOverlay != null) cameraModesOverlay.SetActive(!showMap);
        if (mapControlsOverlay != null) mapControlsOverlay.SetActive(showMap);
        if (worldAxesOverlay != null) worldAxesOverlay.SetActive(!showMap);
        if (viewModeStyles.Count >= 2)
        {
            viewModeStyles[0]?.SetSelected(!showMap);
            viewModeStyles[1]?.SetSelected(showMap);
        }
        Debug.Log("[DashboardUI] View: " + (showMap ? "MAP" : "3D"));
    }

    void ToggleLayerPopover()
    {
        if (layerPopover == null) return;
        layerPopoverOpen = !layerPopoverOpen;
        layer3DOptions?.SetActive(!showingMap);
        layerMapOptions?.SetActive(showingMap);
        ((RectTransform)layerPopover.transform).sizeDelta = new Vector2(196, showingMap ? 147 : 112);
        layerPopover.SetActive(layerPopoverOpen);
        if (layerPopoverOpen) layerPopover.transform.SetAsLastSibling();
        layersStyle?.SetSelected(layerPopoverOpen);
    }

    void CloseLayerPopover()
    {
        layerPopoverOpen = false;
        if (layerPopover != null) layerPopover.SetActive(false);
        layersStyle?.SetSelected(false);
    }

    void ToggleGrid()
    {
        gridVisible = !gridVisible;
        DroneWorldGrid.SetVisible(gridVisible);
        gridLayerStyle?.SetSelected(gridVisible);
        Debug.Log("[DashboardUI] World grid: " + (gridVisible ? "ON" : "OFF"));
    }

    void ToggleTrail()
    {
        trailVisible = !trailVisible;
        DroneTrail[] trails = FindObjectsByType<DroneTrail>(FindObjectsSortMode.None);
        for (int i = 0; i < trails.Length; i++)
        {
            var renderer = trails[i].GetComponent<LineRenderer>();
            if (renderer != null) renderer.enabled = trailVisible;
        }
        trailLayerStyle?.SetSelected(trailVisible);
        Debug.Log("[DashboardUI] Flight trail: " + (trailVisible ? "ON" : "OFF"));
    }

    void SetBasemap(OpenStreetMapTileLayer.BasemapStyle style, int selectedIndex)
    {
        mapTileLayer?.SetBasemap(style);
        for (int i = 0; i < mapStyleOptions.Count; i++)
            mapStyleOptions[i]?.SetSelected(i == selectedIndex);
        Debug.Log("[DashboardUI] Basemap: " + style);
    }

    void ToggleSettingsPanel()
    {
        if (settingsPanel == null) return;
        CloseLayerPopover();
        bool show = !settingsPanel.activeSelf;
        settingsPanel.SetActive(show);
        if (show) settingsPanel.transform.SetAsLastSibling();
        settingsStyle?.SetSelected(show);
        Debug.Log("[DashboardUI] Settings: " + (show ? "OPEN" : "CLOSED"));
    }

    void ToggleViewportFullscreen()
    {
        if (centerViewport == null || dashboardCanvas == null) return;

        if (!viewportFullscreen)
        {
            viewportAnchorMin = centerViewport.anchorMin;
            viewportAnchorMax = centerViewport.anchorMax;
            viewportOffsetMin = centerViewport.offsetMin;
            viewportOffsetMax = centerViewport.offsetMax;
            viewportSiblingIndex = centerViewport.GetSiblingIndex();
            centerViewport.anchorMin = Vector2.zero;
            centerViewport.anchorMax = Vector2.one;
            centerViewport.offsetMin = new Vector2(OUTER_PAD, OUTER_PAD);
            // Fullscreen keeps the flight-critical top status bar visible.
            centerViewport.offsetMax = new Vector2(-OUTER_PAD, -TOP_H - GAP);
            centerViewport.SetAsLastSibling();
            viewportFullscreen = true;
            fullscreenStyle?.SetSelected(true);
        }
        else
        {
            centerViewport.anchorMin = viewportAnchorMin;
            centerViewport.anchorMax = viewportAnchorMax;
            centerViewport.offsetMin = viewportOffsetMin;
            centerViewport.offsetMax = viewportOffsetMax;
            centerViewport.SetSiblingIndex(viewportSiblingIndex);
            viewportFullscreen = false;
            fullscreenStyle?.SetSelected(false);
        }
        Debug.Log("[DashboardUI] Fullscreen viewport: " + (viewportFullscreen ? "ON" : "OFF"));
    }

    void BuildSettingsPanel(GameObject center)
    {
        settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        settingsPanel.transform.SetParent(dashboardCanvas.transform, false);
        settingsPanel.GetComponent<Image>().color = CARD;
        StylePanel(settingsPanel);
        var rt = (RectTransform)settingsPanel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-18, -TOP_H - GAP);
        rt.sizeDelta = new Vector2(310, 192);

        var layout = settingsPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 9, 10);
        layout.spacing = 5;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        SectionHeader(settingsPanel, "SYSTEM / CONNECTION", DroneUIFX.IconType.Settings);
        InfoRow(settingsPanel, "TELEMETRY UDP", rx != null ? $"{rx.listenAddress}:{rx.listenPort}" : "--");
        InfoRow(settingsPanel, "COMMAND TARGET", rx != null ? $"{rx.commandTargetIP}:{rx.commandPort}" : "--");
        InfoRow(settingsPanel, "BATTERY WARNING", $"{ui.batteryLowPercent}%");
        InfoRow(settingsPanel, "VIBRATION ALERT", $"{ui.vibCriticalThreshold:0} m/s²");
        MakeAeroButton(settingsPanel, "CLOSE", BTN_MODE_ACCENT, false, ToggleSettingsPanel, 9, 28);
        settingsPanel.SetActive(false);
    }

    void InfoRow(GameObject parent, string label, string value)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().preferredHeight = 22;
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        labelGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 10.5f;
        labelText.characterSpacing = 3;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(row.transform, false);
        valueGO.AddComponent<LayoutElement>().preferredWidth = 150;
        var valueText = valueGO.AddComponent<TextMeshProUGUI>();
        valueText.text = value;
        valueText.fontSize = 11.5f;
        valueText.fontStyle = FontStyles.Bold;
        valueText.color = TXT;
        valueText.alignment = TextAlignmentOptions.MidlineRight;
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
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -102);
        rt.sizeDelta = new Vector2(560, 30);
        var txt = new GameObject("AlarmText", typeof(RectTransform));
        txt.transform.SetParent(go.transform, false);
        var t = txt.AddComponent<TextMeshProUGUI>();
        t.text = ""; t.fontSize = 11; t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 6; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        ui.alarmBannerText = t;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    void BuildBottomBar(GameObject bar)
    {
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 7;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandHeight = true;
        hl.childForceExpandWidth = true;

        TextMeshProUGUI v1, t1;
        var g1 = DroneUIFX.CreateGraphCard(bar.transform, "ALTITUDE  (m)", "m", ACCENT, out v1, out t1);
        g1.autoScale = true;
        g1.thickness = 2.1f;
        g1.fillColor = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.10f);
        fx.widgets.Add(g1);
        ui.altitudeGraph = g1; ui.altitudeGraphValue = v1;

        TextMeshProUGUI v2, t2;
        var g2 = DroneUIFX.CreateGraphCard(bar.transform, "GROUND SPEED  (m/s)", "m/s", ACCENT, out v2, out t2);
        g2.autoScale = true;
        g2.manualMin = 0; g2.manualMax = 5;
        g2.fillColor = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.10f);
        fx.widgets.Add(g2);
        ui.speedGraph = g2; ui.speedGraphValue = v2;

        TextMeshProUGUI v3, t3;
        var g3 = DroneUIFX.CreateGraphCard(bar.transform, "BATTERY  (%)", "%", ACCENT, out v3, out t3);
        g3.autoScale = true;
        g3.fillColor = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.10f);
        fx.widgets.Add(g3);
        ui.batteryGraph = g3; ui.batteryGraphValue = v3;

        TextMeshProUGUI v4, t4;
        var g4 = DroneUIFX.CreateGraphCard(bar.transform, "LINK LATENCY  (ms)", "ms", ACCENT, out v4, out t4);
        g4.autoScale = true;
        g4.fillColor = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.10f);
        fx.widgets.Add(g4);
        ui.latencyGraph = g4; ui.latencyGraphValue = v4;
    }

    /// Blocking-style confirm dialog for destructive actions (rendered once per use, destroyed after).
    /// Only fires `onConfirm` if the user presses CONFIRM.
    void ConfirmDialog(string title, string message, string confirmLabel, UnityEngine.Events.UnityAction onConfirm)
    {
        var canvas = dashboardCanvas != null ? dashboardCanvas : GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[DashboardUI] Cannot show force-disarm confirmation because the dashboard Canvas is unavailable.");
            return;
        }
        var root = new GameObject("ConfirmDialog", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)root.transform);
        Debug.Log("[DashboardUI] Confirmation opened: " + title);

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
        vl.padding = new RectOffset(11, 11, 10, 10);
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
        Color fillColor = image.color;
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = BORDER;

        var fill = new GameObject("PanelFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(panel.transform, false);
        fill.transform.SetAsFirstSibling();
        var fillImage = fill.GetComponent<Image>();
        fillImage.sprite = DroneUIFX.RoundedRectSprite;
        fillImage.type = Image.Type.Sliced;
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;
        fill.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)fill.transform, 1f);
    }

    TextMeshProUGUI VerticalStat(GameObject parent, string label, string initial)
    {
        var stat = new GameObject(label + "Stat", typeof(RectTransform));
        stat.transform.SetParent(parent.transform, false);
        stat.AddComponent<LayoutElement>().flexibleHeight = 1;
        var vl = stat.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(7, 2, 2, 2);
        vl.spacing = 0;
        vl.childForceExpandHeight = true;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(stat.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 11;
        labelText.characterSpacing = 4;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(stat.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = initial;
        value.fontSize = 17;
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
        dotLE.preferredWidth = 9;
        dotLE.preferredHeight = 9;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(row.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 10.5f;
        labelText.characterSpacing = 3;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        var labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 126;
        labelLE.flexibleWidth = 1;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(row.transform, false);
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = "WAIT";
        value.fontSize = 10.5f;
        value.fontStyle = FontStyles.Bold;
        value.characterSpacing = 3;
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

    void SectionHeader(GameObject parent, string title, DroneUIFX.IconType icon)
    {
        var go = new GameObject("Hdr_" + title, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 26;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft; hl.spacing = 7;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(go.transform, false);
        var lineLayout = line.AddComponent<LayoutElement>();
        lineLayout.preferredWidth = 3;
        lineLayout.preferredHeight = 22;
        line.GetComponent<Image>().color = ACCENT;

        var iconPlate = new GameObject("IconPlate", typeof(RectTransform), typeof(Image));
        iconPlate.transform.SetParent(go.transform, false);
        var iconPlateImage = iconPlate.GetComponent<Image>();
        iconPlateImage.sprite = DroneUIFX.RoundedRectSprite;
        iconPlateImage.type = Image.Type.Sliced;
        iconPlateImage.color = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.12f);
        iconPlateImage.raycastTarget = false;
        var iconPlateLayout = iconPlate.AddComponent<LayoutElement>();
        iconPlateLayout.preferredWidth = 22;
        iconPlateLayout.preferredHeight = 22;
        iconPlateLayout.flexibleWidth = 0;
        iconPlateLayout.flexibleHeight = 0;
        var iconImage = DroneUIFX.CreateIcon(iconPlate.transform, icon, 16f, ACCENT);
        var iconRect = iconImage.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(16, 16);

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        txtGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = title; txt.fontSize = 13; txt.characterSpacing = 4;
        txt.fontStyle = FontStyles.Bold; txt.color = TXT;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 11;
        txt.fontSizeMax = 13;
    }

    TextMeshProUGUI CompactInlineMetric(GameObject parent, string label)
    {
        var metric = new GameObject(label + "Metric", typeof(RectTransform));
        metric.transform.SetParent(parent.transform, false);
        metric.AddComponent<LayoutElement>().flexibleWidth = 1;
        var layout = metric.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(metric.transform, false);
        labelGO.AddComponent<LayoutElement>().preferredWidth = 13;
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 11;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TXT_SEC;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(metric.transform, false);
        valueGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var value = valueGO.AddComponent<TextMeshProUGUI>();
        value.text = "--";
        value.fontSize = 16;
        value.fontStyle = FontStyles.Bold;
        value.color = ACCENT;
        value.alignment = TextAlignmentOptions.MidlineLeft;
        return value;
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
        hl.spacing = 5; hl.childForceExpandWidth = false;
        hl.childAlignment = TextAnchor.MiddleLeft;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(row.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label; lbl.fontSize = 11.5f; lbl.characterSpacing = 2;
        lbl.fontStyle = FontStyles.Bold; lbl.color = prominent ? TXT : TXT_SEC;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        var lle = lblGO.AddComponent<LayoutElement>(); lle.preferredWidth = labelW; lle.flexibleWidth = 0;

        var valGO = new GameObject("Value", typeof(RectTransform));
        valGO.transform.SetParent(row.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--";
        val.fontSize = prominent ? 19 : 15;
        val.fontStyle = FontStyles.Bold;
        val.color = prominent ? NUM : NUM;
        val.alignment = TextAlignmentOptions.MidlineRight;
        val.fontSizeMin = prominent ? 15 : 12;
        val.fontSizeMax = prominent ? 19 : 15;
        val.enableAutoSizing = true;
        valGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        if (!string.IsNullOrEmpty(unit))
        {
            var unitGO = new GameObject("Unit", typeof(RectTransform));
            unitGO.transform.SetParent(row.transform, false);
            var unitText = unitGO.AddComponent<TextMeshProUGUI>();
            unitText.text = unit;
            unitText.fontSize = 9.5f;
            unitText.color = TXT_SEC;
            unitText.alignment = TextAlignmentOptions.MidlineRight;
            var unitLayout = unitGO.AddComponent<LayoutElement>();
            unitLayout.preferredWidth = unit == "m/s" ? 30 : unit == "ms" ? 22 : 16;
        }

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
        lbl.text = label; lbl.fontSize = 10.5f; lbl.characterSpacing = 4; lbl.fontStyle = FontStyles.Bold;
        lbl.color = TXT_SEC; lbl.alignment = TextAlignmentOptions.Center;
        var valGO = new GameObject("Val"); valGO.transform.SetParent(col.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "--"; val.fontSize = 17; val.fontStyle = FontStyles.Bold;
        val.color = NUM; val.alignment = TextAlignmentOptions.Center;
        if (!string.IsNullOrEmpty(unit))
        {
            var unitGO = new GameObject("Unit", typeof(RectTransform));
            unitGO.transform.SetParent(col.transform, false);
            var unitText = unitGO.AddComponent<TextMeshProUGUI>();
            unitText.text = unit;
            unitText.fontSize = 9.5f;
            unitText.color = TXT_SEC;
            unitText.alignment = TextAlignmentOptions.Center;
        }
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

    DroneUIFX.AeroButtonHoverFX MakeAeroButton(GameObject parent, string label, Color accent, bool destructive,
        UnityEngine.Events.UnityAction cb, float fontSize = 13, float height = 42,
        DroneUIFX.IconType? icon = null)
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

        var style = DroneUIFX.ApplyAeroButtonStyle(go, accent, destructive);
        if (icon.HasValue)
        {
            var iconImage = DroneUIFX.CreateIcon(go.transform, icon.Value, 14f, destructive ? RED : TXT_SEC);
            var iconRT = iconImage.rectTransform;
            iconRT.anchorMin = iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.anchoredPosition = new Vector2(10, 0);
            iconRT.sizeDelta = new Vector2(14, 14);
        }
        txtGO.transform.SetAsLastSibling();
        return style;
    }

    void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset,inset); rt.offsetMax = new Vector2(-inset,-inset);
    }
    void Stretch(RectTransform rt) => Stretch(rt, 0f);

    void SendCmd(string cmd)
    {
        if (rx != null)
        {
            if (rx.SendCommand(cmd)) Debug.Log("[DashboardUI] Command requested: " + cmd);
        }
        else Debug.LogError("[DashboardUI] No receiver for CMD " + cmd);
    }
}
