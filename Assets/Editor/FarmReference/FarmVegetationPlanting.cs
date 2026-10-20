using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using sc.terrain.vegetationspawner;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Plants the map's vegetation in the farm's own logic:
/// - Orchard: the apple, cherry and orange trees (FruitTreeSamples) in three straight rows on the flattest open
///   ground close to the barn, outside the farm fences. GameObjects, since an orchard is laid out by hand.
/// - Oaks: a few big shade trees on open ground by the roads (a few metres off the path), away from the farm.
/// - Nature Starter Kit 2 trees and bushes, spread over the open countryside by the Vegetation Spawner as terrain
///   trees (instanced, cheap). The kit's old Tree Creator trees have no mesh in this Unity version: their mesh is
///   generated again from the tree data and saved with URP materials as Assets/Art/NatureKitURP (the kit itself is not
///   changed). Only painted grass counts as ground for them, so no tree stands on a path, field or yard; invisible
///   "exclusion zones" (the spawner's temporary colliders, only switched on while it spawns) keep the farm, the
///   orchard, the oaks and every tall object without its own collider clear, also when the spawner is run again from
///   its own inspector.
/// Writes review renders to Temp/MapSurvey (after_*.png). "Remove Planted Vegetation" takes it all out again.
/// </summary>
public static class FarmVegetationPlanting
{
    private const string GroupName = "11 Planted Vegetation";
    private const string SpawnerName = "Vegetation Spawner";
    private const string ZonesName = "Vegetation Exclusion Zones";
    private const string KitSource = "Assets/NatureStarterKit2/Nature/";
    private const string KitFolder = "Assets/Art/NatureKitURP";
    private const string FruitPrefabs = "Assets/Art/FruitTrees/Prefabs/";
    private static readonly string[] OrchardRows = { "AppleTree", "CherryTree", "OrangeTree" };
    private const float RowSpacing = 8f, TreeSpacing = 7f, OrchardMargin = 2f;
    private const float EdgeMargin = 20f;            // nothing planted this close to where the terrain ends
    private const int OakCount = 3;
    private const float KitTreeHeight = 8f, KitBushHeight = 1.4f;
    private const string ReviewFolder = "Temp/MapSurvey";

    // Terrain layers that are grass (the rest are paths, earth, pebbles and tilled soil).
    private static bool IsGrass(string layer) =>
        (layer.Contains("Green") || layer.Contains("Meadow")) && !layer.Contains("Worn") && !layer.Contains("Earth") &&
        !layer.Contains("Sand") && !layer.Contains("Damp");
    // Real paths and fields only: the pebble and old-earth layers are small patches scattered over the meadows.
    private static bool IsPath(string layer) =>
        layer.Contains("Trodden") || layer.Contains("Path") || layer.Contains("Tilled");

    [MenuItem("Tools/Chick/Map/Plant Vegetation")]
    private static void Plant()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Vegetation", "Exit play mode first.", "OK"); return; }
        var root = GameObject.Find("Farm Reference Map");
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (root == null || terrain == null) { EditorUtility.DisplayDialog("Vegetation", "The open scene has no 'Farm Reference Map' with its terrain.", "OK"); return; }
        var report = new List<string>();
        try
        {
            EditorUtility.DisplayProgressBar("Vegetation", "Converting Nature Starter Kit 2", 0.05f);
            var kitTrees = new List<GameObject>(); var kitBushes = new List<GameObject>();
            // Regenerating a Tree Creator mesh also rewrites the kit's own atlas textures, prefab and materials: keep
            // the kit exactly as it shipped by putting its files back afterwards.
            var kitFiles = new[] { "Assets/NatureStarterKit2/Nature", "Assets/NatureStarterKit2/Materials" }
                .Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith(".meta")).ToDictionary(f => f, File.ReadAllBytes);
            try
            {
                for (int i = 1; i <= 4; i++) { var p = ConvertKitPrefab("tree0" + i, KitTreeHeight, report); if (p) kitTrees.Add(p); }
                for (int i = 1; i <= 6; i++) { var p = ConvertKitPrefab("bush0" + i, KitBushHeight, report); if (p) kitBushes.Add(p); }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                foreach (var file in kitFiles)
                    if (!File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value))
                    {
                        File.WriteAllBytes(file.Key, file.Value);
                        AssetDatabase.ImportAsset(file.Key.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                    }
            }

