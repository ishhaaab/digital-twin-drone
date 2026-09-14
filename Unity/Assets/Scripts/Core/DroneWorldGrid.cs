using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// World-space XZ reference grid centered on the drone simulation origin.
/// Grid spacing remains one telemetry meter regardless of Unity's uniform display scale.
public static class DroneWorldGrid
{
    const string GridName = "Drone Origin Grid";

    public static void EnsureGrid(float extentMeters = 1000f, float spacingMeters = 1f, float unitsPerMeter = 1f)
    {
        if (GameObject.Find(GridName) != null) return;

        unitsPerMeter = Mathf.Max(0.0001f, unitsPerMeter);
        float extent = extentMeters * unitsPerMeter;
        float spacing = spacingMeters * unitsPerMeter;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[Grid] Sprites/Default shader was not found.");
            return;
        }

        var gridObject = new GameObject(GridName)
        {
            hideFlags = HideFlags.DontSave
        };
        gridObject.transform.position = new Vector3(0f, 0.015f * unitsPerMeter, 0f);

        var filter = gridObject.AddComponent<MeshFilter>();
        var renderer = gridObject.AddComponent<MeshRenderer>();
        var material = new Material(shader)
        {
            name = "Drone Origin Grid Material",
            hideFlags = HideFlags.DontSave
        };
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        int lineCount = Mathf.CeilToInt(extent / spacing);
        for (int i = -lineCount; i <= lineCount; i++)
        {
            float coordinate = i * spacing;
            bool axis = i == 0;
            bool major = i % 5 == 0;
            float width = (axis ? 0.045f : major ? 0.026f : 0.014f) * unitsPerMeter;
            Color color = axis
                ? new Color(0.125f, 0.847f, 0.941f, 0.62f)
                : major ? new Color(0.18f, 0.58f, 0.66f, 0.30f)
                : new Color(0.16f, 0.42f, 0.48f, 0.14f);

            AddQuad(vertices, colors, triangles,
                new Vector3(coordinate - width, 0, -extent),
                new Vector3(coordinate + width, 0, -extent),
                new Vector3(coordinate + width, 0, extent),
                new Vector3(coordinate - width, 0, extent), color);
            AddQuad(vertices, colors, triangles,
                new Vector3(-extent, 0, coordinate - width),
                new Vector3(extent, 0, coordinate - width),
                new Vector3(extent, 0, coordinate + width),
                new Vector3(-extent, 0, coordinate + width), color);
        }

        var mesh = new Mesh { name = "Drone Origin Grid Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    static void AddQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        int index = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1);
        triangles.Add(index); triangles.Add(index + 3); triangles.Add(index + 2);
    }
}
