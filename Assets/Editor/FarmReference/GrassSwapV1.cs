using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Takes the stiff, pale BOKI grass models out of the farm (grass_5: the spiky
    // clump, grass_7: the flat star, grass_8: the flat fern; "Exterior Grass Patches", the "Meadow" grass and the hill
    // scenery), and fills only the gaps they leave: where nothing else grows within GapRadius of a removed tuft, the
    // farm's own ground-tinted grass (Chick_Grass_Green, Chick_Grass_Dry over earthy ground) goes in its place, now and
    // then a meadow flower instead. Paths, the tilled field and other bare earth stay bare. One undo step.
    // Two more menu items do the same for grass_2 and grass_4, then for grass_1, grass_3 and grass_6 (the rest of the
    // "Meadow" grass), and the last for the grass_5 painted into the terrain as detail grass (not selectable objects),
    // which leaves no BOKI grass on the farm.
    public static class GrassSwapV1
    {
        const string Menu = "Tools/Chick/Ground/";
        static readonly string[] RemoveMeshes = { "Grass_5", "Grass_7", "Grass_8" };
        const string GreenPrefab = "Assets/Art/Grass/Chick_Grass_Green.prefab", DryPrefab = "Assets/Art/Grass/Chick_Grass_Dry.prefab";
        static readonly string[] Flowers =
        {
            "Assets/Art/MeadowFlowers/Flower_Daisy.fbx", "Assets/Art/MeadowFlowers/Flower_Buttercup.fbx",
            "Assets/Art/MeadowFlowers/Flower_Clover.fbx", "Assets/Art/MeadowFlowers/Flower_Bellflower.fbx",
            "Assets/Art/MeadowFlowers/Flower_Poppy.fbx"
        };
        static readonly float[] FlowerScale = { .72f, .73f, .83f, .51f, .46f };   // as the farm's wildflowers have them
        static readonly int[] Earth = { 4, 5, 6, 13, 14, 15, 19 };
        const int Tilled = 8;
        const float GapRadius = 1.2f, FlowerShare = .25f, DryFrom = .45f, BareFrom = .7f;
        const int Seed = 5521;

        // the second round: grass_2 (the thin tuft) and grass_4 (the spiky tuft); the last round: the rest of the
        // "Meadow" / "Hill Meadow" grass, grass_1, grass_3 and grass_6
        static readonly string[] SecondRound = { "Grass_2", "Grass_4" };
        static readonly string[] ThirdRound = { "Grass_1", "Grass_3", "Grass_6" };

        // the terrain's own detail grass (painted, not objects): grass_5 in a dense band along the south and under the
        // farm fences. Its density before removal is kept in DetailBackup and guides the fill.
        const string DetailBackup = "Temp/GrassDetail_Before.bin";
        const float DetailSpacing = 1.7f;     // m between the new clumps (each about 1-1.5 m across)
        const float DetailFlowerShare = .08f;

        [MenuItem(Menu + "Swap BOKI Grass for Chick Grass (V1)")]
        static void MenuApply() => Debug.Log(Apply(RemoveMeshes));

        [MenuItem(Menu + "Swap BOKI Grass for Chick Grass (V1, grass 2 and 4)")]
        static void MenuApplySecond() => Debug.Log(Apply(SecondRound));

        [MenuItem(Menu + "Swap BOKI Grass for Chick Grass (V1, grass 1, 3 and 6)")]
        static void MenuApplyThird() => Debug.Log(Apply(ThirdRound));

        [MenuItem(Menu + "Swap BOKI Grass for Chick Grass (V1, terrain detail grass)")]
        static void MenuApplyDetail() => Debug.Log(ApplyDetail());

        // Clears every terrain detail layer whose prototype is a BOKI grass mesh and, where it grew, plants the farm's
        // ground-tinted grass at about DetailSpacing apart (and a few meadow flowers), keeping clear of what already
        // grows there and of bare earth. One undo step.
        public static string ApplyDetail()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            var green = AssetDatabase.LoadAssetAtPath<GameObject>(GreenPrefab) ?? throw new InvalidOperationException("Missing " + GreenPrefab);
            var dry = AssetDatabase.LoadAssetAtPath<GameObject>(DryPrefab) ?? throw new InvalidOperationException("Missing " + DryPrefab);
            var flowers = new GameObject[Flowers.Length];
            for (int i = 0; i < Flowers.Length; i++) flowers[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Flowers[i]) ?? throw new InvalidOperationException("Missing " + Flowers[i]);
            var grassParent = FindNamed("Farm IL3DN Grass") ?? throw new InvalidOperationException("Farm IL3DN Grass missing.");
            var flowerParent = FindNamed("Meadow", t => t.parent != null && t.parent.name == "Flowers") ?? throw new InvalidOperationException("Flowers/Meadow missing.");

            var layers = new List<int>();
            var protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                var mf = protos[i].prototype != null ? protos[i].prototype.GetComponentInChildren<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Grass_") &&
                    AssetDatabase.GetAssetPath(mf.sharedMesh).Contains("BOKI")) layers.Add(i);
            }
            int w = data.detailWidth, h = data.detailHeight;

            // where it grew: the saved density if an earlier run already cleared it, else the layers as they are now
            var density = LoadDetailBackup(w, h, layers.Count);
            if (density == null)
            {
                density = new float[h, w];
                foreach (int l in layers) { var d = data.GetDetailLayer(0, 0, w, h, l); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) density[y, x] += d[y, x]; }
                SaveDetailBackup(data, layers, w, h);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Swap terrain detail grass (V1)");
            Undo.RegisterCompleteObjectUndo(data, "Swap terrain detail grass (V1)");
            var zero = new int[h, w];
            foreach (int l in layers) data.SetDetailLayer(0, 0, l, zero);
            EditorUtility.SetDirty(data);

            // what already grows, on a coarse grid for quick spacing tests
            var occupied = new SpacingGrid(DetailSpacing);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!(r is MeshRenderer) || !r.enabled) continue;
                string n = r.name + (r.transform.parent ? r.transform.parent.name : "");
                if (n.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (r.bounds.size.y > 1.6f) continue;
                occupied.Add(r.bounds.center);
            }

            Vector3 tp = terrain.transform.position; int res = data.alphamapResolution;
            var rng = new System.Random(Seed + 1);
            int greenIn = 0, dryIn = 0, flowerIn = 0;
            float cellX = data.size.x/w, cellZ = data.size.z/h;
            // candidates: every cell the grass grew in, in random order; a cell is taken with a chance by its density
            var cells = new List<int>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (density[y, x] > 0) cells.Add(y*w + x);
            for (int i = cells.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = cells[i]; cells[i] = cells[j]; cells[j] = t; }
            foreach (int c in cells)
            {
                int y = c/w, x = c%w;
                Vector3 p = new Vector3(tp.x + (x + (float)rng.NextDouble())*cellX, 0, tp.z + (y + (float)rng.NextDouble())*cellZ);
                if (occupied.Near(p) || BeyondSight(p)) continue;
                var a = data.GetAlphamaps(Mathf.Min(res - 1, (int)((p.x - tp.x)/data.size.x*res)), Mathf.Min(res - 1, (int)((p.z - tp.z)/data.size.z*res)), 1, 1);
                float earth = 0; foreach (int l in Earth) earth += a[0, 0, l];
                if (earth > BareFrom || a[0, 0, Tilled] > .3f) continue;
                p.y = terrain.SampleHeight(p) + tp.y;
                GameObject placed;
                if (earth < DryFrom && rng.NextDouble() < DetailFlowerShare)
                {
                    int f = rng.Next(flowers.Length);
                    placed = (GameObject)PrefabUtility.InstantiatePrefab(flowers[f], flowerParent);
                    placed.transform.localScale = Vector3.one*FlowerScale[f]*Lerp(.85f, 1.15f, rng.NextDouble());
                    flowerIn++;
                }
                else
                {
                    bool isDry = earth >= DryFrom;
                    placed = (GameObject)PrefabUtility.InstantiatePrefab(isDry ? dry : green, grassParent);
                    placed.transform.localScale = Vector3.one*Lerp(.65f, 1.0f, rng.NextDouble());
                    if (isDry) dryIn++; else greenIn++;
                }
                placed.transform.SetPositionAndRotation(p, Quaternion.Euler(0, (float)rng.NextDouble()*360f, 0));
                Undo.RegisterCreatedObjectUndo(placed, "Swap terrain detail grass (V1)");
                occupied.Add(p);
            }

            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssetIfDirty(data);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Terrain detail grass: layer(s) " + string.Join(", ", layers) + " cleared (" + cells.Count + " cells had it; before: " + DetailBackup + "); planted " +
                   greenIn + " Chick_Grass_Green, " + dryIn + " Chick_Grass_Dry and " + flowerIn + " meadow flowers about " + DetailSpacing + " m apart.";
        }

        // the far rim of the map: more than SightReach from the fenced farm the player cannot leave (the grass is
        // culled at about 55 m anyway), so nothing is planted there
        static readonly Rect PlayArea = Rect.MinMaxRect(59f, -32f, 129f, 36f);
        const float SightReach = 45f;
        static bool BeyondSight(Vector3 p)
        {
            float dx = Mathf.Max(0, Mathf.Max(PlayArea.xMin - p.x, p.x - PlayArea.xMax));
            float dz = Mathf.Max(0, Mathf.Max(PlayArea.yMin - p.z, p.z - PlayArea.yMax));
            return dx*dx + dz*dz > SightReach*SightReach;
        }

        static float[,] LoadDetailBackup(int w, int h, int layerCount)
        {
            if (!System.IO.File.Exists(DetailBackup)) return null;
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(DetailBackup)))
            {
                if (r.ReadInt32() != w || r.ReadInt32() != h) return null;
                var d = new float[h, w];
                for (int l = 0; l < layerCount; l++)
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) d[y, x] += r.ReadInt32();
                return d;
            }
        }

        static void SaveDetailBackup(TerrainData data, List<int> layers, int w, int h)
        {
            using (var wr = new System.IO.BinaryWriter(System.IO.File.Create(DetailBackup)))
            {
                wr.Write(w); wr.Write(h);
                foreach (int l in layers) { var d = data.GetDetailLayer(0, 0, w, h, l); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) wr.Write(d[y, x]); }
            }
        }

        // points bucketed on a grid of the spacing, so "is anything within the spacing" looks at nine buckets only
        sealed class SpacingGrid
        {
            readonly float size;
            readonly Dictionary<long, List<Vector2>> cells = new Dictionary<long, List<Vector2>>();
            public SpacingGrid(float spacing) { size = spacing; }
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(Vector3 p)
            {
                int x = Mathf.FloorToInt(p.x/size), z = Mathf.FloorToInt(p.z/size);
                if (!cells.TryGetValue(Key(x, z), out var list)) cells[Key(x, z)] = list = new List<Vector2>();
                list.Add(new Vector2(p.x, p.z));
            }
            public bool Near(Vector3 p)
            {
                int cx = Mathf.FloorToInt(p.x/size), cz = Mathf.FloorToInt(p.z/size);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (cells.TryGetValue(Key(cx + dx, cz + dz), out var list))
                            foreach (var q in list) if ((q - new Vector2(p.x, p.z)).sqrMagnitude < size*size) return true;
                return false;
            }
        }

        public static string Apply(string[] removeMeshes)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var green = AssetDatabase.LoadAssetAtPath<GameObject>(GreenPrefab) ?? throw new InvalidOperationException("Missing " + GreenPrefab);
            var dry = AssetDatabase.LoadAssetAtPath<GameObject>(DryPrefab) ?? throw new InvalidOperationException("Missing " + DryPrefab);
            var flowers = new GameObject[Flowers.Length];
            for (int i = 0; i < Flowers.Length; i++) flowers[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Flowers[i]) ?? throw new InvalidOperationException("Missing " + Flowers[i]);
            var grassParent = FindNamed("Farm IL3DN Grass") ?? throw new InvalidOperationException("Farm IL3DN Grass missing.");
            var flowerParent = FindNamed("Meadow", t => t.parent != null && t.parent.name == "Flowers") ?? throw new InvalidOperationException("Flowers/Meadow missing.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Swap BOKI grass (V1)");

            // 1. the BOKI tufts (the outermost object of each, so no empty holders stay behind)
            var removed = new List<Vector3>();
            var holders = new HashSet<Transform>();
            var doomed = new HashSet<GameObject>();
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || Array.IndexOf(removeMeshes, filter.sharedMesh.name) < 0) continue;
                var top = PrefabUtility.GetOutermostPrefabInstanceRoot(filter.gameObject) ?? filter.gameObject;
                if (!doomed.Add(top)) continue;
                removed.Add(top.transform.position);
                if (top.transform.parent != null) holders.Add(top.transform.parent);
            }
            foreach (var go in doomed) Undo.DestroyObjectImmediate(go);
            int emptied = 0;
            foreach (var h in holders)
                if (h != null && h.childCount == 0 && h.GetComponents<Component>().Length == 1) { Undo.DestroyObjectImmediate(h.gameObject); emptied++; }

            // 2. what still grows: every renderer that is low vegetation (grass, flowers, small plants)
            var growing = new List<Vector3>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!(r is MeshRenderer) || !r.enabled) continue;
                var size = r.bounds.size;
                if (size.y > 1.6f || size.x > 2.5f || size.z > 2.5f) continue;
                string n = r.name + (r.transform.parent ? r.transform.parent.name : "");
                if (n.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Meadow", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Fern", StringComparison.OrdinalIgnoreCase) < 0) continue;
                growing.Add(r.bounds.center);
            }

            // 3. fill the gaps
            var data = terrain.terrainData; Vector3 tp = terrain.transform.position; int res = data.alphamapResolution;
            var rng = new System.Random(Seed);
            int grassIn = 0, dryIn = 0, flowerIn = 0, bare = 0, kept = 0;
            foreach (var p in removed)
            {
                if (Near(growing, p, GapRadius)) { kept++; continue; }
                float u = (p.x - tp.x)/data.size.x, v = (p.z - tp.z)/data.size.z;
                if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                var a = data.GetAlphamaps(Mathf.Min(res - 1, (int)(u*res)), Mathf.Min(res - 1, (int)(v*res)), 1, 1);
                float earth = 0; foreach (int l in Earth) earth += a[0, 0, l];
                if (earth > BareFrom || a[0, 0, Tilled] > .3f) { bare++; continue; }
                Vector3 at = new Vector3(p.x, terrain.SampleHeight(p) + tp.y, p.z);
                GameObject placed;
                if (earth < DryFrom && rng.NextDouble() < FlowerShare)
                {
                    int f = rng.Next(flowers.Length);
                    placed = (GameObject)PrefabUtility.InstantiatePrefab(flowers[f], flowerParent);
                    placed.transform.localScale = Vector3.one*FlowerScale[f]*Lerp(.85f, 1.15f, rng.NextDouble());
                    flowerIn++;
                }
                else
                {
                    bool isDry = earth >= DryFrom;
                    placed = (GameObject)PrefabUtility.InstantiatePrefab(isDry ? dry : green, grassParent);
                    placed.transform.localScale = Vector3.one*Lerp(.65f, 1.0f, rng.NextDouble());
                    if (isDry) dryIn++; else grassIn++;
                }
                placed.transform.SetPositionAndRotation(at, Quaternion.Euler(0, (float)rng.NextDouble()*360f, 0));
                Undo.RegisterCreatedObjectUndo(placed, "Swap BOKI grass (V1)");
                growing.Add(at);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "BOKI grass swap (" + string.Join(", ", removeMeshes) + "): " + removed.Count + " tufts removed (" + emptied + " empty groups too); of their spots " + kept + " still have plants around them, " +
                   bare + " are bare earth, and the gaps got " + grassIn + " Chick_Grass_Green, " + dryIn + " Chick_Grass_Dry and " + flowerIn + " meadow flowers.";
        }

        static bool Near(List<Vector3> points, Vector3 p, float r)
        {
            float r2 = r*r;
            foreach (var q in points) { float dx = q.x - p.x, dz = q.z - p.z; if (dx*dx + dz*dz < r2) return true; }
            return false;
        }

        static Transform FindNamed(string name, Func<Transform, bool> also = null)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name && (also == null || also(t))) return t;
            return null;
        }

        static float Lerp(float a, float b, double t) => a + (b - a)*(float)t;
    }
}
