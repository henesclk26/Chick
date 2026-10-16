using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Seats the old farmer (SM_Chr_Farmer_Male_Old_01) in the rocking chair (SM_Prop_Chair_Rocking_01) in the open scene.
/// The chair already in the scene is used; if there is none, one is placed where the farmer stands, facing his way.
/// He is parented to the chair and posed seated right here (his T-pose is read from his prefab, so running the menu
/// again never bends him twice); in play mode RockingChair rocks the chair with him in it.
/// </summary>
public static class RockingChairSetup
{
    private const string FarmerName = "SM_Chr_Farmer_Male_Old_01";
    private const string ChairName = "SM_Prop_Chair_Rocking_01";
    private const string ChairPrefabPath = "Assets/Synty/PolygonFarm/Prefabs/Props/SM_Prop_Chair_Rocking_01.prefab";

    [MenuItem("Tools/Chick/Farmer/Seat Old Farmer In Rocking Chair")]
    private static void Seat()
    {
        GameObject farmer = FindInScene(FarmerName);
        Animator animator = farmer != null ? farmer.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman)
        {
            EditorUtility.DisplayDialog("Rocking chair", "No " + FarmerName + " with a Humanoid Animator in the open scene.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Seat farmer in rocking chair");
        int group = Undo.GetCurrentGroup();

        GameObject chair = FindInScene(ChairName);
        if (chair == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChairPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Rocking chair", "Missing " + ChairPrefabPath + ".", "OK");
                return;
            }
            chair = (GameObject)PrefabUtility.InstantiatePrefab(prefab, farmer.scene);
            Undo.RegisterCreatedObjectUndo(chair, "Seat farmer in rocking chair");
            chair.transform.SetParent(farmer.transform.parent, false);
            chair.transform.SetPositionAndRotation(farmer.transform.position, Quaternion.Euler(0f, farmer.transform.eulerAngles.y, 0f));
        }

        var rocking = chair.GetComponent<RockingChair>();
        if (rocking == null) rocking = Undo.AddComponent<RockingChair>(chair);
        if (farmer.transform.parent != chair.transform)
            Undo.SetTransformParent(farmer.transform, chair.transform, "Seat farmer in rocking chair");
        Undo.RegisterFullObjectHierarchyUndo(farmer, "Seat farmer in rocking chair");
        Undo.RecordObject(rocking, "Seat farmer in rocking chair");

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(farmer);
        rocking.Seat(animator, bone =>
        {
            var restBone = source != null ? PrefabUtility.GetCorrespondingObjectFromSource(bone) : null;
            return restBone != null
                ? Quaternion.Inverse(source.transform.rotation) * restBone.rotation
                : Quaternion.Inverse(farmer.transform.rotation) * bone.rotation;
        });
        EditorUtility.SetDirty(rocking);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = chair;
        EditorSceneManager.MarkSceneDirty(chair.scene);
        Debug.Log($"{FarmerName} seated in {chair.name} at {chair.transform.position}. Press Play to see him rock; " +
                  "save the scene to keep it (Ctrl+Z undoes it).");
    }

    private static GameObject FindInScene(string prefix)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name.StartsWith(prefix) && t.gameObject.scene.IsValid() &&
                (t.parent == null || !t.parent.name.StartsWith(prefix)))
                return t.gameObject;
        return null;
    }
}
