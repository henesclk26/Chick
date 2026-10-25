using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Looks at one spot of the open scene for whoever is fixing something there: pictures from four sides, a low view and
/// one from straight above, and a report of the ground (each terrain's height, holes and painted layer around the spot)
/// and every renderer and collider nearby. Asked for with a line "x y z radius label" in Temp/AreaSurvey/request.txt,
/// which the Editor picks up by itself while it is open (or Tools/Chick/Project/Survey Area From Request). Writes
/// Temp/AreaSurvey/&lt;label&gt;. Nothing in the scene is changed.
/// </summary>
[InitializeOnLoad]
public static class AreaSurvey
{
    private const string Root = "Temp/AreaSurvey";
    private const string Request = Root + "/request.txt";
    private static double nextPoll;

    static AreaSurvey() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Run();
    }

    [MenuItem("Tools/Chick/Project/Survey Area From Request")]
    private static void Run()
    {
        if (!File.Exists(Request)) return;
        string[] parts = File.ReadAllText(Request).Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        File.Delete(Request);
        if (parts.Length < 5) return;
        var c = CultureInfo.InvariantCulture;
        var centre = new Vector3(float.Parse(parts[0], c), float.Parse(parts[1], c), float.Parse(parts[2], c));
        float radius = float.Parse(parts[3], c);
        string label = parts[4];
        string outDir = System.IO.Path.Combine(Root, label);
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        report.AppendLine($"centre {centre}, radius {radius}");

        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;
            Vector3 origin = terrain.transform.position, size = data.size;
            if (centre.x < origin.x - radius || centre.x > origin.x + size.x + radius || centre.z < origin.z - radius || centre.z > origin.z + size.z + radius) continue;
            report.AppendLine($"terrain {terrain.name} at {origin} size {size}, heightmap {data.heightmapResolution}, holes {data.holesResolution}, layers {string.Join(", ", data.terrainLayers.Select(l => l != null ? l.name : "null"))}");
            report.AppendLine("  heights (world y) on a 2 m grid, rows north to south, '#' = hole:");
            for (float dz = radius; dz >= -radius; dz -= 2f)
            {
                var row = new StringBuilder("   ");
                for (float dx = -radius; dx <= radius; dx += 2f)
                {
                    Vector3 p = centre + new Vector3(dx, 0f, dz);
                    float u = (p.x - origin.x) / size.x, v = (p.z - origin.z) / size.z;
                    if (u < 0f || v < 0f || u > 1f || v > 1f) { row.Append("     .  "); continue; }
                    bool hole = !data.GetHoles(Mathf.Clamp((int)(u * data.holesResolution), 0, data.holesResolution - 1), Mathf.Clamp((int)(v * data.holesResolution), 0, data.holesResolution - 1), 1, 1)[0, 0];
                    row.Append(hole ? "   #### " : $"{terrain.SampleHeight(p) + origin.y,8:0.00}");
                }
                report.AppendLine(row.ToString());
            }
        }

        report.AppendLine("renderers within the radius (nearest first):");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                     .Where(r => r.enabled && r.gameObject.activeInHierarchy && Flat(r.bounds.ClosestPoint(centre) - centre) < radius)
                     .OrderBy(r => Flat(r.bounds.ClosestPoint(centre) - centre)).Take(80))
        {
            Material m = r.sharedMaterial;
            report.AppendLine($"  {PathOf(r.transform)}  bounds {r.bounds.center} size {r.bounds.size}  material {(m != null ? m.name : "none")} ({(m != null && m.shader != null ? m.shader.name : "-")})");
        }
        report.AppendLine("colliders within the radius:");
        foreach (var col in Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Collide).Take(60))
            report.AppendLine($"  {PathOf(col.transform)} {col.GetType().Name} bounds {col.bounds.center} size {col.bounds.size}");

        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            float ground = GroundAt(centre);
            Vector3 look = new Vector3(centre.x, ground + 0.5f, centre.z);
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 90f + 20f, 0f) * Vector3.forward;
                Vector3 from = look - dir * (radius * 0.9f) + Vector3.up * (radius * 0.45f);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
                Save(MapSurvey.Render(camera, 1280, 800), InDir(outDir, $"side_{i}.png"));
            }
            Vector3 low = look - Vector3.forward * (radius * 0.6f) + Vector3.up * 0.6f;
            camera.transform.SetPositionAndRotation(low, Quaternion.LookRotation(look - low));
            Save(MapSurvey.Render(camera, 1280, 800), InDir(outDir, "low.png"));
            camera.orthographic = true;
            camera.orthographicSize = radius;
            camera.transform.SetPositionAndRotation(new Vector3(centre.x, ground + 60f, centre.z), Quaternion.Euler(90f, 0f, 0f));
            camera.farClipPlane = 200f;
            Save(MapSurvey.Render(camera, 1000, 1000), InDir(outDir, "above.png"));
        }
        finally { Object.DestroyImmediate(camera.gameObject); }

        File.WriteAllText(InDir(outDir, "report.txt"), report.ToString());
        Debug.Log("Area survey: " + System.IO.Path.GetFullPath(outDir));
    }

    private static float GroundAt(Vector3 p)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.z >= o.z && p.x <= o.x + s.x && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
        }
        return p.y;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    private static string InDir(string dir, string file) => System.IO.Path.Combine(dir, file);

    private static void Save(Texture2D tex, string file)
    {
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
