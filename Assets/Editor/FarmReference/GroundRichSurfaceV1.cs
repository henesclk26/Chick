using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Gives the farm's existing twenty ground layers depth without touching their
    // colours: the Chick/Terrain Rich shader draws all of them in one pass from texture arrays, with normal, cavity and
    // occlusion maps made from each layer's own painting (Assets/Art/Terrain/RichSurfaceV1, by make_surface.py), mixes
    // them by height (grass over earth with a ragged edge), and lays a world-space macro variation over the farm.
    // The terrain layers and their textures are not changed; "Restore Original Ground Material" puts the old material
    // back. A separate menu item tunes the light (screen-space AO, cool shadows, a touch of warmth and saturation).
    public static class GroundRichSurfaceV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/RichSurfaceV1/";
        const string AlbedoPath = Folder + "Ground_Albedo_Array.png", NormalPath = Folder + "Ground_Normal_Array.png";
        const string MaskPath = Folder + "Ground_Mask_Array.png", MacroPath = Folder + "Ground_Macro_Variation.png";
        const string DataPath = Folder + "Ground_Layer_Data.asset", MaterialPath = Folder + "TerrainRich.mat";
        const string OriginalMaterial = "Assets/Art/FarmReference/Terrain/TerrainMaterial.mat";
        const string ShaderName = "Chick/Terrain Rich";
        const int Columns = 5, Rows = 4, Layers = 20;

        // per layer, in terrain order: normal strength and height offset (grass a little above the earth it meets)
        static readonly float[] NormalStrength = { .8f, .8f, .8f, .8f, 1f, 1f, 1f, .9f, 1f, .8f, .8f, .8f, .8f, .95f, .9f, .9f, .8f, .8f, .8f, 1f };
        static readonly float[] HeightOffset = { .04f, .04f, .04f, .04f, 0, 0, 0, .02f, 0, .04f, .04f, .04f, .04f, .01f, 0, 0, .04f, .04f, .04f, 0 };

        [MenuItem(Menu + "Rich Ground Surface (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Restore Original Ground Material")]
        static void MenuRestore() => Debug.Log(Restore());

        [MenuItem(Menu + "Rich Ground Lighting (V1)")]
        static void MenuLighting() => Debug.Log(ApplyLighting());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            if (data.terrainLayers.Length != Layers) throw new InvalidOperationException("Expected " + Layers + " ground layers, found " + data.terrainLayers.Length + ".");
            var shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException(ShaderName + " is missing or does not compile.");

            ImportArray(AlbedoPath, true, TextureImporterFormat.BC7);
            ImportArray(NormalPath, false, TextureImporterFormat.BC5);
            ImportArray(MaskPath, false, TextureImporterFormat.BC7);
            ImportMacro();
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AlbedoPath) ?? throw new InvalidOperationException("Colour array did not import.");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2DArray>(NormalPath) ?? throw new InvalidOperationException("Normal array did not import.");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2DArray>(MaskPath) ?? throw new InvalidOperationException("Mask array did not import.");
            if (albedo.depth != Layers || normal.depth != Layers || mask.depth != Layers) throw new InvalidOperationException("Arrays should hold " + Layers + " slices.");

            var layerData = LayerData(data);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) { mat = new Material(shader) { name = "TerrainRich" }; AssetDatabase.CreateAsset(mat, MaterialPath); }
            Undo.RecordObject(mat, "Rich ground surface");
            mat.shader = shader;
            mat.SetTexture("_AlbedoArray", albedo);
            mat.SetTexture("_NormalArray", normal);
            mat.SetTexture("_MaskArray", mask);
            mat.SetTexture("_LayerData", layerData);
            mat.SetTexture("_MacroTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MacroPath));
            var ctrl = data.alphamapTextures;
            for (int i = 0; i < 5; i++) mat.SetTexture("_Ctrl" + i, i < ctrl.Length ? ctrl[i] : Texture2D.blackTexture);
            mat.SetFloat("_LayerCount", Layers);
            mat.SetFloat("_CtrlRes", data.alphamapResolution);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            Undo.RecordObject(terrain, "Rich ground surface");
            terrain.materialTemplate = mat;
            EditorUtility.SetDirty(terrain);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Rich ground: " + Layers + " layers in one pass (" + albedo.width + " px colour, " + mask.width + " px masks), height blend " +
                   mat.GetFloat("_RichHeightTransition") + ", macro variation on. Terrain material -> " + MaterialPath + ".";
        }

        public static string Restore()
        {
            var terrain = GameObject.Find("Farm Reference Map").GetComponentInChildren<Terrain>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterial);
            Undo.RecordObject(terrain, "Restore ground material");
            terrain.materialTemplate = mat;
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return "Ground material restored to " + OriginalMaterial + ".";
        }

        // tiling, colour remap, normal strength and height offset of every layer, exactly as the terrain layers have them
        static Texture2D LayerData(TerrainData data)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DataPath);
            if (tex == null)
            {
                tex = new Texture2D(Layers, 3, TextureFormat.RGBAFloat, false, true) { name = "Ground_Layer_Data" };
                AssetDatabase.CreateAsset(tex, DataPath);
            }
            tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
            var size = data.size;
            for (int i = 0; i < Layers; i++)
            {
                var l = data.terrainLayers[i];
                tex.SetPixel(i, 0, new Color(size.x/l.tileSize.x, size.z/l.tileSize.y, l.tileOffset.x/l.tileSize.x, l.tileOffset.y/l.tileSize.y));
                var remap = l.diffuseRemapMax - l.diffuseRemapMin;
                tex.SetPixel(i, 1, new Color(remap.x, remap.y, remap.z, NormalStrength[i]));
                string n = l.name;
                bool earth = n.Contains("Earth") || n.Contains("Soil") || n.Contains("Pebbles") || n.Contains("Sand") || n.Contains("Stony");
                tex.SetPixel(i, 2, new Color(HeightOffset[i], earth ? 1 : 0, 0, 0));
            }
            tex.Apply(false, false);
            EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssetIfDirty(tex);
            return tex;
        }

        static void ImportArray(string path, bool srgb, TextureImporterFormat format)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("Missing " + path);
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Default;
            s.textureShape = TextureImporterShape.Texture2DArray;
            s.flipbookColumns = Columns; s.flipbookRows = Rows;
            s.sRGBTexture = srgb;
            s.mipmapEnabled = true;
            s.alphaSource = TextureImporterAlphaSource.FromInput;
            s.wrapMode = TextureWrapMode.Repeat;
            s.filterMode = FilterMode.Trilinear;
            s.aniso = 4;
            s.npotScale = TextureImporterNPOTScale.None;
            importer.SetTextureSettings(s);
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.overridden = true; platform.maxTextureSize = 8192; platform.format = format; platform.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }

        static void ImportMacro()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(MacroPath) ?? throw new InvalidOperationException("Missing " + MacroPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        // ---- light ----------------------------------------------------------------------------------------------------

        public static string ApplyLighting()
        {
            var sb = new System.Text.StringBuilder("Rich ground lighting:");
            // screen-space ambient occlusion: deeper and a little wider, so the feet of stones, fences and props sit in the ground
            var rendererData = new SerializedObject(GraphicsSettings.currentRenderPipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            var features = new SerializedObject(rendererData).FindProperty("m_RendererFeatures");
            for (int i = 0; i < features.arraySize; i++)
            {
                var f = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var so = new SerializedObject(f);
                Undo.RecordObject(f, "Rich ground lighting");
                so.FindProperty("m_Settings.Intensity").floatValue = .9f;
                so.FindProperty("m_Settings.Radius").floatValue = .3f;
                so.FindProperty("m_Settings.DirectLightingStrength").floatValue = .35f;
                so.FindProperty("m_Settings.Falloff").floatValue = 60f;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(f);
                sb.Append(" SSAO 0.9 / 0.3 m;");
            }
            // the global volume: cool, slightly green shadows, warm light, a little more colour
            var volume = Object.FindFirstObjectByType<Volume>();
            if (volume != null && volume.sharedProfile != null)
            {
                var profile = volume.sharedProfile;
                Undo.RecordObject(profile, "Rich ground lighting");
                var smh = Get<UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights>(profile);
                smh.shadows.Override(new Vector4(.93f, 1.0f, 1.04f, -.02f));
                smh.highlights.Override(new Vector4(1.03f, 1.01f, .96f, 0f));
                var ca = Get<UnityEngine.Rendering.Universal.ColorAdjustments>(profile);
                ca.saturation.Override(8f);
                ca.contrast.Override(6f);
                var wb = Get<UnityEngine.Rendering.Universal.WhiteBalance>(profile);
                wb.temperature.Override(5f);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                sb.Append(" volume " + profile.name + ": cool shadows, warm highlights, saturation +8, contrast +6, temperature +5.");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var c))
            {
                c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            return c;
        }
    }
}
