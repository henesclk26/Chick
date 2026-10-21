using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application, for comparing two combed-grass grounds on the test Plane only
    // (Map/Props and Buildings/Plane), from Assets/Art/Terrain/CombGrassTest (made by make_combgrass.py):
    //  - north half: "like the reference" - bright yellow-green, soft thin combed strokes, pale sunny patches;
    //  - south half: the soft brush and the reference combined, in the farm's own GrassRich / RichGreen_Dry colours.
    // In each half the green and the yellow layer are mixed in large soft patches by height (Chick/Ground Rich Mesh),
    // so the patch edges are feathered by the strokes. The farm map is not touched; "Restore Test Plane Material"
    // (RefStyleTestPlaneV1) puts URP Lit back.
    public static class CombGrassTestPlaneV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/CombGrassTest/";
        const string PlanePath = "Map/Props and Buildings/Plane/Plane";
        const int Res = 256;

        [MenuItem(Menu + "Comb Grass Test Plane (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var plane = GameObject.Find(PlanePath) ?? throw new InvalidOperationException(PlanePath + " missing.");
            var renderer = plane.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Chick/Ground Rich Mesh") ?? throw new InvalidOperationException("Chick/Ground Rich Mesh missing.");

            Import(Folder + "CombGrass_Albedo_Array.png", true);
            Import(Folder + "CombGrass_Normal_Array.png", false);
            Import(Folder + "CombGrass_Mask_Array.png", false);

            var b = renderer.bounds;
            var ctrl = Bake(b);

            string matPath = Folder + "CombGrassTestGround.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.SetTexture("_AlbedoArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "CombGrass_Albedo_Array.png"));
            mat.SetTexture("_NormalArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "CombGrass_Normal_Array.png"));
            mat.SetTexture("_MaskArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "CombGrass_Mask_Array.png"));
            mat.SetTexture("_Ctrl0", ctrl); mat.SetTexture("_Ctrl1", Texture2D.blackTexture);
            mat.SetVector("_CtrlRect", new Vector4(b.min.x, b.min.z, 1f/b.size.x, 1f/b.size.z));
            mat.SetVector("_Tile0123", new Vector4(5f, 5.3f, 5f, 5.3f));
            mat.SetVector("_Tile45", new Vector4(5, 5, 0, 0));
            mat.SetVector("_Normal0123", Vector4.one);
            mat.SetFloat("_NormalStrength", 1.1f);
            mat.SetFloat("_HeightTransition", .05f);
            mat.SetFloat("_CavityStrength", .7f);
            mat.SetTexture("_MacroTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Terrain/RichSurfaceV1/Ground_Macro_Variation.png"));
            mat.SetFloat("_MacroBrightness", .05f);
            mat.SetFloat("_MacroFine", .02f);
            mat.SetFloat("_MacroWarm", 0f);
            mat.SetFloat("_MacroLush", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            Undo.RecordObject(renderer, "Comb grass test plane");
            renderer.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(plane.scene);
            return "Test Plane: north half = reference-like combed grass, south half = brush + reference in the farm's colours. The farm map is unchanged.";
        }

        // R/G: version 1 green/yellow (north half), B/A: version 2 green/yellow (south half)
        static Texture2D Bake(Bounds b)
        {
            var px = new Color[Res*Res];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float x = b.min.x + (i + .5f)/Res*b.size.x, z = b.min.z + (j + .5f)/Res*b.size.z;
                    float yellow = Smooth(.42f, .7f, Mathf.PerlinNoise(x*.07f + 4.2f, z*.07f + 9.1f)*.8f + Mathf.PerlinNoise(x*.2f + 1, z*.2f + 3)*.2f);
                    float north = Smooth(-1.2f, 1.2f, z - b.center.z);
                    px[j*Res + i] = new Color((1 - yellow)*north, yellow*north, (1 - yellow)*(1 - north), yellow*(1 - north));
                }
            string path = Folder + "CombGrass_Control.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true) { name = "CombGrass_Control" }; AssetDatabase.CreateAsset(tex, path); }
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px); tex.Apply(false, false);
            EditorUtility.SetDirty(tex); AssetDatabase.SaveAssetIfDirty(tex);
            return tex;
        }

        static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a)/(b - a)); return t*t*(3 - 2*t); }

        static void Import(string path, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("Missing " + path);
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Default;
            s.textureShape = TextureImporterShape.Texture2DArray;
            s.flipbookColumns = 2; s.flipbookRows = 2;
            s.sRGBTexture = srgb; s.mipmapEnabled = true;
            s.wrapMode = TextureWrapMode.Repeat; s.filterMode = FilterMode.Trilinear; s.aniso = 4;
            importer.SetTextureSettings(s);
            importer.maxTextureSize = 2048;
            var p = importer.GetPlatformTextureSettings("Standalone");
            p.overridden = true; p.maxTextureSize = 2048; p.format = TextureImporterFormat.BC7; p.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(p);
            importer.SaveAndReimport();
        }
    }
}
