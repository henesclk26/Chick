using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Rocks where rocks would be, across the map (Map/Rock Features), read from the ground itself: outcrops on the
/// hillsides (a cliff block or layered ledge leaning with the slope, sunk into it, boulders below it), a landmark rock or
/// two on the hilltops, boulders and ferns along the edges of the woods, small stones beside the dirt paths outside
/// the farm, and a lone boulder here and there in the open meadows. The big ones are the hand-painted, mossy rocks of
/// the rock kit (RockKit, stone_pack.glb); the path stones are the game's own Synty rocks with PolyOne pebbles. Nothing
/// goes on the farm, the paths, the ponds, the flower meadows or the waterfall's slope, too near the map's edge, or into
/// anything standing. Pebbly ground is painted under each group. Run again, it rebuilds (the ground from its backup in
/// Library/ChickBackups); "remove" takes it all away. Temp/MapRockFeatures/request.txt ("apply", "remove", "look") while
/// the Editor is open, or the menu.
/// </summary>
[InitializeOnLoad]
public static class MapRockFeatures
{
    private const string Out = "Temp/MapRockFeatures";
    private const string Request = Out + "/request.txt";
    private const string GroupName = "Rock Features";
    private const string Backup = "Library/ChickBackups/rock_features_alphamaps.bin.gz";
    private const string Synty = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
    private static readonly string[] PathStones = { "SM_Gen_Env_Rock_01", "SM_Gen_Env_Rock_03", "SM_Gen_Env_Rock_05", "SM_Gen_Env_Rock_06", "SM_Gen_Env_Rock_09" };
    private static readonly string[] Pebbles =
    {
        "Assets/PolyOne/Rocks Stylized/Prefabs/SM_Rocks_01.prefab", "Assets/PolyOne/Rocks Stylized/Prefabs/SM_Rocks_02.prefab",
        "Assets/PolyOne/Rocks Stylized/Prefabs/SM_Rocks_06.prefab", "Assets/PolyOne/Rocks Stylized/Prefabs/SM_Rocks_07.prefab",
        "Assets/PolyOne/Rocks Stylized/Prefabs/SM_Rocks_10.prefab",
    };
    private static readonly string[] Ferns = { "SM_Gen_Env_Fern_02", "SM_Gen_Env_Fern_03" };
    // How many of each, and how far apart (m).
    private const int Outcrops = 20, Ridges = 7, Landmarks = 6, WoodEdge = 18, PathSide = 14, Erratics = 10;
    private const float EdgeMargin = 14f;
    // The waterfall's rocky slope has its own rocks (MapWaterfall).
    private static readonly Rect WaterfallSlope = Rect.MinMaxRect(125f, -10f, 174f, 49f);
    private const int Pebbly = 6, Worn = 7, Stony = 19;
    private static double nextPoll;

