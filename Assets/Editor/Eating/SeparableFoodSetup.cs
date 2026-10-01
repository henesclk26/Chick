using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Extract the verified disconnected toppings without changing their source geometry.</summary>
public static class SeparableFoodSetup
{
    public const string Folder = "Assets/Assets/EdibleFoods";
    public const string MeshFolder = "Assets/Art/Edibles/SeparableFoods";
    public static readonly string[] Names = { "Watermelon_002", "Watermelon_003", "Watermelon_Slice_2",
        "Tomato_Piece", "Pumpkin_Quarter", "Cookie_1", "Apple_Red_Half_2", "Watermelon_Slice_1",
        "Cookie_2", "Croissant", "Donut", "Bread_1" };
    private static readonly int[] Counts = { 24, 12, 10, 16, 8, 7, 2, 22, 8, 11, 19, 30 };
    private static readonly int[] PieceTriangles = { 25, 25, 40, 40, 40, 64, 64, 40, 64, 64, 48, 40 };

    public static Mesh SourceMesh(string name)
    {
        string path = name == "Watermelon_002" || name == "Watermelon_003"
            ? "Assets/ithappy/Food_Free/Meshes/" + name + ".fbx"
            : "Assets/Mnostva Art/Food/Meshes/Food/" + name + ".fbx";
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    // Welding is used only to recognize islands. Original split normals/UV seams are retained.
    private static List<List<int>> Islands(Mesh mesh)
    {
        Vector3[] v = mesh.vertices;
        int[] t = mesh.triangles;
        int[] parent = Enumerable.Range(0, v.Length).ToArray();
        Func<int, int> root = a => { while (parent[a] != a) a = parent[a]; return a; };
        float epsilon = mesh.bounds.size.magnitude * 0.000001f;
        for (int i = 0; i < v.Length; i++)
        for (int j = 0; j < i; j++)
            if ((v[i] - v[j]).sqrMagnitude < epsilon * epsilon) parent[root(i)] = root(j);
        for (int i = 0; i < t.Length; i += 3)
        {
            parent[root(t[i])] = root(t[i + 1]);
            parent[root(t[i + 2])] = root(t[i + 1]);
        }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < t.Length; i += 3)
        {
            int r = root(t[i]);
            if (!groups.ContainsKey(r)) groups[r] = new List<int>();
            groups[r].AddRange(new[] { t[i], t[i + 1], t[i + 2] });
        }
        return groups.Values.OrderBy(g => g.Min()).ToList();
    }

    private static Mesh Extract(Mesh source, List<int> triangles, string name, Vector3 pivot)
    {
        var original = triangles.Distinct().OrderBy(i => i).ToArray();
        var remap = new Dictionary<int, int>();
        for (int i = 0; i < original.Length; i++) remap.Add(original[i], i);
        var mesh = new Mesh { name = name, indexFormat = source.indexFormat };
        var vertices = source.vertices;
        mesh.vertices = original.Select(i => vertices[i] - pivot).ToArray();
        var normals = source.normals;
        if (normals.Length == vertices.Length) mesh.normals = original.Select(i => normals[i]).ToArray();
        var tangents = source.tangents;
        if (tangents.Length == vertices.Length) mesh.tangents = original.Select(i => tangents[i]).ToArray();
        var colors = source.colors;
        if (colors.Length == vertices.Length) mesh.colors = original.Select(i => colors[i]).ToArray();
        for (int channel = 0; channel < 8; channel++)
        {
            var uv = new List<Vector4>();
            source.GetUVs(channel, uv);
            if (uv.Count == vertices.Length) mesh.SetUVs(channel, original.Select(i => uv[i]).ToList());
        }
        mesh.triangles = triangles.Select(i => remap[i]).ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void EnsureFolder(string path)
    {
        string parent = "Assets";
        foreach (string part in path.Split('/').Skip(1))
        {
            string next = parent + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part);
            parent = next;
        }
    }

    private static void SetRenderer(GameObject go, Mesh mesh, MeshRenderer source)
    {
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        EditorUtility.CopySerialized(source, renderer);
    }