            RemoveExisting(root, terrain, report, quiet: true);
            Physics.SyncTransforms();
            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "Plant vegetation");
            group.transform.SetParent(root.transform, false);

            var map = new GroundMap(terrain);
            Bounds farm = FarmBounds(root);
            Vector3 barn = BarnPosition(root, farm);
            report.Add($"farm (fences and pasture) {farm.min.x:F0}..{farm.max.x:F0} x {farm.min.z:F0}..{farm.max.z:F0} z, barn at {barn.x:F0}, {barn.z:F0}");

            EditorUtility.DisplayProgressBar("Vegetation", "Orchard", 0.3f);
            Rect orchard = PlantOrchard(group.transform, map, farm, barn, root.transform, report);
            EditorUtility.DisplayProgressBar("Vegetation", "Oaks", 0.4f);
            List<Vector3> oaks = PlantOaks(group.transform, map, farm, orchard, report);

            EditorUtility.DisplayProgressBar("Vegetation", "Exclusion zones", 0.5f);
            Collider[] zones = BuildZones(root, group.transform, farm, orchard, oaks, report);
            EditorUtility.DisplayProgressBar("Vegetation", "Vegetation Spawner", 0.6f);
            if (kitTrees.Count + kitBushes.Count > 0) Spawn(root, terrain, kitTrees, kitBushes, zones, report);
            else report.Add("Nature Starter Kit 2 could not be converted, so the spawner was not run.");

            EditorUtility.DisplayProgressBar("Vegetation", "Review renders", 0.85f);
            RenderReview(terrain, farm, orchard, oaks);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
        finally { EditorUtility.ClearProgressBar(); }
        File.WriteAllLines(Path.Combine(ReviewFolder, "after.txt"), report);
        Debug.Log("Vegetation planted:\n" + string.Join("\n", report) + "\nSave the scene to keep it; Tools/Chick/Map/Remove Planted Vegetation takes it out again.");
    }

    [MenuItem("Tools/Chick/Map/Remove Planted Vegetation")]
    private static void Remove()
    {
        var root = GameObject.Find("Farm Reference Map");
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (root == null || terrain == null) return;
        var report = new List<string>();
        RemoveExisting(root, terrain, report, quiet: false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(string.Join("\n", report));
    }

    private static void RemoveExisting(GameObject root, Terrain terrain, List<string> report, bool quiet)
    {
        // Scenery hidden to make room for the orchard comes back.
        Transform planted = SceneLookup.FindAnywhere(root, GroupName);
        var record = planted != null ? planted.GetComponent<MapSectionRecord>() : null;
        if (record != null)
        {
            foreach (var go in record.hidden)
                if (go != null) { Undo.RecordObject(go, "Remove planted vegetation"); go.SetActive(true); }
            if (!quiet) report.Add("Shown again: " + record.hidden.Count(g => g != null) + " scenery items hidden for the orchard.");
        }
        foreach (string name in new[] { GroupName, SpawnerName, ZonesName })
        {
            Transform t = SceneLookup.FindAnywhere(root, name);
            if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); if (!quiet) report.Add("Removed " + name + "."); }
        }
        // The terrain had no trees of its own before: every terrain tree is the spawner's.
        if (terrain.terrainData.treeInstanceCount > 0 || terrain.terrainData.treePrototypes.Length > 0)
        {
            terrain.terrainData.SetTreeInstances(new TreeInstance[0], false);
            terrain.terrainData.treePrototypes = new TreePrototype[0];
            terrain.Flush();
            EditorUtility.SetDirty(terrain.terrainData);
            if (!quiet) report.Add("Removed the terrain trees.");
        }
    }

    // ---------------------------------------------------------------- Nature Starter Kit 2

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // Regenerates the Tree Creator mesh from the kit prefab's tree data, scales it to height and saves it with URP
    // materials (bark opaque, leaves cut out, both from the kit's optimized atlas diffuse.png) as a new prefab.
    private static GameObject ConvertKitPrefab(string name, float height, List<string> report)
    {
        string prefabPath = KitFolder + "/" + name + "_URP.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(KitSource + name + "_Textures/diffuse.png");
        if (existing != null)
        {
            // Converted before: only bring the materials and the leaf lighting up to date.
            var renderer = existing.GetComponent<MeshRenderer>();
            var filter = existing.GetComponent<MeshFilter>();
            if (renderer != null && filter != null && filter.sharedMesh != null && atlas != null)
            {
                foreach (var m in renderer.sharedMaterials)
                    if (m != null) KitMaterial(name, m.name.EndsWith("_Leaves"), atlas);
                FixLeafNormals(filter.sharedMesh, renderer.sharedMaterials);
                EditorUtility.SetDirty(filter.sharedMesh);
                AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            }
            return existing;
        }
        string source = KitSource + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null || atlas == null) { report.Add(name + ": not found"); return null; }
        GameObject contents = PrefabUtility.LoadPrefabContents(source);
        try
        {
            // What the prefab file holds: the tree data (Tree Creator), its generated mesh and optimized materials.
            UnityEngine.Object[] parts = AssetDatabase.LoadAllAssetsAtPath(source);
            string inventory = string.Join(", ", parts.Where(p => p != null).Select(p => p.GetType().Name + " '" + p.name + "'" +
                                                                                       (p is Mesh m ? $" ({m.vertexCount} vertices)" : "")));
            var tree = contents.GetComponent<Tree>();
            object data = tree != null && tree.data != null ? (object)tree.data
                : parts.FirstOrDefault(p => p != null && p.GetType().Name == "TreeData");
            Mesh generated = null;
            Material[] outMaterials = null;
            if (data != null)
            {
                Type type = data.GetType();
                FieldInfo meshField = type.GetField("mesh", Any);
                if (meshField != null && meshField.GetValue(data) == null) meshField.SetValue(data, new Mesh { name = name });
                type.GetMethod("ValidateReferences", Any)?.Invoke(data, null);
                MethodInfo update = type.GetMethods(Any).FirstOrDefault(m => m.Name == "UpdateMesh" && m.GetParameters().Length == 2);
                if (update != null)
                {
                    object[] args = { Matrix4x4.identity, null };
                    update.Invoke(data, args);
                    outMaterials = args[1] as Material[];
                }
                generated = meshField?.GetValue(data) as Mesh;
            }
            // Without usable tree data, the mesh the kit saved with it (if there is one) does as well.
            if (generated == null || generated.vertexCount == 0)
                generated = parts.OfType<Mesh>().OrderByDescending(m => m.vertexCount).FirstOrDefault();
            if (generated == null || generated.vertexCount == 0)
            {
                report.Add($"{name}: no mesh and no usable tree data in the prefab (it holds: {inventory})");
                return null;
            }
            if (outMaterials == null)
            {
                var kitMaterials = parts.OfType<Material>().ToList();
                if (kitMaterials.Count >= generated.subMeshCount) outMaterials = kitMaterials.Take(generated.subMeshCount).ToArray();
            }

            if (!AssetDatabase.IsValidFolder(KitFolder)) AssetDatabase.CreateFolder("Assets/Art", "NatureKitURP");
            Mesh mesh = UnityEngine.Object.Instantiate(generated);
            mesh.name = name + "_URP";
            float k = height / Mathf.Max(mesh.bounds.max.y, 0.01f);
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] *= k;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, KitFolder + "/" + name + "_URP.asset");

            // Submeshes follow the materials: the optimized solid (bark) and cut-out (leaf) materials.
            var materials = new Material[mesh.subMeshCount];
            for (int s = 0; s < materials.Length; s++)
            {
                bool leaves = outMaterials != null && s < outMaterials.Length && outMaterials[s] != null
                    ? outMaterials[s].shader.name.Contains("Leaves") || outMaterials[s].name.Contains("Leaf")
                    : s == materials.Length - 1;
                materials[s] = KitMaterial(name, leaves, atlas);
            }
            FixLeafNormals(mesh, materials);
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(name + "_URP");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                var capsule = contents.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    var c = go.AddComponent<CapsuleCollider>();
                    c.center = capsule.center * k; c.radius = Mathf.Max(0.15f, capsule.radius * k); c.height = capsule.height * k; c.direction = capsule.direction;
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                int tris = 0; for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetIndexCount(s) / 3);
                report.Add($"{name}: converted ({tris:N0} triangles, {mesh.subMeshCount} parts, scaled to {height} m)");
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        catch (Exception e)
        {
            report.Add(name + ": conversion failed: " + (e.InnerException ?? e).Message);
            return null;
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    // Tree Creator bends its leaf normals for its own leaf shader; under a plain lit material half the crown then
    // faces away from the sun and turns black. Point every leaf vertex out of the crown instead (a little upwards):
    // the crown lights like one soft, round mass, the usual look for stylised foliage.
    private static void FixLeafNormals(Mesh mesh, Material[] materials)
    {
        var leafVertices = new HashSet<int>();
        for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            if (materials[s] != null && materials[s].name.EndsWith("_Leaves"))
                foreach (int v in mesh.GetTriangles(s)) leafVertices.Add(v);
        if (leafVertices.Count == 0) return;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != vertices.Length) normals = new Vector3[vertices.Length];
        Vector3 centre = Vector3.zero;
        foreach (int v in leafVertices) centre += vertices[v];
        centre /= leafVertices.Count;
        foreach (int v in leafVertices)
        {
            Vector3 outward = vertices[v] - centre;
            outward.y *= 0.8f;                                         // crowns are wider than tall
            normals[v] = (outward.normalized + Vector3.up * 0.35f).normalized;
        }
        mesh.normals = normals;
        mesh.RecalculateTangents();
    }

    // The kit's leaves are a dark olive meant for the Tree Creator leaf shader, which brightens them with translucency;
    // a plain lit material leaves them nearly black. Its own copy of the atlas has the green leaf texels repainted in
    // the farm's sunny greens, keeping their shading (the bark texels, browner than green, stay as they are).
    private static Texture2D KitLeafTexture(string name, Texture2D atlas)
    {
        string path = KitFolder + "/T_" + name + "_Leaves.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null)
        {
            var current = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!current.mipMapsPreserveCoverage) { SetLeafImport(current); current.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        int w = atlas.width, h = atlas.height;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(atlas, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Color[] px = copy.GetPixels();
        bool Leaf(Color c) => c.a > 0.3f && c.g > c.r * 1.05f;
        float sum = 0f; int count = 0;
        foreach (var c in px) if (Leaf(c)) { sum += c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; count++; }
        float mean = count > 0 ? sum / count : 0.2f;
        Color dark = new Color(0.22f, 0.40f, 0.11f), light = new Color(0.60f, 0.79f, 0.28f);
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            if (!Leaf(c)) continue;
            float t = Mathf.Clamp01((c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / mean * 0.55f);
            Color n = Color.Lerp(dark, light, t); n.a = c.a;
            px[i] = n;
        }
        copy.SetPixels(px);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(copy);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        SetLeafImport(importer);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Cut-out leaves thin away in the smaller mipmaps until only the dark branches are left at a distance; keeping
    // each mipmap's alpha coverage (at the material's cutoff) keeps the crowns full.
    private static void SetLeafImport(TextureImporter importer)
    {
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.3f;
        importer.wrapMode = TextureWrapMode.Clamp;
    }

    private static Material KitMaterial(string name, bool leaves, Texture2D atlas)
    {
        string path = KitFolder + "/M_" + name + (leaves ? "_Leaves" : "_Bark") + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", leaves ? KitLeafTexture(name, atlas) : atlas);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Surface", 0f);
        m.SetFloat("_WorkflowMode", 1f);
        m.SetFloat("_AlphaClip", leaves ? 1f : 0f);
        m.SetFloat("_Cutoff", 0.3f);
        m.SetFloat("_Cull", leaves ? 0f : 2f);
        m.enableInstancing = true;                 // terrain trees draw instanced
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- the map

    private sealed class GroundMap
    {
        public readonly Terrain terrain;
        private readonly TerrainData data;
        private readonly float[,,] alpha;
        private readonly bool[] grass, path;
        public readonly List<Vector2> pathPoints = new List<Vector2>();

        public GroundMap(Terrain terrain)
        {
            this.terrain = terrain;
            data = terrain.terrainData;
            alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            grass = data.terrainLayers.Select(l => l != null && IsGrass(l.name)).ToArray();
            path = data.terrainLayers.Select(l => l != null && IsPath(l.name)).ToArray();
            // Path texels every ~2 m, for "how far to the nearest path".
            int step = Mathf.Max(1, Mathf.RoundToInt(2f / (data.size.x / data.alphamapWidth)));
            for (int y = 0; y < data.alphamapHeight; y += step)
                for (int x = 0; x < data.alphamapWidth; x += step)
                {
                    float p = 0f; for (int l = 0; l < path.Length; l++) if (path[l]) p += alpha[y, x, l];
                    if (p > 0.45f) pathPoints.Add(new Vector2(Origin.x + (x + 0.5f) / data.alphamapWidth * data.size.x,
                                                               Origin.z + (y + 0.5f) / data.alphamapHeight * data.size.z));
                }
        }

        public Vector3 Origin => terrain.transform.position;
        public bool Inside(Vector2 p, float margin) =>
            p.x > Origin.x + margin && p.x < Origin.x + data.size.x - margin && p.y > Origin.z + margin && p.y < Origin.z + data.size.z - margin;

        public float Height(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + Origin.y;
        public Vector3 Ground(Vector2 p) => new Vector3(p.x, Height(p), p.y);

        private float Weight(Vector2 p, bool[] which)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - Origin.x) / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((p.y - Origin.z) / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float w = 0f; for (int l = 0; l < which.Length; l++) if (which[l]) w += alpha[y, x, l];
            return w;
        }
        public float Grass(Vector2 p) => Weight(p, grass);
        public float PathWeight(Vector2 p) => Weight(p, path);
        public float PathDistance(Vector2 p) => pathPoints.Count == 0 ? 999f : Mathf.Sqrt(pathPoints.Min(q => (q - p).sqrMagnitude));
        public IEnumerable<int> GrassLayers() => Enumerable.Range(0, grass.Length).Where(i => grass[i]);
        public string LayerName(int i) => data.terrainLayers[i].name;
    }

    private static Bounds FarmBounds(GameObject root)
    {
        Bounds? b = null;
        foreach (string name in new[] { "04 Fences and Gates", "07 Sheep Pasture", "03 Chicken Yard", "01 Barn Yard", "02 Crop Fields", "Farm Pond" })
        {
            Transform t = root.transform.Find(name);
            if (t == null) continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
        }
        return b ?? new Bounds(root.transform.position, new Vector3(80f, 10f, 80f));
    }

    private static Vector3 BarnPosition(GameObject root, Bounds farm)
    {
        foreach (var go in root.scene.GetRootGameObjects().Concat(root.GetComponentsInChildren<Transform>().Select(t => t.gameObject)))
            if (go.name.StartsWith("SM_Bld_Barn_01")) return go.transform.position;
        return farm.center;
    }

    // Something stands here that a tree would grow into: a collider other than the ground, or a tall renderer.
    private static bool Blocked(Vector3 foot, float radius, Transform ignore)
    {
        foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * (radius + 0.3f), foot + Vector3.up * 8f, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is TerrainCollider || c is CharacterController) continue;
            if (ignore != null && c.transform.IsChildOf(ignore)) continue;
            return true;
        }
        return false;
    }

    private static List<Bounds> tallRenderers;
    private static bool NearTallObject(Vector2 p, float radius)
    {
        foreach (var b in tallRenderers)
            if (p.x > b.min.x - radius && p.x < b.max.x + radius && p.y > b.min.z - radius && p.y < b.max.z + radius) return true;
        return false;
    }

    private static void CollectTallRenderers(Transform ignore)
    {
        tallRenderers = new List<Bounds>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || (ignore != null && r.transform.IsChildOf(ignore))) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.2f || b.size.x > 40f || b.size.z > 40f) continue;   // ground cover, or the far scenery
            tallRenderers.Add(b);
        }
    }

    // ---------------------------------------------------------------- orchard and oaks

    private static Rect PlantOrchard(Transform group, GroundMap map, Bounds farm, Vector3 barn, Transform root, List<string> report)
    {
        CollectTallRenderers(group);
        var prefabs = OrchardRows.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(FruitPrefabs + n + ".prefab")).ToArray();
        if (prefabs.Any(p => p == null)) { report.Add("Orchard skipped: fruit tree prefabs missing (run Tools/Chick/Trees/Place Fruit Tree Samples once)."); return Rect.zero; }
        var scenery = SceneryItems(root);
        var reasons = new List<string>();
        foreach (int perRow in new[] { 4, 3 })
        {
            if (!FindOrchard(map, farm, barn, group, scenery, perRow, out Rect best, out bool turned, out List<Transform> clear, out string why)) { reasons.Add(why); continue; }
            // Make room: the map's scattered scenery (trees, bushes, rocks) standing in the orchard is hidden, not deleted,
            // and comes back when the vegetation is removed again.
            var record = group.GetComponent<MapSectionRecord>();
            if (record == null) record = Undo.AddComponent<MapSectionRecord>(group.gameObject);
            foreach (var item in clear) { Undo.RecordObject(item.gameObject, "Plant vegetation"); item.gameObject.SetActive(false); }
            record.hidden = record.hidden.Concat(clear.Select(t => t.gameObject)).ToArray();
            EditorUtility.SetDirty(record);
            Physics.SyncTransforms();

            var orchard = new GameObject("Orchard");
            Undo.RegisterCreatedObjectUndo(orchard, "Plant vegetation");
            orchard.transform.SetParent(group, false);
            var random = new System.Random(11);
            for (int r = 0; r < OrchardRows.Length; r++)
                for (int i = 0; i < perRow; i++)
                {
                    Vector2 p = Spot(best, turned, r, i) + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.6f;
                    var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[r], orchard.transform);
                    tree.transform.position = map.Ground(p) - Vector3.up * 0.05f;
                    tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    tree.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.05f, (float)random.NextDouble());
                }
            report.Add("Orchard search: " + why);
            report.Add($"Orchard: {OrchardRows.Length} rows ({string.Join(", ", OrchardRows)}) of {perRow} at x {best.xMin:F0}..{best.xMax:F0}, z {best.yMin:F0}..{best.yMax:F0}, " +
                       $"{Vector2.Distance(best.center, new Vector2(barn.x, barn.z)):F0} m from the barn; hidden to make room: " +
                       (clear.Count == 0 ? "nothing" : string.Join(", ", clear.Select(t => t.name))));
            return new Rect(best.xMin - OrchardMargin, best.yMin - OrchardMargin, best.width + 2 * OrchardMargin, best.height + 2 * OrchardMargin);
        }
        report.Add("Orchard skipped: " + string.Join("; ", reasons));
        return Rect.zero;
    }

    // The map's scattered scenery: single trees, bushes, rocks, stumps, flowers and mushrooms (from "05 Nature and
    // Scenery", "09 Small Stones" and the natural part of "10 Farm Dressing"), with their bounds. Only these may be
    // cleared for the orchard.
    private static List<(Transform item, Bounds bounds)> SceneryItems(Transform root)
    {
        var items = new List<(Transform, Bounds)>();
        // A child that spreads over a big area or holds several pieces is a group (e.g. "Hill Expansion Scenery"):
        // look inside it, so only single trees, bushes and rocks are ever cleared.
        void Collect(Transform parent)
        {
            foreach (Transform item in parent)
            {
                if (!item.gameObject.activeInHierarchy) continue;
                var renderers = item.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                bool group = item.childCount > 0 && (b.size.x > 20f || b.size.z > 20f || item.childCount > 6);
                if (group) Collect(item);
                else items.Add((item, b));
            }
        }
        // The natural dressing only; the farm dressing's buildings, machines, hay, apiary and stands are never touched.
        foreach (string name in new[] { "05 Nature and Scenery", "09 Small Stones", "10 Farm Dressing/Trees",
                                         "10 Farm Dressing/Stumps and Logs", "10 Farm Dressing/Rocks", "10 Farm Dressing/Undergrowth",
                                         "10 Farm Dressing/Flowers", "10 Farm Dressing/Mushrooms" })
        {
            Transform group = root.Find(name);
            if (group != null) Collect(group);
        }
        return items;
    }

    private static string FullName(Transform t) => t.parent == null ? t.name : FullName(t.parent) + "/" + t.name;

    private static bool IsScenery(Transform t, List<(Transform item, Bounds bounds)> scenery)
    {
        foreach (var s in scenery) if (t == s.item || t.IsChildOf(s.item)) return true;
        return false;
    }

    // The open, gently sloping, grassy spot nearest the barn (outside the farm) that needs the fewest scenery items
    // cleared; nothing else (fences, buildings, props) may be in the way. Rows run east-west or north-south.
    private static bool FindOrchard(GroundMap map, Bounds farm, Vector3 barn, Transform group, List<(Transform item, Bounds bounds)> scenery,
                                    int perRow, out Rect best, out bool bestTurned, out List<Transform> bestClear, out string why)
    {
        float width = (perRow - 1) * TreeSpacing, depth = (OrchardRows.Length - 1) * RowSpacing;
        var farmRect = new Rect(farm.min.x, farm.min.z, farm.size.x, farm.size.z);
        // Tall things that are not part of the scenery: these can never be moved.
        var fixedTall = tallRenderers.Where(b => !scenery.Any(s => s.bounds.Contains(b.center))).ToList();
        float bestScore = float.MaxValue; best = Rect.zero; bestTurned = false; bestClear = new List<Transform>();
        int tried = 0, far = 0, farmOrEdge = 0, path = 0, slope = 0, grass = 0, blocked = 0, crowded = 0;
        var near = new List<(float distance, string reason)>();
        void Near(Rect r, string reason) { float dd = Vector2.Distance(r.center, new Vector2(barn.x, barn.z)); if (dd < 45f) near.Add((dd, reason)); }
        Vector2 barnXZ = new Vector2(barn.x, barn.z);
        for (int turned = 0; turned < 2; turned++)
        {
            float w = turned == 0 ? width : depth, d = turned == 0 ? depth : width;
            for (float x = farm.min.x - 70f; x <= farm.max.x + 70f; x += 2f)
                for (float z = farm.min.z - 60f; z <= farm.max.z + 60f; z += 2f)
                {
                    tried++;
                    var rect = new Rect(x, z, w, d);
                    if (Vector2.Distance(rect.center, barnXZ) > 90f) { far++; continue; }
                    var outer = new Rect(x - OrchardMargin, z - OrchardMargin, w + 2 * OrchardMargin, d + 2 * OrchardMargin);
                    if (outer.Overlaps(farmRect) || !map.Inside(outer.min, EdgeMargin) || !map.Inside(outer.max, EdgeMargin)) { farmOrEdge++; continue; }
                    string blocker = null;
                    float low = float.MaxValue, high = float.MinValue, worstPath = 0f;
                    for (float sx = outer.xMin; sx <= outer.xMax; sx += 2f)
                        for (float sz = outer.yMin; sz <= outer.yMax; sz += 2f)
                        {
                            var p = new Vector2(sx, sz);
                            float h = map.Height(p); low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                            worstPath = Mathf.Max(worstPath, map.PathWeight(p));
                        }
                    if (worstPath > 0.35f) { path++; Near(rect, $"path {worstPath:F2}"); continue; }
                    if (high - low > 4f) { slope++; Near(rect, $"slope {high - low:F1} m"); continue; }
                    bool ok = true, grassy = true;
                    for (int r = 0; r < OrchardRows.Length && ok; r++)
                        for (int i = 0; i < perRow && ok; i++)
                        {
                            Vector2 p = Spot(rect, turned == 1, r, i);
                            if (map.Grass(p) < 0.25f) { ok = false; grassy = false; break; }
                            foreach (var b in fixedTall)
                                if (p.x > b.min.x - 2f && p.x < b.max.x + 2f && p.y > b.min.z - 2f && p.y < b.max.z + 2f) { ok = false; blocker = "tall object at " + b.center.ToString("F0"); break; }
                            if (!ok) break;
                            Vector3 foot = map.Ground(p);
                            foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * 2.3f, foot + Vector3.up * 8f, 2f, ~0, QueryTriggerInteraction.Ignore))
                                if (!(c is TerrainCollider) && !(c is CharacterController) && !c.transform.IsChildOf(group) && !IsScenery(c.transform, scenery)) { ok = false; blocker = "collider " + FullName(c.transform); break; }
                        }
                    if (!ok) { if (grassy) blocked++; else grass++; Near(rect, grassy ? blocker : "not grass"); continue; }
                    // Only tall scenery (trees, big rocks) standing where an orchard tree's trunk or crown goes has to
                    // go; flowers, mushrooms and undergrowth stay and grow under the orchard trees.
                    var clear = new List<Transform>();
                    foreach (var s in scenery)
                    {
                        if (s.bounds.size.y < 1.5f) continue;
                        for (int r = 0; r < OrchardRows.Length && !clear.Contains(s.item); r++)
                            for (int i = 0; i < perRow; i++)
                            {
                                Vector2 p = Spot(rect, turned == 1, r, i);
                                if (p.x > s.bounds.min.x - 3f && p.x < s.bounds.max.x + 3f && p.y > s.bounds.min.z - 3f && p.y < s.bounds.max.z + 3f) { clear.Add(s.item); break; }
                            }
                    }
                    if (clear.Count > 12) { crowded++; Near(rect, $"{clear.Count} scenery items"); continue; }
                    float score = Vector2.Distance(rect.center, barnXZ) + clear.Count * 6f + (high - low) * 3f;
                    if (score < bestScore) { bestScore = score; best = rect; bestTurned = turned == 1; bestClear = clear; }
                }
        }
        string nearest = string.Join(" | ", near.OrderBy(n => n.distance).Select(n => $"{n.distance:F0} m: {n.reason}").Distinct().Take(12));
        why = $"{perRow} per row (nearest to the barn turned down: {nearest}): of {tried} spots {far} more than 90 m from the barn, {farmOrEdge} overlap the farm or the map edge, {path} cross a path, {slope} too steep, {grass} not grass, {blocked} have fences, buildings or props in the way, {crowded} would clear too much scenery";
        return bestScore < float.MaxValue;
    }

    private static Vector2 Spot(Rect rect, bool turned, int row, int index) => turned
        ? new Vector2(rect.xMin + row * RowSpacing, rect.yMin + index * TreeSpacing)
        : new Vector2(rect.xMin + index * TreeSpacing, rect.yMin + row * RowSpacing);

    private static List<Vector3> PlantOaks(Transform group, GroundMap map, Bounds farm, Rect orchard, List<string> report)
    {
        var placed = new List<Vector3>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FruitPrefabs + "OakTree.prefab");
        if (prefab == null) { report.Add("Oaks skipped: OakTree prefab missing (run Tools/Chick/Trees/Place Oak Tree Next To Fruit Trees once)."); return placed; }
        CollectTallRenderers(group);
        var farmRect = new Rect(farm.min.x, farm.min.z, farm.size.x, farm.size.z);
        var candidates = new List<(float score, Vector2 p)>();
        for (float x = map.Origin.x + 8f; x < map.Origin.x + map.terrain.terrainData.size.x - 8f; x += 3f)
            for (float z = map.Origin.z + 8f; z < map.Origin.z + map.terrain.terrainData.size.z - 8f; z += 3f)
            {
                var p = new Vector2(x, z);
                float farmGap = Gap(farmRect, p);
                if (farmGap < 6f || farmGap > 60f || Gap(orchard, p) < 10f || !map.Inside(p, EdgeMargin)) continue;
                if (map.Grass(p) < 0.4f || map.PathWeight(p) > 0.1f) continue;
                float road = map.PathDistance(p);
                if (road < 4f || road > 18f) continue;            // a roadside shade tree, not on the road
                candidates.Add((Mathf.Abs(road - 8f) + farmGap * 0.1f, p));
            }
        var random = new System.Random(23);
        foreach (var c in candidates.OrderBy(c => c.score))
        {
            if (placed.Count >= OakCount) break;
            if (placed.Any(q => Vector2.Distance(new Vector2(q.x, q.z), c.p) < 25f)) continue;
            Vector3 foot = map.Ground(c.p);
            if (NearTallObject(c.p, 3.5f) || Blocked(foot, 3.5f, group)) continue;
            var oak = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            oak.name = "Oak " + (placed.Count + 1);
            oak.transform.position = foot - Vector3.up * 0.05f;
            oak.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            oak.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble());
            placed.Add(foot);
        }
        report.Add($"Oaks: {placed.Count} by the roads at " + string.Join("; ", placed.Select(p => $"{p.x:F0}, {p.z:F0}")));
        return placed;
    }

    private static float Gap(Rect r, Vector2 p)
    {
        if (r.width <= 0f) return 999f;
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------------------------------------------------------------- spawner

    private static Collider[] BuildZones(GameObject root, Transform group, Bounds farm, Rect orchard, List<Vector3> oaks, List<string> report)
    {
        var zones = new GameObject(ZonesName);
        Undo.RegisterCreatedObjectUndo(zones, "Plant vegetation");
        zones.transform.SetParent(root.transform, false);
        var list = new List<Collider>();
        void Box(string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(zones.transform, false);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            go.SetActive(false);                          // the spawner switches it on only while it builds its collision cache
            list.Add(go.GetComponent<BoxCollider>());
        }
        float y = farm.center.y;
        Box("Farm", new Vector3(farm.center.x, y, farm.center.z), new Vector3(farm.size.x + 8f, 40f, farm.size.z + 8f));
        if (orchard.width > 0f) Box("Orchard", new Vector3(orchard.center.x, y, orchard.center.y), new Vector3(orchard.width + 4f, 40f, orchard.height + 4f));
        foreach (var o in oaks) Box("Oak", o, new Vector3(9f, 40f, 9f));
        // Tall things without a collider of their own (most trees and bushes already on the map): keep the new trees off them.
        CollectTallRenderers(group);
        int tall = 0;
        foreach (var b in tallRenderers)
        {
            if (b.size.y < 2f) continue;
            bool hasCollider = Physics.OverlapBox(b.center, b.extents * 0.6f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider));
            if (hasCollider) continue;
            Box("Tall object", new Vector3(b.center.x, b.center.y, b.center.z), new Vector3(Mathf.Max(b.size.x, 2f) + 1f, 40f, Mathf.Max(b.size.z, 2f) + 1f));
            tall++;
        }
        report.Add($"Exclusion zones: farm, orchard, {oaks.Count} oaks and {tall} tall objects without colliders");
        return list.ToArray();
    }

    private static void Spawn(GameObject root, Terrain terrain, List<GameObject> trees, List<GameObject> bushes, Collider[] zones, List<string> report)
    {
        var go = new GameObject(SpawnerName);
        Undo.RegisterCreatedObjectUndo(go, "Plant vegetation");
        go.transform.SetParent(root.transform, false);
        var spawner = go.AddComponent<VegetationSpawner>();
        spawner.autoRespawnTrees = false;
        spawner.terrains = new List<Terrain> { terrain };
        spawner.seed = 2026;
        spawner.tempColliders = zones;
        spawner.cellSize = 32;
        spawner.cellDivisions = 16;                       // 2 m collision cells
        spawner.highPrecisionCollision = true;
        spawner.collisionLayerMask = ~0;
        var map = new GroundMap(terrain);
        List<SpawnerBase.TerrainLayerMask> Grass() =>
            map.GrassLayers().Select(i => new SpawnerBase.TerrainLayerMask(map.LayerName(i), i, 0.3f)).ToList();
        spawner.treeTypes = new List<SpawnerBase.TreeType>();
        if (trees.Count > 0)
            spawner.treeTypes.Add(new SpawnerBase.TreeType
            {
                name = "Nature Kit Trees", seed = 101, probability = 55f, collisionCheck = true, distance = 14f,
                slopeRange = new Vector2(0f, 25f), scaleRange = new Vector2(0.8f, 1.15f), sinkAmount = 0.1f,
                layerMasks = Grass(), prefabs = trees.Select(p => new SpawnerBase.TreePrefab { prefab = p, probability = 100f }).ToList(),
            });
        if (bushes.Count > 0)
            spawner.treeTypes.Add(new SpawnerBase.TreeType
            {
                name = "Nature Kit Bushes", seed = 202, probability = 35f, collisionCheck = true, distance = 7f,
                slopeRange = new Vector2(0f, 30f), scaleRange = new Vector2(0.75f, 1.25f), sinkAmount = 0.05f,
                layerMasks = Grass(), prefabs = bushes.Select(p => new SpawnerBase.TreePrefab { prefab = p, probability = 100f }).ToList(),
            });
        Physics.SyncTransforms();
        spawner.RebuildCollisionCache();
        spawner.Respawn(grass: false, trees: true);
        foreach (var t in spawner.treeTypes) report.Add($"Vegetation Spawner: {t.instanceCount} {t.name}");
        EditorUtility.SetDirty(spawner);
    }

    // ---------------------------------------------------------------- review

    private static void RenderReview(Terrain terrain, Bounds farm, Rect orchard, List<Vector3> oaks)
    {
        Directory.CreateDirectory(ReviewFolder);
        var camera = MapSurvey.TempCamera();
        try
        {
            Vector3 tp = terrain.transform.position, size = terrain.terrainData.size;
            camera.orthographic = true;
            float half = Mathf.Max(size.x, size.z) / 2f;
            camera.orthographicSize = half;
            Vector3 centre = tp + new Vector3(size.x / 2f, 0f, size.z / 2f);
            camera.transform.position = centre + Vector3.up * (size.y + 200f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 1f; camera.farClipPlane = size.y + 600f;
            Save(camera, 2048, 2048, "after_topdown.png");

            camera.orthographic = false;
            camera.fieldOfView = 55f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 1500f;
            camera.clearFlags = CameraClearFlags.Skybox;
            void Look(Vector3 target, Vector3 from, string file)
            {
                camera.transform.position = from;
                camera.transform.rotation = Quaternion.LookRotation(target - from);
                Save(camera, 1600, 900, file);
            }
            float Ground(Vector3 p) => terrain.SampleHeight(p) + tp.y;
            if (orchard.width > 0f)
            {
                var o = new Vector3(orchard.center.x, 0f, orchard.center.y); o.y = Ground(o);
                Look(o + Vector3.up * 2f, o + new Vector3(-orchard.width, 6f, -orchard.height * 1.2f), "after_orchard.png");
            }
            if (oaks.Count > 0) Look(oaks[0] + Vector3.up * 4f, oaks[0] + new Vector3(14f, 3f, -12f), "after_oak.png");
            // From the farm towards the countryside, at chick-ish height and from above.
            var f = farm.center; f.y = Ground(f);
            Look(f + new Vector3(60f, 0f, 30f), f + new Vector3(0f, 3f, 0f), "after_from_farm.png");
            Look(f, f + new Vector3(-90f, 60f, -90f), "after_overview.png");
        }
        finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
    }

    private static void Save(Camera camera, int w, int h, string file)
    {
        var tex = MapSurvey.Render(camera, w, h);
        File.WriteAllBytes(Path.Combine(ReviewFolder, file), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
