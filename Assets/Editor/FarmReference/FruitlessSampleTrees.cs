using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds fruitless trees to the row of sample trees on the test plane outside the map: a fruitless apple and pear tree,
/// the apple, pear and cherry trees as first designed (OldAppleTree, OldPearTree, OldCherryTree, made without fruit), and
/// the newer designs in the same style (BlossomTree, AutumnTree, OliveTree, GoldenTree, and the fuller LushTree and
/// CrimsonTree, and three pines)
/// (the fruit trees and the oak placed by FruitTreeSamples). They are the same prefabs as the fruit trees with their
/// "Fruit_*" children switched off and their FruitTree removed in the scene (prefab overrides), so nothing ripens or
/// falls. They stand in rows beside the first, level with its trees, row after row on the side the plane
/// goes on (failing that, past either end of the row). Run again, it replaces them. Menu, or by itself when
/// Temp/FruitlessTrees/request.txt appears while the Editor is open (report and a picture in that folder).
/// </summary>
[InitializeOnLoad]
public static class FruitlessSampleTrees
{
    private const string PrefabFolder = "Assets/Art/FruitTrees/Prefabs/";
    private static readonly string[] Rows = { "AppleTree", "OrangeTree", "CherryTree", "PearTree", "OakTree" };
    private static readonly (string prefab, string name)[] Fruitless =
    {
        ("AppleTree", "AppleTree (Meyvesiz)"), ("PearTree", "PearTree (Meyvesiz)"),
        // The first designs (build_fruit_trees.py --old-fruitless), made without fruit.
        ("OldAppleTree", "OldAppleTree (Meyvesiz, eski)"), ("OldPearTree", "OldPearTree (Meyvesiz, eski)"),
        ("OldCherryTree", "OldCherryTree (Meyvesiz, eski)"),
        // Newer designs in the same style (build_fruit_trees.py --new-designs); LushTree and CrimsonTree are the fuller ones.
        ("BlossomTree", "BlossomTree (Yeni)"), ("AutumnTree", "AutumnTree (Yeni)"), ("OliveTree", "OliveTree (Yeni)"),
        ("GoldenTree", "GoldenTree (Yeni)"), ("LushTree", "LushTree (Yeni, gür)"), ("CrimsonTree", "CrimsonTree (Yeni, gür)"),
        ("PineTree", "PineTree (Çam)"), ("TallPineTree", "TallPineTree (Çam)"), ("BushyPineTree", "BushyPineTree (Çam)"),
    };
    private static bool Ours(Transform t) => Fruitless.Any(f => t.name == f.name);
    private const float Spacing = 10f;
    private const string Out = "Temp/FruitlessTrees";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static FruitlessSampleTrees() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        Place();
    }

    [MenuItem("Tools/Chick/Trees/Place Extra Sample Trees On Plane")]
    private static void Place()
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            if (GameObject.Find("Farm Reference Map") == null) { report.AppendLine("The open scene has no 'Farm Reference Map'."); return; }

            // The sample row: tree prefab instances standing on something that is not the terrain (the test plane).
            var row = new List<Transform>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) || Ours(t)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (!Rows.Any(r => path == PrefabFolder + r + ".prefab")) continue;
                Collider under = Under(t.position);
                if (under != null && !(under is TerrainCollider)) row.Add(t);
            }
            if (row.Count == 0) { report.AppendLine("No sample trees on a plane in the open scene."); return; }
            Collider plane = Under(row[0].position);
            report.AppendLine($"sample row on {plane.name}: {string.Join(", ", row.Select(t => t.name + " " + t.position))}");

            // Row direction from its two ends; spots after the end, then before the start, then a parallel row.
            Transform first = row.OrderBy(t => Vector3.Dot(t.position, row.Last().position - row.First().position)).First();
            List<Transform> ordered = row.OrderBy(t => (t.position - first.position).sqrMagnitude).ToList();
            Vector3 along = ordered.Count > 1 ? ordered.Last().position - ordered.First().position : Vector3.right;
            along.y = 0f;
            along = along.normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            // Rows beside the first, level with its trees (apple first), on whichever side the plane goes on, one row
            // after another; failing that, on past either end of the row.
            var candidates = new List<Vector3>();
            for (int k = 1; k <= 4; k++)
                foreach (float side in new[] { 1f, -1f })
                    foreach (Transform t in ordered.OrderBy(t => t.name.StartsWith("Apple") ? 0 : t.name.StartsWith("Orange") ? 1 : 2))
                        candidates.Add(t.position + across * side * Spacing * k);
            for (int k = 1; k <= 4; k++) candidates.Add(ordered.Last().position + along * (Spacing * k + 1f));
            for (int k = 1; k <= 4; k++) candidates.Add(ordered.First().position - along * (Spacing * k));

            // The first designs' prefabs (bark and leaf materials, a capsule round the trunk), made once.
            FruitTreeSamples.EnsureMaterials();
            foreach (string old in FruitTreeSamples.PlainTrees)
            {
                if (!File.Exists(PrefabFolder.Replace("Prefabs/", "") + old + ".fbx")) continue;
                AssetDatabase.ImportAsset(PrefabFolder.Replace("Prefabs/", "") + old + ".fbx", ImportAssetOptions.ForceUpdate);
                FruitTreeSamples.EnsurePrefab(old);
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add fruitless trees");
            int group = Undo.GetCurrentGroup();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && Ours(t) && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    Undo.DestroyObjectImmediate(t.gameObject);

            var taken = new List<Vector3>(ordered.Select(t => t.position));
            Transform parent = ordered.Last().parent;
            var placed = new List<GameObject>();
            foreach (var (prefabName, name) in Fruitless)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + prefabName + ".prefab");
                if (prefab == null) { report.AppendLine("missing " + prefabName + " prefab"); continue; }
                Vector3? spot = null;
                foreach (Vector3 c in candidates)
                {
                    if (taken.Any(p => Flat(p - c) < Spacing * 0.8f)) continue;
                    Collider below = Under(c);
                    if (below != plane) continue;
                    spot = new Vector3(c.x, Ground(c), c.z);
                    break;
                }
                if (spot == null) { report.AppendLine($"no free spot on {plane.name} for {name}"); continue; }
                taken.Add(spot.Value);
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(tree, "Add fruitless trees");
                tree.name = name;
                tree.transform.SetPositionAndRotation(spot.Value, Quaternion.Euler(0f, 25f + placed.Count * 70f, 0f));
                int fruits = 0;
                foreach (Transform child in tree.GetComponentsInChildren<Transform>(true))
                    if (child != tree.transform && child.name.StartsWith("Fruit_")) { child.gameObject.SetActive(false); fruits++; }
                foreach (FruitTree ft in tree.GetComponentsInChildren<FruitTree>(true)) Undo.DestroyObjectImmediate(ft);
                placed.Add(tree);
                report.AppendLine($"{name} at {spot.Value}: {fruits} fruit switched off, no fruit falling");
            }
            Undo.CollapseUndoOperations(group);
            if (placed.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(placed[0].scene);
                Selection.objects = placed.ToArray();
                Picture(ordered.Concat(placed.Select(p => p.transform)).ToList());
                report.AppendLine("save the scene to keep them (Ctrl+Z undoes it)");
            }
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Fruitless trees: " + report);
        }
    }

    private static Collider Under(Vector3 p)
    {
        Collider best = null;
        float y = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody != null || hit.collider is CharacterController) continue;
            if (hit.point.y > p.y + 2f) continue;   // the tree's own crown, a roof...
            if (hit.point.y > y) { y = hit.point.y; best = hit.collider; }
        }
        return best;
    }

    private static float Ground(Vector3 p)
    {
        float y = p.y;
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.point.y <= y + 2f && hit.point.y > best && hit.collider.attachedRigidbody == null) best = hit.point.y;
        return float.IsNegativeInfinity(best) ? y : best;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static void Picture(List<Transform> trees)
    {
        var bounds = new Bounds(trees[0].position, Vector3.zero);
        foreach (Transform t in trees)
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 50f;
            camera.farClipPlane = 500f;
            Vector3 centre = bounds.center;
            float size = bounds.extents.magnitude;
            Vector3 from = centre + new Vector3(0.35f, 0.45f, -1f).normalized * size * 1.6f;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(centre - from));
            Texture2D tex = MapSurvey.Render(camera, 1400, 800);
            File.WriteAllBytes(Path.Combine(Out, "row.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }
}
