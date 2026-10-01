using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class MainMenuBuildVerification
{
    public static string Result = "Not started";
    public static string Begin()
    {
        if (Result == "Building") return Result;
        Result = "Building";
        EditorApplication.delayCall += Run;
        return Result;
    }
    public static void Run()
    {
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = "Builds/MenuVerification/ChickMenu.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                Result = report.summary.result + " / " + report.summary.totalErrors + " errors / " + report.summary.totalSize + " bytes";
            }
            catch (System.Exception error) { Result = "FAIL " + error.Message; }
    }
}
