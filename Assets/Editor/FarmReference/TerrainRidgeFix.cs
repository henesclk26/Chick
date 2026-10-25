using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Finds and flattens thin ridges left in the terrain (a raised seam a metre or two wide running across otherwise
/// gentle ground, like the one south of SM_Tree_Color (1) by the pond). Inside a rectangle each heightmap sample is
/// compared with the ground around it (a wide blur of the heights); where it stands clearly above that, it is lowered
/// to it, the change feathered out over the rectangle's last metre. Dips (the pond, ditches) are never raised, and
/// nothing outside the rectangle changes, so keep it tight round the ridge. Asked for with a line
/// "scan|fix|smooth xmin xmax zmin zmax [blur metres]" in Temp/TerrainFix/request.txt, which the Editor picks up by itself
/// while it is open; "scan" only writes a picture (Temp/TerrainFix/ridges.png, red = above the ground around it, blue =
/// below, grey = outside the rectangle) and numbers, "fix" also lowers the ridges, "smooth" brings narrow bumps and
/// grooves alike (a crease: a ridge with a groove beside it) to the ground around them; both use Undo and save.
/// </summary>
[InitializeOnLoad]
public static class TerrainRidgeFix
{
    private const string Root = "Temp/TerrainFix";
    private const string Request = Root + "/request.txt";
    private const float Threshold = 0.03f;     // metres above the ground around it before a sample counts as a ridge
    private static double nextPoll;

    static TerrainRidgeFix() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] parts = File.ReadAllText(Request).Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        File.Delete(Request);
        if (parts.Length < 5) return;
        var c = CultureInfo.InvariantCulture;
        var area = Rect.MinMaxRect(float.Parse(parts[1], c), float.Parse(parts[3], c), float.Parse(parts[2], c), float.Parse(parts[4], c));
        Run(parts[0] == "fix" || parts[0] == "smooth", parts[0] == "smooth", area, parts.Length > 5 ? float.Parse(parts[5], c) : 3f);
    }

    private static void Run(bool fix, bool smooth, Rect area, float blur)
    {
        Directory.CreateDirectory(Root);
        var report = new StringBuilder($"{(smooth ? "smooth" : fix ? "fix" : "scan")} x {area.xMin}..{area.xMax}, z {area.yMin}..{area.yMax}, blur {blur} m\n");
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position, size = data.size;
            int res = data.heightmapResolution;
            float cellX = size.x / (res - 1), cellZ = size.z / (res - 1);
            float margin = blur + 1f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - margin - origin.x) / cellX), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax + margin - origin.x) / cellX), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - margin - origin.z) / cellZ), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax + margin - origin.z) / cellZ), 0, res - 1);
            if (x1 <= x0 || z1 <= z0) continue;
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            float[,] heights = data.GetHeights(x0, z0, w, h);   // [z, x], 0..1 of size.y
            float[,] ground = Blur(heights, Mathf.Max(1, Mathf.RoundToInt(blur / cellX)), Mathf.Max(1, Mathf.RoundToInt(blur / cellZ)));

            var picture = new Texture2D(w, h, TextureFormat.RGB24, false);
            var fixedHeights = (float[,])heights.Clone();
            int ridgeCells = 0;
            float highest = 0f, lowered = 0f;
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float wx = origin.x + (x0 + x) * cellX, wz = origin.z + (z0 + z) * cellZ;
                float edge = Mathf.Min(Mathf.Min(wx - area.xMin, area.xMax - wx), Mathf.Min(wz - area.yMin, area.yMax - wz));
                float inside = Mathf.Clamp01(edge);   // feathered over the last metre inside the rectangle
                float above = (heights[z, x] - ground[z, x]) * size.y;
                if (smooth && inside > 0f)
                {
                    // Narrow bumps and grooves alike go to the ground around them; broad slopes are kept.
                    if (Mathf.Abs(above) > Threshold) ridgeCells++;
                    highest = Mathf.Max(highest, Mathf.Abs(above));
                    fixedHeights[z, x] = Mathf.Lerp(heights[z, x], ground[z, x], inside);
                    lowered = Mathf.Max(lowered, Mathf.Abs(heights[z, x] - fixedHeights[z, x]) * size.y);
                }
                else if (inside > 0f && above > Threshold)
                {
                    ridgeCells++;
                    highest = Mathf.Max(highest, above);
                    float target = ground[z, x] + Threshold * 0.5f / size.y;
                    float next = Mathf.Lerp(heights[z, x], Mathf.Min(heights[z, x], target), inside);
                    lowered = Mathf.Max(lowered, (heights[z, x] - next) * size.y);
                    fixedHeights[z, x] = next;
                }
                float red = Mathf.Clamp01(above / 0.2f), blue = Mathf.Clamp01(-above / 0.2f);
                picture.SetPixel(x, z, inside <= 0f ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.3f + 0.7f * red, 0.3f, 0.3f + 0.7f * blue));
            }
            picture.Apply();
            File.WriteAllBytes(Path.Combine(Root, "ridges.png"), picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            report.AppendLine($"terrain {terrain.name}: cells {cellX:0.000} x {cellZ:0.000} m, picture {w} x {h} from x {origin.x + x0 * cellX:0.00}, z {origin.z + z0 * cellZ:0.00}; " +
                              $"{ridgeCells} samples above the ground around them by > {Threshold} m (highest {highest:0.00} m)");
            if (fix && ridgeCells > 0)
            {
                Undo.RegisterCompleteObjectUndo(data, "Flatten terrain ridge");
                data.SetHeights(x0, z0, fixedHeights);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                report.AppendLine($"  {(smooth ? "smoothed (moved" : "flattened (lowered")} at most {lowered:0.00} m) and saved");
            }
        }
        File.WriteAllText(Path.Combine(Root, "report.txt"), report.ToString());
        Debug.Log("Terrain ridge " + (fix ? "fix" : "scan") + ": " + report);
    }

    // Box blur twice each way (close to a Gaussian), edges clamped.
    private static float[,] Blur(float[,] src, int rx, int rz)
    {
        float[,] a = BoxPass(BoxPass(src, rx, true), rz, false);
        return BoxPass(BoxPass(a, rx, true), rz, false);
    }

    private static float[,] BoxPass(float[,] src, int r, bool alongX)
    {
        int h = src.GetLength(0), w = src.GetLength(1);
        var dst = new float[h, w];
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int k = -r; k <= r; k++)
            {
                int xx = alongX ? Mathf.Clamp(x + k, 0, w - 1) : x, zz = alongX ? z : Mathf.Clamp(z + k, 0, h - 1);
                sum += src[zz, xx];
            }
            dst[z, x] = sum / (2 * r + 1);
        }
        return dst;
    }
}
