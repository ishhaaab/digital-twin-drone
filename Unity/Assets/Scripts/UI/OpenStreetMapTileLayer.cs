using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// Interactive raster basemap layer for the dashboard map viewport.
/// Only visible tiles are requested. OSM street tiles are cached for seven days;
/// Esri imagery remains in memory and is not exported for offline use.
[RequireComponent(typeof(RectTransform), typeof(Image), typeof(RectMask2D))]
public sealed class OpenStreetMapTileLayer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public enum BasemapStyle
    {
        Street,
        Satellite,
        Hybrid
    }

    public RectTransform tileRoot;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI attributionText;
    public DroneMapView overlay;

    [Range(3, 19)] public int zoom = 19;
    public string tileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string satelliteTileUrlTemplate = "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public string hybridLabelsTileUrlTemplate = "https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Transportation/MapServer/tile/{z}/{y}/{x}";
    public string userAgent = "DigitalTwinDrone/1.0 (+https://github.com/ishhaaab/digital-twin-drone)";
    public BasemapStyle basemapStyle = BasemapStyle.Street;

    const int TileSize = 256;
    const int MinZoom = 3;
    const int MaxZoom = 19;
    const double MaxLatitude = 85.05112878;
    static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    sealed class TileSlot
    {
        public string key;
        public int x;
        public int y;
        public RawImage image;
        public Texture2D texture;
        public RawImage referenceImage;
        public Texture2D referenceTexture;
    }

    readonly Dictionary<string, TileSlot> tiles = new Dictionary<string, TileSlot>();
    RectTransform viewport;
    Vector2 lastViewportSize;
    double centerLatitude;
    double centerLongitude;
    double centerWorldX;
    double centerWorldY;
    bool hasCenter;
    bool refreshQueued;
    int pendingRequests;
    int failedRequests;
    int tileGeneration;

    public bool HasCenter => hasCenter;
    public string AttributionUrl => basemapStyle == BasemapStyle.Street
        ? "https://www.openstreetmap.org/copyright"
        : basemapStyle == BasemapStyle.Satellite
            ? "https://www.arcgis.com/home/item.html?id=10df2279f9684e4a9f6a7f08febac2a9"
            : "https://www.arcgis.com/home/item.html?id=86265e5a4bbb4187a59719cf134e0018";

    public float MetersPerPixel
    {
        get
        {
            if (!hasCenter) return 0f;
            double latitudeRadians = centerLatitude * Math.PI / 180.0;
            double circumference = 2.0 * Math.PI * 6378137.0;
            return (float)(Math.Cos(latitudeRadians) * circumference / (TileSize * Math.Pow(2.0, zoom)));
        }
    }

    void Awake()
    {
        viewport = (RectTransform)transform;
        var hitArea = GetComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0f);
        hitArea.raycastTarget = true;
        UpdateAttribution();
    }

    void OnEnable()
    {
        QueueRefresh();
    }

    void OnDisable()
    {
        refreshQueued = false;
        pendingRequests = 0;
        ClearTiles();
    }

    void OnRectTransformDimensionsChange()
    {
        if (viewport == null) return;
        Vector2 size = viewport.rect.size;
        if ((size - lastViewportSize).sqrMagnitude < 1f) return;
        lastViewportSize = size;
        QueueRefresh();
    }

    public void SetLocation(double latitude, double longitude)
    {
        if (!IsValidCoordinate(latitude, longitude)) return;

        if (!hasCenter)
        {
            CenterOn(latitude, longitude);
            return;
        }

        Vector2 offset = Project(latitude, longitude);
        Rect rect = viewport.rect;
        if (Mathf.Abs(offset.x) > rect.width * 0.38f || Mathf.Abs(offset.y) > rect.height * 0.38f)
            CenterOn(latitude, longitude);
    }

    public void CenterOn(double latitude, double longitude)
    {
        if (!IsValidCoordinate(latitude, longitude)) return;
        centerLatitude = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, latitude));
        centerLongitude = NormalizeLongitude(longitude);
        LatLonToWorldPixel(centerLatitude, centerLongitude, zoom, out centerWorldX, out centerWorldY);
        hasCenter = true;
        PositionTiles();
        QueueRefresh();
        UpdateStatus();
        overlay?.RefreshProjection();
    }

    public Vector2 Project(double latitude, double longitude)
    {
        if (!hasCenter || !IsValidCoordinate(latitude, longitude)) return Vector2.zero;
        LatLonToWorldPixel(latitude, longitude, zoom, out double worldX, out double worldY);
        double worldSize = TileSize * Math.Pow(2.0, zoom);
        double deltaX = worldX - centerWorldX;
        if (deltaX > worldSize * 0.5) deltaX -= worldSize;
        else if (deltaX < -worldSize * 0.5) deltaX += worldSize;
        return new Vector2((float)deltaX, (float)(centerWorldY - worldY));
    }

    public void RefreshTiles()
    {
        QueueRefresh();
    }

    public void SetBasemap(BasemapStyle style)
    {
        if (basemapStyle == style) return;
        basemapStyle = style;
        ClearTiles();
        UpdateAttribution();
        UpdateStatus();
        QueueRefresh();
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (!hasCenter) return;
        centerWorldX -= eventData.delta.x;
        centerWorldY += eventData.delta.y;
        WorldPixelToLatLon(centerWorldX, centerWorldY, zoom, out centerLatitude, out centerLongitude);
        PositionTiles();
        overlay?.RefreshProjection();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        QueueRefresh();
    }

    public void OnScroll(PointerEventData eventData)
    {
        ZoomBy(eventData.scrollDelta.y > 0f ? 1 : -1);
    }

    public void ZoomBy(int delta)
    {
        int nextZoom = Mathf.Clamp(zoom + delta, MinZoom, MaxZoom);
        if (!hasCenter || nextZoom == zoom) return;
        zoom = nextZoom;
        LatLonToWorldPixel(centerLatitude, centerLongitude, zoom, out centerWorldX, out centerWorldY);
        ClearTiles();
        QueueRefresh();
        overlay?.RefreshProjection();
    }

    void QueueRefresh()
    {
        if (!isActiveAndEnabled || refreshQueued) return;
        refreshQueued = true;
        StartCoroutine(RefreshNextFrame());
    }

    IEnumerator RefreshNextFrame()
    {
        yield return null;
        refreshQueued = false;
        RefreshVisibleTiles();
    }

    void RefreshVisibleTiles()
    {
        if (!hasCenter || tileRoot == null || viewport.rect.width < 2f || viewport.rect.height < 2f) return;

        Rect rect = viewport.rect;
        int tileCount = 1 << zoom;
        int minTileX = (int)Math.Floor((centerWorldX - rect.width * 0.5) / TileSize);
        int maxTileX = (int)Math.Floor((centerWorldX + rect.width * 0.5) / TileSize);
        int minTileY = Mathf.Max(0, (int)Math.Floor((centerWorldY - rect.height * 0.5) / TileSize));
        int maxTileY = Mathf.Min(tileCount - 1, (int)Math.Floor((centerWorldY + rect.height * 0.5) / TileSize));

        var required = new HashSet<string>();
        for (int rawX = minTileX; rawX <= maxTileX; rawX++)
        {
            int wrappedX = ((rawX % tileCount) + tileCount) % tileCount;
            for (int y = minTileY; y <= maxTileY; y++)
            {
                string key = $"{zoom}/{wrappedX}/{y}";
                required.Add(key);
                if (tiles.ContainsKey(key)) continue;
                CreateTile(key, rawX, wrappedX, y);
            }
        }

        var obsolete = new List<string>();
        foreach (var pair in tiles)
            if (!required.Contains(pair.Key)) obsolete.Add(pair.Key);
        for (int i = 0; i < obsolete.Count; i++) RemoveTile(obsolete[i]);

        PositionTiles();
        UpdateStatus();
    }

    void CreateTile(string key, int layoutX, int requestX, int y)
    {
        var go = new GameObject($"Tile {key}", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(tileRoot, false);
        var image = go.GetComponent<RawImage>();
        image.color = new Color(0.12f, 0.16f, 0.17f, 1f);
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(TileSize, TileSize);

        var slot = new TileSlot { key = key, x = layoutX, y = y, image = image };
        if (basemapStyle == BasemapStyle.Hybrid)
        {
            var reference = new GameObject("Reference", typeof(RectTransform), typeof(RawImage));
            reference.transform.SetParent(go.transform, false);
            var referenceImage = reference.GetComponent<RawImage>();
            referenceImage.color = Color.clear;
            referenceImage.raycastTarget = false;
            RectTransform referenceRect = referenceImage.rectTransform;
            referenceRect.anchorMin = Vector2.zero;
            referenceRect.anchorMax = Vector2.one;
            referenceRect.offsetMin = Vector2.zero;
            referenceRect.offsetMax = Vector2.zero;
            slot.referenceImage = referenceImage;
        }
        tiles.Add(key, slot);
        PositionTile(slot);
        int generation = tileGeneration;
        string baseTemplate = basemapStyle == BasemapStyle.Street ? tileUrlTemplate : satelliteTileUrlTemplate;
        StartCoroutine(LoadTile(slot, requestX, baseTemplate, false, basemapStyle == BasemapStyle.Street, generation));
        if (basemapStyle == BasemapStyle.Hybrid)
            StartCoroutine(LoadTile(slot, requestX, hybridLabelsTileUrlTemplate, true, false, generation));
    }

    IEnumerator LoadTile(TileSlot slot, int requestX, string urlTemplate, bool reference, bool useDiskCache, int generation)
    {
        string cachePath = useDiskCache ? GetCachePath(zoom, requestX, slot.y) : null;
        bool hasCachedTile = useDiskCache && TryLoadCachedTile(slot, cachePath, reference);
        bool cacheIsFresh = hasCachedTile && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime;
        if (cacheIsFresh)
        {
            UpdateStatus();
            yield break;
        }

        pendingRequests++;
        UpdateStatus();
        string url = urlTemplate
            .Replace("{z}", zoom.ToString())
            .Replace("{x}", requestX.ToString())
            .Replace("{y}", slot.y.ToString());
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, true))
        {
            request.SetRequestHeader("User-Agent", userAgent);
            yield return request.SendWebRequest();
            if (generation != tileGeneration) yield break;
            pendingRequests = Mathf.Max(0, pendingRequests - 1);

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                ApplyTexture(slot, texture, reference);
                if (useDiskCache)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                        File.WriteAllBytes(cachePath, request.downloadHandler.data);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Map] Could not cache OSM tile: " + e.Message);
                    }
                }
            }
            else
            {
                failedRequests++;
                if (!hasCachedTile)
                    Debug.LogWarning($"[Map] Tile request failed ({request.responseCode}): {request.error}");
            }
        }
        UpdateStatus();
    }

    bool TryLoadCachedTile(TileSlot slot, string path, bool reference)
    {
        if (!File.Exists(path)) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, true))
            {
                Destroy(texture);
                return false;
            }
            ApplyTexture(slot, texture, reference);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Map] Could not read cached OSM tile: " + e.Message);
            return false;
        }
    }

    void ApplyTexture(TileSlot slot, Texture2D texture, bool reference)
    {
        RawImage image = reference ? slot.referenceImage : slot.image;
        if (image == null)
        {
            if (texture != null) Destroy(texture);
            return;
        }
        Texture2D oldTexture = reference ? slot.referenceTexture : slot.texture;
        if (oldTexture != null && oldTexture != texture) Destroy(oldTexture);
        if (reference) slot.referenceTexture = texture;
        else slot.texture = texture;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        image.texture = texture;
        image.color = Color.white;
    }

    void PositionTiles()
    {
        foreach (TileSlot slot in tiles.Values) PositionTile(slot);
    }

    void PositionTile(TileSlot slot)
    {
        if (slot.image == null) return;
        slot.image.rectTransform.anchoredPosition = new Vector2(
            (float)(slot.x * TileSize - centerWorldX),
            (float)(centerWorldY - slot.y * TileSize));
    }

    void RemoveTile(string key)
    {
        if (!tiles.TryGetValue(key, out TileSlot slot)) return;
        tiles.Remove(key);
        if (slot.texture != null) Destroy(slot.texture);
        if (slot.referenceTexture != null) Destroy(slot.referenceTexture);
        if (slot.image != null) Destroy(slot.image.gameObject);
    }

    void ClearTiles()
    {
        tileGeneration++;
        pendingRequests = 0;
        var keys = new List<string>(tiles.Keys);
        for (int i = 0; i < keys.Count; i++) RemoveTile(keys[i]);
        failedRequests = 0;
    }

    void UpdateStatus()
    {
        if (statusText == null) return;
        string provider = basemapStyle == BasemapStyle.Street
            ? "OPENSTREETMAP"
            : basemapStyle == BasemapStyle.Satellite ? "ESRI WORLD IMAGERY" : "ESRI IMAGERY HYBRID";
        if (!hasCenter) statusText.text = provider + "  /  WAITING FOR GPS";
        else if (pendingRequests > 0) statusText.text = $"{provider}  /  LOADING {pendingRequests}";
        else if (failedRequests > 0)
        {
            bool hasCachedTiles = false;
            foreach (TileSlot tile in tiles.Values)
            {
                if (tile.texture == null) continue;
                hasCachedTiles = true;
                break;
            }
            statusText.text = hasCachedTiles
                ? basemapStyle == BasemapStyle.Street ? "OPENSTREETMAP  /  OFFLINE CACHE" : provider + "  /  PARTIAL"
                : "MAP TILES UNAVAILABLE  /  LOCAL GRID";
        }
        else statusText.text = $"{provider}  /  ZOOM {zoom}";
    }

    void UpdateAttribution()
    {
        if (attributionText == null) return;
        attributionText.text = basemapStyle == BasemapStyle.Street
            ? "© OpenStreetMap contributors"
            : basemapStyle == BasemapStyle.Satellite
                ? "Imagery © Esri and providers"
                : "Imagery / labels © Esri and contributors";
    }

    string GetCachePath(int z, int x, int y)
    {
        return Path.Combine(Application.persistentDataPath, "OpenStreetMapTileCache", z.ToString(), x.ToString(), y + ".png");
    }

    void OnDestroy()
    {
        ClearTiles();
    }

    public static bool IsValidCoordinate(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) && !double.IsInfinity(latitude)
            && !double.IsNaN(longitude) && !double.IsInfinity(longitude)
            && latitude >= -90.0 && latitude <= 90.0
            && longitude >= -180.0 && longitude <= 180.0;
    }

    static double NormalizeLongitude(double longitude)
    {
        return ((longitude + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
    }

    static void LatLonToWorldPixel(double latitude, double longitude, int zoomLevel, out double x, out double y)
    {
        double lat = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, latitude));
        double worldSize = TileSize * Math.Pow(2.0, zoomLevel);
        double sinLatitude = Math.Sin(lat * Math.PI / 180.0);
        x = (NormalizeLongitude(longitude) + 180.0) / 360.0 * worldSize;
        y = (0.5 - Math.Log((1.0 + sinLatitude) / (1.0 - sinLatitude)) / (4.0 * Math.PI)) * worldSize;
    }

    static void WorldPixelToLatLon(double x, double y, int zoomLevel, out double latitude, out double longitude)
    {
        double worldSize = TileSize * Math.Pow(2.0, zoomLevel);
        x = ((x % worldSize) + worldSize) % worldSize;
        y = Math.Max(0.0, Math.Min(worldSize, y));
        longitude = x / worldSize * 360.0 - 180.0;
        double mercatorY = Math.PI * (1.0 - 2.0 * y / worldSize);
        latitude = Math.Atan(Math.Sinh(mercatorY)) * 180.0 / Math.PI;
    }
}
