using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built REPLAY library. The records in this view are deliberately seeded
/// presentation data until a persistent flight recorder is connected.
/// </summary>
[DisallowMultipleComponent]
public sealed class FlightRecordsView : MonoBehaviour
{
    static Color BG => DroneUIFX.AERO_BG;
    static Color PANEL => DroneUIFX.AERO_PANEL;
    static Color CARD => DroneUIFX.AERO_CARD;
    static Color CARD2 => DroneUIFX.AERO_CARD2;
    static Color BORDER => DroneUIFX.AERO_BORDER;
    static Color TEXT => DroneUIFX.AERO_TEXT;
    static Color TEXT_SEC => DroneUIFX.AERO_TEXT_SEC;
    static Color TEXT_DIM => DroneUIFX.AERO_TEXT_DIM;
    static Color ACCENT => DroneUIFX.AERO_ACCENT;
    static Color AMBER => DroneUIFX.AERO_AMBER;
    static Color RED => DroneUIFX.AERO_RED;
    static Color GREEN => DroneUIFX.AERO_GREEN;

    enum QuickFilter
    {
        All,
        Autonomous,
        Manual,
        Reflight,
        Warnings
    }

    enum FilterKind
    {
        Date,
        Drone,
        Location,
        Mission,
        Type,
        Status,
        Warnings
    }

    sealed class FlightRecord
    {
        public string id;
        public long dateKey;
        public DateTime recordedAt;
        public string date;
        public string dateFilter;
        public string location;
        public string city;
        public string drone;
        public string mission;
        public string missionFilter;
        public string type;
        public string duration;
        public string distance;
        public string maxAltitude;
        public string battery;
        public int warnings;
        public string status;
        public string operatorName;
        public string sourceFlight;
    }

    sealed class FilterControl
    {
        public FilterKind kind;
        public string title;
        public string[] options;
        public readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public TextMeshProUGUI label;
        public RectTransform buttonRect;
        public DateTime? dateFrom;
        public DateTime? dateTo;

        public bool IsActive => kind == FilterKind.Date
            ? dateFrom.HasValue || dateTo.HasValue
            : selected.Count > 0;
    }

    sealed class CheckVisual
    {
        public Image background;
        public Image mark;
    }

    sealed class QuickControl
    {
        public QuickFilter filter;
        public DroneUIFX.AeroButtonHoverFX style;
    }

    sealed class CalendarDayVisual
    {
        public DateTime date;
        public Image background;
        public TextMeshProUGUI label;
    }

    sealed class DatePickerState
    {
        public FilterControl filter;
        public DateTime? from;
        public DateTime? to;
        public DateTime displayedMonth;
        public bool editingFrom = true;
        public Image fromBackground;
        public Image toBackground;
        public TextMeshProUGUI fromValue;
        public TextMeshProUGUI toValue;
        public TextMeshProUGUI monthLabel;
        public readonly List<CalendarDayVisual> days = new List<CalendarDayVisual>(42);
    }

    readonly List<FlightRecord> records = new List<FlightRecord>();
    readonly List<FlightRecord> visibleRecords = new List<FlightRecord>();
    readonly HashSet<string> selectedIds = new HashSet<string>();
    readonly List<FilterControl> filters = new List<FilterControl>();
    readonly List<QuickControl> quickControls = new List<QuickControl>();

    readonly float[] columnWeights =
    {
        42f, 92f, 138f, 145f, 88f, 218f, 112f,
        92f, 82f, 102f, 114f, 72f, 108f, 46f
    };

    bool built;
    bool newestFirst = true;
    QuickFilter quickFilter = QuickFilter.All;
    string searchTerm = string.Empty;
    string requestedReplayId = string.Empty;
    int rowsPerPage = 25;

    RectTransform tableBody;
    TextMeshProUGUI selectedCountText;
    TextMeshProUGUI paginationText;
    TextMeshProUGUI rowsPerPageText;
    TextMeshProUGUI statusText;
    Image dateSortIcon;
    CheckVisual selectAllVisual;
    Button compareButton;
    Button exportButton;
    TMP_InputField searchInput;

    GameObject actionMenu;
    GameObject sourceFlightAction;
    TextMeshProUGUI actionMenuTitle;
    FlightRecord menuRecord;

    GameObject filterPopoverLayer;
    FilterControl openFilter;
    RectTransform openPopoverRect;
    string exportDirectory;

    public void Build()
    {
        if (built) return;
        built = true;

        SeedRecords();
        PrepareExportDirectory();
        selectedIds.Add("FL-00382");
        selectedIds.Add("FL-00381");

        var background = gameObject.GetComponent<Image>();
        if (background == null) background = gameObject.AddComponent<Image>();
        background.color = BG;

        var rootLayout = gameObject.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(14, 14, 13, 12);
        rootLayout.spacing = 8;
        rootLayout.childAlignment = TextAnchor.UpperLeft;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        BuildHeader();
        BuildFilterRow();
        BuildQuickFilters();
        BuildTable();
        BuildActionBar();
        BuildStatusStrip();
        BuildActionMenu();

        ApplyFilters(false);
        SetStatus("SEEDED UI DEMO · 10 sample records · persistent flight recorder unavailable", false);
    }

    void OnDisable()
    {
        if (actionMenu != null) actionMenu.SetActive(false);
        CloseFilterPopover();
    }

