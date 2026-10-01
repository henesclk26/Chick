using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Clockwise UI Toolkit ring, starting at twelve o'clock.</summary>
[UxmlElement]
public partial class UpgradeHoldIndicator : VisualElement
{
    private float progress;
    public float Progress
    {
        get => progress;
        set
        {
            float next = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            if (progress == next) return;
            progress = next;
            MarkDirtyRepaint();
        }
    }

    public UpgradeHoldIndicator()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        float outer = Mathf.Min(r.width, r.height) * .5f - 1f;
        if (outer <= 5f) return;
        Ring(context, r.center, outer, outer - 4f, 1f, new Color(.23f, .26f, .20f));
        if (progress > 0f)
            Ring(context, r.center, outer, outer - 4f, progress, new Color(.48f, .94f, .34f));
    }

    private static void Ring(MeshGenerationContext context, Vector2 center, float outer, float inner,
        float fraction, Color color)
    {
        int segments = Mathf.Max(1, Mathf.CeilToInt(96f * fraction));
        var mesh = context.Allocate((segments + 1) * 2, segments * 6);
        for (int i = 0; i <= segments; i++)
        {
            float angle = (-.25f + fraction * i / segments) * Mathf.PI * 2f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            var a = center + direction * outer;
            var b = center + direction * inner;
            mesh.SetNextVertex(new Vertex { position = new Vector3(a.x, a.y, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(b.x, b.y, Vertex.nearZ), tint = color });
        }
        for (int i = 0; i < segments; i++)
        {
            ushort a = (ushort)(i * 2);
            mesh.SetNextIndex(a); mesh.SetNextIndex((ushort)(a + 2)); mesh.SetNextIndex((ushort)(a + 1));
            mesh.SetNextIndex((ushort)(a + 1)); mesh.SetNextIndex((ushort)(a + 2)); mesh.SetNextIndex((ushort)(a + 3));
        }
    }
}
