using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Makes a mushroom food. Put it on the object that is one mushroom (its meshes may be on children). Everything is
/// worked out when play starts, from how the mushroom stands in the scene at that moment, so a copy that is moved,
/// turned or made bigger simply works: how many bites it takes comes from its size on the farm (one; two from
/// <see cref="TwoBitesFrom"/>; three from <see cref="ThreeBitesFrom"/>; a selected one shows it in the editor), the
/// beak's bite point is at its foot, and its save id comes from where it stands.
/// A mushroom of two or three bites is eaten in that many pieces: seen from above it is split evenly through its
/// stem, in halves or in three slices like a pie, and each bite takes the piece nearest the bird. The cut faces
/// show the pale flesh inside, so a bitten mushroom is told at a glance; the cap's own top is never cut. The last
/// bite eats what is left (EatingFeedback shrinks it away). Crumbs fly in <see cref="crumbColour"/>. Helper chicks
/// leave mushrooms to the player.
/// </summary>
[DisallowMultipleComponent]
public sealed class EdibleMushroom : MonoBehaviour
{
    private const int EdibleLayer = 9;
    // One rule for every mushroom. Size on the farm (m): the cube root of its bounding box volume, or 3/4 of its
    // height if more. From TwoBitesFrom it takes two bites, from ThreeBitesFrom three.
    private const float TwoBitesFrom = .27f, ThreeBitesFrom = .37f;
    public const string StatisticKey = "mushroom";

    /// <summary>A bite that did not finish the mushroom (the last one raises EdibleObject.Consumed).</summary>
    public static event Action<EdibleMushroom> Bitten;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBiteListeners() => Bitten = null;

    [SerializeField] private Color crumbColour = new Color(.86f, .74f, .6f);

    // one mesh of the mushroom as it was, in world space
    private sealed class Part
    {
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Material[] materials;
        public Mesh copy;
        public Vector3[] positions, normals;
        public Vector2[] uvs;
        public int[][] submeshes;
        public Matrix4x4 toLocal;
    }

    private struct Cut { public Vector3 normal; public float offset; }   // keeps dot(p - foot, normal) <= offset

    private static ChickPlayerController eater;
    private static Material flesh;
    private readonly List<Part> parts = new List<Part>();
    // the pieces, seen from above: piece i is the slice centred on pieceAngle + i*360/Bites; the eaten ones in order
    private readonly List<int> eatenPieces = new List<int>();
    private float pieceAngle;
    private bool pieceAngleSet;
    private EdibleObject edible;
    private Vector3 foot;
    private bool measured;

    public Color CrumbColour => crumbColour;
    public int Bites { get; private set; } = 1;
    public int BitesTaken { get; private set; }
    public EdibleObject Edible => edible;
    private string SaveId => edible.PersistentId + "#bites";

    /// <summary>How many bites the mushroom takes at its present size (also in the editor).</summary>
    public int BitesForCurrentSize()
    {
        if (!WorldBounds(out Bounds b)) return 1;
        // its bulk, or for a tall thin one its height (taller than the chick is a mouthful too)
        float size = Mathf.Max(Mathf.Pow(Mathf.Max(b.size.x*b.size.y*b.size.z, 0f), 1f/3f), b.size.y*.75f);
        return size >= ThreeBitesFrom ? 3 : size >= TwoBitesFrom ? 2 : 1;
    }

    /// <summary>Editor setup: the crumb colour for this model.</summary>
    public void Configure(Color crumbs) => crumbColour = crumbs;

