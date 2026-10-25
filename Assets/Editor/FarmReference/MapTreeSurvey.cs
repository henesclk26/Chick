using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lists every tree in the open scene for planning where new tree models go: tree objects (by the prefab or model they
/// come from, with position, height and parent group) and the terrain's painted trees (by prototype), counted overall
/// and around the barn, plus a picture of the map from above with the trees marked by kind. Asked for by
/// Temp/MapTrees/request.txt (picked up while the Editor is open) or the menu; writes Temp/MapTrees.
/// </summary>
[InitializeOnLoad]
public static class MapTreeSurvey
{
    private const string Out = "Temp/MapTrees";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static MapTreeSurvey() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        Survey();
    }

    internal static bool LooksLikeTree(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("tree") || n.Contains("pine") || n.Contains("fir") || n.Contains("oak") || n.Contains("birch") ||
               n.Contains("spruce") || n.Contains("willow") || n.Contains("conifer") || n.Contains("agac");
    }

    [MenuItem("Tools/Chick/Map/Survey Trees")]
    private static void Survey()
    {
        Directory.CreateDirectory(Out);
        var sb = new StringBuilder();
        var barn = GameObject.Find("SM_Bld_Barn_01");
        Vector3 barnAt = barn != null ? barn.transform.position : Vector3.zero;
        sb.AppendLine($"barn at {barnAt}");

        // Tree objects: the outermost prefab instance (or plain object) whose name says tree and that has renderers.
        var trees = new List<(Transform t, string source, float height)>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!LooksLikeTree(t.name)) continue;
            if (t.parent != null && LooksLikeTree(t.parent.name) && t.parent.GetComponentsInChildren<Renderer>().Length > 0 &&
                (PrefabUtility.IsPartOfPrefabInstance(t.parent) || t.parent.GetComponent<Renderer>() != null)) continue;   // a part of a tree
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || renderers.Length > 40) continue;   // groups are not trees
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y < 2f) continue;
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
            if (string.IsNullOrEmpty(source)) source = "(scene) " + t.name;
            trees.Add((t, source, b.size.y));
        }
        sb.AppendLine($"tree objects: {trees.Count}");
        foreach (var g in trees.GroupBy(x => x.source).OrderByDescending(g => g.Count()))
        {
            var near = g.Count(x => Flat(x.t.position - barnAt) < 45f);
            sb.AppendLine($"  {g.Count(),4} x {g.Key}  (height {g.Min(x => x.height):0.0}-{g.Max(x => x.height):0.0} m, {near} within 45 m of the barn, " +
                          $"parents: {string.Join(" | ", g.Select(x => x.t.parent != null ? x.t.parent.name : "-").Distinct().Take(4))})");
        }

        // Terrain trees.
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData d = terrain.terrainData;
            sb.AppendLine($"terrain {terrain.name}: {d.treeInstanceCount} painted trees");
            for (int p = 0; p < d.treePrototypes.Length; p++)
            {
                var inst = d.treeInstances.Where(i => i.prototypeIndex == p).ToArray();
                int near = inst.Count(i => Flat(Vector3.Scale(i.position, d.size) + terrain.transform.position - barnAt) < 45f);
                sb.AppendLine($"  {inst.Length,4} x {(d.treePrototypes[p].prefab != null ? AssetDatabase.GetAssetPath(d.treePrototypes[p].prefab) : "?")} ({near} near the barn)");
            }
        }

        // Around the barn, nearest first.
        sb.AppendLine("tree objects within 45 m of the barn:");
        foreach (var x in trees.Where(x => Flat(x.t.position - barnAt) < 45f).OrderBy(x => Flat(x.t.position - barnAt)))
            sb.AppendLine($"  {Flat(x.t.position - barnAt),5:0.0} m  {x.t.name} at {x.t.position} h {x.height:0.0}  [{Path.GetFileNameWithoutExtension(x.source)}]  in {PathOf(x.t.parent)}");
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString());

        // Map from above: every tree object as a dot, coloured by its source.
        Terrain main = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).FirstOrDefault();
        if (main != null)
        {
            Vector3 o = main.transform.position, s = main.terrainData.size;
            Camera camera = MapSurvey.TempCamera();
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = s.z / 2f;
                camera.aspect = s.x / s.z;
                camera.farClipPlane = 1000f;
                camera.transform.SetPositionAndRotation(o + new Vector3(s.x / 2f, 300f, s.z / 2f), Quaternion.Euler(90f, 0f, 0f));
                int w = 1600, h = Mathf.RoundToInt(1600 * s.z / s.x);
                Texture2D tex = MapSurvey.Render(camera, w, h);
                var colours = new Dictionary<string, Color>();
                Color[] palette = { Color.red, Color.yellow, Color.cyan, Color.magenta, new Color(1f, .5f, 0f), Color.white, Color.blue, new Color(.5f, 0f, 1f), Color.black, new Color(0f, 1f, .5f) };
                foreach (var x in trees)
                {
                    if (!colours.ContainsKey(x.source)) colours[x.source] = palette[colours.Count % palette.Length];
                    Vector3 v = camera.WorldToViewportPoint(x.t.position);
                    int px = Mathf.RoundToInt(v.x * w), py = Mathf.RoundToInt(v.y * h);
                    for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
                        if (px + dx >= 0 && px + dx < w && py + dy >= 0 && py + dy < h) tex.SetPixel(px + dx, py + dy, colours[x.source]);
                }
                Vector3 bv = camera.WorldToViewportPoint(barnAt);
                for (int dx = -6; dx <= 6; dx++) for (int dy = -6; dy <= 6; dy++)
                    if (Mathf.Abs(dx) == 6 || Mathf.Abs(dy) == 6) tex.SetPixel(Mathf.RoundToInt(bv.x * w) + dx, Mathf.RoundToInt(bv.y * h) + dy, Color.white);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(Out, "trees_map.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                File.AppendAllText(Path.Combine(Out, "report.txt"), "map colours: " + string.Join("; ", colours.Select(c => $"{Path.GetFileNameWithoutExtension(c.Key)} = {ColorUtility.ToHtmlStringRGB(c.Value)}")) +
                                   $"\nmap: x {o.x}..{o.x + s.x} left to right, z {o.z}..{o.z + s.z} bottom to top\n");
            }
            finally { Object.DestroyImmediate(camera.gameObject); }
        }
        Debug.Log("Map tree survey written to " + Path.GetFullPath(Out));
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    private static string PathOf(Transform t) => t == null ? "-" : t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
