using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. The farm's ground layers are hand painted and smooth: seen from the chick's
    // camera, a hand's width above the ground, they are magnified into a soft smear. This bakes one small tileable
    // close-up detail (Assets/Art/Terrain/RichSurfaceV1/Ground_CloseDetail*.png) in the same painted spirit, soft dabs
    // rather than photographic noise:
    //  - earth: crumbs and small clods with a lit top, a few shallow pits and darker specks;
    //  - grass: soft, short brush strokes leaning in loose directions, mostly lighter, with low relief (thin, deep
    //    strokes read as cracks under a low sun).
    // Colour (R earth, G grass; 0.5 = unchanged) and relief (RG earth normal, BA grass normal) are separate files.
    // Chick/Terrain Rich lays it in world space over the first few metres in front of the camera only, by the
    // ground's earth share, on top of the layer textures, which stay as they are; the far view does not change.
    public static class GroundCloseDetailV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Terrain/RichSurfaceV1/";
        const string ColourPath = Folder + "Ground_CloseDetail.png", NormalPath = Folder + "Ground_CloseDetail_Normal.png";
        const string MaterialPath = Folder + "TerrainRich.mat";
        const int Res = 512;
        const int Seed = 4127;

        [MenuItem(Menu + "Close-up Ground Detail (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Remove Close-up Ground Detail")]
        static void MenuRemove()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) return;
            Undo.RecordObject(mat, "Remove close-up ground detail");
            mat.SetTexture("_CloseDetail", null); mat.SetTexture("_CloseDetailNormal", null);
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            Debug.Log("Close-up ground detail off (" + MaterialPath + ").");
        }

        public static string Apply()
        {
            var rng = new Random(Seed);
            float[] earthH = new float[Res*Res], earthC = new float[Res*Res];
            float[] grassH = new float[Res*Res], grassC = new float[Res*Res];

            // earth: soft mottle, then crumbs (raised, lighter), pits (sunk, darker) and dark specks
            Mottle(earthC, rng, 6, .10f);
            for (int i = 0; i < 1100; i++)
            {
                float r = Lerp(1.6f, 6.5f, Mathf.Pow((float)rng.NextDouble(), 1.8f));
                float amp = Lerp(.5f, 1f, (float)rng.NextDouble());
                Dab(earthH, earthC, rng, r, r*Lerp(.7f, 1f, (float)rng.NextDouble()), amp, amp*.22f);
            }
            for (int i = 0; i < 140; i++)
            {
                float r = Lerp(4f, 11f, (float)rng.NextDouble());
                Dab(earthH, earthC, rng, r, r*.8f, -.45f, -.12f);
            }
            for (int i = 0; i < 260; i++)
            {
                float r = Lerp(1f, 2.4f, (float)rng.NextDouble());
                Dab(earthH, earthC, rng, r, r, -.1f, -.3f);
            }

            // grass: short strokes, loosely along one direction, a lighter and a darker family
            Mottle(grassC, rng, 5, .08f);
            for (int i = 0; i < 1100; i++)
            {
                bool light = rng.NextDouble() < .6;
                float len = Lerp(12f, 26f, (float)rng.NextDouble());
                float wid = Lerp(3.5f, 6f, (float)rng.NextDouble());
                Stroke(grassH, grassC, rng, len, wid, light ? .35f : -.12f, light ? .11f : -.08f);
            }

            var colour = new Color[Res*Res];
            var normal = new Color[Res*Res];
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    int i = y*Res + x;
                    colour[i] = new Color(Mathf.Clamp01(.5f + earthC[i]), Mathf.Clamp01(.5f + grassC[i]), .5f, 1f);
                    Vector2 en = Slope(earthH, x, y, 2.2f), gn = Slope(grassH, x, y, .7f);
                    normal[i] = new Color(.5f + en.x*.5f, .5f + en.y*.5f, .5f + gn.x*.5f, .5f + gn.y*.5f);
                }

            Write(ColourPath, colour);
            Write(NormalPath, normal);
            Import(ColourPath); Import(NormalPath);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) ?? throw new InvalidOperationException("Missing " + MaterialPath);
            Undo.RecordObject(mat, "Close-up ground detail");
            mat.SetTexture("_CloseDetail", AssetDatabase.LoadAssetAtPath<Texture2D>(ColourPath));
            mat.SetTexture("_CloseDetailNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return "Close-up ground detail: " + Res + " px earth crumbs and grass strokes (" + ColourPath + ", " + NormalPath + ") on " + MaterialPath +
                   ", tile " + mat.GetFloat("_DetailSize") + " m, within " + mat.GetFloat("_DetailDistance") + " m of the camera.";
        }

        // a soft elliptical bump at a random spot, wrapped around the tile edges
        static void Dab(float[] h, float[] c, Random rng, float rx, float ry, float height, float tone)
        {
            float cx = (float)rng.NextDouble()*Res, cy = (float)rng.NextDouble()*Res;
            float a = (float)(rng.NextDouble()*Math.PI);
            Splat(h, c, cx, cy, rx, ry, a, height, tone);
        }

        static void Stroke(float[] h, float[] c, Random rng, float len, float wid, float height, float tone)
        {
            float cx = (float)rng.NextDouble()*Res, cy = (float)rng.NextDouble()*Res;
            float a = (float)(Math.PI*.5 + (rng.NextDouble() - .5)*2.2);
            Splat(h, c, cx, cy, len*.5f, wid*.5f, a, height, tone);
        }

        static void Splat(float[] h, float[] c, float cx, float cy, float rx, float ry, float a, float height, float tone)
        {
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            int reach = Mathf.CeilToInt(Mathf.Max(rx, ry)) + 1;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float px = Mathf.Floor(cx) + dx + .5f - cx, py = Mathf.Floor(cy) + dy + .5f - cy;
                    float u = (px*ca + py*sa)/rx, v = (-px*sa + py*ca)/ry;
                    float d = u*u + v*v;
                    if (d >= 1f) continue;
                    float f = (1f - d)*(1f - d);
                    int x = Wrap((int)Mathf.Floor(cx) + dx), y = Wrap((int)Mathf.Floor(cy) + dy);
                    h[y*Res + x] += height*f;
                    c[y*Res + x] += tone*f;
                }
        }

        // tileable value noise, a few cells across
        static void Mottle(float[] c, Random rng, int cells, float amp)
        {
            var g = new float[cells*cells];
            for (int i = 0; i < g.Length; i++) g[i] = (float)rng.NextDouble()*2f - 1f;
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float fx = (float)x/Res*cells, fy = (float)y/Res*cells;
                    int x0 = (int)fx, y0 = (int)fy; float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
                    float v00 = g[(y0%cells)*cells + x0%cells], v10 = g[(y0%cells)*cells + (x0 + 1)%cells];
                    float v01 = g[((y0 + 1)%cells)*cells + x0%cells], v11 = g[((y0 + 1)%cells)*cells + (x0 + 1)%cells];
                    c[y*Res + x] += Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty)*amp;
                }
        }

        // tangent-space normal xy from the height field
        static Vector2 Slope(float[] h, int x, int y, float strength)
        {
            float sx = h[y*Res + Wrap(x + 1)] - h[y*Res + Wrap(x - 1)];
            float sy = h[Wrap(y + 1)*Res + x] - h[Wrap(y - 1)*Res + x];
            var n = new Vector3(-sx*strength, -sy*strength, 1f).normalized;
            return new Vector2(n.x, n.y);
        }

        static void Write(string path, Color[] px)
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = Res;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static int Wrap(int v) => ((v%Res) + Res)%Res;
        static float Lerp(float a, float b, float t) => a + (b - a)*t;
        static float Smooth(float t) => t*t*(3f - 2f*t);
    }
}
