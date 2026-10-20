using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Butterflies fluttering round the farm ("Cartoon butterflies" by Mixall, Fab, see CREDITS.md; in
/// Assets/Animal/AI cartoon butterflies: one rigged model with "fly" and "idle" clips and the wings of ten species as
/// textures).
/// - Import: no embedded materials; both clips loop.
/// - Menu: one cut-out, double-sided material per species, an Animator controller (Fly / Idle), the Butterfly prefab
///   (model a little bigger than life so it reads next to the chick, head along +Z, the simpler body LOD switched off
///   so the two bodies do not flicker into each other) and a dozen butterflies, each of a random species, released in
///   threes round the pond, the chicken yard, the barn yard and the flower borders ("Butterflies" in the Map). Not food:
///   Butterfly.cs makes them wander and settle on rocks and flowers. Running it again replaces them.
/// </summary>
public sealed class ButterflySetup : AssetPostprocessor
{
    private const string Source = "Assets/Animal/AI cartoon butterflies/";
    private const string ModelPath = Source + "Butterfly.FBX";
    private const string TextureFolder = Source + "Textures";
    private const string Folder = "Assets/Art/Butterflies/";
    private const string ControllerPath = Folder + "Butterfly.controller";
    private const string PrefabPath = Folder + "Butterfly.prefab";
    private const string GroupName = "Butterflies";
    private const float ModelScale = 1.8f;            // 5 cm wingspan -> 9 cm
    private const int PerSpot = 3;

    public override uint GetVersion() => 1;

    private bool IsModel => assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase);

    private void OnPreprocessModel()
    {
        if (!IsModel) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.globalScale = 1f;
        importer.addCollider = false;
    }

    private void OnPreprocessAnimation()
    {
        if (!IsModel) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        foreach (var clip in clips)
        {
            clip.loopTime = true;
            clip.name = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
        }
        importer.clipAnimations = clips;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TextureFolder + "/", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;   // thin wing edges stay at a distance
        importer.alphaTestReferenceValue = .5f;
        importer.maxTextureSize = 512;
    }

    [MenuItem("Tools/Chick/Butterflies/Release Butterflies")]
    private static void Release()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Butterflies", "Exit play mode first.", "OK"); return; }
        var root = GameObject.Find("Farm Reference Map");
        if (root == null || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            EditorUtility.DisplayDialog("Butterflies", root == null ? "The open scene has no 'Farm Reference Map'." : ModelPath + " is missing.", "OK");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/Art", "Butterflies");
        foreach (string t in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }).Select(AssetDatabase.GUIDToAssetPath))
            AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate);
        var materials = SpeciesMaterials();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var controller = EnsureController();
        if (materials.Count == 0 || controller == null)
        {
            EditorUtility.DisplayDialog("Butterflies", "The wing textures or the fly/idle clips were not found.", "OK");
            return;
        }
        GameObject prefab = EnsurePrefab(controller, materials[0]);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Release butterflies");
        int undo = Undo.GetCurrentGroup();
        Transform map = SceneLookup.FindAnywhere(root, "Map");
        Transform parent = map != null && map.parent == null ? map : root.transform;
        Transform old = SceneLookup.FindAnywhere(root, GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Release butterflies");
        group.transform.SetParent(parent, false);

        Physics.SyncTransforms();
        var spots = Spots(root);
        var random = new System.Random(42);
        int count = 0;
        foreach (var (name, centre) in spots)
            for (int i = 0; i < PerSpot; i++)
            {
                Vector3 p = centre + new Vector3((float)random.NextDouble() * 6f - 3f, 0f, (float)random.NextDouble() * 6f - 3f);
                if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                p.y += 1f;
                var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                b.name = "Butterfly " + (++count);
                b.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                Material wing = materials[random.Next(materials.Count)];
                foreach (var r in b.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = wing;
            }
        Undo.CollapseUndoOperations(undo);
        Selection.activeGameObject = group;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"Released {count} butterflies ({materials.Count} species) round " + string.Join(", ", spots.Select(s => s.name)) +
                  ". Save the scene to keep them (Ctrl+Z undoes it).", group);
    }

    // Where they are released: the pond, the chicken yard, the barn yard and the flower borders (whichever exist).
    private static List<(string name, Vector3 centre)> Spots(GameObject root)
    {
        var spots = new List<(string, Vector3)>();
        Transform pond = SceneLookup.FindAnywhere(root, "Farm Pond");
        var water = pond != null ? pond.Find("Water Surface") : null;
        if (water != null) spots.Add(("the pond", water.position));
        spots.Add(("the chicken yard", root.transform.TransformPoint(new Vector3(20f, 0f, 18f))));   // fence x 5-35, z 0-36
        var barn = root.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>()).FirstOrDefault(t => t.name.StartsWith("SM_Bld_Barn_01"));
        if (barn != null) spots.Add(("the barn yard", barn.position + Vector3.right * 9f));
        var borders = root.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>())
            .Where(t => t.name.StartsWith("Flower Border")).Select(t => t.position).ToList();
        if (borders.Count > 0) spots.Add(("the flower borders", borders.Aggregate(Vector3.zero, (a, b) => a + b) / borders.Count));
        return spots;
    }

    private static List<Material> SpeciesMaterials()
    {
        var list = new List<Material>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            string species = Path.GetFileNameWithoutExtension(path).Replace("_diffuse", "").Replace(" ", "_");
            string matPath = Folder + "M_Butterfly_" + species + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, matPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", .2f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_AlphaClip", 1f);          // wing shapes cut out of their cards
            m.SetFloat("_Cutoff", .5f);
            m.SetFloat("_Cull", 0f);               // wings seen from above and below
            m.enableInstancing = true;
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(m);
            list.Add(m);
        }
        AssetDatabase.SaveAssets();
        return list;
    }

    private static AnimatorController EnsureController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        AnimationClip fly = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("fly"));
        AnimationClip idle = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle"));
        if (fly == null || idle == null) return null;
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var flyState = machine.AddState("Fly");
        flyState.motion = fly;
        var idleState = machine.AddState("Idle");
        idleState.motion = idle;
        machine.defaultState = flyState;
        AssetDatabase.SaveAssets();
        return controller;
    }

    // Root: Butterfly. Child "Visual": the model, bigger, head along +Z, animated by the controller.
    private static GameObject EnsurePrefab(AnimatorController controller, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var root = new GameObject("Butterfly");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            visual.transform.localScale = Vector3.one * ModelScale;
            // (No ?? here: in the Editor a missing component comes back as a "fake null" object, not null.)
            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // a 9 cm butterfly: no shadow needed
                if (r.name.Contains("LOD1")) r.enabled = false;
            }
            // Head along +Z: the head is the end of the body that rises higher (head and antennae).
            var body = visual.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("body_LOD0"));
            Mesh mesh = body is SkinnedMeshRenderer s ? s.sharedMesh : body != null ? body.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh != null)
            {
                var points = mesh.vertices.Select(v => root.transform.InverseTransformPoint(body.transform.TransformPoint(v))).ToList();
                float mid = (points.Max(p => p.z) + points.Min(p => p.z)) / 2f;
                float front = points.Where(p => p.z > mid).Select(p => p.y).DefaultIfEmpty(0f).Max();
                float back = points.Where(p => p.z <= mid).Select(p => p.y).DefaultIfEmpty(0f).Max();
                if (back > front) visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            root.AddComponent<Butterfly>();
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
