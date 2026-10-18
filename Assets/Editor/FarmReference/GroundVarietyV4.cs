using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Adds seven variations of the RichGreen ground (Assets/Art/Terrain/
    // RichVariantsV1, made from GrassRich.png itself) to the farm terrain beside its nine layers, so all sixteen are
    // used. Nothing is taken away: the new grounds take a share of the meadow greens (OriginalMeadow, RichGreen,
    // FreshGreen, SunlitGreen and half of WornMeadow) and leave paths, pebbles, old earth and the tilled field exactly
    // as they are. Each is scattered all over the map in its own soft drifting patches (the grasses broad, the earths
    // small, like bare spots), and comes on stronger where it belongs: shade under the trees' crowns, dry meadow up
    // the hills, earth along the fringe of the trodden paths, damp earth and sand in bands round the pond.
    // The nine-layer weights are kept in Temp/ the first time, so a re-run starts from the original paint.
    public static class GroundVarietyV4
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/RichVariantsV1/";
        const string Reference = "Assets/Art/Terrain/MeadowVarietyV3/GrassRich.png";
        const string Backup = "Temp/GroundVarietyV4_NineLayerAlphamap.bin";
        static readonly string[] Names = { "RichGreen_Lush", "RichGreen_Clover", "RichGreen_Dry", "RichGreen_Shade", "RichGreen_Earth", "RichGreen_Sand", "RichGreen_DampEarth" };
        static readonly float[] Tiles = { 5.0f, 5.5f, 5.3f, 4.9f, 4.2f, 4.4f, 4.0f };
        const int OldCount = 9, Trodden = 4, OldEarth = 5, WornMeadow = 7;
        const int Lush = 9, Clover = 10, Dry = 11, Shade = 12, Earth = 13, Sand = 14, Damp = 15;
        const float MaxShare = .85f;   // never more than this much of a texel's meadow goes to the new grounds

        [MenuItem(Menu + "Add RichGreen Ground Variations (V4)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            if (data.terrainLayers.Length != OldCount && data.terrainLayers.Length != OldCount + Names.Length)
                throw new InvalidOperationException("Expected 9 (or 16) terrain layers, found " + data.terrainLayers.Length + ".");

            Undo.RegisterCompleteObjectUndo(data, "Add RichGreen ground variations");
            var old = NineLayerWeights(data, res);
            var layers = new List<TerrainLayer>(data.terrainLayers);
            if (layers.Count > OldCount) layers.RemoveRange(OldCount, layers.Count - OldCount);
            for (int i = 0; i < Names.Length; i++) layers.Add(EnsureLayer(i));
            data.terrainLayers = layers.ToArray();

            Vector3 tp = terrain.transform.position, size = data.size;
            float cell = size.x/res, cellZ = size.z/res;
            // where the paths are, softened, to find their grassy fringe
            var path = new float[res, res];
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) path[z, x] = old[z, x, Trodden] + .6f*old[z, x, OldEarth];
            var pathNear = Blur(path, Mathf.RoundToInt(1.3f/cell), Mathf.RoundToInt(1.3f/cellZ));
            var shade = TreeShade(root, tp, size, res);
            var shore = PondRelocationV1.NewShoreline();
            Vector2 sMin = shore[0], sMax = shore[0];
            foreach (var p in shore) { sMin = Vector2.Min(sMin, p); sMax = Vector2.Max(sMax, p); }
            sMin -= Vector2.one*5f; sMax += Vector2.one*5f;
            float farmGround = root.transform.position.y + .25f;

            var a = new float[res, res, OldCount + Names.Length];
            var sums = new double[OldCount + Names.Length];
            var f = new float[Names.Length];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = tp.x + (x + .5f)*cell, wz = tp.z + (z + .5f)*cellZ;
                float meadow = old[z, x, 0] + old[z, x, 1] + old[z, x, 2] + old[z, x, 3] + .5f*old[z, x, WornMeadow];
                Array.Clear(f, 0, f.Length);
                if (meadow > .001f)
                {
                    // every ground is scattered all over the map in its own drifting patches; where it naturally
                    // belongs (shade under trees, earth at path edges, damp and sand round the pond) it comes on stronger
                    float shadeHere = shade[z, x];
                    float height = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tp.y - farmGround;
                    float open = (1 - .6f*shadeHere)*Mathf.Lerp(.7f, 1f, Mathf.Clamp01(height/2.5f));
                    f[Lush - OldCount] = .55f*Patch(wx, wz, 13f, 101f, .52f);
                    f[Clover - OldCount] = .5f*Patch(wx, wz, 11f, 233f, .55f);
                    f[Dry - OldCount] = .5f*Patch(wx, wz, 16f, 377f, .52f)*open;
                    f[Shade - OldCount] = Mathf.Max(.75f*shadeHere*(.75f + .25f*Mathf.PerlinNoise(wx*.4f + 7, wz*.4f + 3)), .45f*Patch(wx, wz, 12f, 419f, .6f));
                    float fringe = Mathf.Clamp01((pathNear[z, x] - path[z, x])*2.4f);
                    f[Earth - OldCount] = Mathf.Max(.55f*fringe*(.55f + .45f*Mathf.PerlinNoise(wx*.35f + 11, wz*.35f + 19)), .55f*Patch(wx, wz, 7f, 521f, .61f));
                    f[Sand - OldCount] = .5f*Patch(wx, wz, 8f, 613f, .63f);
                    f[Damp - OldCount] = .5f*Patch(wx, wz, 7.5f, 709f, .63f);
                    if (wx > sMin.x && wx < sMax.x && wz > sMin.y && wz < sMax.y)
                    {
                        float sd = FarmPondV1.Signed(shore, wx, wz);
                        float wobble = Mathf.PerlinNoise(wx*.6f + 31, wz*.6f + 17);
                        f[Damp - OldCount] = Mathf.Max(f[Damp - OldCount], .8f*Band(sd, .2f, 2.2f + .8f*wobble));
                        f[Sand - OldCount] = Mathf.Max(f[Sand - OldCount], .55f*Band(sd, 1.6f + .6f*wobble, 4.2f)*(.5f + .5f*Mathf.PerlinNoise(wx*.3f + 3, wz*.3f + 41)));
                    }
                }
                float total = 0;
                for (int k = 0; k < f.Length; k++) total += f[k];
                if (total > MaxShare) { float s = MaxShare/total; for (int k = 0; k < f.Length; k++) f[k] *= s; total = MaxShare; }
                for (int l = 0; l < OldCount; l++) a[z, x, l] = old[z, x, l];
                for (int l = 0; l < 4; l++) a[z, x, l] *= 1 - total;
                a[z, x, WornMeadow] *= 1 - .5f*total;
                for (int k = 0; k < f.Length; k++) a[z, x, OldCount + k] = meadow*f[k];
                for (int l = 0; l < OldCount + Names.Length; l++) sums[l] += a[z, x, l];
            }
            data.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);

            var sb = new System.Text.StringBuilder("Ground variations added: " + data.terrainLayers.Length + " layers.");
            double count = (double)res*res;
            for (int l = 0; l < sums.Length; l++) sb.Append("\n  " + data.terrainLayers[l].name + " " + (100*sums[l]/count).ToString("F1") + "%");
            return sb.ToString();
        }

        // The terrain's nine original layers: read from the backup if one was made, else from the terrain (and saved).
        static float[,,] NineLayerWeights(TerrainData data, int res)
        {
            var w = new float[res, res, OldCount];
            if (File.Exists(Backup))
            {
                using (var r = new BinaryReader(File.OpenRead(Backup)))
                {
                    if (r.ReadInt32() == res)
                    {
                        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < OldCount; l++) w[z, x, l] = r.ReadSingle();
                        return w;
                    }
                }
            }
            if (data.terrainLayers.Length != OldCount) throw new InvalidOperationException("No nine-layer backup in Temp/ to start again from.");
            var cur = data.GetAlphamaps(0, 0, res, res);
            Directory.CreateDirectory(Path.GetDirectoryName(Backup));
            using (var wr = new BinaryWriter(File.Create(Backup)))
            {
                wr.Write(res);
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < OldCount; l++) { w[z, x, l] = cur[z, x, l]; wr.Write(cur[z, x, l]); }
            }
            return w;
        }

        static TerrainLayer EnsureLayer(int i)
        {
            string tex = Folder + Names[i] + ".png", path = Folder + Names[i] + ".terrainlayer";
            // the same import settings as GrassRich, so the new grounds read exactly alike
            var refImporter = (TextureImporter)AssetImporter.GetAtPath(Reference);
            var importer = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (importer == null) throw new InvalidOperationException("Missing " + tex);
            var settings = new TextureImporterSettings();
            refImporter.ReadTextureSettings(settings);
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(refImporter.GetDefaultPlatformTextureSettings());
            importer.SaveAndReimport();
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.name = Names[i];
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            layer.tileSize = Vector2.one*Tiles[i];
            layer.tileOffset = new Vector2(i*1.37f + .4f, i*.91f + .7f);   // never in step with RichGreen
            layer.metallic = 0; layer.smoothness = 0;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // 0..1 shade under every tree's crown (Forest/Hill/Yard/Scenery trees, willows, pines), soft at the edge.
        static float[,] TreeShade(GameObject root, Vector3 tp, Vector3 size, int res)
        {
            var shade = new float[res, res];
            float cell = size.x/res, cellZ = size.z/res;
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.Contains("Tree") || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (b.size.y < 3f || b.size.y > 40f) continue;
                float radius = Mathf.Clamp(.45f*(b.size.x + b.size.z)*.5f, 1.5f, 6f);
                Vector2 c = new Vector2(r.transform.position.x, r.transform.position.z);
                int x0 = Mathf.FloorToInt((c.x - radius*1.2f - tp.x)/cell), x1 = Mathf.CeilToInt((c.x + radius*1.2f - tp.x)/cell);
                int z0 = Mathf.FloorToInt((c.y - radius*1.2f - tp.z)/cellZ), z1 = Mathf.CeilToInt((c.y + radius*1.2f - tp.z)/cellZ);
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(res - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(res - 1, x1); x++)
                {
                    float wx = tp.x + (x + .5f)*cell, wz = tp.z + (z + .5f)*cellZ;
                    float d = Vector2.Distance(new Vector2(wx, wz), c);
                    float edge = radius*(.85f + .3f*Mathf.PerlinNoise(wx*.5f + 13, wz*.5f + 7));
                    shade[z, x] = Mathf.Max(shade[z, x], 1 - Smooth(edge*.45f, edge, d));
                }
            }
            return shade;
        }

        // Soft drifting patches: 0 outside, 1 well inside, `cover` of the ground roughly in them.
        static float Patch(float x, float z, float scale, float seed, float threshold)
        {
            float n = .65f*Mathf.PerlinNoise(x/scale + seed, z/scale - seed) + .35f*Mathf.PerlinNoise(x/(scale*.35f) - seed, z/(scale*.35f) + seed*.5f);
            return Smooth(threshold, threshold + .14f, n);
        }

        // 1 between `from` and `to` metres outside the pond's shoreline, easing off at both ends.
        static float Band(float sd, float from, float to) => Smooth(from - .5f, from + .3f, sd)*(1 - Smooth(to - .6f, to + .4f, sd));

        static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a)/(b - a));
            return t*t*(3 - 2*t);
        }

        static float[,] Blur(float[,] src, int rx, int rz)
        {
            int h = src.GetLength(0), w = src.GetLength(1);
            var tmp = new float[h, w]; var dst = new float[h, w];
            for (int z = 0; z < h; z++)
            {
                float acc = 0; int n = 0;
                for (int x = -rx; x < w + rx; x++)
                {
                    if (x + rx < w) { acc += src[z, Mathf.Clamp(x + rx, 0, w - 1)]; n++; }
                    if (x - rx - 1 >= 0) { acc -= src[z, x - rx - 1]; n--; }
                    if (x >= 0 && x < w) tmp[z, x] = acc/Mathf.Max(1, n);
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0; int n = 0;
                for (int z = -rz; z < h + rz; z++)
                {
                    if (z + rz < h) { acc += tmp[Mathf.Clamp(z + rz, 0, h - 1), x]; n++; }
                    if (z - rz - 1 >= 0) { acc -= tmp[z - rz - 1, x]; n--; }
                    if (z >= 0 && z < h) dst[z, x] = acc/Mathf.Max(1, n);
                }
            }
            return dst;
        }
    }
}
