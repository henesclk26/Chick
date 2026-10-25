using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The wooded hills around the map, scenery only: nobody walks there. A square of ground around the map's terrain
/// that starts as the map's own ground at its edge (Farm/Backdrop Ground draws the map's layers, their weights
/// carried on from the edge) and turns outward into a forest floor of its own (olive and dry grass, worn and bare
/// earth) on rising hills, covered by a forest of low-poly pines and broadleaf trees: groves and thinner stretches,
/// clearings and a few autumn trees, each side as thick and as high as set (the south, where the painted mountain
/// stands, thin and low). Everything is built from the seed when enabled
/// (in the Editor too) and never saved, so the scene and the project stay small; the trees are merged into a few
/// meshes per area, cast no shadows and use one cheap material (Farm/Backdrop) that also hazes them into the horizon
/// the further out they stand.
/// </summary>
[ExecuteAlways]
public sealed class BackdropForest : MonoBehaviour
{
    [SerializeField] private Terrain map;
    [SerializeField] private Material groundMaterial, treeMaterial;
    [Tooltip("The eight map layers the ground draws (ids in the map's terrain layers): four that match the map's edge, then the forest's olive grass, dry grass, grass with earth and bare earth.")]
    [SerializeField] private int[] groundLayers = { 0, 1, 2, 3, 4, 5, 6, 7 };
    [Tooltip("How far the scenery reaches out from the map: east-west (x) and north-south (y), metres.")]
    [SerializeField] private Vector2 reach = new Vector2(35f, 90f);
    [SerializeField] private float hillHeight = 22f;
    [Tooltip("Hill height on each side, as a share of the above: west, east, south, north.")]
    [SerializeField] private Vector4 sideHills = new Vector4(1f, 1f, 0.45f, 1f);
    [SerializeField] private float treeSpacing = 5.5f;
    [Tooltip("Share of the possible trees that grow, on average.")]
    [SerializeField, Range(0.05f, 1f)] private float density = 0.85f;
    [Tooltip("Tree density on each side, as a share of the above: west, east, south, north.")]
    [SerializeField] private Vector4 sideDensity = new Vector4(1f, 1f, 0.3f, 1f);
    [Tooltip("How much the density changes from grove to grove: 0 even, 1 thick groves beside thin stretches.")]
    [SerializeField, Range(0f, 1f)] private float groves = 0.85f;
    [SerializeField, Range(0f, 0.6f)] private float clearings = 0.3f;
    [SerializeField, Range(0f, 0.2f)] private float autumnShare = 0.06f;
    [SerializeField] private int seed = 1905;

    private const float GroundCell = 2f, Chunk = 60f, PathFade = 6f;
    private readonly List<GameObject> built = new List<GameObject>();
    private bool dirty;
    // The map's layer weights along its four edges (south, north, west, east), read once per build.
    private float[,,] edgeSouth, edgeNorth, edgeWest, edgeEast;
    private bool[] earthLayer;

    public int TreeCount { get; private set; }
    public int TriangleCount { get; private set; }

    public void Setup(Terrain terrain, Material ground, Material trees, int[] layers)
    {
        map = terrain; groundMaterial = ground; treeMaterial = trees; groundLayers = layers;
        Rebuild();
    }

    private void OnEnable() => Rebuild();
    private void OnDisable() => Clear();
    private void OnValidate() => dirty = true;   // rebuilt on the next Editor update: objects can't be made in OnValidate

    private void Update()
    {
        if (!dirty) return;
        dirty = false;
        Rebuild();
    }

    public void Rebuild()
    {
        Clear();
        if (map == null || groundMaterial == null || treeMaterial == null || groundLayers == null || groundLayers.Length != 8) return;
        TreeCount = 0; TriangleCount = 0;
        var block = new MaterialPropertyBlock();
        Vector3 o = map.transform.position, s = map.terrainData.size;
        block.SetVector("_MapRect", new Vector4(o.x, o.z, o.x + s.x, o.z + s.z));
        block.SetVector("_Reach", new Vector4(reach.x, reach.y, 0f, 0f));
        block.SetVector("_LayerIds", new Vector4(groundLayers[0], groundLayers[1], groundLayers[2], groundLayers[3]));
        block.SetVector("_LayerIds2", new Vector4(groundLayers[4], groundLayers[5], groundLayers[6], groundLayers[7]));
        ReadEdges();
        BuildGround(block);
        BuildTrees(block);
        edgeSouth = edgeNorth = edgeWest = edgeEast = null;
    }

