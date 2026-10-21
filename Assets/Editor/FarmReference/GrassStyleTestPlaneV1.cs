using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application, for trying one ground on the test Plane only (Map/Props and Buildings/Plane):
    // GrassRich's painted style with far fewer colour transitions, and a 3D grassy surface (a normal, cavity and
    // occlusion relief of short grass blades), from Assets/Art/Terrain/GrassStyleTest (made by make_grassstyle.py),
    // drawn with Chick/Ground Rich Mesh using a single layer and only the faintest macro variation.
    // The farm map is not touched. "Restore Test Plane Material" (RefStyleTestPlaneV1) puts URP Lit back.
    public static class GrassStyleTestPlaneV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/GrassStyleTest/";
        const string PlanePath = "Map/Props and Buildings/Plane/Plane";

        [MenuItem(Menu + "Grass Style Test Plane (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var plane = GameObject.Find(PlanePath) ?? throw new InvalidOperationException(PlanePath + " missing.");
            var renderer = plane.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Chick/Ground Rich Mesh") ?? throw new InvalidOperationException("Chick/Ground Rich Mesh missing.");

            Import(Folder + "GrassStyle_Albedo.png", true);
            Import(Folder + "GrassStyle_Normal.png", false);
            Import(Folder + "GrassStyle_Mask.png", false);

            // the whole plane is the one ground
            string ctrlPath = Folder + "GrassStyle_Control.asset";
            var ctrl = AssetDatabase.LoadAssetAtPath<Texture2D>(ctrlPath);
            if (ctrl == null)
            {
                ctrl = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "GrassStyle_Control", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = new Color(1, 0, 0, 0);
                ctrl.SetPixels(px); ctrl.Apply(false, false);
                AssetDatabase.CreateAsset(ctrl, ctrlPath);
            }

            string matPath = Folder + "GrassStyleTestGround.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.SetTexture("_AlbedoArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "GrassStyle_Albedo.png"));
            mat.SetTexture("_NormalArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "GrassStyle_Normal.png"));
            mat.SetTexture("_MaskArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "GrassStyle_Mask.png"));
            mat.SetTexture("_Ctrl0", ctrl); mat.SetTexture("_Ctrl1", Texture2D.blackTexture);
            var b = renderer.bounds;
            mat.SetVector("_CtrlRect", new Vector4(b.min.x, b.min.z, 1f/b.size.x, 1f/b.size.z));
            mat.SetVector("_Tile0123", new Vector4(5.2f, 5, 5, 5));
            mat.SetVector("_Tile45", new Vector4(5, 5, 0, 0));
            mat.SetVector("_Normal0123", new Vector4(1, 1, 1, 1));
            mat.SetVector("_Normal45", new Vector4(1, 1, 0, 0));
            mat.SetFloat("_NormalStrength", 1.4f);
            mat.SetFloat("_CavityStrength", .8f);
            mat.SetFloat("_OcclusionStrength", .8f);
            // few colour transitions: only a whisper of the macro variation
            mat.SetTexture("_MacroTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Terrain/RichSurfaceV1/Ground_Macro_Variation.png"));
            mat.SetFloat("_MacroBrightness", .03f);
            mat.SetFloat("_MacroFine", .015f);
            mat.SetFloat("_MacroWarm", 0f);
            mat.SetFloat("_MacroLush", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            Undo.RecordObject(renderer, "Grass style test plane");
            renderer.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(plane.scene);
            return "Test Plane: even GrassRich-style grass with a 3D grass-blade surface (" + matPath + "). The farm map is unchanged.";
        }

        static void Import(string path, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("Missing " + path);
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Default;
            s.textureShape = TextureImporterShape.Texture2DArray;
            s.flipbookColumns = 1; s.flipbookRows = 1;
            s.sRGBTexture = srgb; s.mipmapEnabled = true;
            s.wrapMode = TextureWrapMode.Repeat; s.filterMode = FilterMode.Trilinear; s.aniso = 4;
            importer.SetTextureSettings(s);
            importer.maxTextureSize = 1024;
            var p = importer.GetPlatformTextureSettings("Standalone");
            p.overridden = true; p.maxTextureSize = 1024; p.format = TextureImporterFormat.BC7; p.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(p);
            importer.SaveAndReimport();
        }
    }
}
