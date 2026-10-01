#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Disposable Play Mode verification for the continuous journal growth HUD.
/// Attach to a temporary GameObject; only its isolated UIDocument and render targets are owned here.
/// </summary>
public sealed class JournalGrowthHUDPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";

    private const string AtlasResourcesPath = "JournalGrowthHUD/HudAtlas";
    private const string CaptureDirectory = "Captures/JournalGrowthHUD";
    private const float Tolerance = .15f;

    private static readonly string[] ExpectedSpriteNames = { "Frame", "Fill", "Cursor" };
    private readonly StringBuilder report = new StringBuilder();
    private readonly List<RenderTexture> renderTargets = new List<RenderTexture>();
    private GameObject documentObject;
    private PanelSettings runtimePanelSettings;
    private UIDocument document;
    private GrowthProgressBarElement bar;
    private bool originalRunInBackground;
    private bool hasCapturedOriginalRunInBackground;
    private bool cleaned;
    private int failures;

    private void Start()
    {
        Result = "Running";
        originalRunInBackground = Application.runInBackground;
        hasCapturedOriginalRunInBackground = true;
        Application.runInBackground = true;
        StartCoroutine(RunGuarded());
    }

    private IEnumerator RunGuarded()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(RunVerification());
        while (stack.Count > 0)
        {
            IEnumerator current = stack.Peek();
            bool moved;
            object yielded = null;
            try
            {
                moved = current.MoveNext();
                if (moved) yielded = current.Current;
            }
            catch (Exception exception)
            {
                Check(false, "Verification aborted safely: " + exception);
                break;
            }

            if (!moved)
            {
                stack.Pop();
                continue;
            }
            if (yielded is IEnumerator nested) stack.Push(nested);
            else yield return yielded;
        }

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("JOURNAL GROWTH HUD VERIFICATION\n" + Result, this);
        WriteReport();
        CleanupOwnedObjects();
        Destroy(gameObject);
    }

    private IEnumerator RunVerification()
    {
        CreateIsolatedDocument();
        yield return null;
        yield return null;
        VerifyAtlasAndBarStructure();
        yield return VerifyProgressCases();
        yield return VerifyContinuousMovement();
        yield return CaptureRequestedRenders();
    }

    private void CreateIsolatedDocument()
    {
        PanelSettings sourceSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
            "Assets/UI/GrowthProgress/GrowthPanelSettings.asset");
        VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/GrowthProgress/GrowthProgressHUD.uxml");
        if (sourceSettings == null) throw new InvalidOperationException("Could not load GrowthPanelSettings.asset.");
        if (visualTree == null) throw new InvalidOperationException("Could not load GrowthProgressHUD.uxml.");

        runtimePanelSettings = Instantiate(sourceSettings);
        runtimePanelSettings.name = "JournalGrowthHUDVerificationPanel_TEMP";
        runtimePanelSettings.clearColor = true;
        runtimePanelSettings.colorClearValue = Color.clear;
        runtimePanelSettings.targetTexture = CreateTargetTexture(1920, 1080);

        documentObject = new GameObject("JournalGrowthHUDVerificationDocument_TEMP");
        documentObject.hideFlags = HideFlags.DontSave;
        document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = runtimePanelSettings;
        document.visualTreeAsset = visualTree;

        VisualElement root = document.rootVisualElement;
        VisualElement hud = root.Q("GrowthProgressHUD");
        if (hud == null) throw new InvalidOperationException("GrowthProgressHUD was not found in the isolated document.");
        hud.style.display = DisplayStyle.Flex;
        VisualElement upgrades = root.Q("UpgradeNotificationRoot");
        if (upgrades != null) upgrades.style.display = DisplayStyle.None;
        Image leftIcon = root.Q<Image>("LeftIcon");
        Image rightIcon = root.Q<Image>("RightIcon");
        if (leftIcon != null) leftIcon.style.display = DisplayStyle.None;
        if (rightIcon != null) rightIcon.style.display = DisplayStyle.None;

        bar = root.Q<GrowthProgressBarElement>("GrowthBarContainer");
        if (bar == null) throw new InvalidOperationException("GrowthProgressBarElement was not found.");
        Check(root.Q<Label>("GrowthCount") == null, "HUD has no percentage label above the bar.");
    }

    private void VerifyAtlasAndBarStructure()
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(AtlasResourcesPath);
        Check(sprites.Length == ExpectedSpriteNames.Length,
            "Atlas loads three sprites (found " + sprites.Length + ").");
        for (int i = 0; i < ExpectedSpriteNames.Length; i++)
        {
            string name = ExpectedSpriteNames[i];
            Sprite sprite = FindSprite(sprites, name);
            Check(sprite != null, "Atlas contains " + name + ".");
            if (sprite != null && sprite.texture != null)
            {
                Check(sprite.texture.filterMode == FilterMode.Bilinear, name + " uses bilinear filtering.");
                Check(sprite.texture.mipmapCount == 1, name + " texture has no mipmaps.");
            }
        }

        VisualElement frame = bar.Q("growth-frame");
        VisualElement track = bar.Q("growth-track");
        VisualElement reveal = bar.Q("growth-reveal");
        Image fill = bar.Q<Image>("growth-fill");
        Image cursor = bar.Q<Image>("growth-cursor");
        Check(frame != null && track != null && reveal != null && fill != null && cursor != null,
            "Journal HUD has frame, oval track, reveal, fill, and cursor elements.");
        if (frame != null)
        {
            Sprite frameSprite = frame.resolvedStyle.backgroundImage.sprite;
            Check(frameSprite != null && frameSprite.name == "Frame", "Frame is drawn from the Frame sprite.");
        }
        Check(fill != null && fill.sprite != null && fill.sprite.name == "Fill", "Fill Image uses the Fill sprite.");
        Check(cursor != null && cursor.sprite != null && cursor.sprite.name == "Cursor", "Cursor Image uses the Cursor sprite.");
        Check(bar.Q("growth-wheat") == null, "Journal HUD does not reserve space for a wheat image.");
        Check(bar.HasArtwork, "Custom element reports that journal artwork is loaded.");
        Check(bar.Q("growth-cell-0") == null && bar.Q("growth-buffer") == null,
            "HUD contains no pixel cells or buffer layer.");
        Check(Mathf.Abs(bar.resolvedStyle.height - 90f) <= 1f,
            "Journal progress element has a 90 px height.");
    }

    private IEnumerator VerifyProgressCases()
    {
        VerifyProgress(0f, 0f);
        VerifyProgress(2.1f, .021f);
        VerifyProgress(2.9f, .029f);
        VerifyProgress(25f, .25f);
        VerifyProgress(50f, .5f);
        VerifyProgress(99.9f, .999f);
        VerifyProgress(100f, 1f);
        VerifyProgress(float.NaN, 0f);
        VerifyProgress(-25f, 0f);
        VerifyProgress(float.NegativeInfinity, 0f);
        VerifyProgress(float.PositiveInfinity, 1f);
        VerifyProgress(125f, 1f);
        yield return null;
        CheckEndpointGeometry(0f);
        CheckEndpointGeometry(1f);
    }

    private void VerifyProgress(float percent, float expected)
    {
        bar.Progress = percent / 100f;
        string title = DisplayPercent(percent);
        Check(Mathf.Abs(bar.Progress - expected) <= .0001f,
            title + ": progress is sanitized and clamped to " + expected + ".");
        Vector2 edge = GrowthProgressBarElement.FillEdge(100f, 20f, expected);
        Check(Mathf.Abs(edge.x - 100f * expected) <= .001f && Mathf.Abs(edge.y - edge.x) <= .001f,
            title + ": fill edge uses continuous proportional width.");
    }

    private IEnumerator VerifyContinuousMovement()
    {
        SetProgress(.492f);
        yield return null;
        yield return null;
        float firstFill = bar.FillWidth;
        float firstCursor = bar.CursorPosition;
        SetProgress(.499f);
        yield return null;
        yield return null;
        float nextFill = bar.FillWidth;
        float nextCursor = bar.CursorPosition;
        Check(nextFill > firstFill && nextCursor > firstCursor,
            "Fill and cursor both move continuously inside the same former 5% step.");
        Check(Mathf.Abs((nextFill - firstFill) - bar.TrackWidth * .007f) <= Tolerance,
            "Sub-step fill movement matches the progress delta times track width.");
    }

    private void CheckEndpointGeometry(float progress)
    {
        SetProgress(progress);
        float expectedFill = bar.TrackWidth * progress;
        Check(Mathf.Abs(bar.FillWidth - expectedFill) <= Tolerance,
            DisplayPercent(progress * 100f) + ": fill width matches the track endpoint.");
        // CursorPosition is in bar coordinates; the fill starts at the track's left edge.
        float cursorOnTrack = bar.CursorPosition - bar.Q("growth-track").layout.x;
        if (progress == 0f)
        {
            Check(bar.Q("growth-reveal").resolvedStyle.display == DisplayStyle.None || bar.FillWidth <= Tolerance,
                "At zero, the gold fill is hidden.");
            Check(Mathf.Abs(cursorOnTrack) <= Tolerance,
                "At zero, cursor center matches the fill start.");
        }
        else
        {
            Check(Mathf.Abs(bar.FillWidth - bar.TrackWidth) <= Tolerance,
                "At 100%, gold fill spans the full track.");
            Check(Mathf.Abs(cursorOnTrack - bar.TrackWidth) <= Tolerance,
                "At 100%, cursor center matches the fill end.");
        }
    }

    private IEnumerator CaptureRequestedRenders()
    {
        var captures = new List<CaptureSpec>
        {
            new CaptureSpec(1920, 1080, 0f), new CaptureSpec(1920, 1080, 50f), new CaptureSpec(1920, 1080, 100f),
            new CaptureSpec(1280, 720, 50f), new CaptureSpec(1440, 1080, 50f), new CaptureSpec(2560, 1080, 50f)
        };
        for (int i = 0; i < captures.Count; i++)
        {
            CaptureSpec spec = captures[i];
            RenderTexture target = CreateTargetTexture(spec.Width, spec.Height);
            runtimePanelSettings.targetTexture = target;
            SetProgress(spec.Percent / 100f);
            yield return null;
            yield return null;
            CheckLayout(spec.Width, spec.Height, spec.Percent);
            yield return new WaitForEndOfFrame();
            yield return CapturePng(target, spec);
        }
    }

    private IEnumerator CapturePng(RenderTexture target, CaptureSpec spec)
    {
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CaptureDirectory));
        Directory.CreateDirectory(directory);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            RenderTexture.active = target;
            image = new Texture2D(spec.Width, spec.Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, spec.Width, spec.Height), 0, 0);
            image.Apply(false, false);
            string file = Path.Combine(directory, "hud-" + spec.Width + "x" + spec.Height + "-" + DisplayPercent(spec.Percent) + ".png");
            File.WriteAllBytes(file, image.EncodeToPNG());
            if (spec.Width == 1920 && spec.Height == 1080 && spec.Percent != 50f)
            {
                int left = Mathf.Max(0, Mathf.FloorToInt(bar.worldBound.xMin) - 64);
                int cropWidth = Mathf.Min(spec.Width - left, Mathf.CeilToInt(bar.worldBound.width) + 128);
                var preview = new Texture2D(cropWidth, 180, TextureFormat.RGBA32, false);
                try
                {
                    preview.ReadPixels(new Rect(left, spec.Height - 180, cropWidth, 180), 0, 0);
                    preview.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(directory, "hud-preview-" + DisplayPercent(spec.Percent) + ".png"), preview.EncodeToPNG());
                }
                finally { Destroy(preview); }
            }
            Check(true, "Captured journal HUD render " + spec.Width + "x" + spec.Height + " at " + DisplayPercent(spec.Percent) + ".");
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) Destroy(image);
        }
        yield return null;
    }

    private void CheckLayout(int width, int height, float percent)
    {
        Rect panel = document.rootVisualElement.worldBound;
        Rect root = bar.worldBound;
        VisualElement frame = bar.Q("growth-frame");
        VisualElement track = bar.Q("growth-track");
        VisualElement reveal = bar.Q("growth-reveal");
        VisualElement cursor = bar.Q("growth-cursor");
        Check(panel.width > 0f && panel.height > 0f && root.width > 0f && root.height > 0f,
            width + "x" + height + ": panel and journal root have layout geometry.");
        // Scaled panels (e.g. 1440x1080 -> .875) snap to device pixels, so allow one panel pixel.
        Check(Mathf.Abs(root.center.x - panel.center.x) <= 1.5f && root.width <= 821f && root.width >= 239f,
            width + "x" + height + ": root is centered and fits 240..820 px.");
        if (frame == null || track == null || reveal == null || cursor == null)
        {
            Check(false, width + "x" + height + ": frame, oval track, reveal, and cursor exist.");
            return;
        }
        Rect frameRect = frame.worldBound;
        Rect trackRect = track.worldBound;
        Rect revealRect = reveal.worldBound;
        Rect cursorRect = cursor.worldBound;
        Check(trackRect.width > 0f && trackRect.height > 0f && frameRect.width > 0f,
            width + "x" + height + ": frame and oval track have visible bounds.");
        Check(revealRect.xMin >= trackRect.xMin - 1f && revealRect.xMax <= trackRect.xMax + 1f,
            width + "x" + height + ": continuous fill reveal stays within the track.");
        // A zero-height reveal clips the fill away entirely even though its width is correct.
        Check(Mathf.Abs(revealRect.height - trackRect.height) <= 1f,
            width + "x" + height + ": fill reveal is as tall as the track, so the gold fill is visible.");
        // The cursor center marks the fill end, so at 0%/100% half the diamond may overhang the frame edge.
        Check(cursorRect.yMin >= frameRect.yMax - 1f && cursorRect.center.x >= frameRect.xMin - 1f && cursorRect.center.x <= frameRect.xMax + 1f,
            width + "x" + height + ": cursor remains below the frame with its center horizontally within it.");
        Check(bar.TrackWidth > 0f && bar.FillWidth >= -Tolerance && bar.FillWidth <= bar.TrackWidth + Tolerance,
            width + "x" + height + " at " + DisplayPercent(percent) + ": fill dimensions stay in bounds.");
    }

    private void SetProgress(float value) => bar.Progress = value;

    private RenderTexture CreateTargetTexture(int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "JournalGrowthHUDVerification_" + width + "x" + height + "_TEMP",
            antiAliasing = 1, useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.DontSave
        };
        target.Create();
        renderTargets.Add(target);
        return target;
    }

    private void Check(bool passed, string message)
    {
        if (!passed) failures++;
        report.AppendLine((passed ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }

    private void WriteReport()
    {
        try
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CaptureDirectory));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Report.txt"), Result);
        }
        catch (Exception exception) { Debug.LogError("Could not write journal HUD report: " + exception, this); }
    }

    private void CleanupOwnedObjects()
    {
        if (cleaned) return;
        cleaned = true;
        if (runtimePanelSettings != null) runtimePanelSettings.targetTexture = null;
        if (documentObject != null) Destroy(documentObject);
        if (runtimePanelSettings != null) Destroy(runtimePanelSettings);
        for (int i = 0; i < renderTargets.Count; i++)
        {
            RenderTexture target = renderTargets[i];
            if (target == null) continue;
            target.Release();
            Destroy(target);
        }
        renderTargets.Clear();
        if (hasCapturedOriginalRunInBackground) Application.runInBackground = originalRunInBackground;
    }

    private void OnDestroy() { CleanupOwnedObjects(); }

    private static Sprite FindSprite(Sprite[] sprites, string name)
    {
        for (int i = 0; i < sprites.Length; i++)
            if (sprites[i] != null && sprites[i].name == name) return sprites[i];
        return null;
    }

    private static string DisplayPercent(float percent) =>
        percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private readonly struct CaptureSpec
    {
        public readonly int Width, Height;
        public readonly float Percent;
        public CaptureSpec(int width, int height, float percent) { Width = width; Height = height; Percent = percent; }
    }
}
#endif
