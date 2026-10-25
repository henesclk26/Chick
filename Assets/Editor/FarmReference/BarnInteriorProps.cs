using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Furnishes the inside of the hand-painted barn (HandPaintedBarn) with Synty POLYGON Farm / Generic props so it no
/// longer looks bare: a stack of hay bales and a loose hay pile against the shut back doors, a workbench with a
/// toolbox, bucket, seed packet and bottle on it and a wall rack over it, garden tools hanging on the long wall,
/// barrels and a water tub, a wall lantern, a sack stack and a wheelbarrow in the corners by the open doors, and in
/// the side room a feeding trough, round bales, crates of produce, a pumpkin, grain bags and a barrel. Over the open
/// front doors a hayloft (plank floor, joists, girder on two posts with knee braces, railing) with a ladder up to it
/// and hay bales on it. The whole inside (hall and side room) gets a solid board floor painted like the inside walls
/// (T_BarnFloor), the grass and pebbles
/// that stood there are cleared, and every prop stands on the boards. The two wall torches burn: flame and ember particles and a flickering light (TorchFlame).
/// The middle of the hall (the way in from the open doors) and the middle of the side room stay free for the chick.
/// Positions are in the barn's own space (x: back doors -5.5 .. open front doors +5.5; z: long wall -3.15 .. side
/// room's outer wall +6.3). Each prop is fitted by its actual mesh bounds: stood on the floor (the ground under it),
/// put on top of another prop, or hung on a wall found by raycasting the barn's collider, so Synty's varied pivots do
/// not matter. A prop that would overlap something already in the barn is left out and reported.
/// Props that "Collect Unused Assets" moved to Assets/_Unused are moved back first (with the meshes they use), so they
/// are part of the project again. Everything goes under "Barn Interior Props" in the barn; running the menu again
/// rebuilds it. Survey pictures go to Temp/BarnFurnish.
/// </summary>
public static class BarnInteriorProps
{
    private const string BarnName = "SM_Bld_Barn_01";
    private const string GroupName = "Barn Interior Props";
    private const string UnusedManifest = "Assets/_Unused/_moved.txt";
    private const string Out = "Temp/BarnFurnish";

    // The barn's inside, its own space.
    private const float BackWallX = -5.5f, FrontWallX = 5.5f, LongWallZ = -3.15f, SideRoomZ = 3.18f, SideOuterZ = 6.3f;

    private enum Mode { Floor, OnTop, Wall, Loft }
    private enum Side { LongWall, BackWall, SideOuterWall }

    private sealed class Spec
    {
        public string key, prefab, on;
        public Mode mode;
        public Side wall;
        public float x, z, yaw, height;
        public bool hangByTop, keepCollider = true;
    }

    private static Spec Floor(string key, string prefab, float x, float z, float yaw) =>
        new Spec { key = key, prefab = prefab, mode = Mode.Floor, x = x, z = z, yaw = yaw };
    private static Spec Loft(string key, string prefab, float x, float z, float yaw) =>
        new Spec { key = key, prefab = prefab, mode = Mode.Loft, x = x, z = z, yaw = yaw };
    private static Spec On(string key, string prefab, string on, float x, float z, float yaw) =>
        new Spec { key = key, prefab = prefab, mode = Mode.OnTop, on = on, x = x, z = z, yaw = yaw, keepCollider = false };
    // Along a wall: "along" is x on the long walls, z on the back wall; height is the bottom (or top, for hung tools).
    private static Spec Wall(string key, string prefab, Side side, float along, float height, bool hangByTop = false, float yaw = 0f) =>
        new Spec { key = key, prefab = prefab, mode = Mode.Wall, wall = side, x = along, z = along, height = height, hangByTop = hangByTop, yaw = yaw, keepCollider = false };

