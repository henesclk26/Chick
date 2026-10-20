using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// An apple, a sour cherry, an orange and a pear tree round the farm pond, whose fruit falls and can be eaten (FruitTree); apples
/// left lying get a caterpillar ("10_PinkCaterpillar" from Low Poly Cartoon Bugs (Free Sample) by POLYGROVE Studio,
/// CC BY 4.0; Assets/Art/Caterpillar, made by ArtSource/Caterpillar/build_caterpillar.py).
/// - Import: the caterpillar gets M_Caterpillar (URP Lit with the bugs' palette).
/// - Menu: makes the caterpillar prefab Resources/Bugs/AppleCaterpillar (FruitTree loads it by name); clears the
///   chunky low-poly grass tufts (BOKI / polygonTrees grass) round the pond; plants the three trees on open grass a
///   few metres from the water, spread round it and clear of paths, fences and props, in "Farm Pond/Pond Fruit Trees".
///   Running it again replaces the trees. Ctrl+Z undoes it.
/// </summary>
public sealed class PondFruitTrees : AssetPostprocessor
{
    private const string Folder = "Assets/Art/Caterpillar/";
    private const string ModelPath = Folder + "Caterpillar.fbx";
    private const string MaterialPath = Folder + "M_Caterpillar.mat";
    private const string PaletteTexture = Folder + "T_BugsPalette.png";
    private const string PrefabFolder = "Assets/Resources/Bugs";
    private const string PrefabPath = PrefabFolder + "/AppleCaterpillar.prefab";
    private const string TreesName = "Pond Fruit Trees";
    private static readonly string[] Trees = { "AppleTree", "CherryTree", "OrangeTree", "PearTree" };
    private const float MinGap = 3f, MaxGap = 7f;      // from the water's edge to a trunk
    private const float ClearRadius = 1.4f;            // nothing else this close to a trunk
    private const float TreeSpacing = 9f;

    public override uint GetVersion() => 1;

    private void OnPreprocessModel()
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.globalScale = 1f;
        importer.addCollider = false;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        context.DependsOnArtifact(MaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { context.LogImportWarning("M_Caterpillar missing; run Tools/Chick/Pond/Plant Fruit Trees By The Pond."); return; }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
    }

    [MenuItem("Tools/Chick/Pond/Plant Fruit Trees By The Pond")]
    private static void Plant()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Pond trees", "Exit play mode first.", "OK"); return; }
        var root = GameObject.Find("Farm Reference Map");
        Transform pond = SceneLookup.FindAnywhere(root, "Farm Pond");
        Renderer water = pond != null ? pond.Find("Water Surface")?.GetComponent<Renderer>() : null;
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (root == null || water == null || terrain == null)
        {
            EditorUtility.DisplayDialog("Pond trees", root == null ? "The open scene has no 'Farm Reference Map'." : "The farm pond or the terrain was not found.", "OK");
            return;
        }
        var report = new List<string>();

        // Caterpillar: material, model, prefab in Resources.
        EnsureCaterpillar(report);
        // Fruit trees: materials, models (now with FruitTree and loose fruit) and prefabs.
        FruitTreeSamples.EnsureMaterials();
        var prefabs = new GameObject[Trees.Length];
        for (int i = 0; i < Trees.Length; i++)
        {
            AssetDatabase.ImportAsset("Assets/Art/FruitTrees/" + Trees[i] + ".fbx", ImportAssetOptions.ForceUpdate);
            prefabs[i] = FruitTreeSamples.EnsurePrefab(Trees[i]);
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Plant fruit trees by the pond");
        int group = Undo.GetCurrentGroup();

        Bounds wb = water.bounds;
        Vector3 centre = wb.center;
        float area = Mathf.Max(wb.extents.x, wb.extents.z) + MaxGap + 5f;

        // The chunky low-poly grass tufts round the pond go.
        int removed = 0;
        var tufts = new HashSet<GameObject>();
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (filter.sharedMesh == null) continue;
            Vector3 p = filter.transform.position;
            if (new Vector2(p.x - centre.x, p.z - centre.z).magnitude > area) continue;
            string source = (AssetDatabase.GetAssetPath(filter.sharedMesh) + " " +
                             PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(filter.gameObject)).ToLowerInvariant();
            bool lowPolyGrass = (source.Contains("boki") || source.Contains("polygontrees")) && source.Contains("grass");
            if (!lowPolyGrass) continue;
            GameObject item = PrefabUtility.IsPartOfPrefabInstance(filter.gameObject)
                ? PrefabUtility.GetOutermostPrefabInstanceRoot(filter.gameObject) : filter.gameObject;
            if (item.GetComponentsInChildren<Renderer>(true).Length > 4) continue;   // not a whole group of things
            tufts.Add(item);
        }
        foreach (var tuft in tufts) { Undo.DestroyObjectImmediate(tuft); removed++; }
        report.Add($"removed {removed} low-poly grass tufts round the pond");
        Physics.SyncTransforms();

