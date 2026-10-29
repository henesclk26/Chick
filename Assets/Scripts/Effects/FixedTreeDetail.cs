using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Frame-rate help for the woods around the farm: the trees' generated Mesh LODs, picked once per tree and then held.
/// Left to itself Unity swaps a tree's LOD as the camera moves, and on these alpha-cut leaves a swap shows as branches
/// vanishing and coming back. The bird and its camera never leave the outer fence (<see cref="playArea"/>), so how
/// close a tree can ever be seen from is known up front: trees in or near the farm keep full detail, trees further
/// out get a lighter LOD (<see cref="bandStarts"/>), and none of them ever changes while playing. The generated
/// LODs thin a crown by dropping whole leaf cards (a broad-leaf tree is bare by LOD 2), so a tree only goes as low
/// as keeps <see cref="minLeafShare"/> of its leaves: in practice the pines lose a step, the leafy trees none.
/// Trees deep in the woods (<see cref="shadowlessFrom"/>) also stop casting shadows, which land behind the nearer trees.
/// Every renderer whose mesh has generated LODs is covered (the fruit trees, the new tree designs). Trees that come
/// as LOD groups (the firs, the IL3DN pines, SM_Tree_Color) are held too: each at the level Unity would give it from
/// the nearest point of the play area, so it never shows less than it would up close and never switches. LOD groups
/// smaller than <see cref="minGroupSize"/> (the grass) and outside Map/Trees (the distant mountain) are left alone.
/// It runs in the Editor too, so the Scene view shows the woods as the game does instead of trees shedding their
/// leaves with distance; the shadows are only changed in play. Restored to Unity's own choice when disabled.
/// Presentation only.
/// </summary>
[ExecuteAlways]
public sealed class FixedTreeDetail : MonoBehaviour
{
    [SerializeField, Tooltip("World X / Z extent the bird and its camera stay in (the outer fence): x, y = min X, min Z; width, height = size.")]
    private Rect playArea = Rect.MinMaxRect(59f, -48f, 129f, 36f);
    [SerializeField, Tooltip("A tree whose nearest side is at least this far (m) from the play area drops to LOD 1, 2, 3...")]
    private float[] bandStarts = { 15f, 40f, 70f };
    [SerializeField, Range(0f, 1f), Tooltip("A lighter LOD is only used while its leaves (alpha-cut submeshes) keep this share of the full tree's.")]
    private float minLeafShare = .45f;
    [SerializeField, Min(0f), Tooltip("Trees this far (m) from the play area cast no shadow: it falls deep in the woods, behind the nearer trees.")]
    private float shadowlessFrom = 25f;
    [SerializeField, Min(0f), Tooltip("LOD groups at least this tall (m) count as trees and are held; smaller ones (grass) are not.")]
    private float minGroupSize = 3f;

    private readonly List<Renderer> pinned = new List<Renderer>();
    private readonly List<LODGroup> heldGroups = new List<LODGroup>();
    private readonly List<Renderer> unshadowed = new List<Renderer>();
    private readonly Dictionary<Mesh, int> lightest = new Dictionary<Mesh, int>();

    private void OnEnable()
    {
        pinned.Clear();
        heldGroups.Clear();
        unshadowed.Clear();
        lightest.Clear();
        var inLod = new HashSet<Renderer>();
        var cam = Camera.main;
        float viewHeight = 2f*Mathf.Tan((cam != null ? cam.fieldOfView : 58f)*.5f*Mathf.Deg2Rad);
        foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var lods = group.GetLODs();
            foreach (var lod in lods)
                foreach (var r in lod.renderers) if (r != null) inLod.Add(r);

            var scale = group.transform.lossyScale;
            float size = group.size*Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (size < minGroupSize || lods.Length < 2 || !group.enabled || !group.gameObject.activeInHierarchy || !InTrees(group.transform)) continue;
            // as Unity picks it: on-screen height share (with the LOD bias) against each level's threshold, never culled
            Vector3 centre = group.transform.TransformPoint(group.localReferencePoint);
            float distance = Distance(new Bounds(centre, Vector3.zero));
            float share = size/(Mathf.Max(distance, 1f)*viewHeight)*QualitySettings.lodBias;
            int level = 0;
            while (level < lods.Length - 1 && share < lods[level].screenRelativeTransitionHeight) level++;
            group.ForceLOD(level);
            heldGroups.Add(group);
            if (distance < shadowlessFrom || !Application.isPlaying) continue;
            foreach (var lod in lods)
                foreach (var r in lod.renderers)
                    if (r != null && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On)
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        unshadowed.Add(r);
                    }
        }

        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.lodCount < 2 || inLod.Contains(r)) continue;
            float distance = Distance(r.bounds);
            r.forceMeshLod = (short)Mathf.Min(Band(distance), Lightest(mesh, r.sharedMaterials));
            pinned.Add(r);
            if (Application.isPlaying && distance >= shadowlessFrom && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                unshadowed.Add(r);
            }
        }
    }

    // the lowest LOD whose leaf submeshes all still hold minLeafShare of their full triangle count
    private int Lightest(Mesh mesh, Material[] materials)
    {
        if (lightest.TryGetValue(mesh, out int found)) return found;
        int lod = mesh.lodCount - 1;
        for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
        {
            var m = materials[s];
            if (m == null || !m.IsKeywordEnabled("_ALPHATEST_ON") && m.name.IndexOf("Leaves", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            float full = mesh.GetLod(s, 0).indexCount;
            int keep = 0;
            while (keep < lod && mesh.GetLod(s, keep + 1).indexCount >= full*minLeafShare) keep++;
            lod = keep;
        }
        lightest[mesh] = lod;
        return lod;
    }

    private void OnDisable()
    {
        foreach (var r in pinned) if (r != null) r.forceMeshLod = -1;
        foreach (var r in unshadowed) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var g in heldGroups) if (g != null) g.ForceLOD(-1);
        heldGroups.Clear();
        pinned.Clear();
        unshadowed.Clear();
    }

    private static bool InTrees(Transform t)
    {
        for (; t != null; t = t.parent) if (t.name == "Trees") return true;
        return false;
    }

    // across the ground from the play area to the nearest side of the tree
    private float Distance(Bounds b)
    {
        float dx = Mathf.Max(playArea.xMin - b.max.x, 0f, b.min.x - playArea.xMax);
        float dz = Mathf.Max(playArea.yMin - b.max.z, 0f, b.min.z - playArea.yMax);
        return Mathf.Sqrt(dx*dx + dz*dz);
    }

    private int Band(float distance)
    {
        int lod = 0;
        while (lod < bandStarts.Length && distance >= bandStarts[lod]) lod++;
        return lod;
    }
}
