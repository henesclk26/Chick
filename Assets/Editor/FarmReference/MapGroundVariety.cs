using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Takes the sameness off the meadows. The map's grass is painted with its own grass layers in large, uneven patches:
/// sun-dried and golden stretches, lush clover-green ones and cooler olive shade, softly blended; only where the ground
/// is already grass (paths, fields, earth and the pond's banks are left alone), and more gently inside the farm.
/// The edges of the earth paths get a strip of worn grass and pebbles so they fade into the meadow instead of ending
/// like puddles. Then the grass takes the new ground's colour (GrassGroundTintV1). Flower meadows go on open grass:
/// a handful of patches, each mostly one kind (daisy, buttercup, poppy, bellflower, clover) thinning out at its edge,
/// each flower culled when it is small on screen; under Map/Flower Meadows. The ground as it was is kept in
/// Library/ChickBackups (not in the project), so "apply" again starts from it and "remove" puts it back.
/// Temp/MapGroundVariety/request.txt ("apply", "remove", "look") while the Editor is open, or the menu.
/// </summary>
[InitializeOnLoad]
public static class MapGroundVariety
{
    private const string Out = "Temp/MapGroundVariety";
    private const string Request = Out + "/request.txt";
    private const string Backup = "Library/ChickBackups/ground_variety_alphamaps.bin.gz";
    private const string FlowersName = "Flower Meadows";
    // Map terrain layers.
    private static readonly int[] Grass = { 0, 1, 2, 3, 9, 10, 11, 12, 16, 17, 18 };
    private static readonly int[] PathLayers = { 4, 19 };   // TroddenEarth, StonyPath
    private const int Pebbles = 6, WornMeadow = 7, Lush = 9, Clover = 10, Dry = 11, Shade = 12, Cool = 16, Golden = 17, Olive = 18;
    private static readonly (string path, float scale)[] FlowerKinds =
    {
        ("Assets/Art/MeadowFlowers/Flower_Daisy.fbx", .72f), ("Assets/Art/MeadowFlowers/Flower_Buttercup.fbx", .73f),
        ("Assets/Art/MeadowFlowers/Flower_Poppy.fbx", .46f), ("Assets/Art/MeadowFlowers/Flower_Bellflower.fbx", .51f),
        ("Assets/Art/MeadowFlowers/Flower_Clover.fbx", .83f),
    };
    private const int Patches = 8;
    private static double nextPoll;

    static MapGroundVariety() => EditorApplication.update += Poll;

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

    [MenuItem("Tools/Chick/Ground/Ground Variety And Flower Meadows")]
    private static void ApplyMenu() => Run("apply");

