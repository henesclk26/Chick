using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Give dragged/duplicated food prefabs independent, persistent save identities.</summary>
[InitializeOnLoad]
public static class EdibleFoodSourceIdentity
{
    private static bool queued;
    private static readonly Dictionary<string, EdibleFoodSource> Owners =
        new Dictionary<string, EdibleFoodSource>(StringComparer.Ordinal);

    static EdibleFoodSourceIdentity()
    {
        EditorApplication.hierarchyChanged += Queue;
        EditorSceneManager.sceneSaving += (scene, path) => EnsureSceneIds();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) EnsureSceneIds();
        };
        Queue();
    }

    private static void Queue()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; EnsureSceneIds(); };
    }

    public static void EnsureSceneIds()
    {
        if (EditorApplication.isPlaying) return;
        foreach (string key in Owners.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray())
            Owners.Remove(key);
        var used = new HashSet<string>(StringComparer.Ordinal);
        // Saved instances keep their identity when an unsaved duplicate is created.
        var sources = UnityEngine.Object.FindObjectsByType<EdibleFoodSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(source =>
            {
                ulong id = GlobalObjectId.GetGlobalObjectIdSlow(source).targetObjectId;
                return id == 0 ? ulong.MaxValue : id;
            }).ThenByDescending(source => source.GetInstanceID());
        foreach (var source in sources)
        {
            if (EditorUtility.IsPersistent(source) || !source.gameObject.scene.IsValid() ||
                string.IsNullOrEmpty(source.gameObject.scene.path) ||
                PrefabStageUtility.GetPrefabStage(source.gameObject) != null) continue;
            var data = new SerializedObject(source);
            var id = data.FindProperty("sourceId");
            string value = id.stringValue;
            EdibleFoodSource owner;
            bool ownedByAnother = !string.IsNullOrEmpty(value) && Owners.TryGetValue(value, out owner)
                && owner != source;
            if (string.IsNullOrEmpty(value) || ownedByAnother || !used.Add(value))
            {
                value = Guid.NewGuid().ToString("N");
                used.Add(value);
                id.stringValue = value;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
            }
            Owners[value] = source;
            foreach (var edible in source.GetComponentsInChildren<EdibleObject>(true))
            {
                var edibleData = new SerializedObject(edible);
                var pieceId = edibleData.FindProperty("persistentId");
                string relative = AnimationUtility.CalculateTransformPath(edible.transform, source.transform);
                string expected = "food-" + value + "/" + relative;
                if (pieceId.stringValue == expected) continue;
                pieceId.stringValue = expected;
                edibleData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(edible);
                EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
            }
        }
    }
}
