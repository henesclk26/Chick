using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SunflowerOptimization
{
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/sunflower_seed.glb");
        var source = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Mesh best = null;
        for (int grid = 4; grid <= 40; grid++)
        {
            var candidate = Cluster(source, grid);
            if (candidate.vertexCount >= 300 && candidate.vertexCount <= 700 &&
                (best == null || candidate.vertexCount > best.vertexCount))
            {
                if (best != null) UnityEngine.Object.DestroyImmediate(best);
                best = candidate;
            }
            else UnityEngine.Object.DestroyImmediate(candidate);
        }
        if (best == null) throw new InvalidOperationException("No suitable reduction found.");
        const string meshPath = "Assets/Art/Edibles/SunflowerSeed_Optimized.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        best.name = "SunflowerSeed_Optimized";
        if (mesh == null) { mesh = best; AssetDatabase.CreateAsset(mesh, meshPath); }
        else { EditorUtility.CopySerialized(best, mesh); UnityEngine.Object.DestroyImmediate(best); }
        const string path = "Assets/Prefabs/Edibles/SunflowerSeed.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.GetComponentInChildren<MeshFilter>().sharedMesh = mesh;
            var visual = root.transform.Find("Visual");
            // Source mesh's thinnest axis is Y; remove the importer upright rotation.
            visual.localRotation = Quaternion.identity;
            visual.localPosition = Vector3.zero;
            var renderer = visual.GetComponentInChildren<Renderer>();
            var b = renderer.bounds;
            visual.position -= new Vector3(b.center.x - root.transform.position.x,
                b.min.y - root.transform.position.y, b.center.z - root.transform.position.z);
            b = renderer.bounds;
            var box = root.GetComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
            root.transform.Find("BitePoint").localPosition = box.center;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return source.vertexCount + " -> " + mesh.vertexCount + " vertices; " + mesh.triangles.Length / 3 + " triangles";
    }

    private static Mesh Cluster(Mesh source, int grid)
    {
        var positions = source.vertices;
        var uv = source.uv;
        var normals = source.normals;
        var bounds = source.bounds;
        float cell = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / grid;
        var groups = new Dictionary<string, int>();
        var vertices = new List<Vector3>();
        var tex = new List<Vector2>();
        var ns = new List<Vector3>();
        var counts = new List<int>();
        var mapping = new int[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = (positions[i] - bounds.min) / cell;
            // UV buckets keep distant texture islands from being welded together.
            string key = Mathf.FloorToInt(p.x) + ":" + Mathf.FloorToInt(p.y) + ":" + Mathf.FloorToInt(p.z)
                + ":" + Mathf.FloorToInt(uv[i].x * 8) + ":" + Mathf.FloorToInt(uv[i].y * 8);
            int index;
            if (!groups.TryGetValue(key, out index))
            {
                index = vertices.Count; groups.Add(key, index);
                vertices.Add(Vector3.zero); tex.Add(Vector2.zero); ns.Add(Vector3.zero); counts.Add(0);
            }
            mapping[i] = index; vertices[index] += positions[i]; tex[index] += uv[i]; ns[index] += normals[i]; counts[index]++;
        }
        for (int i = 0; i < vertices.Count; i++) { vertices[i] /= counts[i]; tex[i] /= counts[i]; ns[i] = ns[i].normalized; }
        var triangles = new List<int>();
        var original = source.triangles;
        for (int i = 0; i < original.Length; i += 3)
        {
            int a = mapping[original[i]], b = mapping[original[i+1]], c = mapping[original[i+2]];
            if (a == b || a == c || b == c) continue;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices); mesh.SetUVs(0, tex); mesh.SetNormals(ns); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }
}
