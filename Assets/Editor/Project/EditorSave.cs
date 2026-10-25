using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Saves the open scenes and the project's changed assets when Temp/EditorSave/request.txt appears while the Editor is
/// open (as File > Save and File > Save Project), e.g. before a commit; writes Temp/EditorSave/report.txt.
/// </summary>
[InitializeOnLoad]
public static class EditorSave
{
    private const string Out = "Temp/EditorSave";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static EditorSave() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        var report = new StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            report.AppendLine($"{scene.path}: {(scene.isDirty ? "had unsaved changes" : "already saved")}");
        }
        report.AppendLine(EditorSceneManager.SaveOpenScenes() ? "scenes saved" : "SAVING THE SCENES FAILED");
        AssetDatabase.SaveAssets();
        report.AppendLine($"assets saved; version {PlayerSettings.bundleVersion}");
        File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
    }
}
