using System.IO;
using UnityEditor;
using UnityEngine;

public static class GenerateJournalCharacterPreviews
{
    private const string OutputFolder = "Assets/UI/Journal/CharacterPreviews";

    [MenuItem("Tools/Chick/Regenerate Journal Character Previews")]
    public static void Generate()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Journal previews must be regenerated in Play Mode so the chick uses its real animated in-game pose.");
            return;
        }

        PlayerGrowthController growth = Object.FindFirstObjectByType<PlayerGrowthController>(FindObjectsInactive.Include);
        if (growth == null)
        {
            Debug.LogError("Journal previews: PlayerGrowthController was not found in the open scene.");
            return;
        }

        Renderer chick = FindRenderer(growth.transform, "chick.body");
        SerializedObject growthObject = new SerializedObject(growth);
        GameObject chickenPrefab = growthObject.FindProperty("chickenVisualPrefab")?.objectReferenceValue as GameObject;
        if (chick == null || chickenPrefab == null)
        {
            Debug.LogError("Journal previews: the in-game chick renderer or chicken visual prefab was not found.");
            return;
        }

        EnsureFolders();
        RenderModel(growth.gameObject, OutputFolder + "/ChickGamePreview.png", "chick.body", 118f);
        RenderModel(chickenPrefab, OutputFolder + "/ChickenGamePreview.png", null, 28f);
        AssetDatabase.Refresh();
        ConfigureTexture(OutputFolder + "/ChickGamePreview.png");
        ConfigureTexture(OutputFolder + "/ChickenGamePreview.png");
        AssetDatabase.SaveAssets();
        Debug.Log("Journal previews regenerated from the real in-game character models and materials.");
    }

    private static Renderer FindRenderer(Transform root, string rendererName)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            if (renderer.name == rendererName)
                return renderer;
        return null;
    }

    private static void RenderModel(GameObject source, string outputPath, string visibleRendererName = null, float yaw = 28f)
    {
        GameObject clone = null;
        GameObject cameraObject = null;
        GameObject keyLightObject = null;
        GameObject fillLightObject = null;
        RenderTexture target = null;
        Texture2D image = null;
        Mesh bakedMesh = null;

        try
        {
            SkinnedMeshRenderer animatedSource = null;
            if (!string.IsNullOrEmpty(visibleRendererName))
            {
                foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.name == visibleRendererName) { animatedSource = renderer; break; }
            }

            if (animatedSource != null)
            {
                bakedMesh = new Mesh { name = visibleRendererName + "_JournalPose" };
                animatedSource.BakeMesh(bakedMesh, true);
                clone = new GameObject(visibleRendererName);
                clone.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
                clone.AddComponent<MeshRenderer>().sharedMaterials = animatedSource.sharedMaterials;
            }
            else
            {
                clone = Object.Instantiate(source);
                clone.name = source.name + "_JournalPreview";
            }
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.SetActive(true);
            clone.transform.SetParent(null);
            clone.transform.position = Vector3.zero;
            clone.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            SetLayerRecursively(clone, 31);

            foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = string.IsNullOrEmpty(visibleRendererName) || renderer.name == visibleRendererName;

            Bounds bounds = CalculateBounds(clone);
            clone.transform.position -= bounds.center;
            bounds = CalculateBounds(clone);
            float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

            cameraObject = new GameObject("JournalPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << 31;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.y * 1.18f, extent * 1.12f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = extent * 10f + 10f;
            camera.transform.position = new Vector3(extent * 2.4f, extent * 0.45f, -extent * 3.8f);
            camera.transform.LookAt(new Vector3(0f, bounds.extents.y * 0.04f, 0f));

            keyLightObject = CreateLight("JournalPreviewKey", new Color(1f, 0.92f, 0.78f), 1.15f, new Vector3(35f, -35f, 0f));
            fillLightObject = CreateLight("JournalPreviewFill", new Color(0.67f, 0.79f, 1f), 0.55f, new Vector3(330f, 145f, 0f));

            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 8,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            image = new Texture2D(512, 512, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.GetFullPath(outputPath), image.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            if (target != null) target.Release();
            if (image != null) Object.DestroyImmediate(image);
            if (target != null) Object.DestroyImmediate(target);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (keyLightObject != null) Object.DestroyImmediate(keyLightObject);
            if (fillLightObject != null) Object.DestroyImmediate(fillLightObject);
            if (clone != null) Object.DestroyImmediate(clone);
            if (bakedMesh != null) Object.DestroyImmediate(bakedMesh);
        }
    }

    private static GameObject CreateLight(string name, Color color, float intensity, Vector3 rotation)
    {
        GameObject lightObject = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << 31;
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(rotation);
        return lightObject;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Renderer first = null;
        foreach (Renderer renderer in renderers)
            if (renderer.enabled) { first = renderer; break; }
        Bounds bounds = first != null ? first.bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (Renderer renderer in renderers)
            if (renderer.enabled && renderer != first) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UI/Journal/CharacterPreviews"))
            AssetDatabase.CreateFolder("Assets/UI/Journal", "CharacterPreviews");
    }

    private static void ConfigureTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
    }
}
