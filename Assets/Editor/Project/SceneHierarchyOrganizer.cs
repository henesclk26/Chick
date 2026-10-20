using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

/// <summary>
/// Tidies the open scene's hierarchy into a few top groups, by what things are:
/// - Systems (camera, lights, volumes, day cycle), Player, UI;
/// - Map: the "Farm Reference Map" (its farm groups, terrain and animals stay as they are) next to one group per kind
///   of thing: Trees, Bushes, Grass, Flowers, Rocks, Mushrooms, Stumps and Logs, Water Plants, Garden Plants, Props
///   and Buildings. Inside each, things of the same kind sit together ("Trees/Scenery Tree", "Trees/Hill Tree", ...).
///   They come from the loose scene roots and from the map's nature groups (05 Nature and Scenery, 09 Small Stones,
///   the natural part of 10 Farm Dressing, the planted vegetation and the point grass); groups that hold one kind
///   move as a whole.
/// - Food & Test Areas: food lying around and the test patches.
/// Only parents change, every object keeps its place in the world. Edible food keeps its save id: ids made from the
/// hierarchy path are written into the food first, so a save made before still knows what was eaten.
/// Undo (Ctrl+Z) puts it back.
/// </summary>
public static class SceneHierarchyOrganizer
{
    private static readonly string[] Kinds =
        { "Trees", "Bushes", "Grass", "Flowers", "Rocks", "Mushrooms", "Stumps and Logs", "Water Plants", "Garden Plants", "Props and Buildings" };

    [MenuItem("Tools/Chick/Scene/Organize Hierarchy")]
    private static void Organize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Hierarchy", "Exit play mode first.", "OK"); return; }
        var scene = EditorSceneManager.GetActiveScene();
        var farm = GameObject.Find("Farm Reference Map");
        if (farm == null) { EditorUtility.DisplayDialog("Hierarchy", "The open scene has no 'Farm Reference Map'.", "OK"); return; }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Organize hierarchy");
        int undo = Undo.GetCurrentGroup();
        var moved = new Dictionary<string, int>();

        int ids = BakeEdibleIds(scene);

        Transform systems = Root(scene, "Systems"), player = Root(scene, "Player"), ui = Root(scene, "UI"),
                  map = Root(scene, "Map"), food = Root(scene, "Food & Test Areas");
        var tops = new HashSet<Transform> { systems, player, ui, map, food };
        Transform Kind(string kind) => Child(map, kind);
        void Move(Transform t, Transform parent, string what)
        {
            if (t.parent == parent) return;
            Undo.SetTransformParent(t, parent, true, "Organize hierarchy");
            moved[what] = moved.TryGetValue(what, out int n) ? n + 1 : 1;
        }

        // Loose things at the top of the scene.
        foreach (var go in scene.GetRootGameObjects())
        {
            Transform t = go.transform;
            if (tops.Contains(t)) continue;
            if (go == farm) { Move(t, map, "Map"); continue; }
            if (go.GetComponentInChildren<ChickPlayerController>(true) != null) { Move(t, player, "Player"); continue; }
            if (go.GetComponentInChildren<UIDocument>(true) != null || go.GetComponentInChildren<Canvas>(true) != null ||
                go.name.Contains("HUD") || go.name.Contains("UI")) { Move(t, ui, "UI"); continue; }
            if (go.GetComponentInChildren<Camera>(true) != null || go.GetComponentInChildren<Light>(true) != null ||
                go.GetComponentInChildren<Volume>(true) != null || go.name.Contains("Day Systems") ||
                go.GetComponentsInChildren<Renderer>(true).Length == 0) { Move(t, systems, "Systems"); continue; }
            string rootKind = Classify(t);
            if (rootKind == "Garden Plants") { Move(t, Child(Kind(rootKind), Family(t.name)), rootKind); continue; }
            if (IsFoodOrTest(go))
            {
                Move(t, Child(food, go.transform.position.z < -500f ? "Test Area" : "On The Map"), "Food & Test Areas");
                continue;
            }
            string kind = rootKind ?? "Props and Buildings";
            Move(t, Child(Kind(kind), Family(t.name)), kind);
        }

