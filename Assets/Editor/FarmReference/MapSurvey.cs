using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Writes a survey of the open map to Temp/MapSurvey, for planning where vegetation goes and what makes the game heavy:
/// - map_topdown.png: the terrain from straight above (orthographic, north up), map_topdown.txt says how its pixels map
///   to world x/z.
/// - map.txt: terrain size and layers, the Farm Reference Map's groups with their world bounds, terrain tree and
///   detail prototypes with their instance counts.
/// - lineup.png + lineup.txt: candidate tree and bush prefabs side by side (order and sizes in the text file), with
///   the shaders and triangle counts of each.
/// - heavy.txt: total triangles in the scene and the heaviest renderers and prefabs.
/// Nothing in the scene or the project is changed.
/// </summary>
public static class MapSurvey
{
    private const string Out = "Temp/MapSurvey";
    private static readonly string[] Candidates =
    {
        "Assets/NatureStarterKit2/Nature",
        "Assets/Art/FruitTrees/Prefabs",
        "Assets/polygonTrees",
        "Assets/BOKI/LowPolyNature",
        "Assets/JC_StylizedNature_Lite",
        "Assets/BluBlu Games",
        "Assets/VertexRageStudio",
        "Assets/PolyRonin",
        "Assets/IL3DN",
    };

