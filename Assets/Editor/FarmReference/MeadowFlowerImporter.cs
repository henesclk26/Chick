using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sets up the hand-painted meadow flower models (Assets/Art/MeadowFlowers/Flower_*.fbx) on import: no embedded
/// materials, every renderer on M_MeadowFlowers (Chick/InteractiveFoliage, so flowers bend away from the player like
/// the meadow grass), and nothing else imported. The models carry no colliders, so they stay walk-through.
/// </summary>
public sealed class MeadowFlowerImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Art/MeadowFlowers/";
    private const string MaterialPath = Folder + "M_MeadowFlowers.mat";
    // The flowers are modelled at real-world size, about a third of the BOKI meadow meshes. Foliage bending assumes an
    // unscaled plant is ~0.3 units across (FoliageBendDriver.plantReach) and widens the touch range with object scale,
    // so importing them small and scaling instances up made them lean from over a metre away.
    private const float ImportScale = 3f;

    public override uint GetVersion() => 2;

    private bool IsFlower =>
        assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) &&
        assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    private void OnPreprocessModel()
    {
        if (!IsFlower) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;   // flat facets, as painted
        importer.globalScale = ImportScale;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!IsFlower) return;
        // Import the material first, and reimport the flowers if it moves or is recreated.
        context.DependsOnArtifact(MaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            context.LogImportWarning("Missing " + MaterialPath + "; flower imported without its material.");
            return;
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = material;
    }
}
