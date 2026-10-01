using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds only the laid-egg presentation. Vendor models and hatch animations stay untouched.
public static class EggPresentationAuthoring
{
    private const string Folder = "Assets/Resources/EggLaying";
    private struct Face
    {
        public int a, b, c;
        public Vector2 ua, ub, uc;
        public Face(int a, int b, int c, Vector2 ua, Vector2 ub, Vector2 uc)
        { this.a = a; this.b = b; this.c = c; this.ua = ua; this.ub = ub; this.uc = uc; }
    }
    private sealed class Edge
    {
        public int a, b, index;
        public readonly List<int> opposite = new();
    }
    private static long Key(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

    [MenuItem("Tools/Chick/Rebuild Soft Laid Egg")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Build in edit mode only.");
        var source = Resources.Load<GameObject>("Prefabs/egg");
        if (source == null) throw new InvalidOperationException("Animals_3D egg prefab missing.");
        GameObject instance = null, output = null;
        Mesh baked = null, mesh = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(source);
            instance.hideFlags = HideFlags.HideAndDontSave;
            foreach (var animator in instance.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            baked = new Mesh(); renderer.BakeMesh(baked);
            var transform = instance.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            var vertices = baked.vertices;
            var uv = baked.uv;
            var points = new List<Vector3>();
            var remap = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = transform.MultiplyPoint3x4(vertices[i]);
                int found = points.FindIndex(p => (p - point).sqrMagnitude < 1e-12f);
                if (found < 0) { found = points.Count; points.Add(point); }
                remap[i] = found;
            }
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            var faces = new List<Face>();
            var triangles = baked.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                faces.Add(new Face(remap[a], remap[b], remap[c], uv[a], uv[b], uv[c]));
            }
            // Two modest Loop passes: 112 -> 1792 triangles, built once, never at runtime.
            for (int pass = 0; pass < 2; pass++) Subdivide(ref points, ref faces);
            var softBounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) softBounds.Encapsulate(point);
            // The vendor egg is almost as wide as it is tall (0.88), which reads as fruit.
            // Reprofile the smooth topology as a convex ovoid: broad rounded lower half,
            // gently tapered upper half, no pear neck, flat base or pinched tip.
            float height = bounds.size.y;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i] - softBounds.center;
                Vector3 direction = new Vector3(p.x / softBounds.extents.x,
                    p.y / softBounds.extents.y, p.z / softBounds.extents.z).normalized;
                float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
                float radius = height * .34f * horizontal * (1f - .16f * direction.y);
                // Slightly fuller lower body; preserve the tip and the current height.
                float lowerBody = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, -.6f, direction.y));
                radius *= 1f + .06f * lowerBody;
                Vector3 radial = horizontal > .00001f
                    ? new Vector3(direction.x / horizontal, 0f, direction.z / horizontal) : Vector3.zero;
                points[i] = bounds.center + radial * radius + Vector3.up * (height * .5f * direction.y);
            }

            var normals = new Vector3[points.Count];
            foreach (var f in faces)
            {
                var normal = Vector3.Cross(points[f.b] - points[f.a], points[f.c] - points[f.a]);
                normals[f.a] += normal; normals[f.b] += normal; normals[f.c] += normal;
            }
            for (int i = 0; i < normals.Length; i++) normals[i].Normalize();
            var finalPoints = new List<Vector3>(); var finalNormals = new List<Vector3>();
            var finalUV = new List<Vector2>(); var finalTriangles = new List<int>();
            var wedges = new Dictionary<(int, Vector2), int>();
            void Add(int index, Vector2 texcoord)
            {
                var key = (index, texcoord);
                if (!wedges.TryGetValue(key, out int vertex))
                {
                    vertex = finalPoints.Count; wedges.Add(key, vertex);
                    finalPoints.Add(points[index]); finalNormals.Add(normals[index]); finalUV.Add(texcoord);
                }
                finalTriangles.Add(vertex);
            }
            foreach (var f in faces) { Add(f.a, f.ua); Add(f.b, f.ub); Add(f.c, f.uc); }
            mesh = new Mesh { name = "Soft Laid Egg" };
            mesh.SetVertices(finalPoints); mesh.SetNormals(finalNormals); mesh.SetUVs(0, finalUV);
            mesh.SetTriangles(finalTriangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            const string meshPath = Folder + "/SoftEgg.asset";
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (savedMesh == null) { AssetDatabase.CreateAsset(mesh, meshPath); savedMesh = mesh; mesh = null; }
            else { EditorUtility.CopySerialized(mesh, savedMesh); EditorUtility.SetDirty(savedMesh); }
            AssetDatabase.SaveAssetIfDirty(savedMesh);
            output = new GameObject("Soft Laid Egg");
            output.AddComponent<MeshFilter>().sharedMesh = savedMesh;
            var outputRenderer = output.AddComponent<MeshRenderer>();
            outputRenderer.sharedMaterial = WhiteShellMaterial();
            outputRenderer.shadowCastingMode = renderer.shadowCastingMode;
            outputRenderer.receiveShadows = true;
            PrefabUtility.SaveAsPrefabAsset(output, Folder + "/SoftEgg.prefab");
            Debug.Log($"Soft egg built: {triangles.Length / 3} -> {savedMesh.triangles.Length / 3} triangles; original asset unchanged.");
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
            if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static Material WhiteShellMaterial()
    {
        const string path = Folder + "/WhiteEgg.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "WhiteEgg" };
        // Neutral warm white, softly shaded matte shell. No vendor toon bands or metal reflection.
        material.SetColor("_BaseColor", new Color(.94f, .925f, .89f, 1f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .18f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.enableInstancing = true;
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static void Subdivide(ref List<Vector3> points, ref List<Face> faces)
    {
        var edges = new Dictionary<long, Edge>();
        var neighbours = new HashSet<int>[points.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = new HashSet<int>();
        void AddEdge(int a, int b, int opposite)
        {
            long key = Key(a, b);
            if (!edges.TryGetValue(key, out var edge)) { edge = new Edge { a = a, b = b }; edges.Add(key, edge); }
            edge.opposite.Add(opposite); neighbours[a].Add(b); neighbours[b].Add(a);
        }
        foreach (var f in faces) { AddEdge(f.a, f.b, f.c); AddEdge(f.b, f.c, f.a); AddEdge(f.c, f.a, f.b); }
        var next = new List<Vector3>(points.Count + edges.Count);
        for (int i = 0; i < points.Count; i++)
        {
            int count = neighbours[i].Count;
            float beta = count == 3 ? 3f / 16f : 3f / (8f * count);
            var sum = Vector3.zero;
            foreach (int neighbour in neighbours[i]) sum += points[neighbour];
            next.Add((1f - count * beta) * points[i] + beta * sum);
        }
        foreach (var edge in edges.Values)
        {
            if (edge.opposite.Count != 2) throw new InvalidOperationException("Egg must be a closed manifold mesh.");
            edge.index = next.Count;
            next.Add((points[edge.a] + points[edge.b]) * .375f +
                (points[edge.opposite[0]] + points[edge.opposite[1]]) * .125f);
        }
        var nextFaces = new List<Face>(faces.Count * 4);
        foreach (var f in faces)
        {
            int ab = edges[Key(f.a, f.b)].index, bc = edges[Key(f.b, f.c)].index, ca = edges[Key(f.c, f.a)].index;
            var uab = (f.ua + f.ub) * .5f; var ubc = (f.ub + f.uc) * .5f; var uca = (f.uc + f.ua) * .5f;
            nextFaces.Add(new Face(f.a, ab, ca, f.ua, uab, uca));
            nextFaces.Add(new Face(f.b, bc, ab, f.ub, ubc, uab));
            nextFaces.Add(new Face(f.c, ca, bc, f.uc, uca, ubc));
            nextFaces.Add(new Face(ab, bc, ca, uab, ubc, uca));
        }
        points = next; faces = nextFaces;
    }
}
