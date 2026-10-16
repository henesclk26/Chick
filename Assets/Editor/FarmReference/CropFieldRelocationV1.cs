using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. The crop field was moved by hand onto the meadow north-west of the
    // old one, but its tilled soil is painted into the terrain (MixedCropFieldsV1.PaintSoil), so it stayed behind.
    // This moves the soil with the planting: the old field rectangles return to the varied meadow of
    // MeadowVarietyV3, and the new planting gets dark tilled soil (its own TilledSoil layer, added once), with
    // furrows along the planted rows. Paths outside the field are left as they are.
    // Note: re-running MixedCropFieldsV1 or NaturalHillsV4 repaints the old rectangles.
    public static class CropFieldRelocationV1
    {
        const string Menu = "Tools/Chick/Crop Field/";
        // World XZ: (minX, maxX, minZ, maxZ). The old ones are MixedCropFieldsV1.West and .East.
        static readonly Vector4 OldWest = new Vector4(66.3f, 91.4f, -25.2f, -8.6f);
        static readonly Vector4 OldEast = new Vector4(95.4f, 120.6f, -25.2f, -8.6f);
        // The planting as laid out in the scene (corn, wheat, vine, pepper, lettuce, tomato, strawberry and
        // blackberry rows, scarecrow: x 64.0..87.2, z -14.4..2.4) with a small margin, short of the pond shore.
        static readonly Vector4 NewField = new Vector4(64.0f, 87.8f, -15.0f, 2.6f);
        const int Trodden = 4, OldEarth = 5;
        // Dark, damp tilled soil: the path earth read as light, dusty ground. Appended after the eight V3 layers.
        internal const int Tilled = 8;
        const string TilledTexture = "Assets/Art/Terrain/CropFieldV1/TilledSoil.png";
        const string TilledLayer = "Assets/Art/Terrain/CropFieldV1/TilledSoil.terrainlayer";

        [MenuItem(Menu + "Move Field Soil To New Field (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            if (root == null) throw new InvalidOperationException("Farm root missing.");
            var terrain = root.GetComponentInChildren<Terrain>();
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            if (data.alphamapLayers < 8 || data.terrainLayers[Trodden].name != "TroddenEarth" || data.terrainLayers[OldEarth].name != "OldEarthAccent")
                throw new InvalidOperationException("Unexpected terrain layers; this tool expects the MeadowVarietyV3 set.");
            EnsureTilledLayer(data);
            int layers = data.alphamapLayers;

            var trees = Trees(root);

            int w = data.alphamapWidth, h = data.alphamapHeight;
            var origin = terrain.transform.position;
            float minX = Mathf.Min(OldWest.x, NewField.x) - 2, maxX = OldEast.y + 2;
            float minZ = OldWest.z - 2, maxZ = NewField.w + 2;
            int x0 = Mathf.Max(0, (int)((minX - origin.x)/data.size.x*(w-1))), x1 = Mathf.Min(w-1, (int)((maxX - origin.x)/data.size.x*(w-1)) + 1);
            int z0 = Mathf.Max(0, (int)((minZ - origin.z)/data.size.z*(h-1))), z1 = Mathf.Min(h-1, (int)((maxZ - origin.z)/data.size.z*(h-1)) + 1);

            Undo.RegisterCompleteObjectUndo(data.alphamapTextures, "Move crop field soil V1");
            var a = data.GetAlphamaps(x0, z0, x1-x0+1, z1-z0+1);
            var meadow = new float[layers];
            int restored = 0, tilled = 0;
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = origin.x + (ix+x0)*data.size.x/(w-1), z = origin.z + (iz+z0)*data.size.z/(h-1);
                var p = root.transform.InverseTransformPoint(new Vector3(x, 0, z));
                OrganicFarmPaths.Evaluate(new Vector2(p.x, p.z), out float core, out _);
                float newSoil = Soil(NewField, x, z);
                // Wherever the old soil reached, even faintly at its soft edge: a partial blend there left a thin brown
                // outline of the old field in the grass. The meadow formula matches the grass around it, so no seam.
                float restore = Smooth(0, .08f, Mathf.Max(Soil(OldWest, x, z), Soil(OldEast, x, z)))*(1 - newSoil)*(1 - core);
                if (restore > 0)
                {
                    Meadow(p, trees, meadow);
                    for (int i = 0; i < layers; i++) a[iz, ix, i] = Mathf.Lerp(a[iz, ix, i], meadow[i], restore);
                    restored++;
                }
                // Inside the field everything is tilled, old path lines included (skipping them left green strips);
                // only the soft edge outside it keeps clear of the paths that run along it.
                bool inside = x >= NewField.x && x <= NewField.y && z >= NewField.z && z <= NewField.w;
                float soil = newSoil*(inside ? 1 : 1 - core);
                if (soil > 0)
                {
                    // The planting runs in north-south rows, so the furrows do too: dark grooves, slightly drier ridges.
                    float ridge = .5f + .5f*Mathf.Sin(x*Mathf.PI/.9f);
                    for (int i = 0; i < layers; i++) a[iz, ix, i] *= 1 - soil;
                    a[iz, ix, Tilled] += soil*(1 - .3f*ridge);
                    a[iz, ix, OldEarth] += soil*.3f*ridge;
                    tilled++;
                }
                float total = 0; for (int i = 0; i < layers; i++) total += a[iz, ix, i];
                if (total > 0) for (int i = 0; i < layers; i++) a[iz, ix, i] /= total;
            }
            data.SetAlphamaps(x0, z0, a); data.SetBaseMapDirty();
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            return "Crop field soil moved: " + restored + " cells back to meadow, " + tilled + " cells tilled. Terrain: " + AssetDatabase.GetAssetPath(data);
        }

        // The old field sat on the builder's flat field pad; the new one partly on the meadow's rolling ground, so its
        // northern rows rose and fell by up to ~20 cm and the plants (set for flat ground) sank in or floated.
        const float FieldLevel = -11.75f;   // world height of the old field pad, which the plants are placed for
        const float FlattenFalloff = 1f;

        [MenuItem(Menu + "Flatten New Field Ground (V1)")]
        static void MenuFlatten() => Debug.Log(Flatten());

        public static string Flatten()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            int r = data.heightmapResolution;
            var origin = terrain.transform.position;
            float target = (FieldLevel - origin.y)/data.size.y;
            int x0 = Mathf.Max(0, (int)((NewField.x - FlattenFalloff - origin.x)/data.size.x*(r-1))), x1 = Mathf.Min(r-1, (int)((NewField.y + FlattenFalloff - origin.x)/data.size.x*(r-1)) + 1);
            int z0 = Mathf.Max(0, (int)((NewField.z - FlattenFalloff - origin.z)/data.size.z*(r-1))), z1 = Mathf.Min(r-1, (int)((NewField.w + FlattenFalloff - origin.z)/data.size.z*(r-1)) + 1);
            Undo.RegisterCompleteObjectUndo(data, "Flatten new crop field V1");
            var heights = data.GetHeights(x0, z0, x1-x0+1, z1-z0+1);
            float maxChange = 0;
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = origin.x + (ix+x0)*data.size.x/(r-1), z = origin.z + (iz+z0)*data.size.z/(r-1);
                // Flat across the field, easing back to the untouched ground within a metre around it.
                float outside = Mathf.Max(Mathf.Max(NewField.x - x, x - NewField.y), Mathf.Max(NewField.z - z, z - NewField.w));
                float weight = 1 - Smooth(0, FlattenFalloff, outside);
                if (weight <= 0) continue;
                float before = heights[iz, ix];
                heights[iz, ix] = Mathf.Lerp(before, target, weight);
                maxChange = Mathf.Max(maxChange, Mathf.Abs(heights[iz, ix] - before)*data.size.y);
            }
            data.SetHeights(x0, z0, heights);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            return "New field flattened to y " + FieldLevel + " (largest change " + (maxChange*100).ToString("F0") + " cm).";
        }

        // Adds the TilledSoil layer (importing its texture like the V3 ground tiles) after the eight V3 layers, once.
        static void EnsureTilledLayer(TerrainData data)
        {
            var current = data.terrainLayers;
            if (current.Length > Tilled && current[Tilled] != null && current[Tilled].name == "TilledSoil") return;
            if (current.Length != Tilled) throw new InvalidOperationException("Expected exactly the eight V3 layers before adding TilledSoil.");
            var importer = AssetImporter.GetAtPath(TilledTexture) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture not imported: " + TilledTexture);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Mirror;   // as every V3 layer: URP terrain shares one sampler per pass
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.name = "Standalone"; platform.overridden = true;
            platform.format = TextureImporterFormat.DXT1;
            platform.maxTextureSize = 2048; platform.compressionQuality = 100;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TilledLayer);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, TilledLayer); }
            layer.name = "TilledSoil";
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TilledTexture);
            layer.tileSize = Vector2.one*3.2f;
            layer.tileOffset = new Vector2(Tilled*1.73f, Tilled*.83f);
            layer.metallic = 0; layer.smoothness = 0;
            layer.diffuseRemapMin = Vector4.zero; layer.diffuseRemapMax = new Vector4(1, 1, 1, 0);
            EditorUtility.SetDirty(layer); AssetDatabase.SaveAssetIfDirty(layer);
            // Adding a layer keeps the existing weights and gives the new one zero everywhere.
            var layers = new TerrainLayer[Tilled + 1];
            Array.Copy(current, layers, Tilled);
            layers[Tilled] = layer;
            data.terrainLayers = layers;
        }

        // MixedCropFieldsV1.PaintSoil's field mask: noisy, soft edges about a metre wide.
        static float Soil(Vector4 r, float x, float z)
        {
            float d = Mathf.Max(Mathf.Max(r.x - x, x - r.y), Mathf.Max(r.z - z, z - r.w)) + (Mathf.PerlinNoise(x*.7f, z*.7f) - .5f)*.8f;
            return 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.4f, .6f, d));
        }

        internal static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));

        // Tree positions in root-local XZ, as MeadowVarietyV3 collects them (the meadow is lusher near trees).
        internal static List<Vector2> Trees(GameObject root)
        {
            var trees = new List<Vector2>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0)
                { var p = root.transform.InverseTransformPoint(r.bounds.center); trees.Add(new Vector2(p.x, p.z)); }
            return trees;
        }

        // MeadowVarietyV3's grass away from paths: dominant rich, fresh and sunlit patches, denser near trees,
        // with its sparse worn spots, so the restored ground matches the meadow around it.
        internal static void Meadow(Vector3 p, List<Vector2> trees, float[] weights)
        {
            var q = new Vector2(p.x, p.z);
            float treeDistance = 1000;
            foreach (var tree in trees) treeDistance = Mathf.Min(treeDistance, (q - tree).sqrMagnitude);
            float treeBias = 1 - Smooth(3, 11, Mathf.Sqrt(treeDistance));
            float n = Mathf.PerlinNoise(p.x*.043f + 19.2f, p.z*.043f + 31.6f);
            float n2 = Mathf.PerlinNoise(p.x*.058f + 82.7f, p.z*.058f + 9.3f);
            float lush = Smooth(.38f, .64f, n + treeBias*.21f);
            float sun = Smooth(.42f, .67f, n2)*(1 - lush);
            float fresh = Mathf.Max(.04f, 1 - lush - sun);
            lush *= lush; sun *= sun; fresh *= fresh;
            float sum = lush + sun + fresh; lush /= sum; sun /= sum; fresh /= sum;
            float wear = Smooth(.69f, .83f, Mathf.PerlinNoise(p.x*.095f + 71, p.z*.095f + 45))*.45f;
            float grass = 1 - wear;
            weights[0] = grass*.09f;
            weights[1] = grass*.91f*lush;
            weights[2] = grass*.91f*fresh;
            weights[3] = grass*.91f*sun;
            for (int i = 4; i < weights.Length; i++) weights[i] = 0;
            weights[7] = wear;
        }
    }
}