    private void Awake()
    {
        if (!WorldBounds(out Bounds b)) { enabled = false; return; }
        Bites = BitesForCurrentSize();
        foot = new Vector3(b.center.x, b.min.y, b.center.z);
        gameObject.layer = EdibleLayer;

        // The beak's target: at the stem's foot, where the bird pecks on the ground. The contact point the eater
        // draws to the beak has no mesh, so the mushroom itself never moves.
        var bite = new GameObject("Mushroom Bite") { layer = EdibleLayer }.transform;
        bite.SetParent(transform, false);
        bite.position = foot + Vector3.up*.015f;
        var trigger = bite.gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = .05f/Mathf.Max(MaxScale(bite), .0001f);
        var contact = new GameObject("ContactProxy").transform;
        contact.SetParent(bite, false);

        edible = GetComponent<EdibleObject>();
        if (edible == null) edible = gameObject.AddComponent<EdibleObject>();
        var p = transform.position;
        string id = string.Format(CultureInfo.InvariantCulture, "{0}/mushroom/{1}@{2:F3},{3:F3},{4:F3}", gameObject.scene.path, name, p.x, p.y, p.z);
        edible.ConfigureRuntime(id, EdibleCategory.Other, StatisticKey, "Mantar", bite, contact, 1.5f, .05f);
        edible.IgnoredByHelpers = true;
        if (Bites > 1) edible.BiteHandler = Bite;
    }

