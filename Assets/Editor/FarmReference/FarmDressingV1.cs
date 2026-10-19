using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Dresses the farm with the project's own Synty models (PolygonFarm and
    // PolygonGeneric) and spreads the two IL3DN grass clumps, all under "Farm Reference Map/10 Farm Dressing":
    //  - small scenes: a woodcutting corner (stumps, chainsaw, axe, wood stacks, a log), hay by the barn and in the
    //    pasture, round bales in the lower field, a small orchard with a ladder and an apiary, a garden table, a junk
    //    corner, a produce stand by the west road and a few farm buildings and machines round the fences;
    //  - nature: trees, stumps, logs, rock clusters with pebbles, flower clusters, sunflowers, mushrooms at the foot of
    //    trees, stumps and logs, ferns, shrubs, tall grass by the pond, fallen leaves, twigs and roots;
    //  - the IL3DN grass clumps along the fences, round rocks, stumps and trees, on the pond banks and in the meadows.
    // Nothing goes on the paths, in the tilled field, in the water, in the seed yard, in front of the gates or onto
    // the spawn points, and nothing overlaps what is already there. Big things keep their colliders, small decor does
    // not, so the chick walks through flowers and grass. Re-running replaces the earlier set (one undo step).
    public static class FarmDressingV1
    {
        const string Menu = "Tools/Chick/Scenery/";
        const string GroupName = "10 Farm Dressing";
        const string PF = "Assets/Synty/PolygonFarm/Prefabs/", PG = "Assets/Synty/PolygonGeneric/Prefabs/";
        static readonly string[] IL3DNGrass = { "Assets/IL3DN/Graphics/Prefabs/IL3DN_Plant_Grass_01.prefab", "Assets/IL3DN/Graphics/Prefabs/IL3DN_Plant_Grass_02.prefab" };

        static readonly Rect Farm = Rect.MinMaxRect(59.7f, -63.6f, 128.7f, 35.6f);
        static readonly Rect Outer = Rect.MinMaxRect(34f, -77f, 158f, 60f);
        static readonly Rect Orchard = Rect.MinMaxRect(63.5f, -61.5f, 91f, -50.5f);
        static readonly Rect LowerField = Rect.MinMaxRect(104f, -62.5f, 127.5f, -50f);
        static Rect seedYard = Rect.MinMaxRect(103f, 12f, 118.5f, 28.5f);

        enum Kind { Decor, Solid, Tree }
        struct Disc { public Vector2 c; public float r; public Kind k; }

        static Terrain terrain; static TerrainData data; static Vector2[] shore; static System.Random rng;
        static Transform group, flock, chick;
        static float[,] pathMap, tillMap; static int res;
        static readonly List<Rect> lowRects = new List<Rect>(), tallRects = new List<Rect>();
        static readonly List<Disc> discs = new List<Disc>();
        static readonly List<Vector2> gates = new List<Vector2>(), spawns = new List<Vector2>();
        static readonly List<Vector2> trunks = new List<Vector2>(), woody = new List<Vector2>(), stones = new List<Vector2>();
        static readonly Dictionary<string, Vector3> sizes = new Dictionary<string, Vector3>();
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly SortedDictionary<string, int> counts = new SortedDictionary<string, int>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static readonly Collider[] buffer = new Collider[128];

        static string F(string s) => PF + s + ".prefab";
        static string G(string s) => PG + s + ".prefab";
        static string[] Fs(params string[] s) => Array.ConvertAll(s, F);
        static string[] Gs(params string[] s) => Array.ConvertAll(s, G);

        // ---- the pools ----------------------------------------------------------------------------------------------
        static readonly string[] FruitTrees = Fs("Environments/SM_Env_Tree_Apple_Grown_01", "Environments/SM_Env_Tree_Pear_Grown_01", "Environments/SM_Env_Tree_Plum_Grown_01",
            "Environments/SM_Env_Tree_Cherry_Grown_01", "Environments/SM_Env_Tree_Peach_Grown_01", "Environments/SM_Env_Tree_Apricot_Grown_01",
            "Environments/SM_Env_Tree_Apple_01", "Environments/SM_Env_Tree_Pear_01", "Environments/SM_Env_Tree_Plum_01", "Environments/SM_Env_Tree_Cherry_01");
        static readonly string[] SmallTrees = Fs("Generic/SM_Generic_Tree_01", "Generic/SM_Generic_Tree_02", "Generic/SM_Generic_Tree_03", "Generic/SM_Generic_Tree_04");
        static readonly string[] Pines = Gs("Environment/SM_Gen_Env_Tree_Pine_01", "Environment/SM_Gen_Env_Tree_Pine_02", "Environment/SM_Gen_Env_Tree_Pine_03");
        static readonly string[] DeadTrees = { G("Environment/SM_Gen_Env_Tree_Dead_01"), F("Generic/SM_Generic_TreeDead_01") };
        static readonly string[] Stumps = { F("Environments/SM_Env_Tree_Stump_01"), F("Environments/SM_Env_Tree_Stump_02"), F("Environments/SM_Env_Tree_Stump_03"),
            G("Environment/SM_Gen_Env_Stump_01"), G("Environment/SM_Gen_Env_Stump_02"), G("Environment/SM_Gen_Env_Stump_03"), F("Generic/SM_Generic_TreeStump_01") };
        static readonly string[] SmallStumps = { F("Environments/SM_Env_Tree_Stump_02"), F("Environments/SM_Env_Tree_Stump_03"), G("Environment/SM_Gen_Env_Stump_03"), F("Generic/SM_Generic_TreeStump_01") };
        static readonly string[] Logs = Gs("Environment/SM_Gen_Env_Log_01", "Environment/SM_Gen_Env_Log_02");
        static readonly string[] Rocks = Gs("Environment/SM_Gen_Env_Rock_01", "Environment/SM_Gen_Env_Rock_02", "Environment/SM_Gen_Env_Rock_03", "Environment/SM_Gen_Env_Rock_04",
            "Environment/SM_Gen_Env_Rock_05", "Environment/SM_Gen_Env_Rock_06", "Environment/SM_Gen_Env_Rock_07", "Environment/SM_Gen_Env_Rock_08", "Environment/SM_Gen_Env_Rock_09", "Environment/SM_Gen_Env_Rock_10");
        static readonly string[] Pebbles = { F("Environments/SM_Env_Pebbles_01"), F("Environments/SM_Env_Pebbles_03"), G("Environment/SM_Gen_Env_Rock_Pebbles_01"),
            G("Environment/SM_Gen_Env_Rock_Pebbles_02"), G("Environment/SM_Gen_Env_Rock_Pebbles_03"), G("Environment/SM_Gen_Env_Rock_Pebbles_04") };
        static readonly string[] LowFlowers = { F("Environments/SM_Env_Flowers_01"), F("Environments/SM_Env_Flowers_02"), F("Environments/SM_Env_Flowers_03"),
            G("Environment/SM_Gen_Env_Flowers_01"), G("Environment/SM_Gen_Env_Flowers_02"), G("Environment/SM_Gen_Env_Flowers_03") };
        static readonly string[] TallFlowers = Gs("Environment/SM_Gen_Env_Flowers_04", "Environment/SM_Gen_Env_Flowers_05", "Environment/SM_Gen_Env_Flowers_06",
            "Environment/SM_Gen_Env_Flowers_07", "Environment/SM_Gen_Env_Flowers_08");
        static readonly string[] Sunflowers = Fs("Plants/SM_Prop_Sunflower_01", "Plants/SM_Prop_Sunflower_02");
        static readonly string[] Mushrooms = Gs("Environment/SM_Gen_Env_Mushroom_01", "Environment/SM_Gen_Env_Mushroom_02", "Environment/SM_Gen_Env_Mushroom_03");
        static readonly string[] Ferns = Gs("Environment/SM_Gen_Env_Fern_02", "Environment/SM_Gen_Env_Fern_03");
        // (the PolygonGeneric shrubs and bushes render as flat dark lumps with this project's lighting: ferns and tall grass take their place)
        static readonly string[] Shrubs = Gs("Environment/SM_Gen_Env_Fern_02", "Environment/SM_Gen_Env_Fern_03", "Environment/SM_Gen_Env_Grass_Tall_01", "Environment/SM_Gen_Env_Grass_Tall_02");
        static readonly string[] TallGrass = Gs("Environment/SM_Gen_Env_Grass_Tall_01", "Environment/SM_Gen_Env_Grass_Tall_02", "Environment/SM_Gen_Env_Grass_Tall_03");
        static readonly string[] SmallGrass = { G("Environment/SM_Gen_Env_Grass_03"), G("Environment/SM_Gen_Env_Grass_04"), F("Generic/SM_Generic_Grass_Patch_03") };
        static readonly string[] Reeds = Fs("Environments/SM_Env_Reeds_01", "Environments/SM_Env_Reeds_02", "Environments/SM_Env_Reeds_03");
        static readonly string[] Leaves = Gs("Environment/SM_Gen_Env_Leaves_01", "Environment/SM_Gen_Env_Leaves_02", "Environment/SM_Gen_Env_Leaves_03",
            "Environment/SM_Gen_Env_Leaves_Pile_01", "Environment/SM_Gen_Env_Leaves_Pile_02");
        static readonly string[] Twigs = Gs("Environment/SM_Gen_Env_Twig_01", "Environment/SM_Gen_Env_Twig_02", "Environment/SM_Gen_Env_Twig_03", "Environment/SM_Gen_Env_Twig_04");
        static readonly string[] Roots = Gs("Environment/SM_Gen_Env_Root_01", "Environment/SM_Gen_Env_Root_02");
        static readonly string[] RoundBales = Fs("Props/SM_Prop_Hay_Bale_Round_01", "Props/SM_Prop_Hay_Bale_Round_02", "Props/SM_Prop_Hay_Bale_Round_01", "Props/SM_Prop_Hay_Bale_Round_02_Green");
        static readonly string[] Beehives = Fs("Props/SM_Prop_Beehive_01", "Props/SM_Prop_Beehive_02", "Props/SM_Prop_Beehive_03", "Props/SM_Prop_Beehive_04", "Props/SM_Prop_Beehive_05", "Props/SM_Prop_Beehive_06");

        [MenuItem(Menu + "Dress Farm With Synty Props (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            data = terrain.terrainData;
            shore = PondRelocationV1.NewShoreline();
            rng = new System.Random(2610);
            discs.Clear(); lowRects.Clear(); tallRects.Clear(); gates.Clear(); spawns.Clear(); trunks.Clear(); woody.Clear(); stones.Clear();
            counts.Clear(); missing.Clear(); sizes.Clear(); prefabs.Clear();

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Dress farm with Synty props"); int undo = Undo.GetCurrentGroup();
            var old = root.transform.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            group = new GameObject(GroupName).transform;
            group.SetParent(root.transform, false);
            Physics.SyncTransforms();

            ReadGround();
            ReadScene(root.transform);

            // the small scenes first, they need the most room
            WoodcuttingCorner();
            BarnHay();
            PastureHay();
            LowerFieldBales();
            OrchardAndApiary();
            GardenCorner();
            JunkCorner();
            ProduceStand();
            Buildings();
            // then the nature round them
            TreesAndStumps();
            RockClusters();
            Undergrowth();
            FlowerClusters();
            MushroomClusters();
            IL3DNGrassClumps();

            Undo.RegisterCreatedObjectUndo(group.gameObject, "Dress farm with Synty props");
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(root.scene);

            var sb = new System.Text.StringBuilder("Farm dressed under " + GroupName + ": " + group.GetComponentsInChildren<Transform>().Length + " transforms.");
            foreach (var kv in counts) sb.Append("\n  " + kv.Key + ": " + kv.Value);
            foreach (var m in missing) sb.Append("\n  MISSING " + m);
            return sb.ToString();
        }

        // ================================================================================================================
        // Small scenes
        // ================================================================================================================

        static void WoodcuttingCorner()
        {
            var g = Sub("Woodcutting Corner");
            if (!FindSpot(new[] { new Vector2(94.5f, 3.5f), new Vector2(94f, -14f), new Vector2(66.5f, -31f), new Vector2(124f, 3f) }, 3.6f, 7f, out var c)) { Count("Woodcutting Corner (no room)"); return; }
            float yaw = Range(0, 360);
            var big = PutV(F("Environments/SM_Env_Tree_Stump_01"), g, c, yaw, 0, 0, Range(0, 360), .85f, .08f);
            var block = PutV(G("Environment/SM_Gen_Env_Stump_03"), g, c, yaw, 2.1f, -.5f, Range(0, 360), .9f, .05f);
            var sawStump = PutV(F("Environments/SM_Env_Tree_Stump_02"), g, c, yaw, -1.8f, 1.3f, Range(0, 360), 1f, .04f);
            PutV(F("Props/SM_Prop_Wood_Stack_01"), g, c, yaw, .5f, 2.6f, 8, 1f, .02f);
            PutV(F("Props/SM_Prop_Wood_Stack_02"), g, c, yaw, 2.5f, 2.0f, -14, 1f, .02f);
            PutV(G("Environment/SM_Gen_Env_Log_02"), g, c, yaw, -2.6f, -1.5f, 64, .72f, .1f, tilt: .8f);
            PutV(F("Props/SM_Prop_GasCan_01"), g, c, yaw, -2.7f, 2.3f, 35, 1f, 0f, collider: false);
            PutV(G("Props/SM_Gen_Prop_Plank_01"), g, c, yaw, 1.1f, -2.1f, 22, 1f, -.05f, tilt: 1f, collider: false);
            PutV(F("Props/SM_Prop_Wheelbarrow_Metal_01"), g, c, yaw, -.6f, -2.8f, 105, 1f, .02f);
            if (block != null)
            {
                // the axe driven into the chopping block, head down, handle up and leaning
                var b2 = Flat(block.transform.position);
                var axe = PutV(F("Props/SM_Prop_Tool_Axe_01"), g, c, yaw, 2.1f, -.5f, 0, 1f, 0, collider: false);
                if (axe != null)
                {
                    axe.transform.rotation = Quaternion.Euler(0, yaw + 40, 0)*Quaternion.Euler(22, 0, 180);
                    SitOn(axe, new Vector3(b2.x + .05f, TopAt(block, b2) - .12f, b2.y));
                }
            }
            if (sawStump != null)
            {
                var s2 = Flat(sawStump.transform.position);
                var saw = PutV(F("Props/SM_Prop_ChainSaw_01"), g, c, yaw, -1.8f, 1.3f, Range(0, 360), 1f, 0, collider: false);
                if (saw != null) SitOn(saw, new Vector3(s2.x, TopAt(sawStump, s2) - .01f, s2.y));
            }
            for (int i = 0; i < 6; i++) PutV(Pick(Twigs), g, c, yaw, Range(-3.2f, 3.2f), Range(-3.2f, 3.2f), Range(0, 360), Range(.8f, 1.2f), .01f, tilt: 1f, collider: false);
            if (big != null) { woody.Add(Flat(big.transform.position)); Cluster(g, Mushrooms, Flat(big.transform.position) + Polar(1.25f), 3, .45f, .35f, .55f, .6f); }
            Cluster(g, Ferns, c + Rotate(new Vector2(-2.9f, .2f), yaw), 1, .3f, .55f, .75f, .5f);
            Claim(c, 3.6f, Kind.Solid);
            Count("Woodcutting Corner");
        }

        static void BarnHay()
        {
            var g = Sub("Barn Hay");
            if (!FindSpot(new[] { new Vector2(89f, 33.5f), new Vector2(66.5f, 22.5f), new Vector2(94.5f, 31f), new Vector2(70f, 35f), new Vector2(121f, 33.5f) }, 2.8f, 4f, out var c)) { Count("Barn Hay (no room)"); return; }
            float yaw = Range(0, 360);
            var a = PutV(F("Props/SM_Prop_Hay_Bale_Square_01"), g, c, yaw, 0, -.72f, 0, 1f, .03f);
            var b = PutV(F("Props/SM_Prop_Hay_Bale_Square_01"), g, c, yaw, .05f, .72f, 2, 1f, .03f);
            if (a != null && b != null)
            {
                var top = PutV(F("Props/SM_Prop_Hay_Bale_Square_02"), g, c, yaw, 0, 0, 90 + Range(-6, 6), 1f, 0);
                if (top != null) SitOn(top, new Vector3(c.x, Mathf.Max(Top(a), Top(b)) - .04f, c.y));
            }
            PutV(F("Props/SM_Prop_Hay_Bale_Round_01"), g, c, yaw, 2.1f, .2f, Range(0, 360), 1f, .03f);
            PutV(F("Props/SM_Prop_GrainBag_01"), g, c, yaw, -1.7f, 1.1f, 20, 1f, .01f);
            PutV(F("Props/SM_Prop_GrainBag_Open_01"), g, c, yaw, -1.6f, -.5f, -30, 1f, .01f);
            PutV(G("Props/SM_Gen_Prop_Sack_Stack_01"), g, c, yaw, -1.9f, -1.8f, 10, .9f, .01f);
            var fork = PutV(F("Props/SM_Prop_Tool_Hayfork_01"), g, c, yaw, 1.2f, -1.7f, 0, 1f, 0, collider: false);
            if (fork != null) { fork.transform.rotation = Quaternion.Euler(0, yaw + 70, 0)*Quaternion.Euler(90, 0, 0); var f2 = Flat(fork.transform.position); SitOn(fork, new Vector3(f2.x, H(f2) - .01f, f2.y)); }
            Claim(c, 2.8f, Kind.Solid);
            Count("Barn Hay");
        }

        static void PastureHay()
        {
            var g = Sub("Pasture Hay");
            if (!FindSpot(new[] { new Vector2(101f, -31f), new Vector2(98f, -38f), new Vector2(116f, -43f), new Vector2(72f, -35f) }, 3.4f, 5f, out var c)) { Count("Pasture Hay (no room)"); return; }
            float yaw = Range(0, 360);
            PutV(F("Props/SM_Prop_Hay_Bale_Round_01"), g, c, yaw, 0, 0, Range(0, 360), 1f, .03f);
            PutV(F("Props/SM_Prop_Hay_Bale_Round_02"), g, c, yaw, 1.45f, .55f, Range(0, 360), 1f, .03f);
            PutV(F("Props/SM_Prop_Hay_Bale_Round_01_Green"), g, c, yaw, -.5f, 1.75f, Range(0, 360), .95f, .03f);
            PutV(F("Props/SM_Prop_Hay_Pile_01"), g, c, yaw, -2.2f, -1.2f, Range(0, 360), .85f, .12f);
            PutV(F("Props/SM_Prop_Hay_Bale_Square_03"), g, c, yaw, 1.7f, -1.6f, 15, 1f, .03f);
            var fork = PutV(F("Props/SM_Prop_Tool_Pitchfork_01"), g, c, yaw, .2f, -2.4f, 0, 1f, 0, collider: false);
            if (fork != null) { fork.transform.rotation = Quaternion.Euler(0, yaw - 20, 0)*Quaternion.Euler(90, 0, 0); var f2 = Flat(fork.transform.position); SitOn(fork, new Vector3(f2.x, H(f2) - .01f, f2.y)); }
            Claim(c, 3.4f, Kind.Solid);
            Count("Pasture Hay");
        }

        static void LowerFieldBales()
        {
            var g = Sub("Field Bales");
            var placed = new List<Vector2>();
            int bales = Scatter(7, 900, () => RandIn(LowerField), p =>
            {
                if (placed.Exists(q => (q - p).sqrMagnitude < 4.6f*4.6f)) return false;
                var go = TryPut(Pick(RoundBales), g, p, 1f, Kind.Solid, .2f, 2f, 16f, .03f, .3f, true);
                if (go == null) return false;
                placed.Add(p); return true;
            });
            Count("Field Bales (round)", bales);
            int rows = Scatter(2, 600, () => RandIn(LowerField), p =>
            {
                if (placed.Exists(q => (q - p).sqrMagnitude < 4.4f*4.4f)) return false;
                var go = TryPut(F("Props/SM_Prop_Hay_Pile_02"), g, p, 1f, Kind.Solid, .2f, 2f, 14f, .08f, .6f, true, Range(-12, 12) + (rng.NextDouble() < .5 ? 0 : 90));
                if (go == null) return false;
                placed.Add(p); return true;
            });
            Count("Field Bales (windrows)", rows);
            int crow = Scatter(1, 600, () => RandIn(LowerField), p =>
                !placed.Exists(q => (q - p).sqrMagnitude < 3.5f*3.5f) && TryPut(F("Props/SM_Prop_Scarecrow_01"), g, p, 1f, Kind.Solid, .2f, 2f, 14f, 0f, 0f, true, Range(150, 210)) != null);
            Count("Field Scarecrow", crow);
        }

        static void OrchardAndApiary()
        {
            var g = Sub("Orchard");
            var bag = new List<string>();
            var trees = new List<GameObject>();
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 5; col++)
                {
                    var grid = new Vector2(Mathf.Lerp(Orchard.xMin + 2.5f, Orchard.xMax - 2.5f, col/4f) + (row == 1 ? 2.4f : 0), Mathf.Lerp(Orchard.yMin + 2.4f, Orchard.yMax - 2.4f, row));
                    if (bag.Count == 0) { bag.AddRange(FruitTrees); Shuffle(bag); }
                    string sp = bag[0]; bag.RemoveAt(0);
                    for (int t = 0; t < 10; t++)
                    {
                        var p = grid + (t == 0 ? Rand2(.5f) : Rand2(1.8f));
                        var go = TryPut(sp, g, p, Range(.95f, 1.15f), Kind.Tree, .2f, 3f, 18f, .1f, 0f, true);
                        if (go != null) { trees.Add(go); trunks.Add(p); Count("Orchard Trees"); break; }
                    }
                }
            // a ladder leant against one of the trees, a wheelbarrow at the end of the rows
            foreach (var tree in trees)
            {
                var tp = Flat(tree.transform.position);
                var dir = Polar(1f).normalized;
                var foot = tp - dir*1.05f;
                if (!GroundOk(foot, .3f, .2f, 2f, 20f) || !Free(foot, .4f, Kind.Solid)) continue;
                var ladder = Put(G("Building/SM_Gen_Bld_Ladder_01"), g, foot, 0, 1f, 0, 0, true, Kind.Solid, .4f);
                var look = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                ladder.transform.rotation = look*Quaternion.Euler(18, 0, 0);
                ladder.transform.position = new Vector3(foot.x, H(foot) - .03f, foot.y) - look*Vector3.right*.3f;
                Count("Orchard Ladder"); break;
            }
            PlaceNear(F("Props/SM_Prop_Wheelbarrow_01"), g, new[] { new Vector2(Orchard.xMax - 1f, Orchard.center.y), new Vector2(Orchard.xMin + 1f, Orchard.center.y) }, 3f, 1f, Kind.Solid, .25f, 2f, 16f, .02f, .5f, "Orchard Wheelbarrow");
            // beehives at the sunny end of the orchard, flowers round them
            var h = Sub("Apiary");
            if (FindSpot(new[] { new Vector2(94f, -56.5f), new Vector2(61.8f, -55f), new Vector2(66f, -45f), new Vector2(95f, -43f) }, 2.6f, 3.5f, out var c))
            {
                float yaw = Range(0, 360);
                var hives = new List<string>(Beehives); Shuffle(hives);
                for (int i = 0; i < 4; i++) PutV(hives[i], h, c, yaw, -1.8f + i*1.2f, i == 1 || i == 2 ? .25f : 0, Range(-8, 8), 1f, .02f);
                Cluster(h, TallFlowers, c + Rotate(new Vector2(0, 1.9f), yaw), 5, .9f, .55f, .75f, .4f);
                Cluster(h, LowFlowers, c + Rotate(new Vector2(-2.2f, -1.3f), yaw), 4, .8f, .7f, .95f, .5f);
                Cluster(h, TallFlowers, c + Rotate(new Vector2(2.3f, -1.2f), yaw), 4, .8f, .55f, .75f, .4f);
                Claim(c, 2.6f, Kind.Solid);
                Count("Apiary");
            }
            else Count("Apiary (no room)");
        }

        static void GardenCorner()
        {
            var g = Sub("Garden Corner");
            if (!FindSpot(new[] { new Vector2(124.5f, 32.8f), new Vector2(125.6f, 13.2f), new Vector2(101.8f, 33f), new Vector2(88f, 34f), new Vector2(124f, -16f) }, 2.4f, 3f, out var c)) { Count("Garden Corner (no room)"); return; }
            float yaw = Range(0, 360);
            var table = PutV(F("Props/SM_Prop_Table_01"), g, c, yaw, 0, 0, 90, 1f, .02f);
            if (table != null)
            {
                var t2 = Flat(table.transform.position);
                var right = Rotate(new Vector2(1, 0), yaw);
                string[] onTable = { F("Props/SM_Prop_Pot_01"), F("Props/SM_Prop_Pot_01"), F("Props/SM_Prop_SeedPacket_01"), F("Props/SM_Prop_SeedPacket_02"), F("Props/SM_Prop_Pot_02") };
                for (int i = 0; i < onTable.Length; i++)
                {
                    var p = t2 + right*(-.95f + i*.47f) + Rotate(new Vector2(0, Range(-.2f, .2f)), yaw);
                    var item = PutV(onTable[i], g, p, 0, 0, 0, Range(0, 360), 1f, 0, collider: false);
                    if (item != null) SitOn(item, new Vector3(p.x, TopAt(table, p), p.y));
                }
            }
            PutV(F("Props/SM_Prop_Bench_01"), g, c, yaw, 0, -1.25f, 0, 1f, .02f);
            PutV(F("Props/SM_Prop_Watering_Can_01"), g, c, yaw, 1.6f, -.9f, 60, 1f, 0, collider: false);
            var barrel = PutV(F("Props/SM_Prop_Barrel_Half_01"), g, c, yaw, -1.75f, .8f, 0, .9f, .02f);
            if (barrel != null) PlantIn(g, barrel, LowFlowers, .6f, .22f);
            for (int i = 0; i < 2; i++)
            {
                var pot = PutV(G("Props/SM_Gen_Prop_Pot_01"), g, c, yaw, 1.55f + i*.45f, .95f - i*.55f, Range(0, 360), .8f, .02f);
                if (pot != null) PlantIn(g, pot, TallFlowers, .38f, .12f);
            }
            Claim(c, 2.4f, Kind.Solid);
            Count("Garden Corner");
        }

        static void JunkCorner()
        {
            var g = Sub("Junk Corner");
            if (!FindSpot(new[] { new Vector2(61.8f, -61.6f), new Vector2(126.5f, -61.5f), new Vector2(127f, -46f), new Vector2(52f, 29f) }, 2.5f, 3f, out var c)) { Count("Junk Corner (no room)"); return; }
            float yaw = Range(0, 360);
            var tyre = PutV(F("Props/SM_Prop_Tyre_01"), g, c, yaw, 0, 0, Range(0, 360), 1f, .02f);
            if (tyre != null) { var t2 = PutV(F("Props/SM_Prop_Tyre_01"), g, c, yaw, 0, 0, Range(0, 360), 1f, 0); if (t2 != null) SitOn(t2, new Vector3(c.x + .08f, Top(tyre) - .03f, c.y + .05f)); }
            PutV(F("Props/SM_Prop_SpoolWheel_01"), g, c, yaw, 1.9f, .7f, Range(0, 360), .9f, .02f);
            PutV(G("Props/SM_Gen_Prop_Barrel_Metal_01"), g, c, yaw, -1.6f, .8f, Range(0, 360), 1f, .02f);
            PutV(G("Props/SM_Gen_Prop_Barrel_Metal_02"), g, c, yaw, -1.3f, -.45f, Range(0, 360), 1f, .02f);
            PutV(F("Props/SM_Prop_GasCan_01"), g, c, yaw, .9f, -1.35f, 30, 1f, 0, collider: false);
            PutV(G("Props/SM_Gen_Prop_Crate_02"), g, c, yaw, 1.6f, -1f, 12, .9f, .02f);
            PutV(G("Props/SM_Gen_Prop_Plank_02"), g, c, yaw, -.2f, 1.7f, 75, 1f, -.04f, tilt: 1f, collider: false);
            Cluster(g, TallGrass, c + Rotate(new Vector2(-2.2f, -1.4f), yaw), 2, .5f, .5f, .7f, .4f);
            Claim(c, 2.5f, Kind.Solid);
            Count("Junk Corner");
        }

        static void ProduceStand()
        {
            var g = Sub("Produce Stand");
            if (!FindSpot(new[] { new Vector2(51.5f, 19.5f), new Vector2(52f, 4.5f), new Vector2(47f, 21f), new Vector2(46f, 4f) }, 3.3f, 4f, out var c, .15f)) { Count("Produce Stand (no room)"); return; }
            float yaw = FaceRoad(c);
            PutV(F("Buildings/SM_Bld_ProduceStand_01"), g, c, yaw, 0, 0, 0, 1f, .03f);
            string[] boxes = Fs("Plants/SM_Prop_Box_Apple_01", "Plants/SM_Prop_Box_Peach_01", "Plants/SM_Prop_Box_Plum_01", "Plants/SM_Prop_Box_Apple_02");
            for (int i = 0; i < boxes.Length; i++) PutV(boxes[i], g, c, yaw, -1.35f + i*.9f, 2.05f, Range(-6, 6), 1f, .01f);
            var potatoes = PutV(F("Plants/SM_Prop_Box_Potato_01"), g, c, yaw, -2.1f, .9f, 80, 1f, .01f);
            if (potatoes != null) { var p2 = Flat(potatoes.transform.position); var carrots = PutV(F("Plants/SM_Prop_Box_Carrot_01"), g, p2, 0, 0, 0, yaw + 84, 1f, 0); if (carrots != null) SitOn(carrots, new Vector3(p2.x, Top(potatoes) - .02f, p2.y)); }
            PutV(F("Plants/SM_Prop_Pumpkin_01_L"), g, c, yaw, -2.2f, -.4f, Range(0, 360), 1f, .02f);
            PutV(F("Plants/SM_Prop_Pumpkin_White_01_M"), g, c, yaw, -2.6f, .1f, Range(0, 360), 1f, .02f);
            PutV(F("Plants/SM_Prop_Squash_Butternut_01_L"), g, c, yaw, 2.1f, 1.1f, Range(0, 360), 1f, .02f);
            PutV(F("Props/SM_Prop_Barrel_Half_01"), g, c, yaw, 2.2f, -.5f, 0, .9f, .02f);
            PutV(F("Props/SM_Prop_Wheelbarrow_Metal_01"), g, c, yaw, 2.9f, 1.9f, 70, 1f, .02f);
            Claim(c, 3.3f, Kind.Solid);
            Count("Produce Stand");
        }

        static void Buildings()
        {
            var g = Sub("Buildings and Machines");
            PlaceNear(F("Buildings/SM_Bld_Silo_Small_01"), g, new[] { new Vector2(66.5f, 34f), new Vector2(90.5f, 34.5f), new Vector2(54f, 30f) }, 3f, 1f, Kind.Solid, .2f, 3f, 12f, .05f, 0f, "Small Silo");
            PlaceNear(F("Buildings/SM_Bld_Outhouse_01"), g, new[] { new Vector2(127f, -61.8f), new Vector2(127f, 34.3f), new Vector2(61.5f, -40f) }, 2.5f, 1f, Kind.Solid, .2f, 3f, 12f, .03f, 0f, "Outhouse");
            PlaceNear(F("Buildings/SM_Bld_Greenhouse_01"), g, new[] { new Vector2(47f, 31f), new Vector2(141f, 24f), new Vector2(47f, -22f) }, 6f, 1f, Kind.Solid, .15f, 3f, 10f, .05f, 0f, "Greenhouse");
            PlaceNear(F("Props/SM_Prop_Truck_Rusted_01"), g, new[] { new Vector2(141f, -34f), new Vector2(45f, -46f) }, 6f, 1f, Kind.Solid, .15f, 3f, 12f, .06f, .5f, "Rusted Truck");
            PlaceNear(F("Vehicles/SM_Veh_Attach_Plough_01"), g, new[] { new Vector2(48f, -8f), new Vector2(141f, -8f) }, 6f, 1f, Kind.Solid, .15f, 3f, 12f, .02f, .6f, "Plough");
            PlaceNear(F("Vehicles/SM_Veh_Attach_Trailer_Cage_01"), g, new[] { new Vector2(54f, 25f), new Vector2(142f, 12f) }, 5f, 1f, Kind.Solid, .15f, 3f, 12f, .02f, .5f, "Cage Trailer");
            PlaceNear(F("Props/SM_Prop_ClothesLine_01"), g, new[] { new Vector2(69f, 35f), new Vector2(92.5f, 34f), new Vector2(127f, 29f) }, 2.5f, 1f, Kind.Solid, .2f, 3f, 14f, .03f, 0f, "Clothes Line");
            PlaceNear(F("Props/SM_Prop_Rack_01"), g, new[] { new Vector2(81.5f, 34.5f), new Vector2(96f, 33.5f) }, 2.5f, 1f, Kind.Solid, .2f, 3f, 14f, -.5f, 0f, "Tool Rack");
        }

        // ================================================================================================================
        // Nature
        // ================================================================================================================

        static void TreesAndStumps()
        {
            var g = Sub("Trees");
            // a few trees inside the fences, near them, out of the way
            int inside = Scatter(11, 4000, () => RandIn(Farm), p =>
            {
                if (!InFarm(p) || !FenceNear(p, 4.5f) || FenceNear(p, 1.8f)) return false;
                double u = rng.NextDouble();
                string sp = u < .5 ? Pick(SmallTrees) : u < .85 ? Pick(FruitTrees) : Pick(Pines);
                float s = Array.IndexOf(Pines, sp) >= 0 ? Range(.7f, .85f) : Array.IndexOf(SmallTrees, sp) >= 0 ? Range(1.1f, 1.5f) : Range(.9f, 1.1f);
                var go = TryPut(sp, g, p, s, Kind.Tree, .15f, 3f, 16f, .12f, 0f, true);
                if (go == null) return false;
                trunks.Add(p); return true;
            });
            Count("Trees (inside)", inside);
            int outside = Scatter(26, 6000, () => RandIn(Outer), p =>
            {
                if (!OutFarm(p)) return false;
                double u = rng.NextDouble();
                string sp = u < .3 ? Pick(Pines) : u < .72 ? Pick(SmallTrees) : u < .93 ? Pick(FruitTrees) : Pick(DeadTrees);
                float s = Array.IndexOf(SmallTrees, sp) >= 0 ? Range(1.3f, 1.9f) : Range(.8f, 1.1f);
                var go = TryPut(sp, g, p, s, Kind.Tree, .15f, 4f, 20f, .12f, 0f, true);
                if (go == null) return false;
                trunks.Add(p);
                if (rng.NextDouble() < .45) { var r = Put(Pick(Roots), g, p + Polar(Range(.15f, .35f)), Range(0, 360), Range(.7f, 1f), .06f, .6f, false, Kind.Decor, .3f); if (r != null) Count("Tree Roots"); }
                return true;
            });
            Count("Trees (outside)", outside);
            int large = Scatter(1, 2000, () => RandIn(Outer), p => OutFarm(p) && TryPut(F("Environments/SM_Env_Tree_Large_01"), g, p, Range(.75f, .85f), Kind.Tree, .15f, 5f, 14f, .15f, 0f, true) != null && Add(trunks, p));
            Count("Trees (large)", large);

            var s2 = Sub("Stumps and Logs");
            int stumpsIn = Scatter(7, 3000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 3f) && !FenceNear(p, 1f)
                && TryPut(Pick(SmallStumps), s2, p, Range(.85f, 1.1f), Kind.Solid, .15f, 2f, 20f, .05f, .3f, true) != null && Add(woody, p));
            Count("Stumps (inside)", stumpsIn);
            int stumpsOut = Scatter(12, 4000, () => RandIn(Outer), p => OutFarm(p)
                && TryPut(Pick(Stumps), s2, p, Range(.85f, 1.15f), Kind.Solid, .15f, 3f, 22f, .06f, .3f, true) != null && Add(woody, p));
            Count("Stumps (outside)", stumpsOut);
            int logsOut = Scatter(8, 4000, () => RandIn(Outer), p => OutFarm(p)
                && TryPut(Pick(Logs), s2, p, Range(.6f, .85f), Kind.Solid, .15f, 3f, 16f, .1f, .8f, true) != null && Add(woody, p));
            Count("Logs (outside)", logsOut);
            int logsIn = Scatter(2, 3000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 3f) && !FenceNear(p, 1.2f)
                && TryPut(Logs[1], s2, p, Range(.55f, .65f), Kind.Solid, .15f, 2f, 14f, .08f, .8f, true) != null && Add(woody, p));
            Count("Logs (inside)", logsIn);
        }

        static void RockClusters()
        {
            var g = Sub("Rocks");
            int inside = Scatter(13, 5000, () => RandIn(Farm), p =>
            {
                if (!InFarm(p)) return false;
                bool bank = Pond(p) > .8f && Pond(p) < 3.5f;
                if (!bank && !(FenceNear(p, 2.6f) && !FenceNear(p, .9f)) && rng.NextDouble() > .2) return false;
                return RockCluster(g, p, Range(.2f, .34f), .8f);
            });
            Count("Rock Clusters (inside)", inside);
            int outside = Scatter(18, 5000, () => RandIn(Outer), p => OutFarm(p) && RockCluster(g, p, Range(.32f, .6f), 1.5f));
            Count("Rock Clusters (outside)", outside);
        }

        static bool RockCluster(Transform g, Vector2 p, float scale, float pondGap)
        {
            var main = TryPut(Pick(Rocks), g, p, scale, Kind.Solid, .25f, pondGap, 24f, .12f*scale*2f, .5f, true);
            if (main == null) return false;
            stones.Add(p);
            int n = rng.Next(1, 3);
            for (int i = 0; i < n; i++)
            {
                var q = p + Polar(Range(.7f, 1.4f))*Mathf.Max(1f, scale*2.2f);
                TryPut(Pick(Rocks), g, q, scale*Range(.3f, .5f), Kind.Solid, .3f, pondGap, 26f, .05f, .6f, false);
            }
            for (int i = 0; i < 2; i++) TryPut(Pick(Pebbles), g, p + Polar(Range(.8f, 1.8f)), Range(.7f, 1f), Kind.Decor, .35f, .4f, 30f, .02f, 1f, false);
            if (rng.NextDouble() < .6) Cluster(g, SmallGrass, p + Polar(Range(.7f, 1.3f))*Mathf.Max(1f, scale*2f), rng.Next(1, 3), .4f, .55f, .8f, .5f);
            return true;
        }

        static void Undergrowth()
        {
            var g = Sub("Undergrowth");
            Count("Ferns", Scatter(26, 3000, () => Around(trunks, 1.1f, 2.6f), p => Cluster(g, Ferns, p, rng.Next(1, 3), .5f, .5f, .8f, .5f) > 0));
            Count("Shrubs (rocks)", Scatter(9, 3000, () => Around(stones, 1f, 2f), p => Cluster(g, Shrubs, p, 1, .2f, .6f, .9f, .3f) > 0));
            Count("Shrubs (fences)", Scatter(9, 3000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 1.4f) && !FenceNear(p, .5f) && Cluster(g, Shrubs, p, 1, .2f, .6f, .9f, .3f) > 0));
            Count("Shrubs and Ferns (round the fences)", Scatter(22, 5000, () => RandIn(Outer), p => OutFarm(p) && FenceNear(p, 3.5f) && !FenceNear(p, 1.2f)
                && Cluster(g, rng.NextDouble() < .6 ? Shrubs : Ferns, p, rng.Next(1, 3), .7f, .7f, 1.05f, .3f) > 0));
            Count("Tall Grass (pond)", Scatter(12, 3000, () => RandIn(Rect.MinMaxRect(80, -20, 130, 12)), p => Pond(p) > .45f && Pond(p) < 2.2f
                && Cluster(g, TallGrass, p, rng.Next(1, 3), .5f, .45f, .7f, .4f, .3f) > 0));
            Count("Tall Grass (fences)", Scatter(14, 3000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 1.2f) && !FenceNear(p, .35f)
                && Cluster(g, TallGrass, p, 1, .3f, .45f, .65f, .4f) > 0));
            Count("Reeds (pond edge)", Scatter(6, 3000, () => RandIn(Rect.MinMaxRect(80, -20, 130, 12)), p => Pond(p) > -.5f && Pond(p) < .3f
                && Cluster(g, Reeds, p, rng.Next(1, 3), .5f, .5f, .7f, .2f, -1f) > 0));
            Count("Fallen Leaves", Scatter(16, 3000, () => Around(trunks, .6f, 2f), p => TryPut(Pick(Leaves), g, p, Range(.5f, .8f), Kind.Decor, .3f, 1.5f, 24f, .02f, 1f, false) != null));
            Count("Twigs", Scatter(24, 3000, () => rng.NextDouble() < .7 ? Around(trunks, .8f, 3f) : Around(woody, .6f, 2f), p => TryPut(Pick(Twigs), g, p, Range(.8f, 1.3f), Kind.Decor, .35f, 1f, 30f, .01f, 1f, false) != null));
            var sf = Sub("Sunflowers");
            Count("Sunflower Patches", Scatter(3, 3000, () => RandIn(Rect.MinMaxRect(60, 10, 99, 35.5f)), p => FenceNear(p, 2f) && !FenceNear(p, .6f)
                && Cluster(sf, Sunflowers, p, rng.Next(5, 9), 1.2f, .85f, 1.1f, .1f, 1.5f, oneSpecies: false) >= 4));
        }

        static void FlowerClusters()
        {
            var g = Sub("Flowers");
            Count("Flower Clusters (fences)", Scatter(20, 4000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 1.8f) && !FenceNear(p, .45f) && FlowerCluster(g, p)));
            Count("Flower Clusters (trees, rocks, stumps)", Scatter(14, 3000, () => rng.NextDouble() < .5 ? Around(trunks, 1.2f, 3f) : Around(stones, 1f, 2.2f), p => FlowerCluster(g, p)));
            Count("Flower Clusters (pond bank)", Scatter(8, 3000, () => RandIn(Rect.MinMaxRect(80, -20, 130, 12)), p => Pond(p) > 1f && Pond(p) < 3.5f && FlowerCluster(g, p)));
            Count("Flower Clusters (meadows)", Scatter(14, 4000, () => RandIn(Farm), p => InFarm(p) && !FenceNear(p, 2f) && FlowerCluster(g, p)));
            Count("Flower Clusters (outside)", Scatter(26, 4000, () => RandIn(Outer), p => OutFarm(p) && FlowerCluster(g, p)));
        }

        static bool FlowerCluster(Transform g, Vector2 p)
        {
            bool tall = rng.NextDouble() < .35;
            return tall ? Cluster(g, TallFlowers, p, rng.Next(3, 6), .9f, .5f, .72f, .35f) >= 2
                        : Cluster(g, LowFlowers, p, rng.Next(3, 7), 1.1f, .65f, .95f, .5f) >= 2;
        }

        static void MushroomClusters()
        {
            var g = Sub("Mushrooms");
            Count("Mushroom Clusters (tree feet)", Scatter(26, 3000, () => Around(trunks, .7f, 1.6f), p => Cluster(g, Mushrooms, p, rng.Next(2, 6), .5f, .3f, .55f, .6f) >= 2));
            Count("Mushroom Clusters (stumps, logs)", Scatter(14, 3000, () => Around(woody, .7f, 1.4f), p => Cluster(g, Mushrooms, p, rng.Next(2, 5), .4f, .3f, .5f, .6f) >= 2));
            Count("Mushroom Clusters (shade by fences)", Scatter(8, 3000, () => RandIn(Farm), p => InFarm(p) && FenceNear(p, 1.2f) && !FenceNear(p, .4f)
                && Cluster(g, Mushrooms, p, rng.Next(2, 4), .35f, .28f, .45f, .6f) >= 2));
        }

        static void IL3DNGrassClumps()
        {
            var g = Sub("IL3DN Grass");
            var around = Rect.MinMaxRect(Farm.xMin - 3, Farm.yMin - 3, Farm.xMax + 3, Farm.yMax + 3);
            Count("IL3DN Grass (fences)", Scatter(90, 9000, () => RandIn(around), p =>
                !seedYard.Contains(p) && FenceNear(p, 1.4f) && !FenceNear(p, .35f) && GrassClump(g, p, rng.Next(2, 5), 1.3f) > 0));
            Count("IL3DN Grass (rocks, stumps, trees)", Scatter(80, 6000, () => rng.NextDouble() < .4 ? Around(stones, .9f, 1.9f) : rng.NextDouble() < .5 ? Around(woody, .8f, 1.7f) : Around(trunks, .9f, 2.6f),
                p => GrassClump(g, p, rng.Next(2, 4), 1f) > 0));
            Count("IL3DN Grass (pond bank)", Scatter(26, 4000, () => RandIn(Rect.MinMaxRect(80, -20, 130, 12)), p => Pond(p) > .5f && Pond(p) < 3.2f && GrassClump(g, p, rng.Next(2, 4), 1.1f) > 0));
            // the meadows themselves: soft drifts of grass all over the farm and round it, thicker in patches
            Count("IL3DN Grass (meadow drifts, inside)", Scatter(230, 14000, () => RandIn(Farm), p => InFarm(p) && Drift(p) > rng.NextDouble()
                && GrassClump(g, p, rng.Next(3, 7), 1.9f, .08f) > 1));
            Count("IL3DN Grass (meadow drifts, outside)", Scatter(190, 14000, () => RandIn(Outer), p => OutFarm(p) && Drift(p) > rng.NextDouble()
                && GrassClump(g, p, rng.Next(3, 7), 2.1f, .1f) > 1));
        }

        // where the grass grows thicker: soft patches over the whole map, never quite empty
        static float Drift(Vector2 p) => .25f + .75f*Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, .65f, Mathf.PerlinNoise(p.x/13f + 41.3f, p.y/13f - 17.9f)));

        static int GrassClump(Transform g, Vector2 c, int n, float radius, float maxPath = .15f)
        {
            int placed = 0;
            for (int t = 0; t < n*6 && placed < n; t++)
            {
                var p = t == 0 ? c : c + Rand2(radius);
                int k = rng.Next(2);
                float s = k == 0 ? Range(.7f, 1.1f) : Range(.6f, .9f);
                if (seedYard.Contains(p)) continue;
                var go = TryPut(IL3DNGrass[k], g, p, s, Kind.Decor, maxPath, .6f, 30f, .02f, .4f, false, -1, false, .55f);
                if (go != null) placed++;
            }
            if (placed > 0) Count("IL3DN Grass clumps", placed);
            return placed;
        }

        // ================================================================================================================
        // Placement
        // ================================================================================================================

        static int Cluster(Transform g, string[] pool, Vector2 c, int n, float radius, float sMin, float sMax, float tilt, float pondGap = 1f, bool oneSpecies = true)
        {
            string sp = Pick(pool); int placed = 0;
            for (int t = 0; t < n*6 && placed < n; t++)
            {
                var p = t == 0 ? c : c + Rand2(radius);
                if (TryPut(oneSpecies ? sp : Pick(pool), g, p, Range(sMin, sMax), Kind.Decor, .18f, pondGap, 30f, .02f, tilt, false) != null) placed++;
            }
            return placed;
        }

        static GameObject TryPut(string path, Transform parent, Vector2 p, float scale, Kind kind, float maxPath, float pondGap, float maxSlope, float sink, float tilt, bool collider, float yaw = -1, bool isStatic = true, float spacing = 1f)
        {
            if (Load(path) == null) return null;
            float r = Rad(path, scale)*spacing;
            float groundR = kind == Kind.Tree ? .7f : kind == Kind.Decor ? r*.4f : r*.75f;
            if (!GroundOk(p, groundR, maxPath, pondGap, maxSlope)) return null;
            if (!Free(p, r, kind)) return null;
            return Put(path, parent, p, yaw < 0 ? Range(0, 360) : yaw, scale, sink, tilt, collider, kind, r, true, isStatic);
        }

        static void PlaceNear(string path, Transform parent, Vector2[] candidates, float search, float scale, Kind kind, float maxPath, float pondGap, float maxSlope, float sink, float tilt, string label)
        {
            foreach (var c in candidates)
                for (int t = 0; t < 80; t++)
                {
                    var p = t == 0 ? c : c + Rand2(search);
                    if (!Outer.Contains(p)) continue;
                    if (TryPut(path, parent, p, scale, kind, maxPath, pondGap, maxSlope, sink, tilt, true) != null) { Count(label); return; }
                }
            Count(label + " (no room)");
        }

        // an item of a small scene, at a local offset from its centre; skipped where the ground is unfit
        static GameObject PutV(string path, Transform parent, Vector2 c, float yaw, float lx, float lz, float localYaw, float scale, float sink, float tilt = 0f, bool collider = true)
        {
            var p = c + Rotate(new Vector2(lx, lz), yaw);
            if (Load(path) == null || !GroundOk(p, 0, .4f, .4f, 30f)) return null;
            return Put(path, parent, p, yaw + localYaw, scale, sink, tilt, collider, Kind.Decor, 0, false);
        }

        static GameObject Put(string path, Transform parent, Vector2 p, float yaw, float scale, float sink, float tilt, bool collider, Kind kind, float r, bool addDisc = true, bool isStatic = true)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var up = Vector3.Slerp(Vector3.up, Normal(p), tilt);
            go.transform.SetPositionAndRotation(new Vector3(p.x, H(p) - sink, p.y), Quaternion.FromToRotation(Vector3.up, up)*Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = prefab.transform.localScale*scale;
            if (!collider) foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            if (addDisc) discs.Add(new Disc { c = p, r = r, k = kind });
            Physics.SyncTransforms();
            Count("_objects");
            return go;
        }

        // Is there room for something of radius r here?
        static bool Free(Vector2 p, float r, Kind k)
        {
            foreach (var gp in gates) if ((gp - p).sqrMagnitude < Sq(k == Kind.Decor ? 2.4f : 4.4f + r*.5f)) return false;
            foreach (var sp in spawns) if ((sp - p).sqrMagnitude < Sq(k == Kind.Decor ? 1.6f : 2.6f + r*.5f)) return false;
            float lowM = k == Kind.Decor ? r*.3f : r*.9f;
            foreach (var q in lowRects) if (RectDist(q, p) < lowM) return false;
            if (k == Kind.Tree) foreach (var q in tallRects) if (RectDist(q, p) < r*.8f) return false;
            if (k == Kind.Solid && r > 1.5f) foreach (var q in tallRects) if (RectDist(q, p) < r*.6f) return false;
            foreach (var d in discs)
            {
                float dist = (d.c - p).magnitude, need;
                if (k == Kind.Decor) need = d.k == Kind.Decor ? (d.r + r)*.6f : d.k == Kind.Tree ? .7f : d.r*.75f + r*.2f;
                else if (k == Kind.Tree) need = d.k == Kind.Tree ? (d.r + r)*.85f : d.k == Kind.Solid ? d.r + 1.2f : 0f;
                else need = d.k == Kind.Decor ? 0f : d.k == Kind.Tree ? 1.2f + r : (d.r + r)*.95f;
                if (dist < need) return false;
            }
            if (k == Kind.Decor) return CollidersFree(p, Mathf.Max(.1f, r*.3f), .3f);
            if (k == Kind.Tree) return CollidersFree(p, 1.1f, 2.5f);
            return CollidersFree(p, r*.85f, 1.4f);
        }

        static bool CollidersFree(Vector2 p, float r, float h)
        {
            float y = H(p);
            int n = Physics.OverlapCapsuleNonAlloc(new Vector3(p.x, y + .08f + r, p.y), new Vector3(p.x, y + .08f + Mathf.Max(h, r), p.y), r, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = buffer[i];
                if (c is TerrainCollider) continue;
                if ((flock != null && c.transform.IsChildOf(flock)) || (chick != null && c.transform.IsChildOf(chick))) continue;
                return false;
            }
            return true;
        }

        // Is the ground fit: on the terrain, off the paths and the tilled field, clear of the water and the seed yard, not steep?
        static bool GroundOk(Vector2 p, float r, float maxPath, float pondGap, float maxSlope)
        {
            var tp = terrain.transform.position;
            if (p.x < tp.x + 1 || p.y < tp.z + 1 || p.x > tp.x + data.size.x - 1 || p.y > tp.z + data.size.z - 1) return false;
            if (seedYard.Overlaps(new Rect(p.x - r, p.y - r, 2*r, 2*r))) return false;
            if (Pond(p) - r < pondGap) return false;
            for (int i = 0; i < 9; i++)
            {
                var q = i == 0 ? p : p + new Vector2(Mathf.Cos(i*Mathf.PI/4), Mathf.Sin(i*Mathf.PI/4))*r;
                Cell(q, out int x, out int z);
                if (pathMap[z, x] > maxPath || tillMap[z, x] > .12f) return false;
            }
            return Vector3.Angle(Vector3.up, Normal(p)) <= maxSlope;
        }

        static bool FindSpot(Vector2[] candidates, float r, float search, out Vector2 spot, float maxPath = .2f)
        {
            foreach (var c in candidates)
                for (int t = 0; t < 150; t++)
                {
                    var p = t == 0 ? c : c + Rand2(search);
                    if (!GroundOk(p, r, maxPath, 2f, 14f) || !Free(p, r, Kind.Solid)) continue;
                    spot = p; return true;
                }
            spot = default; return false;
        }

        static void Claim(Vector2 c, float r, Kind k) => discs.Add(new Disc { c = c, r = r, k = k });

        static void PlantIn(Transform g, GameObject holder, string[] pool, float scale, float sinkBelowTop)
        {
            var h2 = Flat(holder.transform.position);
            var f = Put(Pick(pool), g, h2, Range(0, 360), scale, 0, 0, false, Kind.Decor, 0, false);
            if (f != null) SitOn(f, new Vector3(h2.x, Top(holder) - sinkBelowTop, h2.y));
        }

        // Turns a scene so that its front (local +z) looks at the nearest path.
        static float FaceRoad(Vector2 c)
        {
            float best = float.MaxValue, yaw = 0;
            for (int a = 0; a < 24; a++)
            {
                var dir = new Vector2(Mathf.Sin(a*15*Mathf.Deg2Rad), Mathf.Cos(a*15*Mathf.Deg2Rad));
                for (float d = 1; d < 14; d += .5f)
                {
                    Cell(c + dir*d, out int x, out int z);
                    if (pathMap[z, x] > .5f) { if (d < best) { best = d; yaw = a*15; } break; }
                }
            }
            return yaw;
        }

        // ================================================================================================================
        // Reading the farm
        // ================================================================================================================

        static void ReadGround()
        {
            res = data.alphamapResolution;
            int layers = data.alphamapLayers;
            pathMap = new float[res, res]; tillMap = new float[res, res];
            for (int z0 = 0; z0 < res; z0 += 64)
            {
                int rows = Mathf.Min(64, res - z0);
                var a = data.GetAlphamaps(0, z0, res, rows);
                for (int z = 0; z < rows; z++)
                    for (int x = 0; x < res; x++)
                    {
                        float path = a[z, x, 4] + a[z, x, 5] + .6f*a[z, x, 6];
                        if (layers > 19) path += a[z, x, 19];
                        if (layers > 14) path += .5f*a[z, x, 14];
                        pathMap[z0 + z, x] = path;
                        tillMap[z0 + z, x] = a[z, x, 8];
                    }
            }
        }

        static void ReadScene(Transform root)
        {
            flock = root.Find("07 Sheep Pasture/Flock");
            var player = GameObject.Find("ChickPlayer"); chick = player != null ? player.transform : null;
            var skip = new List<Transform> { group, terrain.transform };
            foreach (var n in new[] { "09 Small Stones", "Chicken Yard Point Grass", "07 Sheep Pasture/Wildflowers", "South Mountain - distant scenery" }) { var t = root.Find(n); if (t != null) skip.Add(t); }
            foreach (var n in new[] { "Spawn - Mixed Foraging Seeds" })
            {
                var s = GameObject.Find(n);
                if (s == null) continue;
                skip.Add(s.transform);
                // keep the seed yard round wherever the seeds lie
                foreach (Transform c in s.transform) if (Outer.Contains(Flat(c.position))) seedYard = Encapsulate(seedYard, Flat(c.position));
            }
            seedYard = Rect.MinMaxRect(seedYard.xMin - 1, seedYard.yMin - 1, seedYard.xMax + 1, seedYard.yMax + 1);

            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                var t = r.transform;
                if (skip.Exists(s => t.IsChildOf(s))) continue;
                if (Path(t).Contains("Fence")) continue;                  // the fences are found by their colliders
                string n = t.name;
                if (n.StartsWith("Meadow") || n.StartsWith("Flower Border") || n.StartsWith("Exterior") || n.StartsWith("Hill") || n.StartsWith("Mountain")) continue;
                var b = r.bounds;
                if (!Outer.Overlaps(new Rect(b.min.x, b.min.z, b.size.x, b.size.z))) continue;
                float wide = Mathf.Max(b.size.x, b.size.z);
                if (wide > 18f || (wide < .5f && b.size.y < .5f)) continue;
                var rect = Rect.MinMaxRect(b.min.x + b.size.x*.08f, b.min.z + b.size.z*.08f, b.max.x - b.size.x*.08f, b.max.z - b.size.z*.08f);
                if (b.size.y > 3.2f) tallRects.Add(rect); else lowRects.Add(rect);
                // the feet of the trees already there, for the undergrowth
                if (b.size.y > 3.2f && (n.Contains("Tree") || n.Contains("Fir") || n.Contains("Pine")) && wide < 12f)
                {
                    var foot = Flat(t.position);
                    if (!trunks.Exists(q => (q - foot).sqrMagnitude < 1f)) trunks.Add(foot);
                }
                if (n.StartsWith("Scenery Rock") && !stones.Exists(q => (q - Flat(t.position)).sqrMagnitude < 1f)) stones.Add(Flat(t.position));
                if (n.Contains("Stump") && !woody.Exists(q => (q - Flat(t.position)).sqrMagnitude < 1f)) woody.Add(Flat(t.position));
            }
            // gates and spawn points stay clear
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Gate") && t.parent != null && !t.parent.name.StartsWith("Gate"))
                {
                    var rs = t.GetComponentsInChildren<Renderer>();
                    gates.Add(rs.Length > 0 ? Flat(rs[0].bounds.center) : Flat(t.position));
                }
            var sp = root.Find("Developer Spawn Points");
            if (sp != null) foreach (Transform t in sp) spawns.Add(Flat(t.position));
            if (chick != null) spawns.Add(Flat(chick.position));
            Count("_existing low obstacles", lowRects.Count); Count("_existing tall obstacles", tallRects.Count);
            Count("_gates", gates.Count); Count("_tree feet found", trunks.Count);
            Count("_seed yard " + seedYard.xMin.ToString("F0") + ".." + seedYard.xMax.ToString("F0") + " x " + seedYard.yMin.ToString("F0") + ".." + seedYard.yMax.ToString("F0"), 1);
        }

        // ================================================================================================================
        // Helpers
        // ================================================================================================================

        static bool InFarm(Vector2 p) => Farm.Contains(p) && !seedYard.Contains(p);
        static bool OutFarm(Vector2 p) => Outer.Contains(p) && !Rect.MinMaxRect(Farm.xMin - 1.2f, Farm.yMin - 1.2f, Farm.xMax + 1.2f, Farm.yMax + 1.2f).Contains(p);

        static bool FenceNear(Vector2 p, float d)
        {
            float y = H(p);
            int n = Physics.OverlapSphereNonAlloc(new Vector3(p.x, y + .5f, p.y), d, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (Path(buffer[i].transform).Contains("Fence")) return true;
            return false;
        }

        static float Pond(Vector2 p) => FarmPondV1.Signed(shore, p.x, p.y);
        static float H(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;
        static Vector3 Normal(Vector2 p)
        {
            var tp = terrain.transform.position;
            return data.GetInterpolatedNormal((p.x - tp.x)/data.size.x, (p.y - tp.z)/data.size.z);
        }
        static void Cell(Vector2 p, out int x, out int z)
        {
            var tp = terrain.transform.position;
            x = Mathf.Clamp(Mathf.FloorToInt((p.x - tp.x)/data.size.x*res), 0, res - 1);
            z = Mathf.Clamp(Mathf.FloorToInt((p.y - tp.z)/data.size.z*res), 0, res - 1);
        }

        static GameObject Load(string path)
        {
            if (!prefabs.TryGetValue(path, out var go)) { go = AssetDatabase.LoadAssetAtPath<GameObject>(path); prefabs[path] = go; if (go == null) missing.Add(path); }
            return go;
        }

        static Vector3 Size(string path)
        {
            if (sizes.TryGetValue(path, out var s)) return s;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load(path));
            go.transform.position = new Vector3(0, -900, 0);
            s = Bounds(go).size;
            Object.DestroyImmediate(go);
            sizes[path] = s;
            return s;
        }
        static float Rad(string path, float scale) { var s = Size(path); return .5f*Mathf.Max(s.x, s.z)*scale; }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
        static float Top(GameObject go) => Bounds(go).max.y;

        // the highest point of an object's colliders straight above p (its renderer top if it has none there)
        static float TopAt(GameObject go, Vector2 p)
        {
            Physics.SyncTransforms();
            float best = float.MinValue;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, Top(go) + 2, p.y), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(go.transform)) best = Mathf.Max(best, hit.point.y);
            return best > float.MinValue ? best : Top(go);
        }

        // moves an object so that the bottom centre of its bounds sits at 'at'
        static void SitOn(GameObject go, Vector3 at)
        {
            var b = Bounds(go);
            go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
            Physics.SyncTransforms();
        }

        static Transform Sub(string name)
        {
            var t = group.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(group, false); }
            return t;
        }

        static int Scatter(int want, int tries, Func<Vector2> sample, Func<Vector2, bool> place)
        {
            int got = 0;
            for (int t = 0; t < tries && got < want; t++) if (place(sample())) got++;
            return got;
        }

        static Vector2 Around(List<Vector2> anchors, float rMin, float rMax) => anchors.Count == 0 ? new Vector2(-9999, -9999) : anchors[rng.Next(anchors.Count)] + Polar(Range(rMin, rMax));
        static Vector2 RandIn(Rect r) => new Vector2(Range(r.xMin, r.xMax), Range(r.yMin, r.yMax));
        static Vector2 Rand2(float r) => Polar(r*Mathf.Sqrt((float)rng.NextDouble()));
        static Vector2 Polar(float d) { float a = Range(0, Mathf.PI*2); return new Vector2(Mathf.Cos(a), Mathf.Sin(a))*d; }
        static Vector2 Rotate(Vector2 v, float yaw) { var r = Quaternion.Euler(0, yaw, 0)*new Vector3(v.x, 0, v.y); return new Vector2(r.x, r.z); }
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
        static float Range(float a, float b) => a + (float)rng.NextDouble()*(b - a);
        static T Pick<T>(IList<T> list) => list[rng.Next(list.Count)];
        static float Sq(float v) => v*v;
        static bool Add(List<Vector2> list, Vector2 p) { list.Add(p); return true; }
        static void Shuffle<T>(List<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); } }
        static Rect Encapsulate(Rect r, Vector2 p) => Rect.MinMaxRect(Mathf.Min(r.xMin, p.x), Mathf.Min(r.yMin, p.y), Mathf.Max(r.xMax, p.x), Mathf.Max(r.yMax, p.y));
        static float RectDist(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, 0, p.y - r.yMax);
            return Mathf.Sqrt(dx*dx + dz*dz);
        }
        static string Path(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
        static void Count(string label, int n = 1) { counts.TryGetValue(label, out int c); counts[label] = c + n; }
    }
}