    void Update()
    {
        if (openPopoverRect == null || !Input.GetMouseButtonDown(0)) return;

        Canvas canvas = openPopoverRect.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 pointer = Input.mousePosition;
        if (RectTransformUtility.RectangleContainsScreenPoint(openPopoverRect, pointer, eventCamera)) return;

        // Filter buttons handle their own toggle/switch on mouse-up. Leaving them open
        // until then prevents the currently open filter from immediately reopening.
        for (int i = 0; i < filters.Count; i++)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(filters[i].buttonRect, pointer, eventCamera))
                return;
        }

        CloseFilterPopover();
    }

    void BuildHeader()
    {
        var header = CreateRect("Header", transform, 88f);
        var row = header.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;

        var titleBlock = CreateRect("TitleBlock", header.transform, 84f);
        var titleLayout = titleBlock.GetComponent<LayoutElement>();
        titleLayout.preferredWidth = 0;
        titleLayout.flexibleWidth = 1;
        var titleColumn = titleBlock.AddComponent<VerticalLayoutGroup>();
        titleColumn.padding = new RectOffset(7, 8, 8, 6);
        titleColumn.spacing = 2;
        titleColumn.childAlignment = TextAnchor.MiddleLeft;
        titleColumn.childForceExpandHeight = false;

        var title = CreateText(titleBlock.transform, "Title", "Flight Records", 30f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;

        var subtitle = CreateText(titleBlock.transform, "Subtitle",
            "Review, search, export, compare, and reuse historical drone flights.", 13f, TEXT_SEC,
            TextAlignmentOptions.MidlineLeft, false);
        subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

        CreateMetric(header.transform, "248", "TOTAL FLIGHTS");
        CreateMetric(header.transform, "63h 42m", "FLIGHT TIME");
        CreateMetric(header.transform, "1,284 km", "DISTANCE FLOWN");
        CreateMetric(header.transform, "12", "TOTAL DRONES");
    }

    void CreateMetric(Transform parent, string value, string caption)
    {
        var card = CreateSurface("Metric_" + caption, parent, CARD, 166f, 70f);
        var cardElement = card.GetComponent<LayoutElement>();
        cardElement.minWidth = 166f;
        cardElement.preferredWidth = 166f;
        cardElement.flexibleWidth = 0f;
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 10, 8, 7);
        layout.spacing = 0;
        layout.childForceExpandHeight = false;

        var valueText = CreateText(card.transform, "Value", value, 20f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        valueText.enableAutoSizing = true;
        valueText.fontSizeMin = 14;
        valueText.fontSizeMax = 20;
        valueText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;

        var captionText = CreateText(card.transform, "Caption", caption, 9.5f, TEXT_SEC,
            TextAlignmentOptions.MidlineLeft, true);
        captionText.characterSpacing = 2;
        captionText.enableAutoSizing = true;
        captionText.fontSizeMin = 7.5f;
        captionText.fontSizeMax = 9.5f;
        captionText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
    }

    void BuildFilterRow()
    {
        var filterRow = CreateRect("SearchAndFilters", transform, 48f);
        var layout = filterRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 7;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        BuildSearchField(filterRow.transform);
        CreateFilter(filterRow.transform, FilterKind.Date, "DATE RANGE", 190f,
            DroneUIFX.IconType.Calendar, Array.Empty<string>());
        CreateFilter(filterRow.transform, FilterKind.Drone, "DRONE", 126f,
            DroneUIFX.IconType.Drone,
            new[] { "DRN-002", "DRN-003", "DRN-004", "DRN-005", "DRN-007", "DRN-008", "DRN-009", "DRN-010", "DRN-011" });
        CreateFilter(filterRow.transform, FilterKind.Location, "LOCATION", 142f,
            DroneUIFX.IconType.Gps,
            new[] { "BENGALURU", "CHENNAI", "COIMBATORE", "HYDERABAD", "KOCHI", "MYSURU", "PUNE", "VISAKHAPATNAM" });
        CreateFilter(filterRow.transform, FilterKind.Mission, "MISSION", 142f,
            DroneUIFX.IconType.Map,
            new[] { "COASTAL MAPPING", "INFRASTRUCTURE SURVEY", "PERIMETER ROUTE B", "PERIMETER ROUTE C", "PORT SURVEY", "SOLAR INSPECTION ROUTE A", "THERMAL INSPECTION", "UNASSIGNED", "WIND TURBINE SURVEY" });
        CreateFilter(filterRow.transform, FilterKind.Type, "FLIGHT TYPE", 158f,
            DroneUIFX.IconType.Layers, new[] { "AUTONOMOUS", "MANUAL", "RE-FLIGHT" });
        CreateFilter(filterRow.transform, FilterKind.Status, "STATUS", 132f,
            DroneUIFX.IconType.System, new[] { "COMPLETED", "ABORTED", "FAILED" });
        CreateFilter(filterRow.transform, FilterKind.Warnings, "WARNINGS", 148f,
            DroneUIFX.IconType.Warning, new[] { "WITH WARNINGS", "NO WARNINGS" });
    }

    void BuildSearchField(Transform parent)
    {
        var search = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        search.transform.SetParent(parent, false);
        var layout = search.AddComponent<LayoutElement>();
        layout.minWidth = 300f;
        layout.preferredWidth = 0f;
        layout.preferredHeight = 43f;
        layout.flexibleWidth = 3.6f;

        var image = search.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;

        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(search.transform, false);
        border.transform.SetAsFirstSibling();
        var borderImage = border.GetComponent<Image>();
        borderImage.sprite = DroneUIFX.RoundedRectSprite;
        borderImage.type = Image.Type.Sliced;
        borderImage.color = BORDER;
        borderImage.raycastTarget = false;
        Stretch((RectTransform)border.transform);

        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(search.transform, false);
        inner.transform.SetSiblingIndex(1);
        var innerImage = inner.GetComponent<Image>();
        innerImage.sprite = DroneUIFX.RoundedRectSprite;
        innerImage.type = Image.Type.Sliced;
        innerImage.color = CARD;
        innerImage.raycastTarget = false;
        Stretch((RectTransform)inner.transform, 1f);

        var marker = DroneUIFX.CreateIcon(search.transform, DroneUIFX.IconType.Search, 19f, ACCENT);
        marker.gameObject.name = "SearchIcon";
        var markerRT = marker.rectTransform;
        markerRT.anchorMin = new Vector2(0, 0);
        markerRT.anchorMax = new Vector2(0, 1);
        markerRT.pivot = new Vector2(0, 0.5f);
        markerRT.anchoredPosition = new Vector2(11, 0);
        markerRT.sizeDelta = new Vector2(19, 0);

        var text = CreateText(search.transform, "Text", string.Empty, 12f, TEXT,
            TextAlignmentOptions.MidlineLeft, false);
        var textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(38, 3);
        textRT.offsetMax = new Vector2(-10, -3);

        var placeholder = CreateText(search.transform, "Placeholder",
            "Search flights, drones, missions, locations, operators...", 12f, TEXT_DIM,
            TextAlignmentOptions.MidlineLeft, false);
        var placeholderRT = placeholder.rectTransform;
        placeholderRT.anchorMin = Vector2.zero;
        placeholderRT.anchorMax = Vector2.one;
        placeholderRT.offsetMin = new Vector2(38, 3);
        placeholderRT.offsetMax = new Vector2(-10, -3);

        searchInput = search.GetComponent<TMP_InputField>();
        searchInput.targetGraphic = image;
        searchInput.textComponent = text;
        searchInput.placeholder = placeholder;
        searchInput.textViewport = textRT;
        searchInput.lineType = TMP_InputField.LineType.SingleLine;
        searchInput.onValueChanged.AddListener(value =>
        {
            searchTerm = value == null ? string.Empty : value.Trim();
            ApplyFilters();
        });
        text.transform.SetAsLastSibling();
    }

    void CreateFilter(Transform parent, FilterKind kind, string title, float width,
        DroneUIFX.IconType iconType, string[] options)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.preferredHeight = 43f;
        layout.flexibleWidth = 0f;

        var leftIcon = DroneUIFX.CreateIcon(go.transform, iconType, 15f, ACCENT);
        leftIcon.gameObject.name = "FilterIcon";
        AnchorIcon(leftIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(17f, 0f), 15f);

        var label = CreateText(go.transform, "Label", title, 9.5f, TEXT_SEC,
            TextAlignmentOptions.MidlineLeft, true);
        label.enableAutoSizing = true;
        label.fontSizeMin = 7.5f;
        label.fontSizeMax = 9.5f;
        label.characterSpacing = 1.5f;
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(31f, 3f);
        label.rectTransform.offsetMax = new Vector2(-24f, -3f);

        var chevron = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.ChevronDown, 11f, TEXT_SEC);
        chevron.gameObject.name = "Chevron";
        AnchorIcon(chevron.rectTransform, new Vector2(1f, 0.5f), new Vector2(-13f, 0f), 11f);

        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        var control = new FilterControl
        {
            kind = kind,
            title = title,
            options = options,
            label = label,
            buttonRect = (RectTransform)go.transform
        };
        filters.Add(control);
        button.onClick.AddListener(() => ToggleFilterPopover(control));
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.normalTextColor = TEXT_SEC;
        style.foreground = leftIcon;
        style.normalForegroundColor = ACCENT;
        label.transform.SetAsLastSibling();
        chevron.transform.SetAsLastSibling();
    }

    void ToggleFilterPopover(FilterControl filter)
    {
        if (openFilter == filter && filterPopoverLayer != null)
        {
            CloseFilterPopover();
            return;
        }

        CloseFilterPopover();
        openFilter = filter;

        filterPopoverLayer = new GameObject("FilterPopoverLayer", typeof(RectTransform));
        filterPopoverLayer.transform.SetParent(transform, false);
        var layerElement = filterPopoverLayer.AddComponent<LayoutElement>();
        layerElement.ignoreLayout = true;
        var layerRect = (RectTransform)filterPopoverLayer.transform;
        Stretch(layerRect);
        filterPopoverLayer.transform.SetAsLastSibling();

        float width = filter.kind == FilterKind.Date ? 350f
            : filter.kind == FilterKind.Mission ? 286f
            : filter.kind == FilterKind.Location ? 224f
            : Mathf.Max(196f, filter.buttonRect.rect.width);
        float height = filter.kind == FilterKind.Date
            ? 350f
            : 78f + filter.options.Length * 31f;

        var panel = CreateSurface("Popover_" + filter.title, filterPopoverLayer.transform, CARD2, width, height);
        var panelElement = panel.GetComponent<LayoutElement>();
        panelElement.ignoreLayout = true;
        var panelRect = (RectTransform)panel.transform;
        openPopoverRect = panelRect;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(width, height);

        var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(8, 8, 8, 8);
        panelLayout.spacing = 3f;
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        if (filter.kind == FilterKind.Date) BuildDatePopover(panel.transform, filter);
        else BuildOptionsPopover(panel.transform, filter);

        Canvas.ForceUpdateCanvases();
        PositionPopover(layerRect, panelRect, filter.buttonRect, width, height);
    }

    void PositionPopover(RectTransform layer, RectTransform panel, RectTransform button, float width, float height)
    {
        var corners = new Vector3[4];
        button.GetWorldCorners(corners);
        Vector3 bottomLeft = layer.InverseTransformPoint(corners[0]);
        Vector3 topLeft = layer.InverseTransformPoint(corners[1]);
        Rect bounds = layer.rect;
        float x = Mathf.Clamp(bottomLeft.x, bounds.xMin + 4f, bounds.xMax - width - 4f);
        float y = bottomLeft.y - 4f;
        if (y - height < bounds.yMin + 4f)
            y = Mathf.Min(bounds.yMax - 4f, topLeft.y + height + 4f);
        panel.localPosition = new Vector3(x, y, 0f);
    }

    void BuildOptionsPopover(Transform parent, FilterControl filter)
    {
        var title = CreateText(parent, "Title", filter.title, 9.5f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        title.characterSpacing = 1.5f;
        SetFixedHeight(title.gameObject.AddComponent<LayoutElement>(), 24f);

        var visuals = new List<CheckVisual>();
        for (int i = 0; i < filter.options.Length; i++)
        {
            string option = filter.options[i];
            var row = new GameObject(option, typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(parent, false);
            SetFixedHeight(row.AddComponent<LayoutElement>(), 28f);
            var rowImage = row.GetComponent<Image>();
            rowImage.color = new Color(CARD.r, CARD.g, CARD.b, 0.76f);
            var rowButton = row.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;

            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(7, 7, 4, 4);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            CheckVisual check = CreatePassiveCheckbox(row.transform, filter.selected.Contains(option));
            visuals.Add(check);
            var optionText = CreateText(row.transform, "Label", option, 9.5f, TEXT_SEC,
                TextAlignmentOptions.MidlineLeft, false);
            var optionLayout = optionText.gameObject.AddComponent<LayoutElement>();
            optionLayout.flexibleWidth = 1f;
            optionLayout.preferredHeight = 20f;

            rowButton.onClick.AddListener(() =>
            {
                bool selected = filter.selected.Add(option);
                if (!selected) filter.selected.Remove(option);
                SetCheckbox(check, selected);
                UpdateFilterLabel(filter);
                ApplyFilters(false);
                SetStatus(filter.title + ": " + filter.selected.Count + " selected", false);
            });
        }

        var footer = CreatePopoverFooter(parent);
        CreatePopoverButton(footer.transform, "CLEAR", 82f, () =>
        {
            filter.selected.Clear();
            for (int i = 0; i < visuals.Count; i++) SetCheckbox(visuals[i], false);
            UpdateFilterLabel(filter);
            ApplyFilters(false);
            SetStatus(filter.title + " filter cleared", false);
        });
        AddFlexibleSpacer(footer.transform);
        CreatePopoverButton(footer.transform, "DONE", 82f, CloseFilterPopover);
    }

    void BuildDatePopover(Transform parent, FilterControl filter)
    {
        var title = CreateText(parent, "Title", "DATE RANGE", 9.5f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        title.characterSpacing = 1.5f;
        SetFixedHeight(title.gameObject.AddComponent<LayoutElement>(), 18f);

        var state = new DatePickerState
        {
            filter = filter,
            from = filter.dateFrom,
            to = filter.dateTo
        };
        DateTime initialDate = state.from ?? state.to ?? new DateTime(2026, 9, 1);
        state.displayedMonth = new DateTime(initialDate.Year, initialDate.Month, 1);

        BuildDateEndpointRow(parent, state);
        BuildCalendarMonthHeader(parent, state);
        BuildCalendarWeekdayHeader(parent);
        BuildCalendarDays(parent, state);

        var footer = CreatePopoverFooter(parent);
        CreatePopoverButton(footer.transform, "CLEAR", 82f, () =>
        {
            state.from = null;
            state.to = null;
            state.editingFrom = true;
            state.displayedMonth = new DateTime(2026, 9, 1);
            filter.dateFrom = null;
            filter.dateTo = null;
            UpdateDatePicker(state);
            UpdateFilterLabel(filter);
            ApplyFilters(false);
            SetStatus("Date range cleared", false);
        });
        AddFlexibleSpacer(footer.transform);
        CreatePopoverButton(footer.transform, "APPLY", 82f, () =>
        {
            NormalizeDateRange(state);
            filter.dateFrom = state.from?.Date;
            filter.dateTo = state.to?.Date;
            UpdateFilterLabel(filter);
            ApplyFilters(false);
            SetStatus("Date range applied", false);
            CloseFilterPopover();
        });

        UpdateDatePicker(state);
    }

    void BuildDateEndpointRow(Transform parent, DatePickerState state)
    {
        var row = CreateRect("DateEndpoints", parent, 42f);
        SetFixedHeight(row.GetComponent<LayoutElement>(), 42f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        CreateDateEndpointButton(row.transform, "FROM", true, state);
        CreateDateEndpointButton(row.transform, "TO", false, state);
    }

    void CreateDateEndpointButton(Transform parent, string caption, bool editsFrom, DatePickerState state)
    {
        var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = 164f;
        element.preferredWidth = 164f;
        element.flexibleWidth = 0f;
        SetFixedHeight(element, 40f);

        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        var captionText = CreateText(go.transform, "Caption", caption, 8f,
            editsFrom ? ACCENT : AMBER, TextAlignmentOptions.MidlineLeft, true);
        captionText.characterSpacing = 1.5f;
        captionText.rectTransform.anchorMin = new Vector2(0f, 0.48f);
        captionText.rectTransform.anchorMax = Vector2.one;
        captionText.rectTransform.offsetMin = new Vector2(10f, 0f);
        captionText.rectTransform.offsetMax = new Vector2(-8f, -1f);

        var valueText = CreateText(go.transform, "Value", "Select date", 10f, TEXT_DIM,
            TextAlignmentOptions.MidlineLeft, false);
        valueText.rectTransform.anchorMin = Vector2.zero;
        valueText.rectTransform.anchorMax = new Vector2(1f, 0.58f);
        valueText.rectTransform.offsetMin = new Vector2(10f, 1f);
        valueText.rectTransform.offsetMax = new Vector2(-8f, 0f);

        if (editsFrom)
        {
            state.fromBackground = image;
            state.fromValue = valueText;
        }
        else
        {
            state.toBackground = image;
            state.toValue = valueText;
        }

        button.onClick.AddListener(() =>
        {
            state.editingFrom = editsFrom;
            DateTime? selected = editsFrom ? state.from : state.to;
            if (selected.HasValue)
                state.displayedMonth = new DateTime(selected.Value.Year, selected.Value.Month, 1);
            UpdateDatePicker(state);
        });
    }

    void BuildCalendarMonthHeader(Transform parent, DatePickerState state)
    {
        var row = CreateRect("MonthNavigation", parent, 30f);
        SetFixedHeight(row.GetComponent<LayoutElement>(), 30f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        CreateCalendarNavButton(row.transform, "PreviousMonth", -90f, () =>
        {
            state.displayedMonth = state.displayedMonth.AddMonths(-1);
            UpdateDatePicker(state);
        });

        state.monthLabel = CreateText(row.transform, "Month", string.Empty, 10f, TEXT,
            TextAlignmentOptions.Center, true);
        state.monthLabel.characterSpacing = 1.2f;
        var monthElement = state.monthLabel.gameObject.AddComponent<LayoutElement>();
        monthElement.flexibleWidth = 1f;
        SetFixedHeight(monthElement, 28f);

        CreateCalendarNavButton(row.transform, "NextMonth", 90f, () =>
        {
            state.displayedMonth = state.displayedMonth.AddMonths(1);
            UpdateDatePicker(state);
        });
    }

    void CreateCalendarNavButton(Transform parent, string name, float rotation, Action callback)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = 30f;
        element.preferredWidth = 30f;
        element.flexibleWidth = 0f;
        SetFixedHeight(element, 28f);

        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => callback());

        var icon = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.ChevronDown, 10f, TEXT_SEC);
        icon.gameObject.name = "Chevron";
        AnchorIcon(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 10f);
        icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.foreground = icon;
        style.normalForegroundColor = TEXT_SEC;
        icon.transform.SetAsLastSibling();
    }

    void BuildCalendarWeekdayHeader(Transform parent)
    {
        var row = CreateRect("Weekdays", parent, 18f);
        SetFixedHeight(row.GetComponent<LayoutElement>(), 18f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        string[] weekdays = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        for (int i = 0; i < weekdays.Length; i++)
        {
            var label = CreateText(row.transform, weekdays[i], weekdays[i], 7.5f, TEXT_DIM,
                TextAlignmentOptions.Center, true);
            label.characterSpacing = 0.7f;
            var element = label.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 46f;
            element.preferredWidth = 46f;
            element.flexibleWidth = 0f;
            SetFixedHeight(element, 18f);
        }
    }

    void BuildCalendarDays(Transform parent, DatePickerState state)
    {
        for (int week = 0; week < 6; week++)
        {
            var row = CreateRect("Week_" + (week + 1).ToString(CultureInfo.InvariantCulture), parent, 27f);
            SetFixedHeight(row.GetComponent<LayoutElement>(), 27f);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int day = 0; day < 7; day++)
            {
                var go = new GameObject("Day", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(row.transform, false);
                var element = go.AddComponent<LayoutElement>();
                element.minWidth = 46f;
                element.preferredWidth = 46f;
                element.flexibleWidth = 0f;
                SetFixedHeight(element, 27f);

                var image = go.GetComponent<Image>();
                image.sprite = DroneUIFX.RoundedRectSprite;
                image.type = Image.Type.Sliced;
                var label = CreateText(go.transform, "Label", string.Empty, 9f, TEXT,
                    TextAlignmentOptions.Center, false);
                Stretch(label.rectTransform, 1f);

                var visual = new CalendarDayVisual { background = image, label = label };
                state.days.Add(visual);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectCalendarDate(state, visual.date));
            }
        }
    }

    void SelectCalendarDate(DatePickerState state, DateTime date)
    {
        if (state.editingFrom)
        {
            state.from = date.Date;
            if (state.to.HasValue && state.to.Value.Date < state.from.Value.Date)
                state.to = null;
            state.editingFrom = false;
        }
        else
        {
            state.to = date.Date;
            NormalizeDateRange(state);
            state.editingFrom = true;
        }

        state.filter.dateFrom = state.from?.Date;
        state.filter.dateTo = state.to?.Date;
        UpdateFilterLabel(state.filter);
        ApplyFilters(false);
        state.displayedMonth = new DateTime(date.Year, date.Month, 1);
        UpdateDatePicker(state);
    }

    static void NormalizeDateRange(DatePickerState state)
    {
        if (!state.from.HasValue || !state.to.HasValue || state.from.Value.Date <= state.to.Value.Date)
            return;

        DateTime from = state.from.Value.Date;
        state.from = state.to.Value.Date;
        state.to = from;
    }

    void UpdateDatePicker(DatePickerState state)
    {
        state.fromValue.text = state.from.HasValue
            ? state.from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "Select date";
        state.toValue.text = state.to.HasValue
            ? state.to.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "Select date";
        state.fromValue.color = state.from.HasValue ? TEXT : TEXT_DIM;
        state.toValue.color = state.to.HasValue ? TEXT : TEXT_DIM;
        state.fromBackground.color = new Color(ACCENT.r, ACCENT.g, ACCENT.b,
            state.editingFrom ? 0.28f : 0.10f);
        state.toBackground.color = new Color(AMBER.r, AMBER.g, AMBER.b,
            state.editingFrom ? 0.10f : 0.28f);

        state.monthLabel.text = state.displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        int mondayOffset = ((int)state.displayedMonth.DayOfWeek + 6) % 7;
        DateTime gridStart = state.displayedMonth.AddDays(-mondayOffset);
        for (int i = 0; i < state.days.Count; i++)
        {
            CalendarDayVisual visual = state.days[i];
            DateTime date = gridStart.AddDays(i).Date;
            visual.date = date;
            visual.label.text = date.Day.ToString(CultureInfo.InvariantCulture);

            bool inCurrentMonth = date.Month == state.displayedMonth.Month
                && date.Year == state.displayedMonth.Year;
            bool isFrom = state.from.HasValue && date == state.from.Value.Date;
            bool isTo = state.to.HasValue && date == state.to.Value.Date;
            bool inRange = state.from.HasValue && state.to.HasValue
                && date >= state.from.Value.Date && date <= state.to.Value.Date;

            visual.background.color = inCurrentMonth
                ? new Color(CARD.r, CARD.g, CARD.b, 0.58f)
                : new Color(CARD.r, CARD.g, CARD.b, 0.22f);
            visual.label.color = inCurrentMonth ? TEXT_SEC : TEXT_DIM;
            visual.label.fontStyle = FontStyles.Normal;
            if (inRange)
            {
                visual.background.color = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.20f);
                visual.label.color = TEXT;
            }
            if (isFrom && isTo)
            {
                visual.background.color = new Color(GREEN.r, GREEN.g, GREEN.b, 0.88f);
                visual.label.color = BG;
                visual.label.fontStyle = FontStyles.Bold;
            }
            else if (isFrom)
            {
                visual.background.color = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.88f);
                visual.label.color = BG;
                visual.label.fontStyle = FontStyles.Bold;
            }
            else if (isTo)
            {
                visual.background.color = new Color(AMBER.r, AMBER.g, AMBER.b, 0.92f);
                visual.label.color = BG;
                visual.label.fontStyle = FontStyles.Bold;
            }
        }
    }

    GameObject CreatePopoverFooter(Transform parent)
    {
        var footer = CreateRect("Footer", parent, 34f);
        SetFixedHeight(footer.GetComponent<LayoutElement>(), 34f);
        var layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return footer;
    }

    void CreatePopoverButton(Transform parent, string label, float width, Action callback)
    {
        var button = CreateActionButton(parent, label, width, callback);
        SetFixedHeight(button.GetComponent<LayoutElement>(), 30f);
    }

    static void AddFlexibleSpacer(Transform parent)
    {
        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }

    void UpdateFilterLabel(FilterControl filter)
    {
        int count = filter.kind == FilterKind.Date
            ? (filter.dateFrom.HasValue ? 1 : 0) + (filter.dateTo.HasValue ? 1 : 0)
            : filter.selected.Count;
        filter.label.text = count == 0 ? filter.title : filter.title + " (" + count + ")";
    }

    void CloseFilterPopover()
    {
        openFilter = null;
        openPopoverRect = null;
        if (filterPopoverLayer == null) return;
        filterPopoverLayer.SetActive(false);
        Destroy(filterPopoverLayer);
        filterPopoverLayer = null;
    }

    void BuildQuickFilters()
    {
        var quickRow = CreateRect("QuickFilters", transform, 38f);
        var layout = quickRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 7;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        CreateQuickFilter(quickRow.transform, "ALL FLIGHTS", 104f, QuickFilter.All);
        CreateQuickFilter(quickRow.transform, "AUTONOMOUS", 112f, QuickFilter.Autonomous);
        CreateQuickFilter(quickRow.transform, "MANUAL", 82f, QuickFilter.Manual);
        CreateQuickFilter(quickRow.transform, "RE-FLIGHTS", 98f, QuickFilter.Reflight);
        CreateQuickFilter(quickRow.transform, "WITH WARNINGS", 124f, QuickFilter.Warnings);

        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(quickRow.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        var sortHint = CreateText(quickRow.transform, "SortHint", "SORTED: NEWEST FLIGHT FIRST",
            9.5f, TEXT_DIM, TextAlignmentOptions.MidlineRight, true);
        sortHint.characterSpacing = 2;
        var sortLayout = sortHint.gameObject.AddComponent<LayoutElement>();
        sortLayout.preferredWidth = 220;
        sortLayout.preferredHeight = 32;
    }

    void CreateQuickFilter(Transform parent, string label, float width, QuickFilter filter)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = 32;

        var text = CreateText(go.transform, "Text", label, 10f, TEXT_SEC,
            TextAlignmentOptions.Center, true);
        text.characterSpacing = 1;
        Stretch(text.rectTransform, 4f);

        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            quickFilter = filter;
            ApplyFilters(false);
            SetStatus("Quick filter: " + label, false);
        });

        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.normalTextColor = TEXT_SEC;
        style.SetSelected(filter == quickFilter);
        text.transform.SetAsLastSibling();
        quickControls.Add(new QuickControl { filter = filter, style = style });
    }

    void BuildTable()
    {
        var table = CreateSurface("FlightRecordsTable", transform, PANEL, 0f, 0f);
        var tableElement = table.GetComponent<LayoutElement>();
        tableElement.minHeight = 480f;
        tableElement.flexibleHeight = 1f;

        var tableLayout = table.AddComponent<VerticalLayoutGroup>();
        tableLayout.padding = new RectOffset(1, 1, 1, 1);
        tableLayout.spacing = 1;
        tableLayout.childControlWidth = true;
        tableLayout.childControlHeight = true;
        tableLayout.childForceExpandWidth = true;
        tableLayout.childForceExpandHeight = false;

        var header = new GameObject("TableHeader", typeof(RectTransform), typeof(Image));
        header.transform.SetParent(table.transform, false);
        SetFixedHeight(header.AddComponent<LayoutElement>(), 42f);
        header.GetComponent<Image>().color = CARD2;
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        ConfigureTableRowLayout(headerLayout);

        var selectCell = CreateTableCell(header.transform, "Select", 0);
        selectAllVisual = CreateCheckbox(selectCell.transform, false, ToggleSelectAllVisible);
        CreateHeaderCell(header.transform, "FLIGHT ID", 1, false);
        CreateHeaderCell(header.transform, "DATE & TIME", 2, true);
        CreateHeaderCell(header.transform, "LOCATION", 3, false);
        CreateHeaderCell(header.transform, "DRONE", 4, false);
        CreateHeaderCell(header.transform, "MISSION / RUN", 5, false);
        CreateHeaderCell(header.transform, "TYPE", 6, false);
        CreateHeaderCell(header.transform, "DURATION", 7, false);
        CreateHeaderCell(header.transform, "DISTANCE", 8, false);
        CreateHeaderCell(header.transform, "MAX ALT.", 9, false);
        CreateHeaderCell(header.transform, "BATTERY", 10, false);
        CreateHeaderCell(header.transform, "EVENTS", 11, false);
        CreateHeaderCell(header.transform, "STATUS", 12, false);
        CreateHeaderIconCell(header.transform, DroneUIFX.IconType.More, 13);

        var body = new GameObject("Rows", typeof(RectTransform), typeof(RectMask2D));
        body.transform.SetParent(table.transform, false);
        var bodyElement = body.AddComponent<LayoutElement>();
        bodyElement.minHeight = 430f;
        bodyElement.flexibleHeight = 1f;
        var bodyLayout = body.AddComponent<VerticalLayoutGroup>();
        bodyLayout.spacing = 1;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;
        tableBody = (RectTransform)body.transform;
    }

    void ConfigureTableRowLayout(HorizontalLayoutGroup layout)
    {
        layout.padding = new RectOffset(7, 7, 0, 0);
        layout.spacing = 0;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
    }

    void CreateHeaderCell(Transform parent, string label, int column, bool sortable)
    {
        var cell = CreateTableCell(parent, label, column);
        if (sortable)
        {
            var button = cell.AddComponent<Button>();
            var hit = cell.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                newestFirst = !newestFirst;
                if (dateSortIcon != null)
                    dateSortIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, newestFirst ? 0f : 180f);
                ApplyFilters(false);
                SetStatus(newestFirst ? "Sorted by newest flight first" : "Sorted by oldest flight first", false);
            });
        }

        var text = CreateText(cell.transform, "Label", label, 9.5f,
            sortable ? ACCENT : TEXT_SEC, TextAlignmentOptions.MidlineLeft, true);
        text.enableAutoSizing = true;
        text.fontSizeMin = 7.5f;
        text.fontSizeMax = 9.5f;
        text.characterSpacing = 1;
        Stretch(text.rectTransform, 4f);
        if (sortable)
        {
            text.rectTransform.offsetMax = new Vector2(-17f, -4f);
            dateSortIcon = DroneUIFX.CreateIcon(cell.transform, DroneUIFX.IconType.ChevronDown, 10f, ACCENT);
            dateSortIcon.gameObject.name = "SortDirection";
            AnchorIcon(dateSortIcon.rectTransform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), 10f);
        }
    }

    void CreateHeaderIconCell(Transform parent, DroneUIFX.IconType iconType, int column)
    {
        var cell = CreateTableCell(parent, "Actions", column);
        var icon = DroneUIFX.CreateIcon(cell.transform, iconType, 14f, TEXT_SEC);
        AnchorIcon(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 14f);
    }

    GameObject CreateTableCell(Transform parent, string name, int column)
    {
        var cell = new GameObject(name, typeof(RectTransform));
        cell.transform.SetParent(parent, false);
        var layout = cell.AddComponent<LayoutElement>();
        layout.minWidth = 0;
        layout.preferredWidth = 0;
        layout.flexibleWidth = columnWeights[column];
        return cell;
    }

    void RenderRows()
    {
        if (tableBody == null) return;
        for (int i = tableBody.childCount - 1; i >= 0; i--)
        {
            tableBody.GetChild(i).gameObject.SetActive(false);
            Destroy(tableBody.GetChild(i).gameObject);
        }

        for (int i = 0; i < visibleRecords.Count; i++)
            CreateRecordRow(visibleRecords[i], i);

        UpdateSelectionUI();
        UpdatePagination();
    }

    void CreateRecordRow(FlightRecord record, int rowIndex)
    {
        var row = new GameObject(record.id, typeof(RectTransform), typeof(Image), typeof(Button));
        row.transform.SetParent(tableBody, false);
        SetFixedHeight(row.AddComponent<LayoutElement>(), 43f);
        var rowImage = row.GetComponent<Image>();
        bool isReplayRequest = requestedReplayId == record.id;
        rowImage.color = isReplayRequest
            ? new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.16f)
            : rowIndex % 2 == 0 ? CARD : new Color(CARD2.r, CARD2.g, CARD2.b, 0.72f);
        var rowButton = row.GetComponent<Button>();
        rowButton.targetGraphic = rowImage;
        rowButton.transition = Selectable.Transition.ColorTint;
        var colors = rowButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.86f, 0.90f, 0.94f, 1f);
        colors.fadeDuration = 0.05f;
        rowButton.colors = colors;
        rowButton.onClick.AddListener(() => RequestReplay(record));

        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        ConfigureTableRowLayout(rowLayout);

        var selectCell = CreateTableCell(row.transform, "Select", 0);
        CreateCheckbox(selectCell.transform, selectedIds.Contains(record.id), () => ToggleSelection(record.id));
        AddCellText(row.transform, record.id, 1, ACCENT, true);
        AddCellText(row.transform, record.date, 2, TEXT, false);
        AddCellText(row.transform, record.location, 3, TEXT, false);
        AddCellText(row.transform, record.drone, 4, TEXT_SEC, true);
        AddCellText(row.transform, record.mission, 5, TEXT, false);
        AddPillCell(row.transform, record.type, 6, TypeColor(record.type));
        AddCellText(row.transform, record.duration, 7, TEXT, false);
        AddCellText(row.transform, record.distance, 8, TEXT, false);
        AddCellText(row.transform, record.maxAltitude, 9, TEXT, false);
        AddCellText(row.transform, record.battery, 10, TEXT, false);
        AddWarningCell(row.transform, record, 11);
        AddPillCell(row.transform, record.status, 12, StatusColor(record.status));
        AddMoreCell(row.transform, record, 13);
    }

    void AddCellText(Transform parent, string value, int column, Color color, bool bold)
    {
        var cell = CreateTableCell(parent, "Cell_" + column, column);
        var text = CreateText(cell.transform, "Value", value, 10.5f, color,
            TextAlignmentOptions.MidlineLeft, bold);
        text.enableAutoSizing = true;
        text.fontSizeMin = 7.5f;
        text.fontSizeMax = 10.5f;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform, 4f);
    }

    void AddPillCell(Transform parent, string value, int column, Color color)
    {
        var cell = CreateTableCell(parent, "Cell_" + column, column);
        var pill = new GameObject(value, typeof(RectTransform), typeof(Image));
        pill.transform.SetParent(cell.transform, false);
        var image = pill.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(color.r, color.g, color.b, 0.15f);
        image.raycastTarget = false;
        var rt = (RectTransform)pill.transform;
        rt.anchorMin = new Vector2(0.04f, 0.5f);
        rt.anchorMax = new Vector2(0.94f, 0.5f);
        rt.offsetMin = new Vector2(0f, -13f);
        rt.offsetMax = new Vector2(0f, 13f);

        var text = CreateText(pill.transform, "Label", value, 9.5f, color,
            TextAlignmentOptions.Center, true);
        text.enableAutoSizing = true;
        text.fontSizeMin = 7.5f;
        text.fontSizeMax = 9.5f;
        Stretch(text.rectTransform, 3f);
    }

    void AddWarningCell(Transform parent, FlightRecord record, int column)
    {
        var cell = CreateTableCell(parent, "Events", column);
        if (record.warnings == 0)
        {
            var empty = CreateText(cell.transform, "None", "-", 10.5f, TEXT_DIM,
                TextAlignmentOptions.MidlineLeft, false);
            Stretch(empty.rectTransform, 4f);
            return;
        }

        Color color = record.status == "Failed" ? RED : AMBER;
        var icon = DroneUIFX.CreateIcon(cell.transform, DroneUIFX.IconType.Warning, 13f, color);
        icon.gameObject.name = "WarningIcon";
        AnchorIcon(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), 13f);
        var count = CreateText(cell.transform, "Count", record.warnings.ToString(CultureInfo.InvariantCulture),
            10.5f, color, TextAlignmentOptions.MidlineLeft, true);
        Stretch(count.rectTransform);
        count.rectTransform.offsetMin = new Vector2(25f, 2f);
        count.rectTransform.offsetMax = new Vector2(-2f, -2f);
    }

    void AddMoreCell(Transform parent, FlightRecord record, int column)
    {
        var cell = CreateTableCell(parent, "More", column);
        var go = new GameObject("MoreButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(cell.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.08f, 0.15f);
        rt.anchorMax = new Vector2(0.92f, 0.85f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.color = new Color(0, 0, 0, 0);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => ShowActionMenu(record));
        var icon = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.More, 16f, ACCENT);
        icon.gameObject.name = "MoreIcon";
        AnchorIcon(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 16f);
    }

    CheckVisual CreateCheckbox(Transform parent, bool selected, Action callback)
    {
        var go = new GameObject("Checkbox", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(19, 19);
        rt.anchoredPosition = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => callback());
        button.transition = Selectable.Transition.None;

        var mark = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.Check, 12f, BG);
        mark.gameObject.name = "CheckMark";
        AnchorIcon(mark.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 12f);
        var visual = new CheckVisual { background = image, mark = mark };
        SetCheckbox(visual, selected);
        return visual;
    }

    CheckVisual CreatePassiveCheckbox(Transform parent, bool selected)
    {
        var go = new GameObject("Checkbox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = 18f;
        element.preferredHeight = 18f;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        var mark = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.Check, 11f, BG);
        mark.gameObject.name = "CheckMark";
        AnchorIcon(mark.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 11f);
        var visual = new CheckVisual { background = image, mark = mark };
        SetCheckbox(visual, selected);
        return visual;
    }

    void SetCheckbox(CheckVisual visual, bool selected)
    {
        if (visual == null) return;
        visual.background.color = selected ? ACCENT : CARD2;
        visual.mark.enabled = selected;
        visual.mark.color = selected ? BG : TEXT_DIM;
    }

    void ToggleSelection(string flightId)
    {
        if (!selectedIds.Add(flightId)) selectedIds.Remove(flightId);
        RenderRows();
        SetStatus(selectedIds.Count + (selectedIds.Count == 1 ? " flight selected" : " flights selected"), false);
    }

    void ToggleSelectAllVisible()
    {
        bool allSelected = visibleRecords.Count > 0;
        for (int i = 0; i < visibleRecords.Count; i++)
            allSelected &= selectedIds.Contains(visibleRecords[i].id);

        for (int i = 0; i < visibleRecords.Count; i++)
        {
            if (allSelected) selectedIds.Remove(visibleRecords[i].id);
            else selectedIds.Add(visibleRecords[i].id);
        }
        RenderRows();
        SetStatus(allSelected ? "Visible selection cleared" : "All visible sample flights selected", false);
    }

    void BuildActionBar()
    {
        var bar = CreateSurface("SelectionAndPagination", transform, PANEL, 0f, 58f);
        var layout = bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 7;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var selectionMarker = new GameObject("SelectionMarker", typeof(RectTransform), typeof(Image));
        selectionMarker.transform.SetParent(bar.transform, false);
        var markerLayout = selectionMarker.AddComponent<LayoutElement>();
        markerLayout.preferredWidth = 18;
        markerLayout.preferredHeight = 18;
        var markerImage = selectionMarker.GetComponent<Image>();
        markerImage.sprite = DroneUIFX.RoundedRectSprite;
        markerImage.type = Image.Type.Sliced;
        markerImage.color = new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.55f);
        markerImage.raycastTarget = false;

        selectedCountText = CreateText(bar.transform, "SelectedCount", "2 flights selected", 11f,
            TEXT, TextAlignmentOptions.MidlineLeft, true);
        var countLayout = selectedCountText.gameObject.AddComponent<LayoutElement>();
        countLayout.preferredWidth = 135;
        countLayout.preferredHeight = 36;

        compareButton = CreateActionButton(bar.transform, "COMPARE FLIGHTS", 144f, () =>
        {
            if (selectedIds.Count != 2)
            {
                SetStatus("Compare requires exactly two selected flights", true);
                return;
            }
            SetStatus("Compare requested · comparison backend is not connected", false);
        });
        exportButton = CreateActionButton(bar.transform, "EXPORT", 88f, () => ExportRecords(null));
        CreateActionButton(bar.transform, "CLEAR", 78f, () =>
        {
            selectedIds.Clear();
            RenderRows();
            SetStatus("Flight selection cleared", false);
        });

        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(bar.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        paginationText = CreateText(bar.transform, "Pagination", "1–25 of 248 flights", 10.5f,
            TEXT_SEC, TextAlignmentOptions.MidlineRight, false);
        var pageLayout = paginationText.gameObject.AddComponent<LayoutElement>();
        pageLayout.preferredWidth = 170;
        pageLayout.preferredHeight = 36;

        CreatePagerButton(bar.transform, "Previous", -90f, false, () => SetStatus("Already at the first demo page", false));
        CreatePagerButton(bar.transform, "Next", 90f, true, () =>
            SetStatus("Additional pages require the persistent flight recorder", true));

        var rowsLabel = CreateText(bar.transform, "RowsLabel", "ROWS PER PAGE", 9.5f, TEXT_DIM,
            TextAlignmentOptions.MidlineRight, true);
        rowsLabel.characterSpacing = 1;
        var rowsLabelLayout = rowsLabel.gameObject.AddComponent<LayoutElement>();
        rowsLabelLayout.preferredWidth = 105;
        rowsLabelLayout.preferredHeight = 36;

        var rowsButton = new GameObject("RowsPerPage", typeof(RectTransform), typeof(Image), typeof(Button));
        rowsButton.transform.SetParent(bar.transform, false);
        var rowsLayout = rowsButton.AddComponent<LayoutElement>();
        rowsLayout.preferredWidth = 66;
        rowsLayout.preferredHeight = 36;
        rowsPerPageText = CreateText(rowsButton.transform, "Label", "25", 10.5f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        Stretch(rowsPerPageText.rectTransform);
        rowsPerPageText.rectTransform.offsetMin = new Vector2(14f, 2f);
        rowsPerPageText.rectTransform.offsetMax = new Vector2(-22f, -2f);
        var rowsPerPageChevron = DroneUIFX.CreateIcon(rowsButton.transform, DroneUIFX.IconType.ChevronDown, 10f, TEXT_SEC);
        rowsPerPageChevron.gameObject.name = "Chevron";
        AnchorIcon(rowsPerPageChevron.rectTransform, new Vector2(1f, 0.5f), new Vector2(-11f, 0f), 10f);
        var rowsImage = rowsButton.GetComponent<Image>();
        rowsImage.sprite = DroneUIFX.RoundedRectSprite;
        rowsImage.type = Image.Type.Sliced;
        rowsImage.color = CARD2;
        var rowsPageButton = rowsButton.GetComponent<Button>();
        rowsPageButton.targetGraphic = rowsImage;
        rowsPageButton.onClick.AddListener(CycleRowsPerPage);
        DroneUIFX.ApplyAeroButtonStyle(rowsButton, ACCENT);
        rowsPerPageText.transform.SetAsLastSibling();
        rowsPerPageChevron.transform.SetAsLastSibling();
    }

    Button CreateActionButton(Transform parent, string label, float width, Action callback)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = 36;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD2;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => callback());
        var text = CreateText(go.transform, "Label", label, 9.5f, ACCENT,
            TextAlignmentOptions.Center, true);
        text.characterSpacing = 1;
        Stretch(text.rectTransform, 4f);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.normalTextColor = ACCENT;
        text.transform.SetAsLastSibling();
        return button;
    }

    void CreatePagerButton(Transform parent, string name, float rotation, bool interactable, Action callback)
    {
        var go = new GameObject("Page" + name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 36;
        layout.preferredHeight = 36;
        var image = go.GetComponent<Image>();
        image.sprite = DroneUIFX.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.color = CARD2;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (interactable) button.onClick.AddListener(() => callback());
        var icon = DroneUIFX.CreateIcon(go.transform, DroneUIFX.IconType.ChevronDown, 12f,
            interactable ? TEXT : TEXT_DIM);
        icon.gameObject.name = "Chevron";
        AnchorIcon(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 12f);
        icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
        var style = DroneUIFX.ApplyAeroButtonStyle(go, ACCENT);
        style.foreground = icon;
        style.normalForegroundColor = interactable ? TEXT : TEXT_DIM;
        style.SetInteractable(interactable);
        icon.transform.SetAsLastSibling();
    }

    void CycleRowsPerPage()
    {
        rowsPerPage = rowsPerPage == 10 ? 25 : rowsPerPage == 25 ? 50 : 10;
        rowsPerPageText.text = rowsPerPage.ToString(CultureInfo.InvariantCulture);
        UpdatePagination();
        SetStatus("Rows per page set to " + rowsPerPage + " · only 10 seeded records are available", false);
    }

    void BuildStatusStrip()
    {
        var strip = CreateSurface("ReplayStatus", transform, CARD, 0f, 28f);
        statusText = CreateText(strip.transform, "Status",
            "SEEDED UI DEMO · 10 sample records · persistent flight recorder unavailable",
            9.5f, TEXT_SEC, TextAlignmentOptions.MidlineLeft, true);
        statusText.characterSpacing = 1.5f;
        Stretch(statusText.rectTransform, 10f);
    }

    void BuildActionMenu()
    {
        actionMenu = CreateSurface("FlightActions", transform, CARD2, 256f, 232f);
        actionMenu.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)actionMenu.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -248f);
        rt.sizeDelta = new Vector2(256f, 232f);

        var layout = actionMenu.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 4;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var titleRow = CreateRect("Title", actionMenu.transform, 28f);
        var titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
        titleLayout.childAlignment = TextAnchor.MiddleLeft;
        titleLayout.childControlWidth = true;
        titleLayout.childForceExpandWidth = false;
        actionMenuTitle = CreateText(titleRow.transform, "Label", "FLIGHT ACTIONS", 10f, TEXT,
            TextAlignmentOptions.MidlineLeft, true);
        actionMenuTitle.characterSpacing = 2;
        actionMenuTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        var closeObject = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
        closeObject.transform.SetParent(titleRow.transform, false);
        var closeLayout = closeObject.AddComponent<LayoutElement>();
        closeLayout.preferredWidth = 26;
        closeLayout.preferredHeight = 26;
        var closeHit = closeObject.GetComponent<Image>();
        closeHit.color = new Color(0f, 0f, 0f, 0f);
        var closeIcon = DroneUIFX.CreateIcon(closeObject.transform, DroneUIFX.IconType.Close, 11f, TEXT_SEC);
        AnchorIcon(closeIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 11f);
        var closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeHit;
        closeButton.onClick.AddListener(() => actionMenu.SetActive(false));

        CreateMenuAction("VIEW REPLAY", () => RequestReplay(menuRecord));
        CreateMenuAction("EXPORT FLIGHT DATA", () => ExportRecords(menuRecord));
        CreateMenuAction("CREATE MISSION FROM FLIGHT", () => MenuBackendAction("Create Mission from Flight"));
        CreateMenuAction("COMPARE FLIGHT", () => MenuBackendAction("Compare Flight"));
        CreateMenuAction("COPY FLIGHT ID", CopyMenuFlightId);
        sourceFlightAction = CreateMenuAction("VIEW SOURCE FLIGHT", ViewSourceFlight);
        actionMenu.SetActive(false);
    }

    GameObject CreateMenuAction(string label, Action callback)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(actionMenu.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 27f;
        var image = go.GetComponent<Image>();
        image.color = new Color(CARD.r, CARD.g, CARD.b, 0.82f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => callback());
        var text = CreateText(go.transform, "Label", label, 9f, TEXT_SEC,
            TextAlignmentOptions.MidlineLeft, true);
        text.characterSpacing = 1;
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(9, 0);
        text.rectTransform.offsetMax = new Vector2(-5, 0);
        return go;
    }

    void ShowActionMenu(FlightRecord record)
    {
        menuRecord = record;
        actionMenuTitle.text = "ACTIONS  /  " + record.id;
        bool hasSource = !string.IsNullOrEmpty(record.sourceFlight);
        sourceFlightAction.SetActive(hasSource);
        ((RectTransform)actionMenu.transform).sizeDelta = new Vector2(256f, hasSource ? 232f : 201f);
        actionMenu.SetActive(true);
        actionMenu.transform.SetAsLastSibling();
        SetStatus("Actions opened for " + record.id, false);
    }

    void RequestReplay(FlightRecord record)
    {
        if (record == null) return;
        requestedReplayId = record.id;
        if (actionMenu != null) actionMenu.SetActive(false);
        RenderRows();
        SetStatus("Replay selected: " + record.id + " · recorded telemetry is not available in this UI demo", false);
    }

    void MenuBackendAction(string action)
    {
        if (menuRecord == null) return;
        actionMenu.SetActive(false);
        SetStatus(action + " requested for " + menuRecord.id + " · backend unavailable in demo", false);
    }

    void PrepareExportDirectory()
    {
        try
        {
            exportDirectory = ResolveExportDirectory();
            Directory.CreateDirectory(exportDirectory);
        }
        catch (Exception exception)
        {
            Debug.LogError("[FlightRecords] Could not create CSV export directory "
                + (exportDirectory ?? "<unresolved>") + "\n" + exception);
        }
    }

    static string ResolveExportDirectory()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "Temp", "FlightRecordsExports"));
