using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Brings the farm terrain to twenty ground layers: adds four more
    // (Assets/Art/Terrain/RichVariantsV2) beside the sixteen, taking nothing away.
    //  - RichGreen_Cool, RichGreen_Golden, RichGreen_Olive: the RichGreen ground in a cooler, a sunnier and an olive
    //    tone, scattered evenly over all the meadows in soft drifting patches; they take a share of the plain meadow
    //    greens (OriginalMeadow, RichGreen, FreshGreen, SunlitGreen, RichGreen_Lush, RichGreen_Clover).
    //  - StonyPath: the path earth with a light scatter of small stones, along about half of the trodden paths in
    //    stretches, so a path runs now stony, now plain; it takes its share from TroddenEarth.
    // Everything else (the other grounds, the tilled field, the pond paint) is left exactly as it is. The sixteen-layer
    // weights are kept in Temp/ the first time, so a re-run starts from them.
    public static class GroundVarietyV5
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/RichVariantsV2/";
        const string Reference = "Assets/Art/Terrain/MeadowVarietyV3/GrassRich.png";
        const string Backup = "Temp/GroundVarietyV5_SixteenLayerAlphamap.bin";
        static readonly string[] Names = { "RichGreen_Cool", "RichGreen_Golden", "RichGreen_Olive", "StonyPath" };
        static readonly float[] Tiles = { 5.4f, 5.0f, 5.6f, 3.6f };
        const int OldCount = 16, Trodden = 4;
        static readonly int[] Meadow = { 0, 1, 2, 3, 9, 10 };
        const float MaxShare = .7f;     // never more than this much of a texel's plain meadow goes to the new tones

        [MenuItem(Menu + "Add Twenty-Layer Ground Variations (V5)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            if (data.terrainLayers.Length != OldCount && data.terrainLayers.Length != OldCount + Names.Length)
                throw new InvalidOperationException("Expected 16 (or 20) terrain layers, found " + data.terrainLayers.Length + ".");

            Undo.RegisterCompleteObjectUndo(data, "Add twenty-layer ground variations");
            var old = SixteenLayerWeights(data, res);
            var layers = new List<TerrainLayer>(data.terrainLayers);
            if (layers.Count > OldCount) layers.RemoveRange(OldCount, layers.Count - OldCount);
            for (int i = 0; i < Names.Length; i++) layers.Add(EnsureLayer(i));
            data.terrainLayers = layers.ToArray();

            Vector3 tp = terrain.transform.position, size = data.size;
            float cell = size.x/res, cellZ = size.z/res;
            int total = OldCount + Names.Length;
            var a = new float[res, res, total];
            var sums = new double[total];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = tp.x + (x + .5f)*cell, wz = tp.z + (z + .5f)*cellZ;
                for (int l = 0; l < OldCount; l++) a[z, x, l] = old[z, x, l];
                // the three meadow tones, each in its own patches all over the map
                float meadow = 0;
                foreach (int l in Meadow) meadow += old[z, x, l];
                if (meadow > .001f)
                {
                    float cool = .5f*Patch(wx, wz, 14f, 811f, .54f), golden = .5f*Patch(wx, wz, 15f, 919f, .54f), olive = .45f*Patch(wx, wz, 17f, 1013f, .56f);
                    float share = cool + golden + olive;
                    if (share > MaxShare) { float s = MaxShare/share; cool *= s; golden *= s; olive *= s; share = MaxShare; }
                    foreach (int l in Meadow) a[z, x, l] *= 1 - share;
                    a[z, x, OldCount] = meadow*cool; a[z, x, OldCount + 1] = meadow*golden; a[z, x, OldCount + 2] = meadow*olive;
                }
                // stony stretches along the trodden paths
                float trodden = old[z, x, Trodden];
                if (trodden > .001f)
                {
                    float stony = .9f*Patch(wx, wz, 9f, 1103f, .47f);
                    a[z, x, Trodden] = trodden*(1 - stony);
                    a[z, x, OldCount + 3] = trodden*stony;
                }
                for (int l = 0; l < total; l++) sums[l] += a[z, x, l];
            }
            data.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);

            var sb = new System.Text.StringBuilder("Ground layers: " + data.terrainLayers.Length + ".");
            double count = (double)res*res;
            for (int l = 0; l < total; l++) sb.Append("\n  " + data.terrainLayers[l].name + " " + (100*sums[l]/count).ToString("F1") + "%");
            return sb.ToString();
        }

        // The terrain's sixteen layers: from the backup if one was made, else from the terrain (and saved).
        static float[,,] SixteenLayerWeights(TerrainData data, int res)
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
            if (data.terrainLayers.Length != OldCount) throw new InvalidOperationException("No sixteen-layer backup in Temp/ to start again from.");
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
            var importer = (TextureImporter)AssetImporter.GetAtPath(tex) ?? throw new InvalidOperationException("Missing " + tex);
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
            layer.tileOffset = new Vector2(i*1.91f + 2.3f, i*1.13f + 1.6f);   // never in step with the other grounds
            layer.metallic = 0; layer.smoothness = 0;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // Soft drifting patches: 0 outside, 1 well inside.
        static float Patch(float x, float z, float scale, float seed, float threshold)
        {
            float n = .65f*Mathf.PerlinNoise(x/scale + seed, z/scale - seed) + .35f*Mathf.PerlinNoise(x/(scale*.35f) - seed, z/(scale*.35f) + seed*.5f);
            float t = Mathf.Clamp01((n - threshold)/.14f);
            return t*t*(3 - 2*t);
        }
    }
}
