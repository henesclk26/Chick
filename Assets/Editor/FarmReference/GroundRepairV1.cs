using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Smooths three leftovers in the farm's ground:
    //  - the old pond's hollow (x 85..93, z -1..10, ~35 cm deep; older than the pond, which was dug into it) and a small
    //    dip by Gate 1's west post: filled up, never lowered;
    //  - a long mound under Meadow 42 (x 70.5..77.5, z 3..11, ~36 cm high) on otherwise level ground: cut down;
    //  - a thin groove along z ≈ -6 (x 86..110, 3-6 cm, one or two heightmap rows) under Meadow 156: filled in.
    // Hollow and mound are rebuilt as the smooth surface spanned by their surrounding ground, so they blend in.
    // Scenery standing there is moved with the ground; run the fence snap afterwards for the fences.
    public static class GroundRepairV1
    {
        const string Menu = "Tools/Chick/Ground/";
        static readonly Rect OldPondHollow = Rect.MinMaxRect(78.5f, -2.6f, 95f, 11.6f);
        static readonly Rect Meadow42Mound = Rect.MinMaxRect(69.5f, 2.3f, 78.5f, 11.7f);
        const float GrooveZ = -5.9f, GrooveHalf = .7f, GrooveMinX = 86f, GrooveMaxX = 110f;

        [MenuItem(Menu + "Repair Old Pond Hollow, Meadow Mound And Groove (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;

            // Remember the ground under the scenery that may be affected, to move it by the same amount.
            var scenery = new List<Transform>();
            var before = new List<float>();
            var nature = root.transform.Find("05 Nature and Scenery");
            if (nature != null)
                foreach (Transform child in nature)
                {
                    var p = child.position;
                    if (!OldPondHollow.Contains(new Vector2(p.x, p.z)) && !Meadow42Mound.Contains(new Vector2(p.x, p.z)) &&
                        !(p.x >= GrooveMinX && p.x <= GrooveMaxX && Mathf.Abs(p.z - GrooveZ) <= GrooveHalf)) continue;
                    scenery.Add(child);
                    before.Add(terrain.SampleHeight(p) + terrain.transform.position.y);
                }

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Repair farm ground V1"); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(data, "Repair farm ground V1");
            float filled = Span(terrain, OldPondHollow, +1);
            float cut = Span(terrain, Meadow42Mound, -1);
            float groove = FillGroove(terrain);

            int moved = 0;
            for (int i = 0; i < scenery.Count; i++)
            {
                float delta = terrain.SampleHeight(scenery[i].position) + terrain.transform.position.y - before[i];
                if (Mathf.Abs(delta) < .01f) continue;
                Undo.RecordObject(scenery[i], "Repair farm ground V1");
                scenery[i].position += Vector3.up*delta;
                moved++;
            }
            Undo.CollapseUndoOperations(group);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            if (moved > 0) EditorSceneManager.MarkSceneDirty(root.scene);
            return "Ground repaired: old pond hollow filled up to " + (filled*100).ToString("F0") + " cm, Meadow 42 mound cut down up to " +
                   (cut*100).ToString("F0") + " cm, z -6 groove filled up to " + (groove*100).ToString("F0") + " cm; " + moved + " scenery objects moved with the ground.";
        }

        // Replaces the area with the smooth surface spanned by its border (relaxed until it settles), keeping only
        // raises (sign +1) or only cuts (sign -1). Returns the largest change in metres.
        static float Span(Terrain terrain, Rect area, int sign)
        {
            var data = terrain.terrainData;
            Vector3 tp = terrain.transform.position, size = data.size;
            int r = data.heightmapResolution;
            int x0 = Mathf.Max(0, Mathf.RoundToInt((area.xMin - tp.x)/size.x*(r-1))), x1 = Mathf.Min(r-1, Mathf.RoundToInt((area.xMax - tp.x)/size.x*(r-1)));
            int z0 = Mathf.Max(0, Mathf.RoundToInt((area.yMin - tp.z)/size.z*(r-1))), z1 = Mathf.Min(r-1, Mathf.RoundToInt((area.yMax - tp.z)/size.z*(r-1)));
            int w = x1-x0+1, h = z1-z0+1;
            var heights = data.GetHeights(x0, z0, w, h);
            var surface = (float[,])heights.Clone();
            // The heightmap cells are 0.264 x 0.156 m: weight the neighbours by their distance so the surface is even.
            float cx = size.x/(r-1), cz = size.z/(r-1);
            float wx = 1/(cx*cx), wz = 1/(cz*cz), total = 2*wx + 2*wz;
            for (int iteration = 0; iteration < 3000; iteration++)
            {
                float change = 0;
                for (int iz = 1; iz < h-1; iz++) for (int ix = 1; ix < w-1; ix++)
                {
                    float v = (wx*(surface[iz, ix-1] + surface[iz, ix+1]) + wz*(surface[iz-1, ix] + surface[iz+1, ix]))/total;
                    float next = surface[iz, ix] + 1.9f*(v - surface[iz, ix]);   // over-relaxed for speed
                    change = Mathf.Max(change, Mathf.Abs(next - surface[iz, ix]));
                    surface[iz, ix] = next;
                }
                if (change*size.y < 1e-5f) break;
            }
            float largest = 0;
            for (int iz = 1; iz < h-1; iz++) for (int ix = 1; ix < w-1; ix++)
            {
                float target = sign > 0 ? Mathf.Max(heights[iz, ix], surface[iz, ix]) : Mathf.Min(heights[iz, ix], surface[iz, ix]);
                largest = Mathf.Max(largest, Mathf.Abs(target - heights[iz, ix])*size.y);
                heights[iz, ix] = target;
            }
            data.SetHeights(x0, z0, heights);
            return largest;
        }

        // Each row across the groove gets the straight line between the ground just north and just south of it.
        static float FillGroove(Terrain terrain)
        {
            var data = terrain.terrainData;
            Vector3 tp = terrain.transform.position, size = data.size;
            int r = data.heightmapResolution;
            int x0 = Mathf.RoundToInt((GrooveMinX - tp.x)/size.x*(r-1)), x1 = Mathf.RoundToInt((GrooveMaxX - tp.x)/size.x*(r-1));
            int z0 = Mathf.RoundToInt((GrooveZ - GrooveHalf - tp.z)/size.z*(r-1)), z1 = Mathf.RoundToInt((GrooveZ + GrooveHalf - tp.z)/size.z*(r-1));
            int w = x1-x0+1, h = z1-z0+1;
            var heights = data.GetHeights(x0, z0, w, h);
            float largest = 0;
            for (int ix = 0; ix < w; ix++)
            {
                float south = heights[0, ix], north = heights[h-1, ix];
                // Fade in over the first and last metre so the repair has no edge of its own.
                float x = tp.x + (x0+ix)*size.x/(r-1);
                float fade = Mathf.Min(Mathf.Clamp01((x - GrooveMinX)/1f), Mathf.Clamp01((GrooveMaxX - x)/1f));
                for (int iz = 1; iz < h-1; iz++)
                {
                    float line = Mathf.Lerp(south, north, iz/(float)(h-1));
                    float target = Mathf.Lerp(heights[iz, ix], line, fade);
                    largest = Mathf.Max(largest, Mathf.Abs(target - heights[iz, ix])*size.y);
                    heights[iz, ix] = target;
                }
            }
            data.SetHeights(x0, z0, heights);
            return largest;
        }
    }
}
