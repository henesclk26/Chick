using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A continuous parchment growth bar for the farming journal HUD.</summary>
[UxmlElement]
public partial class GrowthProgressBarElement : VisualElement
{
    private const string AtlasPath = "JournalGrowthHUD/HudAtlas";
    private const float FrameLeft = 4f;
    private const float FrameTop = 8f;
    private const float FrameHeight = 36f;
    private const float NativeFrameHeight = 179f;
    private const float NativeTrackInset = 31f;
    private const float CursorSize = 22f;

    private static Sprite[] atlas;
    private static Sprite frameSprite;
    private static Sprite fillSprite;
    private static Sprite cursorSprite;

    private readonly VisualElement frame;
    private readonly VisualElement track;
    private readonly VisualElement reveal;
    private readonly Image fill;
    private readonly Image cursor;

    private float progress;
    private float eatingHighlight;
    private float trackWidth;
    private float cursorPosition;

    /// <summary>Optional warm highlight while the chick is eating.</summary>
    public float EatingHighlight
    {
        get => eatingHighlight;
        set
        {
            float next = Sanitize01(value);
            if (eatingHighlight == next) return;
            eatingHighlight = next;
            RefreshSprites();
        }
    }

    /// <summary>Normalized growth from zero through one.</summary>
    public float Progress
    {
        get => progress;
        set
        {
            float next = Sanitize01(value);
            if (progress == next) return;
            progress = next;
            RefreshSprites();
        }
    }

    /// <summary>Current revealed fill width in local panel units.</summary>
    public float FillWidth => trackWidth * progress;

    /// <summary>Continuous track width in local panel units.</summary>
    public float TrackWidth => trackWidth;

    /// <summary>Cursor center X in this element's local coordinates.</summary>
    public float CursorPosition => cursorPosition;

    /// <summary>Whether all three journal HUD sprites are available.</summary>
    public bool HasArtwork => EnsureSprites();

    public GrowthProgressBarElement()
    {
        pickingMode = PickingMode.Ignore;
        style.position = Position.Relative;
        style.width = Length.Percent(100);
        style.minWidth = 240;
        style.maxWidth = 740;
        style.height = 70;
        style.flexShrink = 0;
        style.overflow = Overflow.Visible;

        frame = new VisualElement
        {
            name = "growth-frame",
            pickingMode = PickingMode.Ignore
        };
        frame.style.position = Position.Absolute;
        frame.style.unitySliceLeft = 90;
        frame.style.unitySliceTop = 0;
        frame.style.unitySliceRight = 90;
        frame.style.unitySliceBottom = 0;
        frame.style.unitySliceScale = FrameHeight / NativeFrameHeight;
        Add(frame);

        track = new VisualElement
        {
            name = "growth-track",
            pickingMode = PickingMode.Ignore
        };
        track.style.position = Position.Absolute;
        track.style.overflow = Overflow.Hidden;
        Add(track);

        reveal = new VisualElement
        {
            name = "growth-reveal",
            pickingMode = PickingMode.Ignore
        };
        reveal.style.position = Position.Absolute;
        reveal.style.left = 0;
        reveal.style.top = 0;
        reveal.style.overflow = Overflow.Hidden;
        track.Add(reveal);

        fill = CreateImage("growth-fill");
        fill.style.position = Position.Absolute;
        fill.style.left = 0;
        fill.style.top = 0;
        reveal.Add(fill);

        cursor = CreateImage("growth-cursor");
        cursor.style.position = Position.Absolute;
        Add(cursor);

        RegisterCallback<GeometryChangedEvent>(_ => UpdateGeometry());
        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            EnsureSprites();
            if (frameSprite != null)
                frame.style.backgroundImage = Background.FromSprite(frameSprite);
            RefreshSprites();
        });
        UpdateGeometry();
    }

    /// <summary>Returns the exact continuous reveal edge for a normalized value.</summary>
    public static Vector2 FillEdge(float width, float height, float value)
    {
        float edge = Mathf.Max(0f, IsFinite(width) ? width : 0f) * Sanitize01(value);
        return new Vector2(edge, edge);
    }

    private static Image CreateImage(string elementName)
    {
        return new Image
        {
            name = elementName,
            scaleMode = ScaleMode.StretchToFill,
            pickingMode = PickingMode.Ignore
        };
    }

    private static bool EnsureSprites()
    {
        if (frameSprite == null || fillSprite == null || cursorSprite == null)
        {
            atlas = Resources.LoadAll<Sprite>(AtlasPath);
            frameSprite = FindSprite("Frame");
            fillSprite = FindSprite("Fill");
            cursorSprite = FindSprite("Cursor");
        }
        return frameSprite != null && fillSprite != null && cursorSprite != null;
    }

    private static Sprite FindSprite(string spriteName)
    {
        if (atlas == null) return null;
        for (int i = 0; i < atlas.Length; i++)
            if (atlas[i] != null && atlas[i].name == spriteName)
                return atlas[i];
        return null;
    }

    private static float Sanitize01(float value)
    {
        if (float.IsNaN(value) || float.IsNegativeInfinity(value)) return 0f;
        if (float.IsPositiveInfinity(value)) return 1f;
        return Mathf.Clamp01(value);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void UpdateGeometry()
    {
        float width = contentRect.width;
        float height = contentRect.height;
        if (!IsFinite(width) || !IsFinite(height) || width <= 0f || height <= 0f) return;

        EnsureSprites();
        float frameWidth = Mathf.Max(0f, width - FrameLeft * 2f);
        float trackInset = NativeTrackInset * FrameHeight / NativeFrameHeight;
        float trackHeight = FrameHeight - trackInset * 2f;
        float trackLeft = FrameLeft + trackInset;
        float trackTop = FrameTop + trackInset;
        trackWidth = Mathf.Max(0f, frameWidth - trackInset * 2f);

        frame.style.left = FrameLeft;
        frame.style.top = FrameTop;
        frame.style.width = frameWidth;
        frame.style.height = FrameHeight;
        if (frameSprite != null)
            frame.style.backgroundImage = Background.FromSprite(frameSprite);

        track.style.left = trackLeft;
        track.style.top = trackTop;
        track.style.width = trackWidth;
        track.style.height = trackHeight;
        track.style.borderTopLeftRadius = trackHeight * .5f;
        track.style.borderTopRightRadius = trackHeight * .5f;
        track.style.borderBottomLeftRadius = trackHeight * .5f;
        track.style.borderBottomRightRadius = trackHeight * .5f;

        reveal.style.height = trackHeight;
        fill.style.width = trackWidth;
        fill.style.height = trackHeight;

        cursor.style.width = CursorSize;
        cursor.style.height = CursorSize;
        cursor.style.top = FrameTop + FrameHeight + 2f;
        RefreshSprites();
    }

    private void RefreshSprites()
    {
        EnsureSprites();

        fill.sprite = fillSprite;
        cursor.sprite = cursorSprite;

        float revealWidth = trackWidth * progress;
        reveal.style.width = revealWidth;
        cursorPosition = FrameLeft + NativeTrackInset * FrameHeight / NativeFrameHeight + revealWidth;
        cursor.style.left = cursorPosition - CursorSize * .5f;

        // A restrained warm tint preserves the artwork while the controller signals eating.
        Color tint = Color.Lerp(Color.white, new Color(1f, .88f, .68f, 1f), eatingHighlight * .18f);
        fill.tintColor = tint;
    }
}
