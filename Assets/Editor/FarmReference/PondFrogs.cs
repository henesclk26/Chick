using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Two frogs on the farm pond's lily pads ("Frog" by johnrife48, Sketchfab, CC-BY-4.0; Assets/Art/Frog, made by
/// ArtSource/Frog/build_frog.py).
/// - Import: no embedded materials; the frog gets M_Frog (URP Lit with its base colour and normal map).
/// - Menu: every lily pad (WaterLily_Lief) on the pond gets a mesh collider in its own shape, so the chick can stand on
///   it (the flowers get none). The two biggest pads without a flower on them, well apart, get a frog each, sized to
///   its pad: the "Pond Frog" prefab, a FrogIdle (breathes, shuffles, watches the chick) with a small collider.
///   Running it again replaces the frogs.
/// </summary>
public sealed class PondFrogs : AssetPostprocessor
{
    private const string Folder = "Assets/Art/Frog/";
    private const string ModelPath = Folder + "Frog.fbx";
    private const string MaterialPath = Folder + "M_Frog.mat";
    private const string PrefabPath = Folder + "PondFrog.prefab";
    private const string FrogsName = "Frogs";
    private const float FrogLength = 0.19f;             // the model's length, nose to tail
    private const int FrogCount = 2;

    public override uint GetVersion() => 1;

    private void OnPreprocessModel()
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.globalScale = 1f;
        importer.addCollider = false;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        context.DependsOnArtifact(MaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { context.LogImportWarning("M_Frog missing; run Tools/Chick/Pond/Place Frogs On Lily Pads."); return; }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder + "T_Frog_", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        bool normal = assetPath.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = assetPath.EndsWith("_BaseColor.png", StringComparison.OrdinalIgnoreCase);
        importer.maxTextureSize = 1024;                 // a 19 cm frog
    }

    [MenuItem("Tools/Chick/Pond/Place Frogs On Lily Pads")]
    private static void Place()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Frogs", "Exit play mode first.", "OK"); return; }
        var root = GameObject.Find("Farm Reference Map");
        Transform pond = SceneLookup.FindAnywhere(root, "Farm Pond");
        Transform waterT = pond != null ? pond.Find("Water Surface") : null;
        Renderer water = waterT != null ? waterT.GetComponent<Renderer>() : null;
        if (root == null || water == null || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            EditorUtility.DisplayDialog("Frogs", root == null ? "The open scene has no 'Farm Reference Map'."
                : water == null ? "The farm pond's 'Water Surface' was not found." : ModelPath + " is missing.", "OK");
            return;
        }
        Bounds pondBounds = water.bounds;
        pondBounds.Expand(new Vector3(1f, 4f, 1f));
        bool OnPond(Bounds b) => b.center.x > pondBounds.min.x && b.center.x < pondBounds.max.x &&
                                 b.center.z > pondBounds.min.z && b.center.z < pondBounds.max.z;

        EnsureMaterial();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        GameObject prefab = EnsurePrefab();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Place pond frogs");
        int group = Undo.GetCurrentGroup();

        // Lily pads and flowers on the pond.
        var pads = new List<MeshFilter>();
        var flowers = new List<Bounds>();
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (renderer == null || filter.sharedMesh == null || !OnPond(renderer.bounds)) continue;
            // The model sits a few levels down (WaterLily_Lief / offset / Water_Lily): look at its ancestors' names.
            string name = "";
            for (Transform t = filter.transform; t != null && t != pond.parent; t = t.parent) name += " " + t.name;
            if (name.Contains("Flower")) flowers.Add(renderer.bounds);
            else if (name.Contains("WaterLily_Lief") || name.Contains("Lily_Leaf")) pads.Add(filter);
        }
        if (pads.Count == 0) { EditorUtility.DisplayDialog("Frogs", "No lily pads (WaterLily_Lief) found on the pond.", "OK"); return; }
        int added = 0;
        foreach (var pad in pads)
        {
            if (pad.GetComponent<Collider>() != null) continue;
            var collider = Undo.AddComponent<MeshCollider>(pad.gameObject);
            collider.sharedMesh = pad.sharedMesh;
            added++;
        }
        Physics.SyncTransforms();

        // The biggest flower-free pads, well apart.
        Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        var free = pads.Select(p => p.GetComponent<Renderer>().bounds)
            .Where(b => !flowers.Any(f => Vector2.Distance(XZ(f.center), XZ(b.center)) < Mathf.Min(b.extents.x, b.extents.z)))
            .OrderByDescending(b => b.size.x * b.size.z).ToList();
        if (free.Count == 0) free = pads.Select(p => p.GetComponent<Renderer>().bounds).OrderByDescending(b => b.size.x * b.size.z).ToList();
        var chosen = new List<Bounds>();
        foreach (var b in free)
        {
            if (chosen.Count >= FrogCount) break;
            if (chosen.Any(c => Vector2.Distance(XZ(c.center), XZ(b.center)) < 1f)) continue;
            chosen.Add(b);
        }

        Transform old = pond.Find(FrogsName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var frogs = new GameObject(FrogsName);
        Undo.RegisterCreatedObjectUndo(frogs, "Place pond frogs");
        frogs.transform.SetParent(pond, false);
        var random = new System.Random(5);
        var report = new List<string>();
        foreach (var b in chosen)
        {
            Vector3 spot = b.center;
            spot.y = b.max.y;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) spot.y = hit.point.y;
            float pad = Mathf.Min(b.size.x, b.size.z);
            float scale = Mathf.Clamp(pad * 0.7f / FrogLength, 0.6f, 1.3f);
            var frog = (GameObject)PrefabUtility.InstantiatePrefab(prefab, frogs.transform);
            frog.name = "Pond Frog " + (report.Count + 1);
            frog.transform.position = spot + Vector3.up * 0.003f;
            frog.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            frog.transform.localScale = Vector3.one * scale;
            report.Add($"{frog.name} on a {pad * 100f:F0} cm pad at {spot.x:F1}, {spot.z:F1} (scale {scale:F2})");
        }
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = frogs;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(water.bounds.center, water.bounds.size * 0.6f), false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"Pond: {pads.Count} lily pads ({added} got a mesh collider, so the chick can stand on them); " +
                  string.Join("; ", report) + ". Save the scene to keep it (Ctrl+Z undoes it). " +
                  "Credit: \"Frog\" by johnrife48 (Sketchfab), CC-BY-4.0.", frogs);
    }

    private static void EnsureMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Frog" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "T_Frog_BaseColor.png"));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "T_Frog_Normal.png"));
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Smoothness", 0.55f);        // wet skin, but not the model's mirror shine
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", 2f);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    // Root: FrogIdle and a small sphere collider (the chick bumps into the frog rather than through it); the model
    // is the "Visual" child that breathes and hops.
    private static GameObject EnsurePrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var root = new GameObject("Pond Frog");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            root.AddComponent<FrogIdle>();
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.07f;
            sphere.center = new Vector3(0f, 0.07f, 0f);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
