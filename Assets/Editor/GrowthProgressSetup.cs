using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class GrowthProgressSetup
{
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        const string folder = "Assets/UI/GrowthProgress/";
        foreach (string file in new[] { "ChickIcon.png", "ChickenIcon.png" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(folder + file);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(folder + "GrowthPanelSettings.asset");
        if (panel == null)
        {
            panel = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/FarmPanelSettings.asset"));
            panel.name = "GrowthPanelSettings";
            AssetDatabase.CreateAsset(panel, folder + "GrowthPanelSettings.asset");
        }
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1920, 1080);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = .5f;
        panel.sortingOrder = -1;
        EditorUtility.SetDirty(panel);
        var go = GameObject.Find("Growth Progress HUD");
        if (go == null)
        {
            go = new GameObject("Growth Progress HUD");
            Undo.RegisterCreatedObjectUndo(go, "Add growth HUD");
        }
        var document = go.GetComponent<UIDocument>();
        if (document == null) document = go.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "GrowthProgressHUD.uxml");
        document.sortingOrder = 20;
        var controller = go.GetComponent<GrowthProgressController>();
        if (controller == null) controller = go.AddComponent<GrowthProgressController>();
        var settings = new SerializedObject(controller);
        settings.FindProperty("saves").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<FarmSaveSystem>();
        settings.FindProperty("gameplayGate").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<MainMenuGameplayGate>();
        settings.FindProperty("timeManager").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<GameTimeManager>();
        settings.FindProperty("chickIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "ChickIcon.png");
        settings.FindProperty("chickenIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "ChickenIcon.png");
        settings.FindProperty("upgradePlayer").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>();
        settings.ApplyModifiedPropertiesWithoutUndo();

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Edibles", "Assets/Assets/EdibleFoods" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool looseSeed = path.StartsWith("Assets/Prefabs/Edibles/", StringComparison.Ordinal);
                bool seedFruit = prefab.name.StartsWith("Watermelon", StringComparison.Ordinal) ||
                    prefab.name == "Tomato_Piece" || prefab.name == "Pumpkin_Quarter" || prefab.name == "Apple_Red_Half_2";
                foreach (var edible in prefab.GetComponentsInChildren<EdibleObject>(true))
                {
                    var data = new SerializedObject(edible);
                    data.FindProperty("category").enumValueIndex = (int)(looseSeed || (seedFruit && edible.transform != prefab.transform)
                        ? EdibleCategory.Seed : EdibleCategory.Other);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
    }
}
