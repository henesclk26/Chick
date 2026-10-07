using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds <see cref="BerryPlant"/> to the berry plant models on import (StrawberryPlant, BlackberryBush,
/// StrawberryBush, TomatoPlant), so every
/// plant placed from a model or its prefab variant gets edible, droppable berries and a solid base, leaves and
/// berries without per-instance setup.
/// </summary>
public sealed class BerryPlantImporter : AssetPostprocessor
{
    // Bump when the setup below changes, so Unity reimports the plant models.
    public override uint GetVersion() => 5;

    private void OnPostprocessModel(GameObject root)
    {
        if (assetPath.EndsWith("/StrawberryPlant.fbx", StringComparison.OrdinalIgnoreCase))
        {
            // Defaults are the strawberry's.
            if (root.GetComponent<BerryPlant>() == null) root.AddComponent<BerryPlant>();
            SetUpColliders(root, "Strawberry_", "Pot");
        }
        else if (assetPath.EndsWith("/BlackberryBush.fbx", StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetComponent(out BerryPlant plant)) plant = root.AddComponent<BerryPlant>();
            // ~1.3 m bush on a rocky mound (~0.55 m radius): reach in from outside the leaves, drop clear of the rocks.
            plant.Configure("Blackberry_", "blackberry", "Böğürtlen", .85f, .68f);
            SetUpColliders(root, "Blackberry_", "Mound");
        }
        else if (assetPath.EndsWith("/StrawberryBush.fbx", StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetComponent(out BerryPlant plant)) plant = root.AddComponent<BerryPlant>();
            // Same ~1.3 m bush on a rocky mound as the blackberry, so the same reach and drop distance.
            plant.Configure("Strawberry_", "strawberry", "Çilek", .85f, .68f);
            SetUpColliders(root, "Strawberry_", "Mound");
        }
        else if (assetPath.EndsWith("/TomatoPlant.fbx", StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetComponent(out BerryPlant plant)) plant = root.AddComponent<BerryPlant>();
            // Bush-sized (~1.4 m wide); whole tomatoes, counted apart from the seeds of the tomato slices.
            plant.Configure("Tomato_", "tomato_fruit", "Domates", .85f, .68f);
            SetUpColliders(root, "Tomato_", "Mound");
        }
    }

    // Solid base, leaves and berries so characters bump into them; stems/canes and flowers stay passable.
    // They stay on the model's layer, so eating (Edible layer only) and the camera (CameraBlocker) ignore them.
    private static void SetUpColliders(GameObject root, string berryPrefix, string baseName)
    {
        AddHull(root.transform.Find(baseName));
        AddHull(root.transform.Find("Leaves"));
        foreach (Transform child in root.transform)
        {
            // A sphere per berry: it keeps fitting while a dropped berry tumbles, and moves with it.
            if (!child.name.StartsWith(berryPrefix) || child.GetComponent<Collider>() != null ||
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
