using UnityEngine;
using UnityEngine.UI;

/// Professional aerospace sparkline graph — bounded history, single draw call.
/// No Instantiate/Destroy per frame. Uses a circular float buffer and a
/// MaskableGraphic that rebuilds its mesh on data push.
///
/// Evidence for data sources (verified):
///   altitude -> DroneData.z (LOCAL_POSITION_NED, sign-flipped in mavlink_bridge.py:239) -> DroneDataReceiver.latestData.z
///   speed    -> DroneData.speed (derived in DroneDataReceiver.ComputeVelocity, lines 328-353)
///   latency  -> DroneDataReceiver.latencyStats.mean (computed from send timestamp in DroneDataReceiver.Update lines 258-262)
///
/// Rendering: custom UGUI mesh (quads per segment), thin line, low-contrast grid.
/// The updater samples at 5 Hz, so 301 samples represent a 60-second window.
public class TelemetryGraph : MaskableGraphic, DroneUIFX.IFxWidget
{
    [Header("Data")]
    public int capacity = 301;
    [SerializeField] float[] buffer;
    [SerializeField] int head = 0;          // next write index
    [SerializeField] int filled = 0;

    [Header("Range")]
    public bool autoScale = false;
    public float manualMin = 0f;
    public float manualMax = 10f;
    float autoMin, autoMax;

    [Header("Style")]
    public Color lineColor = new Color(0.18f, 0.72f, 0.68f, 1f);
    public Color gridColor = new Color(1f, 1f, 1f, 0.07f);
    public Color fillColor = new Color(0.18f, 0.72f, 0.68f, 0.08f);
    public float thickness = 1.9f;
    public bool drawFill = true;
    public bool drawGrid = true;
    public int gridLines = 3;
    public int verticalGridLines = 4;

    // display smoothing not needed — data is already smoothed by telemetry rate.
    // Tick kept for IFxWidget compatibility; graphing is event-driven via Push().
    public void Tick(float dt) { }

    protected override void Awake()
    {
        base.Awake();
        if (buffer == null || buffer.Length != capacity)
            buffer = new float[capacity];
        // UGUI MaskableGraphic needs raycastTarget false for perf unless interactive
        raycastTarget = false;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetVerticesDirty();
    }

#pragma warning disable CS0114
    protected void OnValidate()
    {
        if (capacity < 8) capacity = 8;
        if (capacity > 1024) capacity = 1024;
        if (buffer == null || buffer.Length != capacity)
        {
            var nb = new float[capacity];
            if (buffer != null)
            {
                int copy = System.Math.Min(filled, capacity);
                for (int i = 0; i < copy; i++)
                {
                    int src = (head - filled + i + buffer.Length) % buffer.Length;
                    nb[i] = buffer[src];
                }
                filled = copy;
                head = filled % capacity;
            }
            buffer = nb;
            SetVerticesDirty();
        }
    }
#pragma warning restore CS0114

    /// Push one sample. Thread-safe only from main thread (called from DroneUIUpdater.Update).
    public void Push(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
        if (buffer == null || buffer.Length != capacity) buffer = new float[capacity];
        buffer[head] = v;
        head = (head + 1) % capacity;
        if (filled < capacity) filled++;
        SetVerticesDirty();
    }

    public void Clear()
    {
        filled = 0;
        head = 0;
        SetVerticesDirty();
    }

    public void SetRange(float min, float max)
    {
        manualMin = min;
        manualMax = max;
        if (manualMax <= manualMin) manualMax = manualMin + 1f;
        SetVerticesDirty();
    }

