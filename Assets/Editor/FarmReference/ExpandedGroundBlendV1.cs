using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the ground the map was later grown with (the terrain outside the original map's rectangle) meet the original
/// ground unseen, without changing a single texel or height inside that rectangle:
/// - the paint: outside, the grass variants are re-weighted so the mix matches the original ground's (the outside had
///   two to three times as much Lush, Clover, Dry, Golden and Olive, which read greener and more saturated), and along
///   the rectangle the outside takes the inside's paint mirrored across the edge, fading back into its own over
///   <see cref="PaintBand"/> metres (earth, paths and pebbles over <see cref="EarthBand"/>, so a path running beside the
///   edge is not doubled outside it);
/// - the heights: along the rectangle the outside continues the inside's slope (mirrored through the edge) and eases back
///   into its own hills over <see cref="HeightBand"/> metres, so no crease or step is left on the edge;
/// - props standing on the changed ground are moved up or down with it.
/// One undo step. Menu, or <see cref="Run"/>(false) for a report only.
/// </summary>
public static class ExpandedGroundBlendV1
{
    // The original map inside the terrain: alphamap texels and heightmap samples, inclusive.
    private const int AX0 = 378, AX1 = 645, AZ0 = 126, AZ1 = 743;
    private const int HX0 = 378, HX1 = 646, HZ0 = 126, HZ1 = 744;
    private const float PaintBand = 8f, EarthBand = 1.5f, HeightBand = 6f;
    // Steepest slope the continued inside slope may take (rise per metre).
    private const float MaxSlope = .35f;
    private static readonly string[] GrassLayers =
    {
        "OriginalMeadow", "RichGreen", "FreshGreen", "SunlitGreen", "WornMeadow", "RichGreen_Lush", "RichGreen_Clover",
        "RichGreen_Dry", "RichGreen_Shade", "RichGreen_Cool", "RichGreen_Golden", "RichGreen_Olive",
    };

    [MenuItem("Tools/Chick/Map/Blend Expanded Ground Into Main Map")]
    private static void Menu() => Debug.Log(Run(true));

    public static string Run(bool write)
    {
        var terrain = Terrain.activeTerrain;
        var td = terrain.terrainData;
        var report = new StringBuilder();
        int n = td.alphamapResolution, layers = td.alphamapLayers;
        float sx = td.size.x / n, sz = td.size.z / n;

        var grass = new bool[layers];
        for (int k = 0; k < layers; k++) grass[k] = System.Array.IndexOf(GrassLayers, td.terrainLayers[k].name) >= 0;

        var source = td.GetAlphamaps(0, 0, n, n);
        var paint = (float[,,])source.Clone();

        // the original ground's grass mix
        var target = new double[layers];
        double targetSum = 0;
        for (int z = AZ0; z <= AZ1; z++)
            for (int x = AX0; x <= AX1; x++)
                for (int k = 0; k < layers; k++)
                    if (grass[k]) { target[k] += source[z, x, k]; targetSum += source[z, x, k]; }
        for (int k = 0; k < layers; k++) target[k] /= targetSum;

        // the outside's mix; the layers it has too much of give that share of their weight, wherever they are, to the
        // layers it has too little of (the calm greens), in proportion to what each lacks
        var outside = Mix(source, grass);
        var give = new float[layers];
        var receive = new float[layers];
        double lacking = 0;
        for (int k = 0; k < layers; k++)
            if (grass[k] && outside[k] > target[k]) give[k] = (float)(1 - target[k] / outside[k]);
            else if (grass[k]) lacking += target[k] - outside[k];
        for (int k = 0; k < layers; k++)
            if (grass[k] && outside[k] < target[k] && lacking > 0) receive[k] = (float)((target[k] - outside[k]) / lacking);
        report.Append("outside grass mix before: ").Append(Shares(td, outside, grass)).Append('\n');

        var mirror = new float[layers];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (Inside(x, z, AX0, AX1, AZ0, AZ1)) continue;
                Reweigh(paint, z, x, layers, give, receive);

                float dx = x < AX0 ? (AX0 - x - .5f) * sx : x > AX1 ? (x - AX1 - .5f) * sx : 0f;
                float dz = z < AZ0 ? (AZ0 - z - .5f) * sz : z > AZ1 ? (z - AZ1 - .5f) * sz : 0f;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= PaintBand) continue;
                int mx = Mirror(x, AX0, AX1), mz = Mirror(z, AZ0, AZ1);
                for (int k = 0; k < layers; k++) mirror[k] = source[mz, mx, k];
                Blend(paint, z, x, layers, grass, mirror, Smooth(d / PaintBand), Smooth(d / EarthBand));
            }

        // every border outside feathered over about 1.5 m, as GroundDistributionV2 did for the original ground
        var soft = (float[,,])paint.Clone();
        for (int k = 0; k < layers; k++) Blur(soft, k, n, 3, 2);
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (Inside(x, z, AX0, AX1, AZ0, AZ1)) continue;
                float sum = 0f;
                for (int k = 0; k < layers; k++) sum += soft[z, x, k];
                if (sum <= 1e-5f) continue;
                for (int k = 0; k < layers; k++) paint[z, x, k] = soft[z, x, k] / sum;
            }

        report.Append("outside grass mix after:  ").Append(Shares(td, Mix(paint, grass), grass)).Append('\n');
        report.Append("original ground mix:      ").Append(Shares(td, target, grass)).Append('\n');

        // heights
        int hn = td.heightmapResolution;
        float hsx = td.size.x / (hn - 1), hsz = td.size.z / (hn - 1), height = td.size.y;
        var heights = td.GetHeights(0, 0, hn, hn);
        var shaped = (float[,])heights.Clone();
        float largest = 0f;
        for (int z = 0; z < hn; z++)
            for (int x = 0; x < hn; x++)
            {
                if (Inside(x, z, HX0, HX1, HZ0, HZ1)) continue;
                float dx = x < HX0 ? (HX0 - x) * hsx : x > HX1 ? (x - HX1) * hsx : 0f;
                float dz = z < HZ0 ? (HZ0 - z) * hsz : z > HZ1 ? (z - HZ1) * hsz : 0f;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= HeightBand) continue;
                int cx = Mathf.Clamp(x, HX0, HX1), cz = Mathf.Clamp(z, HZ0, HZ1);
                int mx = Mathf.Clamp(2 * cx - x, HX0, HX1), mz = Mathf.Clamp(2 * cz - z, HZ0, HZ1);
                float edge = heights[cz, cx];
                float rise = Mathf.Clamp(edge - heights[mz, mx], -MaxSlope * d / height, MaxSlope * d / height);
                shaped[z, x] = Mathf.Lerp(edge + rise, heights[z, x], Smooth(d / HeightBand));
                largest = Mathf.Max(largest, Mathf.Abs(shaped[z, x] - heights[z, x]) * height);
            }
        report.Append("largest height change ").Append(largest.ToString("F2")).Append(" m\n");
        report.Append("edge crease before: ").Append(Creases(heights, hsx, hsz, height)).Append('\n');
        report.Append("edge crease after:  ").Append(Creases(shaped, hsx, hsz, height)).Append('\n');

        var moves = PropsToMove(terrain, heights, shaped);
        report.Append(moves.Count).Append(" props stand on the changed ground");
        foreach (var move in moves) report.Append("\n  ").Append(move.Key.name).Append(' ').Append(move.Value.ToString("+0.00;-0.00"));

        if (!write) return report.ToString();

        Undo.RegisterCompleteObjectUndo(td, "Blend expanded ground into main map");
        foreach (var move in moves) Undo.RecordObject(move.Key, "Blend expanded ground into main map");
        td.SetAlphamaps(0, 0, paint);
        td.SetHeights(0, 0, shaped);
        foreach (var move in moves) move.Key.position += Vector3.up * move.Value;
        EditorUtility.SetDirty(td);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        AssetDatabase.SaveAssetIfDirty(td);
        return "applied\n" + report;
    }

    private static bool Inside(int x, int z, int x0, int x1, int z0, int z1) => x >= x0 && x <= x1 && z >= z0 && z <= z1;

    private static int Mirror(int i, int lo, int hi) =>
        Mathf.Clamp(i < lo ? 2 * lo - 1 - i : i > hi ? 2 * hi + 1 - i : i, lo, hi);

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The over-used grass variants hand their share to the under-used ones; earth and paths stay as they are.</summary>
    private static void Reweigh(float[,,] paint, int z, int x, int layers, float[] give, float[] receive)
    {
        float pool = 0f;
        for (int k = 0; k < layers; k++)
            if (give[k] > 0f) { float t = paint[z, x, k] * give[k]; paint[z, x, k] -= t; pool += t; }
        for (int k = 0; k < layers; k++) paint[z, x, k] += pool * receive[k];
    }

    /// <summary>
    /// From the mirrored inside paint (t = 0) to the texel's own (t = 1): grass by grassT, everything else by earthT,
    /// the grass then filling whatever the rest leaves.
    /// </summary>
    private static void Blend(float[,,] paint, int z, int x, int layers, bool[] grass, float[] mirror, float grassT, float earthT)
    {
        float earth = 0f, grassSum = 0f;
        for (int k = 0; k < layers; k++)
        {
            float value = Mathf.Lerp(mirror[k], paint[z, x, k], grass[k] ? grassT : earthT);
            paint[z, x, k] = value;
            if (grass[k]) grassSum += value; else earth += value;
        }
        float room = Mathf.Max(0f, 1f - earth);
        if (grassSum <= 1e-5f)
        {
            // no grass in either: share out what is left among the others as they are
            float scale = earth > 1e-5f ? 1f / earth : 0f;
            for (int k = 0; k < layers; k++) paint[z, x, k] *= scale;
            return;
        }
        for (int k = 0; k < layers; k++)
            if (grass[k]) paint[z, x, k] *= room / grassSum;
            else if (earth > 1f) paint[z, x, k] /= earth;
    }

    /// <summary>The grass mix outside the original map (shares of the grass weight).</summary>
    private static double[] Mix(float[,,] paint, bool[] grass)
    {
        int n = paint.GetLength(0), layers = paint.GetLength(2);
        var sum = new double[layers];
        double total = 0;
        for (int z = 0; z < n; z += 2)
            for (int x = 0; x < n; x += 2)
            {
                if (Inside(x, z, AX0, AX1, AZ0, AZ1)) continue;
                for (int k = 0; k < layers; k++)
                    if (grass[k]) { sum[k] += paint[z, x, k]; total += paint[z, x, k]; }
            }
        for (int k = 0; k < layers; k++) sum[k] /= total;
        return sum;
    }

    private static string Shares(TerrainData td, double[] share, bool[] grass)
    {
        var text = new StringBuilder();
        for (int k = 0; k < share.Length; k++)
            if (grass[k] && share[k] > .005)
                text.Append(td.terrainLayers[k].name.Replace("RichGreen_", "")).Append(' ').Append((share[k] * 100).ToString("F0")).Append(' ');
        return text.ToString();
    }

    /// <summary>Box blur of one layer, r texels, a few passes (GroundDistributionV2's).</summary>
    private static void Blur(float[,,] w, int l, int res, int r, int passes)
    {
        var cur = new float[res, res];
        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) cur[z, x] = w[z, x, l];
        float n = 2 * r + 1;
        for (int p = 0; p < passes; p++)
        {
            var tmp = new float[res, res];
            var o = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                float s = 0; for (int x = -r; x <= r; x++) s += cur[z, Mathf.Clamp(x, 0, res - 1)];
                for (int x = 0; x < res; x++) { tmp[z, x] = s / n; s += cur[z, Mathf.Min(x + r + 1, res - 1)] - cur[z, Mathf.Max(x - r, 0)]; }
            }
            for (int x = 0; x < res; x++)
            {
                float s = 0; for (int z = -r; z <= r; z++) s += tmp[Mathf.Clamp(z, 0, res - 1), x];
                for (int z = 0; z < res; z++) { o[z, x] = s / n; s += tmp[Mathf.Min(z + r + 1, res - 1), x] - tmp[Mathf.Max(z - r, 0), x]; }
            }
            cur = o;
        }
        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) w[z, x, l] = cur[z, x];
    }

    /// <summary>Largest and mean bend (degrees) across the rectangle's edge, over the last and first metre.</summary>
    private static string Creases(float[,] h, float hsx, float hsz, float height)
    {
        float max = 0f, sum = 0f;
        int count = 0;
        int stepX = Mathf.Max(1, Mathf.RoundToInt(1f / hsx)), stepZ = Mathf.Max(1, Mathf.RoundToInt(1f / hsz));
        for (int z = HZ0; z <= HZ1; z++)
        {
            Bend(h[z, HX0 + stepX], h[z, HX0], h[z, HX0 - stepX], stepX * hsx, height, ref max, ref sum, ref count);
            Bend(h[z, HX1 - stepX], h[z, HX1], h[z, HX1 + stepX], stepX * hsx, height, ref max, ref sum, ref count);
        }
        for (int x = HX0; x <= HX1; x++)
        {
            Bend(h[HZ0 + stepZ, x], h[HZ0, x], h[HZ0 - stepZ, x], stepZ * hsz, height, ref max, ref sum, ref count);
            Bend(h[HZ1 - stepZ, x], h[HZ1, x], h[HZ1 + stepZ, x], stepZ * hsz, height, ref max, ref sum, ref count);
        }
        return "max " + max.ToString("F1") + "°, mean " + (sum / count).ToString("F2") + "°";
    }

    private static void Bend(float inner, float edge, float outer, float run, float height, ref float max, ref float sum, ref int count)
    {
        float a = Mathf.Atan((edge - inner) * height / run) * Mathf.Rad2Deg;
        float b = Mathf.Atan((outer - edge) * height / run) * Mathf.Rad2Deg;
        float bend = Mathf.Abs(b - a);
        max = Mathf.Max(max, bend);
        sum += bend;
        count++;
    }

    /// <summary>Props standing on ground the heights change, outermost prefab or object, with how far to move each.</summary>
    private static Dictionary<Transform, float> PropsToMove(Terrain terrain, float[,] before, float[,] after)
    {
        var td = terrain.terrainData;
        var origin = terrain.transform.position;
        int hn = td.heightmapResolution;
        float height = td.size.y;
        var moves = new Dictionary<Transform, float>();
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (renderer.gameObject.scene != terrain.gameObject.scene) continue;
            var bounds = renderer.bounds;
            if (bounds.size.x > 8f || bounds.size.z > 8f) continue;
            float u = (bounds.center.x - origin.x) / td.size.x * (hn - 1), v = (bounds.center.z - origin.z) / td.size.z * (hn - 1);
            int x = Mathf.RoundToInt(u), z = Mathf.RoundToInt(v);
            if (x < 0 || z < 0 || x >= hn || z >= hn || Inside(x, z, HX0, HX1, HZ0, HZ1)) continue;
            float shift = (after[z, x] - before[z, x]) * height;
            if (Mathf.Abs(shift) < .02f) continue;
            float ground = origin.y + before[z, x] * height;
            if (Mathf.Abs(bounds.min.y - ground) > .5f) continue;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
            var mover = root != null ? root.transform : renderer.transform;
            if (mover.GetComponentInParent<Terrain>() != null || moves.ContainsKey(mover)) continue;
            moves[mover] = shift;
        }
        return moves;
    }
}
