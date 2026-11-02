using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A waterfall on the rocky slope east of the chicken yard, and the slope's rocks made more natural (Map/Waterfall).
/// A spring among boulders up the hill feeds a stream that runs down the slope onto a rock shelf and falls from it,
/// in two sheets, into a small shallow pool at the foot (a FarmPond, so the bird can wade in and drink from it) with
/// a foam ring, a little spray and the sound of the water. The terrain around it gets a pool basin, the shelf with its
/// steep face behind the fall, a pebbly stream bed and no grass in the water; the shelf's face is hidden behind wet,
/// mossy rock walls. The slope's rows of rock walls (Map/Coop Side Rocks) are switched off and laid out again as
/// clusters of differently sized and turned rocks with ferns and flowers at their feet. Everything is cheap: the water
/// is a few hundred triangles with one transparent shader (Farm/Waterfall), the spray a couple of dozen particles.
/// "apply" (again: rebuilt from scratch), "remove" (the rocks back on, the terrain as it was from the backup in
/// ArtSource/Waterfall), "look" (pictures), "survey" (the slope's heights). Temp/MapWaterfall/request.txt while the
/// Editor is open, or the menu; writes Temp/MapWaterfall.
/// </summary>
[InitializeOnLoad]
public static class MapWaterfall
{
    private const string Out = "Temp/MapWaterfall";
    private const string Request = Out + "/request.txt";
    private const string RocksName = "Coop Side Rocks";
    private const string GroupName = "Waterfall";
    private const string Folder = "Assets/Art/Waterfall";
    private const string Backup = "ArtSource/Waterfall/terrain_patch.bin";
    private const string WallFolder = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/";
    private const string WallMaterials = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Materials/";
    private const string GenericFolder = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
    private const string SoundSource = "Assets/_Unused/Audio/BOKI/LowPolyNature/Audio/Clips/waterfall_sfx.wav";
    private static readonly string[] Boulders = { "SM_Gen_Env_Rock_01", "SM_Gen_Env_Rock_02", "SM_Gen_Env_Rock_03", "SM_Gen_Env_Rock_04", "SM_Gen_Env_Rock_05", "SM_Gen_Env_Rock_06", "SM_Gen_Env_Rock_09", "SM_Gen_Env_Rock_10" };
    private static readonly string[] Plants = { "SM_Gen_Env_Fern_02", "SM_Gen_Env_Fern_03", "SM_Gen_Env_Fern_02", "SM_Gen_Env_Flowers_03", "SM_Gen_Env_Flowers_05" };

    // The layout (world metres), across the slope at Z0: the pool at the foot, the shelf the water falls from (its edge
    // at LipX, its top at ShelfY), and the spring up the hill.
    private const float Z0 = 22f;
    private static readonly Vector2 PoolCentre = new Vector2(146.4f, Z0);
    private const float PoolRadiusX = 3.3f, PoolRadiusZ = 2.9f, PoolDepth = .1f, Freeboard = .035f, ShoreMargin = .3f;
    private const float LipX = 151.2f, ShelfY = -7.6f, ShelfHalfWidth = 3.4f;
    // The rock wall in front of the shelf's face: the stream crosses its top and falls from its front edge.
    private const float WallFrontX = LipX - 1.7f;
    // The cave the stream comes out of: its mouth facing down the slope (-x), its tunnel running back into a hill of
    // rock piled over it.
    private static readonly Vector2 CaveMouth = new Vector2(LipX + 3.6f, Z0 + .3f);
    private const float CaveDepth = 3.8f, CaveHalfWidth = 1.45f, CaveHeight = 2.2f;
    private static readonly Vector2 HillCentre = CaveMouth + new Vector2(2.6f, 0f);
    private const float HillRadius = 6f, HillRise = 3.9f;
    private static float caveFloor;   // set while building: the ground at the mouth
    private const float FallSpeed = 1.9f;   // how fast the water leaves the lip, m/s
    // The flow speed the water material runs at; the strip's uv stretches so each part runs at its own speed.
    private const float RefSpeed = 2f;
    // The waterfall's sound: quiet, a backdrop to the farm rather than a roar.
    private const float SoundVolume = .12f;
    // The terrain patch the waterfall changes (and the backup holds).
    private const float PatchMinX = 136f, PatchMaxX = 170f, PatchMinZ = Z0 - 13f, PatchMaxZ = Z0 + 13f;
    // The slope's rock layout outside the waterfall: from the fence up, along it.
    private const float FenceX = 129f, SlopeMinZ = -3f, SlopeMaxZ = 42f, WaterfallZone = 9f;
    // Ground layers (map terrain): earth and pebbles for the stream bed and pool bank.
    private const int TroddenEarth = 4, OldEarth = 5, Pebbles = 6, WornMeadow = 7, StonyPath = 19;
    private static double nextPoll;

    static MapWaterfall() => EditorApplication.update += Poll;

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

    [MenuItem("Tools/Chick/Map/Waterfall/Build Waterfall")]
    private static void ApplyMenu() => Run("apply");

    [MenuItem("Tools/Chick/Map/Waterfall/Remove Waterfall")]
    private static void RemoveMenu() => Run("remove");

