using UnityEngine;

/// <summary>
/// Finds an object by name anywhere in a scene, inactive ones included. The map tools use it for the groups they made
/// (planted vegetation, point grass, tree samples), which "Organize Hierarchy" may have moved out of the Farm
/// Reference Map into the Map's kind groups.
/// </summary>
public static class SceneLookup
{
    public static Transform FindAnywhere(GameObject inScene, string name)
    {
        if (inScene == null) return null;
        foreach (var root in inScene.scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }
}
