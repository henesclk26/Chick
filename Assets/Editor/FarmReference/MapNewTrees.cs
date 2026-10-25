using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Brings the new tree designs (build_fruit_trees.py --new-designs and --old-fruitless) onto the map. In the dense
/// wood north of the barn (and the clump west of it) about half the scenery trees are swapped for them: pines for the
/// pine trees, the full broadleaf designs (LushTree, CrimsonTree, AutumnTree, GoldenTree) for the round ones, each
/// sized to the tree it replaces. Over the rest of the map a share of the scenery trees are swapped the same way and
/// small groups of the lighter designs (BlossomTree, OliveTree, the old fruitless apple, cherry and pear) are planted
/// on open meadow away from the farm, the paths, the edges and anything standing. A swapped tree is only switched off,
/// so "Remove" (or running it again, which removes first) brings it back. The new trees are under
/// Map/Trees/New Tree Designs. Menu, or Temp/MapNewTrees/request.txt ("place" or "remove") while the Editor is open
/// (report and pictures in that folder).
/// </summary>
[InitializeOnLoad]
public static class MapNewTrees
{
    private const string PrefabFolder = "Assets/Art/FruitTrees/Prefabs/";
    private const string GroupName = "New Tree Designs";
    private const string Out = "Temp/MapNewTrees";
    private const string Request = Out + "/request.txt";
    private static readonly string[] SceneryGroups = { "Scenery Tree", "Hill Expansion Scenery" };
    private static readonly (string tree, int weight)[] Pines = { ("PineTree", 3), ("TallPineTree", 2), ("BushyPineTree", 2) };
    private static readonly (string tree, int weight)[] FullBroadleaf = { ("LushTree", 3), ("CrimsonTree", 2), ("AutumnTree", 2), ("GoldenTree", 1) };
    private static readonly (string tree, int weight)[] LightBroadleaf =
        { ("LushTree", 2), ("AutumnTree", 2), ("GoldenTree", 2), ("OliveTree", 2), ("CrimsonTree", 1), ("BlossomTree", 1) };
    // Planted in small groups of one kind on open meadow.
    private static readonly string[] GroupTrees =
        { "BlossomTree", "OliveTree", "OldAppleTree", "OldCherryTree", "OldPearTree", "BlossomTree", "OliveTree", "AutumnTree", "GoldenTree", "PineTree", "TallPineTree" };
    private const float WoodShare = 0.5f, ElsewhereShare = 0.3f;
    private const int MeadowGroups = 11;
    private const float Spacing = 7f, EdgeMargin = 18f, FarmMargin = 8f;
    private static double nextPoll;

