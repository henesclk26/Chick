using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws a white outline around the food the player's next peck would take. The outline is a shell of the food's own
/// meshes with the Chick/FoodOutline material, parented under its visual so it follows contact assist and disappears
/// with the food when it is eaten.
/// </summary>
[DisallowMultipleComponent]
public sealed class FoodTargetHighlight : MonoBehaviour
{
    private const string MaterialResource = "FoodOutline";
    private static Material outline;

    private readonly List<GameObject> shells = new List<GameObject>();
    private ChickEatingController eating;
    private EdibleObject current;

    private void Awake()
    {
        eating = GetComponent<ChickEatingController>();
        if (outline == null) outline = Resources.Load<Material>(MaterialResource);
    }

    private void LateUpdate()
    {
        EdibleObject next = eating != null && outline != null ? eating.PeekTarget() : null;
        // shortcut: seeds only while the look is being tested; drop this line to outline every food.
        if (next != null && next.Category != EdibleCategory.Seed) next = null;
        if (next == current) return;
        Clear();
        current = next;
        if (current != null) Build(current.ContactVisual);
    }

    private void OnDisable()
    {
        Clear();
        current = null;
    }

    private void Build(Transform visual)
    {
        foreach (var source in visual.GetComponentsInChildren<Renderer>())
        {
            var shell = new GameObject("FoodOutline") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
            shell.transform.SetParent(source.transform, false);
            Renderer renderer;
            Mesh mesh;
            if (source is SkinnedMeshRenderer skinned)
            {
                var copy = shell.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = mesh = skinned.sharedMesh;
                copy.bones = skinned.bones;
                copy.rootBone = skinned.rootBone;
                renderer = copy;
            }
            else
            {
                var filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) { Destroy(shell); continue; }
                shell.AddComponent<MeshFilter>().sharedMesh = mesh = filter.sharedMesh;
                renderer = shell.AddComponent<MeshRenderer>();
            }
            if (mesh == null) { Destroy(shell); continue; }
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = outline;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            shells.Add(shell);
        }
    }

    private void Clear()
    {
        foreach (var shell in shells)
            if (shell != null) Destroy(shell);
        shells.Clear();
    }
}
