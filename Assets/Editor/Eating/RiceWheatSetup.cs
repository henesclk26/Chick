using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class RiceWheatSetup
{
    private const string RicePrefabPath = "Assets/Prefabs/Edibles/RiceGrain.prefab";
    private const string WheatPrefabPath = "Assets/Prefabs/Edibles/WheatGrain.prefab";

    [MenuItem("Chick/Eating/Create Rice And Wheat Test Patch")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");

        GameObject player = GameObject.Find("ChickPlayer");
        GameObject riceSource = GameObject.Find("rice_grains");
        GameObject wheatSource = GameObject.Find("rice_grains (1)");
        if (player == null || riceSource == null || wheatSource == null)
            throw new InvalidOperationException("ChickPlayer, rice_grains or rice_grains (1) is missing.");

        GameObject ricePrefab = CreateFoodPrefab("RiceGrain", riceSource, RicePrefabPath);
        GameObject wheatPrefab = CreateFoodPrefab("WheatGrain", wheatSource, WheatPrefabPath);

        Undo.DestroyObjectImmediate(riceSource);
        Undo.DestroyObjectImmediate(wheatSource);

        GameObject patch = GameObject.Find("Rice And Wheat Test Patch");
        if (patch == null)
        {
            patch = new GameObject("Rice And Wheat Test Patch");
            Undo.RegisterCreatedObjectUndo(patch, "Create rice and wheat test patch");

            var random = new System.Random(2718);
            for (int i = 0; i < 200; i++)
            {
                bool rice = (i & 1) == 0;
                GameObject prefab = rice ? ricePrefab : wheatPrefab;
                GameObject grain = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
                Undo.RegisterCreatedObjectUndo(grain, "Create edible grain");
                int typeIndex = i / 2;
                grain.name = (rice ? "RiceGrain_" : "WheatGrain_") + typeIndex.ToString("D4");
                grain.transform.SetParent(patch.transform, false);

                int row = i / 10;
                int column = i % 10;
                Vector3 position = player.transform.position + player.transform.right * (0.95f + column * 0.085f)
                    + player.transform.forward * (0.22f + row * 0.10f);
                position += player.transform.right * ((float)random.NextDouble() - 0.5f) * 0.025f;

                RaycastHit hit;
                float ground = player.transform.position.y;
                int edibleLayer = LayerMask.NameToLayer("Edible");
                int groundMask = edibleLayer >= 0 ? ~(1 << edibleLayer) : ~0;
                if (Physics.Raycast(position + Vector3.up, Vector3.down, out hit, 3f, groundMask,
                    QueryTriggerInteraction.Ignore)) ground = hit.point.y;
                grain.transform.position = new Vector3(position.x, ground + 0.001f, position.z);
                grain.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

                var serialized = new SerializedObject(grain.GetComponent<EdibleObject>());
                serialized.FindProperty("persistentId").stringValue =
                    (rice ? "rice-grain-v1-" : "wheat-grain-v1-") + typeIndex.ToString("D4");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    private static GameObject CreateFoodPrefab(string prefabName, GameObject source, string prefabPath)
    {
        GameObject root = new GameObject(prefabName);
        try
        {
            int edibleLayer = LayerMask.NameToLayer("Edible");
            if (edibleLayer < 0) throw new InvalidOperationException("Edible layer is missing.");
            root.layer = edibleLayer;

            GameObject visual = UnityEngine.Object.Instantiate(source);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = source.transform.rotation;
            visual.transform.localScale = source.transform.lossyScale;
            foreach (Transform child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = edibleLayer;
            foreach (Collider childCollider in visual.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(childCollider);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException(source.name + " has no renderer.");
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;

            Transform bitePoint = new GameObject("BitePoint").transform;
            bitePoint.gameObject.layer = edibleLayer;
            bitePoint.SetParent(root.transform, false);
            bitePoint.localPosition = collider.center;

            EdibleObject edible = root.AddComponent<EdibleObject>();
            var data = new SerializedObject(edible);
            data.FindProperty("bitePoint").objectReferenceValue = bitePoint;
            data.FindProperty("contactVisual").objectReferenceValue = visual.transform;
            data.FindProperty("interactionAssistMultiplier").floatValue = 1.35f;
            data.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + prefabPath);
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