    // Last pushed value (for header numeric display)
    public float LastValue
    {
        get
        {
            if (filled == 0) return 0f;
            int idx = (head - 1 + capacity) % capacity;
            return buffer[idx];
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 2f || r.height < 2f) return;

        // Background grid (horizontal lines)
        if (drawGrid && gridLines > 0)
        {
            for (int g = 0; g <= gridLines; g++)
            {
                float y = r.y + (r.height * g / gridLines);
                // Use thin quad for grid line (0.8px)
                float hw = r.width * 0.5f;
                float hy = 0.45f;
                // centered quad stretched horizontally — we emulate with two triangles
                // Instead of generating full grid quads one by one, add a single thin rect across width.
                // Use UIVertex helper
                AddQuad(vh,
                    new Vector2(r.x, y - hy),
                    new Vector2(r.x + r.width, y - hy),
                    new Vector2(r.x + r.width, y + hy),
                    new Vector2(r.x, y + hy),
                    gridColor);
            }
        }

        if (drawGrid && verticalGridLines > 0)
        {
            for (int g = 0; g <= verticalGridLines; g++)
            {
                float x = r.x + (r.width * g / verticalGridLines);
                AddQuad(vh,
                    new Vector2(x - 0.4f, r.y),
                    new Vector2(x + 0.4f, r.y),
                    new Vector2(x + 0.4f, r.y + r.height),
                    new Vector2(x - 0.4f, r.y + r.height),
                    gridColor);
            }
        }

        if (filled == 0) return;

        // Determine vertical range
        float vMin, vMax;
        if (autoScale)
        {
            // compute min/max over window
            vMin = float.MaxValue; vMax = float.MinValue;
            for (int i = 0; i < filled; i++)
            {
                int idx = (head - filled + i + capacity) % capacity;
                float v = buffer[idx];
                if (v < vMin) vMin = v;
                if (v > vMax) vMax = v;
            }
            float pad = Mathf.Max((vMax - vMin) * 0.12f, 0.5f);
            vMin -= pad; vMax += pad;
            if (Mathf.Abs(vMax - vMin) < 0.25f) { vMin -= 0.5f; vMax += 0.5f; }
            autoMin = vMin; autoMax = vMax;
        }
        else
        {
            vMin = manualMin; vMax = manualMax;
            if (vMax <= vMin) vMax = vMin + 1f;
        }
        float vRange = vMax - vMin;
        if (vRange < 0.001f) vRange = 1f;

        // Build point list in order
        // Precompute screen positions
        // Using r.x..r.x+r.width, r.y..r.y+r.height
        Vector2[] pts = new Vector2[filled];
        for (int i = 0; i < filled; i++)
        {
            int idx = (head - filled + i + capacity) % capacity;
            float v = buffer[idx];
            float t = filled == 1 ? 1f
                : filled < capacity ? (float)i / (filled - 1)
                : (float)i / (capacity - 1);
            float x = r.x + t * r.width;
            float norm = Mathf.Clamp01((v - vMin) / vRange);
            float y = r.y + 2f + norm * Mathf.Max(1f, r.height - 4f);
            pts[i] = new Vector2(x, y);
        }

        // Optional fill under line (polygonal area to bottom)
        if (drawFill && filled >= 2)
        {
            // Build fill mesh as triangle strip to bottom (r.y)
            // For each segment, create quad between line and baseline
            Color fc = fillColor;
            for (int i = 0; i < filled - 1; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[i + 1];
                Vector2 a0 = new Vector2(a.x, r.y);
                Vector2 b0 = new Vector2(b.x, r.y);
                AddQuad(vh, a0, b0, b, a, fc);
            }
        }

        // Line segments as thick quads
        float half = thickness * 0.5f;
        for (int i = 0; i < filled - 1; i++)
        {
            Vector2 p1 = pts[i];
            Vector2 p2 = pts[i + 1];
            Vector2 dir = p2 - p1;
            float len = dir.magnitude;
            if (len < 0.001f) continue;
            dir /= len;
            Vector2 perp = new Vector2(-dir.y, dir.x) * half;

            Vector2 v0 = p1 - perp;
            Vector2 v1 = p1 + perp;
            Vector2 v2 = p2 + perp;
            Vector2 v3 = p2 - perp;

            AddQuad(vh, v0, v1, v2, v3, lineColor);

            // Joint circle to avoid gaps (small quad at p1)
            // Cheap: add a small square at joint
            // Skip for perf — quads already overlap slightly due to perp calc; gap only at sharp angles.
        }

        // Current value dot (small circle approximated as 6-vertex fan — simplified as quad)
        if (filled > 0)
        {
            Vector2 last = pts[filled - 1];
            float d = thickness * 2.2f;
            float hd = d * 0.5f;
            // outer glow (larger, low alpha)
            AddQuad(vh,
                new Vector2(last.x - hd*1.8f, last.y - hd*1.8f),
                new Vector2(last.x + hd*1.8f, last.y - hd*1.8f),
                new Vector2(last.x + hd*1.8f, last.y + hd*1.8f),
                new Vector2(last.x - hd*1.8f, last.y + hd*1.8f),
                new Color(lineColor.r, lineColor.g, lineColor.b, 0.18f));
            // core dot
            AddQuad(vh,
                new Vector2(last.x - hd, last.y - hd),
                new Vector2(last.x + hd, last.y - hd),
                new Vector2(last.x + hd, last.y + hd),
                new Vector2(last.x - hd, last.y + hd),
                new Color(1f,1f,1f,0.95f));
        }
    }

    static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int idx = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = col;
        v.uv0 = Vector2.zero;

        v.position = a; vh.AddVert(v);
        v.position = b; vh.AddVert(v);
        v.position = c; vh.AddVert(v);
        v.position = d; vh.AddVert(v);

        vh.AddTriangle(idx, idx+1, idx+2);
        vh.AddTriangle(idx+2, idx+3, idx);
    }
}
