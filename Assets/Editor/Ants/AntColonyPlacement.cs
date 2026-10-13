using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places the ant colony across the chicken yard: one nest near its south-west corner, the other near the north-east
/// corner, and the trail bush part way between them. An existing "Ant Colony" is replaced, keeping its ant size.
/// The nests and the bush are ordinary children, so they can be moved afterwards; the routes follow them.
/// </summary>
public static class AntColonyPlacement
{
    private const string AntPath = "Assets/Art/Ants/Ant.fbx";
    private const string NestPath = "Assets/Art/Ants/AntNest.fbx";
    private const string BushPath = "Assets/Art/Ants/AntBush.fbx";
    private const string WheatPath = "Assets/Prefabs/Edibles/WheatGrain.prefab";
    private const string ColonyName = "Ant Colony";
    private const int CameraBlockerLayer = 8;

    // The chicken yard fence runs from x 5 to 35 and z 0 to 36 in Farm Reference Map space
    // (FarmReferenceMapBuilder.BuildZones); the nests sit a few metres in from two opposite corners.
    private static readonly Vector2 StartCorner = new Vector2(8.5f, 3.5f);
    private static readonly Vector2 EndCorner = new Vector2(31.5f, 32.5f);
    private const float BushAlong = 0.45f;

    [MenuItem("Tools/Chick/Ants/Place Ant Colony Across Chicken Yard")]
    private static void Place()
    {
        var root = GameObject.Find("Farm Reference Map");
        var ant = AssetDatabase.LoadAssetAtPath<GameObject>(AntPath);
        var nest = AssetDatabase.LoadAssetAtPath<GameObject>(NestPath);
        var bush = AssetDatabase.LoadAssetAtPath<GameObject>(BushPath);
        var wheat = AssetDatabase.LoadAssetAtPath<GameObject>(WheatPath);
        if (root == null || ant == null || nest == null || bush == null || wheat == null)
        {
            EditorUtility.DisplayDialog("Ant colony", root == null
                ? "The open scene has no 'Farm Reference Map'."
                : "Ant models (Assets/Art/Ants) or the wheat prefab are missing.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Place ant colony");
        int group = Undo.GetCurrentGroup();

        float antScale = 1f;
        Transform old = root.transform.Find(ColonyName);
        if (old != null)
        {
            if (old.TryGetComponent(out AntColony previous)) antScale = previous.AntScale;
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        Vector3 start = Clear(Ground(root.transform.TransformPoint(new Vector3(StartCorner.x, 0f, StartCorner.y))), 0.7f);
        Vector3 end = Clear(Ground(root.transform.TransformPoint(new Vector3(EndCorner.x, 0f, EndCorner.y))), 0.7f);
        Vector3 middle = Clear(Ground(Vector3.Lerp(start, end, BushAlong)), 1.0f);

        var colony = new GameObject(ColonyName);
        Undo.RegisterCreatedObjectUndo(colony, "Place ant colony");
        colony.transform.SetParent(root.transform, false);
        colony.transform.position = middle;

        Transform from = Spawn(nest, colony.transform, "Ant Nest (start)", start, 1f, 20f);
        Transform to = Spawn(nest, colony.transform, "Ant Nest (end)", end, 0.85f, 145f);
        Transform trailBush = Spawn(bush, colony.transform, "Ant Trail Bush", middle, 1f, 70f);
        // The camera treats the bush like a wall, so it can never swing inside and show what happens in there.
        foreach (Transform t in trailBush.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = CameraBlockerLayer;

        var trail = Undo.AddComponent<AntColony>(colony);
        trail.Configure(ant, wheat, from, to, trailBush);
        trail.AntScale = antScale;
        EditorUtility.SetDirty(trail);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = colony;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"Ant colony placed across the chicken yard: nests at {start} and {end}, bush at {middle}" +
                  (old != null ? $" (replaced the old colony, ant size {antScale:0.##} kept)" : "") +
                  ". Save the scene to keep it (Ctrl+Z undoes it).");
    }

    private static Transform Spawn(GameObject model, Transform parent, string name, Vector3 position, float scale, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        Undo.RegisterCreatedObjectUndo(go, "Place ant colony");
        return go.transform;
    }

    // Terrain height under the point (other colliders such as fences and props are skipped).
    private static Vector3 Ground(Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 200f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (hit.collider is TerrainCollider) return hit.point;
        return hits.Length > 0 ? hits[0].point : point;
    }

    // Nudges a spot outwards in a widening spiral until nothing but terrain is within the radius.
    private static Vector3 Clear(Vector3 point, float radius)
    {
        for (int i = 0; i < 40; i++)
        {
            float angle = i * 2.39996f, distance = i * 0.35f;
            Vector3 candidate = Ground(point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
            bool blocked = false;
            foreach (Collider c in Physics.OverlapSphere(candidate + Vector3.up * (radius * 0.6f), radius, ~((1 << 2) | (1 << 4)),
                         QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider)) { blocked = true; break; }
            if (!blocked) return candidate;
        }
        return point;
    }
}