    [MenuItem("Tools/Chick/Ground/Remove Ground Variety And Flower Meadows")]
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
            if (what == "look") { Look(terrain, report); return; }
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Ground variety");
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Transform old = map.transform.Find(FlowersName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            if (File.Exists(Backup)) { data.SetAlphamaps(0, 0, ReadBackup()); report.AppendLine("ground restored from " + Backup); }
            if (what == "apply")
            {
                if (!File.Exists(Backup)) { WriteBackup(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)); report.AppendLine("ground backed up to " + Backup); }
                Paint(terrain, report);
                Flowers(map.transform, terrain, report);
            }
            else report.AppendLine("ground variety and flower meadows removed");
            EditorUtility.SetDirty(data);
            Undo.CollapseUndoOperations(undo);
            report.AppendLine(FarmReferenceTools.GrassGroundTintV1.Apply());
            EditorSceneManager.MarkSceneDirty(map.scene);
            AssetDatabase.SaveAssets();
            if (what == "apply") Look(terrain, report);
            report.AppendLine("save the scene to keep it");
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Ground variety: " + report);
        }
    }

    // ---------------------------------------------------------------- backup (8 bits a weight, compressed)

    private static void WriteBackup(float[,,] alpha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Backup));
        using var file = File.Create(Backup);
        using var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
        using var w = new BinaryWriter(zip);
        int h = alpha.GetLength(0), wd = alpha.GetLength(1), l = alpha.GetLength(2);
        w.Write(h); w.Write(wd); w.Write(l);
        var row = new byte[wd * l];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < wd; x++) for (int k = 0; k < l; k++) row[x * l + k] = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[y, x, k]) * 255f);
            w.Write(row);
        }
    }

    private static float[,,] ReadBackup()
    {
        using var file = File.OpenRead(Backup);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(zip);
        int h = r.ReadInt32(), wd = r.ReadInt32(), l = r.ReadInt32();
        var alpha = new float[h, wd, l];
        for (int y = 0; y < h; y++)
        {
            byte[] row = r.ReadBytes(wd * l);
            for (int x = 0; x < wd; x++)
            {
                float sum = 0f;
                for (int k = 0; k < l; k++) sum += alpha[y, x, k] = row[x * l + k] / 255f;
                for (int k = 0; k < l; k++) alpha[y, x, k] /= Mathf.Max(sum, 1e-4f);
            }
        }
        return alpha;
    }

    // ---------------------------------------------------------------- the ground

    private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

    private static float Noise(float x, float z, float size, float seed)
    {
        float n = Mathf.PerlinNoise(x / size + seed, z / size + seed * .7f) * .65f + Mathf.PerlinNoise(x / (size * .37f) + seed * 1.3f, z / (size * .37f) + seed) * .35f;
        return n;
    }

    private static Bounds FarmBounds()
    {
        Transform root = GameObject.Find("Farm Reference Map").transform;
        Bounds? b = null;
        foreach (string name in new[] { "04 Fences and Gates", "07 Sheep Pasture", "03 Chicken Yard", "01 Barn Yard", "02 Crop Fields" })
        {
            Transform t = root.Find(name);
            if (t == null) continue;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds; else { Bounds x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
        }
        return b ?? new Bounds(root.position, Vector3.one * 80f);
    }

    private static void Paint(Terrain terrain, StringBuilder report)
    {
        TerrainData data = terrain.terrainData;
        int w = data.alphamapWidth, h = data.alphamapHeight, layers = data.alphamapLayers;
        float[,,] alpha = data.GetAlphamaps(0, 0, w, h);
        Vector3 o = terrain.transform.position, size = data.size;
        Bounds farm = FarmBounds();

        // Paths, and how near each texel is to one (a blur of the path mask).
        var path = new float[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float p = 0f;
                foreach (int k in PathLayers) p += alpha[y, x, k];
                path[y, x] = Mathf.Clamp01(p);
            }
        int radius = Mathf.Max(2, Mathf.RoundToInt(1.6f / (size.x / w)));
        float[,] near = Blur(path, radius);

        int patched = 0, edged = 0;
        var grassShare = new float[layers];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float wx = o.x + (x + .5f) / w * size.x, wz = o.z + (y + .5f) / h * size.z;
                float grass = 0f;
                foreach (int k in Grass) grass += alpha[y, x, k];
                float inFarm = wx > farm.min.x && wx < farm.max.x && wz > farm.min.z && wz < farm.max.z ? .6f : 1f;

                // Patches, on grass only.
                if (grass > .85f)
                {
                    float dry = S(.47f, .6f, Noise(wx, wz, 42f, 3.1f));
                    float lush = S(.5f, .63f, Noise(wx, wz, 36f, 11.7f)) * .85f;
                    float cool = S(.53f, .66f, Noise(wx, wz, 30f, 23.3f)) * .7f;
                    float total = Mathf.Min(1f, dry + lush + cool) * inFarm;
                    if (total > .01f)
                    {
                        float sum = dry + lush + cool;
                        float golden = Noise(wx, wz, 9f, 41f), clover = Noise(wx, wz, 7f, 53f), olive = Noise(wx, wz, 8f, 67f);
                        System.Array.Clear(grassShare, 0, layers);
                        grassShare[Dry] = dry / sum * (1f - golden * .6f); grassShare[Golden] = dry / sum * golden * .6f;
                        grassShare[Lush] = lush / sum * (1f - clover * .7f); grassShare[Clover] = lush / sum * clover * .7f;
                        grassShare[Olive] = cool / sum * olive; grassShare[Shade] = cool / sum * (1f - olive) * .6f; grassShare[Cool] = cool / sum * (1f - olive) * .4f;
                        for (int k = 0; k < layers; k++) alpha[y, x, k] = alpha[y, x, k] * (1f - total) + grassShare[k] * total;
                        patched++;
                    }
                }

                // Path edges: worn grass and pebbles in a strip just outside the path.
                float edge = Mathf.Clamp01((near[y, x] - path[y, x]) * 2.6f) * (1f - S(.25f, .5f, path[y, x]));
                if (edge > .02f && grass > .4f)
                {
                    float pebbles = edge * (.15f + .25f * Noise(wx, wz, 2.5f, 77f));
                    float worn = edge * .45f;
                    float keep = 1f - pebbles - worn;
                    for (int k = 0; k < layers; k++) alpha[y, x, k] *= keep;
                    alpha[y, x, Pebbles] += pebbles;
                    alpha[y, x, WornMeadow] += worn;
                    edged++;
                }
            }
        data.SetAlphamaps(0, 0, alpha);
        report.AppendLine($"ground: {patched * 100f / (w * h):0}% of the map given grass patches, {edged * 100f / (w * h):0.0}% path edge");
    }

    private static float[,] Blur(float[,] src, int r)
    {
        int h = src.GetLength(0), w = src.GetLength(1);
        var tmp = new float[h, w];
        var dst = new float[h, w];
        for (int y = 0; y < h; y++)
        {
            float sum = 0f;
            for (int x = -r; x <= r; x++) sum += src[y, Mathf.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[y, x] = sum / (2 * r + 1);
                sum += src[y, Mathf.Min(x + r + 1, w - 1)] - src[y, Mathf.Max(x - r, 0)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int y = -r; y <= r; y++) sum += tmp[Mathf.Clamp(y, 0, h - 1), x];
            for (int y = 0; y < h; y++)
            {
                dst[y, x] = sum / (2 * r + 1);
                sum += tmp[Mathf.Min(y + r + 1, h - 1), x] - tmp[Mathf.Max(y - r, 0), x];
            }
        }
        return dst;
    }

    // ---------------------------------------------------------------- flower meadows

    private static void Flowers(Transform map, Terrain terrain, StringBuilder report)
    {
        TerrainData data = terrain.terrainData;
        Vector3 o = terrain.transform.position, size = data.size;
        float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        float GrassAt(float x, float z)
        {
            int ax = Mathf.Clamp(Mathf.FloorToInt((x - o.x) / size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int az = Mathf.Clamp(Mathf.FloorToInt((z - o.z) / size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float g = 0f;
            foreach (int k in Grass) g += alpha[az, ax, k];
            return g;
        }
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + o.y;
        Bounds farm = FarmBounds();
        bool Open(Vector2 p, float clear)
        {
            if (p.x < o.x + 18f || p.x > o.x + size.x - 18f || p.y < o.z + 12f || p.y > o.z + size.z - 12f) return false;
            if (p.x > farm.min.x - 2f && p.x < farm.max.x + 2f && p.y > farm.min.z - 2f && p.y < farm.max.z + 2f) return false;
            if (GrassAt(p.x, p.y) < .9f) return false;
            if (terrain.terrainData.GetSteepness((p.x - o.x) / size.x, (p.y - o.z) / size.z) > 18f) return false;
            Vector3 foot = new Vector3(p.x, H(p.x, p.y), p.y);
            foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * .5f, foot + Vector3.up * 3f, clear, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider) && !(c is CharacterController)) return false;
            return true;
        }

        var root = new GameObject(FlowersName).transform;
        root.SetParent(map, false);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Ground variety");
        var rng = new System.Random(4407);
        float R() => (float)rng.NextDouble();
        var centres = new List<Vector2>();
        int total = 0, tries = 0;
        while (centres.Count < Patches && tries++ < 3000)
        {
            var c = new Vector2(o.x + R() * size.x, o.z + R() * size.z);
            if (!Open(c, 2.5f) || centres.Any(q => (q - c).magnitude < 22f)) continue;
            var (path, scale) = FlowerKinds[centres.Count % FlowerKinds.Length];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            centres.Add(c);
            var patch = new GameObject($"{prefab.name} Meadow").transform;
            patch.SetParent(root, false);
            patch.position = new Vector3(c.x, H(c.x, c.y), c.y);
            float radius = Mathf.Lerp(3f, 4.8f, R());
            var spots = new List<Vector2>();
            for (int k = 0; k < 400 && spots.Count < 60; k++)
            {
                float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * radius;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d * .8f);
                if (R() > 1f - S(.6f, 1f, d / radius) * .8f) continue;   // thinner toward the edge
                if (spots.Any(q => (q - p).sqrMagnitude < .38f * .38f) || GrassAt(p.x, p.y) < .8f) continue;
                spots.Add(p);
            }
            // A few strays of other kinds among them.
            foreach (Vector2 p in spots)
            {
                var (kindPath, kindScale) = R() < .15f ? FlowerKinds[rng.Next(FlowerKinds.Length)] : (path, scale);
                var kind = AssetDatabase.LoadAssetAtPath<GameObject>(kindPath);
                var flower = (GameObject)PrefabUtility.InstantiatePrefab(kind, patch);
                flower.transform.SetPositionAndRotation(new Vector3(p.x, H(p.x, p.y) - .01f, p.y), Quaternion.Euler(0f, R() * 360f, 0f));
                flower.transform.localScale = Vector3.one * kindScale * Mathf.Lerp(.8f, 1.15f, R());
                var lod = flower.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.012f, flower.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
                total++;
            }
            report.AppendLine($"  {patch.name} at {patch.position}: {spots.Count} flowers, {radius:0.0} m");
        }
        int tris = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0);
        report.AppendLine($"flower meadows: {centres.Count} patches, {total} flowers, {tris} triangles (each culled when small on screen)");
    }

    // ---------------------------------------------------------------- pictures

    private static void Look(Terrain terrain, StringBuilder report)
    {
        Vector3 o = terrain.transform.position, s = terrain.terrainData.size;
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + o.y;
        var sky = new GameViewCapture.SkyState();
        try
        {
            sky.Apply(new StringBuilder());
            Vector3 centre = o + new Vector3(s.x / 2f, 0f, s.z / 2f);
            Save(centre + new Vector3(0f, 230f, -1f), centre, "from_above");
            foreach (var v in GameViewCapture.Views(terrain).Where(v => v.name == "over_farm_north" || v.name == "whole_map"))
                Save(v.from, v.at, v.name);
            Transform flowers = GameObject.Find("Map")?.transform.Find(FlowersName);
            int i = 0;
            if (flowers != null)
                foreach (Transform patch in flowers)
                {
                    if (i >= 3) break;
                    Vector3 p = patch.position;
                    Vector3 from = p + new Vector3(-6f, 0f, -6f); from.y = H(from.x, from.z) + 1.6f;
                    Save(from, p + Vector3.up * .3f, $"meadow_{i++}");
                }
            Transform barn = GameObject.Find("SM_Bld_Barn_01")?.transform;
            if (barn != null)
            {
                Vector3 from = barn.position + new Vector3(-30f, 0f, -20f); from.y = H(from.x, from.z) + 3f;
                Save(from, barn.position + new Vector3(-10f, 0f, 0f), "path_edges");
            }
        }
        finally { sky.Restore(); }
        report.AppendLine("pictures in " + Out);
    }

    private static void Save(Vector3 from, Vector3 at, string name)
    {
        Texture2D tex = GameViewCapture.Shot(from, at);
        File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
