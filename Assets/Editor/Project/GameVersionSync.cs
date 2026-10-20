using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Player Settings > Version (Application.version, shown at the bottom left of the HUD) equal to the version in
/// GameVersionHUD.uxml. A commit sets both files to the new version (see AGENTS.md); when the Editor is open it would
/// otherwise keep its old Player Settings in memory and write the old version back the next time it saves them.
/// </summary>
[InitializeOnLoad]
public static class GameVersionSync
{
    private const string HudPath = "Assets/UI/GameVersion/GameVersionHUD.uxml";

    static GameVersionSync() => EditorApplication.delayCall += Sync;

    private static void Sync()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(HudPath)) return;
        Match m = Regex.Match(File.ReadAllText(HudPath), "text=\"v([0-9.]+)\"");
        if (!m.Success || PlayerSettings.bundleVersion == m.Groups[1].Value) return;
        Debug.Log($"Version: Player Settings {PlayerSettings.bundleVersion} -> {m.Groups[1].Value} (from the HUD).");
        PlayerSettings.bundleVersion = m.Groups[1].Value;
        AssetDatabase.SaveAssets();
    }
}
