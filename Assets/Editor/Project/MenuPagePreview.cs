using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A picture of one page of the main menu (MainMenu.uxml) as laid out by UI Toolkit, without entering play mode: the
/// page named in Temp/MenuPreview/request.txt ("home", "credits-page", "settings-page"; "credits-page scrolled" scrolls
/// it to the end) is shown in a temporary window over the menu's backdrop, read off the screen and saved to
/// Temp/MenuPreview/&lt;page&gt;.png, with a report of what the page holds. The window then closes.
/// </summary>
[InitializeOnLoad]
public sealed class MenuPagePreview : EditorWindow
{
    private const string Out = "Temp/MenuPreview";
    private const string Request = Out + "/request.txt";
    private const string Uxml = "Assets/UI/Menu/MainMenu.uxml";
    private static double nextPoll;
    private string page;
    private bool scrolled;
    private int frames;

    static MenuPagePreview() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] what = File.ReadAllText(Request).Trim().Split(' ');
        File.Delete(Request);
        var window = CreateInstance<MenuPagePreview>();
        window.page = what[0];
        window.scrolled = what.Length > 1 && what[1] == "scrolled";
        window.titleContent = new GUIContent("Menu Preview");
        window.ShowUtility();
        window.position = new Rect(60, 60, 1280, 720);
    }

    private void CreateGUI()
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml);
        tree.CloneTree(rootVisualElement);
        var shell = rootVisualElement.Q("menu-shell");
        shell.style.flexGrow = 1;
        shell.AddToClassList("visible");
        rootVisualElement.style.flexGrow = 1;
        foreach (var child in shell.Children().ToList())
            if (child.name != "menu-backdrop") child.AddToClassList("hidden");
        rootVisualElement.Q(page)?.RemoveFromClassList("hidden");
        EditorApplication.update += Tick;
    }

    private void Tick()
    {
        Repaint();
        if (++frames == 20 && scrolled)
            foreach (var scroll in rootVisualElement.Query<ScrollView>().ToList()) scroll.scrollOffset = new Vector2(0f, 100000f);
        if (frames < 40) return;
        EditorApplication.update -= Tick;
        Directory.CreateDirectory(Out);
        try
        {
            Rect r = position;
            Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(r.position, (int)r.width, (int)r.height);
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGB24, false);
            tex.SetPixels(pixels); tex.Apply();
            string name = page + (scrolled ? "_scrolled" : "");
            File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
            DestroyImmediate(tex);
            VisualElement shown = rootVisualElement.Q(page);
            string report = shown == null ? $"no element named {page}" :
                $"{page}: {shown.Query<Label>().ToList().Count} labels, {shown.Query<Button>().ToList().Count} buttons, size {shown.layout.size}\n" +
                string.Join("\n", shown.Query<Button>().ToList().Select(b => $"  button {b.name}: {b.text}"));
            File.WriteAllText(Path.Combine(Out, name + ".txt"), report);
        }
        finally { Close(); }
    }
}
