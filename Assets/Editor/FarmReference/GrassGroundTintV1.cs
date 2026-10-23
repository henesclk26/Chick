using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Lets the grass (Chick_Grass_Green, Chick_Grass_Dry) take the colour of the ground
    // under it, blade by blade: green over grass, golden over earth, so a clump on the edge of a path turns from one to the other.
    //  - bakes a top-down map of the farm terrain (Assets/Art/Grass/GroundTint.png): rgb = the grass colour of the ground
    //    (the grass layers' mean colours by weight, with the terrain shader's macro variation and grass grade), a = its
    //    earth share;
    //  - puts GroundTintGlobals on the terrain, which hands the map and the golden colour to the shader;
    //  - turns on _GROUND_TINT on both grass materials.
    // Re-run after repainting the ground. "Remove Grass Ground Tint" turns it off again.
    public static class GrassGroundTintV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string MapPath = "Assets/Art/Grass/GroundTint.png";
        const string GrassMaterial = "Assets/Art/Grass/Chick_Grass_Green.mat";
        const string DryMaterial = "Assets/Art/Grass/Chick_Grass_Dry.mat";
        const float DryAmount = 1f;
        const int Res = 1024;
        static readonly int[] Earth = { 4, 5, 6, 8, 13, 14, 15, 19 };   // the earth-type ground layers

        [MenuItem(Menu + "Grass Ground Tint (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Remove Grass Ground Tint")]
        static void MenuRemove()
        {
            foreach (var path in new[] { GrassMaterial, DryMaterial })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) { Undo.RecordObject(mat, "Remove grass ground tint"); mat.DisableKeyword("_GROUND_TINT"); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
            }
            Debug.Log("Grass ground tint off (Chick_Grass_Green, Chick_Grass_Dry).");
        }

        public static string Apply()
        {
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            int ares = data.alphamapResolution, L = data.alphamapLayers;
            var alpha = data.GetAlphamaps(0, 0, ares, ares);

            // every layer's mean colour (linear), with its colour remap
            var mean = new Color[L];
            for (int l = 0; l < L; l++)
            {
                var layer = data.terrainLayers[l];
                var c = MeanLinear(layer.diffuseTexture);
                var remap = layer.diffuseRemapMax - layer.diffuseRemapMin;
                mean[l] = new Color(c.r*remap.x, c.g*remap.y, c.b*remap.z);
            }

            // the terrain shader's macro variation
            var tmat = terrain.materialTemplate;
            bool macroOn = tmat != null && tmat.HasProperty("_MacroTex") && tmat.GetTexture("_MacroTex") != null;
            Color[] macro = null; int mw = 0, mh = 0;
            if (macroOn) { var mt = tmat.GetTexture("_MacroTex"); macro = ReadLinear(mt, mt.width, mt.height); mw = mt.width; mh = mt.height; }
            bool gradeOn = tmat != null && tmat.HasProperty("_GrassSaturation") && tmat.HasProperty("_GrassTint");
            float grassSaturation = gradeOn ? tmat.GetFloat("_GrassSaturation") : 1f;
            Color grassTint = gradeOn ? tmat.GetColor("_GrassTint").linear : Color.white;

            Vector3 tp = terrain.transform.position, size = data.size;
            // rgb: the grass colour of the ground (grass layers only; over bare earth the green of the grass around it,
            // so a blade there still has a green to turn to), a: the earth share of the ground
            var num = new Color[Res*Res]; var den = new float[Res*Res]; var earthShare = new float[Res*Res];
            int step = Mathf.Max(1, ares/Res);
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                {
                    Color c = Color.black; float g = 0, e = 0;
                    for (int dz = 0; dz < step; dz++)
                        for (int dx = 0; dx < step; dx++)
                            for (int l = 0; l < L; l++)
                            {
                                float w = alpha[Mathf.Min(z*step + dz, ares - 1), Mathf.Min(x*step + dx, ares - 1), l];
                                if (w <= 0) continue;
                                if (Array.IndexOf(Earth, l) >= 0) e += w; else { c += mean[l]*w; g += w; }
                            }
                    int i = z*Res + x;
                    num[i] = c; den[i] = g; earthShare[i] = e/(step*step);
                }
            var spreadNum = BlurColor(num, Res, 12); var spreadDen = BlurFloat(den, Res, 12);
            var px = new Color[Res*Res];
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                {
                    int i = z*Res + x;
                    Color around = spreadDen[i] > 1e-3f ? spreadNum[i]/spreadDen[i] : mean[1];
                    Color c = den[i] > 1e-3f ? Color.Lerp(around, num[i]/den[i], Mathf.Clamp01((den[i] - .05f)/.25f)) : around;
                    if (macroOn)
                    {
                        float wx = tp.x + (x + .5f)/Res*size.x, wz = tp.z + (z + .5f)/Res*size.z;
                        float m1 = Bilinear(macro, mw, mh, wx/tmat.GetFloat("_MacroScaleLarge"), wz/tmat.GetFloat("_MacroScaleLarge")).r;
                        float m2 = Bilinear(macro, mw, mh, wx/tmat.GetFloat("_MacroScaleMedium") + .37f, wz/tmat.GetFloat("_MacroScaleMedium") + .37f).g;
                        float patch = m1*.55f + m2*.45f;
                        c *= 1 + (m1 - .5f)*2*tmat.GetFloat("_MacroBrightness");
                        var warm = tmat.GetColor("_MacroWarmColor").linear; var lush = tmat.GetColor("_MacroLushColor").linear;
                        c = Color.Lerp(c, c*warm, SmoothStep(.54f, .70f, patch)*tmat.GetFloat("_MacroWarm"));
                        c = Color.Lerp(c, c*lush, SmoothStep(.46f, .30f, patch)*tmat.GetFloat("_MacroLush"));
                    }
                    if (gradeOn)
                    {
                        // the terrain shader's grass grade (saturation and tint of the green layers), so the blades
                        // keep the colour of the meadow they stand in
                        float lum = c.r*.3f + c.g*.59f + c.b*.11f;
                        c = Color.Lerp(new Color(lum, lum, lum), c, grassSaturation);
                        c = new Color(Mathf.Max(c.r, 0)*grassTint.r, Mathf.Max(c.g, 0)*grassTint.g, Mathf.Max(c.b, 0)*grassTint.b);
                    }
                    var gc = c.gamma;                   // rgb stored as sRGB, read back as linear by the shader
                    px[i] = new Color(gc.r, gc.g, gc.b, Mathf.Clamp01(earthShare[i]));
                }

            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(MapPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(MapPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(MapPath);
            imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.alphaIsTransparency = false; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = Res; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            var map = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);

            var globals = terrain.GetComponent<GroundTintGlobals>();
            if (globals == null) globals = Undo.AddComponent<GroundTintGlobals>(terrain.gameObject);
            Undo.RecordObject(globals, "Grass ground tint");
            globals.Configure(map, new Vector4(tp.x, tp.z, 1f/size.x, 1f/size.z));
            EditorUtility.SetDirty(globals);

            // both grasses, blade by blade: green over grass, golden over earth
            Tint(GrassMaterial, 1f);
            Tint(DryMaterial, DryAmount);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Grass ground tint: " + Res + " px ground colour map baked (" + MapPath + "), GroundTintGlobals on the terrain, _GROUND_TINT on Chick_Grass_Green (1) and Chick_Grass_Dry (" + DryAmount + ").";
        }

        static void Tint(string path, float amount)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidOperationException("Missing " + path);
            Undo.RecordObject(mat, "Grass ground tint");
            mat.EnableKeyword("_GROUND_TINT");
            mat.SetFloat("_DetailAlbedoMapScale", amount);      // the per-material tint amount (see InteractiveFoliage.shader)
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
        }

        // separable box blurs, clamped at the edges (sums, not averages: the callers divide colour by weight)
        static Color[] BlurColor(Color[] src, int n, int r)
        {
            var tmp = new Color[src.Length]; var o = new Color[src.Length];
            for (int z = 0; z < n; z++)
            {
                Color s = Color.clear; for (int x = -r; x <= r; x++) s += src[z*n + Mathf.Clamp(x, 0, n - 1)];
                for (int x = 0; x < n; x++) { tmp[z*n + x] = s; s += src[z*n + Mathf.Min(x + r + 1, n - 1)] - src[z*n + Mathf.Max(x - r, 0)]; }
            }
            for (int x = 0; x < n; x++)
            {
                Color s = Color.clear; for (int z = -r; z <= r; z++) s += tmp[Mathf.Clamp(z, 0, n - 1)*n + x];
                for (int z = 0; z < n; z++) { o[z*n + x] = s; s += tmp[Mathf.Min(z + r + 1, n - 1)*n + x] - tmp[Mathf.Max(z - r, 0)*n + x]; }
            }
            return o;
        }

        static float[] BlurFloat(float[] src, int n, int r)
        {
            var tmp = new float[src.Length]; var o = new float[src.Length];
            for (int z = 0; z < n; z++)
            {
                float s = 0; for (int x = -r; x <= r; x++) s += src[z*n + Mathf.Clamp(x, 0, n - 1)];
                for (int x = 0; x < n; x++) { tmp[z*n + x] = s; s += src[z*n + Mathf.Min(x + r + 1, n - 1)] - src[z*n + Mathf.Max(x - r, 0)]; }
            }
            for (int x = 0; x < n; x++)
            {
                float s = 0; for (int z = -r; z <= r; z++) s += tmp[Mathf.Clamp(z, 0, n - 1)*n + x];
                for (int z = 0; z < n; z++) { o[z*n + x] = s; s += tmp[Mathf.Min(z + r + 1, n - 1)*n + x] - tmp[Mathf.Max(z - r, 0)*n + x]; }
            }
            return o;
        }

        static Color MeanLinear(Texture tex)
        {
            if (tex == null) return Color.grey;
            var px = ReadLinear(tex, 64, 64);
            Color s = Color.black; foreach (var p in px) s += p;
            return s/px.Length;
        }

        // the texture's pixels in linear colour (a blit into a linear target decodes sRGB textures)
        static Color[] ReadLinear(Texture tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels(); Object.DestroyImmediate(t);
            return px;
        }

        static Color Bilinear(Color[] px, int w, int h, float u, float v)
        {
            u = (u - Mathf.Floor(u))*w - .5f; v = (v - Mathf.Floor(v))*h - .5f;
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v); float fx = u - x0, fy = v - y0;
            Func<int, int, Color> at = (x, y) => px[((y%h + h)%h)*w + (x%w + w)%w];
            return Color.Lerp(Color.Lerp(at(x0, y0), at(x0 + 1, y0), fx), Color.Lerp(at(x0, y0 + 1), at(x0 + 1, y0 + 1), fx), fy);
        }

        static float SmoothStep(float a, float b, float v) { float t = Mathf.Clamp01((v - a)/(b - a)); return t*t*(3 - 2*t); }
    }
}
