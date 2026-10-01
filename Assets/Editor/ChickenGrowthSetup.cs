using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ChickenGrowthSetup
{
    public static void Build()
    {
        const string folder = "Assets/Prefabs/PlayerGrowth";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "PlayerGrowth");
        string controllerPath = folder + "/ChickenGameplay.controller";
        string visualPath = folder + "/ChickenVisual.prefab";
        if (File.Exists(controllerPath) || File.Exists(visualPath))
            throw new System.InvalidOperationException("Growth assets already exist; edit them instead of rebuilding.");

        var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Animals_3D/Chicken/Animators/ChickGameplayController.controller");
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), controllerPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var chickenEat = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Animals_3D/Chicken/AnimationClips/chicken/twoLayer/eat.anim"));
        chickenEat.name = "ChickenEat_Gameplay";
        AnimationUtility.SetAnimationEvents(chickenEat, new[] {
            new AnimationEvent { functionName = "OnEatImpact", time = 20f / 60f },
            new AnimationEvent { functionName = "OnEatRecovery", time = 30f / 60f }
        });
        var clipSettings = AnimationUtility.GetAnimationClipSettings(chickenEat);
        clipSettings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(chickenEat, clipSettings);
        AssetDatabase.CreateAsset(chickenEat, folder + "/ChickenEat_Gameplay.anim");
        var layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (i == 1) layers[i].avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
                "Assets/Animals_3D/Chicken/Fbx/ChickenAvatarMask.mask");
            foreach (var child in layers[i].stateMachine.states)
            {
                var state = child.state;
                var oldClip = state.motion as AnimationClip;
                if (oldClip == null) throw new System.InvalidOperationException("Unexpected motion: " + state.name);
                string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(oldClip));
                if (name == "peep") name = "honk";
                var clip = state.name == "eat" ? chickenEat : AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Animals_3D/Chicken/AnimationClips/chicken/twoLayer/" + name + ".anim");
                if (clip == null) throw new System.InvalidOperationException("Missing chicken clip: " + name);
                // Keep the approved playback speed. Some chicken clips contain
                // multiple repetitions; matching total clip length would rush them.
                state.motion = clip;
                EditorUtility.SetDirty(state);
            }
        }
        controller.layers = layers;
        EditorUtility.SetDirty(controller);

        var visual = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Animals_3D/Chicken/Resources/Prefabs/chicken.prefab"));
        try
        {
            visual.name = "ChickenVisual";
            visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var component in visual.GetComponents<Component>())
                if (!(component is Transform) && !(component is Animator)) Object.DestroyImmediate(component);
            var animator = visual.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            visual.AddComponent<ChickenEatEventRelay>();
            var beak = new GameObject("BeakEatPoint").transform;
            beak.SetParent(visual.transform.Find("Chicken/body/chest/neck/head/beak_top"), false);
            beak.localPosition = new Vector3(.0000068f, .0002085f, -.0000947f) * 2.8f;
            PrefabUtility.SaveAsPrefabAsset(visual, visualPath);
        }
        finally { Object.DestroyImmediate(visual); }

        var player = GameObject.Find("ChickPlayer");
        var growth = player.GetComponent<PlayerGrowthController>();
        if (growth == null) growth = player.AddComponent<PlayerGrowthController>();
        var growthSettings = new SerializedObject(growth);
        growthSettings.FindProperty("chickenVisualPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
        growthSettings.ApplyModifiedPropertiesWithoutUndo();
        var camera = new SerializedObject(Camera.main.GetComponent<FreeOrbitThirdPersonCamera>());
        camera.FindProperty("chickenProfile.distance").floatValue = 1.6f;
        camera.FindProperty("chickenProfile.targetHeight").floatValue = .24f;
        camera.FindProperty("chickenProfile.fieldOfView").floatValue = 58f;
        camera.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }
}
