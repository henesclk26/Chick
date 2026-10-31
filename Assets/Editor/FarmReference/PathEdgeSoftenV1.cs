using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Softens where the meadow's colour patches (dry, golden, lush, clover, shade, cool, olive grass) stop beside the paths:
/// they were cut off about a metre short of the path in one texel, so a plain green band with a hard edge ran along
/// every path. Inside the given rectangle the patches' share of the grass is spread out over about
/// <see cref="Reach"/> metres (weighted by the grass, so the bare path does not thin them), and they now thin out
/// towards the path instead. Only the grass layers change, their total kept at every texel: the paths, earth, pebbles
/// and the field stay exactly as they are. The change fades out over <see cref="Feather"/> metres at the rectangle's
/// edge. One undo step.
/// </summary>
public static class PathEdgeSoftenV1
{
    private static readonly string[] Patches =
        { "RichGreen_Lush", "RichGreen_Clover", "RichGreen_Dry", "RichGreen_Shade", "RichGreen_Cool", "RichGreen_Golden", "RichGreen_Olive" };
    private static readonly string[] Plain = { "RichGreen", "FreshGreen", "OriginalMeadow", "SunlitGreen", "WornMeadow" };
    private const float Reach = 1.2f, Feather = 5f;

    // The barn and the coop with their yards and paths.
    [MenuItem("Tools/Chick/Ground/Soften Path Edges (Barn And Coop)")]
    private static void Menu() => Debug.Log(Run(55f, 135f, -5f, 45f));

    public static string Run(float xMin, float xMax, float zMin, float zMax)
    {
        var terrain = Terrain.activeTerrain;
        var td = terrain.terrainData;
        int n = td.alphamapResolution, layers = td.alphamapLayers;
        Vector3 o = terrain.transform.position, size = td.size;
        float sx = size.x / n, sz = size.z / n;

        var patch = new bool[layers];
        var plain = new bool[layers];
        for (int k = 0; k < layers; k++)
        {
            string name = td.terrainLayers[k].name;
            patch[k] = System.Array.IndexOf(Patches, name) >= 0;
            plain[k] = System.Array.IndexOf(Plain, name) >= 0;
        }

        // texel window: the rectangle, the feather and the blur's reach around it
        int pad = Mathf.CeilToInt(Reach * 3f / Mathf.Min(sx, sz));
        int x0 = Mathf.Clamp(Mathf.FloorToInt((xMin - o.x) / sx) - pad, 0, n - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((xMax - o.x) / sx) + pad, 0, n - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((zMin - o.z) / sz) - pad, 0, n - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((zMax - o.z) / sz) + pad, 0, n - 1);
        int w = x1 - x0 + 1, h = z1 - z0 + 1;
        var a = td.GetAlphamaps(x0, z0, w, h);

        // the grass (patches and plain) and each patch layer, blurred the same way
        var grass = new float[h, w];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                for (int k = 0; k < layers; k++)
                    if (patch[k] || plain[k]) grass[z, x] += a[z, x, k];
        int rx = Mathf.Max(1, Mathf.RoundToInt(Reach / sx)), rz = Mathf.Max(1, Mathf.RoundToInt(Reach / sz));
        var grassBlur = Blur(grass, rx, rz);
        var shares = new float[layers][,];
        for (int k = 0; k < layers; k++)
        {
            if (!patch[k]) continue;
            var layer = new float[h, w];
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) layer[z, x] = a[z, x, k];
            shares[k] = Blur(layer, rx, rz);
        }

        int changed = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float wx = o.x + (x0 + x + .5f) * sx, wz = o.z + (z0 + z + .5f) * sz;
                float inside = Mathf.Min(Mathf.Min(wx - xMin, xMax - wx), Mathf.Min(wz - zMin, zMax - wz));
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / Feather + 1f));   // full inside, gone Feather m out
                float g = grass[z, x];
                if (t <= 0f || g <= 1e-4f || grassBlur[z, x] <= 1e-4f) continue;

                float patchSum = 0f;
                var target = new float[layers];
                for (int k = 0; k < layers; k++)
                    if (patch[k]) { target[k] = shares[k][z, x] / grassBlur[z, x] * g; patchSum += target[k]; }
                if (patchSum > g) { for (int k = 0; k < layers; k++) target[k] *= g / patchSum; patchSum = g; }

                // what the patches leave goes to the plain greens, in the shares they have here (RichGreen if none)
                float plainNow = 0f;
                for (int k = 0; k < layers; k++) if (plain[k]) plainNow += a[z, x, k];
                float plainLeft = g - patchSum;
                for (int k = 0; k < layers; k++)
                {
                    if (patch[k]) a[z, x, k] = Mathf.Lerp(a[z, x, k], target[k], t);
                    else if (plain[k])
                    {
                        float want = plainNow > 1e-4f ? a[z, x, k] / plainNow * plainLeft : (td.terrainLayers[k].name == "RichGreen" ? plainLeft : 0f);
                        a[z, x, k] = Mathf.Lerp(a[z, x, k], want, t);
                    }
                }
                changed++;
            }

        Undo.RegisterCompleteObjectUndo(td, "Soften path edges");
        td.SetAlphamaps(x0, z0, a);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssetIfDirty(td);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        return "Path edges softened: " + changed + " texels between x " + xMin + ".." + xMax + ", z " + zMin + ".." + zMax + ".";
    }

    private static float[,] Blur(float[,] src, int rx, int rz)
    {
        int h = src.GetLength(0), w = src.GetLength(1);
        var cur = src;
        for (int pass = 0; pass < 2; pass++)
        {
            var tmp = new float[h, w];
            for (int z = 0; z < h; z++)
            {
                float s = 0; for (int x = -rx; x <= rx; x++) s += cur[z, Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++) { tmp[z, x] = s / (2 * rx + 1); s += cur[z, Mathf.Min(x + rx + 1, w - 1)] - cur[z, Mathf.Max(x - rx, 0)]; }
            }
            var o = new float[h, w];
            for (int x = 0; x < w; x++)
            {
                float s = 0; for (int z = -rz; z <= rz; z++) s += tmp[Mathf.Clamp(z, 0, h - 1), x];
                for (int z = 0; z < h; z++) { o[z, x] = s / (2 * rz + 1); s += tmp[Mathf.Min(z + rz + 1, h - 1), x] - tmp[Mathf.Max(z - rz, 0), x]; }
            }
            cur = o;
        }
        return cur;
    }
}
