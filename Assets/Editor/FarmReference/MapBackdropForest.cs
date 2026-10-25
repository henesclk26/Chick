using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the wooded hills around the map (BackdropForest, under Map/Backdrop Forest): a square of scenery reaching
/// 35 m out east and west and 90 m north and south, built from a seed when the scene opens, so nothing big is stored.
/// Makes its two materials in Assets/Art/Backdrop (the ground copies the map's terrain material, draws the four
/// layers that cover most of the map's edge so the two meet unseen, and four others for its own forest floor), takes away the terrain hills an earlier version
/// put there (Map/Backdrop Hills and their terrain data), puts the game's fog back to how it was (60..1000 m: the haze
/// is now the scenery's own, the map stays clear), puts the painted mountain to the south back if it is missing and
/// lowers the sample-tree platform out of sight behind the southern ridge. Run again, it rebuilds; "remove" takes the
/// scenery away. Menu, or Temp/MapBackdropForest/request.txt ("apply" or
/// "remove") while the Editor is open (report and pictures in that folder).
/// </summary>
[InitializeOnLoad]
public static class MapBackdropForest
{
    private const string GroupName = "Backdrop Forest";
    private const string OldGroupName = "Backdrop Hills";
    private const string OldFolder = "Assets/Art/Terrain/Backdrop";
    private const string Folder = "Assets/Art/Backdrop";
    private const string GroundLayer = "RichGreen";
    private const float FogStart = 60f, FogEnd = 1000f;
    // The scenery's own haze: from this share of the way out, up to this much of the horizon colour.
    private const float HazeStart = 0.35f, HazeMax = 0.55f;
    // The sample-tree platform's top, out of sight below the scenery's southern ridge.
    private const float PlatformTop = -40f;
    private const string Out = "Temp/MapBackdropForest";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static MapBackdropForest() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        if (what == "pictures") { Directory.CreateDirectory(Out); Pictures(Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First()); }
        else Run(what != "remove");
    }

    [MenuItem("Tools/Chick/Map/Add Backdrop Forest")]
    private static void Apply() => Run(true);

    [MenuItem("Tools/Chick/Map/Remove Backdrop Forest")]
    private static void Remove() => Run(false);

    private static void Run(bool apply)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            GameObject map = GameObject.Find("Map");
            if (GameObject.Find("Farm Reference Map") == null || map == null) { report.AppendLine("The open scene has no 'Farm Reference Map' / 'Map'."); return; }
            Terrain main = Terrain.activeTerrains.Where(t => !t.name.StartsWith(OldGroupName))
                .OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(apply ? "Add backdrop forest" : "Remove backdrop forest");
            int undo = Undo.GetCurrentGroup();
            foreach (string name in new[] { OldGroupName, GroupName })
            {
                Transform old = map.transform.Find(name);
                if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); report.AppendLine($"removed Map/{name}"); }
            }
            if (AssetDatabase.IsValidFolder(OldFolder)) { AssetDatabase.DeleteAsset(OldFolder); report.AppendLine($"deleted {OldFolder}"); }
            SetFog(report);
            if (apply) { PaintedMountain(report); LowerPlatform(main, report); }

            if (apply)
            {
                var (ground, trees) = Materials(main, report);
                var go = new GameObject(GroupName) { layer = main.gameObject.layer };
                go.transform.SetParent(map.transform, false);
                go.transform.position = Vector3.zero;
                Undo.RegisterCreatedObjectUndo(go, "Add backdrop forest");
                var forest = go.AddComponent<BackdropForest>();
                forest.Setup(main, ground, trees, EdgeLayers(main, report));
                EditorUtility.SetDirty(forest);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                forest.Rebuild();
                report.AppendLine($"backdrop forest: {forest.TreeCount} trees, {forest.TriangleCount} triangles in {go.transform.childCount} meshes, built in {watch.ElapsedMilliseconds} ms");
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(map.scene);
            AssetDatabase.SaveAssets();
            if (apply) Pictures(main);
            report.AppendLine("save the scene to keep it");
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Backdrop forest: " + report);
        }
    }

    private static (Material ground, Material trees) Materials(Terrain main, StringBuilder report)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
        Material Make(string name, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetFloat("_HazeStart", HazeStart);
            m.SetFloat("_HazeMax", HazeMax);
            return m;
        }
        Material ground = Make("M_Backdrop_Ground", "Farm/Backdrop Ground"), trees = Make("M_Backdrop_Trees", "Farm/Backdrop");
        // The ground draws the map's layers exactly as the map's terrain material does: same textures, same settings.
        Material map = main.materialTemplate;
        int copied = 0;
        if (map != null)
            for (int i = 0; i < ground.shader.GetPropertyCount(); i++)
            {
                string name = ground.shader.GetPropertyName(i);
                if (!map.HasProperty(name) || name == "_LayerIds") continue;
                switch (ground.shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: ground.SetTexture(name, map.GetTexture(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color: ground.SetColor(name, map.GetColor(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: ground.SetVector(name, map.GetVector(name)); break;
                    default: ground.SetFloat(name, map.GetFloat(name)); break;
                }
                copied++;
            }
        report.AppendLine($"ground material: {copied} settings copied from {(map != null ? map.name : "-")}");
        EditorUtility.SetDirty(ground);
        EditorUtility.SetDirty(trees);
        return (ground, trees);
    }

    // The eight map layers the scenery's ground draws: the four that cover most of the map's edges (the grass that covers
    // most first), then the forest floor's own.
    private static int[] EdgeLayers(Terrain main, StringBuilder report)
    {
        TerrainData data = main.terrainData;
        int w = data.alphamapWidth, h = data.alphamapHeight, layers = data.terrainLayers.Length;
        var cover = new float[layers];
        foreach (float[,,] row in new[] { data.GetAlphamaps(0, 0, w, 1), data.GetAlphamaps(0, h - 1, w, 1), data.GetAlphamaps(0, 0, 1, h), data.GetAlphamaps(w - 1, 0, 1, h) })
            for (int a = 0; a < row.GetLength(0); a++)
                for (int b = 0; b < row.GetLength(1); b++)
                    for (int l = 0; l < layers; l++) cover[l] += row[a, b, l];
        var ranked = Enumerable.Range(0, layers).OrderByDescending(l => cover[l]).ToList();
        int grass = ranked.First(l => data.terrainLayers[l] != null && !BackdropForest.IsEarth(data.terrainLayers[l].name));
        var chosen = new[] { grass }.Concat(ranked.Where(l => l != grass)).Take(4).ToArray();
        float total = cover.Sum();
        report.AppendLine("edge layers: " + string.Join(", ", chosen.Select(l => $"{data.terrainLayers[l].name} {cover[l] / total:P0}")) +
                          $" (together {chosen.Sum(l => cover[l]) / total:P0} of the map's edge)");
        // The forest floor's own four, unlike the map: olive grass, dry grass, grass with earth, bare earth.
        int Find(params string[] names)
        {
            foreach (string n in names)
            {
                int i = System.Array.FindIndex(data.terrainLayers, l => l != null && l.name == n);
                if (i >= 0) return i;
            }
            return chosen[0];
        }
        int[] forest = { Find("RichGreen_Olive", "RichGreen_Shade"), Find("RichGreen_Dry", "RichGreen_Golden"), Find("RichGreen_Earth", "WornMeadow"), Find("OldEarthAccent", "TroddenEarth") };
        report.AppendLine("forest layers: " + string.Join(", ", forest.Select(l => data.terrainLayers[l].name)));
        return chosen.Concat(forest).ToArray();
    }

    // The painted mountain to the south (FarmReferenceTools.MountainBackdropV1), put back if it is missing.
    private static void PaintedMountain(StringBuilder report)
    {
        if (GameObject.Find(FarmReferenceTools.MountainBackdropV1.ObjectName) != null) { report.AppendLine("painted mountain already there"); return; }
        report.AppendLine(FarmReferenceTools.MountainBackdropV1.Build());
    }

    // The sample-tree platform outside the map, down out of sight behind the scenery's southern ridge.
    private static void LowerPlatform(Terrain main, StringBuilder report)
    {
        Vector3 o = main.transform.position, s = main.terrainData.size;
        Collider platform = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).FirstOrDefault(c =>
            c.name == "Plane" && !(c is TerrainCollider) && c.attachedRigidbody == null &&
            !(c.bounds.center.x > o.x && c.bounds.center.x < o.x + s.x && c.bounds.center.z > o.z && c.bounds.center.z < o.z + s.z));
        if (platform == null) { report.AppendLine("no sample-tree platform outside the map"); return; }
        Physics.SyncTransforms();
        Renderer surface = platform.GetComponent<Renderer>();
        Bounds b = surface != null ? surface.bounds : platform.bounds;
        float top = b.max.y, lift = PlatformTop - top;
        if (Mathf.Abs(lift) < 0.05f) { report.AppendLine($"platform already at {PlatformTop}"); return; }
        var things = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t != platform.transform && !t.IsChildOf(platform.transform) && t.GetComponent<Terrain>() == null && t.childCount <= 40 &&
                        t.position.x > b.min.x - 1f && t.position.x < b.max.x + 1f && t.position.z > b.min.z - 1f && t.position.z < b.max.z + 1f &&
                        t.position.y > top - 1f && t.position.y < top + 4f &&
                        (t.GetComponentInChildren<Renderer>(true) != null || t.GetComponentInChildren<Collider>(true) != null))
            .OrderBy(t => { int d = 0; for (Transform p = t; p.parent != null; p = p.parent) d++; return d; }).ToList();
        var moving = new System.Collections.Generic.List<Transform> { platform.transform };
        foreach (Transform t in things) if (!moving.Any(m => t.IsChildOf(m))) moving.Add(t);
        foreach (Transform t in moving)
        {
            Undo.RecordObject(t, "Add backdrop forest");
            t.position += Vector3.up * lift;
        }
        Physics.SyncTransforms();
        report.AppendLine($"platform moved {lift:+0.0;-0.0} m to {PlatformTop} with {moving.Count - 1} things on it");
    }

    private static void SetFog(StringBuilder report)
    {
        var controller = Object.FindFirstObjectByType<TimeOfDayVisualController>();
        if (controller == null) return;
        var so = new SerializedObject(controller);
        so.FindProperty("fogStart").floatValue = FogStart;
        so.FindProperty("fogEnd").floatValue = FogEnd;
        so.ApplyModifiedProperties();
        report.AppendLine($"game fog back to {FogStart}..{FogEnd} m");
    }

    private static void Pictures(Terrain main)
    {
        var sky = new GameViewCapture.SkyState();
        try
        {
            sky.Apply(new StringBuilder());
            foreach (var v in GameViewCapture.Views(main))
            {
                Texture2D tex = GameViewCapture.Shot(v.from, v.at);
                File.WriteAllBytes(Path.Combine(Out, v.name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            // From above, square on, as in the Scene view.
            Vector3 o = main.transform.position, s = main.terrainData.size;
            Vector3 centre = o + new Vector3(s.x / 2f, 0f, s.z / 2f);
            Texture2D top = GameViewCapture.Shot(centre + new Vector3(0f, 330f, -1f), centre);
            File.WriteAllBytes(Path.Combine(Out, "from_above.png"), top.EncodeToPNG());
            Object.DestroyImmediate(top);
            // Into the forest from the map's edge.
            Vector3 edge = new Vector3(o.x + s.x * 0.4f, 0f, o.z + s.z - 6f);
            edge.y = main.SampleHeight(edge) + o.y + 2f;
            Texture2D into = GameViewCapture.Shot(edge, edge + new Vector3(0f, 2f, 30f));
            File.WriteAllBytes(Path.Combine(Out, "edge_north.png"), into.EncodeToPNG());
            Object.DestroyImmediate(into);
            edge = new Vector3(o.x + s.x - 8f, 0f, o.z + s.z * 0.5f);
            edge.y = main.SampleHeight(edge) + o.y + 2f;
            into = GameViewCapture.Shot(edge, edge + new Vector3(30f, 2f, 5f));
            File.WriteAllBytes(Path.Combine(Out, "edge_east.png"), into.EncodeToPNG());
            Object.DestroyImmediate(into);
            // What the old farmer on the porch looks at.
            Transform farmer = GameObject.Find("SM_Chr_Farmer_Male_Old_01")?.transform;
            if (farmer != null)
            {
                Vector3 facing = Vector3.ProjectOnPlane(farmer.forward, Vector3.up).normalized;
                Vector3 eye = farmer.position + Vector3.up * 1.6f + facing * 0.6f;
                into = GameViewCapture.Shot(eye, eye + facing * 50f + Vector3.up * 3f);
                File.WriteAllBytes(Path.Combine(Out, "farmer_view.png"), into.EncodeToPNG());
                Object.DestroyImmediate(into);
            }
            // Where the map's ground meets the scenery's, close up at a chick's height, along each side.
            var seams = new (string name, Vector3 at, Vector3 outward)[]
            {
                ("seam_north", new Vector3(o.x + s.x * 0.55f, 0f, o.z + s.z), Vector3.forward), ("seam_south", new Vector3(o.x + s.x * 0.45f, 0f, o.z), Vector3.back),
                ("seam_west", new Vector3(o.x, 0f, o.z + s.z * 0.3f), Vector3.left), ("seam_east", new Vector3(o.x + s.x, 0f, o.z + s.z * 0.6f), Vector3.right),
            };
            foreach (var (name, at, outward) in seams)
            {
                // Straight down across the edge: both sides lit alike, so only the ground itself differs.
                Camera overhead = MapSurvey.TempCamera();
                try
                {
                    overhead.orthographic = true;
                    overhead.orthographicSize = 15f;
                    overhead.farClipPlane = 300f;
                    overhead.clearFlags = CameraClearFlags.Skybox;
                    Vector3 c = new Vector3(at.x, main.SampleHeight(at) + o.y, at.z) + outward * 10f;
                    overhead.transform.SetPositionAndRotation(c + Vector3.up * 120f, Quaternion.LookRotation(Vector3.down, outward));
                    Texture2D down = MapSurvey.Render(overhead, 600, 600);
                    File.WriteAllBytes(Path.Combine(Out, name + "_top.png"), down.EncodeToPNG());
                    Object.DestroyImmediate(down);
                }
                finally { Object.DestroyImmediate(overhead.gameObject); }
                Vector3 from = at - outward * 6f + Vector3.Cross(outward, Vector3.up) * 3f;
                from.y = main.SampleHeight(from) + o.y + 1.2f;
                Vector3 look = at + outward * 3f;
                look.y = main.SampleHeight(at) + o.y;
                into = GameViewCapture.Shot(from, look);
                File.WriteAllBytes(Path.Combine(Out, name + ".png"), into.EncodeToPNG());
                Object.DestroyImmediate(into);
            }
        }
        finally { sky.Restore(); }
    }
}