    private static readonly Spec[] Layout =
    {
        // Back end, against the shut back doors: two bales with two more on top, and loose hay beside them.
        Floor("bale1", "SM_Prop_Hay_Bale_Square_02", -4.85f, -2.2f, 90f),
        Floor("bale2", "SM_Prop_Hay_Bale_Square_03", -4.85f, -0.5f, 90f),
        On("bale3", "SM_Prop_Hay_Bale_Square_03", "bale1", -4.85f, -2.05f, 93f),
        On("bale4", "SM_Prop_Hay_Bale_Square_02", "bale2", -4.9f, -0.75f, 87f),
        Floor("hay", "SM_Prop_Hay_Pile_01", -4.25f, 2.0f, 20f),
        Wall("hayfork", "SM_Prop_Tool_Hayfork_01", Side.BackWall, 1.0f, 2.15f, true, 90f),

        // Long wall: a workbench with its things, a wall rack over it and a lantern.
        Floor("bench", "SM_Prop_Table_01", -2.1f, -2.47f, 90f),
        On("toolbox", "SM_Prop_ToolBox_01", "bench", -2.85f, -2.6f, 90f),
        On("benchbucket", "SM_Prop_Tool_Bucket_01", "bench", -2.05f, -2.7f, 0f),
        On("seeds", "SM_Prop_SeedPacket_01", "bench", -1.45f, -2.55f, 15f),
        On("bottle", "SM_Gen_Prop_Bottle_01", "bench", -1.15f, -2.75f, 0f),
        Floor("opensack", "SM_Prop_GrainBag_Open_01", -0.45f, -2.75f, 30f),
        // The long wall has windows at x -3.7, 0 and 3.9 (1.35 m wide) with white posts between them at -1.8 and 1.9:
        // the rack and the tools go between them, the torches on the posts.
        Wall("rack", "SM_Prop_Rack_01", Side.LongWall, -2.45f, 1.3f),
        Wall("torch1", "SM_Gen_Prop_Light_Wall_01", Side.LongWall, -1.81f, 1.65f),

        // Garden tools hung on nails, a bucket under them.
        Wall("pitchfork", "SM_Prop_Tool_Pitchfork_01", Side.LongWall, 0.88f, 2.0f, true),
        Wall("spade", "SM_Prop_Tool_Spade_01", Side.LongWall, 1.15f, 2.0f, true),
        Wall("rake", "SM_Prop_Tool_Rake_01", Side.LongWall, 1.48f, 2.0f, true),
        Wall("scythe", "SM_Prop_Tool_Scythe_01", Side.LongWall, 2.55f, 2.05f, true),
        Floor("toolbucket", "SM_Prop_Tool_Bucket_01", 1.15f, -2.8f, 40f),

        // Barrels and a water tub, a second torch; the sack stack in the corner by the open doors (under the loft).
        Floor("barrel1", "SM_Prop_Barrel_01", 2.75f, -2.65f, 0f),
        Floor("barrel2", "SM_Prop_Barrel_02", 3.7f, -2.65f, 40f),
        Floor("tub", "SM_Prop_Barrel_Half_01", 3.6f, -1.75f, 0f),
        Wall("torch2", "SM_Gen_Prop_Light_Wall_01", Side.LongWall, 1.89f, 1.65f),
        Floor("sacks", "SM_Gen_Prop_Sack_Stack_01", 4.9f, -2.6f, 15f),

        // The other corner by the open doors.
        Floor("wheelbarrow", "SM_Prop_Wheelbarrow_01", 4.0f, 2.65f, 90f),

        // Side room: trough and round bales at the back end, produce and sacks towards the front, middle left free.
        Floor("trough", "SM_Prop_Trough_01", -2.45f, 5.75f, 0f),
        Floor("round1", "SM_Prop_Hay_Bale_Round_01", -4.75f, 5.6f, 0f),
        Floor("round2", "SM_Prop_Hay_Bale_Round_01", -4.7f, 4.2f, 35f),
        Floor("crate", "SM_Prop_Crate_01", 0.9f, 5.85f, 5f),
        On("apricots", "SM_Prop_Box_Apricot_01", "crate", 0.9f, 5.85f, -8f),
        Floor("pallet", "SM_Prop_PalletCrate_01", 1.95f, 5.95f, 0f),
        Floor("onions", "SM_Prop_Box_Onion_01", 2.95f, 5.85f, 180f),
        Floor("pumpkin", "SM_Prop_Pumpkin_01", 1.9f, 5.25f, 30f),
        Floor("grain1", "SM_Prop_GrainBag_01", 4.0f, 5.95f, 8f),
        On("grain2", "SM_Prop_GrainBag_01", "grain1", 3.95f, 5.9f, -12f),
        Floor("sack", "SM_Gen_Prop_Sack_02", 4.1f, 5.2f, 50f),
        Floor("woodbarrel", "SM_Gen_Prop_Barrel_Wood_01", 4.95f, 5.85f, 0f),

        // Up in the loft: bales along the walls, loose hay before the loft door, a coil of rope.
        Loft("loftbale1", "SM_Prop_Hay_Bale_Square_01", 4.8f, -2.25f, 90f),
        On("loftbale2", "SM_Prop_Hay_Bale_Square_03", "loftbale1", 4.85f, -2.3f, 88f),
        Loft("loftbale3", "SM_Prop_Hay_Bale_Square_02", 4.85f, 2.2f, 92f),
        Loft("lofthay", "SM_Prop_Hay_Pile_01", 4.25f, 0f, 0f),
        Loft("rope", "SM_Gen_Prop_Rope_02", 3.35f, -2.4f, 30f),
    };

    // The hayloft over the open front doors (barn space): a plank floor on joists and an edge girder held by two posts
    // either side of the way in, a railing along its edge with a gap where the ladder comes up, and the ladder.
    private const float LoftEdgeX = 2.6f, LoftTop = 3.8f, PlankThickness = .12f, JoistDepth = .22f, GirderDepth = .36f;
    private const float LadderZ = 2.3f, LadderGapFrom = 1.95f, LadderGapTo = 2.65f;
    private static readonly float[] PostsZ = { -1.6f, 1.6f };
    private static readonly float[] JoistsZ = { -3.0f, -1.5f, 0f, 1.5f, 3.0f };
    private const string Plank = "SM_Gen_Prop_Plank_01", Beam = "SM_Gen_Bld_Beam_01", Ladder = "SM_Gen_Bld_Ladder_01",
        Fence = "SM_Prop_Fence_Wood_01", FencePole = "SM_Prop_Fence_Wood_Pole_01";
    private static readonly string[] LoftParts = { Plank, Beam, Ladder, Fence, FencePole };

