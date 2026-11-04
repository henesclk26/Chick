using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The end-of-day card (DaySummary): "apply" puts it on the day transition's UIDocument in the open scene; "look"
/// draws the card, filled with a sample day, over the farm at dusk and saves Temp/DaySummary/look.png (in a temporary
/// editor window, read back and closed; nothing is saved). Menu, or
/// Temp/DaySummary/request.txt while the Editor is open (report in that folder).
/// </summary>
[InitializeOnLoad]
public static class DaySummarySetup
{
    private const string Out = "Temp/DaySummary";
    private const string Request = Out + "/request.txt";
    private const string Uxml = "Assets/UI/DayTransition/DayTransition.uxml";
    private static double nextPoll;
    private static Look look;

    static DaySummarySetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        look?.Tick();
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (look != null || !File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Directory.CreateDirectory(Out);
        if (what == "look") { look = new Look(); return; }
        File.WriteAllText(Path.Combine(Out, "report.txt"), Apply());
    }

    [MenuItem("Tools/Chick/UI/Add Day Summary")]
    private static void Menu() => Debug.Log(Apply());

    private static string Apply()
    {
        var transition = Object.FindFirstObjectByType<DayTransitionController>(FindObjectsInactive.Include);
        if (transition == null) return "no DayTransitionController in the open scene";
        var summary = transition.GetComponent<DaySummary>();
        bool added = summary == null;
        if (added) summary = Undo.AddComponent<DaySummary>(transition.gameObject);
        var so = new SerializedObject(summary);
        so.FindProperty("document").objectReferenceValue = transition.GetComponent<UIDocument>();
        so.ApplyModifiedProperties();
        var time = Object.FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        if (time != null)
        {
            var t = new SerializedObject(time);
            t.FindProperty("summary").objectReferenceValue = summary;
            t.ApplyModifiedProperties();
        }
        EditorSceneManager.MarkSceneDirty(transition.gameObject.scene);
        return $"DaySummary {(added ? "added to" : "refreshed on")} {transition.name}; save the scene to keep it";
    }

    // The card in a temporary editor window over a picture of the farm at dusk, read back from the window (the
    // window draws into a texture, so it need not be in front).
    private sealed class Look
    {
        private readonly LookWindow window;
        private readonly StringBuilder report = new StringBuilder();
        private int frames;
        private const int W = 1280, H = 720;

        public Look()
        {
            Texture2D farm;
            var sky = new GameViewCapture.SkyState();
            try
            {
                sky.Apply(report);
                Object.FindFirstObjectByType<TimeOfDayVisualController>()?.Evaluate(1f);
                farm = TimeShot.Shot(new Vector3(104f, TimeShot.Ground(104f, 16f) + 1.4f, 16f), new Vector3(114f, TimeShot.Ground(114f, 21f) + .8f, 21f), 58f, W, H, 1);
            }
            finally { sky.Restore(); }
            window = ScriptableObject.CreateInstance<LookWindow>();
            window.farm = farm;
            window.titleContent = new GUIContent("Day Summary Look");
            window.ShowUtility();
            window.position = new Rect(40, 40, W, H);
        }

        public void Tick()
        {
            frames++;
            window.Repaint();
            if (frames == 4) window.Build();
            if (frames < 30) return;
            try
            {
                // The window is in points; its pixels are that times the display scale.
                float scale = EditorGUIUtility.pixelsPerPoint;
                int w = Mathf.RoundToInt(W * scale), h = Mathf.RoundToInt(H * scale);
                var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var host = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                host.GetType().GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Invoke(host, new object[] { target, new Rect(0, 0, w, h) });
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = previous;
                // Read back bottom row first: turned the right way up.
                Color[] px = tex.GetPixels(), up = new Color[px.Length];
                for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, up, (h - 1 - y) * w, w);
                tex.SetPixels(up);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(Out, "look.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                target.Release();
                Object.DestroyImmediate(target);
                report.AppendLine("look.png");
            }
            catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
            finally
            {
                Object.DestroyImmediate(window.farm);
                window.Close();
                File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
                look = null;
            }
        }
    }

    private sealed class LookWindow : EditorWindow
    {
        public Texture2D farm;

        public void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.backgroundImage = farm;
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml).CloneTree(root);
            foreach (var child in root.Children()) child.style.flexGrow = 1;
            root.Q("FullScreenBlackOverlay").style.display = DisplayStyle.None;
            var card = root.Q("DaySummary");
            card.style.display = DisplayStyle.Flex;
            card.style.opacity = 1f;
            var holder = new GameObject("Day Summary Look") { hideFlags = HideFlags.HideAndDontSave };
            var summary = holder.AddComponent<DaySummary>();
            try
            {
                typeof(DaySummary).GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(summary, card);
                Sample(summary, card);
            }
            finally { DestroyImmediate(holder); }
        }
    }

    // A sample day: some food, a stage, two eggs, gold, two quests and a region.
    private static void Sample(DaySummary summary, VisualElement card)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(DaySummary);
        type.GetField("rows", F).SetValue(summary, card.Q("DaySummaryRows"));
        type.GetField("title", F).SetValue(summary, card.Q<Label>("DaySummaryTitle"));
        type.GetField("foods", F).SetValue(summary, 64);
        type.GetField("eggs", F).SetValue(summary, 2);
        type.GetField("goldenEggs", F).SetValue(summary, 1);
        type.GetField("purchases", F).SetValue(summary, 1);
        var growth = Object.FindFirstObjectByType<GrowthProgressController>(FindObjectsInactive.Include);
        var upgrades = Object.FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        type.GetField("growth", F).SetValue(summary, growth);
        type.GetField("form", F).SetValue(summary, Object.FindFirstObjectByType<PlayerGrowthController>(FindObjectsInactive.Include));
        type.GetField("upgrades", F).SetValue(summary, upgrades);
        // The chick at 238 of 800 for the picture (put back afterwards): 58 eaten today, stage 2 reached.
        var count = typeof(GrowthProgressController).GetProperty("EatenSeedCount");
        int real = growth != null ? growth.EatenSeedCount : 0;
        if (growth != null) count.SetValue(growth, 238);
        type.GetField("startPoints", F).SetValue(summary, 180);
        type.GetField("startStage", F).SetValue(summary, 1);
        int gold = upgrades != null ? upgrades.WhiteEggsLaid + 5 * upgrades.GoldenEggsLaid : 0;
        type.GetField("startGold", F).SetValue(summary, gold - 6);
        ((List<string>)type.GetField("questsDone", F).GetValue(summary)).AddRange(new[] { "İlk büyüme aşaması", "Farklı tohumlar tat" });
        ((List<string>)type.GetField("regionsOpened", F).GetValue(summary)).Add("Ağaçlık");
        try { type.GetMethod("Fill", F).Invoke(summary, new object[] { 3 }); }
        finally { if (growth != null) count.SetValue(growth, real); }
    }
}
