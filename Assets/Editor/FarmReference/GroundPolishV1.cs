using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Two touches to the farm terrain's layer weights, with the existing layers only:
    //  - the small round earth spots scattered over the meadows and hills (seen from afar they read as polka dots): every
    //    earth patch smaller than SpotArea that is not part of the paths, the field or the pond fades into the grass
    //    around it, leaving a faint worn trace (the tiniest go entirely), over a feathered rim;
    //  - the trodden earth in front of the barn (one flat orange sheet) gets soft darker old-earth drifts and a few
    //    pebble scatters, and grass-in-earth creeps in where the earth thins out at its edges.
    // The terrain layers and their textures are not changed. One undo step; the weights before the first run are written
    // to Temp/GroundPolishV1_Before.bin, re-runs start from them, and "Restore Ground Polish (V1)" puts them back.
    public static class GroundPolishV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string BackupPath = "Temp/GroundPolishV1_Before.bin";
        const int Rich = 1, Trodden = 4, OldEarth = 5, Pebbles = 6, Tilled = 8, Lush = 9, EarthV = 13, Count = 20;
        static readonly int[] Earth = { 4, 5, 6, 13, 14, 15, 19 };   // earth-type layers, the tilled field aside
        const float SpotArea = 140f, TinyArea = 18f;               // m²
        const float SpotKeep = .25f;                               // share of a spot's earth left as a worn trace
        static readonly Vector3 Yard = new Vector3(80f, 0f, 16f);  // the trodden earth between the barn and the field
        const float YardRadius = 16f;

        [MenuItem(Menu + "Ground Polish (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Restore Ground Polish (V1)")]
        static void MenuRestore() => Debug.Log(Restore());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var terrain = Terrain();
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            var a = LoadBackup(res) ?? data.GetAlphamaps(0, 0, res, res);
            if (!System.IO.File.Exists(BackupPath)) Backup(a, res);
            a = (float[,,])a.Clone();
            Vector3 tp = terrain.transform.position, size = data.size;
            float texelArea = size.x/res*size.z/res;

            // ---- earth spots ----
            var isEarth = new bool[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float e = 0; foreach (int l in Earth) e += a[z, x, l];
                    isEarth[z, x] = e > .3f;
                }
            var spot = new float[res, res, 1];
            var label = new int[res, res];
            int spots = 0, tiny = 0, next = 0;
            var queue = new Queue<int>(); var members = new List<int>();
            for (int z0 = 0; z0 < res; z0++)
                for (int x0 = 0; x0 < res; x0++)
                {
                    if (!isEarth[z0, x0] || label[z0, x0] != 0) continue;
                    next++; members.Clear(); queue.Enqueue(z0*res + x0); label[z0, x0] = next;
                    bool touchesField = false;
                    while (queue.Count > 0)
                    {
                        int i = queue.Dequeue(); members.Add(i);
                        int z = i/res, x = i%res;
                        if (a[z, x, Tilled] > .2f) touchesField = true;
                        for (int k = 0; k < 4; k++)
                        {
                            int nz = z + (k == 0 ? 1 : k == 1 ? -1 : 0), nx = x + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nz < 0 || nx < 0 || nz >= res || nx >= res || !isEarth[nz, nx] || label[nz, nx] != 0) continue;
                            label[nz, nx] = next; queue.Enqueue(nz*res + nx);
                        }
                    }
                    float area = members.Count*texelArea;
                    if (area >= SpotArea || touchesField) continue;
                    float fade = area < TinyArea ? 1f : 1f - SpotKeep;
                    if (area < TinyArea) tiny++; else spots++;
                    foreach (int i in members) spot[i/res, i%res, 0] = fade;
                }
            // feather: the fade reaches a little past the spot's outline, so no ring is left where it was cut
            Dilate(spot, res, 3);
            Blur(spot, 0, res, 3, 2);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float f = Mathf.Clamp01(spot[z, x, 0]);
                    if (f <= 0f) continue;
                    float moved = 0;
                    foreach (int l in Earth) { float t = a[z, x, l]*f; a[z, x, l] -= t; moved += t; }
                    a[z, x, Rich] += moved*.7f; a[z, x, Lush] += moved*.3f;
                }

            // ---- barn yard ----
            int yardTexels = 0;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = tp.x + (x + .5f)/res*size.x, wz = tp.z + (z + .5f)/res*size.z;
                    float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(Yard.x, Yard.z));
                    float region = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(YardRadius*.6f, YardRadius, d));
                    if (region <= 0f || a[z, x, Trodden] < .05f) continue;
                    yardTexels++;
                    float trodden = a[z, x, Trodden];
                    // darker old earth in soft drifts
                    float drift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.42f, .66f, Mathf.PerlinNoise(wx/4.2f + 11.3f, wz/4.2f + 4.7f)));
                    float toOld = trodden*.75f*drift*region;
                    // small pebble scatters
                    float peb = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.56f, .74f, Mathf.PerlinNoise(wx/1.9f + 71f, wz/1.9f + 29f)));
                    float toPeb = (trodden - toOld)*.6f*peb*region;
                    // grass in the earth where it thins out towards the meadow
                    float e = 0; foreach (int l in Earth) e += a[z, x, l];
                    float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.85f, .45f, e));
                    float tuft = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, .65f, Mathf.PerlinNoise(wx/2.6f + 5f, wz/2.6f + 93f)));
                    float toGrass = (trodden - toOld - toPeb)*.5f*edge*tuft*region;
                    a[z, x, Trodden] -= toOld + toPeb + toGrass;
                    a[z, x, OldEarth] += toOld; a[z, x, Pebbles] += toPeb; a[z, x, EarthV] += toGrass;
                }

            Normalize(a, res);
            Undo.RegisterCompleteObjectUndo(data, "Ground polish (V1)");
            data.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return "Ground polish: " + spots + " earth spots faded to a worn trace, " + tiny + " tiny ones removed; barn yard (" + yardTexels +
                   " texels) given old-earth drifts, pebble scatters and grass at its edges. Before: " + BackupPath + ".";
        }

        public static string Restore()
        {
            var terrain = Terrain();
            var data = terrain.terrainData;
            var a = LoadBackup(data.alphamapResolution) ?? throw new InvalidOperationException("No backup at " + BackupPath + ".");
            Undo.RegisterCompleteObjectUndo(data, "Restore ground polish (V1)");
            data.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return "Ground weights restored from " + BackupPath + ".";
        }

        static Terrain Terrain()
        {
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            if (terrain.terrainData.alphamapLayers != Count) throw new InvalidOperationException("Expected " + Count + " ground layers.");
            return terrain;
        }

        static void Normalize(float[,,] a, int res)
        {
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float s = 0; for (int l = 0; l < Count; l++) { a[z, x, l] = Mathf.Max(0, a[z, x, l]); s += a[z, x, l]; }
                    if (s <= 1e-5f) { a[z, x, Rich] = 1; continue; }
                    for (int l = 0; l < Count; l++) a[z, x, l] /= s;
                }
        }

        // grows the non-zero values outwards by r texels (max of the neighbourhood)
        static void Dilate(float[,,] w, int res, int r)
        {
            var o = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float m = 0;
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int zz = z + dz, xx = x + dx;
                            if (zz < 0 || xx < 0 || zz >= res || xx >= res) continue;
                            m = Mathf.Max(m, w[zz, xx, 0]);
                        }
                    o[z, x] = m;
                }
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) w[z, x, 0] = o[z, x];
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

        static float[,,] LoadBackup(int res)
        {
            if (!System.IO.File.Exists(BackupPath)) return null;
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(BackupPath)))
            {
                if (r.ReadInt32() != res || r.ReadInt32() != Count) return null;
                var a = new float[res, res, Count];
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < Count; l++) a[z, x, l] = r.ReadSingle();
                return a;
            }
        }

        static void Backup(float[,,] a, int res)
        {
            using (var wr = new System.IO.BinaryWriter(System.IO.File.Create(BackupPath)))
            {
                wr.Write(res); wr.Write(Count);
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) for (int l = 0; l < Count; l++) wr.Write(a[z, x, l]);
            }
        }
    }
}