    private static void Run(string what)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            if (what == "look") Look(report);
            else if (what == "survey") Survey(report);
            else if (what == "sound") MakeLoop(SoundSource, Folder + "/waterfall_loop.wav", report);
            else if (what == "volume") SetVolume(report);
            else if (what == "apply" || what == "remove")
            {
                GameObject map = GameObject.Find("Map");
                if (GameObject.Find("Farm Reference Map") == null || map == null) { report.AppendLine("The open scene has no 'Farm Reference Map' / 'Map'."); return; }
                Terrain terrain = Terrain.activeTerrains.Where(t => t.transform.IsChildOf(map.transform) || t.name.StartsWith("Terrain"))
                    .OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(what == "apply" ? "Build waterfall" : "Remove waterfall");
                int undo = Undo.GetCurrentGroup();
                RemoveAll(map.transform, terrain, report);
                if (what == "apply") Build(map.transform, terrain, report);
                Undo.CollapseUndoOperations(undo);
                EditorSceneManager.MarkSceneDirty(map.scene);
                AssetDatabase.SaveAssets();
                if (what == "apply") Look(report);
                report.AppendLine("save the scene to keep it");
            }
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Waterfall: " + report);
        }
    }

    // ---------------------------------------------------------------- remove

    private static void RemoveAll(Transform map, Terrain terrain, StringBuilder report)
    {
        Transform old = map.Find(GroupName);
        if (old != null)
        {
            var record = old.GetComponent<MapSectionRecord>();
            int shown = 0;
            if (record != null)
                foreach (GameObject go in record.hidden)
                    if (go != null) { Undo.RecordObject(go, "Remove waterfall"); go.SetActive(true); shown++; }
            Undo.DestroyObjectImmediate(old.gameObject);
            report.AppendLine($"removed the waterfall, {shown} objects switched back on");
        }
        if (File.Exists(Backup)) { RestoreTerrain(terrain.terrainData); report.AppendLine("terrain patch restored from " + Backup); }
    }

    // ---------------------------------------------------------------- build

    private static void Build(Transform map, Terrain terrain, StringBuilder report)
    {
        TerrainData data = terrain.terrainData;
        if (!File.Exists(Backup)) { SaveTerrain(data); report.AppendLine("terrain patch backed up to " + Backup); }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art", "Waterfall");

        var root = new GameObject(GroupName).transform;
        root.SetParent(map, false);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build waterfall");
        var record = root.gameObject.AddComponent<MapSectionRecord>();
        record.sourceTerrain = data;
        var hidden = new List<GameObject>();

        // The pool's shore and water level, from the ground as it is.
        Vector2[] shore = Shore();
        float H0(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        float level = shore.Select(p => H0(p.x, p.y)).OrderBy(h => h).ElementAt(shore.Length / 5) - .05f;
        report.AppendLine($"pool water level {level:0.00}, shelf {ShelfY}, lip at x {LipX}");

        // The cave floor: level with the shelf where the shelf reaches it, else the ground at its mouth.
        caveFloor = Mathf.Max(H0(CaveMouth.x, CaveMouth.y), ShelfY + Mathf.Max(0f, CaveMouth.x - LipX) * .04f) - .03f;
        List<Vector3> stream = StreamPath();
        ShapeTerrain(terrain, shore, level, stream, report);
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

        // What stood where the water and the new rocks go.
        Transform slopeRocks = map.Find(RocksName);
        if (slopeRocks != null)
            foreach (Transform t in slopeRocks)
                if (t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Build waterfall"); t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!t.gameObject.activeInHierarchy || t.IsChildOf(root) || (slopeRocks != null && t.IsChildOf(slopeRocks))) continue;
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && t.GetComponent<Renderer>() == null) continue;
            if (t.GetComponent<Terrain>() != null || t.GetComponentInChildren<Renderer>() == null) continue;
            Bounds b = t.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(t.position, Vector3.zero), (a, r) => { a.Encapsulate(r.bounds); return a; });
            if (b.size.x > 20f || b.size.z > 20f) continue;
            Vector2 p = new Vector2(t.position.x, t.position.z);
            bool inPool = Signed(shore, p.x, p.y) < 1.2f;
            bool onShelf = p.x > LipX - 2.5f && p.x < LipX + 6f && Mathf.Abs(p.y - Z0) < ShelfHalfWidth + 3f;
            bool onStream = DistanceToPath(stream, p) < 1.6f;
            if (!inPool && !onShelf && !onStream && !OnHill(p.x, p.y)) continue;
            Undo.RecordObject(t.gameObject, "Build waterfall"); t.gameObject.SetActive(false); hidden.Add(t.gameObject);
        }
        record.hidden = hidden.ToArray();
        report.AppendLine($"switched off {hidden.Count} objects (the slope's rock rows and what stood in the water's way)");

        var rng = new System.Random(2210);
        Material wet = WetRockMaterial();
        Transform rocks = new GameObject("Rocks").transform; rocks.SetParent(root, false);
        Transform plants = new GameObject("Plants").transform; plants.SetParent(root, false);
        FrameTheFall(rocks, H, wet, rng, report);
        Cave(rocks, plants, H, wet, rng, report);
        RocksAlongTheWater(rocks, plants, shore, stream, H, wet, rng);
        SlopeRocks(rocks, plants, H, new System.Random(3115), report);

        // The water: one strip from the spring down the slope, over the rock's edge and down into the pool (so there is
        // no seam where the stream turns into the fall), a thinner sheet in front of the fall, and foam where it lands.
        foreach (string gone in new[] { "M_WaterfallStream.mat", "SM_Stream.asset", "SM_Fall.asset" }) AssetDatabase.DeleteAsset($"{Folder}/{gone}");
        Material water = WaterMaterial("M_Waterfall", m =>
        {
            m.SetFloat("_Opacity", .7f); m.SetFloat("_Speed", RefSpeed); m.SetVector("_StreakScale", new Vector4(7f, .9f, 0f, 0f)); m.SetFloat("_Streaks", .7f);
            m.SetFloat("_StartFoam", .7f); m.SetFloat("_EndFoam", .8f); m.SetFloat("_FoamLength", .06f); m.SetFloat("_EdgeFoam", .55f); m.SetFloat("_Sparkle", 1.4f);
        });
        Material front = WaterMaterial("M_WaterfallFront", m =>
        {
            m.SetFloat("_Opacity", .3f); m.SetFloat("_Speed", 3.1f); m.SetVector("_StreakScale", new Vector4(9f, .7f, 0f, 0f)); m.SetFloat("_Streaks", .9f);
            m.SetFloat("_StartFoam", 0f); m.SetFloat("_EndFoam", .7f); m.SetFloat("_FadeIn", .25f); m.SetFloat("_Sparkle", .6f);
        });
        Material foam = WaterMaterial("M_WaterfallFoam", m =>
        {
            m.SetFloat("_Opacity", .0f); m.SetFloat("_Speed", .5f); m.SetVector("_StreakScale", new Vector4(10f, 2.5f, 0f, 0f)); m.SetFloat("_Streaks", .55f);
            m.SetFloat("_StartFoam", 1f); m.SetFloat("_EndFoam", 0f); m.SetFloat("_FoamLength", .5f); m.SetFloat("_EdgeFade", 0f); m.SetFloat("_FadeOut", 1f);
            m.SetFloat("_Rings", 1f); m.SetFloat("_Sparkle", .8f);
        });
        Transform waterRoot = new GameObject("Water").transform; waterRoot.SetParent(root, false);

        // The surface the water runs on: the ground, or the top of the rocks placed here (the wall it falls from).
        Physics.SyncTransforms();
        float Surface(float x, float z)
        {
            float best = H(x, z);
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, ShelfY + 6f, z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(rocks) && hit.point.y < ShelfY + 1.2f) best = Mathf.Max(best, hit.point.y);
            return best;
        }

        // Down the slope: hugging the ground, slow, whiter where it runs steeper.
        var strip = new List<(Vector3 at, float width, float speed, float white)>();
        for (int i = 0; i < stream.Count; i++)   // from the back of the cave
        {
            Vector3 p = stream[i];
            float x = i == stream.Count - 1 ? LipX : p.x, z = i == stream.Count - 1 ? Z0 : p.z;
            float y = Surface(x, z) + .06f;
            float slope = strip.Count > 0 ? Mathf.Abs(strip[strip.Count - 1].at.y - y) / Mathf.Max(.05f, Vector2.Distance(new Vector2(strip[strip.Count - 1].at.x, strip[strip.Count - 1].at.z), new Vector2(x, z))) : 0f;
            strip.Add((new Vector3(x, y, z), Mathf.Lerp(.55f, 1.6f, Mathf.Pow(i / (float)(stream.Count - 1), .7f)), 1.3f + slope * 2f, Mathf.Clamp01((slope - .25f) * 1.5f) * .5f));
        }
        // Across the rock's top: level with its highest bumps (so no bump pokes through) to the last point still near the
        // top, which is where it goes over; speeding up and whitening as it nears the edge.
        float Top(float x) => Mathf.Max(Surface(x, Z0), Mathf.Max(Surface(x, Z0 - .5f), Surface(x, Z0 + .5f)));
        float edgeX = LipX;
        for (float x = LipX - .2f; x > WallFrontX - .6f; x -= .1f)
        {
            if (Top(x) < ShelfY - .35f) break;
            edgeX = x;
        }
        float topY = ShelfY;
        for (float x = LipX; x >= edgeX; x -= .1f) topY = Mathf.Max(topY, Top(x));
        for (float x = LipX - .4f; x > edgeX + .05f; x -= .4f)
            strip.Add((new Vector3(x, Mathf.Lerp(Top(x), topY, .7f) + .06f, Z0), 1.65f, 1.8f, Mathf.InverseLerp(LipX, edgeX, x) * .3f));
        Vector3 lip = new Vector3(edgeX - .05f, topY + .06f, Z0);
        // The fall: off the edge, out and down into the pool.
        int top = strip.Count;
        for (float t = 0f; ; t += .05f)
        {
            Vector3 p = lip + new Vector3(-FallSpeed * t, -4.9f * t * t, 0f);
            bool last = p.y <= level + .02f;
            if (last) p.y = level + .02f;
            float speed = Mathf.Sqrt(FallSpeed * FallSpeed + 96f * t * t);
            strip.Add((p, Mathf.Lerp(1.65f, 2.2f, Mathf.Clamp01(t / .9f)), speed, Mathf.Max(.35f * (1f - t / .25f), 0f)));
            if (last) break;
        }
        Vector3 landing = strip[strip.Count - 1].at;
        Ribbon(waterRoot, "Waterfall Water", strip, water);
        waterRoot.gameObject.AddComponent<FlowingWater>().Configure(strip.Select(s => s.at).ToArray(), strip.Select(s => s.width).ToArray(),
                                                                     strip.Select(s => s.speed).ToArray(), landing, Vector3.left);
        var sheet = strip.Skip(top).Select(s => (s.at + new Vector3(-.14f, 0f, .08f), s.width * .7f, s.speed, 0f)).ToList();
        Ribbon(waterRoot, "Fall Front", sheet, front);
        FoamRing(waterRoot, landing + new Vector3(-.35f, .015f, 0f), 2.5f, foam);
        StreamStones(rocks, stream, Surface, rng);
        report.AppendLine($"water: {strip.Count} steps from the spring, over the edge at {lip}, landing at {landing}");

        Spray(waterRoot, landing);
        Splash(waterRoot, landing);
        Sound(waterRoot, landing, report);
        Pool(root, terrain, shore, level, report);
    }

    // ---------------------------------------------------------------- terrain

    private static Vector2[] Shore()
    {
        // A rounded, slightly uneven loop round the pool's centre.
        var control = new List<Vector2>();
        float[] wobble = { 1f, .9f, 1.08f, .95f, 1.04f, .92f, 1.06f, .97f };
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            control.Add(PoolCentre + new Vector2(Mathf.Cos(a) * PoolRadiusX, Mathf.Sin(a) * PoolRadiusZ) * wobble[k]);
        }
        var pts = new List<Vector2>();
        int n = control.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = control[(i - 1 + n) % n], p1 = control[i], p2 = control[(i + 1) % n], p3 = control[(i + 2) % n];
            for (int k = 0; k < 8; k++)
            {
                float t = k / 8f, t2 = t * t, t3 = t2 * t;
                pts.Add(.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
            }
        }
        return pts.ToArray();
    }

    private static float Signed(Vector2[] poly, float x, float z) => FarmReferenceTools.FarmPondV1.Signed(poly, x, z);

    private static List<Vector3> StreamPath()
    {
        // From the back of the cave out of its mouth, then down the slope meandering a little, across the shelf to its edge.
        var pts = new List<Vector3>();
        for (float x = CaveMouth.x + CaveDepth - .5f; x > CaveMouth.x + .1f; x -= .75f) pts.Add(new Vector3(x, 0f, CaveMouth.y));
        Vector2 end = new Vector2(LipX, Z0);
        int steps = 24;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Vector2.Lerp(CaveMouth, end, t);
            p.y += Mathf.Sin(t * Mathf.PI * 2.2f) * .7f * (1f - t);
            pts.Add(new Vector3(p.x, 0f, p.y));
        }
        return pts;
    }

    private static float DistanceToPath(List<Vector3> path, Vector2 p)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Vector2 a = new Vector2(path[i].x, path[i].z), b = new Vector2(path[i + 1].x, path[i + 1].z), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            best = Mathf.Min(best, (a + ab * t - p).magnitude);
        }
        return best;
    }

    private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

    // The ground's new height: the shelf raised up to ShelfY behind the lip with a steep face down to the pool, the pool
    // basin carved below the water with a bank meeting it at the shore, and a shallow bed under the stream.
    private static float Ground(float h0, float x, float z, Vector2[] shore, float level, List<Vector3> stream)
    {
        h0 = Hill(h0, x, z);
        float across = S(ShelfHalfWidth + 2.5f, ShelfHalfWidth, Mathf.Abs(z - Z0));
        float face = S(LipX - 1.6f, LipX - .1f, x);            // up the steep face to the shelf
        float shelf = Mathf.Max(h0, ShelfY + Mathf.Max(0f, x - LipX) * .04f);
        float h = Mathf.Lerp(h0, Mathf.Lerp(h0, shelf, face), across);

        float sd = Signed(shore, x, z);
        if (sd < 0f)
        {
            float inside = -sd;
            h = level - .012f * S(0f, .2f, inside) - PoolDepth * S(0f, 1.4f, inside) + (Mathf.PerlinNoise(x * .9f + 11f, z * .9f + 3f) - .5f) * .02f * S(.3f, 1.2f, inside);
        }
        else
        {
            float bank = level + Freeboard * S(0f, .45f, sd);
            if (h >= bank) { float w = Mathf.Clamp((h - level) * 4.5f, .6f, 1.5f); h = Mathf.Lerp(bank, h, S(0f, w, sd)); }
            else { float w = Mathf.Clamp((bank - h) * 5f, .6f, 1.6f); h = Mathf.Lerp(bank, h, S(.55f, .55f + w, sd)); }
        }

        // A shallow bed for the stream on the slope (not on the shelf or into the pool).
        float d = DistanceToPath(stream, new Vector2(x, z));
        if (x > LipX + .5f) h -= .12f * (1f - S(.3f, 1.1f, d));
        return h;
    }

    private static void ShapeTerrain(Terrain terrain, Vector2[] shore, float level, List<Vector3> stream, StringBuilder report)
    {
        TerrainData data = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(data, "Build waterfall");
        Vector3 tp = terrain.transform.position, size = data.size;
        int res = data.heightmapResolution;
        (int x0, int z0, int w, int h) = Patch(res - 1, tp, size, true);
        float[,] heights = data.GetHeights(x0, z0, w, h);
        float raised = 0f, carved = 0f;
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                float x = tp.x + (x0 + ix) / (float)(res - 1) * size.x, z = tp.z + (z0 + iz) / (float)(res - 1) * size.z;
                float h0 = heights[iz, ix] * size.y + tp.y;
                float y = Ground(h0, x, z, shore, level, stream);
                raised = Mathf.Max(raised, y - h0); carved = Mathf.Max(carved, h0 - y);
                heights[iz, ix] = (y - tp.y) / size.y;
            }
        data.SetHeights(x0, z0, heights);
        report.AppendLine($"terrain: raised up to {raised:0.00} m (the shelf), carved up to {carved:0.00} m (the pool)");

        // Wet earth and pebbles: the pool's bed and bank, the stream bed, the shelf.
        int ares = data.alphamapResolution, layers = data.alphamapLayers;
        (int ax0, int az0, int aw, int ah) = Patch(ares, tp, size, false);
        float[,,] alpha = data.GetAlphamaps(ax0, az0, aw, ah);
        var target = new float[layers];
        for (int iz = 0; iz < ah; iz++)
            for (int ix = 0; ix < aw; ix++)
            {
                float x = tp.x + (ax0 + ix + .5f) / ares * size.x, z = tp.z + (az0 + iz + .5f) / ares * size.z;
                float sd = Signed(shore, x, z), d = DistanceToPath(stream, new Vector2(x, z)), n = Mathf.PerlinNoise(x * 1.7f + 5f, z * 1.7f + 9f);
                System.Array.Clear(target, 0, layers);
                target[OldEarth] = .25f; target[Pebbles] = .3f + .25f * n; target[WornMeadow] = .35f - .15f * n; target[TroddenEarth] = .1f;
                float pool = 1f - S(-.05f, .8f, sd + (n - .5f) * .4f);
                float bed = 1f - S(.5f, 1.4f, d + (n - .5f) * .5f);
                float shelf = S(LipX - 1.5f, LipX, x) * S(ShelfHalfWidth + 1.5f, ShelfHalfWidth - .5f, Mathf.Abs(z - Z0)) * (1f - S(LipX + 5f, LipX + 7f, x));
                float hill = S(HillRadius, HillRadius - 2.5f, Vector2.Distance(new Vector2(x, z), HillCentre)) * (.35f + .4f * n);
                float floor = (1f - S(CaveHalfWidth, CaveHalfWidth + .8f, Mathf.Abs(z - CaveMouth.y))) * S(-1.5f, 0f, x - CaveMouth.x) * S(CaveDepth + 1f, CaveDepth, x - CaveMouth.x);
                float blend = Mathf.Max(Mathf.Max(pool, Mathf.Max(bed * .85f, shelf * .45f)), Mathf.Max(hill, floor));
                if (blend <= 0f) continue;
                float sum = 0f;
                for (int k = 0; k < layers; k++) { alpha[iz, ix, k] = Mathf.Lerp(alpha[iz, ix, k], target[k], blend); sum += alpha[iz, ix, k]; }
                for (int k = 0; k < layers; k++) alpha[iz, ix, k] /= Mathf.Max(sum, 1e-5f);
            }
        data.SetAlphamaps(ax0, az0, alpha);

        // No grass in the water or the stream bed, and thinner on the wet bank.
        int cleared = 0;
        (int dx0, int dz0, int dw, int dh) = DetailPatch(data, tp, size);
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            int[,] m = data.GetDetailLayer(dx0, dz0, dw, dh, layer);
            for (int iz = 0; iz < dh; iz++)
                for (int ix = 0; ix < dw; ix++)
                {
                    if (m[iz, ix] == 0) continue;
                    float x = tp.x + (dx0 + ix + .5f) / data.detailWidth * size.x, z = tp.z + (dz0 + iz + .5f) / data.detailHeight * size.z;
                    float sd = Signed(shore, x, z), d = DistanceToPath(stream, new Vector2(x, z));
                    bool shelf = x > LipX - 1.8f && x < LipX + 1f && Mathf.Abs(z - Z0) < ShelfHalfWidth;
                    bool clear = sd < .35f || d < .8f || shelf || Vector2.Distance(new Vector2(x, z), HillCentre) < HillRadius - 1.5f || ((sd < 1f || d < 1.4f) && Mathf.PerlinNoise(x * 3.1f, z * 3.1f) < .55f);
                    if (clear) { cleared += m[iz, ix]; m[iz, ix] = 0; }
                }
            data.SetDetailLayer(dx0, dz0, layer, m);
        }
        report.AppendLine($"grass cleared from the water and the stream bed: {cleared}");
        EditorUtility.SetDirty(data);
    }

    private static (int x0, int z0, int w, int h) Patch(int res, Vector3 tp, Vector3 size, bool inclusive)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((PatchMinX - tp.x) / size.x * res));
        int x1 = Mathf.Min(inclusive ? res : res - 1, Mathf.CeilToInt((PatchMaxX - tp.x) / size.x * res));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((PatchMinZ - tp.z) / size.z * res));
        int z1 = Mathf.Min(inclusive ? res : res - 1, Mathf.CeilToInt((PatchMaxZ - tp.z) / size.z * res));
        return (x0, z0, x1 - x0 + 1, z1 - z0 + 1);
    }

    private static (int x0, int z0, int w, int h) DetailPatch(TerrainData data, Vector3 tp, Vector3 size)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((PatchMinX - tp.x) / size.x * data.detailWidth));
        int x1 = Mathf.Min(data.detailWidth - 1, Mathf.CeilToInt((PatchMaxX - tp.x) / size.x * data.detailWidth));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((PatchMinZ - tp.z) / size.z * data.detailHeight));
        int z1 = Mathf.Min(data.detailHeight - 1, Mathf.CeilToInt((PatchMaxZ - tp.z) / size.z * data.detailHeight));
        return (x0, z0, x1 - x0 + 1, z1 - z0 + 1);
    }

    // The patch as it was before the waterfall: heights, ground layers and grass, so "remove" (and every rebuild) starts
    // from the slope as it was.
    private static void SaveTerrain(TerrainData data)
    {
        Terrain terrain = Terrain.activeTerrains.First(t => t.terrainData == data);
        Vector3 tp = terrain.transform.position, size = data.size;
        Directory.CreateDirectory(Path.GetDirectoryName(Backup));
        using var w = new BinaryWriter(File.Create(Backup));
        (int x0, int z0, int hw, int hh) = Patch(data.heightmapResolution - 1, tp, size, true);
        float[,] heights = data.GetHeights(x0, z0, hw, hh);
        w.Write(x0); w.Write(z0); w.Write(hw); w.Write(hh);
        foreach (float v in heights) w.Write(v);
        (int ax0, int az0, int aw, int ah) = Patch(data.alphamapResolution, tp, size, false);
        float[,,] alpha = data.GetAlphamaps(ax0, az0, aw, ah);
        w.Write(ax0); w.Write(az0); w.Write(aw); w.Write(ah); w.Write(data.alphamapLayers);
        foreach (float v in alpha) w.Write(v);
        (int dx0, int dz0, int dw, int dh) = DetailPatch(data, tp, size);
        w.Write(dx0); w.Write(dz0); w.Write(dw); w.Write(dh); w.Write(data.detailPrototypes.Length);
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            foreach (int v in data.GetDetailLayer(dx0, dz0, dw, dh, layer)) w.Write(v);
    }

    private static void RestoreTerrain(TerrainData data)
    {
        Undo.RegisterCompleteObjectUndo(data, "Remove waterfall");
        using var r = new BinaryReader(File.OpenRead(Backup));
        int x0 = r.ReadInt32(), z0 = r.ReadInt32(), hw = r.ReadInt32(), hh = r.ReadInt32();
        var heights = new float[hh, hw];
        for (int i = 0; i < hh; i++) for (int j = 0; j < hw; j++) heights[i, j] = r.ReadSingle();
        data.SetHeights(x0, z0, heights);
        int ax0 = r.ReadInt32(), az0 = r.ReadInt32(), aw = r.ReadInt32(), ah = r.ReadInt32(), layers = r.ReadInt32();
        var alpha = new float[ah, aw, layers];
        for (int i = 0; i < ah; i++) for (int j = 0; j < aw; j++) for (int k = 0; k < layers; k++) alpha[i, j, k] = r.ReadSingle();
        data.SetAlphamaps(ax0, az0, alpha);
        int dx0 = r.ReadInt32(), dz0 = r.ReadInt32(), dw = r.ReadInt32(), dh = r.ReadInt32(), count = r.ReadInt32();
        for (int layer = 0; layer < count; layer++)
        {
            var m = new int[dh, dw];
            for (int i = 0; i < dh; i++) for (int j = 0; j < dw; j++) m[i, j] = r.ReadInt32();
            data.SetDetailLayer(dx0, dz0, layer, m);
        }
        EditorUtility.SetDirty(data);
    }

    // The hill over the cave: the ground raised into a rounded mound, open in front of the mouth (a little ravine the
    // stream runs out along) and hollowed under it to a flat floor the tunnel stands on.
    private static float Hill(float h0, float x, float z)
    {
        float dx = x - CaveMouth.x, dz = Mathf.Abs(z - CaveMouth.y);
        float d = Vector2.Distance(new Vector2(x, z), HillCentre);
        float raise = HillRise * S(HillRadius, 1.2f, d) * (.85f + .3f * Mathf.PerlinNoise(x * .35f + 4f, z * .35f + 7f));
        raise *= Mathf.Lerp(S(CaveHalfWidth, CaveHalfWidth + 2f, dz), 1f, S(-1.5f, 0f, dx) * S(CaveHalfWidth, CaveHalfWidth + 1f, dz));
        float h = h0 + raise;
        float inside = (1f - S(CaveHalfWidth + .1f, CaveHalfWidth + 1.1f, dz)) * S(-.6f, -.1f, dx) * S(CaveDepth + .7f, CaveDepth + .1f, dx);
        return Mathf.Lerp(h, caveFloor + Mathf.Max(0f, dx) * .025f, inside);
    }

    private static bool OnHill(float x, float z) => Vector2.Distance(new Vector2(x, z), HillCentre) < HillRadius + .5f ||
                                                   (x < CaveMouth.x && x > CaveMouth.x - 3f && Mathf.Abs(z - CaveMouth.y) < 2.5f);

    // The cave: a tunnel of rough rock going back into the hill from the mouth, its rock darkening into the dark,
    // closed at the back, with a collider so nothing walks through its walls; and the hill's rocks round and over it.
    private static void Cave(Transform rocks, Transform plants, System.Func<float, float, float> H, Material wet, System.Random rng, StringBuilder report)
    {
        float R() => (float)rng.NextDouble();
        const int around = 14, along = 9;
        var verts = new List<Vector3>(); var colours = new List<Color>(); var tris = new List<int>();
        Color rock = new Color(.24f, .22f, .2f), dark = new Color(.015f, .015f, .02f);
        for (int i = 0; i <= along; i++)
        {
            float dx = Mathf.Lerp(-.2f, CaveDepth, i / (float)along);
            float narrowing = Mathf.Lerp(1f, .75f, i / (float)along);
            for (int k = 0; k <= around; k++)
            {
                float a = Mathf.Lerp(-.12f, Mathf.PI + .12f, k / (float)around);
                float wobble = 1f + (Mathf.PerlinNoise(dx * 1.7f + k * .9f, k * .37f) - .5f) * .35f;
                float z = Mathf.Cos(a) * CaveHalfWidth * narrowing * wobble;
                float y = Mathf.Sin(a) * CaveHeight * narrowing * wobble;
                verts.Add(new Vector3(CaveMouth.x + dx, caveFloor - .25f + y + .25f * Mathf.Clamp01(Mathf.Sin(a) * 4f), CaveMouth.y + z));
                colours.Add(Color.Lerp(rock, dark, S(-.4f, CaveDepth * .55f, dx)) * Mathf.Lerp(1f, .6f, Mathf.Sin(a)));
            }
        }
        for (int i = 0; i < along; i++)
            for (int k = 0; k < around; k++)
            {
                int a = i * (around + 1) + k, b = a + 1, c = a + around + 1, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
        int back = verts.Count;
        verts.Add(new Vector3(CaveMouth.x + CaveDepth + .3f, caveFloor + CaveHeight * .3f, CaveMouth.y));
        colours.Add(dark);
        for (int k = 0; k < around; k++) tris.AddRange(new[] { along * (around + 1) + k, back, along * (around + 1) + k + 1 });
        var mesh = new Mesh { name = "SM_WaterfallCave" };
        mesh.SetVertices(verts); mesh.SetColors(colours); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = Folder + "/SM_WaterfallCave.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        string matPath = Folder + "/M_WaterfallCave.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Farm/Cave Interior")); AssetDatabase.CreateAsset(mat, matPath); }
        var cave = new GameObject("Cave");
        cave.transform.SetParent(rocks, false);
        cave.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = cave.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.TwoSided;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        cave.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Round the mouth: a big wet boulder lying over it, walls flanking it, more boulders at its feet.
        float top = caveFloor + CaveHeight;
        var lintel = Place(GenericFolder + "SM_Gen_Env_Rock_05.prefab", rocks, new Vector3(CaveMouth.x + .5f, top, CaveMouth.y), Quaternion.Euler(0f, 90f, 0f), 1.35f);
        if (lintel != null)
        {
            Bounds b = BoundsOf(lintel);
            lintel.transform.position += new Vector3(CaveMouth.x + .1f - b.min.x, top - .15f - b.min.y, 0f);
            Wet(lintel, wet);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            float z = CaveMouth.y + side * (CaveHalfWidth + 2.4f);
            var wall = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", rocks, new Vector3(CaveMouth.x, caveFloor, z), Quaternion.Euler(0f, 270f + side * 15f, 0f), .5f);
            if (wall == null) continue;
            Fit(wall, CaveMouth.x - .7f, top + 1.6f + R(), z);
            Wet(wall, wet);
            var foot = Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", rocks, new Vector3(CaveMouth.x - .6f, 0f, CaveMouth.y + side * (CaveHalfWidth + 1.4f)),
                             Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.45f, .7f, R()));
            if (foot != null) { var p = foot.transform.position; p.y = H(p.x, p.z) - .15f; foot.transform.position = p; Wet(foot, wet); }
        }
        // The hill's crown: walls stood every way round it (not over the tunnel), boulders over the tunnel.
        int crown = 0;
        for (int k = 0; k < 14; k++)
        {
            float a = k * Mathf.PI * 2f / 14f + R() * .4f, dist = Mathf.Lerp(1.8f, 4.6f, R());
            Vector2 q = HillCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
            bool overTunnel = q.x > CaveMouth.x - .5f && q.x < CaveMouth.x + CaveDepth + 1f && Mathf.Abs(q.y - CaveMouth.y) < CaveHalfWidth + 1.4f;
            if (q.x < CaveMouth.x + 1f && Mathf.Abs(q.y - CaveMouth.y) < CaveHalfWidth + 2f) continue;   // the mouth stays open
            if (overTunnel)
            {
                Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", rocks, new Vector3(q.x, H(q.x, q.y) - .2f, q.y), Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.6f, 1f, R()));
                continue;
            }
            float size = Mathf.Lerp(.45f, .75f, R());
            var wall = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", rocks, Vector3.zero,
                             Quaternion.Euler((R() - .5f) * 10f, a * Mathf.Rad2Deg + 90f + (R() - .5f) * 50f, (R() - .5f) * 8f), size);
            if (wall == null) continue;
            wall.transform.position = new Vector3(q.x, H(q.x, q.y) - 9.6f * size * Mathf.Lerp(.25f, .4f, R()), q.y);
            crown++;
        }
        for (int k = 0; k < 6; k++)
        {
            float a = R() * Mathf.PI * 2f;
            Vector2 q = HillCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Lerp(3f, HillRadius, R());
            if (q.x < CaveMouth.x && Mathf.Abs(q.y - CaveMouth.y) < 2f) continue;
            Place(GenericFolder + Plants[rng.Next(Plants.Length)] + ".prefab", plants, new Vector3(q.x, H(q.x, q.y) - .03f, q.y), Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.8f, 1.2f, R()));
        }
        report.AppendLine($"cave: mouth at {CaveMouth}, floor {caveFloor:0.00}, {CaveDepth} m deep; hill of {crown} walls round it");
    }

    // ---------------------------------------------------------------- rocks and plants

    private static Material WetRockMaterial()
    {
        string path = Folder + "/M_RocksCover_Wet.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>(WallMaterials + "M_RocksCover_A.mat"));
            AssetDatabase.CreateAsset(m, path);
        }
        // Darker, damp stone and thicker moss: the rocks the water runs over.
        m.SetColor("_BaseColor", new Color(.45f, .45f, .42f));
        m.SetColor("_ColorTint", new Color(.58f, .57f, .54f));
        m.SetFloat("_CoverStr", 7f);
        m.SetColor("_CoverTint", new Color(.22f, .38f, .17f));
        EditorUtility.SetDirty(m);
        return m;
    }

    private static GameObject Place(string path, Transform parent, Vector3 at, Quaternion rot, float size)
    {
        // Nothing small on the shelf's edge, where the water goes over.
        if (!path.Contains("RockWall") && !path.Contains("Rock_") && at.x > WallFrontX - 1.2f && at.x < LipX + 1.5f && Mathf.Abs(at.z - Z0) < ShelfHalfWidth + 1f) return null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(at, rot);
        go.transform.localScale = Vector3.one * size;
        return go;
    }

    private static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Moves a placed rock so its bounds' front (west) side, top and centre across sit where asked.
    private static void Fit(GameObject go, float front, float top, float z)
    {
        Bounds b = BoundsOf(go);
        go.transform.position += new Vector3(front - b.min.x, top - b.max.y, z - b.center.z);
    }

    private static void Wet(GameObject go, Material wet)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.StartsWith("M_RocksCover")) mats[i] = wet;
            r.sharedMaterials = mats;
        }
    }

    // The face under the shelf and the walls either side of the fall: wet, mossy rock walls, the one in front of the
    // shelf's face level with it (the water falls from its front edge), the ones beside it standing higher and further
    // forward so the fall sits in a notch.
    private static void FrameTheFall(Transform parent, System.Func<float, float, float> H, Material wet, System.Random rng, StringBuilder report)
    {
        Quaternion facing = Quaternion.Euler(0f, 270f, 0f);
        GameObject back = Place(WallFolder + "SM_RockWall_01.prefab", parent, new Vector3(LipX, ShelfY, Z0), facing, .44f);
        Fit(back, WallFrontX, ShelfY, Z0);
        Wet(back, wet);
        var sides = new[] { (z: Z0 - 4.4f, size: .55f, top: ShelfY + 1.9f, front: WallFrontX - .6f, turn: 18f), (z: Z0 + 4.6f, size: .6f, top: ShelfY + 2.4f, front: WallFrontX - .8f, turn: -14f) };
        foreach (var s in sides)
        {
            GameObject wall = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", parent, new Vector3(LipX, ShelfY, s.z), Quaternion.Euler(0f, 270f + s.turn, 0f), s.size);
            Fit(wall, s.front, s.top, s.z);
            Wet(wall, wet);
        }
        // Close either side of the water, covering the rest of the face down to the pool.
        foreach (float side in new[] { -1f, 1f })
        {
            float z = Z0 + side * 2.5f;
            GameObject wall = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", parent, new Vector3(LipX, ShelfY, z), Quaternion.Euler(0f, 270f + side * 12f, 0f), .42f);
            Fit(wall, WallFrontX - .25f, ShelfY + .45f, z);
            Wet(wall, wet);
        }
        // Boulders at the foot of the face either side of where the water lands.
        foreach (var (dz, size) in new[] { (-1.9f, .75f), (-3.1f, .6f), (2f, .8f), (3.2f, .55f) })
        {
            float x = WallFrontX - .6f, z = Z0 + dz;
            var boulder = Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", parent, new Vector3(x, H(x, z) - .15f, z),
                                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), size);
            if (boulder != null) Wet(boulder, wet);
        }
        // Behind them, up the shelf's sides, a second, higher tier, so the notch reads as a gorge in the hillside.
        foreach (float side in new[] { -1f, 1f })
        {
            float z = Z0 + side * 6.8f, x = LipX + 2.5f;
            GameObject wall = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", parent, new Vector3(x, H(x, z), z), Quaternion.Euler(0f, 270f - side * 25f, 0f), .62f);
            Fit(wall, x - 2.5f, H(x, z) + 3.6f, z);
        }
        report.AppendLine("rock frame round the fall: 1 wall behind the water, 2 beside it, 2 above");
    }

    // Boulders round the pool (not on the side facing the farm, nor where the water lands) and along the stream, a
    // spring among boulders at the top, ferns and flowers between.
    private static void RocksAlongTheWater(Transform rocks, Transform plants, Vector2[] shore, List<Vector3> stream, System.Func<float, float, float> H,
                                          Material wet, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        Quaternion Turn() => Quaternion.Euler((R() - .5f) * 14f, R() * 360f, (R() - .5f) * 14f);
        for (int i = 0; i < shore.Length; i += 5)
        {
            Vector2 p = shore[i];
            Vector2 out_ = (p - PoolCentre).normalized;
            if (out_.x < -.55f || (out_.x > .6f && Mathf.Abs(p.y - Z0) < 1.6f)) continue;   // open toward the farm; the fall
            if (R() < .25f) continue;
            Vector2 q = p + out_ * Mathf.Lerp(.15f, .6f, R());
            var go = Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", rocks, new Vector3(q.x, H(q.x, q.y) - .15f, q.y), Turn(), Mathf.Lerp(.35f, .8f, R()));
            if (go != null && R() < .6f) Wet(go, wet);
        }
        for (int i = 2; i < stream.Count - 3; i += 2)
        {
            Vector3 a = stream[i], b = stream[i + 1];
            if (a.x > CaveMouth.x - 3.5f) continue;   // the cave's mouth stays open
            Vector2 dir = new Vector2(b.x - a.x, b.z - a.z).normalized, side = new Vector2(-dir.y, dir.x) * (R() < .5f ? -1f : 1f);
            if (R() < .3f) continue;
            Vector2 q = new Vector2(a.x, a.z) + side * Mathf.Lerp(.95f, 1.5f, R());
            Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", rocks, new Vector3(q.x, H(q.x, q.y) - .12f, q.y), Turn(), Mathf.Lerp(.3f, .65f, R()));
            if (R() < .55f)
            {
                Vector2 f = new Vector2(a.x, a.z) - side * Mathf.Lerp(1.1f, 1.8f, R());
                Place(GenericFolder + Plants[rng.Next(Plants.Length)] + ".prefab", plants, new Vector3(f.x, H(f.x, f.y) - .03f, f.y), Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.7f, 1.1f, R()));
            }
        }
        // Ferns and flowers round the pool's back and sides.
        for (int i = 0; i < shore.Length; i += 4)
        {
            Vector2 p = shore[i], out_ = (p - PoolCentre).normalized;
            if (out_.x < -.3f || R() < .35f) continue;
            Vector2 q = p + out_ * Mathf.Lerp(.9f, 1.8f, R());
            Place(GenericFolder + Plants[rng.Next(Plants.Length)] + ".prefab", plants, new Vector3(q.x, H(q.x, q.y) - .03f, q.y), Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.7f, 1.15f, R()));
        }
    }

    // The rest of the slope: instead of straight rows of walls, clusters of one big and one or two smaller walls,
    // turned and tilted every way and set at different heights, boulders at their feet and ferns between.
    private static void SlopeRocks(Transform rocks, Transform plants, System.Func<float, float, float> H, System.Random rng, StringBuilder report)
    {
        float R() => (float)rng.NextDouble();
        Material bare = AssetDatabase.LoadAssetAtPath<Material>(WallMaterials + "M_RocksCover_B.mat");
        int clusters = 0, walls = 0;
        for (float z = SlopeMinZ; z <= SlopeMaxZ; z += Mathf.Lerp(4.5f, 6.5f, R()))
        {
            if (Mathf.Abs(z - Z0) < WaterfallZone) continue;
            int perRow = R() < .4f ? 3 : 4;
            for (int k = 0; k < perRow; k++)
            {
                float rise = Mathf.Lerp(1.5f, 11f, (k + R()) / perRow), cz = z + (R() - .5f) * 4f;
                float x = FenceX + 4f;
                while (x < FenceX + 60f && H(x, cz) - H(FenceX, cz) < rise) x += .5f;
                int pieces = R() < .45f ? 1 : R() < .7f ? 2 : 3;
                for (int p = 0; p < pieces; p++)
                {
                    float size = p == 0 ? Mathf.Lerp(.5f, .85f, R()) : Mathf.Lerp(.3f, .55f, R());
                    Vector2 at = new Vector2(x + (p == 0 ? 0f : (R() - .3f) * 4f), cz + (p == 0 ? 0f : (R() - .5f) * 6f));
                    var rot = Quaternion.Euler((R() - .5f) * 12f, 270f + (R() - .5f) * 80f, (R() - .5f) * 10f);
                    var go = Place(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab", rocks, new Vector3(at.x, H(at.x, at.y) - 9.6f * size * Mathf.Lerp(.2f, .4f, R()), at.y), rot, size);
                    if (go == null) continue;
                    walls++;
                    if (R() < .35f && bare != null)
                        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name.StartsWith("M_RocksCover") ? bare : m).ToArray();
                }
                for (int b = 0; b < 2 + rng.Next(3); b++)
                {
                    Vector2 q = new Vector2(x - Mathf.Lerp(2f, 4.5f, R()), cz + (R() - .5f) * 7f);
                    Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", rocks, new Vector3(q.x, H(q.x, q.y) - .12f, q.y),
                          Quaternion.Euler((R() - .5f) * 14f, R() * 360f, (R() - .5f) * 14f), Mathf.Lerp(.3f, .9f, R()));
                }
                for (int f = 0; f < 1 + rng.Next(3); f++)
                {
                    Vector2 q = new Vector2(x - Mathf.Lerp(1.5f, 3.5f, R()), cz + (R() - .5f) * 6f);
                    Place(GenericFolder + Plants[rng.Next(Plants.Length)] + ".prefab", plants, new Vector3(q.x, H(q.x, q.y) - .03f, q.y), Quaternion.Euler(0f, R() * 360f, 0f), Mathf.Lerp(.7f, 1.1f, R()));
                }
                clusters++;
            }
        }
        report.AppendLine($"slope rocks: {clusters} clusters, {walls} walls");
    }

    // ---------------------------------------------------------------- water

    private static Material WaterMaterial(string name, System.Action<Material> setup)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Farm/Waterfall")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Farm/Waterfall");
        setup(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // A strip of water along a path: across 4 quads, a little bulged out of its face; uv.x across, uv.y "flow metres"
    // (metres scaled by RefSpeed / the water's speed there, so the streaks run at that speed with one material), uv2.x
    // 0..1 along, uv2.y extra white water.
    private static void Ribbon(Transform parent, string name, List<(Vector3 at, float width, float speed, float white)> path, Material material)
    {
        const int across = 4;
        var verts = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tris = new List<int>();
        float total = 0f;
        for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1].at, path[i].at);
        float length = 0f, flow = 0f;
        Vector3 side = Vector3.forward;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                float d = Vector3.Distance(path[i - 1].at, path[i].at);
                length += d;
                flow += d * RefSpeed / Mathf.Max(.2f, (path[i - 1].speed + path[i].speed) * .5f);
            }
            Vector3 tangent = (path[Mathf.Min(i + 1, path.Count - 1)].at - path[Mathf.Max(i - 1, 0)].at).normalized;
            Vector3 flat = Vector3.Cross(Vector3.up, tangent);
            if (flat.sqrMagnitude > 1e-3f) side = flat.normalized;
            Vector3 face = Vector3.Cross(tangent, side).normalized;   // up on the slope, out toward the farm on the fall
            float w = path[i].width;
            for (int k = 0; k <= across; k++)
            {
                float s = k / (float)across;
                verts.Add(path[i].at + side * (s - .5f) * w + face * Mathf.Sin(s * Mathf.PI) * w * .06f);
                uv.Add(new Vector2(s, flow));
                uv2.Add(new Vector2(length / Mathf.Max(total, 1e-3f), path[i].white));
            }
        }
        for (int i = 0; i + 1 < path.Count; i++)
            for (int k = 0; k < across; k++)
            {
                int a = i * (across + 1) + k, b = a + 1, c = a + across + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        MeshObject(parent, name, verts, uv, uv2, tris, material);
    }

    // Small stones in the stream, mostly under the water, breaking its surface here and there.
    private static void StreamStones(Transform parent, List<Vector3> stream, System.Func<float, float, float> surface, System.Random rng)
    {
        for (int i = 3; i < stream.Count - 4; i += 3)
        {
            Vector3 a = stream[i], b = stream[i + 1];
            Vector2 dir = new Vector2(b.x - a.x, b.z - a.z).normalized, across = new Vector2(-dir.y, dir.x);
            Vector2 q = new Vector2(a.x, a.z) + across * ((float)rng.NextDouble() - .5f) * .8f;
            Place(GenericFolder + Boulders[rng.Next(Boulders.Length)] + ".prefab", parent, new Vector3(q.x, surface(q.x, q.y) - .12f, q.y),
                  Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Mathf.Lerp(.12f, .22f, (float)rng.NextDouble()));
        }
    }

    // A flat ring of foam round where the fall lands: uv.x round it, uv.y metres out, uv2.x 0..1 out.
    private static void FoamRing(Transform parent, Vector3 centre, float radius, Material material)
    {
        const int rings = 5, segments = 28;
        var verts = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tris = new List<int>();
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f, d = r / (float)rings * radius;
                verts.Add(centre + new Vector3(Mathf.Cos(a) * d * 1.15f, 0f, Mathf.Sin(a) * d));
                uv.Add(new Vector2(s / (float)segments * 6f, d));
                uv2.Add(new Vector2(r / (float)rings, 0f));
            }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        MeshObject(parent, "Foam", verts, uv, uv2, tris, material);
    }

    private static void MeshObject(Transform parent, string name, List<Vector3> verts, List<Vector2> uv, List<Vector2> uv2, List<int> tris, Material material)
    {
        var mesh = new Mesh { name = "SM_" + name.Replace(" ", "") };
        mesh.SetVertices(verts); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = $"{Folder}/{mesh.name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // A few soft puffs rising where the fall lands.
    private static void Spray(Transform parent, Vector3 at)
    {
        string path = Folder + "/M_WaterfallMist.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Farm/Mist")); AssetDatabase.CreateAsset(mat, path); }
        var go = new GameObject("Spray");
        go.transform.SetParent(parent, false);
        go.transform.position = at + Vector3.up * .15f;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.maxParticles = 28;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.3f, .9f);
        main.startSize = new ParticleSystem.MinMaxCurve(.7f, 1.5f);
        main.startColor = new Color(1f, 1f, 1f, .55f);
        main.gravityModifier = -.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 13f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = .7f;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.8f, .2f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .6f, 1f, 1.4f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // White droplets thrown up and out where the fall hits the pool, falling back in.
    private static void Splash(Transform parent, Vector3 at)
    {
        string path = Folder + "/M_WaterfallSplash.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Farm/Mist")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_Color", new Color(.95f, .98f, 1f, .9f));
        mat.SetFloat("_Softness", .35f);
        EditorUtility.SetDirty(mat);
        var go = new GameObject("Splash");
        go.transform.SetParent(parent, false);
        go.transform.position = at + Vector3.up * .05f;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.maxParticles = 40;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(.06f, .16f);
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 45f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 38f; shape.radius = .6f;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(.9f, 0f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void Sound(Transform parent, Vector3 at, StringBuilder report)
    {
        string path = Folder + "/waterfall_loop.wav";
        if (!File.Exists(path) && File.Exists(SoundSource)) MakeLoop(SoundSource, path, report);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) { report.AppendLine("no waterfall sound found"); return; }
        var go = new GameObject("Sound");
        go.transform.SetParent(parent, false);
        go.transform.position = at + Vector3.up * 1.5f;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip; source.loop = true; source.playOnAwake = true; source.volume = SoundVolume;
        source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 2.5f; source.maxDistance = 40f;
        go.AddComponent<MenuAudioChannel>().SetBus(MenuAudioChannel.Bus.Sfx);
    }

    // ---------------------------------------------------------------- the sound's loop

    // The recording fades out at its end and in at its start, so looped as it is the sound drops away for a moment each
    // time round. The loop is cut from the steady middle and its end cross-faded into its start, so it never dips.
    private const float LoopCrossfade = 1.5f;

    private static float[][] ReadWav(string path, out int rate)
    {
        byte[] b = File.ReadAllBytes(path);
        int channels = 0, bits = 0, dataAt = -1, dataSize = 0;
        rate = 0;
        for (int i = 12; i + 8 <= b.Length;)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, i, 4);
            int size = System.BitConverter.ToInt32(b, i + 4);
            if (id == "fmt ") { channels = System.BitConverter.ToInt16(b, i + 10); rate = System.BitConverter.ToInt32(b, i + 12); bits = System.BitConverter.ToInt16(b, i + 22); }
            else if (id == "data") { dataAt = i + 8; dataSize = Mathf.Min(size, b.Length - dataAt); break; }
            i += 8 + size + (size & 1);
        }
        if (dataAt < 0 || (bits != 16 && bits != 24)) throw new System.Exception($"unsupported wav ({bits} bit)");
        int bytes = bits / 8, frames = dataSize / (bytes * channels);
        var data = new float[channels][];
        for (int c = 0; c < channels; c++) data[c] = new float[frames];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
            {
                int o = dataAt + (f * channels + c) * bytes;
                data[c][f] = bits == 16 ? System.BitConverter.ToInt16(b, o) / 32768f : ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f;
            }
        return data;
    }

    private static void WriteWav(string path, float[][] data, int rate)
    {
        int channels = data.Length, frames = data[0].Length;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + frames * channels * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(frames * channels * 2);
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(data[c][f] * 32767f), -32768, 32767));
    }

    // Loudness every tenth of a second; the loop is the stretch from where the start's fade-in has settled to where the
    // end's fade-out begins (within 1.5 dB of the middle's loudness), its last LoopCrossfade seconds faded into its start.
    private static string MakeLoop(string source, string target, StringBuilder report)
    {
        float[][] data = ReadWav(source, out int rate);
        int frames = data[0].Length, window = rate / 10, windows = frames / window;
        var rms = new float[windows];
        for (int k = 0; k < windows; k++)
        {
            double sum = 0;
            for (int f = k * window; f < (k + 1) * window; f++) foreach (float[] ch in data) sum += ch[f] * ch[f];
            rms[k] = Mathf.Sqrt((float)(sum / (window * data.Length)));
        }
        float middle = rms.Skip(windows / 4).Take(windows / 2).OrderBy(v => v).ElementAt(windows / 4);
        float floor = middle * Mathf.Pow(10f, -1.5f / 20f);
        int first = 0, last = windows - 1;
        while (first < windows / 2 && rms[first] < floor) first++;
        while (last > windows / 2 && rms[last] < floor) last--;
        report.AppendLine($"sound: {frames / (float)rate:0.0} s, {data.Length} channels, {rate} Hz; steady from {first / 10f:0.0} s to {(last + 1) / 10f:0.0} s");
        report.AppendLine("  loudness (dB, every 0.5 s): " + string.Join(" ", Enumerable.Range(0, windows / 5).Select(k => (20f * Mathf.Log10(Mathf.Max(rms[k * 5], 1e-6f))).ToString("0"))));
        int start = first * window, end = (last + 1) * window, fade = Mathf.RoundToInt(LoopCrossfade * rate);
        int length = end - start - fade;
        if (length < rate * 3) throw new System.Exception("too little steady sound for a loop");
        var loop = new float[data.Length][];
        for (int c = 0; c < data.Length; c++)
        {
            loop[c] = new float[length];
            for (int f = 0; f < length; f++) loop[c][f] = data[c][start + fade + f];
            // The loop's first `fade` frames: the sound after the tail cross-fades into it (equal power), so its end runs
            // straight on into its start.
            for (int f = 0; f < fade; f++)
            {
                float t = f / (float)fade;
                loop[c][f] = data[c][start + fade + f] * Mathf.Sin(t * Mathf.PI / 2f) + data[c][end - fade + f] * Mathf.Cos(t * Mathf.PI / 2f);
            }
        }
        WriteWav(target, loop, rate);
        AssetDatabase.ImportAsset(target);
        report.AppendLine($"  loop {length / (float)rate:0.0} s written to {target}");
        return target;
    }

    // The sound's volume on the built waterfall, without building it again.
    private static void SetVolume(StringBuilder report)
    {
        Transform sound = GameObject.Find("Map")?.transform.Find(GroupName + "/Water/Sound");
        if (sound == null || !sound.TryGetComponent(out AudioSource source)) { report.AppendLine("no waterfall sound in the scene"); return; }
        Undo.RecordObject(source, "Waterfall volume");
        source.volume = SoundVolume;
        EditorUtility.SetDirty(source);
        EditorSceneManager.MarkSceneDirty(sound.gameObject.scene);
        report.AppendLine($"waterfall sound volume {SoundVolume}; save the scene to keep it");
    }

    // The pool: a FarmPond, so the bird can wade in and drink, with the farm pond's water.
    private static void Pool(Transform root, Terrain terrain, Vector2[] shore, float level, StringBuilder report)
    {
        Vector2 min = shore[0], max = shore[0];
        foreach (Vector2 p in shore) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        var pool = new GameObject("Pool");
        pool.transform.SetParent(root, false);
        pool.transform.position = new Vector3(PoolCentre.x, level, PoolCentre.y);
        var surface = new GameObject("Water Surface");
        surface.transform.SetParent(pool.transform, false);
        Mesh mesh = FarmReferenceTools.FarmPondV1.WaterMesh(shore, min, max, pool.transform.position);
        mesh.name = "SM_WaterfallPool";
        string path = Folder + "/SM_WaterfallPool.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = surface.AddComponent<MeshRenderer>();
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FarmReferenceTools.FarmPondV1.Folder + "/PondWater.mat");
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        var water = pool.AddComponent<FarmPond>();
        water.Configure(terrain, shore, ShoreMargin, AssetDatabase.LoadAssetAtPath<Material>(FarmReferenceTools.FarmPondV1.Folder + "/PondDroplet.mat"), new GameObject[0]);
        report.AppendLine($"pool: {shore.Length} shore points, water level {level:0.00}");
    }

    // ---------------------------------------------------------------- pictures

    private static void Survey(StringBuilder report)
    {
        Terrain main = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
        float H(float x, float z) => main.SampleHeight(new Vector3(x, 0f, z)) + main.transform.position.y;
        report.Append("x:     ");
        for (float x = 128f; x <= 172f; x += 2f) report.Append($"{x,6:0}");
        report.AppendLine();
        for (float z = -4f; z <= 44f; z += 4f)
        {
            report.Append($"z {z,4:0}:");
            for (float x = 128f; x <= 172f; x += 2f) report.Append($"{H(x, z),6:0.0}");
            report.AppendLine();
        }
        Transform rocks = GameObject.Find("Map")?.transform.Find(RocksName);
        if (rocks != null)
            foreach (Transform t in rocks)
            {
                Renderer r = t.GetComponentInChildren<Renderer>();
                report.AppendLine($"{t.name} at {t.position} rot {t.eulerAngles.y:0} scale {t.localScale.x:0.00} bounds {(r != null ? r.bounds.size.ToString() : "-")}");
            }
    }

    private static void Look(StringBuilder report)
    {
        Terrain mainTerrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
        caveFloor = mainTerrain.SampleHeight(new Vector3(CaveMouth.x, 0f, CaveMouth.y)) + mainTerrain.transform.position.y;
        var b = new Bounds(new Vector3(155f, -6f, 20f), new Vector3(26f, 12f, 48f));
        Terrain main = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
        float H(float x, float z) => main.SampleHeight(new Vector3(x, 0f, z)) + main.transform.position.y;
        var views = new List<(string, Vector3, Vector3)>();
        Vector3 c = b.center;
        Vector3 yard = new Vector3(b.min.x - 30f, 0f, c.z); yard.y = H(yard.x, yard.z) + 3f;
        views.Add(("from_yard", yard, c));
        Vector3 high = new Vector3(b.min.x - 45f, H(b.min.x - 45f, c.z) + 25f, c.z - 30f);
        views.Add(("from_high", high, c));
        Vector3 north = new Vector3(c.x - 10f, 0f, b.max.z + 25f); north.y = H(north.x, north.z) + 4f;
        views.Add(("from_north", north, c));
        // The waterfall close up: from in front of the pool at a chick's height and a little higher, and from the side.
        Vector3 fall = new Vector3(WallFrontX - 1f, ShelfY - 1.5f, Z0);
        Vector3 low = new Vector3(PoolCentre.x - 9f, 0f, Z0 - 2f); low.y = H(low.x, low.z) + .6f;
        views.Add(("fall_low", low, fall));
        Vector3 front = new Vector3(PoolCentre.x - 14f, 0f, Z0 + 3f); front.y = H(front.x, front.z) + 3f;
        views.Add(("fall_front", front, fall));
        Vector3 side = new Vector3(PoolCentre.x - 3f, 0f, Z0 - 11f); side.y = H(side.x, side.z) + 2.5f;
        views.Add(("fall_side", side, fall));
        Vector3 above = new Vector3(LipX + 9f, 0f, Z0 + 6f); above.y = H(above.x, above.z) + 4f;
        views.Add(("stream_above", above, new Vector3(PoolCentre.x, H(PoolCentre.x, Z0), Z0)));
        // The edge where the stream turns into the fall, from the side and close; the landing; the spring.
        Vector3 edge = new Vector3(WallFrontX, ShelfY, Z0);
        views.Add(("edge_close", edge + new Vector3(-2.5f, 1.2f, -4.5f), edge + Vector3.down * .5f));
        Vector3 landingAt = new Vector3(WallFrontX - 1.7f, H(PoolCentre.x, Z0), Z0);
        Vector3 near = new Vector3(PoolCentre.x - 5f, 0f, Z0 - 3.5f); near.y = H(near.x, near.z) + 1.2f;
        views.Add(("landing_close", near, landingAt + Vector3.up * .4f));
        Vector3 cave = new Vector3(CaveMouth.x - 5.5f, 0f, CaveMouth.y - 1.2f); cave.y = H(cave.x, cave.z) + 1.1f;
        views.Add(("cave", cave, new Vector3(CaveMouth.x + .8f, caveFloor + 1f, CaveMouth.y)));
        Vector3 hill = new Vector3(CaveMouth.x - 22f, 0f, CaveMouth.y - 13f); hill.y = H(hill.x, hill.z) + 5f;
        views.Add(("cave_hill", hill, new Vector3(HillCentre.x, caveFloor + 2f, HillCentre.y)));
        var sky = new GameViewCapture.SkyState();
        try
        {
            sky.Apply(new StringBuilder());
            foreach (var (name, from, at) in views)
            {
                Texture2D tex = GameViewCapture.Shot(from, at);
                File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            Camera top = MapSurvey.TempCamera();
            try
            {
                top.orthographic = true;
                top.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) + 4f;
                top.farClipPlane = 400f;
                top.clearFlags = CameraClearFlags.Skybox;
                top.transform.SetPositionAndRotation(c + Vector3.up * 150f, Quaternion.Euler(90f, 0f, 0f));
                Texture2D tex = MapSurvey.Render(top, 800, 800);
                File.WriteAllBytes(Path.Combine(Out, "above.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { Object.DestroyImmediate(top.gameObject); }
        }
        finally { sky.Restore(); }
        report.AppendLine("pictures in " + Out);
    }
}