    // Flame for the wall torches: a soft additive particle texture and material, made once.
    private const string FlameFolder = "Assets/Art/Barn";
    private const string FlameTexturePath = FlameFolder + "/T_TorchFlame.png";
    private const string FlameMaterialPath = FlameFolder + "/M_TorchFlame.mat";

    [MenuItem("Tools/Chick/Barn/Furnish Barn Interior")]
    private static void Furnish()
    {
        if (GameObject.Find("Farm Reference Map") == null)
        {
            EditorUtility.DisplayDialog("Barn interior", "The open scene has no 'Farm Reference Map'.", "OK");
            return;
        }
        Transform barn = FindBarn();
        if (barn == null)
        {
            EditorUtility.DisplayDialog("Barn interior", "No hand-painted " + BarnName + " (with its _Interior) in the open scene.", "OK");
            return;
        }

        var report = new List<string>();
        var prefabs = new Dictionary<string, GameObject>();
        foreach (string name in Layout.Select(s => s.prefab).Concat(LoftParts).Distinct())
        {
            GameObject prefab = LoadOrRestore(name, report);
            if (prefab != null) prefabs[name] = prefab;
            else report.Add("missing prefab " + name);
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Furnish barn interior");
        int undo = Undo.GetCurrentGroup();
        Transform old = barn.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Furnish barn interior");
        group.SetParent(barn, false);
        group.localPosition = Vector3.zero;
        group.localRotation = Quaternion.identity;
        group.localScale = Vector3.one;
        Physics.SyncTransforms();

        var barnColliders = barn.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && !c.transform.IsChildOf(group)).ToArray();
        floorTop = float.NaN;
        ClearInside(barn, report);
        floorTop = LayFloor(barn, group, report);
        if (LoftParts.All(prefabs.ContainsKey)) BuildLoft(barn, group, prefabs, report);
        else report.Add("no loft: missing " + string.Join(", ", LoftParts.Where(p => !prefabs.ContainsKey(p))));
        Physics.SyncTransforms();
        var placed = new Dictionary<string, (GameObject go, Bounds local)>();
        foreach (Spec spec in Layout)
        {
            if (!prefabs.TryGetValue(spec.prefab, out GameObject prefab)) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            go.name = spec.prefab + " (" + spec.key + ")";
            go.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
            go.transform.localPosition = Vector3.zero;
            Bounds b = LocalBounds(go, barn);
            Vector3 want;
            switch (spec.mode)
            {
                case Mode.OnTop:
                    if (!placed.TryGetValue(spec.on, out var under)) { Object.DestroyImmediate(go); report.Add($"skipped {spec.key}: {spec.on} not placed"); continue; }
                    want = new Vector3(spec.x - b.center.x, under.local.max.y - b.min.y, spec.z - b.center.z);
                    break;
                case Mode.Wall:
                    want = OnWall(spec, b, barn, barnColliders);
                    break;
                case Mode.Loft:
                    want = new Vector3(spec.x - b.center.x, LoftTop - b.min.y, spec.z - b.center.z);
                    break;
                default:
                    want = new Vector3(spec.x - b.center.x, FloorHeight(barn, spec.x, spec.z) - b.min.y, spec.z - b.center.z);
                    break;
            }
            go.transform.localPosition = want;
            if (!spec.keepCollider)
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            Physics.SyncTransforms();
            Bounds now = LocalBounds(go, barn);
            string blocker = spec.mode == Mode.Floor ? Blocker(go, barn, now) : null;
            if (blocker != null)
            {
                Object.DestroyImmediate(go);
                report.Add($"skipped {spec.key} ({spec.prefab}): would overlap {blocker}");
                continue;
            }
            placed[spec.key] = (go, now);
            if (spec.key.StartsWith("torch")) AddFlame(go, barn);
        }

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(barn.gameObject.scene);
        Selection.activeGameObject = group.gameObject;
        report.Insert(0, $"placed {placed.Count} of {Layout.Length} props in {barn.name} at {barn.position}");
        Survey(barn, report);
        Debug.Log("Barn interior: " + string.Join("; ", report) + ". Pictures in " + Path.GetFullPath(Out) + ". Save the scene to keep it.");
    }

    // The board floor: the hall (out to the open doorway) and the side room, barn space (x, z).
    private static readonly Rect[] FloorAreas =
    {
        Rect.MinMaxRect(BackWallX - .02f, LongWallZ - .07f, FrontWallX + .12f, SideRoomZ),
        Rect.MinMaxRect(BackWallX - .02f, SideRoomZ, FrontWallX + .02f, 6.42f),
    };
    private const string FloorMeshPath = FlameFolder + "/SM_BarnFloor.asset";
    private const string FloorTexturePath = FlameFolder + "/T_BarnFloor.png";
    private const string FloorMaterialPath = FlameFolder + "/M_BarnFloor.mat";
    private const float FloorTile = 2.72f;   // metres per texture tile (8 boards of 0.34 m)
    private static float floorTop = float.NaN;

