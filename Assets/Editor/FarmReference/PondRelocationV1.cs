using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Moves the farm pond from the field by the barn path to where Yard Tree 10
    // stood, at twice the area, as the centrepiece of an orchard to come. FarmPondV1 carved the old pond into its own
    // copy of the GroundWear terrain, and the farm terrain has moved on since, so this works on the current terrain
    // in place: RemoveOldPond puts back the ground, paint and grass FarmPondV1 changed (from GroundWear, the terrain
    // it started from), and BuildNewPond carves, paints and dresses the new pond the way FarmPondV1 does.
    public static class PondRelocationV1
    {
        const string Menu = "Tools/Chick/Pond/";
        const string Tree = "Farm Reference Map/05 Nature and Scenery/Yard Tree 10";
        static readonly Vector2 Centre = new Vector2(113.03f, -5.55f);   // where Yard Tree 10 stands
        static readonly float Scale = Mathf.Sqrt(2f);                     // twice the old pond's area
        const float MaxDepth = .075f, ShoreMargin = .3f, Freeboard = .035f;   // as FarmPondV1
        const int TroddenEarth = 4, Soil = 5, Pebbles = 6, WornMeadow = 7;

        [MenuItem(Menu + "Remove Old Pond In Place (V1)")]
        static void RemoveMenu() => Debug.Log(RemoveOldPond());

        [MenuItem(Menu + "Build Pond At Yard Tree 10 (V1)")]
        static void BuildMenu() => Debug.Log(BuildNewPond());

        // FarmPondV1's shoreline, moved onto Yard Tree 10 and scaled up.
        public static Vector2[] NewShoreline()
        {
            var old = FarmPondV1.Shoreline();
            Vector2 min = old[0], max = old[0];
            foreach (var p in old) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            Vector2 centre = (min + max)*.5f;
            var shore = new Vector2[old.Length];
            for (int i = 0; i < old.Length; i++) shore[i] = Centre + (old[i] - centre)*Scale;
            return shore;
        }

        public static string RemoveOldPond()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var pondRoot = root.transform.Find(FarmPondV1.RootName);
            if (pondRoot == null) return "No pond to remove.";
            var shore = FarmPondV1.Shoreline();
            Vector2 min = shore[0], max = shore[0];
            foreach (var p in shore) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var oldCentre = (min + max)*.5f;
            if (Vector2.Distance(new Vector2(pondRoot.position.x, pondRoot.position.z), oldCentre) > .5f)
                throw new InvalidOperationException("The pond in the scene is not FarmPondV1's; nothing removed.");
            var source = AssetDatabase.LoadAssetAtPath<TerrainData>(FarmPondV1.OriginalTerrain);
            if (source == null) throw new InvalidOperationException("Missing " + FarmPondV1.OriginalTerrain);
            var data = terrain.terrainData;
            Vector3 tp = terrain.transform.position, size = data.size;
            min -= Vector2.one*2.6f; max += Vector2.one*2.6f;

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Remove old farm pond"); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(data, "Remove old farm pond");

            // Heights: FarmPondV1 reshaped the bank up to ~2.2 m past the shore.
            int res = data.heightmapResolution;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*(res-1))), x1 = Mathf.Min(res-1, Mathf.CeilToInt((max.x - tp.x)/size.x*(res-1)));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*(res-1))), z1 = Mathf.Min(res-1, Mathf.CeilToInt((max.y - tp.z)/size.z*(res-1)));
            var h = data.GetHeights(x0, z0, x1-x0+1, z1-z0+1);
            var h0 = source.GetHeights(x0, z0, x1-x0+1, z1-z0+1);
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = tp.x + (x0+ix)/(float)(res-1)*size.x, z = tp.z + (z0+iz)/(float)(res-1)*size.z;
                float w = 1 - FarmPondV1.S(2.2f, 2.6f, FarmPondV1.Signed(shore, x, z));
                h[iz, ix] = Mathf.Lerp(h[iz, ix], h0[iz, ix], w);
            }
            data.SetHeights(x0, z0, h);

            // Wet earth and pebbles: painted up to ~0.7 m past the shore.
            int ares = data.alphamapResolution, layers = data.alphamapLayers, sourceLayers = source.alphamapLayers;
            int ax0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*ares)), ax1 = Mathf.Min(ares-1, Mathf.CeilToInt((max.x - tp.x)/size.x*ares));
            int az0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*ares)), az1 = Mathf.Min(ares-1, Mathf.CeilToInt((max.y - tp.z)/size.z*ares));
            var a = data.GetAlphamaps(ax0, az0, ax1-ax0+1, az1-az0+1);
            var a0 = source.GetAlphamaps(ax0, az0, ax1-ax0+1, az1-az0+1);
            for (int iz = 0; iz <= az1-az0; iz++) for (int ix = 0; ix <= ax1-ax0; ix++)
            {
                float x = tp.x + (ax0+ix+.5f)/ares*size.x, z = tp.z + (az0+iz+.5f)/ares*size.z;
                float w = 1 - FarmPondV1.S(.6f, 1f, FarmPondV1.Signed(shore, x, z));
                if (w <= 0) continue;
                float sum = 0;
                for (int k = 0; k < layers; k++) { a[iz, ix, k] = Mathf.Lerp(a[iz, ix, k], k < sourceLayers ? a0[iz, ix, k] : 0, w); sum += a[iz, ix, k]; }
                for (int k = 0; k < layers; k++) a[iz, ix, k] /= Mathf.Max(sum, 1e-5f);
            }
            data.SetAlphamaps(ax0, az0, a);

            // Grass FarmPondV1 cleared from the water and the wet bank (up to 0.9 m out).
            int dres = data.detailWidth, restoredGrass = 0;
            int dx0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*dres)), dx1 = Mathf.Min(dres-1, Mathf.CeilToInt((max.x - tp.x)/size.x*dres));
            int dz0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*data.detailHeight)), dz1 = Mathf.Min(data.detailHeight-1, Mathf.CeilToInt((max.y - tp.z)/size.z*data.detailHeight));
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                var m = data.GetDetailLayer(dx0, dz0, dx1-dx0+1, dz1-dz0+1, layer);
                var m0 = source.GetDetailLayer(dx0, dz0, dx1-dx0+1, dz1-dz0+1, layer);
                for (int iz = 0; iz <= dz1-dz0; iz++) for (int ix = 0; ix <= dx1-dx0; ix++)
                {
                    float x = tp.x + (dx0+ix+.5f)/dres*size.x, z = tp.z + (dz0+iz+.5f)/data.detailHeight*size.z;
                    if (FarmPondV1.Signed(shore, x, z) >= 1f || m[iz, ix] == m0[iz, ix]) continue;
                    restoredGrass += Mathf.Max(0, m0[iz, ix] - m[iz, ix]);
                    m[iz, ix] = m0[iz, ix];
                }
                data.SetDetailLayer(dx0, dz0, layer, m);
            }

            int reshown = 0;
            if (pondRoot.TryGetComponent(out FarmPond pond))
                foreach (var go in pond.HiddenForPond)
                    if (go != null && !go.activeSelf) { Undo.RecordObject(go, "Remove old farm pond"); go.SetActive(true); reshown++; }
            Undo.DestroyObjectImmediate(pondRoot.gameObject);
            Undo.CollapseUndoOperations(group);

            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Old pond removed: ground, wet paint and " + restoredGrass + " grass instances put back, " + reshown + " covered objects shown again.";
        }

        public static string BuildNewPond()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            if (root.transform.Find(FarmPondV1.RootName) != null) throw new InvalidOperationException("A pond already exists; remove it first.");
            var player = UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
            if (player == null) throw new InvalidOperationException("Player missing.");
            var data = terrain.terrainData;
            var shore = NewShoreline();
            Vector2 min = shore[0], max = shore[0];
            foreach (var p in shore) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var report = new System.Text.StringBuilder();

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Build farm pond at Yard Tree 10"); int group = Undo.GetCurrentGroup();
            // Cover what stands in the water or on its wet bank (Yard Tree 10 among them) before the ground changes.
            var hidden = HideCovered(root.transform, shore, report);
            Undo.RegisterCompleteObjectUndo(data, "Build farm pond at Yard Tree 10");
            Carve(terrain, data, shore, min - Vector2.one*2.4f, max + Vector2.one*2.4f, report);

            var pondGo = new GameObject(FarmPondV1.RootName);
            Undo.RegisterCreatedObjectUndo(pondGo, "Build farm pond at Yard Tree 10");
            pondGo.transform.SetParent(root.transform, false);
            var centre = (min + max)*.5f;
            pondGo.transform.position = new Vector3(centre.x, FarmPondV1.WaterLevel, centre.y);

            var water = FarmPondV1.EnsureMaterial("PondWater", "Chick/PondWater", null);
            var droplet = FarmPondV1.EnsureMaterial("PondDroplet", "Chick/PondDroplet", null);
            var reedMat = FarmPondV1.EnsureMaterial("PondReeds", "Chick/DistantMountain", m => { m.SetColor("_BaseColor", Color.white); m.SetFloat("_HazeStrength", 0); m.SetFloat("_FoliageBend", 1); m.EnableKeyword("_FOLIAGE_BEND"); });

            var surface = new GameObject("Water Surface");
            surface.transform.SetParent(pondGo.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = FarmPondV1.SaveMesh(FarmPondV1.WaterMesh(shore, min, max, pondGo.transform.position));
            var surfaceRenderer = surface.AddComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = water;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off; surfaceRenderer.receiveShadows = true;
            surfaceRenderer.lightProbeUsage = LightProbeUsage.Off; surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

            // Well inside the farm's fences, so every shore spot may be dressed.
            FarmPondV1.Decorate(pondGo.transform, terrain, shore, reedMat, root.transform, report, p => false);

            var pond = pondGo.AddComponent<FarmPond>();
            pond.Configure(terrain, shore, ShoreMargin, droplet, hidden);
            if (player.GetComponent<ChickWaterWading>() == null) Undo.AddComponent<ChickWaterWading>(player.gameObject);
            if (player.GetComponent<ChickDrinkingController>() == null) Undo.AddComponent<ChickDrinkingController>(player.gameObject);
            Undo.CollapseUndoOperations(group);

            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);
            report.Insert(0, "Pond built at Yard Tree 10: " + (max.x - min.x).ToString("F1") + " x " + (max.y - min.y).ToString("F1") + " m, water level " + FarmPondV1.WaterLevel + ".\n");
            return report.ToString();
        }

        // FarmPondV1.Ground without its old fence lines: a level basin below the water line and a bank that meets
        // the water at the shore, carved where the ground is higher and built up into a low berm where it is lower.
        static float Ground(float h0, float sd, float x, float z)
        {
            float water = FarmPondV1.WaterLevel;
            if (sd < 0)
            {
                float inside = -sd;
                float bowl = MaxDepth*FarmPondV1.S(0, 1.5f, inside);
                float wobble = (Mathf.PerlinNoise(x*.9f + 11, z*.9f + 3) - .5f)*.018f*FarmPondV1.S(.3f, 1.2f, inside);
                return water - .006f*FarmPondV1.S(0, .2f, inside) - bowl + wobble;
            }
            float bank = water + Freeboard*FarmPondV1.S(0, .45f, sd);
            if (h0 >= bank) { float w = Mathf.Clamp((h0 - water)*4.5f, .6f, 1.5f); return Mathf.Lerp(bank, h0, FarmPondV1.S(0, w, sd)); }
            float wide = Mathf.Clamp((bank - h0)*5f, .6f, 1.6f);
            return Mathf.Lerp(bank, h0, FarmPondV1.S(.55f, .55f + wide, sd));
        }

        // FarmPondV1.CarveTerrain on the terrain itself: basin and bank heights, wet earth and pebbles, no grass in the water.
        static void Carve(Terrain terrain, TerrainData data, Vector2[] shore, Vector2 min, Vector2 max, System.Text.StringBuilder report)
        {
            Vector3 tp = terrain.transform.position, size = data.size;
            int res = data.heightmapResolution;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*(res-1))), x1 = Mathf.Min(res-1, Mathf.CeilToInt((max.x - tp.x)/size.x*(res-1)));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*(res-1))), z1 = Mathf.Min(res-1, Mathf.CeilToInt((max.y - tp.z)/size.z*(res-1)));
            var h = data.GetHeights(x0, z0, x1-x0+1, z1-z0+1);
            float lowest = float.PositiveInfinity, raised = 0, carved = 0;
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = tp.x + (x0+ix)/(float)(res-1)*size.x, z = tp.z + (z0+iz)/(float)(res-1)*size.z;
                float h0 = h[iz, ix]*size.y + tp.y;
                float sd = FarmPondV1.Signed(shore, x, z), y = Ground(h0, sd, x, z);
                if (sd < 0) lowest = Mathf.Min(lowest, y);
                raised = Mathf.Max(raised, y - h0); carved = Mathf.Max(carved, h0 - y);
                h[iz, ix] = (y - tp.y)/size.y;
            }
            data.SetHeights(x0, z0, h);
            report.AppendLine("Terrain: deepest bed " + (FarmPondV1.WaterLevel - lowest).ToString("F3") + " m below water, bank carved up to " + carved.ToString("F2") + " m, berm raised up to " + raised.ToString("F2") + " m.");

            int ares = data.alphamapResolution, layers = data.alphamapLayers;
            int ax0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*ares)), ax1 = Mathf.Min(ares-1, Mathf.CeilToInt((max.x - tp.x)/size.x*ares));
            int az0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*ares)), az1 = Mathf.Min(ares-1, Mathf.CeilToInt((max.y - tp.z)/size.z*ares));
            var a = data.GetAlphamaps(ax0, az0, ax1-ax0+1, az1-az0+1);
            var target = new float[layers];
            for (int iz = 0; iz <= az1-az0; iz++) for (int ix = 0; ix <= ax1-ax0; ix++)
            {
                float x = tp.x + (ax0+ix+.5f)/ares*size.x, z = tp.z + (az0+iz+.5f)/ares*size.z;
                float sd = FarmPondV1.Signed(shore, x, z), n = Mathf.PerlinNoise(x*1.7f + 5, z*1.7f + 9);
                Array.Clear(target, 0, layers);
                if (sd < -.25f) { target[Soil] = .4f; target[WornMeadow] = .22f; target[Pebbles] = .13f + .25f*n; target[TroddenEarth] = .25f - .25f*n; }
                else { target[Soil] = .48f; target[TroddenEarth] = .3f; target[Pebbles] = .08f + .1f*n; target[WornMeadow] = .14f; }
                float blend = 1 - FarmPondV1.S(-.05f, .55f, sd + (n - .5f)*.3f);
                if (blend <= 0) continue;
                float sum = 0;
                for (int k = 0; k < layers; k++) { a[iz, ix, k] = Mathf.Lerp(a[iz, ix, k], target[k], blend); sum += a[iz, ix, k]; }
                for (int k = 0; k < layers; k++) a[iz, ix, k] /= Mathf.Max(sum, 1e-5f);
            }
            data.SetAlphamaps(ax0, az0, a);

            int dres = data.detailWidth, cleared = 0;
            int dx0 = Mathf.Max(0, Mathf.FloorToInt((min.x - tp.x)/size.x*dres)), dx1 = Mathf.Min(dres-1, Mathf.CeilToInt((max.x - tp.x)/size.x*dres));
            int dz0 = Mathf.Max(0, Mathf.FloorToInt((min.y - tp.z)/size.z*data.detailHeight)), dz1 = Mathf.Min(data.detailHeight-1, Mathf.CeilToInt((max.y - tp.z)/size.z*data.detailHeight));
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                var m = data.GetDetailLayer(dx0, dz0, dx1-dx0+1, dz1-dz0+1, layer);
                for (int iz = 0; iz <= dz1-dz0; iz++) for (int ix = 0; ix <= dx1-dx0; ix++)
                {
                    if (m[iz, ix] == 0) continue;
                    float x = tp.x + (dx0+ix+.5f)/dres*size.x, z = tp.z + (dz0+iz+.5f)/data.detailHeight*size.z;
                    float sd = FarmPondV1.Signed(shore, x, z);
                    if (sd < .35f || (sd < .9f && Mathf.PerlinNoise(x*3.1f, z*3.1f) < .55f)) { cleared += m[iz, ix]; m[iz, ix] = 0; }
                }
                data.SetDetailLayer(dx0, dz0, layer, m);
            }
            report.AppendLine("Detail grass instances cleared from water and wet bank: " + cleared + ".");
        }

        // Scenery standing in the water or on the wet bank is disabled, not deleted; removing the pond shows it again.
        static GameObject[] HideCovered(Transform farm, Vector2[] shore, System.Text.StringBuilder report)
        {
            var hidden = new List<GameObject>();
            var nature = farm.Find("05 Nature and Scenery");
            if (nature != null)
                foreach (Transform child in nature)
                {
                    if (!child.gameObject.activeSelf) continue;
                    var p = child.position;
                    if (FarmPondV1.Signed(shore, p.x, p.z) > .6f) continue;
                    Undo.RecordObject(child.gameObject, "Build farm pond at Yard Tree 10");
                    child.gameObject.SetActive(false);
                    hidden.Add(child.gameObject);
                }
            var tree = GameObject.Find(Tree);
            if (tree != null && !hidden.Contains(tree)) { Undo.RecordObject(tree, "Build farm pond at Yard Tree 10"); tree.SetActive(false); hidden.Add(tree); }
            var names = new List<string>(); foreach (var go in hidden) names.Add(go.name);
            report.AppendLine("Covered by the pond (disabled, not deleted): " + (names.Count == 0 ? "nothing" : string.Join(", ", names)) + ".");
            return hidden.ToArray();
        }
    }
}