#else
        return Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Temp", "FlightRecordsExports"));
#endif
    }

    void ExportRecords(FlightRecord singleRecord)
    {
        if (singleRecord != null && actionMenu != null) actionMenu.SetActive(false);

        var exportRecords = new List<FlightRecord>();
        if (singleRecord != null)
        {
            exportRecords.Add(singleRecord);
        }
        else if (selectedIds.Count > 0)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (selectedIds.Contains(records[i].id)) exportRecords.Add(records[i]);
            }
        }
        else
        {
            exportRecords.AddRange(visibleRecords);
        }

        if (exportRecords.Count == 0)
        {
            SetStatus("No flight records to export", true);
            return;
        }

        exportRecords.Sort((a, b) => newestFirst
            ? b.dateKey.CompareTo(a.dateKey)
            : a.dateKey.CompareTo(b.dateKey));

        string outputPath = exportDirectory ?? "<unresolved>";
        try
        {
            if (string.IsNullOrEmpty(exportDirectory)) exportDirectory = ResolveExportDirectory();
            outputPath = exportDirectory;
            Directory.CreateDirectory(exportDirectory);
            string stem = "FlightRecords_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            outputPath = Path.Combine(exportDirectory, stem + ".csv");
            int suffix = 1;
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(exportDirectory,
                    stem + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".csv");
                suffix++;
            }

            var csv = new StringBuilder(1024 + exportRecords.Count * 256);
            AppendCsvRow(csv, "Flight ID", "Date Key", "Recorded At", "Display Date", "Date Filter",
                "Location", "City", "Drone", "Mission", "Mission Filter", "Flight Type", "Duration",
                "Distance", "Max Altitude", "Battery", "Warnings", "Status", "Operator", "Source Flight");
            for (int i = 0; i < exportRecords.Count; i++)
            {
                FlightRecord record = exportRecords[i];
                AppendCsvRow(csv,
                    record.id,
                    record.dateKey.ToString(CultureInfo.InvariantCulture),
                    record.recordedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    record.date,
                    record.dateFilter,
                    record.location,
                    record.city,
                    record.drone,
                    record.mission,
                    record.missionFilter,
                    record.type,
                    record.duration,
                    record.distance,
                    record.maxAltitude,
                    record.battery,
                    record.warnings.ToString(CultureInfo.InvariantCulture),
                    record.status,
                    record.operatorName,
                    record.sourceFlight);
            }

            File.WriteAllText(outputPath, csv.ToString(), new UTF8Encoding(false));
            SetStatus("Exported " + exportRecords.Count + (exportRecords.Count == 1 ? " flight" : " flights")
                + " to Temp/FlightRecordsExports/" + Path.GetFileName(outputPath), false);
            Debug.Log("[FlightRecords] CSV exported to " + outputPath);
        }
        catch (IOException exception)
        {
            ReportExportFailure(outputPath, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportExportFailure(outputPath, exception);
        }
        catch (Exception exception)
        {
            ReportExportFailure(outputPath, exception);
        }
    }

    static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(',');
            string value = values[i] ?? string.Empty;
            builder.Append('"');
            builder.Append(value.Replace("\"", "\"\""));
            builder.Append('"');
        }
        builder.AppendLine();
    }

    void ReportExportFailure(string outputPath, Exception exception)
    {
        SetStatus("Export failed. Check the Console for details", true);
        Debug.LogError("[FlightRecords] CSV export failed for " + outputPath + "\n" + exception);
    }

    void CopyMenuFlightId()
    {
        if (menuRecord == null) return;
        GUIUtility.systemCopyBuffer = menuRecord.id;
        actionMenu.SetActive(false);
        SetStatus("Copied flight ID: " + menuRecord.id, false);
    }

    void ViewSourceFlight()
    {
        if (menuRecord == null || string.IsNullOrEmpty(menuRecord.sourceFlight)) return;
        actionMenu.SetActive(false);
        SetStatus("Source flight selected: " + menuRecord.sourceFlight + " · source telemetry unavailable in demo", false);
    }

    void ApplyFilters(bool announce = true)
    {
        visibleRecords.Clear();
        for (int i = 0; i < records.Count; i++)
        {
            if (Matches(records[i])) visibleRecords.Add(records[i]);
        }
        visibleRecords.Sort((a, b) => newestFirst
            ? b.dateKey.CompareTo(a.dateKey)
            : a.dateKey.CompareTo(b.dateKey));

        for (int i = 0; i < quickControls.Count; i++)
            quickControls[i].style.SetSelected(quickControls[i].filter == quickFilter);

        RenderRows();
        if (announce)
            SetStatus(visibleRecords.Count + " seeded record(s) match the current search and filters", false);
    }

    bool Matches(FlightRecord record)
    {
        if (!string.IsNullOrEmpty(searchTerm))
        {
            bool found = Contains(record.id, searchTerm)
                || Contains(record.drone, searchTerm)
                || Contains(record.mission, searchTerm)
                || Contains(record.location, searchTerm)
                || Contains(record.operatorName, searchTerm);
            if (!found) return false;
        }

        if (quickFilter == QuickFilter.Autonomous && record.type != "Autonomous") return false;
        if (quickFilter == QuickFilter.Manual && record.type != "Manual") return false;
        if (quickFilter == QuickFilter.Reflight && record.type != "Re-flight") return false;
        if (quickFilter == QuickFilter.Warnings && record.warnings == 0) return false;

        for (int i = 0; i < filters.Count; i++)
        {
            FilterControl filter = filters[i];
            if (!filter.IsActive) continue;
            switch (filter.kind)
            {
                case FilterKind.Date:
                    DateTime recordDate = record.recordedAt.Date;
                    if (filter.dateFrom.HasValue && recordDate < filter.dateFrom.Value.Date) return false;
                    if (filter.dateTo.HasValue && recordDate > filter.dateTo.Value.Date) return false;
                    break;
                case FilterKind.Drone:
                    if (!filter.selected.Contains(record.drone)) return false;
                    break;
                case FilterKind.Location:
                    if (!filter.selected.Contains(record.city)) return false;
                    break;
                case FilterKind.Mission:
                    if (!filter.selected.Contains(record.missionFilter)) return false;
                    break;
                case FilterKind.Type:
                    if (!filter.selected.Contains(record.type)) return false;
                    break;
                case FilterKind.Status:
                    if (!filter.selected.Contains(record.status)) return false;
                    break;
                case FilterKind.Warnings:
                    string warningCategory = record.warnings > 0 ? "WITH WARNINGS" : "NO WARNINGS";
                    if (!filter.selected.Contains(warningCategory)) return false;
                    break;
            }
        }
        return true;
    }

    static bool Contains(string source, string term)
    {
        return !string.IsNullOrEmpty(source)
            && source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void UpdateSelectionUI()
    {
        if (selectedCountText != null)
        {
            selectedCountText.text = selectedIds.Count == 0
                ? "NO FLIGHTS SELECTED"
                : selectedIds.Count + (selectedIds.Count == 1 ? " flight selected" : " flights selected");
        }
        if (compareButton != null)
            compareButton.GetComponent<DroneUIFX.AeroButtonHoverFX>()?.SetInteractable(selectedIds.Count == 2);
        if (exportButton != null)
            exportButton.GetComponent<DroneUIFX.AeroButtonHoverFX>()?.SetInteractable(
                selectedIds.Count > 0 || visibleRecords.Count > 0);

        bool allVisibleSelected = visibleRecords.Count > 0;
        for (int i = 0; i < visibleRecords.Count; i++)
            allVisibleSelected &= selectedIds.Contains(visibleRecords[i].id);
        SetCheckbox(selectAllVisual, allVisibleSelected);
    }

    void UpdatePagination()
    {
        if (paginationText == null) return;
        bool unfiltered = string.IsNullOrEmpty(searchTerm) && quickFilter == QuickFilter.All;
        for (int i = 0; i < filters.Count; i++) unfiltered &= !filters[i].IsActive;
        if (unfiltered)
            paginationText.text = "1–" + rowsPerPage + " of 248 flights";
        else
            paginationText.text = visibleRecords.Count == 0
                ? "0 matching samples"
                : "1–" + Mathf.Min(rowsPerPage, visibleRecords.Count) + " of " + visibleRecords.Count + " sample matches";
    }

    void SetStatus(string message, bool warning)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = warning ? AMBER : TEXT_SEC;
        }
        Debug.Log("[FlightRecords] " + message);
    }

    static Color TypeColor(string type)
    {
        if (type == "Manual") return new Color(0.62f, 0.72f, 0.80f, 1f);
        if (type == "Re-flight") return new Color(0.48f, 0.54f, 1f, 1f);
        return ACCENT;
    }

    static Color StatusColor(string status)
    {
        if (status == "Failed") return RED;
        if (status == "Aborted") return AMBER;
        return GREEN;
    }

    GameObject CreateRect(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return go;
    }

    static void SetFixedHeight(LayoutElement element, float height)
    {
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    GameObject CreateSurface(string name, Transform parent, Color fillColor, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        if (width > 0) layout.preferredWidth = width;
        if (height > 0) layout.preferredHeight = height;
        var border = go.GetComponent<Image>();
        border.sprite = DroneUIFX.RoundedRectSprite;
        border.type = Image.Type.Sliced;
        border.color = BORDER;

        var inner = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(go.transform, false);
        inner.transform.SetAsFirstSibling();
        var innerImage = inner.GetComponent<Image>();
        innerImage.sprite = DroneUIFX.RoundedRectSprite;
        innerImage.type = Image.Type.Sliced;
        innerImage.color = fillColor;
        innerImage.raycastTarget = false;
        inner.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)inner.transform, 1f);
        return go;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        Color color, TextAlignmentOptions alignment, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static void AnchorIcon(RectTransform rt, Vector2 anchor, Vector2 position, float size)
    {
        var layout = rt.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(size, size);
    }

    void SeedRecords()
    {
        // These ten rows mirror the Flight Records reference. They are not persisted telemetry.
        records.Add(NewRecord("FL-00382", 202609151432, "15 Sep 2026 · 14:32", "15 SEP 2026",
            "Bengaluru, India", "BENGALURU", "DRN-004", "Solar Inspection Route A · Run 3",
            "SOLAR INSPECTION ROUTE A", "Autonomous", "16m 22s", "4.8 km", "87 m", "94% → 41%",
            1, "Completed", "Alex Morgan"));
        records.Add(NewRecord("FL-00381", 202609151106, "15 Sep 2026 · 11:06", "15 SEP 2026",
            "Bengaluru, India", "BENGALURU", "DRN-011", "Perimeter Route B · Run 8",
            "PERIMETER ROUTE B", "Re-flight", "12m 04s", "3.1 km", "64 m", "97% → 58%",
            0, "Completed", "Priya Rao", "FL-00291"));
        records.Add(NewRecord("FL-00380", 202609141741, "14 Sep 2026 · 17:41", "14 SEP 2026",
            "Mysuru, India", "MYSURU", "DRN-008", "—", "UNASSIGNED", "Manual", "8m 51s",
            "1.7 km", "42 m", "83% → 49%", 2, "Completed", "Rahul Mehta"));
        records.Add(NewRecord("FL-00379", 202609140918, "14 Sep 2026 · 09:18", "14 SEP 2026",
            "Hyderabad, India", "HYDERABAD", "DRN-003", "Wind Turbine Survey · Run 1",
            "WIND TURBINE SURVEY", "Autonomous", "21m 16s", "6.2 km", "120 m", "100% → 37%",
            0, "Completed", "Neha Sharma"));
        records.Add(NewRecord("FL-00378", 202609131603, "13 Sep 2026 · 16:03", "13 SEP 2026",
            "Chennai, India", "CHENNAI", "DRN-007", "Coastal Mapping · Run 2", "COASTAL MAPPING",
            "Autonomous", "18m 07s", "5.4 km", "95 m", "92% → 33%", 1, "Completed", "Arjun Iyer"));
        records.Add(NewRecord("FL-00377", 202609131027, "13 Sep 2026 · 10:27", "13 SEP 2026",
            "Pune, India", "PUNE", "DRN-005", "Infrastructure Survey · Run 4", "INFRASTRUCTURE SURVEY",
            "Re-flight", "14m 36s", "3.9 km", "78 m", "96% → 52%", 0, "Completed", "Kavya Singh",
            "FL-00341"));
        records.Add(NewRecord("FL-00376", 202609121355, "12 Sep 2026 · 13:55", "12 SEP 2026",
            "Bengaluru, India", "BENGALURU", "DRN-004", "Solar Inspection Route A · Run 2",
            "SOLAR INSPECTION ROUTE A", "Autonomous", "17m 11s", "4.6 km", "88 m", "95% → 40%",
            3, "Completed", "Alex Morgan"));
        records.Add(NewRecord("FL-00375", 202609120812, "12 Sep 2026 · 08:12", "12 SEP 2026",
            "Coimbatore, India", "COIMBATORE", "DRN-009", "Perimeter Route C · Run 1",
            "PERIMETER ROUTE C", "Manual", "9m 48s", "2.1 km", "51 m", "89% → 61%", 0,
            "Completed", "Vikram Nair"));
        records.Add(NewRecord("FL-00374", 202609111520, "11 Sep 2026 · 15:20", "11 SEP 2026",
            "Kochi, India", "KOCHI", "DRN-010", "Port Survey · Run 1", "PORT SURVEY", "Autonomous",
            "23m 14s", "7.3 km", "110 m", "98% → 29%", 1, "Aborted", "Sara Thomas"));
        records.Add(NewRecord("FL-00373", 202609110941, "11 Sep 2026 · 09:41", "11 SEP 2026",
            "Visakhapatnam, India", "VISAKHAPATNAM", "DRN-002", "Thermal Inspection · Run 1",
            "THERMAL INSPECTION", "Manual", "6m 32s", "1.4 km", "36 m", "78% → 22%", 2,
            "Failed", "Dev Patel"));
    }

    static FlightRecord NewRecord(string id, long dateKey, string date, string dateFilter,
        string location, string city, string drone, string mission, string missionFilter, string type,
        string duration, string distance, string maxAltitude, string battery, int warnings, string status,
        string operatorName, string sourceFlight = null)
    {
        DateTime.TryParseExact(dateKey.ToString(CultureInfo.InvariantCulture), "yyyyMMddHHmm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime recordedAt);
        return new FlightRecord
        {
            id = id,
            dateKey = dateKey,
            recordedAt = recordedAt,
            date = date,
            dateFilter = dateFilter,
            location = location,
            city = city,
            drone = drone,
            mission = mission,
            missionFilter = missionFilter,
            type = type,
            duration = duration,
            distance = distance,
            maxAltitude = maxAltitude,
            battery = battery,
            warnings = warnings,
            status = status,
            operatorName = operatorName,
            sourceFlight = sourceFlight
        };
    }
}