    private bool WorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponentInParent<EdibleMushroom>(true) != this) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static float MaxScale(Transform t)
    {
        var s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    // One peck landed. True when it was the last bite (the caller then eats the mushroom).
    private bool Bite()
    {
        if (++BitesTaken >= Bites) return true;
        if (eater == null) eater = FindFirstObjectByType<ChickPlayerController>();
        Vector3 toEater = eater != null ? eater.transform.position - foot : transform.forward;
        float toward = Mathf.Atan2(toEater.z, toEater.x)*Mathf.Rad2Deg;
        // the first bite lays the slices out with one facing the bird; each bite takes the one nearest it
        if (!pieceAngleSet) { pieceAngle = toward; pieceAngleSet = true; }
        int nearest = -1; float best = float.MaxValue;
        for (int i = 0; i < Bites; i++)
        {
            if (eatenPieces.Contains(i)) continue;
            float off = Mathf.Abs(Mathf.DeltaAngle(toward, PieceCentre(i)));
            if (off < best) { best = off; nearest = i; }
        }
        eatenPieces.Add(nearest);
        Reshape();
        Bitten?.Invoke(this);
        return false;
    }

    private float PieceCentre(int i) => pieceAngle + i*360f/Bites;

    // ---- the pieces ----

    private void Measure()
    {
        if (measured) return;
        measured = true;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponentInParent<EdibleMushroom>(true) != this) continue;
            var mesh = filter.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning($"{name}: mesh {mesh.name} is not readable; it stays whole between bites.", this); continue; }
            var renderer = filter.GetComponent<MeshRenderer>();
            var toWorld = filter.transform.localToWorldMatrix;
            var part = new Part
            {
                filter = filter, renderer = renderer, materials = renderer != null ? renderer.sharedMaterials : new Material[0],
                copy = new Mesh { name = mesh.name + " (bitten)" }, toLocal = filter.transform.worldToLocalMatrix,
                positions = mesh.vertices, normals = mesh.normals, uvs = mesh.uv, submeshes = new int[mesh.subMeshCount][],
            };
            for (int i = 0; i < part.positions.Length; i++) part.positions[i] = toWorld.MultiplyPoint3x4(part.positions[i]);
            if (part.normals.Length != part.positions.Length) part.normals = new Vector3[part.positions.Length];
            for (int i = 0; i < part.normals.Length; i++) part.normals[i] = toWorld.MultiplyVector(part.normals[i]).normalized;
            if (part.uvs.Length != part.positions.Length) part.uvs = new Vector2[part.positions.Length];
            for (int s = 0; s < mesh.subMeshCount; s++) part.submeshes[s] = mesh.GetTriangles(s);
            parts.Add(part);
        }
    }

    private void Reshape()
    {
        Measure();
        if (parts.Count == 0) return;
        // every piece left, each a slice between two upright cuts through the stem (a half: one cut)
        var slices = new List<List<Cut>>();
        for (int i = 0; i < Bites; i++)
        {
            if (eatenPieces.Contains(i)) continue;
            float half = 180f/Bites, centre = PieceCentre(i);
            slices.Add(Bites == 2
                ? new List<Cut> { new Cut { normal = -Flat(centre), offset = 0f } }
                : new List<Cut> { Side(centre - half, false), Side(centre + half, true) });
        }
        foreach (var part in parts)
        {
            var built = new Built();
            foreach (var slice in slices) Slice(part, slice, built);
            if (part.filter.sharedMesh != part.copy) part.filter.sharedMesh = part.copy;
            built.Write(part.copy, part.toLocal, part.submeshes.Length);
            if (part.renderer != null)
            {
                var materials = new Material[part.materials.Length + 1];
                part.materials.CopyTo(materials, 0);
                materials[materials.Length - 1] = Flesh();
                part.renderer.sharedMaterials = materials;
            }
        }
    }

    private static Vector3 Flat(float degrees) => new Vector3(Mathf.Cos(degrees*Mathf.Deg2Rad), 0f, Mathf.Sin(degrees*Mathf.Deg2Rad));

    // the upright cut through the stem at this angle, keeping the slice's side of it (counter-clockwise of the
    // slice's first edge, clockwise of its last)
    private static Cut Side(float degrees, bool last)
    {
        float r = degrees*Mathf.Deg2Rad;
        var turn = new Vector3(-Mathf.Sin(r), 0f, Mathf.Cos(r));
        return new Cut { normal = last ? turn : -turn, offset = 0f };
    }

    // The part's triangles kept on this side of every cut, and the cut faces closing the holes.
    private void Slice(Part part, List<Cut> cuts, Built into)
    {
        var piece = new Built();
        for (int s = 0; s < part.submeshes.Length; s++)
        {
            var t = part.submeshes[s];
            for (int i = 0; i < t.Length; i += 3)
            {
                var polygon = new List<Corner> { Corner.Of(part, t[i]), Corner.Of(part, t[i + 1]), Corner.Of(part, t[i + 2]) };
                piece.AddPolygon(s, polygon);
            }
        }
        foreach (var cut in cuts) piece.Clip(cut, foot);
        into.Add(piece);
    }

    // a corner of a polygon being cut
    private struct Corner
    {
        public Vector3 position, normal; public Vector2 uv;
        public static Corner Of(Part p, int i) => new Corner { position = p.positions[i], normal = p.normals[i], uv = p.uvs[i] };
        public static Corner Lerp(Corner a, Corner b, float t) => new Corner
        {
            position = Vector3.Lerp(a.position, b.position, t), normal = Vector3.Lerp(a.normal, b.normal, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t)
        };
    }

    // the mushroom's mesh as polygons (one list per submesh, the cut faces last), cut plane by plane
    private sealed class Built
    {
        private readonly Dictionary<int, List<List<Corner>>> polygons = new Dictionary<int, List<List<Corner>>>();
        private const int CapSubmesh = -1;

        public void Add(Built other)
        {
            foreach (var entry in other.polygons)
                foreach (var polygon in entry.Value) AddPolygon(entry.Key, polygon);
        }

        public void AddPolygon(int submesh, List<Corner> polygon)
        {
            if (!polygons.TryGetValue(submesh, out var list)) polygons[submesh] = list = new List<List<Corner>>();
            list.Add(polygon);
        }

        public void Clip(Cut cut, Vector3 origin)
        {
            var segments = new List<(Vector3 a, Vector3 b)>();
            foreach (var list in polygons.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var kept = ClipPolygon(list[i], cut, origin, segments);
                    if (kept == null) list.RemoveAt(i); else list[i] = kept;
                }
            foreach (var loop in Loops(segments)) AddCap(loop, cut.normal);
        }

        // Sutherland-Hodgman against one plane; null when nothing is left. The new edge on the plane is recorded.
        private static List<Corner> ClipPolygon(List<Corner> polygon, Cut cut, Vector3 origin, List<(Vector3, Vector3)> segments)
        {
            int n = polygon.Count, outside = 0;
            var side = new float[n];
            for (int i = 0; i < n; i++) { side[i] = Vector3.Dot(polygon[i].position - origin, cut.normal) - cut.offset; if (side[i] > 0f) outside++; }
            if (outside == 0) return polygon;
            if (outside == n) return null;
            var result = new List<Corner>(n + 2);
            var cross = new List<Vector3>(2);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1)%n;
                if (side[i] <= 0f) result.Add(polygon[i]);
                if ((side[i] <= 0f) != (side[j] <= 0f))
                {
                    var c = Corner.Lerp(polygon[i], polygon[j], side[i]/(side[i] - side[j]));
                    result.Add(c);
                    cross.Add(c.position);
                }
            }
            if (cross.Count == 2) segments.Add((cross[0], cross[1]));
            return result.Count >= 3 ? result : null;
        }

        // the cut edges joined end to end into closed outlines
        private static List<List<Vector3>> Loops(List<(Vector3 a, Vector3 b)> segments)
        {
            var loops = new List<List<Vector3>>();
            var unused = new List<(Vector3 a, Vector3 b)>(segments);
            const float join = 1e-4f;
            while (unused.Count > 0)
            {
                var loop = new List<Vector3> { unused[0].a, unused[0].b };
                unused.RemoveAt(0);
                bool grew = true;
                while (grew && unused.Count > 0)
                {
                    grew = false;
                    Vector3 end = loop[loop.Count - 1];
                    for (int i = 0; i < unused.Count; i++)
                    {
                        Vector3 next;
                        if ((unused[i].a - end).sqrMagnitude < join*join) next = unused[i].b;
                        else if ((unused[i].b - end).sqrMagnitude < join*join) next = unused[i].a;
                        else continue;
                        unused.RemoveAt(i);
                        if ((next - loop[0]).sqrMagnitude < join*join) { grew = false; goto closed; }
                        loop.Add(next);
                        grew = true;
                        break;
                    }
                }
                closed:
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        // the outline filled (ear clipping on the cut's plane), facing out of the cut
        private void AddCap(List<Vector3> loop, Vector3 normal)
        {
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized, v = Vector3.Cross(normal, u);
            var flat = new List<Vector2>(loop.Count);
            foreach (var p in loop) flat.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            float area = 0f;
            for (int i = 0; i < flat.Count; i++) { var a = flat[i]; var b = flat[(i + 1)%flat.Count]; area += a.x*b.y - b.x*a.y; }
            var order = new List<int>(loop.Count);
            for (int i = 0; i < loop.Count; i++) order.Add(area >= 0f ? i : loop.Count - 1 - i);   // counter-clockwise
            int guard = order.Count*order.Count;
            while (order.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < order.Count; i++)
                {
                    int ia = order[(i + order.Count - 1)%order.Count], ib = order[i], ic = order[(i + 1)%order.Count];
                    Vector2 a = flat[ia], b = flat[ib], c = flat[ic];
                    if ((b.x - a.x)*(c.y - a.y) - (b.y - a.y)*(c.x - a.x) <= 0f) continue;   // a reflex corner
                    bool empty = true;
                    foreach (int k in order)
                        if (k != ia && k != ib && k != ic && Inside(flat[k], a, b, c)) { empty = false; break; }
                    if (!empty) continue;
                    AddCapTriangle(loop[ia], loop[ib], loop[ic], normal);
                    order.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            for (int i = 1; i + 1 < order.Count; i++) AddCapTriangle(loop[order[0]], loop[order[i]], loop[order[i + 1]], normal);
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (b.x - a.x)*(p.y - a.y) - (b.y - a.y)*(p.x - a.x);
            float d2 = (c.x - b.x)*(p.y - b.y) - (c.y - b.y)*(p.x - b.x);
            float d3 = (a.x - c.x)*(p.y - c.y) - (a.y - c.y)*(p.x - c.x);
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }

        private void AddCapTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            var ca = new Corner { position = a, normal = normal };
            var cb = new Corner { position = b, normal = normal };
            var cc = new Corner { position = c, normal = normal };
            bool facing = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f;
            AddPolygon(CapSubmesh, facing ? new List<Corner> { ca, cb, cc } : new List<Corner> { ca, cc, cb });
        }

        // into the mesh copy: the model's submeshes, then the cut faces (drawn with the flesh material)
        public void Write(Mesh mesh, Matrix4x4 toLocal, int submeshCount)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            var triangles = new List<int>[submeshCount + 1];
            for (int s = 0; s <= submeshCount; s++) triangles[s] = new List<int>();
            foreach (var entry in polygons)
            {
                var into = triangles[entry.Key == CapSubmesh ? submeshCount : entry.Key];
                foreach (var polygon in entry.Value)
                {
                    int first = positions.Count;
                    foreach (var c in polygon)
                    {
                        positions.Add(toLocal.MultiplyPoint3x4(c.position));
                        normals.Add(toLocal.MultiplyVector(c.normal).normalized);
                        uvs.Add(c.uv);
                    }
                    for (int i = 1; i + 1 < polygon.Count; i++) { into.Add(first); into.Add(first + i); into.Add(first + i + 1); }
                }
            }
            mesh.Clear();
            mesh.indexFormat = positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshCount + 1;
            for (int s = 0; s <= submeshCount; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }

    // the pale inside of a mushroom, shared by every cut face
    private static Material Flesh()
    {
        if (flesh != null) return flesh;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        flesh = new Material(shader) { name = "Mushroom Flesh" };
        flesh.SetColor("_BaseColor", new Color(.96f, .91f, .8f));
        flesh.SetFloat("_Smoothness", .15f);
        flesh.SetFloat("_Cull", 0f);
        return flesh;
    }

    private static int Hash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h*31 + c;
            return h & 0x7fffffff;
        }
    }

    // ---- save ----

    /// <summary>The other bitten foods' entries followed by the mushrooms' (they share the save's food bites).</summary>
    public static FoodBiteSaveEntry[] AppendTo(FoodBiteSaveEntry[] others)
    {
        var mushrooms = CaptureAll();
        if (mushrooms.Length == 0) return others;
        var all = new FoodBiteSaveEntry[(others?.Length ?? 0) + mushrooms.Length];
        others?.CopyTo(all, 0);
        mushrooms.CopyTo(all, others?.Length ?? 0);
        return all;
    }

    public static FoodBiteSaveEntry[] CaptureAll()
    {
        var entries = new List<FoodBiteSaveEntry>();
        foreach (var m in FindObjectsByType<EdibleMushroom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.edible == null || m.edible.IsConsumed || m.BitesTaken == 0) continue;
            entries.Add(new FoodBiteSaveEntry { id = m.SaveId, stage = m.BitesTaken, bites = 0, bitesNeeded = m.Bites });
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return entries.ToArray();
    }

    public static void RestoreAll(FoodBiteSaveEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return;
        var byId = new Dictionary<string, FoodBiteSaveEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) if (entry != null && !string.IsNullOrEmpty(entry.id)) byId[entry.id] = entry;
        foreach (var m in FindObjectsByType<EdibleMushroom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.edible == null || m.edible.IsConsumed || !byId.TryGetValue(m.SaveId, out var entry)) continue;
            // a mushroom now smaller than when it was bitten keeps at least its last bite
            m.BitesTaken = Mathf.Clamp(entry.stage, 0, m.Bites - 1);
            if (m.BitesTaken == 0) continue;
            // which way the slices lay is not kept: a fixed way of its own, the first pieces gone
            m.pieceAngle = Hash(m.SaveId)%360;
            m.pieceAngleSet = true;
            for (int i = 0; i < m.BitesTaken; i++) m.eatenPieces.Add(i);
            m.Reshape();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!WorldBounds(out Bounds b)) return;
        int bites = Application.isPlaying ? Bites - BitesTaken : BitesForCurrentSize();
        Gizmos.color = new Color(1f, .75f, .2f, .9f);
        for (int i = 0; i < bites; i++)
            Gizmos.DrawSphere(new Vector3(b.center.x + (i - (bites - 1)*.5f)*.06f, b.max.y + .06f, b.center.z), .02f);
        UnityEditor.Handles.Label(new Vector3(b.center.x, b.max.y + .13f, b.center.z), $"{bites} ısırık");
    }
#endif
}
