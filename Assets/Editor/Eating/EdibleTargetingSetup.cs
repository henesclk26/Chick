using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EdibleTargetingSetup
{
    [MenuItem("Chick/Eating/Set Up Forgiving Contact Assist")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var player = GameObject.Find("ChickPlayer");
        var bone = player.transform.Find("Chick/root/body/chest/neck/head/beak_top");
        var point = bone.Find("BeakEatPoint");
        if (point == null)
        {
            point = new GameObject("BeakEatPoint").transform;
            Undo.RegisterCreatedObjectUndo(point.gameObject, "Create beak contact point");
            point.SetParent(bone, false);
        }
        // Bone scale is 100. Offset sampled at frame 21; world contact ~= (-.007,.011,.087).
        point.localPosition = new Vector3(0.00000682f, 0.00020851f, -0.00009465f);
        point.localRotation = Quaternion.identity;
        point.localScale = Vector3.one * 0.01f;
        var eater = new SerializedObject(player.GetComponent<ChickEatingController>());
        eater.FindProperty("beakEatPoint").objectReferenceValue = point;
        eater.FindProperty("eatDetectionRadius").floatValue = 0.12f;
        eater.FindProperty("eatForwardOffset").floatValue = 0.09f;
        eater.FindProperty("maxTargetAngle").floatValue = 80f;
        eater.FindProperty("maxContactAssistDistance").floatValue = 0.13f;
        eater.FindProperty("contactAssistDuration").floatValue = 0.14f;
        eater.FindProperty("localImpactPoint").vector3Value = new Vector3(-0.007f, 0.011f, 0.087f);
        eater.ApplyModifiedProperties();

        const string prefabPath = "Assets/Prefabs/Edibles/WheatGrain_Test.prefab";
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var edible = new SerializedObject(contents.GetComponent<EdibleObject>());
            edible.FindProperty("contactVisual").objectReferenceValue = contents.transform.Find("Visual");
            edible.FindProperty("interactionAssistMultiplier").floatValue = 1.35f;
            edible.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }

        var patch = GameObject.Find("Wheat Test Patch");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Vector3[] positions =
        {
            new Vector3(-0.055f, 0.011f, 0.11f), new Vector3(0.055f, 0.011f, 0.11f),
            new Vector3(0, 0.011f, 0.15f), new Vector3(0, 0.011f, -0.12f), new Vector3(0, 0.011f, 0.28f)
        };
        string[] names = { "TargetTest_FrontLeft", "TargetTest_FrontRight", "TargetTest_Edge", "TargetTest_Behind", "TargetTest_TooFar" };
        for (int i = 0; i < names.Length; i++)
        {
            if (patch.transform.Find(names[i]) != null) continue;
            var grain = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            grain.name = names[i]; grain.transform.SetParent(patch.transform, false);
            grain.transform.position = player.transform.TransformPoint(positions[i]);
        }
        EditorSceneManager.MarkSceneDirty(player.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(player.scene);
    }
}
