using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Places the hand-painted flowers, mushrooms and leafy plant (the pieces of
    // nature_asset_pack_handpainted.glb and stylized_mushroom.glb) where they would grow, in small groups:
    //  - trees: mushroom clumps and the leafy plant at the foot of the trees, in the farm and at the woods just past
    //    the fence (seen through it);
    //  - stumps and rocks: mushrooms and the leafy plant beside them;
    //  - fences: hyacinths, buttercups and the leafy plant at the foot of the fence bays (not at the gates);
    //  - meadow: hyacinth and buttercup patches in the open grass;
    //  - path edges: buttercups on the grass just beside the earth.
    // Never on the tilled field, the water, a building's footprint or anything solid; a little apart from the flowers
    // and mushrooms already there. Each piece gets a prefab (Assets/Art/Flowers/Handpainted/Prefabs) with its pivot at
    // its foot, sized to the farm's own flowers and mushrooms, and an LOD group that only drops it once it is a few
    // pixels tall. Flowers and the plant use Chick/InteractiveFoliage (they bend under the bird like the grass and
    // the other flowers); the mushrooms stay rigid (URP Lit) and come in several colours (Colours: recoloured texture
    // copies under Textures), one colour to a clump. The source models are not touched.
    // One undo step; "Remove Handpainted Flora (V1)" deletes both groups again.
    public static class HandpaintedFloraV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Flowers/Handpainted", PrefabFolder = Folder + "/Prefabs";
        const string PackPath = "Assets/Assets/nature_asset_pack_handpainted.glb", MushroomPath = "Assets/Assets/stylized_mushroom.glb";
        const string PaddosSplitPath = Folder + "/Models/paddos_split.glb";
        const string FlowerGroup = "Handpainted Flowers (V1)", MushroomGroup = "Handpainted Mushrooms (V1)";
        const int Seed = 6203;
        // on screen below this share of the view's height the piece is no longer drawn (with the LOD bias of 2: about
        // 30 m for a mushroom, 45 m for a hyacinth)
        const float CullBelow = .02f;
        static readonly Rect Farm = Rect.MinMaxRect(59.6f, -31.4f, 128.4f, 35.4f);   // inside the outer fence
        static readonly Rect Woods = Rect.MinMaxRect(49.6f, -41.4f, 138.4f, 45.4f);  // and the trees just past it
        static readonly int[] Earth = { 4, 5, 6, 13, 14, 15, 19 };
        const int Tilled = 8;

        sealed class Kind
        {
            public string name, asset, mesh; public float height; public bool mushroom;
            // split: a model holding the mesh's mushrooms one by one (same layout)
            public string split;
            public Kind(string n, string a, string m, float h, bool mush, string apart = null)
            { name = n; asset = a; mesh = m; height = h; mushroom = mush; split = apart; }
        }

        // height on the farm (m): the farm's flowers stand .4-.7 m, its mushrooms .2-.45 m
        static readonly Kind[] Kinds =
        {
            new Kind("Hyacinth", PackPath, "polySurface78_b2_0", .5f, false),
            new Kind("HyacinthBent", PackPath, "polySurface78_pasted__b2_0", .42f, false),
            new Kind("Buttercup", PackPath, "polySurface20_b_loem_0", .4f, false),
            new Kind("ButtercupSmall", PackPath, "polySurface20_pasted__pasted__b_loem_0", .28f, false),
            new Kind("LeafyPlant", PackPath, "pasted__pasted__pasted__pCube5_Plant2_0", .3f, false),
            // the two joined brown mushrooms come apart (split in Blender), so each is its own food
            new Kind("BrownMushrooms", PackPath, "pSphere3_paddos_0", .42f, true, PaddosSplitPath),
            new Kind("PurpleMushroom", PackPath, "group1_pasted__group_pasted__pasted__pCube2_pasted__pasted__polySurface1_lambert2_0", .28f, true),
            new Kind("Toadstool", MushroomPath, "Mushroom_Material_0", .3f, true),
        };
        const int Hyacinth = 0, HyacinthBent = 1, Buttercup = 2, ButtercupSmall = 3, LeafyPlant = 4, Brown = 5, Purple = 6, Toadstool = 7;

        sealed class Colour
        {
            public int kind; public string name; public float hue, saturation, value;
            public Colour(int k, string n, float h, float s, float v) { kind = k; name = n; hue = h; saturation = s; value = v; }
        }

        // the mushrooms' other colours: hue turned (degrees), saturation and brightness scaled, on the coloured parts
        // of the texture (cap, gills), the pale stems and spots kept; each clump is one colour, the model's own as often
        static readonly Colour[] Colours =
        {
            new Colour(Toadstool, "Orange", 33f, 1.05f, 1.32f),
            new Colour(Toadstool, "Golden", 48f, .95f, 1.45f),
            new Colour(Toadstool, "Chestnut", 22f, .65f, .75f),
            new Colour(Toadstool, "Violet", -62f, .85f, 1.05f),
            new Colour(Purple, "Blue", -75f, 1f, 1f),
            new Colour(Purple, "Teal", -130f, .9f, .95f),
            new Colour(Purple, "Coral", 50f, 1.1f, 1.08f),
            new Colour(Purple, "Amber", 80f, 1.7f, 1.18f),
            new Colour(Brown, "Ochre", 18f, 1.5f, 1.2f),
            new Colour(Brown, "DarkChestnut", 0f, 1.15f, .68f),
            new Colour(Brown, "Ghost", 0f, .3f, 1.25f),
            new Colour(Brown, "Rusty", -10f, 1.7f, .95f),
        };
        const int ColourTextureSize = 1024;

        sealed class Context
        {
            public string name; public int budget; public int[] kinds; public int minGroup, maxGroup; public float groupGap, clear; public bool earthOk; public Rect area;
            public Func<System.Random, Vector3?> spot;   // a group's centre, or null
            public Func<System.Random, Vector3, Vector3> step;   // the next member around the previous one
        }

        [MenuItem(Menu + "Place Handpainted Flora (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Rebuild Handpainted Flora Prefabs (V1)")]
        static void MenuRebuild() => Debug.Log(RebuildPrefabs());

        /// <summary>Remakes every prefab (and the mushrooms' colour copies) in place; the placed ones follow.</summary>
        public static string RebuildPrefabs()
        {
            FlowerScatterV1.EnsureFolder(PrefabFolder);
            int made = 0;
            for (int k = 0; k < Kinds.Length; k++) { Prefab(Kinds[k], null); made++; }
            foreach (var colour in Colours) { Prefab(Kinds[colour.kind], colour); made++; }
            return "Rebuilt " + made + " handpainted flora prefabs.";
        }

        [MenuItem(Menu + "Remove Handpainted Flora (V1)")]
        static void MenuRemove()
        {
            int removed = 0;
            foreach (var name in new[] { FlowerGroup, MushroomGroup })
            {
                var group = GameObject.Find(name);
                if (group == null) continue;
                Undo.DestroyObjectImmediate(group);
                removed++;
            }
            Debug.Log(removed > 0 ? "Handpainted flora removed." : "No handpainted flora.");
        }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var map = GameObject.Find("Map") ?? throw new InvalidOperationException("Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            if (GameObject.Find(FlowerGroup) != null || GameObject.Find(MushroomGroup) != null)
                throw new InvalidOperationException("Handpainted flora already placed; remove it first.");
            FlowerScatterV1.EnsureFolder(PrefabFolder);
            // per kind: the model's own colours first, then a mushroom's other colours
            var prefabs = new List<GameObject>[Kinds.Length];
            for (int k = 0; k < Kinds.Length; k++) prefabs[k] = new List<GameObject> { Prefab(Kinds[k], null) };
            foreach (var colour in Colours) prefabs[colour.kind].Add(Prefab(Kinds[colour.kind], colour));

            // the anchors
            var fences = new List<Bounds>(); var trunks = new List<Vector3>(); var rocks = new List<Bounds>();
            var others = new List<Vector3>(); var keepOut = new List<Bounds>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (!Woods.Contains(new Vector2(b.center.x, b.center.z))) continue;
                string n = r.name, parent = r.transform.parent ? r.transform.parent.name : "";
                if (n == "SM_Bld_Barn_01" || n == "SM_Bld_WaterTower_01" || n == "SM_Prop_Windmill_01" || n == "Shelter" || n == "Object_4" ||
                    n.StartsWith("SM_Veh_") || n == "SM_Bridge" || n.StartsWith("SM_Prop_ClothesLine") || n == "Object_5") keepOut.Add(b);
                else if (parent == "Fence Bays" && !n.Contains("Gate")) fences.Add(b);
                else if ((n.Contains("Tree") || n.StartsWith("Fir") || n.Contains("Pine")) && !n.Contains("LOD1") && !n.Contains("LOD2") && b.size.y > 3f || n.Contains("Stump") || n.Contains("Log"))
                    trunks.Add(new Vector3(b.center.x, 0, b.center.z));
                else if ((parent.Contains("Rock") || n.Contains("Rock")) && b.size.x > .7f && b.size.x < 6f) rocks.Add(b);
                string o = n + parent;
                if (o.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("Mushroom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    o.IndexOf("Fern", StringComparison.OrdinalIgnoreCase) >= 0) others.Add(b.center);
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
            Vector3? Edge(System.Random rng, List<Bounds> list, float g0, float g1) => list.Count == 0 ? (Vector3?)null : EdgePoint(rng, list[rng.Next(list.Count)], FlowerScatterV1.Lerp(g0, g1, rng.NextDouble()));
            Vector3? Trunk(System.Random rng) => trunks.Count == 0 ? (Vector3?)null : Around(rng, trunks[rng.Next(trunks.Count)], .8f, 1.7f);

            var contexts = new List<Context>
            {
                new Context { name = "trees", budget = 48, kinds = new[] { Brown, Brown, Purple, Toadstool, LeafyPlant }, minGroup = 1, maxGroup = 3, groupGap = 2.5f, clear = .15f, earthOk = true, area = Woods,
                    spot = Trunk, step = (rng, p) => Around(rng, p, .22f, .4f) },
                new Context { name = "stumps and rocks", budget = 22, kinds = new[] { Brown, Purple, Toadstool, LeafyPlant, LeafyPlant }, minGroup = 1, maxGroup = 3, groupGap = 2.5f, clear = .15f, earthOk = true, area = Woods,
                    spot = rng => rng.NextDouble() < .6 ? Edge(rng, rocks, .2f, .45f) : Trunk(rng), step = (rng, p) => Around(rng, p, .25f, .45f) },
                new Context { name = "fences", budget = 42, kinds = new[] { Hyacinth, HyacinthBent, Buttercup, ButtercupSmall, LeafyPlant }, minGroup = 2, maxGroup = 4, groupGap = 3.5f, clear = .2f, area = Farm,
                    spot = rng => fences.Count == 0 ? (Vector3?)null : FenceFoot(rng, fences[rng.Next(fences.Count)]), step = (rng, p) => Around(rng, p, .3f, .55f) },
                new Context { name = "meadow", budget = 34, kinds = new[] { Hyacinth, Hyacinth, HyacinthBent, Buttercup, ButtercupSmall }, minGroup = 3, maxGroup = 5, groupGap = 8f, clear = .2f, area = Farm,
                    spot = rng => new Vector3(FlowerScatterV1.Lerp(Farm.xMin, Farm.xMax, rng.NextDouble()), 0, FlowerScatterV1.Lerp(Farm.yMin, Farm.yMax, rng.NextDouble())),
                    step = (rng, p) => Around(rng, p, .35f, .7f) },
                new Context { name = "path edges", budget = 14, kinds = new[] { Buttercup, ButtercupSmall }, minGroup = 1, maxGroup = 2, groupGap = 4f, clear = .2f, area = Farm,
                    spot = rng => PathEdge(rng, EarthAt), step = (rng, p) => Around(rng, p, .3f, .5f) },
            };

            Transform Group(string parentName, string name)
            {
                var g = new GameObject(name).transform;
                var parent = map.transform.Find(parentName);
                g.SetParent(parent != null ? parent : map.transform, false);
                Undo.RegisterCreatedObjectUndo(g.gameObject, "Place handpainted flora (V1)");
                return g;
            }
            Transform flowers = Group("Flowers", FlowerGroup), mushrooms = Group("Mushrooms", MushroomGroup);

            var rng0 = new System.Random(Seed);
            var rngColour = new System.Random(Seed + 1);   // its own, so the colours leave the layout as it was
            var colourOf = new Dictionary<int, int>();
            var placed = new List<Vector3>(); var centres = new List<Vector3>();
            var count = new int[Kinds.Length];
            var report = new System.Text.StringBuilder("Handpainted flora (V1): ");
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
                    if (!ctx.area.Contains(new Vector2(c.x, c.z)) || FlowerScatterV1.Near(ownCentres, c, ctx.groupGap) || FlowerScatterV1.Near(centres, c, 2f)) continue;
                    int size = rng0.Next(ctx.minGroup, ctx.maxGroup + 1), kind = ctx.kinds[rng0.Next(ctx.kinds.Length)];
                    var p = c; int made = 0;
                    colourOf.Clear();
                    for (int m = 0; m < size*4 && made < size && n < ctx.budget; m++)
                    {
                        if (m > 0) p = ctx.step(rng0, made > 0 ? p : c);
                        if (!Valid(p, ctx, EarthAt, placed, others, keepOut, terrain, out var at)) { p = c; continue; }
                        // a group is mostly one kind, now and then a second one mixed in
                        int k = made > 0 && rng0.NextDouble() < .3 ? ctx.kinds[rng0.Next(ctx.kinds.Length)] : kind;
                        // one colour per kind in a clump
                        if (!colourOf.TryGetValue(k, out int colour)) colourOf[k] = colour = rngColour.Next(prefabs[k].Count);
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[k][colour], Kinds[k].mushroom ? mushrooms : flowers);
                        go.transform.localScale = Vector3.one*FlowerScatterV1.Lerp(.85f, 1.15f, rng0.NextDouble());
                        // mushrooms lean a little, each its own way
                        float lean = Kinds[k].mushroom ? FlowerScatterV1.Lerp(-6f, 6f, rng0.NextDouble()) : 0f;
                        go.transform.SetPositionAndRotation(at - Vector3.up*(Kinds[k].mushroom ? .02f : .01f),
                            Quaternion.Euler(0, (float)rng0.NextDouble()*360f, 0)*Quaternion.Euler(lean, 0, 0));
                        Undo.RegisterCreatedObjectUndo(go, "Place handpainted flora (V1)");
                        placed.Add(at); made++; n++; count[k]++;
                    }
                    if (made > 0) { ownCentres.Add(c); centres.Add(c); }
                }
                report.Append(ctx.name + " " + n + ", ");
                total += n;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            report.Append("total " + total + " (");
            for (int k = 0; k < Kinds.Length; k++) report.Append(Kinds[k].name + " " + count[k] + (k < Kinds.Length - 1 ? ", " : ")."));
            return report.ToString();
        }

        static bool Valid(Vector3 p, Context ctx, Func<Vector3, float> earthAt, List<Vector3> placed, List<Vector3> others, List<Bounds> keepOut, Terrain terrain, out Vector3 at)
        {
            at = p;
            if (!ctx.area.Contains(new Vector2(p.x, p.z))) return false;
            foreach (var b in keepOut)
                if (p.x > b.min.x - .3f && p.x < b.max.x + .3f && p.z > b.min.z - .3f && p.z < b.max.z + .3f) return false;
            if (FlowerScatterV1.Near(placed, p, .22f) || FlowerScatterV1.Near(others, p, .4f)) return false;
            float e = earthAt(p);
            if (e > 8f || (!ctx.earthOk && e > .3f) || e > .7f) return false;   // never the tilled field or a path; the woods' floor may be bare
            if (FarmWaterBody.Find(p + Vector3.up*20f, out _) != null) return false;
            var d = terrain.terrainData; Vector3 tp = terrain.transform.position;
            if (d.GetSteepness((p.x - tp.x)/d.size.x, (p.z - tp.z)/d.size.z) > 32f) return false;
            at = new Vector3(p.x, terrain.SampleHeight(p) + tp.y, p.z);
            return !FlowerScatterV1.Solid(at, ctx.clear);
        }

        // a prefab for one piece: its mesh at the farm's size, standing with its foot on the prefab's pivot, its own
        // material (bendable for the flowers and the plant) in its own or another colour, and an LOD group that stops
        // drawing it when it is tiny
        static GameObject Prefab(Kind kind, Colour colour)
        {
            string name = "Chick_HP_" + kind.name + (colour != null ? "_" + colour.name : "");
            string path = PrefabFolder + "/" + name + ".prefab";
            Mesh mesh = null; Material source = null;
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(kind.asset).GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || f.sharedMesh.name != kind.mesh) continue;
                mesh = f.sharedMesh; source = r.sharedMaterial;
                break;
            }
            if (mesh == null) throw new InvalidOperationException("Missing mesh " + kind.mesh + " in " + kind.asset);

            var root = new GameObject(name);
            try
            {
                var b = mesh.bounds;
                float scale = kind.height/Mathf.Max(b.size.y, .001f);
                Vector3 offset = -new Vector3(b.center.x, b.min.y, b.center.z)*scale;
                var material = MaterialFor(source, kind, colour);
                var renderers = new List<Renderer>();
                Transform Body(string bodyName, Mesh bodyMesh, Vector3 at)
                {
                    var body = new GameObject(bodyName);
                    body.transform.SetParent(root.transform, false);
                    body.transform.localScale = Vector3.one*scale;
                    body.transform.localPosition = at;
                    body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
                    var renderer = body.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderers.Add(renderer);
                    return body.transform;
                }
                Color crumbs = kind.mushroom ? MushroomFoodSetup.CapColour(material.GetTexture("_BaseMap"), mesh) : Color.white;
                if (kind.split == null)
                {
                    Body(kind.name, mesh, offset);
                    if (kind.mushroom) root.AddComponent<EdibleMushroom>().Configure(crumbs);
                }
                else
                {
                    // each mushroom of the split model where it stood in the joined mesh, its own food
                    var apart = AssetDatabase.LoadAssetAtPath<GameObject>(kind.split) ?? throw new InvalidOperationException("Missing " + kind.split);
                    foreach (var f in apart.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Vector3 node = apart.transform.InverseTransformPoint(f.transform.position);
                        var body = Body(f.sharedMesh.name, f.sharedMesh, offset + node*scale);
                        body.gameObject.AddComponent<EdibleMushroom>().Configure(crumbs);
                    }
                }
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(CullBelow, renderers.ToArray()) });
                lod.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // the model's own texture and colour (or a recoloured copy): Chick/InteractiveFoliage (drawn from both sides,
        // bends) or, for a mushroom, URP Lit
        static Material MaterialFor(Material source, Kind kind, Colour colour)
        {
            bool mushroom = kind.mushroom;
            string path = Folder + "/Chick_HP_" + source.name + (colour != null ? "_" + colour.name : "") + ".mat";
            var shader = Shader.Find(mushroom ? "Universal Render Pipeline/Lit" : "Chick/InteractiveFoliage") ?? throw new InvalidOperationException("Shader missing.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            var texture = source.HasProperty("baseColorTexture") ? source.GetTexture("baseColorTexture") : null;
            mat.SetTexture("_BaseMap", colour != null && texture != null ? Recoloured(texture, kind, colour) : texture);
            mat.SetColor("_BaseColor", source.HasProperty("baseColorFactor") ? source.GetColor("baseColorFactor") : Color.white);
            float roughness = source.HasProperty("roughnessFactor") ? source.GetFloat("roughnessFactor") : 1f;
            mat.SetFloat("_Smoothness", Mathf.Clamp(1f - roughness, .05f, .2f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Cull", mushroom ? 2f : 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        // a copy of the texture (ColourTextureSize) with its coloured parts turned to the colour: hue, saturation and
        // brightness shifted in step with how saturated a pixel is, so the pale stems, gills and spots stay as they are
        static Texture2D Recoloured(Texture source, Kind kind, Colour colour)
        {
            string folder = Folder + "/Textures", path = folder + "/T_HP_" + kind.name + "_" + colour.name + ".png";
            FlowerScatterV1.EnsureFolder(folder);
            int size = Mathf.Min(ColourTextureSize, source.width);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var active = RenderTexture.active;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                var pixels = copy.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, .3f, s));
                    var turned = Color.HSVToRGB(Mathf.Repeat(h + colour.hue/360f, 1f), Mathf.Clamp01(s*colour.saturation), Mathf.Clamp01(v*colour.value));
                    turned.a = c.a;
                    pixels[i] = Color.Lerp(c, turned, w);
                }
                copy.SetPixels(pixels);
                System.IO.File.WriteAllBytes(path, copy.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(copy);
            }
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = ColourTextureSize;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            float t = (float)rng.NextDouble(), off = FlowerScatterV1.Lerp(.4f, .75f, rng.NextDouble())*(rng.NextDouble() < .5 ? -1 : 1);
            return alongX ? new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), 0, b.center.z + off) : new Vector3(b.center.x + off, 0, Mathf.Lerp(b.min.z, b.max.z, t));
        }

        // grass with earth within a metre and a bit on at least one side
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
    }
}