    // Ground cover standing inside the barn (grass tufts, pebbles, flowers) would poke through the boards: the loose
    // ones go, ones inside a prefab are switched off; the terrain's own detail grass is cleared there too.
    private static void ClearInside(Transform barn, List<string> report)
    {
        var gone = new HashSet<GameObject>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.transform.IsChildOf(barn) || r is ParticleSystemRenderer) continue;
            string n = r.name.ToLowerInvariant();
            if (n.Contains("rocking")) continue;
            if (!(n.Contains("grass") || n.Contains("rock") || n.Contains("pebble") || n.Contains("flower") || n.Contains("weed") || n.Contains("mushroom"))) continue;
            Bounds b = r.bounds;
            Vector3 c = barn.InverseTransformPoint(b.center);
            if (b.size.y > 1.5f || !FloorAreas.Any(a => a.Contains(new Vector2(c.x, c.z)))) continue;
            LODGroup lod = r.GetComponentInParent<LODGroup>();
            gone.Add(lod != null && !lod.transform.IsChildOf(barn) ? lod.gameObject : r.gameObject);
        }
        int removed = 0, hidden = 0;
        foreach (GameObject go in gone)
        {
            if (go == null) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(go) && PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go)
            {
                Undo.RecordObject(go, "Furnish barn interior");
                go.SetActive(false);
                hidden++;
            }
            else { Undo.DestroyObjectImmediate(go); removed++; }
        }

        int cells = 0;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null || data.detailPrototypes.Length == 0) continue;
            Vector3 origin = terrain.transform.position, size = data.size;
            foreach (Rect area in FloorAreas)
            {
                Vector3 a = barn.TransformPoint(new Vector3(area.xMin, 0f, area.yMin)), b = barn.TransformPoint(new Vector3(area.xMax, 0f, area.yMax));
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - origin.x) / size.x * data.detailWidth);
                int x1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) - origin.x) / size.x * data.detailWidth);
                int y0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - origin.z) / size.z * data.detailHeight);
                int y1 = Mathf.CeilToInt((Mathf.Max(a.z, b.z) - origin.z) / size.z * data.detailHeight);
                x0 = Mathf.Clamp(x0, 0, data.detailWidth); x1 = Mathf.Clamp(x1, 0, data.detailWidth);
                y0 = Mathf.Clamp(y0, 0, data.detailHeight); y1 = Mathf.Clamp(y1, 0, data.detailHeight);
                if (x1 <= x0 || y1 <= y0) continue;
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                {
                    int[,] map = data.GetDetailLayer(x0, y0, x1 - x0, y1 - y0, layer);
                    int found = 0;
                    foreach (int v in map) if (v > 0) found++;
                    if (found == 0) continue;
                    Undo.RegisterCompleteObjectUndo(data, "Furnish barn interior");
                    data.SetDetailLayer(x0, y0, layer, new int[y1 - y0, x1 - x0]);
                    cells += found;
                }
            }
        }
        Physics.SyncTransforms();
        report.Add($"cleared inside: {removed} removed, {hidden} switched off, {cells} terrain grass cells");
    }

    // A solid board floor painted like the inside walls (T_BarnFloor, ArtSource/BarnFloor/make_floor_texture.py) over
    // the whole inside, boards running along the barn, a touch above the highest ground under it so no grass shows:
    // one flat mesh (an asset) with an edge down to the ground at the open doorway, and a box collider per area.
    private static float LayFloor(Transform barn, Transform group, List<string> report)
    {
        Material material = FloorMaterial();
        if (material == null) { report.Add("no floor: missing " + FloorTexturePath); return float.NaN; }

        // Ground under the floor (terrain and anything else not part of the barn).
        float high = float.NegativeInfinity, low = float.PositiveInfinity;
        foreach (Rect area in FloorAreas)
            for (float x = area.xMin + .1f; x < area.xMax; x += .5f)
            for (float z = area.yMin + .1f; z < area.yMax; z += .5f)
            {
                Vector3 from = barn.TransformPoint(new Vector3(x, 2f, z));
                foreach (RaycastHit hit in Physics.RaycastAll(from, -barn.up, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(barn) || hit.collider is CharacterController || hit.collider.attachedRigidbody != null) continue;
                    float y = barn.InverseTransformPoint(hit.point).y;
                    high = Mathf.Max(high, y);
                    low = Mathf.Min(low, y);
                }
            }
        if (float.IsNegativeInfinity(high)) { high = 0f; low = 0f; }
        float top = Mathf.Max(.06f, high + .025f);

        // Top faces (UV in floor tiles: boards along x) and the edge at the open doorway, facing out.
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            normals.AddRange(new[] { normal, normal, normal, normal });
            uvs.AddRange(new[] { ua, ub, uc, ud });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        Vector2 Uv(float x, float z) => new Vector2(x / FloorTile, z / FloorTile);
        foreach (Rect area in FloorAreas)
            Quad(new Vector3(area.xMin, top, area.yMin), new Vector3(area.xMin, top, area.yMax), new Vector3(area.xMax, top, area.yMax),
                new Vector3(area.xMax, top, area.yMin), Vector3.up,
                Uv(area.xMin, area.yMin), Uv(area.xMin, area.yMax), Uv(area.xMax, area.yMax), Uv(area.xMax, area.yMin));
        Rect hall = FloorAreas[0];
        float bottom = Mathf.Min(low, 0f) - .05f;
        Quad(new Vector3(hall.xMax, bottom, hall.yMin), new Vector3(hall.xMax, top, hall.yMin), new Vector3(hall.xMax, top, hall.yMax),
            new Vector3(hall.xMax, bottom, hall.yMax), Vector3.right,
            new Vector2(0f, hall.yMin / FloorTile), new Vector2((top - bottom) / FloorTile, hall.yMin / FloorTile),
            new Vector2((top - bottom) / FloorTile, hall.yMax / FloorTile), new Vector2(0f, hall.yMax / FloorTile));

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FloorMeshPath);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = "SM_BarnFloor" };
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EnsureFolder(FlameFolder);
        if (created) AssetDatabase.CreateAsset(mesh, FloorMeshPath);
        else EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();

        var floor = new GameObject("Barn Floor");
        floor.transform.SetParent(group, false);
        floor.AddComponent<MeshFilter>().sharedMesh = mesh;
        var floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        foreach (Rect area in FloorAreas)
        {
            var box = floor.AddComponent<BoxCollider>();
            box.center = new Vector3(area.center.x, top - .1f, area.center.y);
            box.size = new Vector3(area.width, .2f, area.height);
        }
        Physics.SyncTransforms();
        report.Add($"floor: top {top:0.000} (ground under it {low:0.000} .. {high:0.000})");
        return top;
    }

    private static Material FloorMaterial()
    {
        if (File.Exists(FloorTexturePath) && AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath) == null) AssetDatabase.ImportAsset(FloorTexturePath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
        if (texture == null) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(FloorTexturePath);
        if (importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel < 4)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, FloorMaterialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", .12f);
        material.SetFloat("_Metallic", 0f);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void BuildLoft(Transform barn, Transform group, Dictionary<string, GameObject> prefabs, List<string> report)
    {
        var loft = new GameObject("Hayloft").transform;
        loft.SetParent(group, false);
        const float back = LoftEdgeX, front = FrontWallX + .05f, zMin = LongWallZ - .09f, zMax = SideRoomZ - .01f;
        float depth = front - back, under = LoftTop - PlankThickness;
        var random = new System.Random(1905);
        float Jitter(float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;
        int count = 0;

        // Floor boards running along the barn's width in staggered rows; one box collider for the whole floor.
        const int rows = 11;
        float rowWidth = depth / rows;
        for (int r = 0; r < rows; r++)
        {
            float x = back + (r + .5f) * rowWidth;
            float a = zMin;
            for (float b = zMin + (r % 2 == 0 ? 1.6f : .8f); a < zMax - .01f; a = b, b += 1.6f)
            {
                float end = Mathf.Min(b, zMax);
                Put(prefabs[Plank], loft, "Floor Board", Quaternion.Euler(0f, 90f + Jitter(.6f), 0f),
                    new Vector3((end - a) / 1.70f - .004f, 1f, rowWidth / .28f * 1.01f), new Vector3(x, LoftTop + Jitter(.006f), (a + end) / 2f),
                    new Vector3(.5f, 1f, .5f), barn, false);
                count++;
            }
        }
        var floor = loft.gameObject.AddComponent<BoxCollider>();
        floor.center = new Vector3((back + front) / 2f, LoftTop - PlankThickness / 2f, (zMin + zMax) / 2f);
        floor.size = new Vector3(depth, PlankThickness, zMax - zMin);

        // Joists from the front wall to the edge, the girder along the edge, the posts and their knee braces.
        foreach (float z in JoistsZ)
            Put(prefabs[Beam], loft, "Joist", Quaternion.Euler(0f, 90f, 0f), new Vector3(.6f, .6f, depth / 5f),
                new Vector3((back + front) / 2f, under, z), new Vector3(.5f, 1f, .5f), barn, false);
        Put(prefabs[Beam], loft, "Girder", Quaternion.identity, new Vector3(1f, 1f, (zMax - zMin) / 5f),
            new Vector3(back + .18f, under, (zMin + zMax) / 2f), new Vector3(.5f, 1f, .5f), barn, false);
        float girderBottom = under - GirderDepth;
        foreach (float z in PostsZ)
        {
            float ground = FloorHeight(barn, back + .18f, z);
            Put(prefabs[Beam], loft, "Post", Quaternion.Euler(-90f, 0f, 0f), new Vector3(.75f, .75f, (girderBottom - ground) / 5f),
                new Vector3(back + .18f, ground, z), new Vector3(.5f, 0f, .5f), barn, true);
            foreach (float side in new[] { -1f, 1f })
                Put(prefabs[Beam], loft, "Brace", Quaternion.Euler(-45f, side > 0f ? 0f : 180f, 0f), new Vector3(.45f, .45f, .19f),
                    new Vector3(back + .18f, girderBottom - .3f, z + side * .3f), new Vector3(.5f, .5f, .5f), barn, false);
        }

        // Railing along the edge, open where the ladder arrives.
        float fenceX = back + .1f;
        foreach (var (a, b) in new[] { (zMin + .05f, -.6f), (-.6f, LadderGapFrom) })
            Put(prefabs[Fence], loft, "Railing", Quaternion.Euler(0f, 90f, 0f), new Vector3((b - a) / 2.5f, 1f, 1f),
                new Vector3(fenceX, LoftTop + .08f, (a + b) / 2f), new Vector3(.5f, 0f, .5f), barn, false);
        foreach (float z in new[] { zMin + .1f, -.6f, LadderGapFrom, LadderGapTo })
            Put(prefabs[FencePole], loft, "Railing Post", Quaternion.identity, new Vector3(1f, .85f, 1f),
                new Vector3(fenceX, LoftTop, z), new Vector3(.5f, 0f, .5f), barn, false);

        // The ladder: leaning on the girder at a quarter slope, reaching 0.6 m above the loft floor.
        const float slope = .25f, top = LoftTop + .6f;
        float foot = FloorHeight(barn, back - 1f, LadderZ);
        float tilt = Mathf.Atan(slope) * Mathf.Rad2Deg;
        float length = (top - foot) / Mathf.Cos(tilt * Mathf.Deg2Rad);
        Put(prefabs[Ladder], loft, "Ladder", Quaternion.AngleAxis(-tilt, Vector3.forward) * Quaternion.Euler(0f, 90f, 0f),
            new Vector3(1f, length / 3f, 1f), new Vector3(back + (top - (under - GirderDepth / 2f)) * slope, foot, LadderZ),
            new Vector3(1f, 0f, .5f), barn, true);
        report.Add($"hayloft: {count} boards, {JoistsZ.Length} joists, {PostsZ.Length} posts, railing, ladder");
    }

    // A prefab instance turned and scaled, then moved so a point of its bounds (fraction of its size from the
    // minimum corner, barn space) lands on the anchor.
    private static GameObject Put(GameObject prefab, Transform parent, string name, Quaternion rotation, Vector3 scale,
        Vector3 anchor, Vector3 fraction, Transform barn, bool collider)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.transform.localPosition = Vector3.zero;
        Bounds b = LocalBounds(go, barn);
        go.transform.localPosition = anchor - (b.min + Vector3.Scale(b.size, fraction));
        if (!collider)
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        return go;
    }

    // A fire burning in the torch: a flickering flame and a few embers (particles) at the top of its head, and a warm
    // flickering light a little out from the wall.
    private static void AddFlame(GameObject torch, Transform barn)
    {
        var tops = new List<Vector3>();
        float maxY = float.NegativeInfinity;
        foreach (var filter in torch.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                Vector3 p = filter.transform.TransformPoint(v);
                tops.Add(p);
                maxY = Mathf.Max(maxY, p.y);
            }
        }
        if (tops.Count == 0) return;
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (Vector3 p in tops) if (p.y > maxY - .06f) { sum += p; n++; }
        Vector3 head = sum / n;

        Material material = FlameMaterial();
        var flame = new GameObject("Flame");
        flame.transform.SetParent(torch.transform, true);
        flame.transform.SetPositionAndRotation(head - Vector3.up * .04f, Quaternion.identity);
        Fire(flame.AddComponent<ParticleSystem>(), material, false);
        var embers = new GameObject("Embers");
        embers.transform.SetParent(flame.transform, false);
        Fire(embers.AddComponent<ParticleSystem>(), material, true);

        var glow = new GameObject("Fire Light");
        glow.transform.SetParent(flame.transform, false);
        glow.transform.position = head + Vector3.up * .12f + barn.forward * .15f;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, .62f, .3f);
        light.range = 4.5f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;
        glow.AddComponent<TorchFlame>();
    }

    private static void Fire(ParticleSystem ps, Material material, bool embers)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startLifetime = embers ? new ParticleSystem.MinMaxCurve(.8f, 1.4f) : new ParticleSystem.MinMaxCurve(.35f, .6f);
        main.startSpeed = embers ? new ParticleSystem.MinMaxCurve(.2f, .5f) : new ParticleSystem.MinMaxCurve(.15f, .35f);
        main.startSize = embers ? new ParticleSystem.MinMaxCurve(.01f, .022f) : new ParticleSystem.MinMaxCurve(.07f, .14f);
        main.startColor = embers
            ? new ParticleSystem.MinMaxGradient(new Color(1f, .7f, .3f), new Color(1f, .45f, .1f))
            : new ParticleSystem.MinMaxGradient(new Color(1f, .85f, .45f), new Color(1f, .55f, .2f));
        main.gravityModifier = embers ? -.12f : -.06f;
        main.maxParticles = embers ? 12 : 80;

        var emission = ps.emission;
        emission.rateOverTime = embers ? 4f : 50f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = embers ? 15f : 6f;
        shape.radius = .025f;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // the cone emits along +Z: point it up

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .95f, .7f), 0f), new GradientColorKey(new Color(1f, .6f, .15f), .35f),
                    new GradientColorKey(new Color(.8f, .2f, .05f), .75f), new GradientColorKey(new Color(.3f, .05f, .02f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .1f), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, embers
            ? AnimationCurve.Linear(0f, 1f, 1f, .3f)
            : new AnimationCurve(new Keyframe(0f, .6f), new Keyframe(.2f, 1f), new Keyframe(1f, .1f)));
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = embers ? .3f : .12f;
        noise.frequency = 3f;
        noise.scrollSpeed = 1.5f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play(true);
    }

    // A soft round spot (additive URP particle material): bright in the middle, fading to nothing at the edge.
    private static Material FlameMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
        if (material != null) return material;
        EnsureFolder(FlameFolder);
        if (!File.Exists(FlameTexturePath))
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / (size / 2f);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            File.WriteAllBytes(FlameTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(FlameTexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(FlameTexturePath);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FlameTexturePath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", (float)BaseShaderGUI.SurfaceType.Transparent);
        material.SetFloat("_Blend", (float)BaseShaderGUI.BlendMode.Additive);
        BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
        AssetDatabase.CreateAsset(material, FlameMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    [MenuItem("Tools/Chick/Barn/Remove Barn Interior Props")]
    private static void Remove()
    {
        Transform barn = FindBarn();
        Transform group = barn != null ? barn.Find(GroupName) : null;
        if (group == null) { EditorUtility.DisplayDialog("Barn interior", "Nothing to remove.", "OK"); return; }
        Undo.DestroyObjectImmediate(group.gameObject);
        EditorSceneManager.MarkSceneDirty(barn.gameObject.scene);
    }

    private static Transform FindBarn()
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == BarnName && t.Find(BarnName + "_Interior") != null) return t;
        return null;
    }

    // The prop's meshes, bounded in the barn's space.
    private static Bounds LocalBounds(GameObject go, Transform space)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
            Bounds m = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? m.min.x : m.max.x, (i & 2) == 0 ? m.min.y : m.max.y, (i & 4) == 0 ? m.min.z : m.max.z);
                Vector3 p = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return any ? b : new Bounds(space.InverseTransformPoint(go.transform.position), Vector3.one * .1f);
    }

    // The ground under a spot, barn space: the hall's floor, or the terrain in the side room (it has no floor of its
    // own); the barn and its props do not count.
    private static float FloorHeight(Transform barn, float x, float z)
    {
        if (!float.IsNaN(floorTop) && FloorAreas.Any(r => r.Contains(new Vector2(x, z)))) return floorTop;
        const float hallFloor = .015f;
        bool inHall = z < SideRoomZ;
        Vector3 from = barn.TransformPoint(new Vector3(x, 2.5f, z));
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, -barn.up, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(barn) || hit.collider is CharacterController || hit.collider.attachedRigidbody != null) continue;
            best = Mathf.Max(best, barn.InverseTransformPoint(hit.point).y);
        }
        if (float.IsNegativeInfinity(best)) return inHall ? hallFloor : 0f;
        return inHall ? Mathf.Max(best, hallFloor) : best;
    }

    // Flat against the wall's inside face (found on the barn's collider, else the plan's figure), at the given height.
    private static Vector3 OnWall(Spec spec, Bounds b, Transform barn, Collider[] colliders)
    {
        const float gap = .04f;
        float floor = FloorHeight(barn, spec.wall == Side.BackWall ? BackWallX + .5f : spec.x, spec.wall == Side.BackWall ? spec.z : spec.wall == Side.LongWall ? LongWallZ + .5f : SideOuterZ - .5f);
        float y = floor + spec.height - (spec.hangByTop ? b.max.y : b.min.y);
        float probeHeight = floor + spec.height - (spec.hangByTop ? .3f : -.3f);
        switch (spec.wall)
        {
            case Side.BackWall:
            {
                float wall = WallAt(barn, colliders, new Vector3(-2f, probeHeight, spec.z), Vector3.left, BackWallX);
                return new Vector3(wall + gap - b.min.x, y, spec.z - b.center.z);
            }
            case Side.SideOuterWall:
            {
                float wall = WallAt(barn, colliders, new Vector3(spec.x, probeHeight, 4.5f), Vector3.forward, SideOuterZ);
                return new Vector3(spec.x - b.center.x, y, wall - gap - b.max.z);
            }
            default:
            {
                float wall = WallAt(barn, colliders, new Vector3(spec.x, probeHeight, 0f), Vector3.back, LongWallZ);
                return new Vector3(spec.x - b.center.x, y, wall + gap - b.min.z);
            }
        }
    }

    // Where a ray from inside (barn space) meets the barn's collider; the fallback if it meets nothing near the plan.
    private static float WallAt(Transform barn, Collider[] colliders, Vector3 from, Vector3 direction, float fallback)
    {
        var ray = new Ray(barn.TransformPoint(from), barn.TransformDirection(direction));
        float best = float.PositiveInfinity;
        foreach (Collider c in colliders)
            if (c.Raycast(ray, out RaycastHit hit, 12f) && hit.distance < best) best = hit.distance;
        if (float.IsPositiveInfinity(best)) return fallback;
        Vector3 p = barn.InverseTransformPoint(ray.GetPoint(best));
        float found = direction.x != 0f ? p.x : p.z;
        return Mathf.Abs(found - fallback) < .6f ? found : fallback;
    }

    // Something already standing where the prop would go (not the barn, the ground or the props placed here).
    private static string Blocker(GameObject go, Transform barn, Bounds local)
    {
        Vector3 half = Vector3.Max(local.extents - Vector3.one * .06f, Vector3.one * .01f);
        Vector3 centre = barn.TransformPoint(local.center + Vector3.up * .05f);
        foreach (Collider c in Physics.OverlapBox(centre, half, barn.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(barn) || c is TerrainCollider) continue;
            return c.name;
        }
        return null;
    }

    // The prefab by name from the Synty packs; if "Collect Unused Assets" moved it aside, it and the files it uses are
    // moved back to where they were.
    private static GameObject LoadOrRestore(string name, List<string> report)
    {
        string path = AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Synty" })
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
        if (path != null) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!File.Exists(UnusedManifest)) return null;

        var lines = File.ReadAllLines(UnusedManifest).Where(l => l.Contains('\t')).Select(l => l.Split('\t')).ToList();
        var line = lines.FirstOrDefault(l => Path.GetFileName(l[1]) == name + ".prefab" && l[1].StartsWith("Assets/Synty/"));
        if (line == null || !File.Exists(line[0])) return null;
        var back = new HashSet<string>(AssetDatabase.GetDependencies(line[0], true).Where(d => d.StartsWith("Assets/_Unused/")));
        int moved = 0;
        foreach (var l in lines.Where(l => back.Contains(l[0])).ToList())
        {
            EnsureFolder(Path.GetDirectoryName(l[1]).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(l[0], l[1]);
            if (string.IsNullOrEmpty(error)) { lines.Remove(l); moved++; }
            else report.Add($"could not move {l[0]} back: {error}");
        }
        File.WriteAllLines(UnusedManifest, lines.Select(l => string.Join("\t", l)));
        report.Add($"brought {name} back from _Unused ({moved} files)");
        return AssetDatabase.LoadAssetAtPath<GameObject>(line[1]);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    // A plan of the inside (everything below 2.6 m, seen from above) and a few views at a person's eye height.
    private static void Survey(Transform barn, List<string> report)
    {
        Directory.CreateDirectory(Out);
        var sb = new StringBuilder();
        foreach (string line in report) sb.AppendLine(line);
        sb.AppendLine("barn rotation " + barn.eulerAngles + " scale " + barn.lossyScale);
        Transform group = barn.Find(GroupName);
        if (group != null)
            foreach (Transform t in group)
                sb.AppendLine($"  {t.name} at {barn.InverseTransformPoint(t.position)} bounds {LocalBounds(t.gameObject, barn)}");
        // Other things standing inside the barn (not its own parts).
        var inside = new Bounds(new Vector3(0f, 1.5f, 1.6f), new Vector3(11.2f, 3f, 9.8f));
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.transform.IsChildOf(barn)) continue;
            if (inside.Contains(barn.InverseTransformPoint(r.bounds.center)))
                sb.AppendLine($"  (already there) {r.name} at {barn.InverseTransformPoint(r.bounds.center)} size {r.bounds.size}");
        }
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString());

        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.orthographic = true;
            camera.orthographicSize = 6.2f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 3f;
            camera.transform.SetPositionAndRotation(barn.TransformPoint(new Vector3(0f, 2.6f, 1.6f)), barn.rotation * Quaternion.Euler(90f, 0f, 0f));
            Save(MapSurvey.Render(camera, 1400, 1100), "plan.png");

            camera.orthographic = false;
            camera.fieldOfView = 70f;
            camera.farClipPlane = 200f;
            var views = new (string name, Vector3 from, Vector3 to)[]
            {
                ("from_front_doors", new Vector3(5.2f, 1.6f, 0f), new Vector3(-5f, .6f, -.5f)),
                ("long_wall", new Vector3(0f, 1.6f, 2.6f), new Vector3(0f, 1f, -3f)),
                ("back_end", new Vector3(1f, 1.6f, 1f), new Vector3(-5.5f, .7f, -.5f)),
                ("side_room", new Vector3(0f, 1.6f, 0.5f), new Vector3(0f, .5f, 6.3f)),
                ("chick_eye", new Vector3(5f, .25f, .3f), new Vector3(-5f, .4f, -1f)),
                ("loft", new Vector3(-2.5f, 1.4f, -.5f), new Vector3(4f, 3.3f, .3f)),
                ("loft_ladder", new Vector3(-.5f, 2f, 3f), new Vector3(3f, 2.6f, 1.2f)),
                ("on_the_loft", new Vector3(3.2f, 5.2f, 2.6f), new Vector3(5f, 3.8f, -1.5f)),
                ("torch", new Vector3(-1f, 1.9f, -1.6f), new Vector3(-1.81f, 2f, -3.1f)),
            };
            foreach (var v in views)
            {
                Vector3 from = barn.TransformPoint(v.from);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(barn.TransformPoint(v.to) - from));
                Save(MapSurvey.Render(camera, 1280, 800), v.name + ".png");
            }
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }

    private static void Save(Texture2D tex, string file)
    {
        File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
