using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DroneTrail : MonoBehaviour
{
    public float minDistance = 0.1f;   // minimum movement before adding point
    public int maxPoints = 1000;       // limit to avoid performance issues

    private LineRenderer line;
    private List<Vector3> points = new List<Vector3>();

    void Start()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 0;
    }

    void Update()
    {
        Vector3 currentPos = transform.position;

        // Add first point OR if moved enough
        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], currentPos) > minDistance)
        {
            points.Add(currentPos);

            // Limit size
            if (points.Count > maxPoints)
                points.RemoveAt(0);

            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
        }
    }
}