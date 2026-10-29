using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the hill east of the chicken yard into a rocky slope: rows of rock walls (SM_RockWall_01/02) stepping up the
/// hill like ledges, their faces toward the farm and their backs buried in it, loose rocks (SM_Gen_Env_Rock) strewn
/// over the slope, and the ground under them painted stony with the map's own Pebbles, StonyPath and dry grass layers,
/// patchy and fading out into the meadow at the foot. Each row follows the ground: the spot along it that stands
/// <see cref="Rows"/> metres above the fence. Trees, bushes and grass clumps standing inside a rock go. Run
/// GrassGroundTintV1 afterwards so the grass and bushes take the new ground's colour (the menu item does). Running it
/// again places the rocks afresh (the painted ground is painted again on top). Under Map/Coop Side Rocks. Menu.
/// </summary>
public static class MapRockyOutcrop
{
    private const string GroupName = "Coop Side Rocks";
    private const string WallFolder = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/";
    private const string RockFolder = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
    private const float FenceX = 129f, ZMin = -3f, ZMax = 42f, Sink = .22f;
    // each row of walls: its rise above the fence (m), the gap between walls along it (m), their sizes
    private static readonly (float rise, float step, float minSize, float maxSize)[] Rows =
    {
        (2f, 7f, .55f, .8f), (5f, 6.5f, .6f, .9f), (8.5f, 7.5f, .5f, .75f),
    };
    private const float RowGaps = .15f;   // share of walls left out, for breaks in the ledges
    private static readonly string[] Rocks = { "SM_Gen_Env_Rock_01", "SM_Gen_Env_Rock_02", "SM_Gen_Env_Rock_03", "SM_Gen_Env_Rock_04", "SM_Gen_Env_Rock_05", "SM_Gen_Env_Rock_06", "SM_Gen_Env_Rock_09", "SM_Gen_Env_Rock_10" };
    private const int LooseRocks = 40;
    // ground layers (Map terrain): Pebbles, StonyPath, RichGreen_Dry
    private const int Pebbles = 6, StonyPath = 19, DryGrass = 11;

    [MenuItem("Tools/Chick/Rocks/Rocky Slope Beside The Chicken Yard")]
    private static void Menu()
    {
        Debug.Log(Run());
        Debug.Log(FarmReferenceTools.GrassGroundTintV1.Apply());
    }

    public static string Run()
    {
        GameObject map = GameObject.Find("Map");
        if (GameObject.Find("Farm Reference Map") == null || map == null) return "The open scene has no 'Farm Reference Map' / 'Map'.";
        Terrain terrain = Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x*t.terrainData.size.z).First();
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Rocky slope beside the chicken yard");
        int undo = Undo.GetCurrentGroup();
        Transform group = map.transform.Find(GroupName);
        if (group != null) Undo.DestroyObjectImmediate(group.gameObject);
        group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Rocky slope beside the chicken yard");
        group.SetParent(map.transform, false);

