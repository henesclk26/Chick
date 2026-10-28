using UnityEngine;
using UnityEngine.UIElements;

public sealed class ClockHUDController : MonoBehaviour
{
    // segments a, b, c, d, e, f, g lit for 0-9
    private static readonly string[] Lit = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
    private static readonly Color Ink = new Color(.094f, .086f, .078f), Rim = new Color(1f, .98f, .93f, .5f);
    private const float Thickness = 7f, Gap = 1.2f;

    [SerializeField] private UIDocument document;
    [SerializeField, Tooltip("A seven-segment readout instead of the time lettered in Fredoka.")]
    private bool segments;
    private VisualElement hud;
    private Label clock;
    private readonly VisualElement[] digits = new VisualElement[4];
    private readonly int[] values = new int[4];
    private int lastMinute = -1;
    public void Bind()
    {
        hud = document.rootVisualElement.Q("ClockHUD");
        clock = document.rootVisualElement.Q<Label>("ClockText");
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            if (digits[i] != null) continue;
            digits[i] = document.rootVisualElement.Q("D" + i);
            digits[i].generateVisualContent += context => DrawDigit(context, digits[index], values[index]);
        }
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        hud.pickingMode = PickingMode.Ignore;
        hud.EnableInClassList("segments", segments);
        lastMinute = -1;
    }
    public void SetVisible(bool visible) => hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    public void SetMinute(int minute)
    {
        if (minute == lastMinute) return;
        lastMinute = minute;
        clock.text = $"{minute / 60:00}:{minute % 60:00}";
        int hours = minute/60%100, minutes = minute%60;
        Show(0, hours/10); Show(1, hours%10); Show(2, minutes/10); Show(3, minutes%10);
    }

    private void Show(int index, int value)
    {
        values[index] = value;
        digits[index]?.MarkDirtyRepaint();
    }

    // The digit's lit segments as pointed bars, like an LED readout: each bar runs between the centres of the corners
    // and ends in a point, so neighbouring bars meet in small mitred gaps and all of them are the same length.
    private static void DrawDigit(MeshGenerationContext context, VisualElement digit, int value)
    {
        Rect r = digit.contentRect;
        float t = Thickness, half = t*.5f, left = half, right = r.width - half, top = half, middle = r.height*.5f, bottom = r.height - half;
        var p = context.painter2D;
        p.fillColor = Ink; p.strokeColor = Rim; p.lineWidth = 1f; p.lineJoin = LineJoin.Miter;
        foreach (char s in Lit[value])
        {
            switch (s)
            {
                case 'a': Bar(p, new Vector2(left, top), new Vector2(right, top), half); break;
                case 'g': Bar(p, new Vector2(left, middle), new Vector2(right, middle), half); break;
                case 'd': Bar(p, new Vector2(left, bottom), new Vector2(right, bottom), half); break;
                case 'f': Bar(p, new Vector2(left, top), new Vector2(left, middle), half); break;
                case 'b': Bar(p, new Vector2(right, top), new Vector2(right, middle), half); break;
                case 'e': Bar(p, new Vector2(left, middle), new Vector2(left, bottom), half); break;
                case 'c': Bar(p, new Vector2(right, middle), new Vector2(right, bottom), half); break;
            }
        }
    }

    // a bar from corner to corner, pulled in by the gap, with pointed ends
    private static void Bar(Painter2D p, Vector2 from, Vector2 to, float half)
    {
        Vector2 dir = (to - from).normalized, side = new Vector2(-dir.y, dir.x)*half;
        Vector2 a = from + dir*Gap, b = to - dir*Gap;
        p.BeginPath();
        p.MoveTo(a);
        p.LineTo(a + dir*half + side);
        p.LineTo(b - dir*half + side);
        p.LineTo(b);
        p.LineTo(b - dir*half - side);
        p.LineTo(a + dir*half - side);
        p.ClosePath();
        p.Fill();
        p.Stroke();
    }

    private void OnValidate()
    {
        if (hud != null) hud.EnableInClassList("segments", segments);
    }
}
