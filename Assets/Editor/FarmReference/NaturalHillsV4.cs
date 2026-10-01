using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Rebuilds V4 from the untouched V3 terrain on every run,
    // so it can be tuned and re-applied. Widens the map 50 m west and east, turns the steep
    // perimeter walls into long natural hills, and ends the north side in a flat meadow and forest.
    public static class NaturalHillsV4
    {
        const string Source = "Assets/Art/Terrain/MeadowVarietyV3/FarmTerrain_MeadowVariety_v3.asset";
        const string Folder = "Assets/Art/Terrain/NaturalHillsV4";
        const string Target = Folder + "/FarmTerrain_NaturalHills_v4.asset";
        const string GeneratedGroup = "Hill Expansion Scenery";
        // Root-local layout (the root sits at the fence centre line).
        const float FenceX = 35.2f, FenceNorth = 36.2f, FenceSouth = -32.2f;
        const float Ground = 2.3f;                  // terrain-local height of the farm floor
        const float HalfWidth = 135f;               // V3 was 85 m; +50 m west and east
        const float MinZ = -80f, Depth = 160f, MaxHeight = 30f;
        const int HeightRes = 1025, AlphaRes = 1024;

        static TerrainData source; static float sourceHalf; static float[,,] sourceAlpha;

        static float Smooth(float a, float b, float x) { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x)); }
        static float Noise(float x, float z, float f, float ox, float oz) { return Mathf.PerlinNoise(x*f+ox, z*f+oz); }

        static float SourceHeight(float x, float z)
        {
            float u = Mathf.Clamp01((x+sourceHalf)/(2*sourceHalf)), v = Mathf.Clamp01((z-MinZ)/Depth);
            return source.GetInterpolatedHeight(u, v);
        }

        // Behind the barn: barely changing meadow, rising ~4.5 m under the forest so the edge stays hidden.
        static float NorthFloor(float x, float z)
        {
            float n = (Noise(x,z,.045f,13.1f,77.3f)-.5f)*.9f;
            return Ground + n*Smooth(FenceNorth, FenceNorth+12, z) + 4.5f*Smooth(56, 80, z);
        }

        static float SideHill(float x, float z, float baseHeight)
        {
            float d = Mathf.Abs(x) - FenceX;
            float side = x < 0 ? 0 : 100;           // decorrelate the west and east ridges
            float foot = 6f + 5f*Noise(z, side, .03f, 3.3f, .37f);
            float top = 58f + 16f*Noise(z, side, .022f, 41.7f, .61f);
            float peak = Ground + 17f + 6f*Noise(z, side, .028f, 8.9f, .83f);
            float rise = Smooth(foot, top, d);
            // Rolling crest and slope texture so the hills don't read as one straight ramp.
            float roll = (Noise(x, z, .035f, 55.5f+side, 21.2f)-.5f)*5f*Smooth(top-20, top+15, d);
            float ripple = (Noise(x, z, .09f, 7.7f+side, 91.4f)-.5f)*1.2f*rise;
            return Mathf.Lerp(baseHeight, peak, rise) + roll + ripple;
        }

        public static float Height(float x, float z)
        {
            float wN = Smooth(0, 6, z-FenceNorth), wS = Smooth(0, 6, FenceSouth-z), wSide = Smooth(0, 6, Mathf.Abs(x)-FenceX);
            float north = NorthFloor(x, z);
            float h = Mathf.Lerp(SourceHeight(x, z), north, wN);
            // The south edge keeps its V3 shape; the side hills continue its profile outward.
            float baseHeight = Mathf.Lerp(Mathf.Lerp(Ground, north, wN), SourceHeight(Mathf.Clamp(x,-FenceX,FenceX), z), wS);
            return Mathf.Clamp(Mathf.Lerp(h, SideHill(x, z, baseHeight), wSide), 0, MaxHeight);
        }

        static bool InsideFence(float x, float z) { return Mathf.Abs(x) <= FenceX+.5f && z >= FenceSouth-.5f && z <= FenceNorth+.5f; }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            if (root == null) throw new InvalidOperationException("Farm root missing.");
            var terrain = root.GetComponentInChildren<Terrain>();
            source = AssetDatabase.LoadAssetAtPath<TerrainData>(Source);
            if (terrain == null || source == null) throw new InvalidOperationException("Terrain or V3 source missing.");
            sourceHalf = source.size.x*.5f;
            sourceAlpha = source.GetAlphamaps(0, 0, source.alphamapWidth, source.alphamapHeight);
            var nature = root.transform.Find("05 Nature and Scenery");
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Apply natural hills V4");

            var old = nature.Find(GeneratedGroup);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            // Remember each scenery object's ground offset on the terrain it currently stands on.
            var resnap = new List<KeyValuePair<Transform,float>>();
            foreach (Transform c in nature)
            {
                var p = root.transform.InverseTransformPoint(c.position);
                if (!InsideFence(p.x, p.z)) resnap.Add(new KeyValuePair<Transform,float>(c, c.position.y - terrain.transform.position.y - terrain.SampleHeight(c.position)));
            }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Terrain", "NaturalHillsV4");
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(Target);
            if (data == null) { data = new TerrainData(); data.name = "FarmTerrain_NaturalHills_v4"; AssetDatabase.CreateAsset(data, Target); }
            data.heightmapResolution = HeightRes;
            data.size = new Vector3(HalfWidth*2, MaxHeight, Depth);
            data.alphamapResolution = AlphaRes; data.baseMapResolution = 1024;
            data.terrainLayers = source.terrainLayers;
            var heights = new float[HeightRes, HeightRes];
            for (int iz = 0; iz < HeightRes; iz++) for (int ix = 0; ix < HeightRes; ix++)
                heights[iz, ix] = Height(-HalfWidth + ix*2*HalfWidth/(HeightRes-1), MinZ + iz*Depth/(HeightRes-1))/MaxHeight;
            data.SetHeights(0, 0, heights);

            Undo.RecordObject(terrain.transform, "Move widened terrain");
            terrain.transform.position = new Vector3(root.transform.position.x-HalfWidth, terrain.transform.position.y, root.transform.position.z+MinZ);
            Undo.RecordObject(terrain, "Assign natural hills V4");
            terrain.terrainData = data;
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) { Undo.RecordObject(collider, "Match terrain collider V4"); collider.terrainData = data; }
            terrain.Flush();

            foreach (var kv in resnap)
            {
                Undo.RecordObject(kv.Key, "Resnap scenery to V4");
                var p = kv.Key.position; p.y = terrain.transform.position.y + terrain.SampleHeight(p) + kv.Value; kv.Key.position = p;
                // Offsets kept from the old steep slopes can leave trunks hovering on the gentler ground.
                var r = kv.Key.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                float gap = r.bounds.min.y - terrain.transform.position.y - terrain.SampleHeight(r.bounds.center);
                if (gap > 0) kv.Key.position -= Vector3.up*(gap + .02f*r.bounds.size.y);
            }
            var created = Scatter(root.transform, nature, terrain);

            var trees = new List<Vector2>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0)
                { var p = root.transform.InverseTransformPoint(r.bounds.center); trees.Add(new Vector2(p.x, p.z)); }
            data.SetAlphamaps(0, 0, Alphamaps(trees)); data.SetBaseMapDirty();
            if (GameObject.Find("Farm Reference Map/02 Crop Fields/Mixed Crop Fields") != null) MixedCropFieldsV1.PaintSoil(terrain);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Resnapped " + resnap.Count + " scenery objects; created " + created + ".\n" + Validate();
        }

        // Seeded, repeatable scatter: a forest band behind the barn and scenery on the new hill strips.
        static string Scatter(Transform root, Transform nature, Terrain terrain)
        {
            var group = new GameObject(GeneratedGroup); Undo.RegisterCreatedObjectUndo(group, "Create hill scenery");
            group.transform.SetParent(nature, false); group.isStatic = true;
            var templates = new Dictionary<string, List<Transform>>();
            var occupied = new List<Vector3>();       // x, z, radius of every tree
            foreach (Transform c in nature)
            {
                string key = c.name.StartsWith("Scenery Tree") ? "tree" : c.name.StartsWith("Scenery Rock") ? "rock" : c.name.StartsWith("Boundary Shrub") ? "shrub" : c.name.StartsWith("Meadow") ? "meadow" : null;
                var p = root.InverseTransformPoint(c.position);
                if (key == "tree" || c.name.StartsWith("Yard Tree")) occupied.Add(new Vector3(p.x, p.z, 3.2f));
                if (key == null || InsideFence(p.x, p.z) && key != "meadow") continue;
                if (!templates.ContainsKey(key)) templates[key] = new List<Transform>();
                templates[key].Add(c);
            }
            foreach (var k in new[]{"tree","rock","shrub","meadow"}) if (!templates.ContainsKey(k)) throw new InvalidOperationException("No template for " + k);
            var rng = new System.Random(4242); int count = 0;
            Func<float, float, float> range = (a, b) => a + (float)rng.NextDouble()*(b-a);
            Func<float, float, float, bool> free = (x, z, r) => { foreach (var o in occupied) if (new Vector2(o.x-x, o.y-z).sqrMagnitude < Mathf.Pow(Mathf.Max(r, o.z), 2)) return false; return true; };
            float boost = 1f;                         // extra scale for the tall back rows of the forest
            Action<string, string, float, float, float> place = (kind, label, x, z, sink) =>
            {
                var list = templates[kind]; var t = list[rng.Next(list.Count)];
                GameObject go;
                var prefab = PrefabUtility.IsPartOfPrefabInstance(t.gameObject) ? PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) : null;
                go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform) : UnityEngine.Object.Instantiate(t.gameObject, group.transform);
                go.name = label + " " + count.ToString("000");
                float k = range(.85f, 1.15f)*boost;
                var bounds = t.GetComponentInChildren<Renderer>().bounds;
                float toBottom = (t.position.y - bounds.min.y)*k, footprint = Mathf.Min(bounds.extents.x, bounds.extents.z)*k*(kind == "tree" ? .08f : .5f);
                var world = root.TransformPoint(new Vector3(x, 0, z)); float ground = float.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    float a = i*Mathf.PI*.5f, r = i == 0 ? 0 : footprint;
                    ground = Mathf.Min(ground, terrain.SampleHeight(world + new Vector3(Mathf.Cos(a)*r, 0, Mathf.Sin(a)*r)));
                }
                go.transform.position = new Vector3(world.x, terrain.transform.position.y + ground + toBottom - sink*bounds.size.y*k, world.z);
                go.transform.rotation = Quaternion.Euler(t.eulerAngles.x, range(0, 360), t.eulerAngles.z);
                go.transform.localScale = t.localScale*k;
                Undo.RegisterCreatedObjectUndo(go, "Create hill scenery"); count++;
            };

            // Forest band behind the barn with an irregular front edge, closing off the map end.
            for (int attempt = 0, placed = 0; attempt < 12000 && placed < 320; attempt++)
            {
                float x = range(-54, 54), z = range(52, 79.5f);
                if (z < 55 + 5*Noise(x, 0, .06f, 3.1f, 7.4f)) continue;
                if (!free(x, z, 3.2f)) continue;
                boost = 1f + .45f*Smooth(62, 80, z);
                place("tree", "Forest Tree", x, z, .02f); boost = 1f; occupied.Add(new Vector3(x, z, 3.2f)); placed++;
            }
            for (int attempt = 0, placed = 0; attempt < 400 && placed < 18; attempt++)
            {
                float x = range(-46, 46), z = 52 + 5*Noise(x, 0, .06f, 3.1f, 7.4f) + range(-1.5f, 1.5f);
                if (!free(x, z, 2.2f)) continue;
                place("shrub", "Forest Edge Shrub", x, z, .1f); placed++;
            }
            // New west and east strips: groves, rocks, shrubs and meadow tufts at the V3 hill density.
            foreach (float s in new[]{-1f, 1f})
            {
                Func<float> sx = () => s*range(HalfWidth-49.5f, HalfWidth-1.5f);
                for (int attempt = 0, placed = 0; attempt < 3000 && placed < 46; attempt++)
                {
                    float x = sx(), z = range(MinZ+1.5f, MinZ+Depth-1.5f);
                    if (Noise(x, z, .04f, 61.2f, 12.9f) < range(.3f, .62f) || !free(x, z, 6f)) continue;
                    place("tree", "Hill Tree", x, z, .02f); occupied.Add(new Vector3(x, z, 6f)); placed++;
                }
                for (int placed = 0; placed < 30; placed++) { float x = sx(), z = range(MinZ+1.5f, MinZ+Depth-1.5f); place("rock", "Hill Rock", x, z, .2f); }
                for (int placed = 0; placed < 10; placed++) { float x = sx(), z = range(MinZ+1.5f, MinZ+Depth-1.5f); if (free(x, z, 2f)) place("shrub", "Hill Shrub", x, z, .1f); }
                for (int placed = 0; placed < 45; placed++) { float x = sx(), z = range(MinZ+1.5f, MinZ+Depth-1.5f); place("meadow", "Hill Meadow", x, z, .02f); }
            }
            return count + " objects";
        }

        static float SampleSource(float x, float z, int layer)
        {
            int w = source.alphamapWidth, h = source.alphamapHeight;
            float fx = Mathf.Clamp01((x+sourceHalf)/(2*sourceHalf))*(w-1), fz = Mathf.Clamp01((z-MinZ)/Depth)*(h-1);
            int x0 = Mathf.Min((int)fx, w-2), z0 = Mathf.Min((int)fz, h-2); float tx = fx-x0, tz = fz-z0;
            return Mathf.Lerp(Mathf.Lerp(sourceAlpha[z0,x0,layer], sourceAlpha[z0,x0+1,layer], tx), Mathf.Lerp(sourceAlpha[z0+1,x0,layer], sourceAlpha[z0+1,x0+1,layer], tx), tz);
        }

        // V3 weights are kept where they exist; the new strips use V3's grass formula, blended over 6 m.
        static float[,,] Alphamaps(List<Vector2> trees)
        {
            int layers = source.alphamapLayers;
            int gw = Mathf.CeilToInt(HalfWidth*2)+1, gh = Mathf.CeilToInt(Depth)+1;
            var treeBias = new float[gh, gw];
            for (int z = 0; z < gh; z++) for (int x = 0; x < gw; x++)
            {
                float px = x-HalfWidth, pz = z+MinZ;
                if (Mathf.Abs(px) < sourceHalf-8) continue;
                float best = 1e6f; var q = new Vector2(px, pz);
                foreach (var t in trees) best = Mathf.Min(best, (q-t).sqrMagnitude);
                treeBias[z, x] = 1-Smooth(3, 11, Mathf.Sqrt(best));
            }
            var weights = new float[AlphaRes, AlphaRes, layers];
            var formula = new float[layers];
            for (int iz = 0; iz < AlphaRes; iz++) for (int ix = 0; ix < AlphaRes; ix++)
            {
                float x = -HalfWidth + ix*2*HalfWidth/(AlphaRes-1), z = MinZ + iz*Depth/(AlphaRes-1);
                float blend = Smooth(sourceHalf-6, sourceHalf, Mathf.Abs(x));
                if (blend > 0)
                {
                    int gx = Mathf.Clamp(Mathf.RoundToInt(x+HalfWidth), 0, gw-1), gz = Mathf.Clamp(Mathf.RoundToInt(z-MinZ), 0, gh-1);
                    float n = Mathf.PerlinNoise(x*.043f+19.2f, z*.043f+31.6f), n2 = Mathf.PerlinNoise(x*.058f+82.7f, z*.058f+9.3f);
                    float lush = Smooth(.38f, .64f, n+treeBias[gz, gx]*.21f), sun = Smooth(.42f, .67f, n2)*(1-lush), fresh = Mathf.Max(.04f, 1-lush-sun);
                    lush = lush*lush; sun = sun*sun; fresh = fresh*fresh; float sum = lush+sun+fresh;
                    float wear = Smooth(.69f, .83f, Mathf.PerlinNoise(x*.095f+71, z*.095f+45))*.45f, grass = 1-wear;
                    Array.Clear(formula, 0, layers);
                    formula[0] = grass*.09f; formula[1] = grass*.91f*lush/sum; formula[2] = grass*.91f*fresh/sum; formula[3] = grass*.91f*sun/sum; formula[7] = wear;
                }
                float total = 0;
                for (int i = 0; i < layers; i++)
                {
                    float a = blend < 1 ? SampleSource(x, z, i) : 0;
                    weights[iz, ix, i] = Mathf.Lerp(a, formula[i], blend); total += weights[iz, ix, i];
                }
                for (int i = 0; i < layers; i++) weights[iz, ix, i] /= total;
            }
            return weights;
        }

        public static string Validate()
        {
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root.GetComponentInChildren<Terrain>(); var data = terrain.terrainData;
            if (source == null) { source = AssetDatabase.LoadAssetAtPath<TerrainData>(Source); sourceHalf = source.size.x*.5f; }
            float farmDelta = 0, maxSlope = 0, peak = 0, northRange = 0, northMin = 99, northMax = 0;
            for (float z = MinZ+1; z < MinZ+Depth-1; z += .5f) for (float x = -HalfWidth+1; x < HalfWidth-1; x += .5f)
            {
                var w = root.transform.TransformPoint(new Vector3(x, 0, z));
                float h = terrain.SampleHeight(w);
                if (InsideFence(x, z)) farmDelta = Mathf.Max(farmDelta, Mathf.Abs(h - SourceHeight(x, z)));
                else
                {
                    var n = data.GetInterpolatedNormal((w.x-terrain.transform.position.x)/data.size.x, (w.z-terrain.transform.position.z)/data.size.z);
                    maxSlope = Mathf.Max(maxSlope, Vector3.Angle(n, Vector3.up)); peak = Mathf.Max(peak, h);
                    if (z > FenceNorth+2 && Mathf.Abs(x) < FenceX) { northMin = Mathf.Min(northMin, h); northMax = Mathf.Max(northMax, h); }
                }
            }
            northRange = northMax - northMin;
            var a = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight); float maxError = 0; int invalid = 0;
            for (int z = 0; z < data.alphamapHeight; z++) for (int x = 0; x < data.alphamapWidth; x++)
            {
                float sum = 0; for (int i = 0; i < data.alphamapLayers; i++) { float f = a[z,x,i]; if (float.IsNaN(f) || f < 0) invalid++; sum += f; }
                maxError = Mathf.Max(maxError, Mathf.Abs(sum-1));
            }
            var sb = new StringBuilder();
            sb.AppendLine("Asset=" + AssetDatabase.GetAssetPath(data) + " size=" + data.size + " pos=" + terrain.transform.position);
            sb.AppendLine("ColliderMatches=" + (terrain.GetComponent<TerrainCollider>().terrainData == data));
            sb.AppendLine("Inside-fence height change vs V3 (m)=" + farmDelta.ToString("F3"));
            sb.AppendLine("Outside max slope (deg)=" + maxSlope.ToString("F1") + "; highest point above farm (m)=" + (peak-Ground).ToString("F1"));
            sb.AppendLine("North middle height range (m)=" + northRange.ToString("F2") + " (" + (northMin-Ground).ToString("F2") + ".." + (northMax-Ground).ToString("F2") + " above farm)");
            sb.AppendLine("invalidWeights=" + invalid + "; maxWeightError=" + maxError);
            return sb.ToString();
        }
    }
}
