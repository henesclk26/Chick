using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts UpgradeCelebration (the wing flap, the burst of down and the dust ring when a growth stage is reached or an
/// upgrade bought) on the player, with its down material in Assets/Art/Effects/UpgradeCelebration and the ground
/// dust's puff (M_GroundPuff). Menu, or Temp/UpgradeCelebration/request.txt while the Editor is open (report in that
/// folder). Run again, it only refreshes the material.
/// </summary>
[InitializeOnLoad]
public static class UpgradeCelebrationSetup
{
    private const string Folder = "Assets/Art/Effects/UpgradeCelebration";
    private const string Puff = "Assets/Art/GroundEffects/M_GroundPuff.mat";
    private const string Out = "Temp/UpgradeCelebration";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static UpgradeCelebrationSetup() => EditorApplication.update += Poll;

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

    [MenuItem("Tools/Chick/Player Growth/Add Upgrade Celebration To The Player")]
    private static void Menu() => Debug.Log(Apply());

    private static string Apply()
    {
        var player = Object.FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        if (player == null) return "no player in the open scene";
        if (!AssetDatabase.IsValidFolder("Assets/Art/Effects")) AssetDatabase.CreateFolder("Assets/Art", "Effects");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Effects", "UpgradeCelebration");

        // The down: opaque, two-sided, coloured by the particles (yellow for the chick, white for the chicken).
        string path = Folder + "/M_CelebrationDown.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var down = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (down == null) { down = new Material(shader); AssetDatabase.CreateAsset(down, path); }
        down.shader = shader;
        down.SetFloat("_Surface", 0f);
        down.SetFloat("_Cull", 0f);
        down.SetColor("_BaseColor", Color.white);
        down.SetOverrideTag("RenderType", "Opaque");
        down.renderQueue = -1;
        EditorUtility.SetDirty(down);

        var celebration = player.GetComponent<UpgradeCelebration>();
        bool added = celebration == null;
        if (added) celebration = Undo.AddComponent<UpgradeCelebration>(player.gameObject);
        Undo.RecordObject(celebration, "Upgrade celebration");
        celebration.Configure(down, AssetDatabase.LoadAssetAtPath<Material>(Puff));
        EditorUtility.SetDirty(celebration);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        AssetDatabase.SaveAssets();
        return $"UpgradeCelebration {(added ? "added to" : "refreshed on")} {player.name}; save the scene to keep it";
    }
}
