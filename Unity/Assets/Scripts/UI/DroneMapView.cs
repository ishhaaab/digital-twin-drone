using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// GPS/local-NED flight overlay rendered above the OpenStreetMap tile layer.
public class DroneMapView : MaskableGraphic
{
    public TextMeshProUGUI gpsText;
    public TextMeshProUGUI localText;
    public TextMeshProUGUI scaleText;
    public OpenStreetMapTileLayer tileLayer;
    public bool showPath = true;

    const int Capacity = 360;
    [SerializeField] Vector2[] path = new Vector2[Capacity];
    [SerializeField] Vector2[] geoPath = new Vector2[Capacity]; // longitude, latitude
    [SerializeField] bool[] geoPathValid = new bool[Capacity];
    [SerializeField] int head;
    [SerializeField] int count;
    [SerializeField] Vector2 current;
    [SerializeField] bool currentLocalValid;
    [SerializeField] Vector2 currentGeo;
    [SerializeField] bool currentGeoValid;
    [SerializeField] Vector2 homeLocal;
    [SerializeField] Vector2 homeGeo;
    [SerializeField] bool homePositionValid;
    [SerializeField] Vector2 mapCenter;
    [SerializeField] float heading;
    [SerializeField] float visibleSpan = 20f;

    protected override void Awake()
    {
        base.Awake();
        EnsurePathBuffer();
        raycastTarget = false;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnsurePathBuffer();
        SetVerticesDirty();
    }

