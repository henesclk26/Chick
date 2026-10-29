using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Scatters the leafy bush (SM_FoliageBush_01) where bushes grow: at the foot of the trees and beside the rocks around
/// the farm, and inside the outer fence along it (never at a gate). Each one is its own size (<see cref="MinSize"/> to
/// <see cref="MaxSize"/>, a little taller or squatter) and turned its own way, on open ground: no paths or fields, no
/// pond, nothing standing there, no flower. All of them, and the bushes already on the map, wear
/// M_FoliageBush_GroundTint (Chick/InteractiveFoliage with the ground tint, as the grass: green over grass, golden over
/// earth; the bird pushes through it and the part it touches gives way). Running it again places them afresh. The new
/// bushes are under Map/Bushes/Placed Bushes. Menu; Ctrl+Z undoes it.
/// </summary>
public static class MapBushes
{
    private const string PrefabPath = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_FoliageBush_01.prefab";
    private const string MaterialPath = "Assets/Art/FarmReference/Foliage/M_FoliageBush_GroundTint.mat";
    private const string GroupName = "Placed Bushes";
    // the outer fence (as FixedTreeDetail)
    private static readonly Rect PlayArea = Rect.MinMaxRect(59f, -48f, 129f, 36f);
    private const float MinSize = .55f, MaxSize = 1.3f, Spacing = 3.2f, OutsideReach = 45f, MaxSlope = 30f;
    private const int AtTrees = 45, AtRocks = 20, ByFence = 25;

