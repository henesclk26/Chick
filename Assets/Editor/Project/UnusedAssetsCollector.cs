using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gathers every asset the game does not use into Assets/_Unused, sorted by what it is (Models, Textures, Materials,
/// Prefabs, Scenes, ...), each under its old path, so it can be looked through and deleted or taken back.
/// Used means: everything the build scenes need, everything in Resources folders (loaded by name at run time) and
/// what it needs, the render pipeline and quality assets, and the files at the top of Assets. Never moved: code,
/// shaders, Editor/Plugins/Settings/Resources/StreamingAssets folders, and assets the editor tools load by path
/// ("Assets/..." in Assets/Editor scripts, and the sources of the ArtSource Blender scripts).
/// Moves go through the AssetDatabase, so GUIDs and references stay intact; Assets/_Unused/_moved.txt lists every
/// move, and "Restore Unused Assets" puts them all back. Unused files do not slow the game itself (a build only
/// takes what it uses); they cost import time and disk space in the project.
/// </summary>
public static class UnusedAssetsCollector
{
    private const string Target = "Assets/_Unused";
    private const string Manifest = Target + "/_moved.txt";
    private static readonly string[] KeepFolders = { "/Editor/", "/Plugins/", "/Resources/", "/StreamingAssets/", "/Settings/", "/Gizmos/" };
    private static readonly string[] CodeAndShaders =
        { ".cs", ".asmdef", ".asmref", ".dll", ".so", ".jslib", ".rsp", ".shader", ".shadergraph", ".shadersubgraph", ".hlsl", ".cginc", ".compute", ".raytrace", ".uss", ".tss" };
    // Sources the Blender build scripts in ArtSource read from the project (not visible to the AssetDatabase).
    private static readonly string[] BuildSources =
        { "Assets/Tree Assets/Trees.blend", "Assets/Tree Assets/stylized_tree.glb", "Assets/PolyOne/Free Fruits" };

    [MenuItem("Tools/Chick/Project/Collect Unused Assets")]
    private static void Collect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Unused assets", "Exit play mode first.", "OK"); return; }
        // The scene file is what counts: unsaved changes would be missed.
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
            {
                if (!EditorUtility.DisplayDialog("Unused assets", "The open scene has unsaved changes. What it uses is read from the saved scene file, " +
                                                 "so save it first?", "Save and continue", "Cancel")) return;
                EditorSceneManager.SaveOpenScenes();
                break;
            }
        if (File.Exists(Manifest) && !EditorUtility.DisplayDialog("Unused assets", "Assets were collected before. Collect the rest as well?", "Collect", "Cancel")) return;

        HashSet<string> used;
        List<string> candidates;
        try
        {
            EditorUtility.DisplayProgressBar("Unused assets", "Finding what the game uses", 0.1f);
            used = UsedAssets();
            EditorUtility.DisplayProgressBar("Unused assets", "Listing the project", 0.4f);
            candidates = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p) && !p.StartsWith(Target + "/"))
                .Where(p => !used.Contains(p) && Movable(p)).OrderBy(p => p).ToList();
        }
        finally { EditorUtility.ClearProgressBar(); }
        if (candidates.Count == 0) { EditorUtility.DisplayDialog("Unused assets", "Everything in the project is used.", "OK"); return; }

        long bytes = candidates.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
        var byKind = candidates.GroupBy(Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {g.Count()}");
        if (!EditorUtility.DisplayDialog("Unused assets",
                $"{candidates.Count} unused files ({bytes / 1048576f:F0} MB) will be moved to {Target}:\n" + string.Join("\n", byKind) +
                "\n\nReferences stay intact and Tools/Chick/Project/Restore Unused Assets moves them back.", "Move", "Cancel")) return;

        var moves = candidates.Select(p => (from: p, to: $"{Target}/{Kind(p)}/{p.Substring("Assets/".Length)}")).ToList();
        var log = new StringBuilder();
        var failed = new List<string>();
        try
        {
            // Folders first: a folder made while asset editing is paused is not there yet for the moves.
            foreach (string folder in moves.Select(m => Path.GetDirectoryName(m.to).Replace('\\', '/')).Distinct()) EnsureFolder(folder);
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < moves.Count; i++)
            {
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Unused assets", moves[i].from, (float)i / moves.Count)) break;
                string error = AssetDatabase.MoveAsset(moves[i].from, moves[i].to);
                if (string.IsNullOrEmpty(error)) log.AppendLine(moves[i].to + "\t" + moves[i].from);
                else failed.Add(moves[i].from + ": " + error);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        File.AppendAllText(Manifest, log.ToString());
        int emptied = DeleteEmptyFolders(moves.Select(m => Path.GetDirectoryName(m.from).Replace('\\', '/')));
        AssetDatabase.Refresh();
        string report = $"Moved {moves.Count - failed.Count} unused files ({bytes / 1048576f:F0} MB) to {Target}, sorted by kind; {emptied} folders left empty were removed." +
                        (failed.Count > 0 ? $"\n{failed.Count} could not be moved:\n" + string.Join("\n", failed.Take(30)) : "");
        File.WriteAllText(Target + "/_report.txt", report + "\n\n" + string.Join("\n", byKind));
        AssetDatabase.ImportAsset(Target + "/_report.txt");
        Debug.Log(report);
    }

    [MenuItem("Tools/Chick/Project/Restore Unused Assets")]
    private static void Restore()
    {
        if (!File.Exists(Manifest)) { EditorUtility.DisplayDialog("Unused assets", "Nothing was collected.", "OK"); return; }
        var lines = File.ReadAllLines(Manifest).Where(l => l.Contains('\t')).Select(l => l.Split('\t')).ToList();
        foreach (string folder in lines.Select(l => Path.GetDirectoryName(l[1]).Replace('\\', '/')).Distinct()) EnsureFolder(folder);
        int back = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var l in lines)
                if (File.Exists(l[0]) && string.IsNullOrEmpty(AssetDatabase.MoveAsset(l[0], l[1]))) back++;
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.DeleteAsset(Manifest);
        AssetDatabase.DeleteAsset(Target + "/_report.txt");
        DeleteEmptyFolders(lines.Select(l => Path.GetDirectoryName(l[0]).Replace('\\', '/')));
        if (AssetDatabase.IsValidFolder(Target) && Directory.GetFileSystemEntries(Target).Length == 0) AssetDatabase.DeleteAsset(Target);
        AssetDatabase.Refresh();
        Debug.Log($"Restored {back} of {lines.Count} files to where they were.");
    }

    private static HashSet<string> UsedAssets()
    {
        var roots = new List<string>();
        roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
        for (int i = 0; i < EditorSceneManager.sceneCount; i++) roots.Add(EditorSceneManager.GetSceneAt(i).path);
        roots.AddRange(AssetDatabase.GetAllAssetPaths().Where(p => p.Contains("/Resources/") || p.StartsWith("Assets/Settings/")));
        roots.AddRange(Directory.GetFiles("Assets").Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')));
        if (GraphicsSettings.defaultRenderPipeline != null) roots.Add(AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline));
        for (int q = 0; q < QualitySettings.names.Length; q++)
        {
            var rp = QualitySettings.GetRenderPipelineAssetAt(q);
            if (rp != null) roots.Add(AssetDatabase.GetAssetPath(rp));
        }
        // Paths the editor tools load: their files (or whole folders) and everything those need.
        var pattern = new Regex("\"(Assets/[^\"]+)\"");
        foreach (string script in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Where(f => f.Replace('\\', '/').Contains("/Editor/")))
            foreach (Match m in pattern.Matches(File.ReadAllText(script))) roots.Add(m.Groups[1].Value.TrimEnd('/'));
        roots.AddRange(BuildSources);
        // Only files that exist: a folder named in a tool is where it writes or searches (often a whole pack, or
        // "Assets/..." in a comment), and a path that is not there is a prefix ("Assets/Art/MeadowFlowers/Flower_" + name)
        // whose files, when placed, the scene uses anyway. Whole folders are kept only for the explicit build sources.
        var expanded = new List<string>();
        foreach (string r in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            if (File.Exists(r)) expanded.Add(r);
            else if (BuildSources.Contains(r) && AssetDatabase.IsValidFolder(r))
                expanded.AddRange(AssetDatabase.FindAssets("", new[] { r }).Select(AssetDatabase.GUIDToAssetPath));
        }
        var used = new HashSet<string>(AssetDatabase.GetDependencies(expanded.Distinct().ToArray(), true));
        foreach (string r in expanded) used.Add(r);
        return used;
    }

    private static bool Movable(string path)
    {
        string p = "/" + path;
        if (KeepFolders.Any(p.Contains)) return false;
        if (path.Count(c => c == '/') < 2) return false;                         // files at the top of Assets
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (CodeAndShaders.Contains(ext)) return false;
        if (path.StartsWith("Assets/TextMesh Pro/")) return false;
        return true;
    }

    private static string Kind(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".fbx": case ".obj": case ".blend": case ".glb": case ".gltf": case ".dae": case ".3ds": case ".max": case ".ma": case ".mb":
                return "Models";
            case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".tif": case ".tiff": case ".exr": case ".hdr": case ".bmp": case ".gif":
                return "Textures";
            case ".mat": case ".physicmaterial": case ".physicsmaterial2d": return "Materials";
            case ".prefab": return "Prefabs";
            case ".unity": case ".lighting": return "Scenes";
            case ".anim": case ".controller": case ".overridecontroller": case ".mask": case ".playable": return "Animations";
            case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff": case ".mixer": return "Audio";
            case ".terrainlayer": case ".brush": return "Terrain";
            case ".ttf": case ".otf": case ".fontsettings": case ".uxml": case ".spriteatlas": return "Fonts and UI";
            case ".pdf": case ".txt": case ".md": case ".html": case ".json": case ".xml": case ".csv": return "Documents";
            case ".asset":
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type == typeof(TerrainData)) return "Terrain";
                if (type == typeof(Mesh)) return "Models";
                if (type == typeof(LightingDataAsset)) return "Scenes";
                return "Other Assets";
            default: return "Other";
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    // Folders the moves emptied (and their parents, once empty in turn); folders that were empty before stay.
    private static int DeleteEmptyFolders(IEnumerable<string> emptiedFrom)
    {
        int count = 0;
        var folders = new HashSet<string>();
        foreach (string dir in emptiedFrom)
            for (string d = dir; !string.IsNullOrEmpty(d) && d != "Assets" && d != Target; d = Path.GetDirectoryName(d)?.Replace('\\', '/'))
                folders.Add(d);
        foreach (string path in folders.OrderByDescending(d => d.Length))
        {
            if (!Directory.Exists(path)) continue;
            if (Directory.GetFiles(path).Any(f => !f.EndsWith(".meta")) || Directory.GetDirectories(path).Length > 0) continue;
            if (AssetDatabase.DeleteAsset(path)) count++;
        }
        return count;
    }
}
