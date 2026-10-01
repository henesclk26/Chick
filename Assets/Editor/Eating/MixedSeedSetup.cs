using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MixedSeedSetup
{
    [MenuItem("Chick/Eating/Create Mixed Seed Test Patch")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var player = GameObject.Find("ChickPlayer");
        if (player == null || player.scene.name != "SampleScene") throw new InvalidOperationException("Open SampleScene first.");
        var corn = CreateFood("CornSeed", "Assets/Assets/hant-painted-corn-kernel/source/cornPainted.glb", .035f);
        var sunflower = CreateFood("SunflowerSeed", "Assets/Assets/sunflower_seed.glb", .045f);
        var wheat = AssetDatabase.LoadAssetAtPath<GameObject>(WheatSliceSetup.PrefabPath);
        var patch = GameObject.Find("Mixed Seed Test Patch");
        if (patch == null)
        {
            patch = new GameObject("Mixed Seed Test Patch");
            Undo.RegisterCreatedObjectUndo(patch, "Create mixed seeds");
            var random = new System.Random(1542);
            var prefabs = new[] { corn, sunflower, wheat };
            for (int i = 0; i < 300; i++)
            {
                var food = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % 3], player.scene);
                Undo.RegisterCreatedObjectUndo(food, "Create seed");
                food.name = prefabs[i % 3].name + "_" + i.ToString("D4");
                food.transform.SetParent(patch.transform, false);
                var pos = player.transform.position + player.transform.right * ((i % 15 - 7) * .09f)
                    + player.transform.forward * (.24f + i / 15 * .10f);
                pos += player.transform.right * ((float)random.NextDouble() - .5f) * .025f;
                RaycastHit hit;
                float ground = player.transform.position.y;
                if (Physics.Raycast(pos + Vector3.up, Vector3.down, out hit, 3f,
                    ~(1 << LayerMask.NameToLayer("Edible")), QueryTriggerInteraction.Ignore)) ground = hit.point.y;
                food.transform.position = new Vector3(pos.x, ground + (i % 3 == 2 ? .011f : .002f), pos.z);
                food.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                var serialized = new SerializedObject(food.GetComponent<EdibleObject>());
                serialized.FindProperty("persistentId").stringValue = "mixed-seed-v1-" + i.ToString("D4");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    private static GameObject CreateFood(string name, string modelPath, float length)
    {
        string path = "Assets/Prefabs/Edibles/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) throw new InvalidOperationException("Missing model: " + modelPath);
        var root = new GameObject(name);
        try
        {
            root.layer = LayerMask.NameToLayer("Edible");
            var edible = root.AddComponent<EdibleObject>();
            var visual = UnityEngine.Object.Instantiate(model, root.transform);
            visual.name = "Visual";
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderer.");
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            visual.transform.localScale *= length / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = bounds.size;
            collider.center = new Vector3(0, bounds.size.y * .5f, 0);
            var bite = new GameObject("BitePoint").transform;
            bite.SetParent(root.transform, false);
            bite.localPosition = collider.center;
            var data = new SerializedObject(edible);
            data.FindProperty("bitePoint").objectReferenceValue = bite;
            data.FindProperty("contactVisual").objectReferenceValue = visual.transform;
            data.FindProperty("interactionAssistMultiplier").floatValue = 1.35f;
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (var r in renderers)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
