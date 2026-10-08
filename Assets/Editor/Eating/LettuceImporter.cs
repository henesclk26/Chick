using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the lettuce (Assets/Art/Lettuce/Lettuce.fbx, built by ArtSource/Lettuce): at farm size, no embedded
/// materials or animation, every renderer on M_Lettuce (palette, unfiltered) and nothing static, since its leaves are
/// knocked off and fall. BerryPlantImporter makes it edible. The palette texture is imported point-filtered without
/// mipmaps so its cells never blend.
/// </summary>
public sealed class LettuceImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Art/Lettuce/";
    private const string ModelPath = Folder + "Lettuce.fbx";
    private const string MaterialPath = Folder + "M_Lettuce.mat";
    // Modelled at real size (~0.5 m across); the farm's plants are about twice that.
    private const float ImportScale = 2f;

    public override uint GetVersion() => 4;

    private void OnPreprocessModel()
    {
        if (!string.Equals(assetPath, ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;   // flat facets, as modelled
        importer.globalScale = ImportScale;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder + "Textures/", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!string.Equals(assetPath, ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        context.DependsOnArtifact(MaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) context.LogImportWarning("Missing " + MaterialPath + "; lettuce imported without its material.");
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (material != null)
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
        }
    }
}
