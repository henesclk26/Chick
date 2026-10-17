using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sets up the hand-painted ant models (Assets/Art/Ants/*.fbx) on import: no embedded materials, the baked atlas
/// material on every renderer, and the object hierarchy kept as authored so AntColony can find the Ant_Leg_* and
/// Ant_Antenna_* children it animates. The nest gets a mesh collider so the trail can climb over the mound and the
/// player can walk onto it, the trail bush one so the player cannot walk into it; the ant itself has none. The
/// hand-painted nest (AntNest_v2.fbx) collides on its mound only, so its crumbs, twigs and grass catch nothing.
/// </summary>
public sealed class AntModelImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Art/Ants/";

    public override uint GetVersion() => 3;

    private bool IsAntModel =>
        assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) &&
        assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    private bool IsNest => assetPath.EndsWith("AntNest.fbx", StringComparison.OrdinalIgnoreCase);
    private bool IsNestV2 => assetPath.EndsWith("AntNest_v2.fbx", StringComparison.OrdinalIgnoreCase);
    private const string NestV2Mound = "AntNest_v2_Mound";
    private bool IsBush => assetPath.EndsWith("AntBush.fbx", StringComparison.OrdinalIgnoreCase);

    private void OnPreprocessModel()
    {
        if (!IsAntModel) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;   // as authored: flat facets, the v2 mound smooth
        importer.preserveHierarchy = true;
        importer.globalScale = 1f;
        importer.addCollider = IsNest || IsBush;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!IsAntModel) return;
        string materialPath = Folder + (IsNestV2 ? "M_AntNest_v2.mat" : IsNest ? "M_AntNest.mat" : IsBush ? "M_AntBush.mat" : "M_Ant.mat");
        context.DependsOnArtifact(materialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            context.LogImportWarning("Missing " + materialPath + "; model imported without its material.");
            return;
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = material;
        if (IsNestV2)
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == NestV2Mound)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
    }
}
