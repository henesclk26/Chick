using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the sheep fold models exported from ArtSource/SheepFold (Blender).
/// One palette material for everything; the sheep and lamb are Generic rigs with looping in-place clips.
/// </summary>
public sealed class SheepFoldImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/SheepFold";
    public const string ModelFolder = Folder + "/Models/";
    public const string PalettePath = Folder + "/Textures/T_SheepFold_Palette.png";
    public const string MaterialPath = Folder + "/M_SheepFold.mat";

    private static bool IsAnimal(string path) =>
        path.EndsWith("/Sheep.fbx") || path.EndsWith("/Lamb.fbx");

    private void OnPreprocessTexture()
    {
        if (assetPath != PalettePath) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        // Every face samples the middle of one 16 px swatch; mips would bleed neighbouring swatches.
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ModelFolder)) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.addCollider = false;
        if (IsAnimal(assetPath))
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }
        else
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
        }
    }

    private void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(ModelFolder) || !IsAnimal(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        // Takes arrive as "<Rig>|<Clip>"; keep the short name and loop every clip in place.
        importer.clipAnimations = importer.defaultClipAnimations.Select(clip =>
        {
            clip.name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
            clip.loopTime = true;
            clip.loopPose = true;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            return clip;
        }).ToArray();
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(ModelFolder)) return;
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
    }
}
