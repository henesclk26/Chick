using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Round sprint gauge: a green ring that fills clockwise from the top around a runner icon,
/// with a SHIFT key badge. Chick and chicken icons cross-fade when the player grows.
/// </summary>
[UxmlElement]
public partial class SprintStaminaGauge : VisualElement
{
    private const float RingWidth = 15f;
    private const float Gap = 3f;

    private static readonly Color Ink = new Color32(40, 29, 18, 255);
    private static readonly Color Rim = new Color32(176, 138, 82, 255);
    private static readonly Color Track = new Color32(62, 55, 42, 255);
    private static readonly Color Green = new Color32(98, 170, 62, 255);
    private static readonly Color GreenLight = new Color32(142, 206, 96, 255);

    private readonly VisualElement ring;
    private readonly Image chickIcon;
    private readonly Image chickenIcon;
    private readonly Label shift;

    private float fill = 1f;
    private float morph;
    private float sparkle;

    /// <summary>Remaining stamina, 0..1.</summary>
    public float Fill
    {
        get => fill;
        set
        {
            float next = Mathf.Clamp01(float.IsNaN(value) ? 1f : value);
            if (Mathf.Approximately(fill, next)) return;
            fill = next;
            ring.MarkDirtyRepaint();
        }
    }

    /// <summary>0 shows the chick, 1 the chicken; values between are the growth cross-fade.</summary>
    public float Morph
    {
        get => morph;
        set
        {
            morph = Mathf.Clamp01(value);
            // The chick shrinks away while the chicken pops in slightly larger, then settles.
            float chickenIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, 1f, morph));
            float chickOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .6f, morph));
            float pop = 1f + .12f * Mathf.Sin(chickenIn * Mathf.PI);
            chickIcon.style.opacity = 1f - chickOut;
            chickIcon.style.scale = new Scale(Vector3.one * Mathf.Lerp(1f, .7f, chickOut));
            chickenIcon.style.opacity = chickenIn;
            chickenIcon.style.scale = new Scale(Vector3.one * Mathf.Lerp(.7f, 1f, chickenIn) * pop);
        }
    }

    /// <summary>0..1 strength of the green "refilling" marks next to the ring.</summary>
    public float Sparkle
    {
        get => sparkle;
        set
        {
            float next = Mathf.Clamp01(value);
            if (Mathf.Approximately(sparkle, next)) return;
            sparkle = next;
            ring.MarkDirtyRepaint();
        }
    }

    public Image ChickIcon => chickIcon;
    public Image ChickenIcon => chickenIcon;

    public SprintStaminaGauge()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("sprint-gauge");

        ring = new VisualElement { name = "sprint-gauge-ring", pickingMode = PickingMode.Ignore };
        ring.AddToClassList("sprint-gauge__ring");
        ring.generateVisualContent += DrawRing;
        Add(ring);

        chickIcon = CreateIcon("sprint-gauge-chick");
        chickenIcon = CreateIcon("sprint-gauge-chicken");
        ring.Add(chickIcon);
        ring.Add(chickenIcon);

        shift = new Label("SHIFT") { name = "sprint-gauge-key", pickingMode = PickingMode.Ignore };
        shift.AddToClassList("sprint-gauge__key");
        Add(shift);

        ring.RegisterCallback<GeometryChangedEvent>(_ => LayoutIcons());
        Morph = 0f;
    }

    private static Image CreateIcon(string elementName)
    {
        var image = new Image { name = elementName, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        image.AddToClassList("sprint-gauge__icon");
        image.style.position = Position.Absolute;
        return image;
    }

    private void LayoutIcons()
    {
        Rect r = ring.contentRect;
        float radius = Mathf.Min(r.width, r.height) * .5f - RingWidth - Gap;
        if (radius <= 0f) return;
        foreach (Image icon in new[] { chickIcon, chickenIcon })
        {
            icon.style.left = r.center.x - radius;
            icon.style.top = r.center.y - radius;
            icon.style.width = radius * 2f;
            icon.style.height = radius * 2f;
        }
    }

    private void DrawRing(MeshGenerationContext context)
    {
        Rect r = ring.contentRect;
        float outer = Mathf.Min(r.width, r.height) * .5f;
        if (outer <= RingWidth) return;
        Vector2 c = r.center;
        float mid = outer - RingWidth * .5f - 1f;
        var p = context.painter2D;
        p.lineCap = LineCap.Butt;

        // Warm rim and dark track behind the stamina.
        Circle(p, c, outer - 1f, 2.5f, Rim);
        Circle(p, c, mid, RingWidth - 2f, Track);

        if (fill > .001f)
        {
            float end = -90f + 360f * fill;
            Arc(p, c, mid, RingWidth - 2f, -90f, end, Green);
            Arc(p, c, mid - RingWidth * .22f, 2.5f, -90f, end, GreenLight);
        }

        // Ink outlines and the small separator at twelve o'clock.
        Circle(p, c, outer - 2.5f, 2f, Ink);
        Circle(p, c, outer - RingWidth - 1.5f, 3f, Ink);
        p.strokeColor = Ink;
        p.lineWidth = 2.5f;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x, c.y - outer + 2f));
        p.LineTo(new Vector2(c.x, c.y - outer + RingWidth + 1f));
        p.Stroke();

        if (sparkle > .01f)
        {
            // Three short green strokes at the upper right, like a "refilling" twinkle.
            p.strokeColor = new Color(GreenLight.r, GreenLight.g, GreenLight.b, sparkle);
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            foreach (float angle in new[] { -62f, -48f, -34f })
            {
                float rad = angle * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                p.BeginPath();
                p.MoveTo(c + dir * (outer + 4f));
                p.LineTo(c + dir * (outer + 12f));
                p.Stroke();
            }
        }
    }

    private static void Circle(Painter2D p, Vector2 c, float radius, float width, Color color) =>
        Arc(p, c, radius, width, 0f, 360f, color);

    private static void Arc(Painter2D p, Vector2 c, float radius, float width, float from, float to, Color color)
    {
        p.strokeColor = color;
        p.lineWidth = width;
        p.BeginPath();
        p.Arc(c, radius, Angle.Degrees(from), Angle.Degrees(to));
        p.Stroke();
    }
}
