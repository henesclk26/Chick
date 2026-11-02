using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What rocks the open scene has and where (for planning where more go): every outermost object whose name says rock,
/// stone, boulder or cliff, grouped by the prefab it comes from with how many, their sizes, and the map area they cover;
/// and the texture properties of the materials of the rock packs in Temp/RockSurvey/request.txt (one material or model
/// path per line). Picked up while the Editor is open; writes Temp/RockSurvey/report.txt.
/// </summary>
[InitializeOnLoad]
public static class RockSurvey
{
    private const string Out = "Temp/RockSurvey";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static RockSurvey() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] lines = File.ReadAllLines(Request).Where(l => l.Trim().Length > 0).ToArray();
        File.Delete(Request);
        var sb = new StringBuilder();
        static bool Rocky(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("rock") || n.Contains("stone") || n.Contains("boulder") || n.Contains("cliff") || n.Contains("pebble");
        }
        var found = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => Rocky(t.name) && t.GetComponentInChildren<Renderer>() != null &&
                        (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) || t.GetComponent<Renderer>() != null) &&
                        (t.parent == null || !Rocky(t.parent.name) || !PrefabUtility.IsPartOfPrefabInstance(t.parent)))
            .ToList();
        sb.AppendLine($"{found.Count} rock objects (active)");
        foreach (var g in found.GroupBy(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) is string p && p.Length > 0 ? p : "(scene) " + t.name)
                     .OrderByDescending(g => g.Count()))
        {
            var bs = g.Select(t => t.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(t.position, Vector3.zero), (a, r) => { a.Encapsulate(r.bounds); return a; })).ToList();
            float minX = g.Min(t => t.position.x), maxX = g.Max(t => t.position.x), minZ = g.Min(t => t.position.z), maxZ = g.Max(t => t.position.z);
            var parents = g.Select(t => t.parent != null ? t.parent.name : "-").Distinct().Take(4);
            sb.AppendLine($"  {g.Count(),4} x {g.Key}  size {bs.Min(b => b.size.y):0.0}-{bs.Max(b => b.size.y):0.0} m tall, x {minX:0}..{maxX:0} z {minZ:0}..{maxZ:0}, in {string.Join(" | ", parents)}");
        }
        foreach (string line in lines)
        {
            string path = line.Trim();
            var mats = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToList();
            foreach (Material m in mats)
            {
                sb.AppendLine($"material {m.name} [{m.shader.name}] in {path}");
                foreach (string tex in m.GetTexturePropertyNames())
                {
                    Texture t = m.GetTexture(tex);
                    if (t != null) sb.AppendLine($"    {tex}: {t.name} {t.width}x{t.height}");
                }
                foreach (string c in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                    if (m.HasProperty(c)) sb.AppendLine($"    {c}: {m.GetColor(c)}");
            }
        }
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString());
    }
}
