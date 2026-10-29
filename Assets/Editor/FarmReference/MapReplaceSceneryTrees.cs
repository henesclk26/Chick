using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Takes the last of the old low-poly scenery trees (Scenery Tree, Hill Tree and Forest Tree, the swaying BOKI pines
/// and round trees) off the map and stands a light tree from the Low Poly Forest pack where each one stood: a fir for
/// a pine (now and then an IL3DN pine), SM_Tree_Color for a round one, sized to the tree it replaces. These are a
/// fraction of the cost of the new tree designs (whose many cut-out leaf cards are what slows the wood by the barn
/// down), so the hills stay cheap. The ones MapNewTrees already swapped (switched off) are simply deleted. Running it
/// again swaps the trees it placed before, in the same spots. The new trees are under Map/Trees/Scenery Replacements.
/// Menu; Ctrl+Z undoes it.
/// </summary>
public static class MapReplaceSceneryTrees
{
    private const string GroupName = "Scenery Replacements";
    private static readonly string[] OldNames = { "Scenery Tree", "Hill Tree", "Forest Tree" };
    private static readonly (string tree, int weight)[] Pines = { ("Fir", 2), ("IL3DN_Tree_Pine_01_OneMesh", 1) };
    private static readonly (string tree, int weight)[] Broadleaf = { ("SM_Tree_Color", 1) };
    private const float MaxPineStretch = 1.3f;
    private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
    {
        { "Fir", "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/Fir.prefab" },
        { "IL3DN_Tree_Pine_01_OneMesh", "Assets/IL3DN/Graphics/Prefabs/IL3DN_Tree_Pine_01_OneMesh.prefab" },
        { "SM_Tree_Color", "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Tree_Color.prefab" },
    };
    // the tree designs an earlier run placed, told apart by name
    private static readonly string[] PineNames = { "Fir", "Pine" };

    [MenuItem("Tools/Chick/Trees/Replace Old Scenery Trees With New Designs")]
    public static string Run()
    {
        var report = new StringBuilder();
        GameObject map = GameObject.Find("Map");
        Transform trees = map != null ? map.transform.Find("Trees") : null;
        if (GameObject.Find("Farm Reference Map") == null || trees == null) return "The open scene has no 'Farm Reference Map' / 'Map/Trees'.";

        var prefabs = new Dictionary<string, (GameObject prefab, float height)>();
        foreach (string name in Pines.Concat(Broadleaf).Select(k => k.tree).Distinct())
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Paths[name]);
            if (prefab == null) return "missing prefab " + name;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            prefabs[name] = (prefab, Measure(probe).size.y);
            Object.DestroyImmediate(probe);
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Replace old scenery trees");
        int undo = Undo.GetCurrentGroup();

        var old = trees.GetComponentsInChildren<Transform>(true)
            .Where(t => t != trees && OldNames.Any(n => t.name.StartsWith(n + " ")))
            .OrderBy(t => t.position.x).ThenBy(t => t.position.z).ToList();
        // spots to fill: each old tree still showing, and each tree an earlier run put in (height as it stands)
        var spots = new List<(Vector3 at, float height, bool pine)>();
        foreach (Transform t in old)
        {
            var filter = t.GetComponent<MeshFilter>();
            if (t.gameObject.activeInHierarchy && filter != null && filter.sharedMesh != null)
                spots.Add((t.position, Measure(t.gameObject).size.y, filter.sharedMesh.name.Contains("Pine")));
        }
        var group = trees.Find(GroupName);
        if (group != null)
        {
            foreach (Transform t in group.Cast<Transform>().ToList())
            {
                spots.Add((t.position, Measure(t.gameObject).size.y, PineNames.Any(n => t.name.Contains(n))));
                Undo.DestroyObjectImmediate(t.gameObject);
            }
        }
        else
        {
            group = new GameObject(GroupName).transform;
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Replace old scenery trees");
            group.SetParent(trees, false);
        }

        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();
        var rng = new System.Random(2910);
        var counts = new Dictionary<string, int>();
        foreach (Transform t in old) Undo.DestroyObjectImmediate(t.gameObject);
        foreach (var (p, height, pine) in spots.OrderBy(s => s.at.x).ThenBy(s => s.at.z))
        {
            string kind = Pick(rng, pine ? Pines : Broadleaf);
            // the IL3DN pine turns lanky when stretched, so the tall spots always get a fir
            if (kind == "IL3DN_Tree_Pine_01_OneMesh" && height > prefabs[kind].height*MaxPineStretch) kind = "Fir";
            var pf = prefabs[kind];
            // about as tall as the tree it stands in for, never tiny or huge
            float scale = Mathf.Clamp(height/pf.height, .4f, 1.8f)*Mathf.Lerp(.93f, 1.07f, (float)rng.NextDouble());
            // on the ground, or as deep as the old one was sunk into a slope
            float y = Mathf.Min(p.y, terrain.SampleHeight(p) + terrain.transform.position.y - .05f);
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(pf.prefab, group);
            Undo.RegisterCreatedObjectUndo(tree, "Replace old scenery trees");
            tree.name = kind;
            tree.transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
            tree.transform.localScale = Vector3.one*scale;
            counts.TryGetValue(kind, out int c);
            counts[kind] = c + 1;
        }

        // the old trees' own group, now empty
        var oldGroup = trees.Find("Scenery Tree");
        if (oldGroup != null && oldGroup.childCount == 0) Undo.DestroyObjectImmediate(oldGroup.gameObject);

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        report.AppendLine($"placed {spots.Count} trees, deleted {old.Count} old ones");
        foreach (var k in counts.OrderByDescending(k => k.Value)) report.AppendLine($"  {k.Value,3} x {k.Key}");
        report.Append("save the scene to keep it (Ctrl+Z undoes it)");
        Debug.Log("Replace old scenery trees: " + report);
        return report.ToString();
    }

    private static string Pick(System.Random rng, (string tree, int weight)[] kinds)
    {
        int roll = rng.Next(kinds.Sum(k => k.weight));
        foreach (var k in kinds) { if (roll < k.weight) return k.tree; roll -= k.weight; }
        return kinds[0].tree;
    }

    private static Bounds Measure(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
