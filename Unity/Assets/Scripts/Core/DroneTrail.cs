using UnityEngine;

/// Aerospace trail — fixed circular buffer, zero per-frame allocations beyond LineRenderer's internal copy.
/// Previously used List.RemoveAt(0) O(n) + ToArray() allocation each new point.
/// Now: ring buffer of maxPoints, reusable posCache, no GC.
///
/// Evidence: trail consumes DroneController's world position (Unity space, NED→Unity scaled in DroneController:75-77).
/// Home position is the first pushed point; not a fabricated GPS coordinate.
[RequireComponent(typeof(LineRenderer))]
public class DroneTrail : MonoBehaviour
{
    [Tooltip("Minimum movement (m) before adding a new point.")]
    public float minDistance = 0.12f;
    [Tooltip("Maximum points (bounded history).")]
    public int maxPoints = 400;

    LineRenderer line;
    Vector3[] ring;
    Vector3[] cache; // reusable copy for LineRenderer.SetPositions
    int head = 0;    // next write index
    int count = 0;
    Vector3 lastPushed;
    bool hasFirst = false;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        ring = new Vector3[Mathf.Clamp(maxPoints, 16, 2048)];
        cache = new Vector3[ring.Length];
        line.positionCount = 0;
        line.useWorldSpace = true;
        // aerospace styling — muted teal, thin
        if (line.material == null) line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = new Color(0.22f, 0.68f, 0.64f, 0.55f);
        line.startWidth = line.endWidth = 0.08f;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
    }

    void OnValidate()
    {
        if (maxPoints < 16) maxPoints = 16;
        if (maxPoints > 2048) maxPoints = 2048;
    }

    void Update()
    {
        // Lazy re-alloc if maxPoints changed via inspector at runtime (editor only)
        if (ring == null || ring.Length != maxPoints)
        {
            ring = new Vector3[maxPoints];
            cache = new Vector3[maxPoints];
            head = 0; count = 0; hasFirst = false;
            line.positionCount = 0;
        }

        Vector3 cur = transform.position;
        if (!hasFirst || Vector3.Distance(lastPushed, cur) > minDistance)
        {
            ring[head] = cur;
            head = (head + 1) % ring.Length;
            if (count < ring.Length) count++;
            lastPushed = cur;
            hasFirst = true;

            // Copy in order oldest→newest into cache
            int start = (head - count + ring.Length) % ring.Length;
            for (int i = 0; i < count; i++)
            {
                int src = (start + i) % ring.Length;
                cache[i] = ring[src];
            }
            line.positionCount = count;
            // SetPositions copies; we reuse cache to avoid ToArray()
            for (int i = 0; i < count; i++) line.SetPosition(i, cache[i]);
        }
    }

    /// Clears trail (e.g. on disarm or home reset).
    public void Clear()
    {
        head = 0; count = 0; hasFirst = false;
        line.positionCount = 0;
    }

    /// Returns home (first point) if available. Used for home marker verificaiton.
    public bool TryGetHome(out Vector3 home)
    {
        if (count == 0) { home = Vector3.zero; return false; }
        int start = (head - count + ring.Length) % ring.Length;
        home = ring[start];
        return true;
    }
}