    [MenuItem("Tools/Chick/Foliage/Scatter Bushes")]
    public static string Run()
    {
        GameObject map = GameObject.Find("Map");
        if (GameObject.Find("Farm Reference Map") == null || map == null) return "The open scene has no 'Farm Reference Map' / 'Map'.";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (prefab == null || material == null) return "missing " + PrefabPath + " or " + MaterialPath;
        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();
        var ground = new Ground(terrain);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Scatter bushes");
        int undo = Undo.GetCurrentGroup();
        Transform bushes = map.transform.Find("Bushes");
        if (bushes == null)
        {
            bushes = new GameObject("Bushes").transform;
            Undo.RegisterCreatedObjectUndo(bushes.gameObject, "Scatter bushes");
            bushes.SetParent(map.transform, false);
        }
        Transform group = bushes.Find(GroupName);
        if (group != null) Undo.DestroyObjectImmediate(group.gameObject);
        group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Scatter bushes");
        group.SetParent(bushes, false);

        // what to keep clear of
        var taken = bushes.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds.center).ToList();
        var flowers = new List<Vector3>();
        var flowerRoot = GameObject.Find("Farm Flowers (V2)");
        if (flowerRoot != null) foreach (Transform f in flowerRoot.transform) flowers.Add(f.position);
        var gates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.Contains("Gate")).Select(t => t.position).ToList();
        var pond = GameObject.Find("Farm Pond");
        Bounds? pondBounds = null;
        if (pond != null)
            foreach (var r in pond.GetComponentsInChildren<Renderer>())
            {
                if (pondBounds == null) pondBounds = r.bounds;
                else { var b = pondBounds.Value; b.Encapsulate(r.bounds); pondBounds = b; }
            }
        int solid = ~LayerMask.GetMask("Ignore Raycast");

        var rng = new System.Random(2911);
        bool Open(Vector3 p, bool inside)
        {
            if (!ground.Inside(p, 3f) || ground.Steepness(p) > MaxSlope || ground.EarthWeight(p) > .2f) return false;
            bool inArea = PlayArea.Contains(new Vector2(p.x, p.z));
            if (inArea != inside) return false;
            if (taken.Any(q => Flat(q - p) < Spacing) || flowers.Any(q => Flat(q - p) < 1.3f) || gates.Any(q => Flat(q - p) < 5f)) return false;
            if (pondBounds != null && p.x > pondBounds.Value.min.x - 2f && p.x < pondBounds.Value.max.x + 2f && p.z > pondBounds.Value.min.z - 2f && p.z < pondBounds.Value.max.z + 2f) return false;
            Vector3 foot = ground.Point(p);
            return !Physics.OverlapCapsule(foot + Vector3.up*.35f, foot + Vector3.up*1.2f, .8f, solid, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider));
        }
        int placed = 0;
        void Plant(Vector3 p)
        {
            var bush = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            Undo.RegisterCreatedObjectUndo(bush, "Scatter bushes");
            bush.name = "SM_FoliageBush_01";
            // more of them middling than very small or very big
            float t = (float)(rng.NextDouble() + rng.NextDouble())*.5f;
            float size = Mathf.Lerp(MinSize, MaxSize, t), tall = Mathf.Lerp(.85f, 1.15f, (float)rng.NextDouble());
            bush.transform.SetPositionAndRotation(ground.Point(p) - Vector3.up*.05f, Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
            bush.transform.localScale = new Vector3(size, size*tall, size);
            foreach (var r in bush.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = material;
            taken.Add(p);
            placed++;
        }
        Vector3 Around(Vector3 c, float from, float to)
        {
            float a = (float)(rng.NextDouble()*Mathf.PI*2.0), d = Mathf.Lerp(from, to, (float)rng.NextDouble());
            return c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))*d;
        }

        // at the foot of trees around the farm
        Transform trees = map.transform.Find("Trees");
        var treeSpots = trees == null ? new List<Vector3>() : trees.GetComponentsInChildren<Transform>()
            .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
            .Select(t => t.position).Where(p => !PlayArea.Contains(new Vector2(p.x, p.z)) && Distance(p) < OutsideReach)
            .OrderBy(_ => rng.Next()).ToList();
        int atTrees = 0;
        foreach (var c in treeSpots)
        {
            if (atTrees >= AtTrees) break;
            int clump = rng.NextDouble() < .35 ? 2 : 1;
            for (int i = 0, tries = 0; i < clump && tries < 12; tries++)
            {
                var p = Around(c, 1.8f, 3.5f);
                if (!Open(p, false)) continue;
                Plant(p); atTrees++; i++;
            }
        }

        // beside rocks
        var rocks = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.name.Contains("Rock") && r.bounds.size.y > .5f && r.bounds.size.x < 6f)
            .Select(r => r.bounds).Where(b => Distance(b.center) < OutsideReach).OrderBy(_ => rng.Next()).ToList();
        int atRocks = 0;
        foreach (var b in rocks)
        {
            if (atRocks >= AtRocks) break;
            for (int tries = 0; tries < 8; tries++)
            {
                var p = Around(b.center, Mathf.Max(b.extents.x, b.extents.z) + .9f, Mathf.Max(b.extents.x, b.extents.z) + 2f);
                if (!Open(p, !(Distance(b.center) > 0f))) continue;
                Plant(p); atRocks++;
                break;
            }
        }

        // inside the outer fence, along it
        var edge = new List<Vector3>();
        for (float x = PlayArea.xMin + 3f; x < PlayArea.xMax - 3f; x += 5f) { edge.Add(new Vector3(x, 0f, PlayArea.yMin)); edge.Add(new Vector3(x, 0f, PlayArea.yMax)); }
        for (float z = PlayArea.yMin + 3f; z < PlayArea.yMax - 3f; z += 5f) { edge.Add(new Vector3(PlayArea.xMin, 0f, z)); edge.Add(new Vector3(PlayArea.xMax, 0f, z)); }
        var centre = new Vector3(PlayArea.center.x, 0f, PlayArea.center.y);
        int byFence = 0;
        foreach (var e in edge.OrderBy(_ => rng.Next()))
        {
            if (byFence >= ByFence) break;
            var inward = (centre - e);
            inward = Mathf.Abs(e.x - PlayArea.xMin) < .1f || Mathf.Abs(e.x - PlayArea.xMax) < .1f ? new Vector3(Mathf.Sign(inward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(inward.z));
            var p = e + inward*Mathf.Lerp(1.6f, 2.6f, (float)rng.NextDouble()) + Vector3.Cross(inward, Vector3.up)*Mathf.Lerp(-1.5f, 1.5f, (float)rng.NextDouble());
            if (!Open(p, true)) continue;
            Plant(p); byFence++;
        }

        // the bushes that were already on the map take the ground tint too
        int retinted = 0;
        foreach (var r in bushes.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.transform.IsChildOf(group) || r.sharedMaterial == material) continue;
            if (!r.name.StartsWith("SM_FoliageBush")) continue;
            Undo.RecordObject(r, "Scatter bushes");
            r.sharedMaterial = material;
            retinted++;
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        string report = $"placed {placed} bushes: {atTrees} at trees, {atRocks} by rocks, {byFence} along the fence; {retinted} existing bushes retinted";
        Debug.Log("Scatter bushes: " + report);
        return report;
    }

    // across the ground from the play area (0 inside it)
    private static float Distance(Vector3 p)
    {
        float dx = Mathf.Max(PlayArea.xMin - p.x, 0f, p.x - PlayArea.xMax);
        float dz = Mathf.Max(PlayArea.yMin - p.z, 0f, p.z - PlayArea.yMax);
        return Mathf.Sqrt(dx*dx + dz*dz);
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private sealed class Ground
    {
        private readonly Terrain terrain;
        private readonly TerrainData data;
        private readonly float[,,] alpha;
        private readonly bool[] earth;

        public Ground(Terrain terrain)
        {
            this.terrain = terrain;
            data = terrain.terrainData;
            alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            earth = data.terrainLayers.Select(l => l != null && (l.name.Contains("Trodden") || l.name.Contains("Path") || l.name.Contains("Tilled"))).ToArray();
        }

        private Vector3 Origin => terrain.transform.position;
        private Vector2 Uv(Vector3 p) => new Vector2((p.x - Origin.x)/data.size.x, (p.z - Origin.z)/data.size.z);
        public bool Inside(Vector3 p, float margin) =>
            p.x > Origin.x + margin && p.x < Origin.x + data.size.x - margin && p.z > Origin.z + margin && p.z < Origin.z + data.size.z - margin;
        public Vector3 Point(Vector3 p) => new Vector3(p.x, terrain.SampleHeight(p) + Origin.y, p.z);
        public float Steepness(Vector3 p) { var uv = Uv(p); return data.GetSteepness(uv.x, uv.y); }
        public float EarthWeight(Vector3 p)
        {
            var uv = Uv(p);
            int x = Mathf.Clamp(Mathf.RoundToInt(uv.x*data.alphamapWidth), 0, data.alphamapWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(uv.y*data.alphamapHeight), 0, data.alphamapHeight - 1);
            float w = 0f;
            for (int l = 0; l < earth.Length; l++) if (earth[l]) w += alpha[y, x, l];
            return w;
        }
    }
}