        // The map's own nature: whole groups of one kind move as they are, mixed groups are sorted item by item.
        void Sort(Transform group)
        {
            foreach (Transform t in group.Cast<Transform>().ToList())
            {
                string kind = KindOfGroup(t);
                if (kind == "mixed" && t.childCount > 0 && !IsLockedPrefabPart(t.GetChild(0))) { Sort(t); continue; }
                if (kind == null || kind == "mixed") continue;
                Transform target = Kind(kind);
                Move(t, t.childCount > 6 ? target : Child(target, Family(t.name)), kind);
            }
        }
        foreach (string name in new[] { "05 Nature and Scenery", "09 Small Stones" })
        {
            Transform g = farm.transform.Find(name);
            if (g == null) continue;
            if (name == "09 Small Stones") { Rename(g, "Small Stones"); Move(g, Kind("Rocks"), "Rocks"); continue; }
            Sort(g);
        }
        Transform dressing = farm.transform.Find("10 Farm Dressing");
        if (dressing != null)
            foreach (string name in new[] { "Trees", "Stumps and Logs", "Rocks", "Undergrowth", "Flowers", "Sunflowers", "Mushrooms", "IL3DN Grass" })
            {
                Transform g = dressing.Find(name);
                if (g == null) continue;
                string kind = name == "Undergrowth" ? "Bushes" : name == "IL3DN Grass" ? "Grass" : name == "Sunflowers" ? "Flowers" : name;
                Rename(g, "Farm " + name);                   // "Trees/Farm Trees", not "Trees/Trees"
                Move(g, Kind(kind), kind);
            }
        foreach (string name in new[] { "11 Planted Vegetation", "Vegetation Spawner", "Vegetation Exclusion Zones", "Fruit Tree Samples" })
        {
            Transform g = farm.transform.Find(name);
            if (g != null) Move(g, Kind("Trees"), "Trees");
        }
        Transform pointGrass = farm.transform.Find("Chicken Yard Point Grass");
        if (pointGrass != null) Move(pointGrass, Kind("Grass"), "Grass");

