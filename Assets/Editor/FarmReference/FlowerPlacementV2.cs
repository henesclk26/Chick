using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Places the eleven bendable flower kinds of FlowerScatterV1 where a farm would
    // have them, in small groups, instead of an even spread:
    //  - garden: roses, hyacinths and daisies in little beds on the grass along the barn, the coop, the water tower, the windmill
    //    and the pasture shelter;
    //  - fences: meadow flowers in twos and threes at the foot of the fences (not at the gates);
    //  - trees, stumps and rocks: the leafy plant and small flowers around their feet;
    //  - path edges: dandelions and daisies on the grass just beside the earth;
    //  - pond: a few on the grassy bank, a step back from the water;
    //  - meadow: a few sparse groups in the open grass.
    // Only inside the fenced farm, never on the tilled field, the water or anything solid; a little apart from the
    // flowers already there. The kinds are scaled to the farm's own flower heights (see FlowerScatterV1.Kinds).
    // One undo step; "Remove Farm Flowers (V2)" deletes the group again.
    public static class FlowerPlacementV2
    {
        const string Menu = "Tools/Chick/Ground/";
        const string GroupName = "Farm Flowers (V2)";
        const int Seed = 4471;
        static readonly Rect Farm = Rect.MinMaxRect(59.6f, -31.4f, 128.4f, 35.4f);
        static readonly int[] Earth = { 4, 5, 6, 13, 14, 15, 19 };
        const int Tilled = 8;
        static float waterY = float.NegativeInfinity;   // the pond surface and shoreline, read in Apply
        static Vector2[] pondLine;
        // footprints nothing grows in: the buildings (an empty barn floor has no collider to stop it), vehicles, bridge
        static readonly List<Bounds> keepOut = new List<Bounds>();
        // FlowerScatterV1.Kinds
        const int Foliage = 0, BluBlu1 = 1, BluBlu2 = 2, Daisy = 3, Dandelion = 4, Pine1 = 5, Pine2 = 6, Pine3 = 7, Hyacinth = 8, RoseRed = 9, RoseBlack = 10;

        sealed class Context
        {
            public string name; public int budget; public int[] kinds; public int minGroup, maxGroup; public float groupGap; public bool earthOk;
            public Func<System.Random, Vector3?> spot;   // a group's centre, or null
            public Func<System.Random, Vector3, Vector3> step;   // the next member around the previous one
        }

        [MenuItem(Menu + "Place Farm Flowers (V2)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Remove Farm Flowers (V2)")]
        static void MenuRemove()
        {
            var group = GameObject.Find(GroupName);
            if (group != null) Undo.DestroyObjectImmediate(group);
            Debug.Log(group != null ? "Farm flowers removed." : "No farm flowers.");
        }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            if (GameObject.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists; remove it first.");

            var kinds = FlowerScatterV1.Kinds;
            var prefabs = new GameObject[kinds.Length]; var heights = new float[kinds.Length];
            var converted = new Dictionary<Material, Material>();
            for (int k = 0; k < kinds.Length; k++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(kinds[k].source) ?? throw new InvalidOperationException("Missing " + kinds[k].source);
                prefabs[k] = AssetDatabase.LoadAssetAtPath<GameObject>(FlowerScatterV1.PrefabFolder + "/Chick_" + source.name + ".prefab") ?? FlowerScatterV1.Variant(source, converted);
                heights[k] = FlowerScatterV1.ModelHeight(prefabs[k]);
            }

            // the anchors
            var buildings = new List<Bounds>(); var fences = new List<Bounds>(); var trunks = new List<Vector3>(); var rocks = new List<Bounds>();
            var others = new List<Vector3>();
            keepOut.Clear();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (!Farm.Contains(new Vector2(b.center.x, b.center.z))) continue;
                string n = r.name, parent = r.transform.parent ? r.transform.parent.name : "";
                if (n == "SM_Bld_Barn_01" || n == "SM_Bld_WaterTower_01" || n == "SM_Prop_Windmill_01" || n == "Shelter" || n == "Object_4") { buildings.Add(b); keepOut.Add(b); }
                else if (n.StartsWith("SM_Veh_") || n == "SM_Bridge" || n.StartsWith("SM_Prop_ClothesLine") || n == "Object_5") keepOut.Add(b);
                else if (parent == "Fence Bays" && !n.StartsWith("Gate") && !n.Contains("Gate")) fences.Add(b);
                else if ((n.Contains("Tree") && !n.Contains("LOD1") && !n.Contains("LOD2") && b.size.y > 3f) || n.Contains("Stump")) trunks.Add(new Vector3(b.center.x, 0, b.center.z));
                else if ((parent.Contains("Rock") || n.Contains("Rock")) && b.size.x > .7f && b.size.x < 5f) rocks.Add(b);
                string o = n + parent;
                if (o.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("Mushroom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    o.IndexOf("Fern", StringComparison.OrdinalIgnoreCase) >= 0) others.Add(b.center);
            }
            var surface = GameObject.Find("Water Surface");
            waterY = surface != null ? surface.transform.position.y : float.NegativeInfinity;
            pondLine = null;
            Vector2[] shore = null;
            var pond = GameObject.Find("Farm Pond");
            var keeper = pond != null ? pond.GetComponent("FarmPond") : null;
            if (keeper != null)
            {
                var line = new SerializedObject(keeper).FindProperty("shoreline");
                shore = new Vector2[line.arraySize];
                for (int i = 0; i < shore.Length; i++) shore[i] = line.GetArrayElementAtIndex(i).vector2Value;
                pondLine = shore;
            }

            var data = terrain.terrainData; Vector3 tp = terrain.transform.position; int res = data.alphamapResolution;
            float EarthAt(Vector3 p)
            {
                var a = data.GetAlphamaps(Mathf.Clamp((int)((p.x - tp.x)/data.size.x*res), 0, res - 1), Mathf.Clamp((int)((p.z - tp.z)/data.size.z*res), 0, res - 1), 1, 1);
                float e = 0; foreach (int l in Earth) e += a[0, 0, l];
                return a[0, 0, Tilled] > .05f ? 9f : e;
            }

            Vector3 Around(System.Random rng, Vector3 c, float r0, float r1)
            {
                float a = (float)(rng.NextDouble()*Math.PI*2), r = FlowerScatterV1.Lerp(r0, r1, rng.NextDouble());
                return c + new Vector3(Mathf.Cos(a)*r, 0, Mathf.Sin(a)*r);
            }
            Vector3 Pick<T>(System.Random rng, List<T> list, Func<T, Vector3> f) => f(list[rng.Next(list.Count)]);

            var contexts = new List<Context>
            {
                new Context { name = "garden", budget = 34, kinds = new[] { RoseRed, RoseRed, RoseBlack, Hyacinth, Hyacinth, Daisy }, minGroup = 3, maxGroup = 5, groupGap = 2.6f,
                    spot = rng => buildings.Count == 0 ? (Vector3?)null : Pick(rng, buildings, b => EdgePoint(rng, b, FlowerScatterV1.Lerp(.7f, 1.2f, rng.NextDouble()))),
                    step = (rng, p) => Around(rng, p, .35f, .55f) },
                new Context { name = "fences", budget = 46, kinds = new[] { Daisy, Dandelion, Pine1, Pine2, BluBlu1, BluBlu2 }, minGroup = 2, maxGroup = 3, groupGap = 3.2f,
                    spot = rng => fences.Count == 0 ? (Vector3?)null : Pick(rng, fences, b => FenceFoot(rng, b)),
                    step = (rng, p) => Around(rng, p, .35f, .6f) },
                new Context { name = "trees and rocks", budget = 34, kinds = new[] { Foliage, Foliage, Pine3, BluBlu2, BluBlu1 }, minGroup = 1, maxGroup = 3, groupGap = 2.6f,
                    spot = rng => trunks.Count + rocks.Count == 0 ? (Vector3?)null :
                        rng.Next(trunks.Count + rocks.Count) < trunks.Count ? Around(rng, trunks[rng.Next(trunks.Count)], .9f, 1.6f)
                                                                            : Pick(rng, rocks, b => EdgePoint(rng, b, FlowerScatterV1.Lerp(.25f, .5f, rng.NextDouble()))),
                    step = (rng, p) => Around(rng, p, .35f, .55f) },
                new Context { name = "path edges", budget = 26, kinds = new[] { Dandelion, Dandelion, Daisy, Pine1 }, minGroup = 1, maxGroup = 2, groupGap = 3.5f,
                    spot = rng => PathEdge(rng, EarthAt),
                    step = (rng, p) => Around(rng, p, .3f, .5f) },
                new Context { name = "pond", budget = 14, kinds = new[] { Foliage, Pine2, Pine3, Dandelion }, minGroup = 1, maxGroup = 3, groupGap = 2.8f,
                    spot = rng => shore == null ? (Vector3?)null : PondBank(rng, shore),
                    step = (rng, p) => Around(rng, p, .35f, .55f) },
                new Context { name = "meadow", budget = 22, kinds = new[] { Daisy, Dandelion, BluBlu1, BluBlu2, Pine1 }, minGroup = 2, maxGroup = 4, groupGap = 9f,
                    spot = rng => new Vector3(FlowerScatterV1.Lerp(Farm.xMin, Farm.xMax, rng.NextDouble()), 0, FlowerScatterV1.Lerp(Farm.yMin, Farm.yMax, rng.NextDouble())),
                    step = (rng, p) => Around(rng, p, .4f, .7f) },
            };

            var parentOf = GameObject.Find("Flowers")?.transform;
            var group = new GameObject(GroupName).transform;
            if (parentOf != null) group.SetParent(parentOf, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Place farm flowers (V2)");

            var rng0 = new System.Random(Seed);
            var placed = new List<Vector3>(); var centres = new List<Vector3>();
            var report = new System.Text.StringBuilder("Farm flowers (V2): ");
            int total = 0;
            foreach (var ctx in contexts)
            {
                var ownCentres = new List<Vector3>();
                int n = 0, tries = 0;
                while (n < ctx.budget && tries++ < ctx.budget*300)
                {
                    var s = ctx.spot(rng0);
                    if (s == null) break;
                    var c = s.Value;
                    if (!Farm.Contains(new Vector2(c.x, c.z)) || FlowerScatterV1.Near(ownCentres, c, ctx.groupGap) || FlowerScatterV1.Near(centres, c, 2.2f)) continue;
                    int size = rng0.Next(ctx.minGroup, ctx.maxGroup + 1), kind = ctx.kinds[rng0.Next(ctx.kinds.Length)];
                    var p = c; int made = 0;
                    for (int m = 0; m < size * 4 && made < size && n < ctx.budget; m++)
                    {
                        if (m > 0) p = ctx.step(rng0, made > 0 ? p : c);
                        if (!Valid(p, ctx.earthOk, EarthAt, placed, others, terrain, out var at)) { p = c; continue; }
                        // a group is mostly one kind, now and then a second one mixed in
                        int k = made > 0 && rng0.NextDouble() < .25 ? ctx.kinds[rng0.Next(ctx.kinds.Length)] : kind;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[k], group);
                        go.transform.localScale = Vector3.one*(kinds[k].height/Mathf.Max(heights[k], .01f)*FlowerScatterV1.Lerp(.85f, 1.15f, rng0.NextDouble()));
                        go.transform.SetPositionAndRotation(at - Vector3.up*.01f, Quaternion.Euler(0, (float)rng0.NextDouble()*360f, 0));
                        Undo.RegisterCreatedObjectUndo(go, "Place farm flowers (V2)");
                        placed.Add(at); made++; n++;
                    }
                    if (made > 0) { ownCentres.Add(c); centres.Add(c); }
                }
                report.Append(ctx.name + " " + n + ", ");
                total += n;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            return report.Append("total " + total + " under " + GroupName + ".").ToString();
        }

        static bool Valid(Vector3 p, bool earthOk, Func<Vector3, float> earthAt, List<Vector3> placed, List<Vector3> others, Terrain terrain, out Vector3 at)
        {
            at = p;
            if (!Farm.Contains(new Vector2(p.x, p.z))) return false;
            foreach (var b in keepOut)
                if (p.x > b.min.x - .3f && p.x < b.max.x + .3f && p.z > b.min.z - .3f && p.z < b.max.z + .3f) return false;
            if (FlowerScatterV1.Near(placed, p, .3f) || FlowerScatterV1.Near(others, p, .45f)) return false;
            float e = earthAt(p);
            if (e > 8f || (!earthOk && e > .3f) || e > .95f) return false;   // never the tilled field; garden beds may sit on worn earth
            at = new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
            if (pondLine != null && FarmPondV1.Signed(pondLine, p.x, p.z) < .8f && at.y < waterY + .05f) return false;   // in or at the pond
            return !FlowerScatterV1.Solid(at, .22f);
        }

        // a point outside a footprint, `gap` metres from one of its sides
        static Vector3 EdgePoint(System.Random rng, Bounds b, float gap)
        {
            int side = rng.Next(4); float t = (float)rng.NextDouble();
            float x = side < 2 ? Mathf.Lerp(b.min.x, b.max.x, t) : (side == 2 ? b.min.x - gap : b.max.x + gap);
            float z = side >= 2 ? Mathf.Lerp(b.min.z, b.max.z, t) : (side == 0 ? b.min.z - gap : b.max.z + gap);
            return new Vector3(x, 0, z);
        }

        // at the foot of a fence bay, on one side, somewhere along it
        static Vector3 FenceFoot(System.Random rng, Bounds b)
        {
            bool alongX = b.size.x >= b.size.z;
            float t = (float)rng.NextDouble(), off = FlowerScatterV1.Lerp(.45f, .8f, rng.NextDouble())*(rng.NextDouble() < .5 ? -1 : 1);
            return alongX ? new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), 0, b.center.z + off) : new Vector3(b.center.x + off, 0, Mathf.Lerp(b.min.z, b.max.z, t));
        }

        // grass with earth within a metre on at least one side
        static Vector3? PathEdge(System.Random rng, Func<Vector3, float> earthAt)
        {
            for (int i = 0; i < 60; i++)
            {
                var p = new Vector3(FlowerScatterV1.Lerp(Farm.xMin, Farm.xMax, rng.NextDouble()), 0, FlowerScatterV1.Lerp(Farm.yMin, Farm.yMax, rng.NextDouble()));
                if (earthAt(p) > .3f) continue;
                for (int d = 0; d < 8; d++)
                {
                    float a = d*Mathf.PI/4f;
                    float e = earthAt(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))*1.3f);
                    if (e > .45f && e < 8f) return p;
                }
            }
            return null;
        }

        // the grassy bank a step back from the water
        static Vector3? PondBank(System.Random rng, Vector2[] shore)
        {
            for (int i = 0; i < 30; i++)
            {
                var a = shore[rng.Next(shore.Length)];
                var c = Vector2.zero; foreach (var s in shore) c += s; c /= shore.Length;
                var outward = (a - c).normalized;
                var p = a + outward*FlowerScatterV1.Lerp(2f, 4f, rng.NextDouble());
                if (FarmPondV1.Signed(shore, p.x, p.y) > 1f) return new Vector3(p.x, 0, p.y);
            }
            return null;
        }
    }
}