    private void Clear()
    {
        foreach (GameObject go in built)
        {
            if (go == null) continue;
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) { Destroy(mesh); Destroy(go); } else { DestroyImmediate(mesh); DestroyImmediate(go); }
        }
        built.Clear();
        // Leftovers (e.g. after a script reload) are never saved, but would still show until then.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if ((child.hideFlags & HideFlags.DontSave) == 0) continue;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }

    // ---------------------------------------------------------------- the shape of the land

    private Vector3 Origin => map.transform.position;
    private Vector3 Size => map.terrainData.size;

    private float MapHeight(float x, float z) => map.SampleHeight(new Vector3(x, 0f, z)) + Origin.y;

    // 0 at the map's edge, 1 at the scenery's far side (more in the corners' far reaches).
    private float Reach(float x, float z)
    {
        Vector3 o = Origin, s = Size;
        float outX = Mathf.Max(0f, o.x - x, x - (o.x + s.x)), outZ = Mathf.Max(0f, o.z - z, z - (o.z + s.z));
        return Mathf.Max(outX / reach.x, outZ / reach.y);
    }

    private float Height(float x, float z)
    {
        Vector3 o = Origin, s = Size;
        float cx = Mathf.Clamp(x, o.x, o.x + s.x), cz = Mathf.Clamp(z, o.z, o.z + s.z);
        float edge = MapHeight(cx, cz);
        var outward = new Vector2(x - cx, z - cz);
        float d = outward.magnitude;
        if (d < 1e-3f) return edge;
        // The edge's own slope carried on a little, levelling off, then hills rising with the reach.
        Vector2 dir = outward / d;
        float slope = Mathf.Clamp((edge - MapHeight(cx - dir.x * 4f, cz - dir.y * 4f)) / 4f, -0.4f, 0.5f);
        float carry = slope * 12f * (1f - Mathf.Exp(-d / 12f));
        float t = Reach(x, z);
        float broad = Fbm(x / 90f, z / 90f), detail = Fbm(x / 26f + 17f, z / 26f + 5f);
        float rise = Smooth(0f, 1f, t) * hillHeight * Side(sideHills, x, z) * Mathf.Lerp(0.5f, 1.1f, broad)
                     + Smooth(0.1f, 0.5f, t) * (detail - 0.45f) * 6f;
        return edge + carry + Mathf.Max(rise, -1f);
    }

    // A per-side setting (west, east, south, north) at a point outside the map, blended across the corners.
    private float Side(Vector4 perSide, float x, float z)
    {
        Vector3 o = Origin, s = Size;
        float west = Mathf.Max(0f, o.x - x) / reach.x, east = Mathf.Max(0f, x - (o.x + s.x)) / reach.x;
        float south = Mathf.Max(0f, o.z - z) / reach.y, north = Mathf.Max(0f, z - (o.z + s.z)) / reach.y;
        float sum = west + east + south + north;
        if (sum <= 0f) return 1f;
        return (west * perSide.x + east * perSide.y + south * perSide.z + north * perSide.w) / sum;
    }