    public void SetTelemetry(
        float north, float east, float latitude, float longitude, float headingDegrees,
        bool localPositionValid, bool gpsValid, bool headingValid,
        bool homeValid, float homeNorth, float homeEast, float homeLatitude, float homeLongitude)
    {
        EnsurePathBuffer();
        currentLocalValid = localPositionValid;
        if (localPositionValid) current = new Vector2(east, north);
        currentGeo = new Vector2(longitude, latitude);
        currentGeoValid = gpsValid && OpenStreetMapTileLayer.IsValidCoordinate(latitude, longitude);
        homeLocal = new Vector2(homeEast, homeNorth);
        homeGeo = new Vector2(homeLongitude, homeLatitude);
        homePositionValid = homeValid
            && OpenStreetMapTileLayer.IsValidCoordinate(homeLatitude, homeLongitude);
        if (headingValid) heading = headingDegrees;
        if (currentGeoValid) tileLayer?.SetLocation(latitude, longitude);

        if (localPositionValid
            && (count == 0 || Vector2.Distance(path[(head - 1 + Capacity) % Capacity], current) >= 0.08f))
        {
            path[head] = current;
            geoPath[head] = currentGeo;
            geoPathValid[head] = currentGeoValid;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        Vector2 relativeCurrent = current - mapCenter;
        float maxExtent = localPositionValid
            ? Mathf.Max(Mathf.Abs(relativeCurrent.x), Mathf.Abs(relativeCurrent.y)) : 0f;
        for (int i = 0; i < count; i++)
        {
            int index = (head - count + i + Capacity) % Capacity;
            Vector2 relative = path[index] - mapCenter;
            maxExtent = Mathf.Max(maxExtent, Mathf.Abs(relative.x), Mathf.Abs(relative.y));
        }
        float required = Mathf.Max(10f, maxExtent * 2.6f);
        visibleSpan = required <= 10f ? 10f : required <= 20f ? 20f : required <= 50f ? 50f
            : required <= 100f ? 100f : required <= 200f ? 200f : required <= 500f ? 500f : 1000f;

        if (gpsText != null) gpsText.text = currentGeoValid
            ? $"LAT  {latitude:F5}°    LON  {longitude:F5}°" : "GPS NO FIX";
        if (localText != null) localText.text = localPositionValid
            ? $"N  {north:F2} m    E  {east:F2} m" : "LOCAL POSITION STALE";
        UpdateScaleText();
        SetVerticesDirty();
    }

    public void SetPathVisible(bool visible)
    {
        showPath = visible;
        SetVerticesDirty();
    }

    public void Recenter()
    {
        EnsurePathBuffer();
        if (currentLocalValid) mapCenter = current;
        visibleSpan = 10f;
        if (currentGeoValid)
            tileLayer?.CenterOn(currentGeo.y, currentGeo.x);
        SetVerticesDirty();
    }

    public void Refresh()
    {
        EnsurePathBuffer();
        tileLayer?.RefreshTiles();
        RefreshProjection();
    }

    public void RefreshProjection()
    {
        UpdateScaleText();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        EnsurePathBuffer();
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width < 2f || rect.height < 2f) return;

        bool hasMap = tileLayer != null && tileLayer.HasCenter;
        Color minor = hasMap ? new Color(0.04f, 0.20f, 0.25f, 0.16f) : new Color(0.09f, 0.26f, 0.34f, 0.42f);
        Color major = hasMap ? new Color(0.04f, 0.34f, 0.40f, 0.30f) : new Color(0.12f, 0.48f, 0.57f, 0.55f);
        for (int i = 0; i <= 8; i++)
        {
            float x = Mathf.Lerp(rect.xMin, rect.xMax, i / 8f);
            float y = Mathf.Lerp(rect.yMin, rect.yMax, i / 8f);
            Color gridColor = i == 4 ? major : minor;
            AddLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), 0.8f, gridColor);
            AddLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), 0.8f, gridColor);
        }

        if (showPath && count > 1)
        {
            Color pathColor = new Color(DroneUIFX.AERO_ACCENT.r, DroneUIFX.AERO_ACCENT.g, DroneUIFX.AERO_ACCENT.b, 0.78f);
            for (int i = 0; i < count - 1; i++)
            {
                int aIndex = (head - count + i + Capacity) % Capacity;
                int bIndex = (aIndex + 1) % Capacity;
                if (hasMap && (!geoPathValid[aIndex] || !geoPathValid[bIndex])) continue;
                AddLine(vh, ToCanvas(path[aIndex], geoPath[aIndex], geoPathValid[aIndex], rect),
                    ToCanvas(path[bIndex], geoPath[bIndex], geoPathValid[bIndex], rect), 2f, pathColor);
            }
        }

        if (homePositionValid)
        {
            Vector2 home = ToCanvas(homeLocal, homeGeo, true, rect);
            Color homeColor = new Color(DroneUIFX.AERO_GREEN.r, DroneUIFX.AERO_GREEN.g, DroneUIFX.AERO_GREEN.b, 0.9f);
            AddLine(vh, home + new Vector2(-6, 0), home + new Vector2(6, 0), 1.4f, homeColor);
            AddLine(vh, home + new Vector2(0, -6), home + new Vector2(0, 6), 1.4f, homeColor);
        }

        if (currentLocalValid && (!hasMap || currentGeoValid))
        {
            Vector2 marker = ToCanvas(current, currentGeo, currentGeoValid, rect);
            float radians = heading * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2 right = new Vector2(forward.y, -forward.x);
            AddTriangle(vh,
                marker + forward * 11f,
                marker - forward * 7f + right * 7f,
                marker - forward * 4f,
                DroneUIFX.AERO_ACCENT);
            AddTriangle(vh,
                marker + forward * 11f,
                marker - forward * 4f,
                marker - forward * 7f - right * 7f,
                DroneUIFX.AERO_ACCENT);
        }
    }

    Vector2 ToCanvas(Vector2 meters, Vector2 geo, bool hasGeo, Rect rect)
    {
        if (tileLayer != null && tileLayer.HasCenter && hasGeo)
            return rect.center + tileLayer.Project(geo.y, geo.x);
        float scale = Mathf.Min(rect.width, rect.height) / visibleSpan;
        return rect.center + (meters - mapCenter) * scale;
    }

    void EnsurePathBuffer()
    {
        bool reset = path == null || path.Length != Capacity;
        if (reset) path = new Vector2[Capacity];
        if (geoPath == null || geoPath.Length != Capacity) geoPath = new Vector2[Capacity];
        if (geoPathValid == null || geoPathValid.Length != Capacity) geoPathValid = new bool[Capacity];
        if (!reset) return;
        head = 0;
        count = 0;
    }

    void UpdateScaleText()
    {
        if (scaleText == null) return;
        scaleText.text = tileLayer != null && tileLayer.HasCenter
            ? $"ZOOM {tileLayer.zoom}  /  {tileLayer.MetersPerPixel:0.##} m/px"
            : $"GRID  {visibleSpan / 8f:0.#} m";
    }

    static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.001f) return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width * 0.5f;
        AddQuad(vh, start - normal, start + normal, end + normal, end - normal, color);
    }

    static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = a; vh.AddVert(vertex);
        vertex.position = b; vh.AddVert(vertex);
        vertex.position = c; vh.AddVert(vertex);
        vh.AddTriangle(index, index + 1, index + 2);
    }

    static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = a; vh.AddVert(vertex);
        vertex.position = b; vh.AddVert(vertex);
        vertex.position = c; vh.AddVert(vertex);
        vertex.position = d; vh.AddVert(vertex);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }
}
