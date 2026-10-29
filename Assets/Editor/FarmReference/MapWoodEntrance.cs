using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the wood north of the barn and coop from a wall of trees into a way into a forest: along the fence the trees
/// stand far apart, and they close in further back (<see cref="EdgeSpacing"/> at the fence down to
/// <see cref="DeepSpacing"/> at <see cref="EdgeDepth"/>). The colourful broadleaf trees are kept first so the edge
/// reads as an inviting margin, the heavy pines (PineTree, TallPineTree, BushyPineTree: thousands of cut-out needle
/// cards each, the main cost of looking at the wood) go first. Behind the edge, and across the rest of the map, every
/// other heavy pine becomes a fir of the same height (BluBlu Fir, a fraction of the cost). Menu; Ctrl+Z undoes it.
/// </summary>
public static class MapWoodEntrance
{
    private const string FirPath = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/Fir.prefab";
    private static readonly string[] Groups = { "New Tree Designs/Wood By The Barn", "New Tree Designs/Scenery Swaps", "Scenery Replacements" };
    private static readonly string[] HeavyPines = { "PineTree", "TallPineTree", "BushyPineTree" };
    // the outer fence (as FixedTreeDetail); the wood is the stretch north of it
    private static readonly Rect PlayArea = Rect.MinMaxRect(59f, -48f, 129f, 36f);
    private const float EdgeDepth = 22f, EdgeSpacing = 9f, DeepSpacing = 5.5f, PineSwapShare = .5f;

