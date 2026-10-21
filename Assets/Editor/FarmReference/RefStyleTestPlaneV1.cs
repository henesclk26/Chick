using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application, for trying the reference-style grounds before they go anywhere else: dresses
    // the test Plane (Map/Props and Buildings/Plane) with the six new grounds (Assets/Art/Terrain/RefStyleTest, made by
    // make_refstyle.py) through Chick/Ground Rich Mesh: combed meadow grass all over with dry yellow patches, deep shade
    // grass under the four trees, a winding cracked dirt path with mossy dirt along its edges and in a few patches, and a
    // small rippled sand clearing - all height-blended, with normal, cavity and occlusion maps and the macro variation.
    // "Restore Test Plane Material" puts URP's default Lit back.
    public static class RefStyleTestPlaneV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/RefStyleTest/";
        const string PlanePath = "Map/Props and Buildings/Plane/Plane";
        const string DefaultLit = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";
        const int Res = 256;
        // layers: 0 meadow, 1 shade grass, 2 cracked path, 3 mossy dirt, 4 sand, 5 dry-patch meadow

        [MenuItem(Menu + "Reference Style Test Plane (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Restore Test Plane Material")]
        static void MenuRestore() => Debug.Log(Restore());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var plane = GameObject.Find(PlanePath) ?? throw new InvalidOperationException(PlanePath + " missing.");
            var renderer = plane.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Chick/Ground Rich Mesh") ?? throw new InvalidOperationException("Chick/Ground Rich Mesh missing.");

            Import(Folder + "RefStyle_Albedo_Array.png", true, TextureImporterFormat.BC7);
            Import(Folder + "RefStyle_Normal_Array.png", false, TextureImporterFormat.BC7);
            Import(Folder + "RefStyle_Mask_Array.png", false, TextureImporterFormat.BC7);

            var b = renderer.bounds;
            var trees = TreesOn(b, renderer);
            var ctrl = Bake(b, trees);

            string matPath = Folder + "RefStyleTestGround.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.SetTexture("_AlbedoArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "RefStyle_Albedo_Array.png"));
            mat.SetTexture("_NormalArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "RefStyle_Normal_Array.png"));
            mat.SetTexture("_MaskArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(Folder + "RefStyle_Mask_Array.png"));
            mat.SetTexture("_Ctrl0", ctrl[0]); mat.SetTexture("_Ctrl1", ctrl[1]);
            mat.SetVector("_CtrlRect", new Vector4(b.min.x, b.min.z, 1f/b.size.x, 1f/b.size.z));
            mat.SetVector("_Tile0123", new Vector4(4f, 4f, 3.5f, 3.5f));
            mat.SetVector("_Tile45", new Vector4(4.5f, 4.2f, 0, 0));
            mat.SetVector("_Normal0123", new Vector4(.9f, .9f, 1f, 1f));
            mat.SetVector("_Normal45", new Vector4(1f, .9f, 0, 0));
            mat.SetTexture("_MacroTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Terrain/RichSurfaceV1/Ground_Macro_Variation.png"));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            Undo.RecordObject(renderer, "Reference style test plane");
            renderer.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(plane.scene);
            return "Test Plane dressed with the six reference-style grounds (" + trees.Count + " trees found for the shade grass).";
        }

        public static string Restore()
        {
            var plane = GameObject.Find(PlanePath);
            var renderer = plane.GetComponent<MeshRenderer>();
            Undo.RecordObject(renderer, "Restore test plane");
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultLit);
            EditorSceneManager.MarkSceneDirty(plane.scene);
            return "Test Plane back on URP Lit.";
        }

        // the trunks of the trees standing on the plane
        static List<Vector2> TreesOn(Bounds b, Renderer self)
        {
            var list = new List<Vector2>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == self) continue;
                var rb = r.bounds;
                if (rb.size.y < 2.5f || rb.center.x < b.min.x || rb.center.x > b.max.x || rb.center.z < b.min.z || rb.center.z > b.max.z) continue;
                if (Mathf.Abs(rb.min.y - b.max.y) > 1.5f) continue;
                var p = new Vector2(rb.center.x, rb.center.z);
                if (!list.Exists(q => (q - p).sqrMagnitude < 4f)) list.Add(p);
            }
            return list;
        }

        static Texture2D[] Bake(Bounds b, List<Vector2> trees)
        {
            var c0 = new Color[Res*Res]; var c1 = new Color[Res*Res];
            float minX = b.min.x, minZ = b.min.z, sx = b.size.x, sz = b.size.z;
            var sand = new Vector2(minX + sx*.76f, minZ + sz*.26f);
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float x = minX + (i + .5f)/Res*sx, z = minZ + (j + .5f)/Res*sz;
                    float v = (z - minZ)/sz;
                    float w0 = 1, w1 = 0, w2 = 0, w3 = 0, w4 = 0, w5 = 0;
                    void Over(ref float target, float a) { w0 *= 1 - a; w1 *= 1 - a; w2 *= 1 - a; w3 *= 1 - a; w4 *= 1 - a; w5 *= 1 - a; target += a; }

                    // dry yellow patches in the meadow
                    float dry = Smooth(.56f, .68f, Mathf.PerlinNoise(x*.075f + 3.1f, z*.075f + 11.7f));
                    // deep shade grass round the trees
                    float shade = 0;
                    foreach (var t in trees)
                    {
                        float d = Vector2.Distance(t, new Vector2(x, z)) + (Mathf.PerlinNoise(x*.3f + 5, z*.3f + 9) - .5f)*2.4f;
                        shade = Mathf.Max(shade, 1 - Smooth(4.2f, 6.8f, d));
                    }
                    // the winding path and the mossy dirt along it
                    float cx = minX + sx*(.5f + .13f*Mathf.Sin(v*Mathf.PI*1.7f + .6f) + .05f*Mathf.Sin(v*Mathf.PI*4.3f));
                    float edge = (Mathf.PerlinNoise(x*.4f + 21, z*.4f + 2) - .5f)*1.5f + (Mathf.PerlinNoise(x*1.3f, z*1.3f + 40) - .5f)*.5f;
                    float dp = Mathf.Abs(x - cx) + edge;
                    float path = 1 - Smooth(1.05f, 1.45f, dp);
                    float moss = (1 - Smooth(2.0f, 3.3f, dp))*Smooth(.3f, .6f, Mathf.PerlinNoise(x*.2f + 13, z*.2f + 3));
                    moss = Mathf.Max(moss, Smooth(.69f, .76f, Mathf.PerlinNoise(x*.11f + 31, z*.11f + 17)));
                    // a small sandy clearing
                    float ds = Vector2.Distance(sand, new Vector2(x, z)) + (Mathf.PerlinNoise(x*.35f + 50, z*.35f + 60) - .5f)*2.6f;
                    float sandW = 1 - Smooth(4.0f, 5.6f, ds);
                    float sandRim = (1 - Smooth(5.4f, 6.8f, ds))*(1 - sandW);

                    Over(ref w5, dry*(1 - shade));
                    Over(ref w1, shade);
                    Over(ref w3, Mathf.Max(moss, sandRim*.7f));
                    Over(ref w4, sandW);
                    Over(ref w2, path);
                    c0[j*Res + i] = new Color(w0, w1, w2, w3);
                    c1[j*Res + i] = new Color(w4, w5, 0, 0);
                }
            return new[] { Save("RefStyle_Control_0", c0), Save("RefStyle_Control_1", c1) };
        }

        static Texture2D Save(string name, Color[] px)
        {
            string path = Folder + name + ".asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true) { name = name }; AssetDatabase.CreateAsset(tex, path); }
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px); tex.Apply(false, false);
            EditorUtility.SetDirty(tex); AssetDatabase.SaveAssetIfDirty(tex);
            return tex;
        }

        static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a)/(b - a)); return t*t*(3 - 2*t); }

        static void Import(string path, bool srgb, TextureImporterFormat format)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("Missing " + path);
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Default;
            s.textureShape = TextureImporterShape.Texture2DArray;
            s.flipbookColumns = 3; s.flipbookRows = 2;
            s.sRGBTexture = srgb; s.mipmapEnabled = true;
            s.wrapMode = TextureWrapMode.Repeat; s.filterMode = FilterMode.Trilinear; s.aniso = 4;
            s.npotScale = TextureImporterNPOTScale.None;
            importer.SetTextureSettings(s);
            importer.maxTextureSize = 4096;
            var p = importer.GetPlatformTextureSettings("Standalone");
            p.overridden = true; p.maxTextureSize = 4096; p.format = format; p.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(p);
            importer.SaveAndReimport();
        }
    }
}
