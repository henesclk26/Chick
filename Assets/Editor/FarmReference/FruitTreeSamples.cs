using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The apple, orange and cherry trees (Assets/Art/FruitTrees, made by ArtSource/FruitTrees/build_fruit_trees.py from
/// the Lux Art Studios trees and the PolyOne Free Fruits).
/// - Import: no embedded materials; the tree mesh gets the trunk material and its own leaf material (in that submesh
///   order), the <Tree>_Fruits child the Free Fruits material. Leaf textures are a 2x2 atlas of tones, cut out by alpha.
/// - Menu: creates the materials and prefabs (a capsule collider round the trunk, nothing on the crown) and puts the
///   three trees side by side as a sample on open ground near the player chick, in a "Fruit Tree Samples" group that
///   running it again replaces. The fruits are only looks for now: they are not edible.
/// </summary>
public sealed class FruitTreeSamples : AssetPostprocessor
{
    private const string Folder = "Assets/Art/FruitTrees/";
    private const string TextureFolder = Folder + "Textures/";
    private const string PrefabFolder = Folder + "Prefabs/";
    private static readonly string[] Trees = { "AppleTree", "OrangeTree", "CherryTree" };
    // "Stylized Tree" by yonimantz (Sketchfab, CC-BY-4.0), made to match by ArtSource/FruitTrees/build_oak_tree.py.
    private const string Oak = "OakTree";
    private const string OakTrunkMaterialPath = Folder + "M_OakTree_Trunk.mat";
    private static readonly Color OakBark = new Color(0.50f, 0.31f, 0.22f);
    private const string TrunkMaterialPath = Folder + "M_FruitTree_Trunk.mat";
    private const string TrunkTexturePath = TextureFolder + "T_FruitTree_Trunk.png";
    private const string FruitMaterialPath = "Assets/PolyOne/Free Fruits/Materials/Free Fruits_M.mat";
    private const string SamplesName = "Fruit Tree Samples";
    private const float Spacing = 10f;              // between the sample trees' trunks
    private const float ClearRadius = 2.5f;         // nothing else may stand this close to a trunk

    private static string LeafMaterialPath(string tree) => Folder + "M_" + tree + "_Leaves.mat";
    private static string LeafTexturePath(string tree) => TextureFolder + "T_" + tree + "_Leaves.png";
    private static string ModelPath(string tree) => Folder + tree + ".fbx";
    private static string PrefabPath(string tree) => PrefabFolder + tree + ".prefab";

    public override uint GetVersion() => 5;

    private string TreeName
    {
        get
        {
            if (!assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return null;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            return Array.IndexOf(Trees, name) >= 0 || name == Oak ? name : null;
        }
    }

    private void OnPreprocessModel()
    {
        if (TreeName == null) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.globalScale = 1f;
        importer.addCollider = false;
    }

    private void OnPostprocessModel(GameObject root)
    {
        string tree = TreeName;
        if (tree == null) return;
        if (tree == Oak)
        {
            // Two meshes, named by the build script: OakTree_Trunk and OakTree_Leaves.
            context.DependsOnArtifact(OakTrunkMaterialPath);
            context.DependsOnArtifact(LeafMaterialPath(Oak));
            var bark = AssetDatabase.LoadAssetAtPath<Material>(OakTrunkMaterialPath);
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(LeafMaterialPath(Oak));
            if (bark == null || foliage == null)
            {
                context.LogImportWarning("Oak materials missing; run Tools/Chick/Trees/Place Oak Tree Next To Fruit Trees.");
                return;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = renderer.name.EndsWith("_Trunk") ? bark : foliage;
            return;
        }
        context.DependsOnArtifact(TrunkMaterialPath);
        context.DependsOnArtifact(LeafMaterialPath(tree));
        context.DependsOnArtifact(FruitMaterialPath);
        var trunk = AssetDatabase.LoadAssetAtPath<Material>(TrunkMaterialPath);
        var leaves = AssetDatabase.LoadAssetAtPath<Material>(LeafMaterialPath(tree));
        var fruit = AssetDatabase.LoadAssetAtPath<Material>(FruitMaterialPath);
        if (trunk == null || leaves == null || fruit == null)
        {
            context.LogImportWarning("Fruit tree materials missing; run Tools/Chick/Trees/Place Fruit Tree Samples.");
            return;
        }
        // Each fruit is its own child (Fruit_00, ...): FruitTree lets them ripen, fall and be eaten; apples left lying
        // get a caterpillar.
        var fruitTree = root.GetComponent<FruitTree>() ?? root.AddComponent<FruitTree>();
        if (tree == "AppleTree") fruitTree.Configure("apple", "Elma", true);
        else if (tree == "OrangeTree") fruitTree.Configure("orange", "Portakal", false);
        else fruitTree.Configure("cherry", "Kiraz", false);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.EndsWith("_Fruits") || renderer.name.StartsWith("Fruit_")) { renderer.sharedMaterials = new[] { fruit }; continue; }
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            // The FBX does not keep Blender's material order, so tell the parts apart by their UVs: the bark tiles
            // (UVs well outside 0-1), every leaf card sits inside one quarter of its atlas.
            var materials = new Material[mesh.subMeshCount];
            for (int s = 0; s < mesh.subMeshCount; s++) materials[s] = IsBark(mesh, s) ? trunk : leaves;
            renderer.sharedMaterials = materials;
        }
    }

    private static bool IsBark(Mesh mesh, int submesh)
    {
        Vector2[] uv = mesh.uv;
        foreach (int v in mesh.GetTriangles(submesh))
            if (uv[v].x < -0.01f || uv[v].x > 1.01f || uv[v].y < -0.01f || uv[v].y > 1.01f) return true;
        return false;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TextureFolder, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        bool leaves = assetPath.EndsWith("_Leaves.png", StringComparison.OrdinalIgnoreCase);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.alphaSource = leaves ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = leaves;
        // Cut-out leaves would thin away in the smaller mipmaps; keep each one's coverage at the material's cutoff.
        importer.mipMapsPreserveCoverage = leaves;
        importer.alphaTestReferenceValue = 0.5f;
        // Leaf cards sit inside one quarter of the atlas; the bark tiles along the branches.
        importer.wrapMode = leaves ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.maxTextureSize = leaves ? 2048 : 1024;
    }

    [MenuItem("Tools/Chick/Trees/Place Fruit Tree Samples")]
    private static void Place()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Fruit trees", "Exit play mode first.", "OK");
            return;
        }
        var root = GameObject.Find("Farm Reference Map");
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        foreach (string tree in Trees)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(tree)) == null) { terrain = null; break; }
        if (root == null || terrain == null)
        {
            EditorUtility.DisplayDialog("Fruit trees", root == null ? "The open scene has no 'Farm Reference Map'."
                : "Terrain or the tree models (Assets/Art/FruitTrees/*.fbx) missing.", "OK");
            return;
        }

        EnsureMaterials();
        foreach (string tree in Trees) AssetDatabase.ImportAsset(ModelPath(tree), ImportAssetOptions.ForceUpdate);
        var prefabs = new GameObject[Trees.Length];
        for (int i = 0; i < Trees.Length; i++) prefabs[i] = EnsurePrefab(Trees[i]);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Place fruit tree samples");
        int group = Undo.GetCurrentGroup();
        Transform old = SceneLookup.FindAnywhere(root, SamplesName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var chick = UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>();
        Vector3 from = chick != null ? chick.transform.position : root.transform.position;
        Vector3 facing = chick != null ? chick.transform.forward : Vector3.forward;
        bool clear = FindRow(terrain, from, facing, out Vector3[] spots);

        var samples = new GameObject(SamplesName);
        Undo.RegisterCreatedObjectUndo(samples, "Place fruit tree samples");
        samples.transform.SetParent(root.transform, false);
        samples.transform.position = spots[1];
        var random = new System.Random(7);
        for (int i = 0; i < Trees.Length; i++)
        {
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], samples.transform);
            Undo.RegisterCreatedObjectUndo(tree, "Place fruit tree samples");
            tree.transform.position = spots[i];
            tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        }

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = samples;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.Frame(new Bounds(spots[1] + Vector3.up * 4f, new Vector3(Spacing * 3f, 9f, 9f)), false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"Fruit tree samples placed at {spots[1]} (apple, orange, cherry, {Spacing} m apart)" +
                  (clear ? "" : "; no fully open ground found near the chick, so check they do not stand in anything") +
                  ". Save the scene to keep them (Ctrl+Z undoes it).", samples);
    }

    // The oak at the end of the row of sample fruit trees, wherever they stand now (also on ground that is not the
    // terrain, such as a test plane): one spacing past the last tree, on whatever is below. Run again, it replaces it.
    [MenuItem("Tools/Chick/Trees/Place Oak Tree Next To Fruit Trees")]
    private static void PlaceOak()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Oak tree", "Exit play mode first.", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(Oak)) == null)
        {
            EditorUtility.DisplayDialog("Oak tree", "Assets/Art/FruitTrees/OakTree.fbx is missing.", "OK");
            return;
        }
        // The sample fruit trees in the open scene, in row order.
        var row = new System.Collections.Generic.List<Transform>();
        foreach (string tree in Trees)
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == PrefabPath(tree)) { row.Add(t); break; }
        if (row.Count == 0)
        {
            EditorUtility.DisplayDialog("Oak tree", "No sample fruit trees in the open scene; place them first " +
                                        "(Tools/Chick/Trees/Place Fruit Tree Samples).", "OK");
            return;
        }

        EnsureMaterials();
        AssetDatabase.ImportAsset(ModelPath(Oak), ImportAssetOptions.ForceUpdate);
        GameObject prefab = EnsurePrefab(Oak);

        Transform last = row[row.Count - 1];
        Vector3 along = row.Count > 1 ? last.position - row[0].position : last.right;
        along.y = 0f;
        along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.right;
        Vector3 spot = last.position + along * (Spacing + 1f);   // the oak's crown is the widest
        Physics.SyncTransforms();
        bool grounded = false;
        float best = float.MinValue;
        foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 50f, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody != null || hit.collider is CharacterController) continue;
            if (hit.point.y > best && hit.point.y < last.position.y + 5f) { best = hit.point.y; grounded = true; }
        }
        spot.y = grounded ? best : last.position.y;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Place oak tree");
        int group = Undo.GetCurrentGroup();
        Transform parent = last.parent;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == PrefabPath(Oak))
                Undo.DestroyObjectImmediate(t.gameObject);
        var oak = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(oak, "Place oak tree");
        oak.transform.position = spot;
        oak.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
        Undo.CollapseUndoOperations(group);

        Selection.activeGameObject = oak;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.Frame(new Bounds(spot + Vector3.up * 4.5f, new Vector3(10f, 10f, 10f)), false);
        EditorSceneManager.MarkSceneDirty(oak.scene);
        Debug.Log($"Oak tree placed at {spot}, after {last.name}" + (grounded ? "" : " (nothing found below it, so at that tree's height)") +
                  ". Save the scene to keep it (Ctrl+Z undoes it). Credit: \"Stylized Tree\" by yonimantz (Sketchfab), CC-BY-4.0.", oak);
    }

    // A row of three spots on fairly flat, open ground in front of the chick (or around it), 12-30 m away.
    private static bool FindRow(Terrain terrain, Vector3 from, Vector3 facing, out Vector3[] spots)
    {
        Physics.SyncTransforms();
        facing.y = 0f;
        facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
        float[] distances = { 14f, 18f, 22f, 26f, 30f };
        float[] angles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };
        spots = new Vector3[3];
        foreach (float distance in distances)
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * facing;
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                Vector3 centre = from + direction * distance;
                bool ok = true;
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < 3 && ok; i++)
                {
                    spots[i] = OnGround(terrain, centre + across * (i - 1) * Spacing, out bool inside);
                    low = Mathf.Min(low, spots[i].y); high = Mathf.Max(high, spots[i].y);
                    ok = inside && Open(spots[i]);
                }
                if (ok && high - low < 1.5f) return true;
            }
        Vector3 fallback = from + facing * 16f, side = Vector3.Cross(Vector3.up, facing);
        for (int i = 0; i < 3; i++) spots[i] = OnGround(terrain, fallback + side * (i - 1) * Spacing, out _);
        return false;
    }

    private static Vector3 OnGround(Terrain terrain, Vector3 point, out bool inside)
    {
        Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
        inside = point.x > origin.x + 3f && point.x < origin.x + size.x - 3f && point.z > origin.z + 3f && point.z < origin.z + size.z - 3f;
        point.y = terrain.SampleHeight(point) + origin.y;
        return point;
    }

    // Nothing but the ground (and things that move) in the space a trunk and low crown would take.
    private static bool Open(Vector3 foot)
    {
        foreach (Collider c in Physics.OverlapCapsule(foot + Vector3.up * (ClearRadius + 0.3f), foot + Vector3.up * 6f,
                                                      ClearRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is TerrainCollider || c is CharacterController) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            return false;
        }
        return true;
    }

    internal static void EnsureMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var trunk = LoadOrCreate(TrunkMaterialPath, lit);
        trunk.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TrunkTexturePath));
        trunk.SetColor("_BaseColor", Color.white);
        trunk.SetFloat("_Smoothness", 0.1f);
        trunk.SetFloat("_AlphaClip", 0f);
        trunk.SetFloat("_Cull", 2f);
        Finish(trunk);
        var oakTrunk = LoadOrCreate(OakTrunkMaterialPath, lit);   // the oak's bark is a plain colour, as in the model
        oakTrunk.SetColor("_BaseColor", OakBark);
        oakTrunk.SetFloat("_Smoothness", 0.1f);
        oakTrunk.SetFloat("_AlphaClip", 0f);
        oakTrunk.SetFloat("_Cull", 2f);
        Finish(oakTrunk);
        foreach (string tree in new[] { Trees[0], Trees[1], Trees[2], Oak })
        {
            var leaves = LoadOrCreate(LeafMaterialPath(tree), lit);
            leaves.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LeafTexturePath(tree)));
            leaves.SetColor("_BaseColor", Color.white);
            leaves.SetFloat("_Smoothness", 0.12f);
            leaves.SetFloat("_AlphaClip", 1f);              // leaf clusters cut out of the cards
            leaves.SetFloat("_Cutoff", 0.5f);
            leaves.SetFloat("_Cull", 0f);                   // cards are seen from both sides
            Finish(leaves);
        }
        AssetDatabase.SaveAssets();
    }

    private static Material LoadOrCreate(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Finish(Material material)
    {
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Metallic", 0f);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
    }

    // The model with a capsule round the foot of the trunk, so the chick walks round it; the crown has no collider.
    internal static GameObject EnsurePrefab(string tree)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder.TrimEnd('/'))) AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Prefabs");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(tree));
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = tree;
            // The bark: the oak's own trunk mesh, or the fruit tree mesh's tiled submesh.
            MeshFilter filter = null;
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>())
                if (f.name.EndsWith("_Trunk") || (filter == null && !f.name.EndsWith("_Fruits") && !f.name.EndsWith("_Leaves"))) filter = f;
            var capsule = instance.AddComponent<CapsuleCollider>();
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int bark = 0;
            if (!filter.name.EndsWith("_Trunk"))
                for (int s = 0; s < mesh.subMeshCount; s++) if (IsBark(mesh, s)) bark = s;
            int[] trunk = mesh.GetTriangles(bark);
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (int v in trunk) if (vertices[v].y < 1f) { sum += vertices[v]; count++; }
            Vector3 middle = count > 0 ? sum / count : Vector3.zero;
            float radius = 0f;
            foreach (int v in trunk)
                if (vertices[v].y < 1f) radius = Mathf.Max(radius, new Vector2(vertices[v].x - middle.x, vertices[v].z - middle.z).magnitude);
            capsule.radius = Mathf.Clamp(radius, 0.15f, 0.6f);
            capsule.height = 3f;
            capsule.center = filter.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(middle.x, 1.5f, middle.z)) - instance.transform.position;
            return PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(tree));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
