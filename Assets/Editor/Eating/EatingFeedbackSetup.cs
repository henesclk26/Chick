using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts EatingFeedback (crumbs, the food shrinking away, the happy squash) on the player chick, with its crumb
/// material in Assets/Art/Effects/Eating. Menu, or Temp/EatingFeedback/request.txt while the Editor is open (report in
/// that folder). Run again, it only refreshes the material.
/// </summary>
[InitializeOnLoad]
public static class EatingFeedbackSetup
{
    private const string Folder = "Assets/Art/Effects/Eating";
    private const string Out = "Temp/EatingFeedback";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static EatingFeedbackSetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "report.txt"), Apply());
    }

    [MenuItem("Tools/Chick/Eating/Add Eating Feedback To The Player")]
    private static void Menu() => Debug.Log(Apply());

    private static string Apply()
    {
        var player = Object.FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        if (player == null) return "no player in the open scene";
        if (!AssetDatabase.IsValidFolder("Assets/Art/Effects")) AssetDatabase.CreateFolder("Assets/Art", "Effects");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Effects", "Eating");
        Material crumb = MaterialAt("M_EatCrumb", "Farm/Mist", m => { m.SetColor("_Color", Color.white); m.SetFloat("_Softness", .25f); });
        var feedback = player.GetComponent<EatingFeedback>();
        bool added = feedback == null;
        if (added) feedback = Undo.AddComponent<EatingFeedback>(player.gameObject);
        Undo.RecordObject(feedback, "Eating feedback");
        feedback.Configure(crumb);
        EditorUtility.SetDirty(feedback);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        AssetDatabase.SaveAssets();
        return $"EatingFeedback {(added ? "added to" : "refreshed on")} {player.name}; save the scene to keep it";
    }

    private static Material MaterialAt(string name, string shader, System.Action<Material> setup)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader);
        setup(m);
        EditorUtility.SetDirty(m);
        return m;
    }
}