        // Groups left empty go; the kinds keep a fixed order.
        foreach (string name in new[] { "05 Nature and Scenery", "10 Farm Dressing" })
        {
            Transform g = farm.transform.Find(name);
            if (g != null && g.GetComponentsInChildren<Transform>(true).Length == 1) Undo.DestroyObjectImmediate(g.gameObject);
        }
        int order = 0;
        foreach (var t in new[] { systems, player, ui, map, food }) t.SetSiblingIndex(order++);
        farm.transform.SetSiblingIndex(0);
        for (int i = 0; i < Kinds.Length; i++)
        {
            Transform k = map.Find(Kinds[i]);
            if (k == null) continue;
            if (k.childCount == 0) { Undo.DestroyObjectImmediate(k.gameObject); continue; }
            k.SetSiblingIndex(i + 1);
            SortChildren(k);
        }
        foreach (var t in new[] { systems, player, ui, food }) if (t.childCount == 0) Undo.DestroyObjectImmediate(t.gameObject);

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Hierarchy organized ({ids} food save ids kept): " +
                  string.Join(", ", moved.OrderByDescending(m => m.Value).Select(m => $"{m.Key} {m.Value}")) +
                  ". Save the scene to keep it (Ctrl+Z undoes it).");
    }

    // Food without a stored save id is known by its hierarchy path; store that path before the path changes.
    private static int BakeEdibleIds(UnityEngine.SceneManagement.Scene scene)
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var edible in root.GetComponentsInChildren<EdibleObject>(true))
            {
                var so = new SerializedObject(edible);
                var id = so.FindProperty("persistentId");
                if (id == null || !string.IsNullOrEmpty(id.stringValue)) continue;
                id.stringValue = edible.PersistentId;
                so.ApplyModifiedProperties();
                count++;
            }
        return count;
    }

    private static void Rename(Transform t, string name)
    {
        if (t.name == name) return;
        Undo.RecordObject(t.gameObject, "Organize hierarchy");
        t.name = name;
    }

    private static bool IsFoodOrTest(GameObject go)
    {
        string n = go.name.ToLowerInvariant();
        return go.GetComponentInChildren<EdibleObject>(true) != null || n.Contains("patch") || n.Contains("test") ||
               n.StartsWith("spawn") || n == "zemin" || go.transform.position.z < -500f;
    }

    private static bool IsLockedPrefabPart(Transform t) =>
        PrefabUtility.IsPartOfPrefabInstance(t) && !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject);

    // The one kind every item of a group belongs to, "mixed", or null when nothing in it is recognised.
    private static string KindOfGroup(Transform t)
    {
        if (t.childCount == 0 || IsLockedPrefabPart(t.GetChild(0)) || t.GetComponent<Renderer>() != null) return Classify(t);
        string own = Classify(t);
        if (own != null && t.childCount > 6) return own;          // "IL3DN Grass", "Exterior Grass Patches (290)", ...
        var kinds = t.Cast<Transform>().Select(KindOfGroup).Distinct().ToList();
        return kinds.Count == 1 ? kinds[0] : kinds.All(k => k == null) ? null : "mixed";
    }

    // What a thing is, from its name and the prefab or model it comes from.
    private static string Classify(Transform t)
    {
        string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) ?? "";
        var filter = t.GetComponentInChildren<MeshFilter>(true);
        if (filter != null && filter.sharedMesh != null) source += " " + AssetDatabase.GetAssetPath(filter.sharedMesh);
        string s = (t.name + " " + System.IO.Path.GetFileNameWithoutExtension(source)).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(s.Contains);
        if (Has("wall", "fence", "gate", "sign", "bld_", "building", "barn", "coop", "house", "shed", "veh_", "tractor", "pickup",
                "prop_", "mail", "well", "windmill", "barrel", "drum", "tool", "wheelbarrow", "weathervane", "bench", "crate", "hay", "bale"))
            return "Props and Buildings";
        if (Has("lily", "reed", "cattail")) return "Water Plants";
        if (Has("strawberry", "blackberry", "pepper", "grape", "tomato", "lettuce")) return "Garden Plants";
        if (Has("mushroom")) return "Mushrooms";
        if (Has("stump", "log", "branch", "fallen")) return "Stumps and Logs";
        if (Has("flower", "rose", "daisy", "poppy", "clover", "buttercup", "bellflower", "sunflower", "tulip")) return "Flowers";
        if (Has("tree", "fir", "pine", "oak", "birch", "willow", "orchard", "spruce", "palm")) return "Trees";
        if (Has("bush", "shrub", "thorn", "hedge", "undergrowth", "fern")) return "Bushes";
        if (Has("grass", "meadow", "il3dn", "tuft")) return "Grass";
        if (Has("rock", "stone", "boulder", "pebble", "cliff")) return "Rocks";
        return null;
    }

    // "Scenery Tree 12" -> "Scenery Tree", "BlackberryBush (3)" -> "BlackberryBush", "Flower Border 5-8" -> "Flower Border".
    private static string Family(string name)
    {
        string family = Regex.Replace(name, @"[\s_\-]*(\(\d+\)|\d+)([\s_\-]*\d+)*$", "").Trim();
        return family.Length > 0 ? family : name;
    }

    private static Transform Root(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects()) if (go.name == name && go.transform.childCount >= 0 && IsGroup(go)) return go.transform;
        var created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Organize hierarchy");
        return created.transform;
    }

    private static bool IsGroup(GameObject go) => go.GetComponents<Component>().Length == 1;   // a bare Transform

    private static Transform Child(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) return found;
        var created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Organize hierarchy");
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static void SortChildren(Transform parent)
    {
        var children = parent.Cast<Transform>().OrderBy(c => c.name, System.StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < children.Count; i++) children[i].SetSiblingIndex(i);
    }
}