        // Three trunks spread round the water, on open grass a few metres from its edge.
        Transform old = pond.Find(TreesName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Physics.SyncTransforms();
        var candidates = new List<(float score, Vector3 foot, float angle)>();
        for (int a = 0; a < 360; a += 6)
            for (float gap = MinGap; gap <= MaxGap; gap += 1f)
            {
                float rad = a * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                // Distance from the middle to the water's edge that way (the pond as an ellipse).
                float edge = 1f / Mathf.Sqrt(Mathf.Pow(dir.x / Mathf.Max(wb.extents.x, .1f), 2f) + Mathf.Pow(dir.z / Mathf.Max(wb.extents.z, .1f), 2f));
                Vector3 p = centre + dir * (edge + gap);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (p.y < wb.center.y - .1f) continue;                   // still in the pond's hollow
                if (PathWeight(terrain, p) > .25f || Blocked(p, pond)) continue;
                candidates.Add((gap, p, a));
            }
        var chosen = new List<Vector3>();
        foreach (var c in candidates.OrderBy(c => c.score))
        {
            if (chosen.Count >= Trees.Length) break;
            if (chosen.Any(q => Vector3.Distance(q, c.foot) < TreeSpacing)) continue;
            chosen.Add(c.foot);
        }
        if (chosen.Count == 0)
        {
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorUtility.DisplayDialog("Pond trees", "No open grass round the pond for the trees (paths, fences or props everywhere). " + string.Join("; ", report), "OK");
            return;
        }
        var trees = new GameObject(TreesName);
        Undo.RegisterCreatedObjectUndo(trees, "Plant fruit trees by the pond");
        trees.transform.SetParent(pond, false);
        var random = new System.Random(19);
        for (int i = 0; i < chosen.Count; i++)
        {
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], trees.transform);
            tree.transform.position = chosen[i] - Vector3.up * .05f;
            tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * Mathf.Lerp(.9f, 1.05f, (float)random.NextDouble());
            report.Add($"{Trees[i]} at {chosen[i].x:F1}, {chosen[i].z:F1}");
        }
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = trees;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(centre + Vector3.up * 3f, Vector3.one * area * 1.6f), false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Pond fruit trees: " + string.Join("; ", report) + ". Save the scene to keep it (Ctrl+Z undoes it). " +
                  "Credit: \"Low Poly Cartoon Bugs (Free Sample)\" by POLYGROVE Studio (CC BY 4.0).", trees);
    }

    // Weight of the path, field and trodden-earth layers under a point.
    private static float PathWeight(Terrain terrain, Vector3 p)
    {
        TerrainData data = terrain.terrainData;
        Vector3 local = p - terrain.transform.position;
        int x = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
        float[,,] a = data.GetAlphamaps(x, z, 1, 1);
        float w = 0f;
        for (int l = 0; l < data.alphamapLayers; l++)
        {
            string n = data.terrainLayers[l] != null ? data.terrainLayers[l].name : "";
            if (n.Contains("Trodden") || n.Contains("Path") || n.Contains("Tilled")) w += a[0, 0, l];
        }
        return w;
    }

    // Fences, props, rocks or other trees round a trunk; the pond's own plants (reeds, lilies) do not count.
    private static bool Blocked(Vector3 foot, Transform pond)
    {
        foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * (ClearRadius + .2f), foot + Vector3.up * 5f, ClearRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is TerrainCollider || c is CharacterController || c.transform.IsChildOf(pond)) continue;
            return true;
        }
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.transform.IsChildOf(pond)) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.2f || b.size.x > 30f || b.size.z > 30f) continue;
            if (foot.x > b.min.x - ClearRadius && foot.x < b.max.x + ClearRadius && foot.z > b.min.z - ClearRadius && foot.z < b.max.z + ClearRadius)
                return true;
        }
        return false;
    }

    private static void EnsureCaterpillar(List<string> report)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Caterpillar" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteTexture));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", .35f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", 2f);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Resources", "Bugs");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var go = new GameObject("Apple Caterpillar");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
            visual.name = "Visual";
            go.AddComponent<AppleCaterpillar>();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            report.Add("caterpillar prefab " + PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
