using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Reuses the supplied reference artwork as an atlas. Text, prices, navigation and
// progress remain real UI Toolkit elements; no screenshot is used as the menu.
public static class GameplayJournalReferenceTheme
{
    private static Texture2D atlas;
    private static Texture2D goldEgg;
    private static Texture2D whiteEgg;
    private static Texture2D Atlas => atlas != null ? atlas : (atlas = Resources.Load<Texture2D>("Journal/ReferenceAtlas"));
    // Atlas rows (top-down) of the "Yükseltmeler" tab that the reference artwork shows as selected.
    private const float BakedTabTop = 176f;
    private const float BakedTabBottom = 306f;

    public static void Apply(VisualElement root)
    {
        if (root == null || Atlas == null || root.ClassListContains("reference-skin")) return;
        root.AddToClassList("reference-skin");
        var sheet = Resources.Load<StyleSheet>("Journal/ReferenceTheme");
        if (sheet != null) root.styleSheets.Add(sheet);
        var window = root.Q(className: "journal-window");
        var panel = new AtlasSurface(new Rect(195, 63, 1280, 815), new Rect(224, 583, 187, 148), 18, true);
        Underlay(window, panel);
        var seam = Crop(new Rect(435, 90, 8, 762));
        seam.AddToClassList("reference-seam");
        // The reference shows "Yükseltmeler" selected; cover that baked tab edge with clean seam
        // so only the live side-active tab is highlighted.
        var seamPatch = Crop(new Rect(435, BakedTabBottom + 6, 8, BakedTabBottom - BakedTabTop));
        seamPatch.style.position = Position.Absolute;
        seamPatch.style.left = 0;
        seamPatch.style.width = 8;
        seamPatch.style.top = BakedTabTop - 90;
        seamPatch.style.height = BakedTabBottom - BakedTabTop;
        seam.Add(seamPatch);
        window.Add(seam);

        var cards = root.Query<VisualElement>(className: "upgrade-card").ToList();
        foreach (var card in cards)
            Underlay(card, new AtlasSurface(new Rect(461, 186, 987, 155), new Rect(976, 210, 224, 113), 12));

        CropInto(root.Q<Image>("peck-art"), new Rect(476, 357, 146, 131));
        CropInto(root.Q<Image>("range-art"), new Rect(476, 516, 146, 131));
        CropInto(root.Q<Image>("helper-art"), new Rect(476, 676, 146, 131));
        CropInto(root.Q<Image>("helper-speed-art"), new Rect(476, 676, 146, 131));
        CropInto(root.Q<Image>("helper-range-art"), new Rect(476, 676, 146, 131));
        root.Q<Image>("range-art").parent.Q<Label>(className: "upgrade-description").text = "Yakındaki tohumlara daha rahat ulaş.";

        foreach (var name in new[] { "peck-price", "range-price", "helper-price", "helper-speed-price", "helper-range-price", "sprint-speed-price", "sprint-duration-price", "double-collect-price", "double-jump-price", "glide-price" })
        {
            var button = root.Q<Button>(name);
            var normal = new AtlasSurface(new Rect(1232, 220, 192, 92), new Rect(1249, 243, 22, 41), 13);
            normal.AddToClassList("price-normal-art");
            Underlay(button, normal);
            var max = new AtlasSurface(new Rect(1232, 538, 192, 92), new Rect(1250, 559, 23, 41), 13);
            max.AddToClassList("price-max-art");
            Underlay(button, max);
        }
        // The atlas brush sits on lighter paper than the journal; this keyed copy keeps only the stroke.
        var balanceBrush = Resources.Load<Texture2D>("Journal/BalanceBrush");
        if (balanceBrush != null)
            Underlay(root.Q(className: "egg-balance"), new Image { image = balanceBrush, scaleMode = ScaleMode.StretchToFill });

        var back = root.Q<Button>("journal-back");
        Underlay(back, Crop(new Rect(224, 757, 194, 79)));
        back.tooltip = "Oyuna dön (TAB / ESC)";
        var close = root.Q<Button>("journal-close");
        close.text = "";
        close.tooltip = "Kapat (ESC)";
        Underlay(close, Crop(new Rect(1393, 86, 59, 57)));

        var footer = root.Q(className: "journal-footer");
        foreach (var child in footer.Children()) child.style.display = DisplayStyle.None;
        Underlay(footer, Crop(new Rect(1260, 823, 176, 40)));

        int glyphIndex = 0;
        foreach (var name in new[] { "upgrades-tab", "shop-tab", "statistics-tab" })
        {
            var button = root.Q<Button>(name);
            var oldIcon = button.Q<Label>(className: "nav-icon");
            if (oldIcon != null) oldIcon.style.display = DisplayStyle.None;
            var glyph = new NavGlyph(button, glyphIndex++);
            glyph.AddToClassList("reference-nav-glyph");
            button.Insert(0, glyph);
            var active = new AtlasSurface(new Rect(216, 184, 220, 115), new Rect(225, 186, 193, 24), 0);
            active.AddToClassList("nav-active-art");
            Underlay(button, active);
            var stripe = new VisualElement { pickingMode = PickingMode.Ignore };
            stripe.AddToClassList("nav-gold-stripe");
            button.Add(stripe);
            button.RegisterCallback<GeometryChangedEvent>(_ => glyph.MarkDirtyRepaint());
        }

        System.Action resize = () =>
        {
            float width = root.resolvedStyle.width;
            float height = root.resolvedStyle.height;
            if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height)) return;
            float scale = Mathf.Min(width * .78f / 1280f, height * .87f / 815f);
            window.style.scale = new Scale(new Vector3(scale, scale, 1));
        };
        root.RegisterCallback<GeometryChangedEvent>(_ => resize());
        root.schedule.Execute(resize);
    }

    public static void RefreshNavigation(VisualElement root)
    {
        if (root == null) return;
        root.Query<NavGlyph>().ForEach(g => g.MarkDirtyRepaint());
    }

    public static Image CreateLevelEgg(bool filled)
    {
        if (goldEgg == null) goldEgg = Resources.Load<Texture2D>("Journal/GoldEgg");
        if (whiteEgg == null) whiteEgg = Resources.Load<Texture2D>("Journal/Egg");
        return new Image { image = filled ? goldEgg : whiteEgg, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, uv = new Rect(.1f, .04f, .8f, .9f) };
    }

    private static Image Crop(Rect source)
    {
        var image = new Image { pickingMode = PickingMode.Ignore };
        CropInto(image, source);
        return image;
    }

    private static void CropInto(Image image, Rect source)
    {
        if (image == null) return;
        image.image = Atlas;
        image.uv = new Rect(source.x / Atlas.width, 1f - source.yMax / Atlas.height, source.width / Atlas.width, source.height / Atlas.height);
        image.scaleMode = ScaleMode.StretchToFill;
        image.tintColor = Color.white;
    }

    private static void Underlay(VisualElement parent, VisualElement art)
    {
        if (parent == null) return;
        art.AddToClassList("reference-surface");
        art.pickingMode = PickingMode.Ignore;
        parent.Insert(0, art);
    }

    // Nine-slice outlines preserve the hand-drawn corners; the centre tiles a
    // clean area of the original material so it contains no baked text or price.
    private sealed class AtlasSurface : VisualElement
    {
        private readonly Rect source;
        private readonly Rect tile;
        private readonly float border;
        private readonly bool paper;
        private readonly List<Piece> pieces = new List<Piece>(128);
        private struct Piece { public Rect destination; public Rect source; }

        public AtlasSurface(Rect source, Rect tile, float border, bool paper = false)
        {
            this.source = source; this.tile = tile; this.border = border; this.paper = paper;
            generateVisualContent += Paint;
        }

        private void Add(Rect destination, Rect sample)
        {
            if (destination.width > 0 && destination.height > 0)
                pieces.Add(new Piece { destination = destination, source = sample });
        }

        private void Paint(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0 || Atlas == null) return;
            pieces.Clear();
            float b = Mathf.Min(border, Mathf.Min(w, h) * .5f);
            // Fixed source-size tiling retains visible paper and brush grain.
            for (float y = b; y < h - b; y += tile.height)
                for (float x = b; x < w - b; x += tile.width)
                {
                    float tw = Mathf.Min(tile.width, w - b - x), th = Mathf.Min(tile.height, h - b - y);
                    Add(new Rect(x, y, tw, th), new Rect(tile.x, tile.y, tw, th));
                }
            if (b > 0)
            {
                Add(new Rect(0, 0, b, b), new Rect(source.x, source.y, border, border));
                Add(new Rect(w-b, 0, b, b), new Rect(source.xMax-border, source.y, border, border));
                Add(new Rect(0, h-b, b, b), new Rect(source.x, source.yMax-border, border, border));
                Add(new Rect(w-b, h-b, b, b), new Rect(source.xMax-border, source.yMax-border, border, border));
                Add(new Rect(b, 0, w-2*b, b), new Rect(source.x+border, source.y, source.width-2*border, border));
                Add(new Rect(b, h-b, w-2*b, b), new Rect(source.x+border, source.yMax-border, source.width-2*border, border));
                Add(new Rect(0, b, b, h-2*b), new Rect(source.x, source.y+border, border, source.height-2*border));
                Add(new Rect(w-b, b, b, h-2*b), new Rect(source.xMax-border, source.y+border, border, source.height-2*border));
            }
            // The reference has broad hatched paper corners, beyond the edge strip.
            if (paper)
            {
                // The left edge strip crosses the baked selected tab; overlay clean edge from below it.
                float edgeScale = (h - 2 * b) / (source.height - 2 * border);
                float patchHeight = BakedTabBottom - BakedTabTop;
                Add(new Rect(0, b + (BakedTabTop - source.y - border) * edgeScale, b, patchHeight * edgeScale),
                    new Rect(source.x, BakedTabBottom + 6, border, patchHeight));
                Add(new Rect(0,0,65,65), new Rect(source.x,source.y,65,65));
                Add(new Rect(w-44,0,44,44), new Rect(source.xMax-44,source.y,44,44));
                Add(new Rect(0,h-37,37,37), new Rect(source.x,source.yMax-37,37,37));
                Add(new Rect(w-65,h-65,65,65), new Rect(source.xMax-65,source.yMax-65,65,65));
            }
            var mesh = context.Allocate(pieces.Count * 4, pieces.Count * 6, Atlas);
            ushort index = 0;
            foreach (var piece in pieces)
            {
                Rect d = piece.destination, s = piece.source;
                float u0 = s.x / Atlas.width;
                float u1 = s.xMax / Atlas.width;
                float v0 = 1-s.y/Atlas.height;
                float v1 = 1-s.yMax/Atlas.height;
                mesh.SetNextVertex(new Vertex { position = new Vector3(d.x,d.y,Vertex.nearZ), tint = Color.white, uv = new Vector2(u0,v0) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(d.xMax,d.y,Vertex.nearZ), tint = Color.white, uv = new Vector2(u1,v0) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(d.xMax,d.yMax,Vertex.nearZ), tint = Color.white, uv = new Vector2(u1,v1) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(d.x,d.yMax,Vertex.nearZ), tint = Color.white, uv = new Vector2(u0,v1) });
                mesh.SetNextIndex(index); mesh.SetNextIndex((ushort)(index+1)); mesh.SetNextIndex((ushort)(index+2));
                mesh.SetNextIndex((ushort)(index+2)); mesh.SetNextIndex((ushort)(index+3)); mesh.SetNextIndex(index);
                index += 4;
            }
        }
    }

    private sealed class NavGlyph : VisualElement
    {
        private readonly Button owner;
        private readonly int kind;
        public NavGlyph(Button owner, int kind)
        {
            this.owner = owner; this.kind = kind;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }
        private void Paint(MeshGenerationContext context)
        {
            Color color = owner.ClassListContains("side-active") ? new Color(.98f,.94f,.77f) : new Color(.20f,.15f,.09f);
            var p = context.painter2D;
            p.fillColor = color; p.strokeColor = color; p.lineWidth = 4;
            if (kind == 1)
            {
                Polygon(p, new Vector2(5,8),new Vector2(44,8),new Vector2(50,27),new Vector2(44,32),new Vector2(37,30),new Vector2(30,32),new Vector2(24,29),new Vector2(17,32),new Vector2(10,30),new Vector2(3,31),new Vector2(0,26));
                Box(p,5,30,6,23); Box(p,43,30,6,23); Box(p,8,48,40,5); Box(p,22,36,12,17);
                p.strokeColor = new Color(.86f,.81f,.66f); p.lineWidth = 2;
                p.BeginPath(); p.MoveTo(new Vector2(17,11)); p.LineTo(new Vector2(14,25)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(32,11)); p.LineTo(new Vector2(35,25)); p.Stroke();
            }
            else
            {
                Box(p,3,kind==0?39:34,11,20); Box(p,19,kind==0?31:21,11,kind==0?28:33); Box(p,35,kind==0?20:9,11,kind==0?39:45);
                if (kind == 0)
                {
                    p.BeginPath(); p.MoveTo(new Vector2(5,31)); p.LineTo(new Vector2(18,20)); p.LineTo(new Vector2(26,24)); p.LineTo(new Vector2(44,5)); p.Stroke();
                    Polygon(p,new Vector2(32,5),new Vector2(47,3),new Vector2(46,18));
                }
            }
        }
        private static void Box(Painter2D p,float x,float y,float w,float h) => Polygon(p,new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h));
        private static void Polygon(Painter2D p,params Vector2[] points)
        {
            p.BeginPath(); p.MoveTo(points[0]); for(int i=1;i<points.Length;i++) p.LineTo(points[i]); p.ClosePath(); p.Fill();
        }
    }
}
