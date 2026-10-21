using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Brings the "Handpainted Grass & Ground Textures" pack into the farm ground:
    // each of the twenty terrain layers gets the pack texture of its own role, recoloured to that layer's lively colours
    // (Assets/Art/Terrain/HandpaintedV1/HP_<layer>.png, made by make_handpainted.py from the untouched pack files), so the
    // painted layout of the map stays and only the painting changes. The layers' former textures and tiling are kept in
    // HandpaintedV1/OriginalLayers.json; "Restore Original Ground Textures" puts them back.
    public static class HandpaintedGroundV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/HandpaintedV1/";
        const string Backup = Folder + "OriginalLayers.json";

        [Serializable] class Entry { public string layer, texture; public Vector2 tile; }
        [Serializable] class Record { public Entry[] entries; }

        [MenuItem(Menu + "Handpainted Pack Ground (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Restore Original Ground Textures")]
        static void MenuRestore() => Debug.Log(Restore());

        static TerrainLayer[] Layers()
        {
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            return terrain.terrainData.terrainLayers;
        }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var layers = Layers();
            // remember what the layers had, the first time only
            if (!File.Exists(Backup))
            {
                var rec = new Record { entries = new Entry[layers.Length] };
                for (int i = 0; i < layers.Length; i++)
                    rec.entries[i] = new Entry { layer = layers[i].name, texture = AssetDatabase.GetAssetPath(layers[i].diffuseTexture), tile = layers[i].tileSize };
                File.WriteAllText(Backup, JsonUtility.ToJson(rec, true));
                AssetDatabase.ImportAsset(Backup);
            }
            int done = 0;
            foreach (var l in layers)
            {
                string path = Folder + "HP_" + l.name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null) { Debug.LogWarning("No handpainted texture for " + l.name); continue; }
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                // an RGB format without alpha: URP terrain reads any alpha channel as full smoothness (a shiny ground)
                var platform = importer.GetPlatformTextureSettings("Standalone");
                platform.overridden = true; platform.maxTextureSize = 2048; platform.format = TextureImporterFormat.DXT1;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
                Undo.RecordObject(l, "Handpainted pack ground");
                l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                bool dirt = l.name.Contains("Earth") || l.name.Contains("Soil") || l.name.Contains("Pebbles") || l.name.Contains("Sand") || l.name.Contains("Stony");
                l.tileSize = Vector2.one*(dirt ? 4.5f : 5f);              // the pack is painted for about 5 m tiles
                l.smoothness = 0; l.metallic = 0;
                EditorUtility.SetDirty(l);
                done++;
            }
            AssetDatabase.SaveAssets();
            return "Handpainted pack ground: " + done + " of " + layers.Length + " layers now use HandpaintedV1 textures (originals kept in " + Backup + ").";
        }

        public static string Restore()
        {
            if (!File.Exists(Backup)) return "Nothing to restore: " + Backup + " not found.";
            var rec = JsonUtility.FromJson<Record>(File.ReadAllText(Backup));
            var layers = Layers();
            int done = 0;
            foreach (var e in rec.entries)
            {
                var l = Array.Find(layers, x => x.name == e.layer);
                if (l == null) continue;
                Undo.RecordObject(l, "Restore ground textures");
                l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(e.texture);
                l.tileSize = e.tile;
                EditorUtility.SetDirty(l);
                done++;
            }
            AssetDatabase.SaveAssets();
            return "Ground textures restored on " + done + " layers.";
        }
    }
}
