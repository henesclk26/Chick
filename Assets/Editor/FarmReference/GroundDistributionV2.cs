using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Repaints the farm terrain's layer weights for the handpainted ground textures
    // instead of reusing the old twenty-layer patchwork (which, under the new textures, left yellow seams and dark-green
    // blotches):
    //  - kept where they are, as clean masks: the paths (trodden earth, with stony stretches), the earth accents, the
    //    pebble patches, the tilled field and the sand / damp earth of the pond;
    //  - the meadows painted afresh: one calm base green (RichGreen), large soft lighter drifts (FreshGreen), faint tone
    //    variation (OriginalMeadow, RichGreen_Lush) and the darker shade grass only under the trees;
    //  - the yellow and the dull layers (SunlitGreen, WornMeadow, RichGreen_Dry, _Cool, _Golden, _Olive, _Clover) unused;
    //  - every border feathered over about 1.5 m.
    // The terrain layers themselves are not changed. One undo step; the previous weights are also written to
    // Temp/GroundDistributionV2_Before.bin.
    public static class GroundDistributionV2
    {
        const string Menu = "Tools/Chick/Ground/";
        const int Meadow = 0, Rich = 1, Fresh = 2, Trodden = 4, OldEarth = 5, Pebbles = 6, Tilled = 8, Lush = 9,
                  Shade = 12, EarthV = 13, Sand = 14, Damp = 15, Stony = 19, Count = 20;
        const int HaloRadius = 8;          // texels (about 2 m); three passes spread it over about 4.5 m
        const float HaloGain = 1.1f;
        const float WarpLarge = 2.2f, WarpSmall = .7f;   // metres the earth outlines are pushed about by

        [MenuItem(Menu + "Repaint Ground Distribution (V2)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            if (data.alphamapLayers != Count) throw new InvalidOperationException("Expected " + Count + " ground layers.");
            int res = data.alphamapResolution;
            // always start from the weights the first run found (Temp/GroundDistributionV2_Before.bin), so re-runs do not drift
            var a = LoadBackup(res) ?? data.GetAlphamaps(0, 0, res, res);
            Backup(a, res);

            // the structure to keep: every earth-type ground as one group, each part keeping its own kind; and the tilled
            // field. The earth is read through a soft domain warp and cut at a threshold that wanders, so the paths are no
            // longer ruled lines: their edges wobble, they widen and narrow, and a few grass islands sit in the wider earth.
            int[] earth = { Trodden, OldEarth, Pebbles, EarthV, Sand, Damp, Stony };
            Vector3 tp = terrain.transform.position, size = data.size;
            var ew = new float[res, res, Count];
            var esum = new float[res, res, 1];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                    float dx = ((Mathf.PerlinNoise(wx/11f + 3.7f, wz/11f + 9.1f) - .5f)*2*WarpLarge + (Mathf.PerlinNoise(wx/3.8f + 41f, wz/3.8f + 7f) - .5f)*2*WarpSmall)/size.x*res;
                    float dz = ((Mathf.PerlinNoise(wx/11f + 61.3f, wz/11f + 23.9f) - .5f)*2*WarpLarge + (Mathf.PerlinNoise(wx/3.8f + 17f, wz/3.8f + 83f) - .5f)*2*WarpSmall)/size.z*res;
                    float e = 0;
                    foreach (int l in earth) { float v = Sample(a, l, res, x + dx, z + dz); ew[z, x, l] = v; e += v; }
                    esum[z, x, 0] = e;
                }
            Blur(esum, 0, res, 4, 2);
            var ground = new float[res, res]; var till = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                    float eb = esum[z, x, 0];
                    float thr = .3f + .14f*(Mathf.PerlinNoise(wx/14f + 5.3f, wz/14f + 71f) - .5f)*2;
                    float g = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(thr - .13f, thr + .13f, eb));
                    float island = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .8f, Mathf.PerlinNoise(wx/3.2f + 13f, wz/3.2f + 37f)));
                    g *= 1 - .75f*island*Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.82f, 1f, eb));
                    ground[z, x] = g;
                    till[z, x] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .5f, a[z, x, Tilled]));
                }
            var shade = TreeShade(terrain, res);
            var w = new float[res, res, Count];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                    // fixed grounds, in order of precedence
                    float left = 1;
                    float t8 = Take(ref left, till[z, x]);
                    float g = Take(ref left, ground[z, x]);
                    float e = 0; foreach (int l in earth) e += ew[z, x, l];
                    w[z, x, Tilled] = t8;
                    if (g > 0)
                    {
                        float k = g/Mathf.Max(e, 1e-4f);
                        float path = (ew[z, x, Trodden] + ew[z, x, EarthV] + ew[z, x, Stony])*k;
                        float stony = .8f*Patch(wx, wz, 9f, 1103f, .5f);
                        w[z, x, Trodden] = path*(1 - stony); w[z, x, Stony] = path*stony;
                        w[z, x, OldEarth] = ew[z, x, OldEarth]*k; w[z, x, Pebbles] = ew[z, x, Pebbles]*k;
                        w[z, x, Sand] = ew[z, x, Sand]*k; w[z, x, Damp] = ew[z, x, Damp]*k;
                        if (e < 1e-4f) w[z, x, Trodden] = g;
                    }

                    // the meadow
                    float m = left;
                    float s = shade[z, x]*.75f;
                    float wShade = m*s; m -= wShade;
                    float light = .8f*Patch(wx, wz, 22f, 311f, .45f);
                    float wFresh = m*light; m -= wFresh;
                    float v0 = .55f*Patch(wx, wz, 11f, 523f, .5f);
                    float wMeadow = m*v0; m -= wMeadow;
                    float v9 = .35f*Patch(wx, wz, 15f, 727f, .58f);
                    float wLush = m*v9; m -= wLush;
                    w[z, x, Shade] = wShade; w[z, x, Fresh] = wFresh; w[z, x, Meadow] = wMeadow; w[z, x, Lush] = wLush; w[z, x, Rich] = m;
                }

            // a wide soft halo round every earth ground, outwards only (the cores stay as they are): the earth thins out
            // into the grass over 3-4 m, as in the pack's own screenshots
            int[] earthLayers = { Trodden, Stony, OldEarth, Pebbles, Sand, Damp };
            var before = new float[res, res];
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) { float t = 0; foreach (int l in earthLayers) t += w[z, x, l]; before[z, x] = t; }
            foreach (int l in earthLayers)
            {
                var core = new float[res, res, 1];
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) core[z, x, 0] = w[z, x, l];
                Blur(core, 0, res, HaloRadius, 3);
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        // an uneven fringe: here the earth fades far into the grass, there it stops short, and the
                        // fade itself breaks into patches
                        float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                        float reach = Mathf.PerlinNoise(wx/9f + 101f, wz/9f + 7f);
                        float grain = Mathf.PerlinNoise(wx/2.6f + 55f, wz/2.6f + 19f);
                        float v = core[z, x, 0]*HaloGain*(.3f + 1.2f*reach) - (1 - grain)*.32f;
                        v = .9f*Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .6f, v));
                        w[z, x, l] = Mathf.Max(w[z, x, l], v*(w[z, x, l] > 0 || core[z, x, 0] > 0 ? 1 : 0));
                    }
            }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float e = 0; foreach (int l in earthLayers) e += w[z, x, l];
                    if (e > 1) { foreach (int l in earthLayers) w[z, x, l] /= e; e = 1; }
                    float grassBefore = 1 - before[z, x] - w[z, x, Tilled];
                    float grassAfter = Mathf.Max(0, 1 - e - w[z, x, Tilled]);
                    float k = grassBefore > 1e-4f ? grassAfter/grassBefore : 0;
                    for (int l = 0; l < Count; l++)
                        if (l != Tilled && System.Array.IndexOf(earthLayers, l) < 0) w[z, x, l] *= k;
                }

            // feather every border over about 1.5 m, then normalise
            for (int l = 0; l < Count; l++) Blur(w, l, res, 3, 2);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float sum = 0; for (int l = 0; l < Count; l++) sum += w[z, x, l];
                    if (sum <= 0) { w[z, x, Rich] = 1; continue; }
                    for (int l = 0; l < Count; l++) w[z, x, l] /= sum;
                }

            Undo.RegisterCompleteObjectUndo(data, "Repaint ground distribution");
            data.SetAlphamaps(0, 0, w);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);

            var sb = new System.Text.StringBuilder("Ground distribution repainted:");
            for (int l = 0; l < Count; l++)
            {
                double t = 0; for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) t += w[z, x, l];
                if (t > 0) sb.Append("\n  " + data.terrainLayers[l].name + " " + (100*t/(res*(double)res)).ToString("F1") + "%");
            }
            return sb.ToString();
        }

        // bilinear, clamped
        static float Sample(float[,,] a, int l, int res, float x, float z)
        {
            x = Mathf.Clamp(x, 0, res - 1.001f); z = Mathf.Clamp(z, 0, res - 1.001f);
            int x0 = (int)x, z0 = (int)z; float fx = x - x0, fz = z - z0;
            return Mathf.Lerp(Mathf.Lerp(a[z0, x0, l], a[z0, x0 + 1, l], fx), Mathf.Lerp(a[z0 + 1, x0, l], a[z0 + 1, x0 + 1, l], fx), fz);
        }

        static float[,,] LoadBackup(int res)
        {
            const string path = "Temp/GroundDistributionV2_Before.bin";
            if (!System.IO.File.Exists(path)) return null;
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
            {
                if (r.ReadInt32() != res || r.ReadInt32() != Count) return null;
                var a = new float[res, res, Count];
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < Count; l++) a[z, x, l] = r.ReadSingle();
                return a;
            }
        }

        static float Take(ref float left, float want) { float t = Mathf.Min(left, want); left -= t; return t; }

        // soft drifting patches: 0 outside, 1 well inside
        static float Patch(float x, float z, float scale, float seed, float threshold)
        {
            float n = .65f*Mathf.PerlinNoise(x/scale + seed, z/scale - seed) + .35f*Mathf.PerlinNoise(x/(scale*.35f) - seed, z/(scale*.35f) + seed*.5f);
            float t = Mathf.Clamp01((n - threshold)/.18f);
            return t*t*(3 - 2*t);
        }

        // 0..1 under the crowns of the trees standing on the terrain, with a ragged rim
        static float[,] TreeShade(Terrain terrain, int res)
        {
            var shade = new float[res, res];
            var data = terrain.terrainData; Vector3 tp = terrain.transform.position, size = data.size;
            var done = new List<Vector2>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                string n = r.name + " " + (r.transform.parent ? r.transform.parent.name : "");
                if (!(n.Contains("Tree") || n.Contains("Pine") || n.Contains("Fir") || n.Contains("Willow") || n.Contains("Oak"))) continue;
                if (n.Contains("Stump") || n.Contains("Dead") || n.Contains("Root")) continue;
                var b = r.bounds;
                if (b.size.y < 3f || Mathf.Max(b.size.x, b.size.z) > 16f) continue;
                var c = new Vector2(b.center.x, b.center.z);
                if (done.Exists(q => (q - c).sqrMagnitude < 1f)) continue;
                done.Add(c);
                float rad = Mathf.Clamp(.5f*Mathf.Max(b.size.x, b.size.z), 1.5f, 6.5f);
                int x0 = Mathf.FloorToInt((c.x - rad*1.4f - tp.x)/size.x*res), x1 = Mathf.CeilToInt((c.x + rad*1.4f - tp.x)/size.x*res);
                int z0 = Mathf.FloorToInt((c.y - rad*1.4f - tp.z)/size.z*res), z1 = Mathf.CeilToInt((c.y + rad*1.4f - tp.z)/size.z*res);
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(res - 1, z1); z++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(res - 1, x1); x++)
                    {
                        float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                        float d = Vector2.Distance(c, new Vector2(wx, wz)) + (Mathf.PerlinNoise(wx*.45f + 17, wz*.45f + 5) - .5f)*rad*.5f;
                        float v = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rad*.55f, rad*1.05f, d));
                        if (v > shade[z, x]) shade[z, x] = v;
                    }
            }
            return shade;
        }

        static void Blur(float[,,] w, int l, int res, int r, int passes)
        {
            var cur = new float[res, res];
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) cur[z, x] = w[z, x, l];
            float n = 2*r + 1;
            for (int p = 0; p < passes; p++)
            {
                var tmp = new float[res, res]; var o = new float[res, res];
                for (int z = 0; z < res; z++)
                {
                    float s = 0; for (int x = -r; x <= r; x++) s += cur[z, Mathf.Clamp(x, 0, res - 1)];
                    for (int x = 0; x < res; x++) { tmp[z, x] = s/n; s += cur[z, Mathf.Min(x + r + 1, res - 1)] - cur[z, Mathf.Max(x - r, 0)]; }
                }
                for (int x = 0; x < res; x++)
                {
                    float s = 0; for (int z = -r; z <= r; z++) s += tmp[Mathf.Clamp(z, 0, res - 1), x];
                    for (int z = 0; z < res; z++) { o[z, x] = s/n; s += tmp[Mathf.Min(z + r + 1, res - 1), x] - tmp[Mathf.Max(z - r, 0), x]; }
                }
                cur = o;
            }
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) w[z, x, l] = cur[z, x];
        }

        static void Backup(float[,,] a, int res)
        {
            using (var wr = new System.IO.BinaryWriter(System.IO.File.Create("Temp/GroundDistributionV2_Before.bin")))
            {
                wr.Write(res); wr.Write(Count);
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < Count; l++) wr.Write(a[z, x, l]);
            }
        }
    }
}
