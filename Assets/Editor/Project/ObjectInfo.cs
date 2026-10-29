using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Describes the open scene's objects whose name contains the text in Temp/ObjectInfo/request.txt: where they sit in
/// the hierarchy, whether they are on, where they are and how big, and what draws them (renderers, meshes, materials,
/// shaders), down to their children. Picked up while the Editor is open; writes Temp/ObjectInfo/report.txt.
/// </summary>
[InitializeOnLoad]
public static class ObjectInfo
{
    private const string Out = "Temp/ObjectInfo";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static ObjectInfo() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string text = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        var sb = new StringBuilder();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Where(t => t.name.Contains(text) && t.gameObject.scene.IsValid()))
            Describe(t, sb, 0);
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.Length > 0 ? sb.ToString() : "nothing named like " + text);
    }

    private static void Describe(Transform t, StringBuilder sb, int depth)
    {
        string pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}{(depth == 0 ? PathOf(t) : t.name)}  active {t.gameObject.activeSelf}/{t.gameObject.activeInHierarchy}, layer {t.gameObject.layer}, " +
                      $"pos {t.position}, rot {t.eulerAngles}, scale {t.lossyScale}, prefab {PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)}");
        foreach (Renderer r in t.GetComponents<Renderer>())
        {
            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            sb.AppendLine($"{pad}  {r.GetType().Name} enabled {r.enabled}, bounds {r.bounds.center} size {r.bounds.size}, mesh {(mesh != null ? mesh.name + " " + AssetDatabase.GetAssetPath(mesh) : "-")}, " +
                          $"materials {string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : $"{m.name} [{m.shader.name}] {AssetDatabase.GetAssetPath(m)}"))}");
        }
        foreach (Component c in t.GetComponents<Component>())
            if (c == null) sb.AppendLine($"{pad}  (missing script)");
            else if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter)) sb.AppendLine($"{pad}  {c.GetType().Name}");
        if (depth < 3) foreach (Transform child in t) Describe(child, sb, depth + 1);
    }

    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