    static MapNewTrees() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        if (what == "remove") Run(false); else Run(true);
    }

    [MenuItem("Tools/Chick/Trees/Place New Tree Designs On Map")]
    private static void Place() => Run(true);

    [MenuItem("Tools/Chick/Trees/Remove New Tree Designs From Map")]
    private static void Remove() => Run(false);

    private static void Run(bool place)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            GameObject map = GameObject.Find("Map");
            if (GameObject.Find("Farm Reference Map") == null || map == null) { report.AppendLine("The open scene has no 'Farm Reference Map' / 'Map'."); return; }
            Transform trees = map.transform.Find("Trees");
            if (trees == null) { report.AppendLine("No Map/Trees."); return; }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(place ? "Place new tree designs" : "Remove new tree designs");
            int undo = Undo.GetCurrentGroup();
            int restored = RemoveOurs(trees);
            report.AppendLine($"removed the earlier new trees, {restored} scenery trees switched back on");
            if (place) PlaceAll(map.transform, trees, report);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(map.scene);
            report.AppendLine("save the scene to keep it (Ctrl+Z undoes it)");
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("New tree designs: " + report);
        }
    }

    private static int RemoveOurs(Transform trees)
    {
        Transform group = trees.Find(GroupName);
        if (group == null) return 0;
        int restored = 0;
        var ours = group.GetComponentsInChildren<Transform>(true).Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToList();
        foreach (Transform old in SceneryTrees(trees, true))
        {
            if (old.gameObject.activeSelf) continue;
            if (!ours.Any(o => Flat(o.position - old.position) < 0.05f)) continue;
            Undo.RecordObject(old.gameObject, "Remove new tree designs");
            old.gameObject.SetActive(true);
            restored++;
        }
        Undo.DestroyObjectImmediate(group.gameObject);
        return restored;
    }

    // The scenery trees (outermost prefab instances in the scenery groups), switched off ones too when asked.
    private static List<Transform> SceneryTrees(Transform trees, bool inactiveToo)
    {
        var list = new List<Transform>();
        foreach (string g in SceneryGroups)
        {
            Transform group = trees.Find(g);
            if (group == null) continue;
            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
                if (t != group && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && (inactiveToo || t.gameObject.activeInHierarchy))
                    list.Add(t);
        }
        return list;
    }

    private static void PlaceAll(Transform map, Transform trees, StringBuilder report)
    {
        FruitTreeSamples.EnsureMaterials();
        var prefabs = new Dictionary<string, (GameObject prefab, float height)>();
        foreach (string name in FruitTreeSamples.PlainTrees)
        {
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + name + ".prefab") ?? FruitTreeSamples.EnsurePrefab(name);
            if (p == null) { report.AppendLine("missing prefab " + name); continue; }
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(p);
            Bounds b = Measure(probe);
            Object.DestroyImmediate(probe);
            prefabs[name] = (p, b.size.y);
            int tris = p.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh != null ? m.sharedMesh.triangles.Length / 3 : 0);
            report.AppendLine($"  {name}: {b.size.y:0.0} m, {tris} triangles");
        }

        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
        var ground = new Ground(terrain);
        Transform farmRoot = GameObject.Find("Farm Reference Map").transform;
        Bounds farm = FarmBounds(farmRoot);
        Vector3 barn = GameObject.Find("SM_Bld_Barn_01")?.transform.position ?? farm.center;
        report.AppendLine($"farm {farm.min.x:0}..{farm.max.x:0} x {farm.min.z:0}..{farm.max.z:0}, barn {barn}");

        var group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Place new tree designs");
        group.SetParent(trees, false);
        var wood = new GameObject("Wood By The Barn").transform; wood.SetParent(group, false);
        var swapped = new GameObject("Scenery Swaps").transform; swapped.SetParent(group, false);
        var meadow = new GameObject("Meadow Groups").transform; meadow.SetParent(group, false);

        var rng = new System.Random(1905);
        var placed = new List<Transform>();
        int woodCount = 0, elsewhere = 0;

        // Swaps: the wood north of the barn and the clump west of it get half, the rest of the map a third.
        foreach (Transform old in SceneryTrees(trees, false).OrderBy(t => t.position.x).ThenBy(t => t.position.z))
        {
            Vector3 p = old.position;
            if (Inside(farm, p, 1f)) continue;
            bool inWood = (p.z > barn.z + 20f && p.x > barn.x - 45f && p.x < barn.x + 85f) || Flat(p - barn) < 36f;
            if (rng.NextDouble() > (inWood ? WoodShare : ElsewhereShare)) continue;
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject);
            bool pine = source.Contains("pine") || source.Contains("Pine") || source.Contains("Fir");
            string kind = Pick(rng, pine ? Pines : inWood ? FullBroadleaf : LightBroadleaf);
            if (!prefabs.TryGetValue(kind, out var pf)) continue;
            float oldHeight = Measure(old.gameObject).size.y;
            // About the tree it replaces, pines a little taller (their trunk stays clear), never tiny or huge.
            float scale = Mathf.Clamp(oldHeight * (pine ? 1.15f : 1.05f) / pf.height, 0.6f, 1.15f) * Mathf.Lerp(0.93f, 1.07f, (float)rng.NextDouble());
            Transform tree = Spawn(pf.prefab, inWood ? wood : swapped, new Vector3(p.x, ground.Height(p) - 0.05f, p.z), scale, rng, kind);
            Undo.RecordObject(old.gameObject, "Place new tree designs");
            old.gameObject.SetActive(false);
            placed.Add(tree);
            if (inWood) woodCount++; else elsewhere++;
        }
        report.AppendLine($"swapped {woodCount} trees in the wood by the barn and {elsewhere} elsewhere");

        // Meadow groups: open grass away from the farm, the paths, the terrain edge and anything standing.
        var standing = SceneryTrees(trees, false).Select(t => t.position).Concat(placed.Select(t => t.position)).ToList();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (MapTreeSurvey.LooksLikeTree(t.name) && !t.IsChildOf(group)) standing.Add(t.position);
        var tall = TallRenderers(group);
        int groups = 0, attempts = 0, meadowCount = 0;
        while (groups < MeadowGroups && attempts++ < 4000)
        {
            Vector2 c = ground.RandomPoint(rng, EdgeMargin);
            if (!Open(c, ground, farm, standing, tall, Spacing * 1.6f)) continue;
            string kind = GroupTrees[groups % GroupTrees.Length];
            if (!prefabs.TryGetValue(kind, out var pf)) { groups++; continue; }
            int size = kind.Contains("Pine") ? 3 : 2 + rng.Next(3);
            var members = new List<Vector2> { c };
            for (int i = 0; i < 30 && members.Count < size; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2f), r = Mathf.Lerp(Spacing, Spacing * 1.5f, (float)rng.NextDouble());
                Vector2 q = members[rng.Next(members.Count)] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (members.Any(m => (m - q).magnitude < Spacing) || !Open(q, ground, farm, standing, tall, Spacing)) continue;
                members.Add(q);
            }
            var cluster = new GameObject($"{kind} Group {groups + 1}").transform;
            cluster.SetParent(meadow, false);
            foreach (Vector2 m in members)
            {
                float scale = Mathf.Lerp(0.8f, 1.05f, (float)rng.NextDouble()) * (kind.Contains("Pine") ? 0.85f : 1f);
                Transform tree = Spawn(pf.prefab, cluster, ground.Point(m) - Vector3.up * 0.05f, scale, rng, kind);
                standing.Add(tree.position);
                placed.Add(tree);
                meadowCount++;
            }
            report.AppendLine($"  {cluster.name}: {members.Count} trees around {ground.Point(c)}");
            groups++;
        }
        report.AppendLine($"planted {meadowCount} trees in {groups} meadow groups");
        foreach (var k in placed.GroupBy(t => t.name.Split(' ')[0]).OrderByDescending(g => g.Count()))
            report.AppendLine($"  {k.Count(),3} x {k.Key}");

        Selection.activeTransform = group;
        Pictures(terrain, barn, placed);
    }

    private static Transform Spawn(GameObject prefab, Transform parent, Vector3 at, float scale, System.Random rng, string kind)
    {
        var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(tree, "Place new tree designs");
        tree.name = kind;
        tree.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f));
        tree.transform.localScale = Vector3.one * scale;
        return tree.transform;
    }

    private static string Pick(System.Random rng, (string tree, int weight)[] kinds)
    {
        int roll = rng.Next(kinds.Sum(k => k.weight));
        foreach (var k in kinds) { if (roll < k.weight) return k.tree; roll -= k.weight; }
        return kinds[0].tree;
    }

    private static bool Open(Vector2 p, Ground ground, Bounds farm, List<Vector3> standing, List<Bounds> tall, float spacing)
    {
        if (!ground.Inside(p, EdgeMargin) || Inside(farm, new Vector3(p.x, 0f, p.y), FarmMargin)) return false;
        if (ground.Steepness(p) > 22f || ground.PathWeight(p) > 0.15f || ground.PathDistance(p) < 5f) return false;
        if (standing.Any(s => (new Vector2(s.x, s.z) - p).sqrMagnitude < spacing * spacing)) return false;
        if (tall.Any(b => p.x > b.min.x - 2.5f && p.x < b.max.x + 2.5f && p.y > b.min.z - 2.5f && p.y < b.max.z + 2.5f)) return false;
        Vector3 foot = ground.Point(p);
        foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * 1.6f, foot + Vector3.up * 8f, 1.3f, ~0, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider) && !(c is CharacterController)) return false;
        return true;
    }

    private static List<Bounds> TallRenderers(Transform ignore)
    {
        var list = new List<Bounds>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.transform.IsChildOf(ignore) || r is ParticleSystemRenderer) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.2f || b.size.x > 40f || b.size.z > 40f) continue;
            list.Add(b);
        }
        return list;
    }

    private static Bounds FarmBounds(Transform root)
    {
        Bounds? b = null;
        foreach (string name in new[] { "04 Fences and Gates", "07 Sheep Pasture", "03 Chicken Yard", "01 Barn Yard", "02 Crop Fields", "Farm Pond" })
        {
            Transform t = root.Find(name);
            if (t == null) continue;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds; else { Bounds x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
        }
        return b ?? new Bounds(root.position, new Vector3(80f, 10f, 80f));
    }

    private static bool Inside(Bounds b, Vector3 p, float margin) =>
        p.x > b.min.x - margin && p.x < b.max.x + margin && p.z > b.min.z - margin && p.z < b.max.z + margin;

    private static Bounds Measure(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private sealed class Ground
    {
        private readonly Terrain terrain;
        private readonly TerrainData data;
        private readonly float[,,] alpha;
        private readonly bool[] path;
        private readonly List<Vector2> pathPoints = new List<Vector2>();

        public Ground(Terrain terrain)
        {
            this.terrain = terrain;
            data = terrain.terrainData;
            alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            path = data.terrainLayers.Select(l => l != null && (l.name.Contains("Trodden") || l.name.Contains("Path") || l.name.Contains("Tilled"))).ToArray();
            int step = Mathf.Max(1, Mathf.RoundToInt(2f / (data.size.x / data.alphamapWidth)));
            for (int y = 0; y < data.alphamapHeight; y += step)
                for (int x = 0; x < data.alphamapWidth; x += step)
                    if (Weight(x, y) > 0.45f)
                        pathPoints.Add(new Vector2(Origin.x + (x + 0.5f) / data.alphamapWidth * data.size.x, Origin.z + (y + 0.5f) / data.alphamapHeight * data.size.z));
        }

        private Vector3 Origin => terrain.transform.position;
        private float Weight(int x, int y) { float w = 0f; for (int l = 0; l < path.Length; l++) if (path[l]) w += alpha[y, x, l]; return w; }

        public bool Inside(Vector2 p, float margin) =>
            p.x > Origin.x + margin && p.x < Origin.x + data.size.x - margin && p.y > Origin.z + margin && p.y < Origin.z + data.size.z - margin;
        public Vector2 RandomPoint(System.Random rng, float margin) => new Vector2(
            Origin.x + margin + (float)rng.NextDouble() * (data.size.x - 2f * margin), Origin.z + margin + (float)rng.NextDouble() * (data.size.z - 2f * margin));
        public float Height(Vector3 p) => terrain.SampleHeight(p) + Origin.y;
        public Vector3 Point(Vector2 p) => new Vector3(p.x, Height(new Vector3(p.x, 0f, p.y)), p.y);
        public float Steepness(Vector2 p) => data.GetSteepness((p.x - Origin.x) / data.size.x, (p.y - Origin.z) / data.size.z);
        public float PathWeight(Vector2 p) => Weight(
            Mathf.Clamp(Mathf.RoundToInt((p.x - Origin.x) / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1),
            Mathf.Clamp(Mathf.RoundToInt((p.y - Origin.z) / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1));
        public float PathDistance(Vector2 p) => pathPoints.Count == 0 ? 999f : Mathf.Sqrt(pathPoints.Min(q => (q - p).sqrMagnitude));
    }

    // From above with the new trees marked, from the barn's yard toward the wood, and from low over a few meadow groups.
    private static void Pictures(Terrain terrain, Vector3 barn, List<Transform> placed)
    {
        Camera camera = MapSurvey.TempCamera();
        try
        {
            Vector3 o = terrain.transform.position, s = terrain.terrainData.size;
            camera.orthographic = true;
            camera.orthographicSize = s.z / 2f;
            camera.aspect = s.x / s.z;
            camera.farClipPlane = 1000f;
            camera.transform.SetPositionAndRotation(o + new Vector3(s.x / 2f, 300f, s.z / 2f), Quaternion.Euler(90f, 0f, 0f));
            int w = 1600, h = Mathf.RoundToInt(1600 * s.z / s.x);
            Texture2D tex = MapSurvey.Render(camera, w, h);
            foreach (Transform t in placed)
            {
                Vector3 v = camera.WorldToViewportPoint(t.position);
                Color c = t.name.Contains("Pine") ? Color.cyan : t.name.StartsWith("Old") ? Color.magenta : Color.red;
                for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
                {
                    int px = Mathf.RoundToInt(v.x * w) + dx, py = Mathf.RoundToInt(v.y * h) + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h) tex.SetPixel(px, py, c);
                }
            }
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Out, "map.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            camera.orthographic = false;
            camera.fieldOfView = 55f;
            camera.clearFlags = CameraClearFlags.Skybox;
            Shot(camera, barn + new Vector3(0f, 6f, -14f), barn + new Vector3(0f, 4f, 40f), "wood_from_yard.png");
            Shot(camera, barn + new Vector3(-30f, 25f, 0f), barn + new Vector3(10f, 0f, 45f), "wood_from_above.png");
            int i = 0;
            foreach (Transform g in placed.Select(t => t.parent).Where(p => p.name.Contains("Group")).Distinct().Take(4))
            {
                Vector3 c = g.GetComponentsInChildren<Transform>().Where(t => t != g).Aggregate(Vector3.zero, (a, t) => a + t.position) / Mathf.Max(1, g.childCount);
                Shot(camera, c + new Vector3(16f, 6f, -16f), c + Vector3.up * 3f, $"meadow_{i++}.png");
            }
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }

    private static void Shot(Camera camera, Vector3 from, Vector3 at, string file)
    {
        camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        Texture2D tex = MapSurvey.Render(camera, 1400, 800);
        File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
