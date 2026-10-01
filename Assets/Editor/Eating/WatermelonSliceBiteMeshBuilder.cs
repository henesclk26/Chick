using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the bite stages of Watermelon_Slice_1 from its Body mesh: the rind alone (with a pale inner wall where
/// the flesh met it) and the red flesh with 0–3 quarters eaten from the flat cut edge toward the rind, with bite
/// notches. Then wires <see cref="WatermelonSliceBites"/> on the slice prefab and on unpacked scene copies.
/// Rerun after changing the Body mesh or the settings below; the scene copies are left for you to save.
/// </summary>
public static class WatermelonSliceBiteMeshBuilder
{
    private const string BodyPath = "Assets/Art/Edibles/SeparableFoods/Watermelon_Slice_1/Body.asset";
    private const string OutputFolder = "Assets/Art/Edibles/SeparableFoods/Watermelon_Slice_1/BiteStages";
    private const string PrefabPath = "Assets/Assets/EdibleFoods/Watermelon_Slice_1.prefab";
    // Palette cells of the shared food atlas (Color_Texture_1024) used by the slice.
    private static readonly Vector2 FleshUV = new Vector2(.0533f, .4005f);
    private static readonly Vector2 PaleRindUV = new Vector2(.0467f, .0534f);
    // Bite notches per stage. Even counts keep the bottom centre of the face (the bite point) between notches.
    private static readonly int[] BiteCounts = { 0, 4, 4, 6 };
    private const float BiteAmplitude = .012f;
    private const float MinimumBand = .008f;
    private const float BitePointInset = .012f;
    private const int ArcSamples = 48;

    [MenuItem("Tools/Eating/Build Watermelon Slice Bite Stages")]
    private static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var bodyMesh = AssetDatabase.LoadAssetAtPath<Mesh>(BodyPath);
        if (bodyMesh == null) return "Watermelon bite stages: Body mesh not found at " + BodyPath;
        Vector3[] vertices = bodyMesh.vertices;
        Vector2[] uvs = bodyMesh.uv;
        int[] triangles = bodyMesh.triangles;

        bool IsFlesh(int t) => ((uvs[triangles[t]] + uvs[triangles[t + 1]] + uvs[triangles[t + 2]]) / 3f - FleshUV).magnitude < .01f;
        float thickness = 0f;
        for (int t = 0; t < triangles.Length; t += 3)
            if (IsFlesh(t)) for (int k = 0; k < 3; k++) thickness = Mathf.Max(thickness, vertices[triangles[t + k]].y);

        // Outline of the red front face (y = 0): its boundary vertices out on the arc, sorted by angle.
        var edgeUse = new Dictionary<(Vector2Int, Vector2Int), int>();
        var positions = new Dictionary<Vector2Int, Vector2>();
        Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
        for (int t = 0; t < triangles.Length; t += 3)
        {
            if (!IsFlesh(t)) continue;
            if (vertices[triangles[t]].y > .001f || vertices[triangles[t + 1]].y > .001f || vertices[triangles[t + 2]].y > .001f) continue;
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = vertices[triangles[t + k]], b = vertices[triangles[t + (k + 1) % 3]];
                Vector2Int ka = Key(a), kb = Key(b);
                positions[ka] = new Vector2(a.x, a.z);
                positions[kb] = new Vector2(b.x, b.z);
                var edge = string.CompareOrdinal(ka.ToString(), kb.ToString()) < 0 ? (ka, kb) : (kb, ka);
                edgeUse[edge] = edgeUse.TryGetValue(edge, out int used) ? used + 1 : 1;
            }
        }
        var boundary = new HashSet<Vector2Int>();
        foreach (var pair in edgeUse.Where(pair => pair.Value == 1)) { boundary.Add(pair.Key.Item1); boundary.Add(pair.Key.Item2); }
        float fleshRadius = boundary.Max(key => positions[key].magnitude);
        List<Vector2> outline = boundary.Select(key => positions[key]).Where(p => p.magnitude > fleshRadius * .9f)
            .OrderBy(Angle).ToList();
        if (outline.Count < 4) return "Watermelon bite stages: could not find the flesh outline.";
        float meanRadius = outline.Average(p => p.magnitude);

        // Angles to sample: the outline's own vertices (exact seam with the rind) plus an even spread for the notches.
        var angles = new List<float> { 0f, Mathf.PI };
        angles.AddRange(outline.Select(p => Mathf.Clamp(Angle(p), 0f, Mathf.PI)));
        for (int i = 1; i < ArcSamples; i++) angles.Add(i * Mathf.PI / ArcSamples);
        angles.Sort();
        angles = angles.Where((a, i) => i == 0 || a - angles[i - 1] > 1e-4f).ToList();

        Vector2 Outer(float theta)
        {
            var direction = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
            if (theta <= Angle(outline[0])) return direction * outline[0].magnitude;
            if (theta >= Angle(outline[outline.Count - 1])) return direction * outline[outline.Count - 1].magnitude;
            for (int i = 0; i < outline.Count - 1; i++)
            {
                if (theta > Angle(outline[i + 1])) continue;
                Vector2 p = outline[i], e = outline[i + 1] - p;
                float denominator = Cross(direction, e);
                if (Mathf.Abs(denominator) < 1e-9f) return p;
                return p - e * (Cross(direction, p) / denominator);
            }
            return direction * outline[outline.Count - 1].magnitude;
        }

        var baseRadius = new float[WatermelonSliceBites.Quarters];
        var fleshMeshes = new Mesh[WatermelonSliceBites.Quarters];
        for (int stage = 0; stage < WatermelonSliceBites.Quarters; stage++)
        {
            // Eaten area grows by a quarter per stage: an eaten disc of radius R·√(stage/4).
            baseRadius[stage] = stage == 0 ? 0f : meanRadius * Mathf.Sqrt(stage / (float)WatermelonSliceBites.Quarters);
            var builder = new FlatMeshBuilder(FleshUV);
            Vector2 Inner(float theta)
            {
                float radius = WatermelonSliceBites.InnerRadius(baseRadius[stage], BiteCounts[stage], BiteAmplitude, theta);
                radius = Mathf.Min(radius, Outer(theta).magnitude - MinimumBand);
                return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * Mathf.Max(0f, radius);
            }
            for (int i = 0; i < angles.Count - 1; i++)
            {
                Vector2 ia = Inner(angles[i]), ib = Inner(angles[i + 1]), oa = Outer(angles[i]), ob = Outer(angles[i + 1]);
                builder.Quad(V(ia, 0f), V(oa, 0f), V(ob, 0f), V(ib, 0f), Vector3.down);
                builder.Quad(V(ia, thickness), V(oa, thickness), V(ob, thickness), V(ib, thickness), Vector3.up);
                if (stage == 0) continue;
                Vector2 edge = ib - ia;
                Vector3 inward = new Vector3(edge.y, 0f, -edge.x).normalized;
                if (Vector3.Dot(inward, -V(ia + ib, 0f)) < 0f) inward = -inward;
                builder.Quad(V(ia, 0f), V(ib, 0f), V(ib, thickness), V(ia, thickness), inward);
            }
            // The flat cut edge (z = 0) at both ends.
            foreach (float end in new[] { angles[0], angles[angles.Count - 1] })
                builder.Quad(V(Inner(end), 0f), V(Outer(end), 0f), V(Outer(end), thickness), V(Inner(end), thickness), Vector3.back);
            fleshMeshes[stage] = builder.ToMesh($"Flesh_{stage * 25}Eaten");
        }

        // Rind: the Body without its red triangles, plus a pale wall where the flesh met it.
        var rind = new FlatMeshBuilder(PaleRindUV);
        Vector3[] normals = bodyMesh.normals;
        for (int t = 0; t < triangles.Length; t += 3)
            if (!IsFlesh(t)) rind.Copy(vertices, normals, uvs, triangles[t], triangles[t + 1], triangles[t + 2]);
        for (int i = 0; i < outline.Count - 1; i++)
        {
            Vector2 a = outline[i], b = outline[i + 1], edge = b - a;
            Vector3 inward = new Vector3(edge.y, 0f, -edge.x).normalized;
            if (Vector3.Dot(inward, -V(a + b, 0f)) < 0f) inward = -inward;
            rind.Quad(V(a, 0f), V(b, 0f), V(b, thickness), V(a, thickness), inward);
        }
        Mesh rindMesh = rind.ToMesh("Rind");

        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Art/Edibles/SeparableFoods/Watermelon_Slice_1", "BiteStages");
        rindMesh = SaveMesh(rindMesh, OutputFolder + "/Rind.asset");
        for (int stage = 0; stage < fleshMeshes.Length; stage++)
            fleshMeshes[stage] = SaveMesh(fleshMeshes[stage], $"{OutputFolder}/Flesh_{stage * 25}Eaten.asset");
        AssetDatabase.SaveAssets();

        var settings = new Settings
        {
            bodyMesh = bodyMesh, rind = rindMesh, flesh = fleshMeshes, baseRadius = baseRadius, thickness = thickness,
            // Bottom centre of the front face, just inside the last band of flesh.
            bitePoint = new Vector3(0f, -.002f, meanRadius - BitePointInset)
        };

        int prefabs = 0;
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { if (Configure(prefabRoot, settings, false)) { PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath); prefabs++; } }
        finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }

        int sceneCopies = 0;
        foreach (var source in Object.FindObjectsByType<EdibleFoodSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Prefab instances inherit the component from the prefab.
            if (PrefabUtility.IsPartOfPrefabInstance(source.gameObject)) continue;
            if (Configure(source.gameObject, settings, true)) sceneCopies++;
        }
        return $"Watermelon bite stages built: flesh radius {meanRadius:0.0000}, thickness {thickness:0.0000}, " +
               $"outline {outline.Count} points; prefab {prefabs}, unpacked scene copies {sceneCopies} (save the scene to keep them).";
    }

    private struct Settings
    {
        public Mesh bodyMesh, rind;
        public Mesh[] flesh;
        public float[] baseRadius;
        public float thickness;
        public Vector3 bitePoint;
    }

    private static bool Configure(GameObject root, Settings settings, bool undo)
    {
        Transform bodyTransform = root.transform.Find("Body");
        if (bodyTransform == null || !bodyTransform.TryGetComponent(out MeshFilter body) || body.sharedMesh != settings.bodyMesh) return false;
        var bites = root.GetComponent<WatermelonSliceBites>();
        if (bites == null) bites = undo ? Undo.AddComponent<WatermelonSliceBites>(root) : root.AddComponent<WatermelonSliceBites>();
        var serialized = new SerializedObject(bites);
        serialized.FindProperty("body").objectReferenceValue = body;
        serialized.FindProperty("rindMesh").objectReferenceValue = settings.rind;
        SetArray(serialized.FindProperty("fleshStages"), settings.flesh.Length, (p, i) => p.objectReferenceValue = settings.flesh[i]);
        SetArray(serialized.FindProperty("stageBaseRadius"), settings.baseRadius.Length, (p, i) => p.floatValue = settings.baseRadius[i]);
        SetArray(serialized.FindProperty("stageBiteCount"), BiteCounts.Length, (p, i) => p.intValue = BiteCounts[i]);
        serialized.FindProperty("biteAmplitude").floatValue = BiteAmplitude;
        serialized.FindProperty("thickness").floatValue = settings.thickness;
        serialized.FindProperty("bitePoint").vector3Value = settings.bitePoint;
        if (undo) serialized.ApplyModifiedProperties(); else serialized.ApplyModifiedPropertiesWithoutUndo();
        if (undo) EditorSceneManager.MarkSceneDirty(root.scene);
        return true;
    }

    private static void SetArray(SerializedProperty array, int size, System.Action<SerializedProperty, int> set)
    {
        array.arraySize = size;
        for (int i = 0; i < size; i++) set(array.GetArrayElementAtIndex(i), i);
    }

    // Overwrites an existing asset in place so references (and GUIDs) survive a rebuild.
    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    private static float Angle(Vector2 p) => Mathf.Atan2(p.y, p.x);
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    // Plane point (x, z) at height y (the slice's thickness axis) in the Body mesh's space.
    private static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

    // Flat-shaded triangles in one atlas colour; winding follows the requested outward normal.
    private sealed class FlatMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly Vector2 uv;

        public FlatMeshBuilder(Vector2 uv) => this.uv = uv;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            Triangle(a, b, c, normal);
            Triangle(a, c, d, normal);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(cross, normal) < 0f) (b, c) = (c, b);
            foreach (Vector3 p in new[] { a, b, c })
            {
                triangles.Add(vertices.Count);
                vertices.Add(p);
                normals.Add(normal);
                uvs.Add(uv);
            }
        }

        public void Copy(Vector3[] sourceVertices, Vector3[] sourceNormals, Vector2[] sourceUVs, params int[] indices)
        {
            foreach (int index in indices)
            {
                triangles.Add(vertices.Count);
                vertices.Add(sourceVertices[index]);
                normals.Add(sourceNormals[index]);
                uvs.Add(sourceUVs[index]);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