    [MenuItem("Tools/Chick/Map/Survey Map For Vegetation")]
    private static void Survey()
    {
        Directory.CreateDirectory(Out);
        var root = GameObject.Find("Farm Reference Map");
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) { EditorUtility.DisplayDialog("Map survey", "No terrain in the open scene.", "OK"); return; }
        try
        {
            EditorUtility.DisplayProgressBar("Map survey", "Map", 0.1f);
            WriteMap(root, terrain);
            EditorUtility.DisplayProgressBar("Map survey", "Top-down view", 0.3f);
            RenderTopDown(terrain);
            EditorUtility.DisplayProgressBar("Map survey", "Heavy objects", 0.5f);
            WriteHeavy();
            EditorUtility.DisplayProgressBar("Map survey", "Tree line-up", 0.7f);
            RenderLineup(terrain);
        }
        finally { EditorUtility.ClearProgressBar(); }
        Debug.Log("Map survey written to " + Path.GetFullPath(Out));
    }

    private static string B(Bounds b) =>
        $"x {b.min.x:F1}..{b.max.x:F1} y {b.min.y:F1}..{b.max.y:F1} z {b.min.z:F1}..{b.max.z:F1}";

    private static void WriteMap(GameObject root, Terrain terrain)
    {
        var sb = new StringBuilder();
        TerrainData data = terrain.terrainData;
        Vector3 tp = terrain.transform.position;
        sb.AppendLine($"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
        sb.AppendLine($"terrain '{terrain.name}' at {tp} size {data.size} heightmap {data.heightmapResolution} alphamap {data.alphamapWidth} data {AssetDatabase.GetAssetPath(data)}");
        float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        for (int l = 0; l < data.alphamapLayers; l++)
        {
            double sum = 0;
            for (int y = 0; y < data.alphamapHeight; y++) for (int x = 0; x < data.alphamapWidth; x++) sum += alpha[y, x, l];
            sb.AppendLine($"  layer {l} {data.terrainLayers[l]?.name}: {100 * sum / (data.alphamapWidth * data.alphamapHeight):F1}%");
        }
        sb.AppendLine($"tree prototypes {data.treePrototypes.Length}, instances {data.treeInstanceCount}");
        var counts = new int[data.treePrototypes.Length];
        foreach (var t in data.treeInstances) counts[t.prototypeIndex]++;
        for (int i = 0; i < data.treePrototypes.Length; i++)
            sb.AppendLine($"  tree {i} {AssetDatabase.GetAssetPath(data.treePrototypes[i].prefab)}: {counts[i]}");
        sb.AppendLine($"detail prototypes {data.detailPrototypes.Length}");
        for (int i = 0; i < data.detailPrototypes.Length; i++)
        {
            var p = data.detailPrototypes[i];
            long n = 0;
            int[,] layer = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, i);
            foreach (int v in layer) n += v;
            sb.AppendLine($"  detail {i} {(p.usePrototypeMesh ? AssetDatabase.GetAssetPath(p.prototype) : AssetDatabase.GetAssetPath(p.prototypeTexture))} total {n}");
        }
        if (root != null)
        {
            sb.AppendLine($"Farm Reference Map at {root.transform.position} rot {root.transform.eulerAngles} scale {root.transform.lossyScale}");
            void Walk(Transform t, int depth)
            {
                var renderers = t.GetComponentsInChildren<Renderer>();
                var colliders = t.GetComponentsInChildren<Collider>();
                Bounds? b = null;
                foreach (var r in renderers) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
                sb.AppendLine($"{new string(' ', depth * 2)}- {t.name} [{t.childCount} children, {renderers.Length} renderers, {colliders.Length} colliders] {(b.HasValue ? B(b.Value) : "")}");
                if (depth < 2) foreach (Transform c in t) Walk(c, depth + 1);
            }
            foreach (Transform c in root.transform) Walk(c, 1);
        }
        sb.AppendLine("other scene roots:");
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go != root) sb.AppendLine($"  - {go.name} at {go.transform.position} [{go.GetComponentsInChildren<Renderer>().Length} renderers]");
        File.WriteAllText(Path.Combine(Out, "map.txt"), sb.ToString());
    }

    internal static Texture2D Render(Camera camera, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        camera.targetTexture = rt;
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
        else camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    internal static Camera TempCamera()
    {
        var go = new GameObject("Survey Camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
        return camera;
    }

    private static void RenderTopDown(Terrain terrain)
    {
        Vector3 tp = terrain.transform.position, size = terrain.terrainData.size;
        var camera = TempCamera();
        try
        {
            camera.orthographic = true;
            float half = Mathf.Max(size.x, size.z) / 2f;
            camera.orthographicSize = half;
            Vector3 centre = tp + new Vector3(size.x / 2f, 0f, size.z / 2f);
            camera.transform.position = centre + Vector3.up * (size.y + 200f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // north (+z) up in the image
            camera.nearClipPlane = 1f; camera.farClipPlane = size.y + 600f;
            int px = 2048;
            var tex = Render(camera, px, px);
            File.WriteAllBytes(Path.Combine(Out, "map_topdown.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllText(Path.Combine(Out, "map_topdown.txt"),
                $"pixel (0,0) = top left; world x = {centre.x - half:F2} + px * {2 * half / px:F4}; world z = {centre.z + half:F2} - py * {2 * half / px:F4}\n" +
                $"terrain covers x {tp.x:F1}..{tp.x + size.x:F1}, z {tp.z:F1}..{tp.z + size.z:F1}\n");
        }
        finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
    }

    private static int Triangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long n = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
        return (int)n;
    }

    private static void WriteHeavy()
    {
        var sb = new StringBuilder();
        var rows = new List<(int tris, string name, string source)>();
        long total = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            int tris = 0;
            if (r is MeshRenderer && r.TryGetComponent(out MeshFilter f)) tris = Triangles(f.sharedMesh);
            else if (r is SkinnedMeshRenderer s) tris = Triangles(s.sharedMesh);
            total += tris;
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(r.gameObject);
            rows.Add((tris, FullName(r.transform), src != null ? AssetDatabase.GetAssetPath(src) : ""));
        }
        sb.AppendLine($"renderers {rows.Count}, triangles {total:N0} (GameObjects only; terrain, terrain trees/details and procedural grass not counted)");
        sb.AppendLine("by source asset:");
        foreach (var g in rows.GroupBy(r => r.source).OrderByDescending(g => g.Sum(r => (long)r.tris)).Take(40))
            sb.AppendLine($"  {g.Sum(r => (long)r.tris),12:N0} tris  {g.Count(),5}x  {(g.Key == "" ? "(scene objects)" : g.Key)}");
        sb.AppendLine("heaviest renderers:");
        foreach (var r in rows.OrderByDescending(r => r.tris).Take(40)) sb.AppendLine($"  {r.tris,10:N0}  {r.name}");
        foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            sb.AppendLine($"terrain {t.name}: trees {t.terrainData.treeInstanceCount}, detail distance {t.detailObjectDistance}, density {t.detailObjectDensity}, tree distance {t.treeDistance}, pixel error {t.heightmapPixelError}");
        sb.AppendLine("lights:");
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            sb.AppendLine($"  {l.type} {FullName(l.transform)} shadows {l.shadows} range {l.range}");
        sb.AppendLine($"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, pipeline {GraphicsSettings.currentRenderPipeline?.name}");
        File.WriteAllText(System.IO.Path.Combine(Out, "heavy.txt"), sb.ToString());
    }

    private static string FullName(Transform t) => t.parent == null ? t.name : FullName(t.parent) + "/" + t.name;

    // Every tree- or bush-like prefab of the candidate packs, side by side on the terrain's far corner (off to the
    // side of the map, removed again afterwards).
    private static void RenderLineup(Terrain terrain)
    {
        var sb = new StringBuilder();
        var prefabs = new List<GameObject>();
        foreach (string folder in Candidates)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = path.ToLowerInvariant();
                if (!(lower.Contains("tree") || lower.Contains("bush") || lower.Contains("oak") || lower.Contains("pine") ||
                      lower.Contains("birch") || lower.Contains("shrub") || lower.Contains("fir"))) continue;
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p != null && p.GetComponentInChildren<Renderer>(true) != null) prefabs.Add(p);
            }
        }
        var holder = new GameObject("Survey Lineup") { hideFlags = HideFlags.HideAndDontSave };
        var camera = TempCamera();
        try
        {
            float x = 0f;
            var placed = new List<(GameObject go, Bounds b)>();
            foreach (var p in prefabs.Take(60))
            {
                var go = (GameObject)UnityEngine.Object.Instantiate(p, holder.transform);
                go.hideFlags = HideFlags.HideAndDontSave;
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(x - b.min.x, -b.min.y, -b.center.z) + new Vector3(0f, 5000f, 0f);
                b.center += new Vector3(x - b.min.x, -b.min.y, -b.center.z) + new Vector3(0f, 5000f, 0f);
                placed.Add((go, b));
                int tris = 0; var shaders = new HashSet<string>();
                foreach (var f in go.GetComponentsInChildren<MeshFilter>()) tris += Triangles(f.sharedMesh);
                foreach (var r in rs) foreach (var m in r.sharedMaterials) shaders.Add(m == null ? "(none)" : m.shader.name);
                sb.AppendLine($"{placed.Count,2}. x {x:F1}..{x + b.size.x:F1}  {AssetDatabase.GetAssetPath(p)}  size {b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1}  tris {tris:N0}  shaders [{string.Join(", ", shaders)}]" +
                          $"  components [{string.Join(", ", go.GetComponentsInChildren<Component>().Select(c => c.GetType().Name).Distinct())}]");
                x += b.size.x + 1.5f;
            }
            if (placed.Count > 0)
            {
                float width = x, height = placed.Max(p => p.b.size.y);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(height * 0.55f, width / 2f / 4f);
                camera.transform.position = new Vector3(width / 2f, 5000f + camera.orthographicSize * 0.95f, -200f);
                camera.transform.rotation = Quaternion.identity;
                camera.farClipPlane = 500f;
                // Rendered in strips so each stays readable; the text file says which x range each prefab has.
                int strips = Mathf.CeilToInt(width / (camera.orthographicSize * 2f * 4f));
                for (int s = 0; s < strips; s++)
                {
                    float stripWidth = camera.orthographicSize * 2f * 4f;
                    camera.transform.position = new Vector3(s * stripWidth + stripWidth / 2f, camera.transform.position.y, -200f);
                    var tex = Render(camera, 2400, 600);
                    File.WriteAllBytes(System.IO.Path.Combine(Out, $"lineup_{s}.png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    sb.AppendLine($"lineup_{s}.png covers x {s * stripWidth:F1}..{(s + 1) * stripWidth:F1}");
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
        File.WriteAllText(System.IO.Path.Combine(Out, "lineup.txt"), sb.ToString());
    }
}