    static MapRockFeatures() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Run(what);
    }

    [MenuItem("Tools/Chick/Map/Rocks/Place Rock Features")]
    private static void ApplyMenu() => Run("apply");

    [MenuItem("Tools/Chick/Map/Rocks/Remove Rock Features")]
    private static void RemoveMenu() => Run("remove");

    private static void Run(string what)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            GameObject map = GameObject.Find("Map");
            if (GameObject.Find("Farm Reference Map") == null || map == null) { report.AppendLine("The open scene has no 'Farm Reference Map' / 'Map'."); return; }
            Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
            if (what == "look") { Look(terrain, map.transform, report); return; }
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Rock features");
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Transform old = map.transform.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            if (File.Exists(Backup)) { data.SetAlphamaps(0, 0, Load()); report.AppendLine("ground restored from " + Backup); }
            if (what == "apply")
            {
                if (!File.Exists(Backup)) { Save(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)); report.AppendLine("ground backed up to " + Backup); }
                Build(map.transform, terrain, report);
            }
            else report.AppendLine("rock features removed");
            EditorUtility.SetDirty(data);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(map.scene);
            AssetDatabase.SaveAssets();
            if (what == "apply") Look(terrain, map.transform, report);
            report.AppendLine("save the scene to keep it");
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Rock features: " + report);
        }
    }

    // ---------------------------------------------------------------- where

    private sealed class Site
    {
        public Terrain terrain;
        public TerrainData data;
        public float[,,] alpha;
        public bool[] path;
        public List<Vector2> pathPoints = new List<Vector2>();
        public List<Vector2> trees = new List<Vector2>();
        public List<(Vector2 at, float radius)> keepOut = new List<(Vector2, float)>();
        public List<Bounds> farm = new List<Bounds>();
        public List<Bounds> tall = new List<Bounds>();
        public List<(Vector2 at, float spacing)> placed = new List<(Vector2, float)>();
        public Vector3 O => terrain.transform.position;
        public Vector3 Size => data.size;

        public float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + O.y;
        public float Steep(Vector2 p) => data.GetSteepness((p.x - O.x) / Size.x, (p.y - O.z) / Size.z);
        public Vector3 Normal(Vector2 p) => data.GetInterpolatedNormal((p.x - O.x) / Size.x, (p.y - O.z) / Size.z);
        public float PathAt(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - O.x) / Size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((p.y - O.z) / Size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float w = 0f;
            for (int l = 0; l < path.Length; l++) if (path[l]) w += alpha[y, x, l];
            return w;
        }
        public float PathDistance(Vector2 p) => pathPoints.Count == 0 ? 999f : Mathf.Sqrt(pathPoints.Min(q => (q - p).sqrMagnitude));
        public float TreeDistance(Vector2 p) => trees.Count == 0 ? 999f : Mathf.Sqrt(trees.Min(q => (q - p).sqrMagnitude));
        public int TreesWithin(Vector2 p, float r) => trees.Count(q => (q - p).sqrMagnitude < r * r);

        // Free for a rock of this radius: on the map, not on the farm, a path, a pond, the waterfall slope or a flower
        // meadow, not in anything standing, and far enough from the other rocks placed here.
        public bool Free(Vector2 p, float radius, float spacing)
        {
            if (p.x < O.x + EdgeMargin || p.x > O.x + Size.x - EdgeMargin || p.y < O.z + EdgeMargin || p.y > O.z + Size.z - EdgeMargin) return false;
            if (WaterfallSlope.Contains(p)) return false;
            foreach (Bounds b in farm) if (p.x > b.min.x - 4f && p.x < b.max.x + 4f && p.y > b.min.z - 4f && p.y < b.max.z + 4f) return false;
            if (PathAt(p) > .1f || PathDistance(p) < radius + 1.2f) return false;
            foreach (var (at, r) in keepOut) if ((at - p).sqrMagnitude < (r + radius) * (r + radius)) return false;
            foreach (var (at, s) in placed) if ((at - p).sqrMagnitude < Mathf.Max(s, spacing) * Mathf.Max(s, spacing)) return false;
            if (TreeDistance(p) < radius + 1.2f) return false;
            foreach (Bounds b in tall) if (p.x > b.min.x - radius && p.x < b.max.x + radius && p.y > b.min.z - radius && p.y < b.max.z + radius) return false;
            Vector3 foot = new Vector3(p.x, H(p.x, p.y), p.y);
            foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * (radius + .4f), foot + Vector3.up * 4f, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider) && !(c is CharacterController)) return false;
            return true;
        }
    }

    private static Site Survey(Transform map, Terrain terrain)
    {
        var s = new Site { terrain = terrain, data = terrain.terrainData };
        s.alpha = s.data.GetAlphamaps(0, 0, s.data.alphamapWidth, s.data.alphamapHeight);
        s.path = s.data.terrainLayers.Select(l => l != null && (l.name.Contains("Trodden") || l.name.Contains("Path") || l.name.Contains("Tilled"))).ToArray();
        int step = Mathf.Max(1, Mathf.RoundToInt(2f / (s.Size.x / s.data.alphamapWidth)));
        for (int y = 0; y < s.data.alphamapHeight; y += step)
            for (int x = 0; x < s.data.alphamapWidth; x += step)
            {
                float w = 0f;
                for (int l = 0; l < s.path.Length; l++) if (s.path[l]) w += s.alpha[y, x, l];
                if (w > .45f) s.pathPoints.Add(new Vector2(s.O.x + (x + .5f) / s.data.alphamapWidth * s.Size.x, s.O.z + (y + .5f) / s.data.alphamapHeight * s.Size.z));
            }
        Transform farmRoot = GameObject.Find("Farm Reference Map").transform;
        foreach (string name in new[] { "04 Fences and Gates", "07 Sheep Pasture", "03 Chicken Yard", "01 Barn Yard", "02 Crop Fields", "Farm Pond" })
        {
            Transform t = farmRoot.Find(name);
            if (t == null) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            s.farm.Add(b);
        }
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (MapTreeSurvey.LooksLikeTree(t.name) && t.GetComponentInChildren<Renderer>() != null) s.trees.Add(new Vector2(t.position.x, t.position.z));
        foreach (string group in new[] { "Flower Meadows", "Waterfall" })
        {
            Transform g = map.Find(group);
            if (g == null) continue;
            foreach (Transform c in g) s.keepOut.Add((new Vector2(c.position.x, c.position.z), 7f));
        }
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r.GetComponentInParent<Terrain>() != null) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.2f || b.size.x > 30f || b.size.z > 30f) continue;
            if (MapTreeSurvey.LooksLikeTree(r.transform.root.name) || (r.transform.parent != null && MapTreeSurvey.LooksLikeTree(r.transform.parent.name))) continue;
            s.tall.Add(b);
        }
        return s;
    }

    private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

    // ---------------------------------------------------------------- what

    private static void Build(Transform map, Terrain terrain, StringBuilder report)
    {
        var kit = RockKit.Ensure(new StringBuilder());
        Site site = Survey(map, terrain);
        report.AppendLine($"ground read: {site.pathPoints.Count} path points, {site.trees.Count} trees, {site.farm.Count} farm areas, {site.tall.Count} standing objects");
        var root = new GameObject(GroupName).transform;
        root.SetParent(map, false);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Rock features");
        var rng = new System.Random(6107);
        float R() => (float)rng.NextDouble();
        GameObject Pick(string kind) => kit[kind][rng.Next(kit[kind].Count)];
        Vector2 RandomPoint() => new Vector2(site.O.x + EdgeMargin + R() * (site.Size.x - 2f * EdgeMargin), site.O.z + EdgeMargin + R() * (site.Size.z - 2f * EdgeMargin));
        var groups = new List<(Transform group, string kind)>();

        Transform Group(string kind, Vector2 at)
        {
            var g = new GameObject($"{kind} {groups.Count(x => x.kind == kind) + 1}").transform;
            g.SetParent(root, false);
            g.position = new Vector3(at.x, site.H(at.x, at.y), at.y);
            groups.Add((g, kind));
            return g;
        }

        // Hillside outcrops: on slopes, leaning with them, the boulders below (downhill).
        int made = 0;
        for (int tries = 0; tries < 6000 && made < Outcrops; tries++)
        {
            Vector2 p = RandomPoint();
            float steep = site.Steep(p);
            if (steep < 11f || steep > 34f || !site.Free(p, 2.4f, 14f)) continue;
            Vector3 n = site.Normal(p);
            Vector2 downhill = new Vector2(n.x, n.z).normalized;
            var g = Group("Outcrop", p);
            float yaw = Mathf.Atan2(downhill.x, downhill.y) * Mathf.Rad2Deg + (R() - .5f) * 50f;
            var main = Rock(R() < .6f ? Pick("Big") : Pick("Mid"), g, site, p, yaw, Mathf.Lerp(.55f, .9f, R()), Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, .5f)), .28f);
            for (int k = 0; k < 2 + rng.Next(2); k++)
            {
                Vector2 q = p + downhill * Mathf.Lerp(2f, 3.5f, R()) + new Vector2(-downhill.y, downhill.x) * (R() - .5f) * 4f;
                Rock(Pick("Small"), g, site, q, R() * 360f, Mathf.Lerp(.3f, .55f, R()), Quaternion.identity, .2f);
            }
            // Now and then a mossy rock wall behind it, up the slope, its face to the valley.
            Vector2 up = p - downhill * Mathf.Lerp(1.8f, 2.6f, R());
            if (R() < .45f && site.Free(up, 1f, 0f))
            {
                var wall = Rock(Wall(rng), g, site, up, Mathf.Atan2(downhill.x, downhill.y) * Mathf.Rad2Deg + (R() - .5f) * 30f, Mathf.Lerp(.3f, .42f, R()), Quaternion.identity, .35f);
                Mossy(wall);
            }
            Scatter(g, site, p, 3.5f, 3 + rng.Next(3), Pebbles, .8f, 1.4f, rng);
            Scatter(g, site, p, 3f, 1 + rng.Next(2), Ferns.Select(f => Synty + f + ".prefab").ToArray(), .8f, 1.2f, rng);
            site.placed.Add((p, 14f));
            made++;
        }
        report.AppendLine($"hillside outcrops: {made}");

        // Rock ridges on the steeper hillsides: two or three mossy rock walls side by side along the slope, their faces to
        // the valley, painted rocks and boulders at their feet.
        made = 0;
        for (int tries = 0; tries < 8000 && made < Ridges; tries++)
        {
            Vector2 p = RandomPoint();
            float steep = site.Steep(p);
            if (steep < 16f || steep > 38f || !site.Free(p, 3.4f, 22f)) continue;
            Vector3 n = site.Normal(p);
            Vector2 downhill = new Vector2(n.x, n.z).normalized, along = new Vector2(-downhill.y, downhill.x);
            if (!site.Free(p + along * 3.2f, 1.6f, 0f) || !site.Free(p - along * 3.2f, 1.6f, 0f)) continue;
            var g = Group("Rock Ridge", p);
            float yaw = Mathf.Atan2(downhill.x, downhill.y) * Mathf.Rad2Deg;
            // Two walls in a shallow V opening down the slope (not one straight line, which reads as a built wall), leaned with
            // the slope and half sunk, a big painted rock standing in the V's mouth, a ledge to one side.
            Quaternion lean = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, .6f));
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 q = p + along * side * Mathf.Lerp(1.6f, 2.2f, R()) - downhill * Mathf.Lerp(.2f, .8f, R());
                var wall = Rock(Wall(rng), g, site, q, yaw + side * Mathf.Lerp(22f, 40f, R()), Mathf.Lerp(.3f, .44f, R()), lean, .42f);
                Mossy(wall);
            }
            Rock(Pick("Big"), g, site, p + downhill * Mathf.Lerp(.8f, 1.4f, R()), R() * 360f, Mathf.Lerp(.5f, .7f, R()), lean, .3f);
            Rock(Pick("Mid"), g, site, p + along * (R() < .5f ? -1f : 1f) * 3.6f + downhill * 1.2f, R() * 360f, Mathf.Lerp(.4f, .55f, R()), lean, .3f);
            for (int k = 0; k < 2 + rng.Next(2); k++)
                Rock(Pick("Small"), g, site, p + downhill * Mathf.Lerp(2.6f, 4f, R()) + along * (R() - .5f) * 6f, R() * 360f, Mathf.Lerp(.3f, .5f, R()), Quaternion.identity, .2f);
            Scatter(g, site, p + downhill * 2.5f, 4f, 4, Pebbles, .8f, 1.4f, rng);
            Scatter(g, site, p + downhill * 2f, 4f, 2, Ferns.Select(f => Synty + f + ".prefab").ToArray(), .8f, 1.2f, rng);
            site.placed.Add((p, 22f));
            made++;
        }
        report.AppendLine($"rock ridges: {made}");

        // Hilltop landmarks: a big rock on the highest ground round about, a ledge beside it.
        made = 0;
        for (int tries = 0; tries < 8000 && made < Landmarks; tries++)
        {
            Vector2 p = RandomPoint();
            float h = site.H(p.x, p.y);
            bool top = true;
            for (int k = 0; k < 12 && top; k++)
            {
                float a = k * Mathf.PI / 6f;
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f;
                if (site.H(q.x, q.y) > h - .12f) top = false;
            }
            if (!top || site.Steep(p) > 16f || !site.Free(p, 2.8f, 30f)) continue;
            var g = Group("Hilltop", p);
            float yaw = R() * 360f;
            Rock(Pick("Big"), g, site, p, yaw, Mathf.Lerp(.85f, 1.1f, R()), Quaternion.identity, .2f);
            Vector2 side = p + new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)) * 3.6f;
            Rock(Pick("Mid"), g, site, side, yaw + 70f, Mathf.Lerp(.5f, .7f, R()), Quaternion.identity, .25f);
            Rock(Pick("Small"), g, site, p - new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)) * 3.2f, R() * 360f, .5f, Quaternion.identity, .2f);
            Scatter(g, site, p, 4.5f, 4, Pebbles, .9f, 1.5f, rng);
            site.placed.Add((p, 30f));
            made++;
        }
        report.AppendLine($"hilltop landmarks: {made}");

        // The edges of the woods: a boulder or a ledge with ferns, near trees but in the open beside them.
        made = 0;
        for (int tries = 0; tries < 8000 && made < WoodEdge; tries++)
        {
            Vector2 p = RandomPoint();
            float d = site.TreeDistance(p);
            if (d < 2.6f || d > 5.5f || site.TreesWithin(p, 11f) < 3 || site.Steep(p) > 28f || !site.Free(p, 1.4f, 9f)) continue;
            var g = Group("Wood Edge", p);
            Rock(R() < .7f ? Pick("Small") : Pick("Mid"), g, site, p, R() * 360f, Mathf.Lerp(.45f, .75f, R()), Quaternion.identity, .22f);
            if (R() < .6f) Rock(Pick("Small"), g, site, p + Random2(rng) * 1.8f, R() * 360f, Mathf.Lerp(.25f, .4f, R()), Quaternion.identity, .2f);
            Scatter(g, site, p, 2.5f, 2 + rng.Next(2), Ferns.Select(f => Synty + f + ".prefab").ToArray(), .9f, 1.3f, rng);
            Scatter(g, site, p, 2.5f, 2, Pebbles, .8f, 1.2f, rng);
            site.placed.Add((p, 9f));
            made++;
        }
        report.AppendLine($"wood-edge boulders: {made}");

        // Beside the dirt paths outside the farm: two or three of the game's small stones with pebbles.
        made = 0;
        for (int tries = 0; tries < 8000 && made < PathSide; tries++)
        {
            Vector2 p = RandomPoint();
            float d = site.PathDistance(p);
            if (d < 1.4f || d > 3.2f || site.Steep(p) > 22f || !site.Free(p, .6f, 10f)) continue;
            var g = Group("Path Stones", p);
            for (int k = 0; k < 2 + rng.Next(2); k++)
            {
                Vector2 q = p + Random2(rng) * Mathf.Lerp(0f, 1.1f, R());
                var stone = Place(Synty + PathStones[rng.Next(PathStones.Length)] + ".prefab", g, new Vector3(q.x, site.H(q.x, q.y) - .05f, q.y),
                                  Quaternion.Euler((R() - .5f) * 10f, R() * 360f, (R() - .5f) * 10f), Mathf.Lerp(.5f, .9f, R()));
            }
            Scatter(g, site, p, 1.8f, 3, Pebbles, .7f, 1.2f, rng);
            site.placed.Add((p, 12f));
            made++;
        }
        report.AppendLine($"path-side stones: {made}");

        // Open meadow: a lone boulder far from the trees, on fairly level ground.
        made = 0;
        for (int tries = 0; tries < 8000 && made < Erratics; tries++)
        {
            Vector2 p = RandomPoint();
            if (site.TreeDistance(p) < 9f || site.Steep(p) > 12f || !site.Free(p, 1.6f, 20f)) continue;
            var g = Group("Meadow Boulder", p);
            Rock(R() < .5f ? Pick("Mid") : Pick("Small"), g, site, p, R() * 360f, Mathf.Lerp(.55f, .85f, R()), Quaternion.identity, .25f);
            Scatter(g, site, p, 2f, 2 + rng.Next(2), Pebbles, .8f, 1.3f, rng);
            site.placed.Add((p, 20f));
            made++;
        }
        report.AppendLine($"meadow boulders: {made}");

        PaintUnder(site, root);
        int tris = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0);
        report.AppendLine($"rock features: {groups.Count} groups, {root.GetComponentsInChildren<LODGroup>().Length} kit rocks, {tris} triangles in all (each kit rock culled when small on screen)");
        foreach (var (g, kind) in groups) report.AppendLine($"  {g.name} at {g.position}");
    }

    private static GameObject Wall(System.Random rng) =>
        AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_RockWall_0" + (1 + rng.Next(2)) + ".prefab");

    // The rock walls in the waterfall's darker, mossier stone (M_RocksCover_Wet), to sit with the painted rocks.
    private static void Mossy(GameObject go)
    {
        var wet = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Waterfall/M_RocksCover_Wet.mat");
        if (wet == null) return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name.StartsWith("M_RocksCover") ? wet : m).ToArray();
    }

    private static Vector2 Random2(System.Random rng)
    {
        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    // A kit rock standing on the ground at p, turned, leaned (lean: from up toward the slope) and sunk into the ground.
    private static GameObject Rock(GameObject prefab, Transform parent, Site site, Vector2 p, float yaw, float scale, Quaternion lean, float sink)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(new Vector3(p.x, site.H(p.x, p.y), p.y), lean * Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.down * b.size.y * sink;
        return go;
    }

    private static GameObject Place(string path, Transform parent, Vector3 at, Quaternion rot, float size)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(at, rot);
        go.transform.localScale = Vector3.one * size;
        return go;
    }

    private static void Scatter(Transform parent, Site site, Vector2 centre, float radius, int count, string[] prefabs, float min, float max, System.Random rng)
    {
        for (int k = 0; k < count; k++)
        {
            Vector2 q = centre + Random2(rng) * Mathf.Lerp(radius * .4f, radius, (float)rng.NextDouble());
            if (site.PathAt(q) > .2f) continue;
            Place(prefabs[rng.Next(prefabs.Length)], parent, new Vector3(q.x, site.H(q.x, q.y) - .03f, q.y),
                  Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Mathf.Lerp(min, max, (float)rng.NextDouble()));
        }
    }

    // Pebbles and worn grass round the foot of each group's rocks, fading out unevenly.
    private static void PaintUnder(Site site, Transform root)
    {
        TerrainData data = site.data;
        int w = data.alphamapWidth, h = data.alphamapHeight, layers = data.alphamapLayers;
        float[,,] alpha = data.GetAlphamaps(0, 0, w, h);
        var target = new float[layers];
        foreach (Transform t in root.GetComponentsInChildren<Transform>())
        {
            if (!t.name.StartsWith("Rock_") && !t.name.StartsWith("SM_Gen_Env_Rock") && !t.name.StartsWith("SM_RockWall")) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            float radius = Mathf.Min(b.extents.x, b.extents.z) * .85f, reach = radius + 1.4f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((b.center.x - reach - site.O.x) / site.Size.x * w)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt((b.center.x + reach - site.O.x) / site.Size.x * w));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((b.center.z - reach - site.O.z) / site.Size.z * h)), z1 = Mathf.Min(h - 1, Mathf.CeilToInt((b.center.z + reach - site.O.z) / site.Size.z * h));
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float x = site.O.x + (ix + .5f) / w * site.Size.x, z = site.O.z + (iz + .5f) / h * site.Size.z;
                    float n = Mathf.PerlinNoise(x * 1.3f + 17f, z * 1.3f + 3f);
                    float d = new Vector2(x - b.center.x, z - b.center.z).magnitude - radius;
                    float weight = (1f - S(-.2f, 1f + n * .5f, d)) * .7f;
                    if (weight <= 0f) continue;
                    System.Array.Clear(target, 0, layers);
                    target[Pebbly] = .3f + .25f * n; target[Worn] = .45f - .15f * n; target[Stony] = .25f - .1f * n;
                    float sum = 0f;
                    for (int k = 0; k < layers; k++) { alpha[iz, ix, k] = Mathf.Lerp(alpha[iz, ix, k], target[k], weight); sum += alpha[iz, ix, k]; }
                    for (int k = 0; k < layers; k++) alpha[iz, ix, k] /= Mathf.Max(sum, 1e-5f);
                }
        }
        data.SetAlphamaps(0, 0, alpha);
    }

    // ---------------------------------------------------------------- backup (8 bits a weight, compressed)

    private static void Save(float[,,] alpha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Backup));
        using var file = File.Create(Backup);
        using var zip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
        using var bw = new BinaryWriter(zip);
        int h = alpha.GetLength(0), w = alpha.GetLength(1), l = alpha.GetLength(2);
        bw.Write(h); bw.Write(w); bw.Write(l);
        var row = new byte[w * l];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) for (int k = 0; k < l; k++) row[x * l + k] = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[y, x, k]) * 255f);
            bw.Write(row);
        }
    }

    private static float[,,] Load()
    {
        using var file = File.OpenRead(Backup);
        using var zip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var br = new BinaryReader(zip);
        int h = br.ReadInt32(), w = br.ReadInt32(), l = br.ReadInt32();
        var alpha = new float[h, w, l];
        for (int y = 0; y < h; y++)
        {
            byte[] row = br.ReadBytes(w * l);
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = 0; k < l; k++) sum += alpha[y, x, k] = row[x * l + k] / 255f;
                for (int k = 0; k < l; k++) alpha[y, x, k] /= Mathf.Max(sum, 1e-4f);
            }
        }
        return alpha;
    }

    // ---------------------------------------------------------------- pictures

    private static void Look(Terrain terrain, Transform map, StringBuilder report)
    {
        Transform root = map.Find(GroupName);
        if (root == null) { report.AppendLine("no rock features"); return; }
        Vector3 o = terrain.transform.position, s = terrain.terrainData.size;
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + o.y;
        var sky = new GameViewCapture.SkyState();
        try
        {
            sky.Apply(new StringBuilder());
            foreach (var v in GameViewCapture.Views(terrain).Where(v => v.name == "whole_map" || v.name == "over_farm_north"))
                Shot(v.from, v.at, v.name);
            // Two of each kind, close up.
            var blockers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.size.y > 1.2f && r.bounds.size.x < 30f && r.bounds.size.z < 30f).ToList();
            foreach (var kind in root.Cast<Transform>().GroupBy(g => g.name.Substring(0, g.name.LastIndexOf(' '))))
                foreach (Transform g in kind.Take(2))
                {
                    // From the map's middle side (where the player comes from), a little above, framing the group.
                    var rs = g.GetComponentsInChildren<Renderer>();
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    // Turning round it until nothing (a tree, a fence) stands between.
                    Vector3 toCentre = new Vector3(o.x + s.x / 2f - b.center.x, 0f, o.z + s.z / 2f - b.center.z).normalized;
                    float dist = Mathf.Max(5f, b.extents.magnitude * 2.4f);
                    Vector3 from = Vector3.zero;
                    foreach (float turn in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 160f, -160f })
                    {
                        from = b.center + Quaternion.Euler(0f, turn, 0f) * toCentre * dist;
                        from.y = Mathf.Max(H(from.x, from.z) + 1.6f, b.center.y + dist * .25f);
                        var ray = new Ray(from, b.center - from);
                        float reach = Vector3.Distance(from, b.center) - b.extents.magnitude * .5f;
                        bool clear = !blockers.Any(r => !r.transform.IsChildOf(g) && (r.bounds.Contains(from) || r.bounds.IntersectRay(ray, out float d) && d < reach));
                        if (clear) break;
                    }
                    Shot(from, b.center, g.name.Replace(' ', '_').ToLowerInvariant());
                }
        }
        finally { sky.Restore(); }
        report.AppendLine("pictures in " + Out);
    }

    private static void Shot(Vector3 from, Vector3 at, string name)
    {
        Texture2D tex = GameViewCapture.Shot(from, at);
        File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
