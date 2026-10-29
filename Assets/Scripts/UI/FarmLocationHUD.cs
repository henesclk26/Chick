using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Top left of the screen: a map pin and the name of the part of the farm the bird is in. The farm is split along its
/// fences and gates (world XZ rectangles, below). On a new region the old name lifts up and fades, the new one rises in
/// from below and the pin hops (ClockHUD.uss). A region only takes over once the bird is a little way into it, so
/// standing in a gateway does not flicker between two names; outside every region the last name stays.
/// Shown and hidden with the clock (ClockHUDController.SetVisible). Presentation only.
/// </summary>
public sealed class FarmLocationHUD : MonoBehaviour
{
    [System.Serializable]
    private struct Region
    {
        public string name;
        [Tooltip("World X / Z extent: x, y = min X, min Z; width, height = size.")] public Rect area;
    }

    [SerializeField] private UIDocument document;
    [SerializeField] private Region[] regions =
    {
        // north of the barn-to-field fence (z 9), split by the barn-to-coop fence and Gate 3 (x 99)
        new Region { name = "Kümes bölgesi", area = Rect.MinMaxRect(99f, 9f, 129f, 36f) },
        new Region { name = "Ahır bölgesi", area = Rect.MinMaxRect(59f, 9f, 99f, 36f) },
        // south of it down to the pasture fence (z -20), split by the fence at x 90
        new Region { name = "Tarla bölgesi", area = Rect.MinMaxRect(59f, -20f, 90f, 9f) },
        new Region { name = "Ağaçlık bölge", area = Rect.MinMaxRect(90f, -20f, 129f, 9f) },
        // past the crop-fields-to-sheep-pasture gate
        new Region { name = "Otlak bölgesi", area = Rect.MinMaxRect(59f, -50f, 129f, -20f) },
    };
    [SerializeField, Min(0f), Tooltip("How far (m) into a region the bird must be before its name shows.")]
    private float enterMargin = .4f;

    private static readonly Color Ink = new Color(.13f, .11f, .086f), Rim = new Color(1f, .976f, .91f, .55f);

    private VisualElement names, pin;
    private Label current;
    private Transform bird;
    private int shown = -1;
    private float nextBirdLookup;

    private void Start()
    {
        var root = document.rootVisualElement;
        names = root.Q("LocationNames");
        pin = root.Q("LocationPin");
        pin.generateVisualContent += DrawPin;
    }

    private void Update()
    {
        if (names == null) return;
        if (bird == null && Time.unscaledTime >= nextBirdLookup)
        {
            var player = FindFirstObjectByType<ChickPlayerController>();
            bird = player != null ? player.transform : null;
            nextBirdLookup = Time.unscaledTime + 1f;
        }
        if (bird == null) return;

        var at = new Vector2(bird.position.x, bird.position.z);
        if (shown >= 0 && regions[shown].area.Contains(at)) return;
        for (int i = 0; i < regions.Length; i++)
        {
            var r = regions[i].area;
            if (at.x > r.xMin + enterMargin && at.x < r.xMax - enterMargin && at.y > r.yMin + enterMargin && at.y < r.yMax - enterMargin)
            {
                Show(i);
                return;
            }
        }
    }

    private void Show(int index)
    {
        if (index == shown) return;
        bool first = shown < 0;
        shown = index;

        // the old name lifts away, then goes
        var old = current;
        if (old != null)
        {
            old.AddToClassList("leave");
            old.schedule.Execute(() => old.RemoveFromHierarchy()).StartingIn(350);
        }
        current = new Label(regions[index].name) { pickingMode = PickingMode.Ignore };
        current.AddToClassList("location-name");
        if (!first) current.AddToClassList("enter");
        names.Add(current);
        var entering = current;
        // one frame in the start pose, so the transition has something to run from
        if (!first) entering.schedule.Execute(() => entering.RemoveFromClassList("enter")).StartingIn(30);

        if (!first)
        {
            pin.AddToClassList("hop");
            pin.schedule.Execute(() => pin.RemoveFromClassList("hop")).StartingIn(180);
        }
    }

    // A map pin: a round head running down to a point, with a hole through the head.
    private static void DrawPin(MeshGenerationContext context)
    {
        Rect r = context.visualElement.contentRect;
        float radius = r.width*.5f - 1f;
        var centre = new Vector2(r.width*.5f, radius + 1f);
        var tip = new Vector2(r.width*.5f, r.height - 1f);
        // where the straight sides leave the head, tangent to it
        float reach = tip.y - centre.y, side = Mathf.Acos(Mathf.Clamp(radius/reach, -1f, 1f))*Mathf.Rad2Deg;
        float right = 90f - side, left = 90f + side;

        var p = context.painter2D;
        p.fillColor = Ink; p.strokeColor = Rim; p.lineWidth = 1.2f; p.lineJoin = LineJoin.Round;
        p.BeginPath();
        p.MoveTo(tip);
        p.LineTo(centre + Polar(right, radius));
        p.Arc(centre, radius, right, left - 360f, ArcDirection.CounterClockwise);
        p.ClosePath();
        p.MoveTo(centre + Polar(0f, radius*.38f));
        p.Arc(centre, radius*.38f, 0f, 360f);
        p.ClosePath();
        p.Fill(FillRule.OddEven);
        p.Stroke();
    }

    private static Vector2 Polar(float degrees, float length)
    {
        float a = degrees*Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a))*length;
    }
}
