using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps every mushroom food (EdibleMushroom) out of static batching. A mushroom shrinks and hops as it is eaten, and a
/// statically batched mesh would keep drawing at its old size. Runs on each scene as it goes into play mode or a
/// build, before the batching, so the scene's own static flags stay as they are and a mushroom copied in later needs
/// nothing.
/// </summary>
public sealed class MushroomStaticBatching : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var mushroom in root.GetComponentsInChildren<EdibleMushroom>(true))
                foreach (var t in mushroom.GetComponentsInChildren<UnityEngine.Transform>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                    if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                }
    }
}
