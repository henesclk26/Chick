using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds <see cref="StrawberryPlant"/> to the StrawberryPlant model on import, so every plant placed from the
/// model or its prefab variant gets edible, droppable berries and solid pot, leaves and berries without per-instance setup.
/// </summary>
public sealed class StrawberryPlantImporter : AssetPostprocessor
{
    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.EndsWith("/StrawberryPlant.fbx", StringComparison.OrdinalIgnoreCase)) return;
        if (root.GetComponent<StrawberryPlant>() == null) root.AddComponent<StrawberryPlant>();
        // Solid pot, leaves and berries so characters bump into them; stems and flowers stay passable.
        // They stay on the model's layer, so eating (Edible layer only) and the camera (CameraBlocker) ignore them.
        AddHull(root.transform.Find("Pot"));
        AddHull(root.transform.Find("Leaves"));
        foreach (Transform child in root.transform)
        {
            // A sphere per berry: it keeps fitting while a dropped berry tumbles, and moves with it.
            if (!child.name.StartsWith("Strawberry_") || child.GetComponent<Collider>() != null ||
                !child.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            var sphere = child.gameObject.AddComponent<SphereCollider>();
            sphere.center = bounds.center;
            sphere.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * .9f;
        }
    }

    private static void AddHull(Transform part)
    {
        if (part == null || part.GetComponent<Collider>() != null || !part.TryGetComponent(out MeshFilter filter)) return;
        var collider = part.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = filter.sharedMesh;
        collider.convex = true;
    }
}
