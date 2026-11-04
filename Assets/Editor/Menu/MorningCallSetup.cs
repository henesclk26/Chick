using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the scene's GameAudio its morning rooster (Assets/Music and Sound/rooster_morning.wav: the crow cut from
/// "Rooster Sound Effects", trimmed, levelled to the chicken call's loudness and faded in and out) and the day
/// transition whose title it crows with. Menu, or Temp/MorningCall/request.txt while the Editor is open (report in
/// that folder).
/// </summary>
[InitializeOnLoad]
public static class MorningCallSetup
{
    private const string Clip = "Assets/Music and Sound/rooster_morning.wav";
    private const string Out = "Temp/MorningCall";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static MorningCallSetup() => EditorApplication.update += Poll;

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

    [MenuItem("Tools/Chick/Audio/Add Morning Rooster")]
    private static void Menu() => Debug.Log(Apply());

    private static string Apply()
    {
        var audio = Object.FindFirstObjectByType<GameAudio>(FindObjectsInactive.Include);
        if (audio == null) return "no GameAudio in the open scene";
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Clip);
        if (clip == null) return "no clip at " + Clip;
        // Short and played at once: loaded whole, decompressed, so the crow starts without a hitch.
        var importer = (AudioImporter)AssetImporter.GetAtPath(Clip);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = .7f;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        var so = new SerializedObject(audio);
        so.FindProperty("morningCall").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Clip);
        so.FindProperty("transition").objectReferenceValue = Object.FindFirstObjectByType<DayTransitionController>(FindObjectsInactive.Include);
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(audio.gameObject.scene);
        return $"morning rooster set on {audio.name} ({clip.length:0.00} s); save the scene to keep it";
    }
}
