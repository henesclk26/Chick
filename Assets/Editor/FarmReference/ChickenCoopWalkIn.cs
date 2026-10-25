using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Lets the chick walk into the hand-painted chicken coop and paints the coop in the barn's colours, without changing
/// how its outside is shaped. The coop body's mesh (the glTF node using the KURNIK material) is swapped for the
/// walk-in one made by ArtSource/ChickenCoop/make_walkin.py: its pop door opens into a plank room inside. The mesh is
/// rebuilt here in the node's own space; the axes are matched to the original mesh by its bounds, the UVs by matching
/// corners, and every original corner keeps the original's normal. The coop gets the barn-coloured texture
/// (T_ChickenCoop_Barn), a mesh collider of the new body (so walls block and the doorway, ramp and room floor carry the
/// chick) and the same shape on the CameraBlocker layer (so the camera follows the chick into the room instead of
/// staying outside the walls). A chick-sized capsule is then walked from the foot of the ramp into the room and the
/// result written to Temp/CoopWalkIn with pictures. The hay's collider leaves out the straw on the landing and in the
/// doorway (ArtSource/ChickenCoop/coop_hay_collider.json), so it does not stop the chick at the door. Runs from the menu, or by itself when Temp/CoopWalkIn/request.txt
/// appears ("apply", or "check" to only test and take pictures).
/// </summary>
[InitializeOnLoad]
public static class ChickenCoopWalkIn
{
    private const string Glb = "Assets/Assets/chicken_coop_handpainted.glb";
    private const string Json = "ArtSource/ChickenCoop/coop_body_walkin.json";
    private const string Folder = "Assets/Art/Coop";
    private const string MeshPath = Folder + "/SM_ChickenCoop_WalkIn.asset";
    private const string TexturePath = Folder + "/T_ChickenCoop_Barn.png";
    private const string MaterialPath = Folder + "/M_ChickenCoop_Barn.mat";
    private const string BlockerName = "Coop Camera Blocker";
    private const string Out = "Temp/CoopWalkIn";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    [System.Serializable]
    private class Data
    {
        public float[][] positions, normals, uvs, originalBounds, walk;
        public int[] triangles;
    }