    [MenuItem("Tools/Chick/Trees/Open Up The Wood Edge")]
    public static string Run()
    {
        GameObject map = GameObject.Find("Map");
        Transform trees = map != null ? map.transform.Find("Trees") : null;
        if (GameObject.Find("Farm Reference Map") == null || trees == null) return "The open scene has no 'Farm Reference Map' / 'Map/Trees'.";
        var fir = AssetDatabase.LoadAssetAtPath<GameObject>(FirPath);
        if (fir == null) return "missing " + FirPath;
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(fir);
        float firHeight = Measure(probe).size.y;
        Object.DestroyImmediate(probe);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Open up the wood edge");
        int undo = Undo.GetCurrentGroup();

        var all = new List<(Transform t, Bounds b, float d, bool north)>();
        foreach (string g in Groups)
        {
            Transform group = trees.Find(g);
            if (group == null) continue;
            foreach (Transform t in group)
            {
                Bounds b = Measure(t.gameObject);
                bool north = b.center.z > PlayArea.yMax && b.center.x > PlayArea.xMin - 10f && b.center.x < PlayArea.xMax + 10f;
                all.Add((t, b, Distance(b), north));
            }
        }

        // the edge: keep greedily, colourful trees first and heavy pines last, never closer than the local spacing
        var rng = new System.Random(3110);
        var kept = all.Where(a => !a.north || a.d >= EdgeDepth).Select(a => a.b.center).ToList();
        var edge = all.Where(a => a.north && a.d < EdgeDepth)
            .OrderBy(a => Priority(a.t.name)).ThenBy(a => rng.Next()).ToList();
        int removed = 0;
        foreach (var a in edge)
        {
            float spacing = Mathf.Lerp(EdgeSpacing, DeepSpacing, a.d/EdgeDepth);
            if (kept.Any(k => Flat(k - a.b.center) < spacing)) { Undo.DestroyObjectImmediate(a.t.gameObject); removed++; }
            else kept.Add(a.b.center);
        }

        // the rest: every other heavy pine becomes a fir as tall
        int swapped = 0;
        foreach (var a in all.Where(a => a.t != null && HeavyPines.Contains(a.t.name)).OrderBy(a => a.b.center.x).ThenBy(a => a.b.center.z))
        {
            if (rng.NextDouble() > PineSwapShare) continue;
            Transform old = a.t;
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(fir, old.parent);
            Undo.RegisterCreatedObjectUndo(tree, "Open up the wood edge");
            tree.name = "Fir";
            tree.transform.SetPositionAndRotation(old.position, Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
            tree.transform.localScale = Vector3.one*Mathf.Clamp(a.b.size.y/firHeight, .6f, 2.2f);
            Undo.DestroyObjectImmediate(old.gameObject);
            swapped++;
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        var report = new StringBuilder();
        report.AppendLine($"wood edge: removed {removed} of {edge.Count} trees within {EdgeDepth} m of the fence");
        report.AppendLine($"swapped {swapped} heavy pines for firs");
        foreach (var k in all.Where(a => a.t != null).GroupBy(a => a.t.name).OrderByDescending(k => k.Count()))
            report.AppendLine($"  {k.Count(),3} x {k.Key} left");
        report.Append("save the scene to keep it (Ctrl+Z undoes it)");
        Debug.Log("Open up the wood edge: " + report);
        return report.ToString();
    }

    // After Run: the line where the full wood starts is frayed (a few trees just behind it thinned out) and a handful of
    // light trees are stood out in the clearing, in ones and twos, so the wood thins out toward the fence gradually.
    private static readonly (string path, int weight)[] Strays =
    {
        ("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/Fir.prefab", 2),
        ("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Tree_Color.prefab", 2),
        ("Assets/Art/FruitTrees/Prefabs/AutumnTree.prefab", 1),
        ("Assets/Art/FruitTrees/Prefabs/GoldenTree.prefab", 1),
    };
    private const float FrayFrom = EdgeDepth, FrayTo = 32f, FraySpacing = 5.5f, StrayFrom = 11f, StrayTo = 20f, StraySpacing = 8f;
    private const int StrayCount = 9;

    [MenuItem("Tools/Chick/Trees/Feather The Wood Edge")]
    public static string Feather()
    {
        GameObject map = GameObject.Find("Map");
        Transform trees = map != null ? map.transform.Find("Trees") : null;
        Transform wood = trees != null ? trees.Find(Groups[0]) : null;
        if (GameObject.Find("Farm Reference Map") == null || wood == null) return "The open scene has no 'Farm Reference Map' / wood by the barn.";
        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Feather the wood edge");
        int undo = Undo.GetCurrentGroup();
        var rng = new System.Random(3111);

        var all = new List<(Transform t, Bounds b, float d, bool north)>();
        foreach (string g in Groups)
        {
            Transform group = trees.Find(g);
            if (group == null) continue;
            foreach (Transform t in group)
            {
                Bounds b = Measure(t.gameObject);
                bool north = b.center.z > PlayArea.yMax && b.center.x > PlayArea.xMin - 10f && b.center.x < PlayArea.xMax + 10f;
                all.Add((t, b, Distance(b), north));
            }
        }

        // fray: just behind the edge, keep the trees FraySpacing apart (shrinking to none at FrayTo), heavy pines go first
        var kept = all.Where(a => !(a.north && a.d >= FrayFrom && a.d < FrayTo)).Select(a => a.b.center).ToList();
        int frayed = 0;
        foreach (var a in all.Where(a => a.north && a.d >= FrayFrom && a.d < FrayTo).OrderBy(a => Priority(a.t.name)).ThenBy(a => rng.Next()))
        {
            float spacing = Mathf.Lerp(FraySpacing, 0f, (a.d - FrayFrom)/(FrayTo - FrayFrom));
            if (kept.Any(k => Flat(k - a.b.center) < spacing)) { Undo.DestroyObjectImmediate(a.t.gameObject); frayed++; }
            else kept.Add(a.b.center);
        }

        // strays: open ground in the clearing, clear of trees, rocks and props
        var prefabs = Strays.Select(s => (prefab: AssetDatabase.LoadAssetAtPath<GameObject>(s.path), s.weight)).Where(s => s.prefab != null).ToArray();
        int planted = 0;
        for (int attempt = 0; attempt < 3000 && planted < StrayCount; attempt++)
        {
            var p = new Vector3(Mathf.Lerp(PlayArea.xMin + 4f, PlayArea.xMax - 4f, (float)rng.NextDouble()), 0f,
                PlayArea.yMax + Mathf.Lerp(StrayFrom, StrayTo, (float)rng.NextDouble()));
            if (kept.Any(k => Flat(k - p) < StraySpacing)) continue;
            p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            if (Physics.OverlapCapsule(p + Vector3.up*.6f, p + Vector3.up*6f, 1.4f, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;
            int roll = rng.Next(prefabs.Sum(s => s.weight));
            var pick = prefabs.First(s => (roll -= s.weight) < 0).prefab;
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(pick, wood);
            Undo.RegisterCreatedObjectUndo(tree, "Feather the wood edge");
            tree.name = pick.name;
            tree.transform.SetPositionAndRotation(p - Vector3.up*.05f, Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
            tree.transform.localScale = Vector3.one*Mathf.Lerp(.85f, 1.15f, (float)rng.NextDouble());
            kept.Add(p);
            planted++;
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        string report = $"frayed {frayed} trees between {FrayFrom} and {FrayTo} m, stood {planted} trees out in the clearing";
        Debug.Log("Feather the wood edge: " + report);
        return report;
    }

    // Tall IL3DN pines (18 m) in small groups far behind the woods to the north, west and east, rising over the canopy.
    // Not to the south, where the painted mountains show over the hill.
    private const string TallPinePath = "Assets/IL3DN/Graphics/Prefabs/IL3DN_Tree_Pine_03_OneMesh.prefab";
    private const float BackFrom = 38f, BackTo = 75f, BackMargin = 14f;
    private const int BackGroups = 5;

    [MenuItem("Tools/Chick/Trees/Plant Tall Pines Behind The Woods")]
    public static string PlantTallPines()
    {
        GameObject map = GameObject.Find("Map");
        Transform trees = map != null ? map.transform.Find("Trees") : null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TallPinePath);
        if (GameObject.Find("Farm Reference Map") == null || trees == null || prefab == null) return "missing 'Map/Trees' or " + TallPinePath;
        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Plant tall pines");
        int undo = Undo.GetCurrentGroup();
        Transform group = trees.Find("Tall Back Pines");
        if (group != null) Undo.DestroyObjectImmediate(group.gameObject);
        group = new GameObject("Tall Back Pines").transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Plant tall pines");
        group.SetParent(trees, false);

        var rng = new System.Random(3112);
        var placed = new List<Vector3>();
        int groups = 0;
        for (int attempt = 0; attempt < 4000 && groups < BackGroups; attempt++)
        {
            var c = new Vector3(origin.x + BackMargin + (float)rng.NextDouble()*(data.size.x - 2f*BackMargin), 0f,
                origin.z + BackMargin + (float)rng.NextDouble()*(data.size.z - 2f*BackMargin));
            float d = Distance(new Bounds(c, Vector3.zero));
            if (d < BackFrom || d > BackTo || c.z < PlayArea.yMin) continue;
            if (placed.Any(p => Flat(p - c) < 25f)) continue;
            int size = 2 + rng.Next(2), made = 0;
            for (int i = 0; i < 20 && made < size; i++)
            {
                var p = made == 0 ? c : c + Quaternion.Euler(0f, (float)rng.NextDouble()*360f, 0f)*Vector3.forward*Mathf.Lerp(4f, 7f, (float)rng.NextDouble());
                if (placed.Any(q => Flat(q - p) < 4f)) continue;
                float steep = data.GetSteepness((p.x - origin.x)/data.size.x, (p.z - origin.z)/data.size.z);
                if (steep > 25f) continue;
                p.y = terrain.SampleHeight(p) + origin.y;
                // the trunk clear of rocks, props and other trunks (crowns may overlap, they stand above them)
                if (Physics.OverlapCapsule(p + Vector3.up*.6f, p + Vector3.up*3f, .8f, ~0, QueryTriggerInteraction.Ignore).Any(col => !(col is TerrainCollider))) continue;
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                Undo.RegisterCreatedObjectUndo(tree, "Plant tall pines");
                tree.name = prefab.name;
                tree.transform.SetPositionAndRotation(p - Vector3.up*.1f, Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
                tree.transform.localScale = Vector3.one*Mathf.Lerp(.8f, 1.1f, (float)rng.NextDouble());
                placed.Add(p);
                made++;
            }
            if (made > 0) groups++;
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        string report = $"planted {placed.Count} tall pines in {groups} groups: " + string.Join(", ", placed.Select(p => $"({p.x:0}, {p.z:0})"));
        Debug.Log("Plant tall pines: " + report);
        return report;
    }

    // Light trees on either side of the wood (it stood as a lone block in open grass), thinning out away from it: next
    // to the wood SideNearSpacing apart, SideWidth further out SideFarSpacing, and none on paths, rocks or steep ground.
    private static readonly (string path, int weight)[] SideTrees =
    {
        ("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/Fir.prefab", 3),
        ("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Tree_Color.prefab", 3),
        ("Assets/IL3DN/Graphics/Prefabs/IL3DN_Tree_Pine_01_OneMesh.prefab", 1),
        ("Assets/Art/FruitTrees/Prefabs/AutumnTree.prefab", 1),
        ("Assets/Art/FruitTrees/Prefabs/GoldenTree.prefab", 1),
    };
    private const float WoodWest = 40f, WoodEast = 150f, SideWidth = 38f, SideZMin = 42f, SideZMax = 76f;
    private const float SideNearSpacing = 6f, SideFarSpacing = 12f, SideMaxSlope = 35f;
    private const int SideTreesEach = 11;

    [MenuItem("Tools/Chick/Trees/Plant Thin Woods Beside The Wood")]
    public static string PlantSides()
    {
        GameObject map = GameObject.Find("Map");
        Transform trees = map != null ? map.transform.Find("Trees") : null;
        if (GameObject.Find("Farm Reference Map") == null || trees == null) return "The open scene has no 'Farm Reference Map' / 'Map/Trees'.";
        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        bool[] path = data.terrainLayers.Select(l => l != null && (l.name.Contains("Trodden") || l.name.Contains("Path") || l.name.Contains("Tilled"))).ToArray();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Plant thin woods");
        int undo = Undo.GetCurrentGroup();
        Transform group = trees.Find("Wood Sides");
        if (group != null) Undo.DestroyObjectImmediate(group.gameObject);
        group = new GameObject("Wood Sides").transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Plant thin woods");
        group.SetParent(trees, false);

        // every tree already standing, as a spacing obstacle
        var standing = trees.GetComponentsInChildren<Transform>()
            .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).Select(t => t.position).ToList();
        var prefabs = SideTrees.Select(s => (prefab: AssetDatabase.LoadAssetAtPath<GameObject>(s.path), s.weight)).Where(s => s.prefab != null).ToArray();
        var rng = new System.Random(3113);
        var report = new StringBuilder();
        foreach (int side in new[] { -1, 1 })
        {
            int planted = 0;
            for (int attempt = 0; attempt < 4000 && planted < SideTreesEach; attempt++)
            {
                // out from the wood's side, more often close to it
                float outward = SideWidth*Mathf.Pow((float)rng.NextDouble(), 1.4f);
                var p = new Vector3(side < 0 ? WoodWest - outward : WoodEast + outward, 0f, Mathf.Lerp(SideZMin, SideZMax, (float)rng.NextDouble()));
                float spacing = Mathf.Lerp(SideNearSpacing, SideFarSpacing, outward/SideWidth);
                if (standing.Any(s => Flat(s - p) < spacing)) continue;
                float u = (p.x - origin.x)/data.size.x, v = (p.z - origin.z)/data.size.z;
                if (u < .03f || u > .97f || v < .03f || v > .97f || data.GetSteepness(u, v) > SideMaxSlope) continue;
                int ax = Mathf.Clamp(Mathf.RoundToInt(u*data.alphamapWidth), 0, data.alphamapWidth - 1), ay = Mathf.Clamp(Mathf.RoundToInt(v*data.alphamapHeight), 0, data.alphamapHeight - 1);
                float onPath = 0f;
                for (int l = 0; l < path.Length; l++) if (path[l]) onPath += alpha[ay, ax, l];
                if (onPath > .15f) continue;
                p.y = terrain.SampleHeight(p) + origin.y;
                if (Physics.OverlapCapsule(p + Vector3.up*.6f, p + Vector3.up*5f, 1.4f, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;
                int roll = rng.Next(prefabs.Sum(s => s.weight));
                var pick = prefabs.First(s => (roll -= s.weight) < 0).prefab;
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(pick, group);
                Undo.RegisterCreatedObjectUndo(tree, "Plant thin woods");
                tree.name = pick.name;
                tree.transform.SetPositionAndRotation(p - Vector3.up*.05f, Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
                tree.transform.localScale = Vector3.one*Mathf.Lerp(.85f, 1.2f, (float)rng.NextDouble());
                standing.Add(p);
                planted++;
            }
            report.Append($"{(side < 0 ? "west" : "east")}: {planted} trees; ");
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log("Plant thin woods: " + report);
        return report.ToString();
    }

    private static int Priority(string kind)
    {
        if (kind == "AutumnTree" || kind == "CrimsonTree" || kind == "GoldenTree") return 0;
        if (HeavyPines.Contains(kind)) return 3;
        if (kind == "LushTree" || kind == "SM_Tree_Color") return 1;
        return 2;
    }

    // across the ground from the play area to the nearest side of the tree
    private static float Distance(Bounds b)
    {
        float dx = Mathf.Max(PlayArea.xMin - b.max.x, 0f, b.min.x - PlayArea.xMax);
        float dz = Mathf.Max(PlayArea.yMin - b.max.z, 0f, b.min.z - PlayArea.yMax);
        return Mathf.Sqrt(dx*dx + dz*dz);
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static Bounds Measure(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
