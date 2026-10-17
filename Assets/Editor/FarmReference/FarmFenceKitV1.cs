using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Replaces the farm's fences (BOKI's wooden_fence_1 bays, white and pink-brown)
    // and its five gates with the hand-painted fence kit made in Blender from Synty's PolygonFarm gates
    // (Assets/Art/FarmFences/Models/FarmFenceKit.fbx) and boards, posts and nails modelled in Blender for it
    // (FarmFenceKitV2.fbx):
    //  - fence lines run post to post: one upright post at every bay end and junction, shared by the bays meeting
    //    there, and boards laid between them that follow the ground's rise (sheared, so they stay level across
    //    and meet each post at its own ground);
    //  - the inner yard fences get the painted four-rail fence, the outer boundary and the sheep pasture the rustic
    //    three-rail one;
    //  - no two alike: straight runs are re-cut into bays of uneven length, a few bays at a time plain, cross-braced,
    //    zigzag-braced or braced one way, every board nailed on a little out of line, posts of different girth and
    //    height leaning their own way; looks, line heights and timber shades dealt evenly over the farm;
    //  - gates: barn to field a wide farm gate, the chicken yard's two a double gate, the pasture and road gates a
    //    single small gate; each swings open by itself for the chick (FarmGate) and the road gate stays locked.
    // CaptureLayout reads the old fence lines once into FenceLayout.json; Build (re)builds everything from it, so it
    // can be re-run after changing the kit or the ground.
    public static class FarmFenceKitV1
    {
        const string Menu = "Tools/Chick/Fences/";
        const string Root = "Assets/Art/FarmFences";
        const string KitPath = Root + "/Models/FarmFenceKit.fbx";       // gates (from Synty's PolygonFarm gates)
        const string BoardsPath = Root + "/Models/FarmFenceKitV2.fbx";  // boards, posts and nails modelled in Blender
        const string TexturePath = Root + "/Textures/FarmFenceWood.png";
        const string MaterialPath = Root + "/Materials/FarmFence_Timber.mat";
        const string LayoutPath = Root + "/Generated/FenceLayout.json";
        const string BaysPath = Root + "/Generated/FarmFenceBays.asset";
        const string InnerGroup = "Farm Reference Map/04 Fences and Gates";
        const string PastureGroup = "Farm Reference Map/07 Sheep Pasture/Pasture Fence";
        const string BaysName = "Fence Bays", PostsName = "Fence Posts";
        const float RailSpan = 2.5f;      // the kit's boards are textured as one 2.5 m bay
        const float MergeDistance = .25f; // bay ends this close share a post
        const int Boards = 5;             // board variants per style in FarmFenceKitV2
        static readonly string[] Samples = { "SM_Prop_Fence_Fancy_Gate_02", "SM_Prop_Fence_Fancy_Gate_01", "SM_Prop_Fence_Wood_Gate_01" };

        public enum Style { Painted, Rustic }
        public enum GateKind { Wood, FancyDouble, FancySingle }
        public enum Brace { None, Single, SingleMirrored, Cross, Zigzag }

        [Serializable] public class Bay { public string name, group; public Style style; public Vector2 a, b; }
        [Serializable] public class Gate { public string name, parent, group; public GateKind kind; public bool locked; public Style style; public Vector2 p, q; }
        [Serializable] class Layout { public List<Bay> bays = new List<Bay>(); public List<Gate> gates = new List<Gate>(); }

        struct GateSpec
        {
            public string frame; public float postOffset, reach; public string[] leaves; public Vector3[] hinges;
        }

        static GateSpec Spec(GateKind kind)
        {
            switch (kind)
            {
                case GateKind.FancyDouble:
                    return new GateSpec { frame = "GateFancyDouble", postOffset = 1.779f, reach = 2.6f,
                        leaves = new[] { "GateFancyDouble_LeafL", "GateFancyDouble_LeafR" },
                        hinges = new[] { new Vector3(1.6645f, 0, .022f), new Vector3(-1.6805f, 0, .022f) } };
                case GateKind.FancySingle:
                    return new GateSpec { frame = "GateFancySingle", postOffset = .664f, reach = 1.9f,
                        leaves = new[] { "GateFancySingle_Leaf" }, hinges = new[] { new Vector3(.5515f, 0, .0385f) } };
                default:
                    return new GateSpec { frame = "GateWood", postOffset = 1.757f, reach = 3.9f,
                        leaves = new[] { "GateWood_Leaf" }, hinges = new[] { new Vector3(1.6565f, 0, -.0125f) } };
            }
        }

        // The old gates, the kit gate each one becomes, and whether it stays shut.
        static readonly (string path, GateKind kind, bool locked)[] OldGates =
        {
            (InnerGroup + "/Gate 1 - Barn to Field", GateKind.Wood, false),
            (InnerGroup + "/Gate 2 - Field to Chicken Yard", GateKind.FancyDouble, false),
            (InnerGroup + "/Gate 3 - Barn to Chicken Yard", GateKind.FancyDouble, false),
            ("Farm Reference Map/08 West Road/Gate - Farm to Outside (closed)", GateKind.FancySingle, true),
            ("Farm Reference Map/07 Sheep Pasture/Gate - Crop Fields to Sheep Pasture", GateKind.FancySingle, false),
        };

        [MenuItem(Menu + "Capture Fence Layout (Kit V1)")]
        static void CaptureMenu() => Debug.Log(CaptureLayout());

        [MenuItem(Menu + "Build Hand-Painted Fences (Kit V1)")]
        static void BuildMenu() => Debug.Log(Build());

        // Reads the old wooden_fence_1 bays (their ends along the ground) and the old gates' bays.
        public static string CaptureLayout()
        {
            if (File.Exists(LayoutPath)) throw new InvalidOperationException(LayoutPath + " exists already; delete it to capture again.");
            var layout = new Layout();
            foreach (var group in new[] { InnerGroup, PastureGroup })
            {
                var root = GameObject.Find(group) ?? throw new InvalidOperationException(group + " missing.");
                foreach (Transform child in root.transform)
                {
                    var filter = child.GetComponent<MeshFilter>();
                    if (!child.gameObject.activeSelf || filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Wooden_Fence_1_Mesh") continue;
                    var b = filter.sharedMesh.bounds;
                    Vector3 c = child.TransformPoint(b.center), right = child.TransformVector(new Vector3(b.extents.x, 0, 0));
                    layout.bays.Add(new Bay
                    {
                        name = child.name, group = group,
                        style = group == PastureGroup || child.name.StartsWith("Outer", StringComparison.Ordinal) ? Style.Rustic : Style.Painted,
                        a = new Vector2(c.x - right.x, c.z - right.z), b = new Vector2(c.x + right.x, c.z + right.z),
                    });
                }
            }
            foreach (var (path, kind, locked) in OldGates)
            {
                var old = GameObject.Find(path) ?? throw new InvalidOperationException(path + " missing.");
                Vector3 centre = old.transform.position;
                var posts = new List<Vector3>();
                foreach (Transform part in old.transform) if (part.name == "Gate Post") posts.Add(part.position);
                if (posts.Count == 2) centre = (posts[0] + posts[1])*.5f;
                // The bay the gate stands in: the two ends of neighbouring bays that frame its centre.
                Bay best = null; Vector2 p = default, q = default; float bestScore = float.MaxValue;
                var c2 = new Vector2(centre.x, centre.z);
                foreach (var bay1 in layout.bays)
                foreach (var e1 in new[] { bay1.a, bay1.b })
                foreach (var bay2 in layout.bays)
                {
                    if (bay2 == bay1) continue;
                    foreach (var e2 in new[] { bay2.a, bay2.b })
                    {
                        float width = Vector2.Distance(e1, e2);
                        if (width < 2f || width > 5.5f) continue;
                        float score = Vector2.Distance((e1 + e2)*.5f, c2);
                        if (score < bestScore) { bestScore = score; best = bay1; p = e1; q = e2; }
                    }
                }
                if (best == null || bestScore > .6f) throw new InvalidOperationException("No bay found for " + path + " (best " + bestScore + ").");
                layout.gates.Add(new Gate
                {
                    name = old.name, parent = ParentPath(old.transform), group = best.group, kind = kind, locked = locked, style = best.style, p = p, q = q,
                });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
            File.WriteAllText(LayoutPath, JsonUtility.ToJson(layout, true));
            AssetDatabase.ImportAsset(LayoutPath);
            return "Fence layout captured: " + layout.bays.Count + " bays, " + layout.gates.Count + " gates -> " + LayoutPath;
        }

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            if (!File.Exists(LayoutPath)) throw new InvalidOperationException("Capture the fence layout first.");
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            var terrain = Terrain.activeTerrain ?? throw new InvalidOperationException("Terrain missing.");
            var kit = LoadKit();
            var materials = EnsureMaterials();

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Build hand-painted fences"); int undo = Undo.GetCurrentGroup();
            int removed = RemoveOld(layout);

            // Posts: every bay end, merged where bays meet; gate posts come with the gate frames.
            var nodes = new List<Vector2>(); var nodeStyle = new List<Style>(); var nodeGroup = new List<string>(); var gatePost = new List<bool>();
            Func<Vector2, Style, string, bool, int> node = (p, style, group, isGatePost) =>
            {
                for (int i = 0; i < nodes.Count; i++)
                    if (Vector2.Distance(nodes[i], p) < MergeDistance)
                    {
                        if (style == Style.Rustic) nodeStyle[i] = Style.Rustic;   // the boundary's posts win at junctions
                        gatePost[i] |= isGatePost;
                        return i;
                    }
                nodes.Add(p); nodeStyle.Add(style); nodeGroup.Add(group); gatePost.Add(isGatePost);
                return nodes.Count - 1;
            };
            // Fence lines: bays meeting in a straight line are joined, then cut again into bays of uneven length
            // (fences in real life are never measured out evenly). Corners, junctions and gate bays end a line.
            var edges = new List<(int a, int b, Style style, string name, string group)>();
            foreach (var bay in layout.bays)
                edges.Add((node(bay.a, bay.style, bay.group, false), node(bay.b, bay.style, bay.group, false), bay.style, bay.name, bay.group));
            var fillers = new List<(int a, int b, Style style, string name, string group)>();
            var fixedNodes = new HashSet<int>();
            foreach (var gate in layout.gates)
            {
                var spec = Spec(gate.kind);
                Vector2 axis = (gate.q - gate.p).normalized, mid = (gate.p + gate.q)*.5f;
                int p = node(gate.p, gate.style, gate.group, false), q = node(gate.q, gate.style, gate.group, false);
                int gp = node(mid - axis*spec.postOffset, gate.style, gate.group, true), gq = node(mid + axis*spec.postOffset, gate.style, gate.group, true);
                fillers.Add((p, gp, gate.style, gate.name + " - side A", gate.group));
                fillers.Add((gq, q, gate.style, gate.name + " - side B", gate.group));
                fixedNodes.Add(p); fixedNodes.Add(q);
            }
            var spans = new List<(int a, int b, Style style, string name, string group, Brace brace, float height, int shade)>();
            foreach (var f in fillers) spans.Add((f.a, f.b, f.style, f.name, f.group, Brace.None, 1f, 0));
            var chains = Chains(nodes, edges, fixedNodes);
            int chainIndex = 0;
            // The looks are dealt from a shuffled bag, a few bays at a time, so every kind turns up evenly all over
            // the farm instead of whole lines of one kind.
            var bagRng = new System.Random(4242);
            var bag = new List<Brace>();
            Func<Brace> deal = () =>
            {
                if (bag.Count == 0)
                {
                    bag.AddRange(new[] { Brace.None, Brace.Cross, Brace.Zigzag, Brace.Single });
                    for (int i = bag.Count - 1; i > 0; i--) { int j = bagRng.Next(i + 1); var t = bag[i]; bag[i] = bag[j]; bag[j] = t; }
                }
                var look = bag[bag.Count - 1]; bag.RemoveAt(bag.Count - 1);
                return look;
            };
            // Each line is painted from its own batch of timber: shades dealt the same even way.
            var shadeBag = new List<int>();
            Func<int> dealShade = () =>
            {
                if (shadeBag.Count == 0)
                {
                    for (int i = 0; i < Shades.Length; i++) shadeBag.Add(i);
                    for (int i = shadeBag.Count - 1; i > 0; i--) { int j = bagRng.Next(i + 1); int t = shadeBag[i]; shadeBag[i] = shadeBag[j]; shadeBag[j] = t; }
                }
                int shade = shadeBag[shadeBag.Count - 1]; shadeBag.RemoveAt(shadeBag.Count - 1);
                return shade;
            };
            foreach (var chain in chains)
            {
                var first = edges[chain.edges[0]];
                var rng = new System.Random(Seed(nodes[chain.nodes[0]]) ^ (++chainIndex*92821));
                var points = new List<Vector2>();
                foreach (int n in chain.nodes) points.Add(nodes[n]);
                float total = 0;
                for (int i = 1; i < points.Count; i++) total += Vector2.Distance(points[i - 1], points[i]);
                int count = Mathf.Max(1, Mathf.RoundToInt(total/2.55f));
                var weights = new float[count]; float sum = 0;
                for (int i = 0; i < count; i++) sum += weights[i] = Range(rng, .68f, 1.32f);
                // Each line its own height (fences are never all built alike); the bottom board stays where it is.
                float height = Range(rng, .84f, 1.14f);
                int lineShade = dealShade();
                var look = Brace.None; int lookLeft = 0; bool mirrored = false;
                int previous = chain.nodes[0]; float along = 0;
                for (int i = 0; i < count; i++)
                {
                    along += total*weights[i]/sum;
                    int next = i == count - 1 ? chain.nodes[chain.nodes.Count - 1] : node(PointAlong(points, along), first.style, first.group, false);
                    float length = Vector2.Distance(nodes[previous], nodes[next]);
                    if (lookLeft-- <= 0) { look = deal(); lookLeft = rng.Next(2, 4) - 1; mirrored = rng.NextDouble() < .5; }
                    var brace = look == Brace.Zigzag ? ((i % 2 == 0) != mirrored ? Brace.Single : Brace.SingleMirrored)
                              : look == Brace.Single && mirrored ? Brace.SingleMirrored : look;
                    if (length < 1.5f) brace = Brace.None;
                    spans.Add((previous, next, first.style, first.name + (count > 1 ? " - " + (i + 1) : ""), first.group, brace, height, lineShade));
                    previous = next;
                }
            }
            var usedNodes = new HashSet<int>();
            var postHeight = new Dictionary<int, float>();   // a post stands as tall as the tallest line it carries
            var postShade = new Dictionary<int, int>();      // and is painted like the first line it carries
            foreach (var s in spans)
                foreach (int n in new[] { s.a, s.b })
                {
                    usedNodes.Add(n);
                    postHeight[n] = Mathf.Max(postHeight.TryGetValue(n, out var h) ? h : 0f, s.height);
                    if (!postShade.ContainsKey(n) || postShade[n] == 0) postShade[n] = s.shade;
                }

            if (AssetDatabase.LoadMainAssetAtPath(BaysPath) != null) AssetDatabase.DeleteAsset(BaysPath);
            var meshes = new List<Mesh>();
            var containers = new Dictionary<string, (Transform bays, Transform posts)>();
            Func<string, (Transform bays, Transform posts)> container = group =>
            {
                if (containers.TryGetValue(group, out var c)) return c;
                var g = GameObject.Find(group).transform;
                c = (NewChild(BaysName, g), NewChild(PostsName, g));
                containers[group] = c;
                return c;
            };

            int postCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (gatePost[i] || !usedNodes.Contains(i)) continue;
                var rng = new System.Random(Seed(nodes[i]));
                var post = NewChild("Post " + (++postCount).ToString("000"), container(nodeGroup[i]).posts);
                float y = Ground(terrain, nodes[i]) - .02f + Range(rng, -.035f, .015f);
                post.position = new Vector3(nodes[i].x, y, nodes[i].y);
                // Set by hand: a little out of true, each its own way, and no two posts quite the same.
                post.rotation = Quaternion.Euler(Range(rng, -2f, 2f), Range(rng, -8f, 8f), Range(rng, -2f, 2f));
                float thick = Range(rng, .9f, 1.15f);
                post.localScale = new Vector3(thick*Range(rng, .95f, 1.05f), Range(rng, .97f, 1.07f)*(.25f + .75f*postHeight[i]), thick);
                var models = nodeStyle[i] == Style.Rustic
                    ? new[] { "PostRound_0", "PostRound_1", "PostRough_0", "PostRough_1" }
                    : new[] { "PostSquareCap_0", "PostSquareCap_1", "PostSquareCap_0", "PostSquareSlant_0", "PostSquareSlant_1" };
                var mesh = kit[models[rng.Next(models.Length)]];
                Dress(post.gameObject, mesh, materials[postShade[i]], true);
                var box = post.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0, .5f, 0); box.size = new Vector3(.17f, 1.7f, .17f);
            }

            foreach (var span in spans)
            {
                var rng = new System.Random(Seed(nodes[span.a] + nodes[span.b]*.37f));
                var bay = NewChild(span.name, container(span.group).bays);
                var mesh = BayMesh(kit, span.style, span.brace, span.height, terrain, nodes[span.a], nodes[span.b], rng, bay, span.name);
                meshes.Add(mesh);
                Dress(bay.gameObject, mesh, materials[span.shade], true);
                bay.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                var blocker = NewChild("Camera Blocker", bay);
                blocker.gameObject.layer = LayerMask.NameToLayer("CameraBlocker");
                var b = mesh.bounds;
                var box = blocker.gameObject.AddComponent<BoxCollider>();
                box.center = b.center; box.size = new Vector3(b.size.x, b.size.y, .28f);
            }

            foreach (var gate in layout.gates) BuildGate(gate, kit, materials[0], terrain);

            // The kit samples placed by hand to show which models to use.
            foreach (var name in Samples)
            {
                var sample = GameObject.Find(name);
                if (sample != null && sample.transform.parent == null) Undo.DestroyObjectImmediate(sample);
            }

            AssetDatabase.CreateAsset(meshes[0], BaysPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], BaysPath);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return "Hand-painted fences built: " + postCount + " posts, " + spans.Count + " bays, " + layout.gates.Count +
                   " gates; " + removed + " old fence pieces and gates removed.";
        }

        // A bay built from the Blender boards (FarmFenceKitV2: each board runs from x = 0 to x = -1, cross-section
        // centred, face to +z, sagging, twisting and sawn a little out of square in its own way). Every rail is a board
        // picked at random and nailed on a little out of line; braces are boards laid corner to corner; the painted
        // fence's boards are nailed to the face of the posts. The whole bay is then sheared so it rises with the ground
        // from post to post, with a little lift where the ground bulges between the posts.
        static Mesh BayMesh(Dictionary<string, Mesh> kit, Style style, Brace brace, float height, Terrain terrain, Vector2 a, Vector2 b, System.Random rng, Transform bay, string name)
        {
            float length = Vector2.Distance(a, b);
            float ha = Ground(terrain, a), hb = Ground(terrain, b), lift = 0;
            for (int i = 1; i < 8; i++)
            {
                float t = i/8f;
                lift = Mathf.Max(lift, Ground(terrain, Vector2.Lerp(a, b, t)) - Mathf.Lerp(ha, hb, t) - .02f);
            }
            lift = Mathf.Min(lift, .12f);
            float da = Range(rng, -.018f, .018f), db = Range(rng, -.018f, .018f);
            Vector3 dir = new Vector3(b.x - a.x, 0, b.y - a.y).normalized;
            bay.position = new Vector3(a.x, ha + lift, a.y);
            bay.rotation = Quaternion.LookRotation(Vector3.Cross(-dir, Vector3.up), Vector3.up);   // local -x runs a -> b

            bool rustic = style == Style.Rustic;
            string boards = rustic ? "PlankRough_" : "PlankSawn_";
            float[] rails = rustic ? new[] { .30f, .68f, 1.1f } : new[] { .165f, .48f, .8f, 1.12f };
            for (int r = 1; r < rails.Length; r++) rails[r] = rails[0] + (rails[r] - rails[0])*height;
            float railZ = rustic ? 0f : .128f, boardHalf = rustic ? .042f : .038f;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            var nail = kit["NailHead"];

            for (int r = 0; r < rails.Length; r++)
            {
                float railHeight = rails[r];
                var board = kit[boards + rng.Next(Boards)];
                float ya = railHeight + Range(rng, -.024f, .024f), yb = railHeight + Range(rng, -.024f, .024f), z = railZ + Range(rng, -.007f, .007f);
                // Boards of different widths: a good share of them wide, which narrows the gaps without closing
                // them (the bottom board stays clear of the ground).
                float width = rng.NextDouble() < .45 ? (r == 0 ? Range(rng, 1.1f, 1.25f) : Range(rng, 1.3f, 1.6f)) : Range(rng, .92f, 1.15f);
                Lay(board, new Vector2(0, ya), new Vector2(-length, yb), z, width, rng, vertices, uv, triangles);
                if (!rustic)   // two nails into each post
                    foreach (float x in new[] { -.04f, -(length - .04f) })
                    {
                        float y = Mathf.Lerp(ya, yb, -x/length);
                        foreach (float dy in new[] { -.032f, .032f })
                            Place(nail, new Vector3(x + Range(rng, -.008f, .008f), y + dy*width, z + boardHalf - .001f), Range(rng, 0f, 360f), vertices, uv, triangles);
                    }
            }

            if (brace != Brace.None)
            {
                float low = rails[0], high = rails[rails.Length - 1], inset = .1f;
                float z0 = railZ + boardHalf + boardHalf*.9f;
                var pairs = new List<(float from, float to)>();
                if (brace == Brace.Single || brace == Brace.Cross) pairs.Add((low, high));
                if (brace == Brace.SingleMirrored || brace == Brace.Cross) pairs.Add((high, low));
                for (int k = 0; k < pairs.Count; k++)
                {
                    float z = z0 + k*boardHalf*1.9f;
                    var start = new Vector2(-inset, pairs[k].from + Range(rng, -.02f, .02f));
                    var end = new Vector2(-(length - inset), pairs[k].to + Range(rng, -.02f, .02f));
                    Lay(kit[boards + rng.Next(Boards)], start, end, z, .85f, rng, vertices, uv, triangles);
                    Vector2 along = (end - start).normalized;
                    foreach (var p in new[] { start + along*.06f, end - along*.06f })
                        Place(nail, new Vector3(p.x, p.y, z + boardHalf - .001f), Range(rng, 0f, 360f), vertices, uv, triangles);
                }
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                float t = Mathf.Clamp01(-v.x/length);
                vertices[i] = new Vector3(v.x, v.y + t*(hb - ha) + Mathf.Lerp(da, db, t), v.z);
            }
            var mesh = new Mesh { name = "FenceBay " + name };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Lays a board from `start` to `end` (bay space, x and y; centre line of the board) at depth z, its width
        // scaled by `width`. The board's own up stays on the upper side; the grain is stretched to the board's length
        // and some boards take the band's sister shade, so neighbours look cut from different trees.
        static void Lay(Mesh board, Vector2 start, Vector2 end, float z, float width, System.Random rng,
                        List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
        {
            Vector2 run = end - start; float length = run.magnitude; Vector2 along = run/length;
            Vector2 up = new Vector2(along.y, -along.x);
            if (up.y < 0) up = -up;
            float shift = Range(rng, 0f, 1f); bool sister = rng.NextDouble() < .35;
            int first = vertices.Count;
            var source = board.vertices; var sourceUv = board.uv;
            for (int i = 0; i < source.Length; i++)
            {
                var v = source[i];
                Vector2 p = start + along*(-v.x*length) + up*(v.y*width);
                vertices.Add(new Vector3(p.x, p.y, z + v.z));
                float w = sourceUv[i].y; int band = Mathf.FloorToInt(w*8f);
                if (sister && band <= 3) w += (band % 2 == 0 ? 1f : -1f)/8f;
                uv.Add(new Vector2(sourceUv[i].x*length/RailSpan + shift, w));
            }
            foreach (int index in board.triangles) triangles.Add(first + index);
        }

        static void Place(Mesh part, Vector3 at, float spin, List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
        {
            int first = vertices.Count;
            var turn = Quaternion.Euler(0, 0, spin);
            foreach (var v in part.vertices) vertices.Add(at + turn*v);
            uv.AddRange(part.uv);
            foreach (int index in part.triangles) triangles.Add(first + index);
        }

        // Fence lines through the post graph: runs of bays going straight on through posts no other bay meets.
        static List<(List<int> nodes, List<int> edges)> Chains(List<Vector2> nodes, List<(int a, int b, Style style, string name, string group)> edges, HashSet<int> fixedNodes)
        {
            var incident = new Dictionary<int, List<int>>();
            for (int e = 0; e < edges.Count; e++)
                foreach (int n in new[] { edges[e].a, edges[e].b })
                {
                    if (!incident.TryGetValue(n, out var list)) incident[n] = list = new List<int>();
                    list.Add(e);
                }
            Func<int, bool> through = n =>
            {
                if (fixedNodes.Contains(n) || incident[n].Count != 2) return false;
                var e0 = edges[incident[n][0]]; var e1 = edges[incident[n][1]];
                if (e0.style != e1.style || e0.group != e1.group) return false;
                Vector2 d0 = (nodes[e0.a == n ? e0.b : e0.a] - nodes[n]).normalized, d1 = (nodes[e1.a == n ? e1.b : e1.a] - nodes[n]).normalized;
                return Vector2.Dot(d0, d1) < -.97f;
            };
            var used = new bool[edges.Count];
            var chains = new List<(List<int> nodes, List<int> edges)>();
            Func<int, int, (List<int>, List<int>)> walk = (start, edge) =>
            {
                var chainNodes = new List<int> { start }; var chainEdges = new List<int>();
                int current = start;
                while (true)
                {
                    used[edge] = true; chainEdges.Add(edge);
                    int other = edges[edge].a == current ? edges[edge].b : edges[edge].a;
                    chainNodes.Add(other);
                    if (other == start || !through(other)) break;
                    int next = incident[other][0] == edge ? incident[other][1] : incident[other][0];
                    if (used[next]) break;
                    current = other; edge = next;
                }
                return (chainNodes, chainEdges);
            };
            foreach (var pair in incident)
                if (!through(pair.Key))
                    foreach (int e in pair.Value)
                        if (!used[e]) chains.Add(walk(pair.Key, e));
            for (int e = 0; e < edges.Count; e++)
                if (!used[e]) chains.Add(walk(edges[e].a, e));   // closed rings with no end
            return chains;
        }

        static Vector2 PointAlong(List<Vector2> points, float distance)
        {
            for (int i = 1; i < points.Count; i++)
            {
                float step = Vector2.Distance(points[i - 1], points[i]);
                if (distance <= step) return Vector2.Lerp(points[i - 1], points[i], distance/step);
                distance -= step;
            }
            return points[points.Count - 1];
        }

        static void BuildGate(Gate gate, Dictionary<string, Mesh> kit, Material material, Terrain terrain)
        {
            var spec = Spec(gate.kind);
            var parent = GameObject.Find(gate.parent) ?? throw new InvalidOperationException(gate.parent + " missing.");
            Vector2 mid = (gate.p + gate.q)*.5f, axis = (gate.q - gate.p).normalized;
            var root = NewChild(gate.name, parent.transform);
            Vector3 right = new Vector3(axis.x, 0, axis.y);
            root.rotation = Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up);
            // Level, and high enough that the leaves clear the ground all round their swing.
            float posts = Mathf.Min(Ground(terrain, mid - axis*spec.postOffset), Ground(terrain, mid + axis*spec.postOffset));
            float swing = float.MinValue;
            for (int i = 0; i < spec.hinges.Length; i++)
            {
                Vector3 hinge = root.TransformDirection(spec.hinges[i]);
                float reach = kit[spec.leaves[i]].bounds.size.x;
                for (int k = 0; k <= 12; k++)
                {
                    float angle = -100f + k*200f/12f;
                    Vector3 d = Quaternion.Euler(0, angle, 0)*(spec.hinges[i].x > 0 ? -right : right);
                    foreach (float r in new[] { .35f, .7f, 1f })
                        swing = Mathf.Max(swing, Ground(terrain, mid + new Vector2(hinge.x + d.x*reach*r, hinge.z + d.z*reach*r)));
                }
            }
            float y = Mathf.Min(Mathf.Max(posts - .02f, swing - .03f), posts + .18f);
            root.position = new Vector3(mid.x, y, mid.y);
            Dress(root.gameObject, kit[spec.frame], material, true);
            root.gameObject.AddComponent<MeshCollider>().sharedMesh = kit[spec.frame];

            var leaves = new FarmGate.Leaf[spec.leaves.Length];
            for (int i = 0; i < spec.leaves.Length; i++)
            {
                var mesh = kit[spec.leaves[i]];
                var leaf = NewChild(i == 0 ? "Leaf" : "Leaf " + (i + 1), root);
                leaf.localPosition = spec.hinges[i];
                Dress(leaf.gameObject, mesh, material, false);
                var box = leaf.gameObject.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center; box.size = mesh.bounds.size;
                var body = leaf.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
                // A leaf reaching towards -x swings to +z when its hinge turns positive (Unity's y rotation is clockwise).
                leaves[i] = new FarmGate.Leaf { hinge = leaf, towardsPositiveZ = mesh.bounds.center.x < 0 ? 1f : -1f };
            }
            var blocker = NewChild("Camera Blocker", root);
            blocker.gameObject.layer = LayerMask.NameToLayer("CameraBlocker");
            foreach (float side in new[] { -1f, 1f })
            {
                var box = blocker.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(side*spec.postOffset, .6f, 0); box.size = new Vector3(.2f, 1.6f, .2f);
            }

            var farmGate = root.gameObject.AddComponent<FarmGate>();
            var so = new SerializedObject(farmGate);
            var list = so.FindProperty("leaves");
            list.arraySize = leaves.Length;
            for (int i = 0; i < leaves.Length; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("hinge").objectReferenceValue = leaves[i].hinge;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("towardsPositiveZ").floatValue = leaves[i].towardsPositiveZ;
            }
            so.FindProperty("locked").boolValue = gate.locked;
            so.FindProperty("reach").floatValue = spec.reach;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The old bays and gates, and whatever an earlier build made.
        static int RemoveOld(Layout layout)
        {
            int removed = 0;
            foreach (var group in new[] { InnerGroup, PastureGroup })
            {
                var root = GameObject.Find(group);
                if (root == null) continue;
                var doomed = new List<GameObject>();
                foreach (Transform child in root.transform)
                {
                    var filter = child.GetComponent<MeshFilter>();
                    bool oldBay = child.gameObject.activeSelf && filter != null && filter.sharedMesh != null && filter.sharedMesh.name == "Wooden_Fence_1_Mesh";
                    if (oldBay || child.name == BaysName || child.name == PostsName) doomed.Add(child.gameObject);
                }
                foreach (var g in doomed) { Undo.DestroyObjectImmediate(g); removed++; }
            }
            foreach (var gate in layout.gates)
            {
                var old = GameObject.Find(gate.parent + "/" + gate.name);
                if (old != null) { Undo.DestroyObjectImmediate(old); removed++; }
            }
            return removed;
        }

        static Dictionary<string, Mesh> LoadKit()
        {
            var kit = new Dictionary<string, Mesh>();
            foreach (var path in new[] { KitPath, BoardsPath })
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh mesh) kit[mesh.name] = mesh;
            if (kit.Count < 30) throw new InvalidOperationException("Fence kit incomplete: " + kit.Count + " meshes.");
            return kit;
        }

        // The timber material and its shades (base colour tints over the painted texture), plain one first: plain
        // walnut, chestnut, espresso, weathered grey-brown, honey.
        static readonly (string suffix, Color tint)[] Shades =
        {
            ("", Color.white), ("_Chestnut", new Color(1f, .9f, .82f)), ("_Espresso", new Color(.84f, .81f, .8f)),
            ("_Weathered", new Color(.93f, .94f, .97f)), ("_Honey", new Color(1f, .96f, .86f)),
        };

        static Material[] EnsureMaterials()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (importer.wrapModeV != TextureWrapMode.Clamp || importer.anisoLevel != 4)
            {
                // Bands stacked in v (clamped so they don't bleed into each other), the grain repeats along u.
                importer.wrapModeU = TextureWrapMode.Repeat; importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 4; importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var materials = new Material[Shades.Length];
            for (int i = 0; i < Shades.Length; i++)
            {
                string path = MaterialPath.Replace(".mat", Shades[i].suffix + ".mat");
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = Path.GetFileNameWithoutExtension(path) };
                    material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
                    material.SetFloat("_Smoothness", .12f);
                    material.SetFloat("_EnvironmentReflections", 0f);
                    material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", Shades[i].tint);
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            return materials;
        }

        static void Dress(GameObject target, Mesh mesh, Material material, bool isStatic)
        {
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            target.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(target, (StaticEditorFlags)~0 & ~StaticEditorFlags.ContributeGI);
        }

        static Transform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build hand-painted fences");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static string ParentPath(Transform t)
        {
            string path = t.parent.name;
            for (var p = t.parent.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        static float Ground(Terrain terrain, Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;
        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble()*(max - min);
        static int Seed(Vector2 p) => Mathf.RoundToInt(p.x*10f)*7919 + Mathf.RoundToInt(p.y*10f)*104729;
    }
}