        var rng = new System.Random(3115);
        var walls = new List<(Vector3 at, Quaternion rot, float size)>();
        for (int row = 0; row < Rows.Length; row++)
        {
            var r = Rows[row];
            // stagger the rows so the ledges overlap like steps
            for (float z = ZMin + (row % 2)*r.step*.5f; z <= ZMax; z += r.step*Mathf.Lerp(.85f, 1.15f, (float)rng.NextDouble()))
            {
                if (rng.NextDouble() < RowGaps) continue;
                float x = FenceX + 4f;
                while (x < FenceX + 60f && H(x, z) - H(FenceX, z) < r.rise) x += .5f;
                float size = Mathf.Lerp(r.minSize, r.maxSize, (float)rng.NextDouble());
                var rot = Quaternion.Euler(0f, 270f + Mathf.Lerp(-25f, 25f, (float)rng.NextDouble()), 0f);
                var at = new Vector3(x + Mathf.Lerp(-1.5f, 1.5f, (float)rng.NextDouble()), 0f, z);
                at.y = H(at.x, at.z) - 9.6f*size*Sink;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallFolder + "SM_RockWall_0" + (rng.Next(2) + 1) + ".prefab");
                var wall = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                Undo.RegisterCreatedObjectUndo(wall, "Rocky slope beside the chicken yard");
                wall.transform.SetPositionAndRotation(at, rot);
                wall.transform.localScale = Vector3.one*size;
                walls.Add((at, rot, size));
            }
        }

        // loose rocks over the slope, more of them near the ledges
        for (int i = 0, tries = 0; i < LooseRocks && tries < 400; tries++)
        {
            var w = walls[rng.Next(walls.Count)];
            var p = w.at + w.rot*new Vector3(Mathf.Lerp(-6f, 6f, (float)rng.NextDouble())*w.size, 0f, 0f) + Vector3.left*Mathf.Lerp(1f, 6f, (float)rng.NextDouble());
            if (p.x < FenceX + 3f) continue;
            float size = Mathf.Lerp(.45f, 1.15f, (float)(rng.NextDouble()*rng.NextDouble()));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockFolder + Rocks[rng.Next(Rocks.Length)] + ".prefab");
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            Undo.RegisterCreatedObjectUndo(rock, "Rocky slope beside the chicken yard");
            rock.transform.SetPositionAndRotation(new Vector3(p.x, H(p.x, p.z) - .15f*size, p.z), Quaternion.Euler(0f, (float)(rng.NextDouble()*360.0), 0f));
            rock.transform.localScale = Vector3.one*size;
            i++;
        }

        // trees, bushes and grass inside a rock go
        Physics.SyncTransforms();
        var rockColliders = new HashSet<Collider>(group.GetComponentsInChildren<Collider>());
        // under a rock: looking straight down on it, a rock covers the ground there more than knee high
        bool InRock(Vector3 p)
        {
            float ground = H(p.x, p.z);
            return Physics.RaycastAll(new Vector3(p.x, ground + 40f, p.z), Vector3.down, 45f, ~0, QueryTriggerInteraction.Ignore)
                .Any(h => rockColliders.Contains(h.collider) && h.point.y > ground + .5f);
        }
        int cleared = 0;
        var area = new Rect(FenceX + 2f, ZMin - 6f, 60f, ZMax - ZMin + 12f);
        var doomed = new List<GameObject>();
        foreach (var root in new[] { "Trees", "Bushes", "Grass" })
        {
            var parent = map.transform.Find(root);
            if (parent == null) continue;
            foreach (var t in parent.GetComponentsInChildren<Transform>())
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && t.GetComponent<LODGroup>() == null) continue;
                if (!area.Contains(new Vector2(t.position.x, t.position.z)) || !InRock(t.position)) continue;
                doomed.Add(t.gameObject);
            }
        }
        foreach (var go in doomed.Distinct()) if (go != null) { Undo.DestroyObjectImmediate(go); cleared++; }

        // the ground: stony around the ledges, patchy, fading into the meadow
        TerrainData data = terrain.terrainData;
        Vector3 o = terrain.transform.position;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - o.x)/data.size.x*data.alphamapWidth), 0, data.alphamapWidth - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - o.x)/data.size.x*data.alphamapWidth), 0, data.alphamapWidth - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - o.z)/data.size.z*data.alphamapHeight), 0, data.alphamapHeight - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - o.z)/data.size.z*data.alphamapHeight), 0, data.alphamapHeight - 1);
        Undo.RegisterCompleteObjectUndo(data, "Rocky slope beside the chicken yard");
        var alpha = data.GetAlphamaps(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        int layers = data.alphamapLayers;
        for (int y = 0; y <= y1 - y0; y++)
            for (int x = 0; x <= x1 - x0; x++)
            {
                float wx = o.x + (x0 + x + .5f)/data.alphamapWidth*data.size.x, wz = o.z + (y0 + y + .5f)/data.alphamapHeight*data.size.z;
                // near a ledge (in its own width), and only up on the slope
                float near = walls.Max(w =>
                {
                    var local = Quaternion.Inverse(w.rot)*(new Vector3(wx, 0f, wz) - new Vector3(w.at.x, 0f, w.at.z));
                    float along = Mathf.Abs(local.x)/(6.5f*w.size), out_ = Mathf.Abs(local.z)/(7f*w.size);
                    return Mathf.Clamp01(1.3f - Mathf.Max(along, out_));
                });
                float foot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FenceX + 6f, FenceX + 16f, wx));
                float patch = Mathf.PerlinNoise(wx*.17f + 31f, wz*.17f + 7f);
                float stony = Mathf.Clamp01(near*foot*Mathf.Lerp(.55f, 1.25f, patch));
                if (stony <= 0f) continue;
                float pebble = Mathf.PerlinNoise(wx*.35f + 3f, wz*.35f + 11f);
                for (int l = 0; l < layers; l++) alpha[y, x, l] *= 1f - stony;
                alpha[y, x, Pebbles] += stony*Mathf.Lerp(.25f, .55f, pebble);
                alpha[y, x, StonyPath] += stony*Mathf.Lerp(.5f, .2f, pebble);
                alpha[y, x, DryGrass] += stony*.25f;
            }
        data.SetAlphamaps(x0, y0, alpha);

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(map.scene);
        EditorUtility.SetDirty(data);
        string text = $"{walls.Count} rock walls in {Rows.Length} rows, {group.childCount - walls.Count} loose rocks, {cleared} trees/bushes/grass clumps inside rocks removed, slope ground painted stony";
        Debug.Log("Rocky slope: " + text);
        return text;
    }
}
