using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authoring-only, undoable mixed food patch. No spawning or polling cost at runtime.</summary>
public static class SpawnSeedScatterSetup
{
    public const string GroupName = "Spawn - Mixed Foraging Seeds";
    private const int Count = 600;
    private const float Radius = 3.2f;
    private const float Spacing = .10f;
    private static readonly string[] Names = { "WheatGrain_Test", "CornSeed", "SunflowerSeed", "RiceGrain" };
    private static readonly string[] Keys = { "wheat", "corn", "sunflower", "rice" };

    [MenuItem("Chick/Eating/Scatter Mixed Seeds Around Spawn")]
    public static void CreateFromMenu() => Debug.Log(Create());

    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open the farm SampleScene first.");
        var existing = GameObject.Find(GroupName);
        if (existing != null) return "Already present: " + existing.transform.childCount + " seeds. Nothing changed.";
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        if (menu == null) throw new InvalidOperationException("Missing new-game spawn configuration.");
        Vector3 spawn = menu.NewGameSpawnPosition;
        var prefabs = new GameObject[Names.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/" + Names[i] + ".prefab");
            if (prefabs[i] == null || prefabs[i].GetComponent<EdibleObject>() == null)
                throw new InvalidOperationException("Missing edible prefab: " + Names[i]);
        }
        Physics.SyncTransforms();
        TerrainCollider ground = null;
        foreach (var hit in Physics.RaycastAll(spawn + Vector3.up * 30f, Vector3.down, 60f,
                     ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider is TerrainCollider terrain) { ground = terrain; break; }
        if (ground == null) throw new InvalidOperationException("No terrain below spawn.");

        // Some decorative coops have no collider. Keep seeds outside their visible footprint too.
        var exclusions = new List<Bounds>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("chicken_coop_handpainted", StringComparison.OrdinalIgnoreCase))
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = r.bounds;
                    b.Expand(.18f);
                    exclusions.Add(b);
                }
        var rng = new System.Random(20261003);
        var hits = new List<RaycastHit>(Count);
        Vector3 center = spawn + new Vector3(-.45f, 0f, .35f);
        for (int attempt = 0; attempt < Count * 100 && hits.Count < Count; attempt++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)rng.NextDouble()) * Radius;
            Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (HorizontalSqr(p - spawn) < .32f * .32f) continue;
            bool blocked = false;
            foreach (var b in exclusions)
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                { blocked = true; break; }
            if (blocked || !ground.Raycast(new Ray(p + Vector3.up * 30f, Vector3.down), out var hit, 60f) ||
                Vector3.Dot(hit.normal, Vector3.up) < .90f) continue;
            // Leave room for the helper's body, not just the seed's tiny collider.
            foreach (var c in Physics.OverlapCapsule(hit.point + Vector3.up * .14f,
                         hit.point + Vector3.up * .3f, .12f, ~(1 << 9), QueryTriggerInteraction.Ignore))
                if (c != ground && !(c is CharacterController)) { blocked = true; break; }
            if (blocked) continue;
            foreach (var other in hits)
                if (HorizontalSqr(other.point - hit.point) < Spacing * Spacing) { blocked = true; break; }
            if (!blocked) hits.Add(hit);
        }
        if (hits.Count != Count) throw new InvalidOperationException("Not enough safe ground; no scene objects created.");

        // Equal quantities, shuffled independently of positions: no repeating rows or single-type strips.
        var types = new int[Count];
        for (int i = 0; i < Count; i++) types[i] = i % Names.Length;
        for (int i = Count - 1; i > 0; i--)
        { int j = rng.Next(i + 1); int old = types[i]; types[i] = types[j]; types[j] = old; }

        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Scatter mixed spawn seeds");
        try
        {
            var parent = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(parent, "Create spawn seed group");
            parent.transform.position = spawn;
            for (int i = 0; i < Count; i++)
            {
                int type = types[i];
                var food = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[type], scene);
                Undo.RegisterCreatedObjectUndo(food, "Create seed");
                food.transform.SetParent(parent.transform, false);
                food.name = Names[type] + "_Spawn_" + i.ToString("D4");
                var hit = hits[i];
                food.transform.SetPositionAndRotation(hit.point,
                    Quaternion.FromToRotation(Vector3.up, hit.normal) *
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                food.transform.localScale = prefabs[type].transform.localScale * Mathf.Lerp(.94f, 1.06f, (float)rng.NextDouble());
                // Project renderer bounds onto the surface normal: handles both centered and bottom pivots.
                float bottom = float.PositiveInfinity;
                foreach (var renderer in food.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = renderer.localBounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 v = b.center + Vector3.Scale(b.extents, new Vector3(
                            (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                        bottom = Mathf.Min(bottom, Vector3.Dot(renderer.transform.TransformPoint(v) - hit.point, hit.normal));
                    }
                }
                if (float.IsPositiveInfinity(bottom)) throw new InvalidOperationException("Seed has no visual: " + food.name);
                food.transform.position += hit.normal * (.001f - bottom);
                var data = new SerializedObject(food.GetComponent<EdibleObject>());
                data.FindProperty("persistentId").stringValue = "spawn-forage-v1-" + i.ToString("D4");
                data.FindProperty("statisticKey").stringValue = Keys[type];
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(food.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(food);
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            Physics.SyncTransforms();
            return "Created 600 mixed seeds (150 each), terrain-aligned, radius 3.2 m. Scene ready for review/save.";
        }
        catch
        {
            Undo.RevertAllDownToGroup(undo);
            throw;
        }
    }

    private static float HorizontalSqr(Vector3 v) => v.x * v.x + v.z * v.z;
}
