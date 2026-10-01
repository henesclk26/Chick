using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Explicit edit-time setup only; never runs at game startup.</summary>
public static class WheatSliceSetup
{
    public const string PrefabPath = "Assets/Prefabs/Edibles/WheatGrain_Test.prefab";
    public const string ClipPath = "Assets/Animations/ChickEat_Gameplay.anim";

    [MenuItem("Chick/Eating/Set Up Wheat Test Slice")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
        var player = GameObject.Find("ChickPlayer");
        if (player == null) throw new System.InvalidOperationException("ChickPlayer is missing.");
        Folder("Assets", "Animations");
        Folder("Assets", "Prefabs"); Folder("Assets/Prefabs", "Edibles");
        Folder("Assets", "Art"); Folder("Assets/Art", "Edibles");

        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layer = tags.FindProperty("layers").GetArrayElementAtIndex(9);
        if (layer.stringValue != "" && layer.stringValue != "Edible")
            throw new System.InvalidOperationException("Layer 9 is occupied.");
        layer.stringValue = "Edible";
        tags.ApplyModifiedPropertiesWithoutUndo();

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) == null)
            AssetDatabase.CopyAsset("Assets/Animals_3D/Chicken/AnimationClips/chick/twoLayer/eat.anim", ClipPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        // Exact source curves, untouched duration. Dedicated copy isolates events from demo animators.
        AnimationUtility.SetAnimationEvents(clip, new[]
        {
            new AnimationEvent { functionName = "OnEatImpact", time = 21f / 60f },
            new AnimationEvent { functionName = "OnEatRecovery", time = 30f / 60f }
        });
        var ac = player.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
        if (ac == null) throw new System.InvalidOperationException("Expected the current gameplay AnimatorController.");
        if (!System.Array.Exists(ac.parameters, p => p.name == "EatPlaybackSpeed"))
            ac.AddParameter(new AnimatorControllerParameter
                { name = "EatPlaybackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1.4f });
        foreach (var child in ac.layers[0].stateMachine.states)
        {
            if (child.state.name != "eat") continue;
            child.state.motion = clip;
            child.state.speed = 1f;
            child.state.speedParameter = "EatPlaybackSpeed";
            child.state.speedParameterActive = true;
            EditorUtility.SetDirty(child.state);
        }
        EditorUtility.SetDirty(ac);
        if (player.GetComponent<ChickEatingController>() == null)
            Undo.AddComponent<ChickEatingController>(player);

        const string meshPath = "Assets/Art/Edibles/WheatGrain_Test.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = MakeGrainMesh();
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        const string materialPath = "Assets/Art/Edibles/WheatGold_Test.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(1f, 0.72f, 0.22f));
            material.SetFloat("_Smoothness", 0.22f);
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = new GameObject("WheatGrain_Test");
            root.layer = 9;
            root.AddComponent<EdibleObject>();
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.03f, 0.022f, 0.046f);
            var visual = new GameObject("Visual");
            visual.layer = 9;
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        if (GameObject.Find("Wheat Test Patch") == null)
        {
            var patch = new GameObject("Wheat Test Patch");
            Undo.RegisterCreatedObjectUndo(patch, "Create wheat test patch");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            for (int i = 0; i < 15; i++)
            {
                var grain = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                grain.name = "WheatGrain_Test_" + (i + 1).ToString("00");
                grain.transform.SetParent(patch.transform, false);
                float x = (i % 3 - 1) * (i < 3 ? 0.028f : 0.13f);
                float z = 0.09f + i / 3 * 0.26f;
                grain.transform.position = player.transform.position +
                    player.transform.right * x + player.transform.forward * z + Vector3.up * 0.011f;
                grain.transform.rotation = Quaternion.Euler(0, i * 47f, 0);
            }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    private static Mesh MakeGrainMesh()
    {
        // 8 sides x 4 latitude bands, flat-shaded 48 triangles, shared by all grains.
        var vertices = new System.Collections.Generic.List<Vector3>();
        var indices = new System.Collections.Generic.List<int>();
        for (int band = 0; band < 4; band++)
        for (int side = 0; side < 8; side++)
        {
            Vector3 a = Point(band, side), b = Point(band + 1, side);
            Vector3 c = Point(band + 1, side + 1), d = Point(band, side + 1);
            if (band != 0) Triangle(vertices, indices, a, b, d);
            if (band != 3) Triangle(vertices, indices, d, b, c);
        }
        var mesh = new Mesh { name = "WheatGrain_Test_48tri" };
        mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 Point(int band, int side)
    {
        float latitude = Mathf.PI * band / 4f;
        float longitude = Mathf.PI * 2f * side / 8f;
        return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude) * 0.014f,
            Mathf.Sin(latitude) * Mathf.Sin(longitude) * 0.010f,
            Mathf.Cos(latitude) * 0.022f);
    }

    private static void Triangle(System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<int> indices, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
    }

    private static void Folder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