    private Vector3 Normal(float x, float z)
    {
        const float e = 0.75f;
        float dx = Height(x + e, z) - Height(x - e, z), dz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-dx, 2f * e, -dz).normalized;
    }

    private static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    private static float Fbm(float x, float z)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < 4; i++)
        {
            sum += Mathf.PerlinNoise(x + i * 13.7f, z + i * 7.3f) * amp;
            norm += amp; amp *= 0.5f; x *= 2.03f; z *= 2.03f;
        }
        return sum / norm;
    }

    private static Color Lin(float r, float g, float b) => new Color(r, g, b).linear;

    // ---------------------------------------------------------------- ground

    private void BuildGround(MaterialPropertyBlock block)
    {
        Vector3 o = Origin, s = Size;
        float x0 = o.x - reach.x, x1 = o.x + s.x + reach.x, z0 = o.z - reach.y, z1 = o.z + s.z + reach.y;
        List<float> xs = Steps(x0, o.x).GetRange(0, Steps(x0, o.x).Count - 1);
        xs.AddRange(Steps(o.x, o.x + s.x)); xs.RemoveAt(xs.Count - 1); xs.AddRange(Steps(o.x + s.x, x1));
        List<float> zsMid = Steps(o.z, o.z + s.z);
        // North and south strips across the whole width, west and east strips between them.
        Strip("Ground South", xs, Steps(z0, o.z), block);
        Strip("Ground North", xs, Steps(o.z + s.z, z1), block);
        Strip("Ground West", Steps(x0, o.x), zsMid, block);
        Strip("Ground East", Steps(o.x + s.x, x1), zsMid, block);
        Skirt(block);
    }

    private static List<float> Steps(float a, float b)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / GroundCell));
        var list = new List<float>(n + 1);
        for (int i = 0; i <= n; i++) list.Add(Mathf.Lerp(a, b, i / (float)n));
        return list;
    }

    // The vertex colour: none at the map's edge (its ground as it is), a shaded forest floor further out, lighter in glades.
    private Color GroundColour(float x, float z)
    {
        float t = Reach(x, z);
        float patch = Fbm(x / 30f + 3f, z / 30f + 9f), clearing = Fbm(x / 45f + 31f, z / 45f + 2f);
        Color floor = Color.Lerp(Lin(0.9f, 0.93f, 0.86f), Lin(0.96f, 0.92f, 0.86f), patch);   // under the trees
        Color c = Color.Lerp(floor, Color.white, Smooth(0.55f, 0.75f, clearing) * (1f - Smooth(0.7f, 0.9f, t)));
        return Color.Lerp(Color.white, c, Smooth(4f, 30f, Outside(x, z)));
    }

    // Metres from the map's edge.
    private float Outside(float x, float z)
    {
        Vector3 o = Origin, s = Size;
        float outX = Mathf.Max(0f, o.x - x, x - (o.x + s.x)), outZ = Mathf.Max(0f, o.z - z, z - (o.z + s.z));
        return Mathf.Sqrt(outX * outX + outZ * outZ);
    }

    private void ReadEdges()
    {
        TerrainData data = map.terrainData;
        int w = data.alphamapWidth, h = data.alphamapHeight;
        edgeSouth = data.GetAlphamaps(0, 0, w, 1);
        edgeNorth = data.GetAlphamaps(0, h - 1, w, 1);
        edgeWest = data.GetAlphamaps(0, 0, 1, h);
        edgeEast = data.GetAlphamaps(w - 1, 0, 1, h);
        earthLayer = new bool[data.terrainLayers.Length];
        for (int i = 0; i < earthLayer.Length; i++) earthLayer[i] = data.terrainLayers[i] != null && IsEarth(data.terrainLayers[i].name);
    }

    public static bool IsEarth(string layer) =>
        layer.Contains("Earth") || layer.Contains("Trodden") || layer.Contains("Path") || layer.Contains("Tilled") ||
        layer.Contains("Pebble") || layer.Contains("Sand") || layer.Contains("Stony") || layer.Contains("Soil");

    // The eight layers' weights (edge four, forest four): the map's own at the nearest point of its edge (paths and
    // earth stop a few metres out), turning further out into the forest's own mix.
    private (Vector4 edge, Vector4 forest) GroundWeights(float x, float z)
    {
        Vector3 o = Origin, s = Size;
        int w = edgeSouth.GetLength(1), h = edgeWest.GetLength(0);
        float u = Mathf.Clamp01((x - o.x) / s.x), v = Mathf.Clamp01((z - o.z) / s.z);
        // Nearest edge: the one the point lies beyond, or (on the apron, inside) the closest.
        float toWest = x - o.x, toEast = (o.x + s.x - x), toSouth = z - o.z, toNorth = o.z + s.z - z;
        float nearest = Mathf.Min(Mathf.Min(toWest, toEast), Mathf.Min(toSouth, toNorth));
        float[,,] row; int index; bool alongX;
        if (nearest == toSouth) { row = edgeSouth; index = Mathf.RoundToInt(u * (w - 1)); alongX = true; }
        else if (nearest == toNorth) { row = edgeNorth; index = Mathf.RoundToInt(u * (w - 1)); alongX = true; }
        else if (nearest == toWest) { row = edgeWest; index = Mathf.RoundToInt(v * (h - 1)); alongX = false; }
        else { row = edgeEast; index = Mathf.RoundToInt(v * (h - 1)); alongX = false; }
        float outside = Mathf.Max(0f, -nearest);
        var edge = new Vector4();
        for (int k = 0; k < 4; k++)
        {
            int id = groundLayers[k];
            float wk = alongX ? row[0, index, id] : row[index, 0, id];
            if (earthLayer[id]) wk *= 1f - Smooth(0f, PathFade, outside);
            edge[k] = wk;
        }
        float sum = edge.x + edge.y + edge.z + edge.w;
        edge = sum > 0.05f ? edge / sum : new Vector4(1f, 0f, 0f, 0f);

        // The forest floor, unlike the map: olive grass with dry stretches, grass worn through to earth, and bare
        // earth in patches of every size.
        float broad = Fbm(x / 45f + 11f, z / 45f + 4f), mid = Fbm(x / 22f + 50f, z / 22f + 20f), small = Fbm(x / 9f + 3f, z / 9f + 8f);
        var forest = new Vector4(
            0.9f,                                                              // olive grass
            Smooth(0.48f, 0.66f, broad) * 1.4f,                                // dry grass
            Smooth(0.45f, 0.62f, mid) * 1.1f,                                  // grass and earth
            Smooth(0.6f, 0.72f, small) * 1.2f + Smooth(0.62f, 0.74f, mid) * 0.8f);   // bare earth
        forest /= forest.x + forest.y + forest.z + forest.w;
        float far = Smooth(6f, 30f, Outside(x, z));
        return (edge * (1f - far), forest * far);
    }

    private void Strip(string name, List<float> xs, List<float> zs, MaterialPropertyBlock block)
    {
        int w = xs.Count, h = zs.Count;
        var verts = new Vector3[w * h];
        var normals = new Vector3[w * h];
        var colours = new Color[w * h];
        var weights = new Vector4[w * h];
        var weights2 = new Vector4[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = xs[i], z = zs[j];
                int k = j * w + i;
                verts[k] = new Vector3(x, Height(x, z), z);
                normals[k] = Reach(x, z) <= 0f ? map.terrainData.GetInterpolatedNormal((x - Origin.x) / Size.x, (z - Origin.z) / Size.z) : Normal(x, z);
                colours[k] = GroundColour(x, z);
                (weights[k], weights2[k]) = GroundWeights(x, z);
            }
        var tris = new int[(w - 1) * (h - 1) * 6];
        int n = 0;
        for (int j = 0; j < h - 1; j++)
            for (int i = 0; i < w - 1; i++)
            {
                int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
                tris[n++] = a; tris[n++] = c; tris[n++] = b;
                tris[n++] = b; tris[n++] = c; tris[n++] = d;
            }
        Add(name, verts, normals, colours, tris, groundMaterial, block, weights, weights2);
    }

    // Under the map's edge: a narrow apron tucked just below its ground, so wherever the map's terrain and this ground
    // part a little (the terrain lowers its detail with distance), the gap shows ground, not sky.
    private void Skirt(MaterialPropertyBlock block)
    {
        Vector3 o = Origin, s = Size;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Edge(Vector3 from, Vector3 to, Vector3 inward)
        {
            float length = Vector3.Distance(from, to);
            int count = Mathf.CeilToInt(length / GroundCell);
            int start = verts.Count;
            for (int i = 0; i <= count; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)count);
                verts.Add(new Vector3(p.x, MapHeight(p.x, p.z), p.z));
                Vector3 q = p + inward * 1.5f;
                verts.Add(new Vector3(q.x, MapHeight(q.x, q.z) - 0.3f, q.z));
            }
            for (int i = 0; i < count; i++)
            {
                int a = start + i * 2;
                tris.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
                tris.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });   // both faces
            }
        }
        Edge(new Vector3(o.x, 0f, o.z), new Vector3(o.x + s.x, 0f, o.z), Vector3.forward);
        Edge(new Vector3(o.x, 0f, o.z + s.z), new Vector3(o.x + s.x, 0f, o.z + s.z), Vector3.back);
        Edge(new Vector3(o.x, 0f, o.z), new Vector3(o.x, 0f, o.z + s.z), Vector3.right);
        Edge(new Vector3(o.x + s.x, 0f, o.z), new Vector3(o.x + s.x, 0f, o.z + s.z), Vector3.left);
        var normals = new Vector3[verts.Count];
        var colours = new Color[verts.Count];
        var weights = new Vector4[verts.Count];
        var weights2 = new Vector4[verts.Count];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.up; colours[i] = Color.white;
            (weights[i], weights2[i]) = GroundWeights(verts[i].x, verts[i].z);
        }
        Add("Ground Edge Apron", verts.ToArray(), normals, colours, tris.ToArray(), groundMaterial, block, weights, weights2);
    }

    // ---------------------------------------------------------------- forest

    private sealed class MeshParts
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Color> colours = new List<Color>();
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            colours.Add(colour); colours.Add(colour); colours.Add(colour);
        }
    }

    private void BuildTrees(MaterialPropertyBlock block)
    {
        Vector3 o = Origin, s = Size;
        float x0 = o.x - reach.x, x1 = o.x + s.x + reach.x, z0 = o.z - reach.y, z1 = o.z + s.z + reach.y;
        var rng = new System.Random(seed);
        var chunks = new Dictionary<(int, int), MeshParts>();
        for (float z = z0; z < z1; z += treeSpacing)
            for (float x = x0; x < x1; x += treeSpacing)
            {
                float px = x + (float)rng.NextDouble() * treeSpacing, pz = z + (float)rng.NextDouble() * treeSpacing;
                double roll = rng.NextDouble(), kindRoll = rng.NextDouble(), keepRoll = rng.NextDouble();
                float t = Reach(px, pz);
                if (t <= 0f || px < x0 || px > x1 || pz < z0 || pz > z1) continue;
                // A ragged forest edge a little way out from the map, glades inside, solid at the far side.
                float edgeNoise = Fbm(px / 20f + 7f, pz / 20f + 1f);
                if (t < 0.08f + edgeNoise * 0.22f) continue;
                float glade = Fbm(px / 45f + 31f, pz / 45f + 2f);
                float open = Smooth(0.75f - clearings, 0.85f - clearings * 0.5f, glade) * (1f - Smooth(0.7f, 0.9f, t));
                if (roll < open) continue;
                if (roll < 0.35f * (1f - Smooth(0.15f, 0.45f, t))) continue;   // thinner near the map
                // Groves and thinner stretches, each side as thick as set.
                float grove = Mathf.Lerp(1f, Mathf.Lerp(0.15f, 1.85f, Smooth(0.3f, 0.7f, Fbm(px / 70f + 23f, pz / 70f + 41f))), groves);
                if (keepRoll > density * Side(sideDensity, px, pz) * grove) continue;

                Vector3 foot = new Vector3(px, Height(px, pz) - 0.3f, pz);
                var key = (Mathf.FloorToInt(px / Chunk), Mathf.FloorToInt(pz / Chunk));
                if (!chunks.TryGetValue(key, out MeshParts parts)) chunks[key] = parts = new MeshParts();
                float grow = Mathf.Lerp(0.85f, 1.2f, t) * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
                bool pine = kindRoll < Mathf.Lerp(0.45f, 0.7f, t);
                if (pine) Pine(parts, foot, 11f * grow, rng);
                else Broadleaf(parts, foot, 8f * grow, rng, rng.NextDouble() < autumnShare);
                TreeCount++;
            }
        foreach (var kv in chunks)
        {
            var tris = new int[kv.Value.verts.Count];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            Add($"Trees {kv.Key.Item1},{kv.Key.Item2}", kv.Value.verts.ToArray(), kv.Value.normals.ToArray(), kv.Value.colours.ToArray(), tris, treeMaterial, block);
        }
    }

    private static readonly Color Bark = new Color(0.36f, 0.25f, 0.17f).linear;

    private static void Trunk(MeshParts parts, Vector3 foot, float radius, float height, int sides)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = foot + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, b1 = foot + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Vector3 t0 = b0 * 0.7f + foot * 0.3f + Vector3.up * height, t1 = b1 * 0.7f + foot * 0.3f + Vector3.up * height;
            parts.Tri(b0, t0, b1, Bark);
            parts.Tri(b1, t0, t1, Bark);
        }
    }

    private static void Pine(MeshParts parts, Vector3 foot, float height, System.Random rng)
    {
        Trunk(parts, foot, height * 0.035f, height * 0.35f, 5);
        float hue = (float)rng.NextDouble();
        Color dark = Color.Lerp(new Color(0.11f, 0.27f, 0.17f), new Color(0.16f, 0.33f, 0.2f), hue).linear;
        Color light = Color.Lerp(new Color(0.2f, 0.42f, 0.24f), new Color(0.26f, 0.46f, 0.26f), hue).linear;
        float radius = height * Mathf.Lerp(0.2f, 0.26f, (float)rng.NextDouble());
        const int sides = 7, tiers = 3;
        for (int k = 0; k < tiers; k++)
        {
            float bottom = height * (0.22f + 0.2f * k), top = k == tiers - 1 ? height : bottom + height * 0.42f;
            float r = radius * (1f - 0.27f * k);
            float turn = (float)rng.NextDouble() * Mathf.PI;
            Vector3 apex = foot + Vector3.up * top;
            Color c = Color.Lerp(dark, light, k / (tiers - 1f));
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = turn + i * Mathf.PI * 2f / sides, rr = r * Mathf.Lerp(0.88f, 1.1f, (float)rng.NextDouble());
                ring[i] = foot + new Vector3(Mathf.Cos(a) * rr, bottom - height * 0.03f * (float)rng.NextDouble(), Mathf.Sin(a) * rr);
            }
            for (int i = 0; i < sides; i++)
            {
                parts.Tri(ring[i], apex, ring[(i + 1) % sides], c);
                if (k == 0) parts.Tri(ring[i], ring[(i + 1) % sides], foot + Vector3.up * (bottom + height * 0.06f), dark * 0.8f);   // underside
            }
        }
    }

    private static readonly Vector3[] Ico = Icosahedron(out IcoFaces);
    private static readonly int[] IcoFaces;

    private static Vector3[] Icosahedron(out int[] faces)
    {
        float p = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new[]
        {
            new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
            new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
            new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
        faces = new[]
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        return v;
    }

    private static void Broadleaf(MeshParts parts, Vector3 foot, float height, System.Random rng, bool autumn)
    {
        Trunk(parts, foot, height * 0.045f, height * 0.5f, 5);
        float hue = (float)rng.NextDouble();
        Color leaf = autumn
            ? Color.Lerp(new Color(0.85f, 0.42f, 0.12f), new Color(0.88f, 0.7f, 0.18f), hue).linear
            : Color.Lerp(new Color(0.24f, 0.5f, 0.2f), new Color(0.38f, 0.6f, 0.22f), hue).linear;
        if (autumn && hue < 0.3f) leaf = new Color(0.72f, 0.18f, 0.12f).linear;
        Vector2 lean = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * height * 0.25f;
        Blob(parts, foot + Vector3.up * height * 0.62f, height * 0.33f, leaf, rng);
        Blob(parts, foot + new Vector3(lean.x, height * 0.76f, lean.y), height * 0.24f, leaf * 1.08f, rng);
    }

    private static void Blob(MeshParts parts, Vector3 centre, float radius, Color colour, System.Random rng)
    {
        var p = new Vector3[Ico.Length];
        for (int i = 0; i < p.Length; i++)
            p[i] = centre + Vector3.Scale(Ico[i], new Vector3(1f, 0.82f, 1f)) * radius * Mathf.Lerp(0.85f, 1.12f, (float)rng.NextDouble());
        for (int f = 0; f < IcoFaces.Length; f += 3)
        {
            float shade = Mathf.Lerp(0.9f, 1.05f, (float)rng.NextDouble());
            parts.Tri(p[IcoFaces[f]], p[IcoFaces[f + 1]], p[IcoFaces[f + 2]], colour * shade);
        }
    }

    // ---------------------------------------------------------------- objects

    private void Add(string name, Vector3[] verts, Vector3[] normals, Color[] colours, int[] tris, Material material, MaterialPropertyBlock block,
                     Vector4[] layerWeights = null, Vector4[] layerWeights2 = null)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.colors = colours;
        if (layerWeights != null) mesh.SetUVs(1, layerWeights);
        if (layerWeights2 != null) mesh.SetUVs(2, layerWeights2);
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
        go.transform.SetParent(transform, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.SetPropertyBlock(block);
        built.Add(go);
        TriangleCount += tris.Length / 3;
    }
}