    static ChickenCoopWalkIn() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Run(what != "check");
    }

    [MenuItem("Tools/Chick/Coop/Make Coop Walk-In (Barn Colours)")]
    private static void ApplyFromMenu() => Run(true);

    private static void Run(bool apply)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            if (GameObject.Find("Farm Reference Map") == null) { report.AppendLine("The open scene has no 'Farm Reference Map'."); return; }
            Data data = Load();
            List<MeshFilter> bodies = FindBodies(data, report);
            if (bodies.Count == 0) { report.AppendLine("No coop body (KURNIK mesh of " + Glb + ") in the open scene."); return; }

            if (apply)
            {
                Mesh original = bodies[0].sharedMesh;
                if (AssetDatabase.GetAssetPath(original) == MeshPath)
                    original = AssetDatabase.LoadAllAssetsAtPath(Glb).OfType<Mesh>().First(m => m.vertexCount > 0 && Match(m, data, out _, out _) < 0.01f);
                Mesh mesh = BuildMesh(data, original, report);
                Material material = BuildMaterial(bodies[0].GetComponent<MeshRenderer>(), report);
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Make coop walk-in");
                foreach (MeshFilter body in bodies) Fit(body, mesh, material);
                FitHayColliders(report);
                Physics.SyncTransforms();
                foreach (MeshFilter body in bodies) AddWalkway(body, data, report);
                EditorSceneManager.MarkSceneDirty(bodies[0].gameObject.scene);
                report.AppendLine($"applied to {bodies.Count} coop(s); save the scene to keep it");
            }
            Physics.SyncTransforms();
            foreach (MeshFilter body in bodies) Check(body, data, report);
            Pictures(bodies[0], data);
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Coop walk-in: " + report);
        }
    }

    private const string HayJson = "ArtSource/ChickenCoop/coop_hay_collider.json";
    private const string HayMeshPath = Folder + "/SM_ChickenCoop_HayCollider.asset";

    // The hay's collider without the straw on the landing and in the doorway (the straw itself is still drawn).
    private static void FitHayColliders(StringBuilder report)
    {
        Data hay = Load(HayJson);
        Mesh collision = null;
        int fitted = 0;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null || AssetDatabase.GetAssetPath(mf.sharedMesh) != Glb || Match(mf.sharedMesh, hay, out int[] perm, out Vector3 sign) > 0.01f) continue;
            var collider = mf.GetComponent<MeshCollider>();
            if (collider == null) continue;
            if (collision == null)
            {
                collision = AssetDatabase.LoadAssetAtPath<Mesh>(HayMeshPath);
                bool created = collision == null;
                if (created) collision = new Mesh();
                collision.Clear();
                collision.name = "SM_ChickenCoop_HayCollider";
                collision.vertices = hay.positions.Select(p => Map(p, perm, sign)).ToArray();
                collision.triangles = hay.triangles;
                collision.RecalculateBounds();
                if (created) AssetDatabase.CreateAsset(collision, HayMeshPath);
                else EditorUtility.SetDirty(collision);
                AssetDatabase.SaveAssets();
            }
            Undo.RecordObject(collider, "Make coop walk-in");
            collider.sharedMesh = null;
            collider.sharedMesh = collision;
            fitted++;
        }
        report.AppendLine($"hay: {fitted} collider(s) now leave the landing and the doorway clear");
    }

    private static Data Load() => Load(Json);

    private static Data Load(string json)
    {
        // JsonUtility cannot read nested arrays: parse the flat numbers ourselves.
        string text = File.ReadAllText(json);
        float[][] Pairs(string key, int width)
        {
            int start = text.IndexOf("\"" + key + "\"");
            if (start < 0) return new float[0][];
            int open = text.IndexOf('[', start), depth = 0, end = open;
            for (; end < text.Length; end++)
            {
                if (text[end] == '[') depth++;
                else if (text[end] == ']' && --depth == 0) break;
            }
            float[] numbers = text.Substring(open, end - open + 1).Split(new[] { '[', ']', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var rows = new float[numbers.Length / width][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new float[width];
                System.Array.Copy(numbers, i * width, rows[i], 0, width);
            }
            return rows;
        }
        return new Data
        {
            positions = Pairs("positions", 3), normals = Pairs("normals", 3), uvs = Pairs("uvs", 2),
            originalBounds = Pairs("originalBounds", 3), walk = Pairs("walk", 3),
            triangles = Pairs("triangles", 1).Select(r => (int)r[0]).ToArray(),
        };
    }

    // Blender's node space to Unity's: an axis permutation with signs, chosen by matching the original mesh's bounds.
    private static readonly int[][] Permutations = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };

    private static Vector3 Map(float[] p, int[] perm, Vector3 sign) => new Vector3(p[perm[0]] * sign.x, p[perm[1]] * sign.y, p[perm[2]] * sign.z);

    private static float Match(Mesh mesh, Data data, out int[] perm, out Vector3 sign)
    {
        float best = float.MaxValue;
        perm = Permutations[0];
        sign = Vector3.one;
        foreach (int[] p in Permutations)
        for (int s = 0; s < 8; s++)
        {
            var sg = new Vector3((s & 1) == 0 ? 1 : -1, (s & 2) == 0 ? 1 : -1, (s & 4) == 0 ? 1 : -1);
            Vector3 a = Map(data.originalBounds[0], p, sg), b = Map(data.originalBounds[1], p, sg);
            var bounds = new Bounds((a + b) / 2f, Vector3.zero);
            bounds.Encapsulate(a); bounds.Encapsulate(b);
            float error = (bounds.min - mesh.bounds.min).magnitude + (bounds.max - mesh.bounds.max).magnitude;
            if (error < best) { best = error; perm = p; sign = sg; }
        }
        return best;
    }

    // Every MeshFilter in the open scene showing the glb's coop body (or already the walk-in mesh).
    private static List<MeshFilter> FindBodies(Data data, StringBuilder report)
    {
        var found = new List<MeshFilter>();
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (path == MeshPath) { found.Add(mf); continue; }
            if (path != Glb) continue;
            float error = Match(mf.sharedMesh, data, out _, out _);
            if (error < 0.01f) found.Add(mf);
        }
        foreach (MeshFilter mf in found) report.AppendLine($"coop body: {PathOf(mf.transform)} (lossy scale {mf.transform.lossyScale.x:0.000})");
        return found;
    }

    private static Mesh BuildMesh(Data data, Mesh original, StringBuilder report)
    {
        float error = Match(original, data, out int[] perm, out Vector3 sign);
        float det = sign.x * sign.y * sign.z * (Parity(perm) ? 1f : -1f);
        Vector3[] ov = original.vertices, on = original.normals;
        Vector2[] ou = original.uv;

        // Corner lookup on the original by position, to take its normal (and to decide whether V is flipped).
        var grid = new Dictionary<Vector3Int, List<int>>();
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));
        for (int i = 0; i < ov.Length; i++)
        {
            if (!grid.TryGetValue(Key(ov[i]), out var list)) grid[Key(ov[i])] = list = new List<int>();
            list.Add(i);
        }
        int Find(Vector3 p, Vector2 uv)
        {
            Vector3Int k = Key(p);
            int best = -1;
            float bestD = 1e-3f;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                if (grid.TryGetValue(k + new Vector3Int(dx, dy, dz), out var list))
                    foreach (int i in list)
                    {
                        float d = (ov[i] - p).sqrMagnitude + (ou[i] - uv).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
            return best;
        }

        int n = data.positions.Length;
        var positions = new Vector3[n];
        var normals = new Vector3[n];
        var uvs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            positions[i] = Map(data.positions[i], perm, sign);
            normals[i] = Map(data.normals[i], perm, sign).normalized;
            uvs[i] = new Vector2(data.uvs[i][0], data.uvs[i][1]);
        }
        // V as Blender has it, or flipped: whichever finds the original's corners.
        int straight = 0, flipped = 0;
        for (int i = 0; i < n; i += 7)
        {
            if (Find(positions[i], uvs[i]) >= 0) straight++;
            if (Find(positions[i], new Vector2(uvs[i].x, 1f - uvs[i].y)) >= 0) flipped++;
        }
        bool flipV = flipped > straight;
        int kept = 0;
        for (int i = 0; i < n; i++)
        {
            if (flipV) uvs[i].y = 1f - uvs[i].y;
            int o = Find(positions[i], uvs[i]);
            if (o >= 0 && on != null && on.Length == ov.Length) { normals[i] = on[o]; kept++; }
        }
        int[] tris = (int[])data.triangles.Clone();
        if (det < 0f)
            for (int t = 0; t < tris.Length; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);

        Directory.CreateDirectory(Folder);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool created = mesh == null;
        if (created) mesh = new Mesh();
        mesh.Clear();
        mesh.name = "SM_ChickenCoop_WalkIn";
        mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
        else EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        report.AppendLine($"mesh: axes ({string.Join(",", perm)}) signs {sign}, bounds error {error:0.0000}, winding {(det < 0 ? "reversed" : "kept")}, " +
                          $"V {(flipV ? "flipped" : "as is")} ({straight} vs {flipped} corner matches), {kept} of {n} corners kept the original normal, " +
                          $"{tris.Length / 3} triangles (original {original.triangles.Length / 3}); bounds {mesh.bounds.size} vs original {original.bounds.size}");
        return mesh;
    }

    private static bool Parity(int[] p)
    {
        int swaps = 0;
        var a = (int[])p.Clone();
        for (int i = 0; i < a.Length; i++)
            while (a[i] != i) { (a[i], a[a[i]]) = (a[a[i]], a[i]); swaps++; }
        return swaps % 2 == 0;
    }

    // The body's own material with its painted texture swapped for the barn-coloured one.
    private static Material BuildMaterial(MeshRenderer renderer, StringBuilder report)
    {
        Material source = renderer.sharedMaterials.FirstOrDefault(m => m != null && AssetDatabase.GetAssetPath(m) == Glb) ?? renderer.sharedMaterial;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null) { AssetDatabase.ImportAsset(TexturePath); texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath); }
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (AssetDatabase.GetAssetPath(source) == MaterialPath) return material;
        Texture painted = null;
        foreach (string name in source.GetTexturePropertyNames())
            if (source.GetTexture(name) is Texture2D t && t.width >= 1024) { painted = t; break; }
        Material made = new Material(source) { name = "M_ChickenCoop_Barn" };
        int swapped = 0;
        foreach (string name in made.GetTexturePropertyNames())
            if (painted != null && made.GetTexture(name) == painted) { made.SetTexture(name, texture); swapped++; }
        if (material == null) AssetDatabase.CreateAsset(made, MaterialPath);
        else { EditorUtility.CopySerialized(made, material); Object.DestroyImmediate(made); }
        AssetDatabase.SaveAssets();
        material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        report.AppendLine($"material: {source.shader.name}, {swapped} texture slot(s) now barn-coloured");
        return material;
    }

    private static void Fit(MeshFilter body, Mesh mesh, Material material)
    {
        Undo.RecordObject(body, "Make coop walk-in");
        body.sharedMesh = mesh;
        var renderer = body.GetComponent<MeshRenderer>();
        Undo.RecordObject(renderer, "Make coop walk-in");
        renderer.sharedMaterials = new[] { material };
        var collider = body.GetComponent<MeshCollider>();
        if (collider == null) collider = Undo.AddComponent<MeshCollider>(body.gameObject);
        Undo.RecordObject(collider, "Make coop walk-in");
        collider.sharedMesh = null;   // the mesh asset may have been rebuilt in place: make the collider take it anew
        collider.sharedMesh = mesh;
        collider.convex = false;
        Transform blocker = body.transform.Find(BlockerName);
        if (blocker == null)
        {
            blocker = new GameObject(BlockerName).transform;
            Undo.RegisterCreatedObjectUndo(blocker.gameObject, "Make coop walk-in");
            blocker.SetParent(body.transform, false);
            blocker.gameObject.AddComponent<MeshCollider>();
        }
        blocker.gameObject.layer = LayerMask.NameToLayer("CameraBlocker");
        var blockerCollider = blocker.GetComponent<MeshCollider>();
        blockerCollider.sharedMesh = null;
        blockerCollider.sharedMesh = mesh;
        blockerCollider.convex = false;
    }

    // An invisible plank on the ramp: one even slope from the ground at its foot onto the landing, so the chick never
    // meets the ramp's steeper foot or the landing board's bevelled lip where the two meet.
    private const string WalkwayName = "Coop Ramp Walkway";

    private static void AddWalkway(MeshFilter body, Data data, StringBuilder report)
    {
        Transform old = body.transform.Find(WalkwayName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Physics.SyncTransforms();
        Match(OriginalOf(), data, out int[] perm, out Vector3 sign);
        int blockerLayer = LayerMask.NameToLayer("CameraBlocker");
        Vector3 Surface(int i)
        {
            Vector3 p = body.transform.TransformPoint(Map(data.walk[i], perm, sign));
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.gameObject.layer != blockerLayer) best = Mathf.Max(best, hit.point.y);
            return new Vector3(p.x, float.IsNegativeInfinity(best) ? p.y : best, p.z);
        }
        // From the landing down towards the foot: the steepest even slope that still stays on or above every board of
        // the ramp, carried on until it meets the ground, so it starts flush with the ground and ends flush with the
        // landing.
        Vector3 top = Surface(2), rampFoot = Surface(0);
        Vector3 away = rampFoot - top; away.y = 0f;
        float reach = away.magnitude;
        away /= reach;
        float SurfaceAt(float d)
        {
            Vector3 p = top + away * d;
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(p.x, top.y + 0.5f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.gameObject.layer != blockerLayer) best = Mathf.Max(best, hit.point.y);
            return best;
        }
        float drop = float.MaxValue;   // metres down per metre out
        for (int s = 1; s <= 24; s++)
        {
            float d = reach * s / 24f, y = SurfaceAt(d);
            // Only the ramp itself: samples still on the landing (level with it) say nothing about the slope.
            if (!float.IsNegativeInfinity(y) && y < top.y - 0.01f) drop = Mathf.Min(drop, (top.y - y) / d);
        }
        if (drop == float.MaxValue || drop < 0.2f) { report.AppendLine("ramp walkway: could not measure the ramp, none added"); return; }
        float end = reach;
        for (float d = reach * 0.5f; d < reach * 2.5f; d += 0.01f)
            if (top.y - drop * d <= SurfaceAt(d) + 0.002f) { end = d; break; }
        Vector3 foot = top + away * end;
        foot.y = top.y - drop * end;
        Vector3 along = top - foot;
        const float width = 0.34f, thickness = 0.03f;
        var walkway = new GameObject(WalkwayName);
        Undo.RegisterCreatedObjectUndo(walkway, "Make coop walk-in");
        walkway.transform.SetParent(body.transform, false);
        Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.Cross(along, Vector3.Cross(Vector3.up, along)).normalized);
        walkway.transform.SetPositionAndRotation((foot + top) / 2f - rotation * Vector3.up * (thickness / 2f), rotation);
        Vector3 parentScale = body.transform.lossyScale;
        walkway.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
        var box = walkway.AddComponent<BoxCollider>();
        box.size = new Vector3(width, thickness, along.magnitude + 0.08f);
        float slope = Mathf.Atan2(along.y, new Vector2(along.x, along.z).magnitude) * Mathf.Rad2Deg;
        report.AppendLine($"ramp walkway: {along.magnitude:0.00} m at {slope:0} deg, from {foot} to {top}");
    }

    // A chick-sized capsule (0.22 m tall, 0.09 m radius, scaled like the player) stood at points from the foot of the
    // ramp into the room, and swept between them: what it would bump into on the way.
    private static void Check(MeshFilter body, Data data, StringBuilder report)
    {
        Match(AssetDatabase.GetAssetPath(body.sharedMesh) == MeshPath ? OriginalOf() : body.sharedMesh, data, out int[] perm, out Vector3 sign);
        const float height = 0.22f, radius = 0.09f, step = 0.026f;
        var points = new List<Vector3>();
        foreach (float[] w in data.walk)
        {
            Vector3 p = body.transform.TransformPoint(Map(w, perm, sign));
            // Stand on whatever is under it (coop or ground), searching from just above the point.
            Vector3 from = p + Vector3.up * 0.5f;
            float ground = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.gameObject.layer != LayerMask.NameToLayer("CameraBlocker")) ground = Mathf.Max(ground, hit.point.y);
            points.Add(new Vector3(p.x, float.IsNegativeInfinity(ground) ? p.y : ground, p.z));
        }
        int blockerLayer = LayerMask.NameToLayer("CameraBlocker");
        float Ground(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.gameObject.layer != blockerLayer && hit.point.y <= p.y + 0.3f - 0.001f) best = Mathf.Max(best, hit.point.y);
            return best;
        }
        report.AppendLine($"walk check for {PathOf(body.transform)} (following the surface in 12 steps between points):");
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 foot = points[i];
            Vector3 a = foot + Vector3.up * (radius + step), b = foot + Vector3.up * (height - radius);
            var hits = Physics.OverlapCapsule(a, b, radius - 0.005f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.gameObject.layer != blockerLayer).Select(c => c.name).Distinct().ToArray();
            string sweep = "";
            if (i + 1 < points.Count)
            {
                // Walk it like the controller would: small steps, each standing on the surface under it; record the
                // steepest slope, the biggest sudden step up and anything a step bumps into.
                Vector3 prev = foot, next = points[i + 1];
                float steepest = 0f, biggestStep = 0f;
                string bump = null;
                for (int s = 1; s <= 12 && bump == null; s++)
                {
                    Vector3 p = Vector3.Lerp(foot, next, s / 12f);
                    float g = Ground(p + Vector3.up * Mathf.Max(0f, prev.y - p.y + 0.06f));
                    if (float.IsNegativeInfinity(g)) g = p.y;
                    var q = new Vector3(p.x, g, p.z);
                    Vector3 d = q - prev;
                    float run = new Vector2(d.x, d.z).magnitude;
                    if (run > 1e-4f) steepest = Mathf.Max(steepest, Mathf.Atan2(d.y, run) * Mathf.Rad2Deg);
                    biggestStep = Mathf.Max(biggestStep, d.y - Mathf.Tan(45f * Mathf.Deg2Rad) * run);
                    Vector3 pa = prev + Vector3.up * (radius + step), pb = prev + Vector3.up * (height - radius);
                    Vector3 flat = new Vector3(d.x, Mathf.Max(d.y, 0f), d.z);
                    if (flat.sqrMagnitude > 1e-8f && Physics.CapsuleCast(pa, pb, radius - 0.01f, flat.normalized, out RaycastHit hit, flat.magnitude, ~0, QueryTriggerInteraction.Ignore)
                        && hit.collider.gameObject.layer != blockerLayer && hit.normal.y < 0.6f)
                        bump = $"BUMPS into {hit.collider.name} (wall facing {hit.normal}) at step {s}";
                    prev = q;
                }
                sweep = $" -> next: steepest {steepest:0} deg (limit 45), biggest step above that slope {Mathf.Max(0f, biggestStep):0.000} m (limit {step}), {bump ?? "clear"}";
            }
            report.AppendLine($"  {i}: stands at {foot} ({(hits.Length == 0 ? "free" : "OVERLAPS " + string.Join(", ", hits))}){sweep}");
        }
    }

    private static Mesh OriginalOf()
    {
        Data data = Load();
        return AssetDatabase.LoadAllAssetsAtPath(Glb).OfType<Mesh>().First(m => m.vertexCount > 0 && Match(m, data, out _, out _) < 0.01f);
    }

    // The doorway from outside with a chick on the landing, and the room from inside with a chick by the door.
    private static void Pictures(MeshFilter body, Data data)
    {
        Match(AssetDatabase.GetAssetPath(body.sharedMesh) == MeshPath ? OriginalOf() : body.sharedMesh, data, out int[] perm, out Vector3 sign);
        Vector3 W(int i) => body.transform.TransformPoint(Map(data.walk[i], perm, sign));
        var chickPrefab = Resources.Load<GameObject>("Prefabs/chick");
        var made = new List<GameObject>();
        Camera camera = MapSurvey.TempCamera();
        try
        {
            void Chick(Vector3 at, Vector3 lookTo)
            {
                if (chickPrefab == null) return;
                var c = Object.Instantiate(chickPrefab);
                c.hideFlags = HideFlags.HideAndDontSave;
                foreach (var mb in c.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                RaycastHit hit;
                float y = Physics.Raycast(at + Vector3.up * 0.4f, Vector3.down, out hit, 1.5f, ~(1 << LayerMask.NameToLayer("CameraBlocker")), QueryTriggerInteraction.Ignore) ? hit.point.y : at.y;
                Vector3 look = lookTo - at; look.y = 0f;
                c.transform.SetPositionAndRotation(new Vector3(at.x, y, at.z), Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look : Vector3.forward));
                made.Add(c);
            }
            Chick(W(2), W(4));
            Chick(W(5) + (W(4) - W(5)) * 0.3f, W(3));
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 300f;
            Vector3 door = W(3), outside = W(0), room = W(5);
            Vector3 view = door + (outside - door).normalized * 1.6f + Vector3.up * 0.7f + Vector3.Cross(Vector3.up, outside - door).normalized * 0.5f;
            camera.transform.SetPositionAndRotation(view, Quaternion.LookRotation(door + Vector3.up * 0.1f - view));
            Save(MapSurvey.Render(camera, 1280, 800), "outside_door.png");
            Vector3 far = room + Vector3.up * 0.3f;   // the middle of the room, looking at the doorway
            camera.transform.SetPositionAndRotation(far, Quaternion.LookRotation(door + Vector3.up * 0.08f - far));
            Save(MapSurvey.Render(camera, 1280, 800), "inside_room.png");
            Vector3 wide = door + (outside - door).normalized * 4f + Vector3.up * 2.2f + Vector3.Cross(Vector3.up, outside - door).normalized * 2.5f;
            camera.transform.SetPositionAndRotation(wide, Quaternion.LookRotation(door - wide));
            Save(MapSurvey.Render(camera, 1280, 800), "coop_wide.png");
        }
        finally
        {
            Object.DestroyImmediate(camera.gameObject);
            foreach (GameObject g in made) Object.DestroyImmediate(g);
        }
    }

    private static void Save(Texture2D tex, string file)
    {
        File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