    [MenuItem("Chick/Eating/Create Separable Food Prefabs")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        int layer = LayerMask.NameToLayer("Edible");
        if (layer < 0) throw new InvalidOperationException("Missing Edible layer.");
        // Validate the entire selection before changing anything.
        for (int i = 0; i < Names.Length; i++)
        {
            var go = GameObject.Find(Names[i]);
            var mesh = SourceMesh(Names[i]);
            if (!go || !mesh || mesh.subMeshCount != 1 || go.transform.childCount != 0 ||
                !go.GetComponent<MeshFilter>() || !go.GetComponent<MeshRenderer>() ||
                AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + Names[i] + ".prefab"))
                throw new InvalidOperationException("Unexpected source or existing output: " + Names[i]);
            if (Islands(mesh).Count(g => g.Count / 3 == PieceTriangles[i]) != Counts[i])
                throw new InvalidOperationException("Source topology changed: " + Names[i]);
        }
        EnsureFolder(Folder);
        EnsureFolder(MeshFolder);
        var report = new StringBuilder();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create separable foods");
        for (int i = 0; i < Names.Length; i++)
        {
            string name = Names[i];
            var go = GameObject.Find(name);
            var source = SourceMesh(name);
            var renderer = go.GetComponent<MeshRenderer>();
            var groups = Islands(source);
            var pieces = groups.Where(g => g.Count / 3 == PieceTriangles[i]).ToList();
            var bodyTriangles = groups.Where(g => g.Count / 3 != PieceTriangles[i]).SelectMany(g => g).ToList();
            EnsureFolder(MeshFolder + "/" + name);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            Undo.RegisterFullObjectHierarchyUndo(go, "Split " + name);
            var body = new GameObject("Body");
            Undo.RegisterCreatedObjectUndo(body, "Create food body");
            body.transform.SetParent(go.transform, false);
            body.layer = go.layer;
            var bodyMesh = Extract(source, bodyTriangles, name + "_Body", Vector3.zero);
            AssetDatabase.CreateAsset(bodyMesh, MeshFolder + "/" + name + "/Body.asset");
            SetRenderer(body, bodyMesh, renderer);
            var collider = body.AddComponent<MeshCollider>();
            var oldCollider = go.GetComponent<MeshCollider>();
            if (oldCollider) EditorUtility.CopySerialized(oldCollider, collider);
            // Stationary food bodies need their actual surface, not an approximate convex hull.
            collider.convex = false;
            collider.isTrigger = false;
            collider.sharedMesh = bodyMesh;
            if (oldCollider) Undo.DestroyObjectImmediate(oldCollider);
            var container = new GameObject("EdiblePieces");
            Undo.RegisterCreatedObjectUndo(container, "Create food pieces");
            container.transform.SetParent(go.transform, false);
            for (int n = 0; n < pieces.Count; n++)
            {
                string pieceName = "Piece_" + (n + 1).ToString("D3");
                var bounds = new Bounds(source.vertices[pieces[n][0]], Vector3.zero);
                foreach (int vertex in pieces[n]) bounds.Encapsulate(source.vertices[vertex]);
                Vector3 pivot = bounds.center;
                Mesh mesh = Extract(source, pieces[n], name + "_" + pieceName, pivot);
                AssetDatabase.CreateAsset(mesh, MeshFolder + "/" + name + "/" + pieceName + ".asset");
                var piece = new GameObject(pieceName);
                piece.transform.SetParent(container.transform, false);
                piece.transform.localPosition = pivot;
                piece.layer = layer;
                var visual = new GameObject("Visual");
                visual.transform.SetParent(piece.transform, false);
                visual.layer = layer;
                SetRenderer(visual, mesh, renderer);
                var trigger = piece.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = mesh.bounds.center;
                trigger.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.001f);
                var bite = new GameObject("BitePoint").transform;
                bite.SetParent(piece.transform, false);
                bite.localPosition = mesh.bounds.center;
                bite.gameObject.layer = layer;
                var edible = piece.AddComponent<EdibleObject>();
                var data = new SerializedObject(edible);
                data.FindProperty("bitePoint").objectReferenceValue = bite;
                data.FindProperty("contactVisual").objectReferenceValue = visual.transform;
                data.FindProperty("interactionAssistMultiplier").floatValue = 1.35f;
                if (name == "Pumpkin_Quarter")
                {
                    data.FindProperty("interactionAssistMultiplier").floatValue = 1.5f;
                    data.FindProperty("maxVerticalInteractionOffset").floatValue = 0.17f;
                }
                if (name == "Watermelon_Slice_1" || name == "Watermelon_Slice_2" || name == "Tomato_Piece")
                {
                    data.FindProperty("requireExposedFace").boolValue = true;
                    data.FindProperty("localExposedFaceNormal").vector3Value =
                        pivot.y > 0.008f ? Vector3.up : Vector3.down;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            if (name == "Watermelon_Slice_1" || name == "Watermelon_Slice_2" || name == "Tomato_Piece")
            {
                // The source slices lie on their back. Stand both faces upright so
                // players can walk around to the still uneaten reverse-side seeds.
                float lift = source.bounds.max.z;
                body.transform.localPosition = Vector3.up * lift;
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                container.transform.localPosition = Vector3.up * lift;
                container.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Undo.DestroyObjectImmediate(go.GetComponent<MeshFilter>());
            Undo.DestroyObjectImmediate(renderer);
            var foodSource = Undo.AddComponent<EdibleFoodSource>(go);
            ConfigureWholeFood(foodSource);
            Vector3 position = go.transform.localPosition;
            Quaternion rotation = go.transform.localRotation;
            Vector3 scale = go.transform.localScale;
            try
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAssetAndConnect(go, Folder + "/" + name + ".prefab", InteractionMode.UserAction);
            }
            finally
            {
                go.transform.localPosition = position;
                go.transform.localRotation = rotation;
                go.transform.localScale = scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            }
            report.AppendLine(name + ": " + pieces.Count + " pieces, body " + bodyMesh.vertexCount + " vertices");
        }
        EdibleFoodSourceIdentity.EnsureSceneIds();
        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Separable food prefabs created:\n" + report);
    }

    public static void ConfigureWholeFood(EdibleFoodSource source)
    {
        var whole = source.GetComponent<EdibleObject>();
        if (!whole) whole = source.gameObject.AddComponent<EdibleObject>();
        whole.enabled = false;
        var edibleData = new SerializedObject(whole);
        edibleData.FindProperty("requiredEatLevel").intValue = 1;
        edibleData.ApplyModifiedPropertiesWithoutUndo();
        var sourceData = new SerializedObject(source);
        sourceData.FindProperty("wholeFood").objectReferenceValue = whole;
        sourceData.FindProperty("wholeFoodEatingEnabled").boolValue = false;
        sourceData.ApplyModifiedPropertiesWithoutUndo();
    }
}
