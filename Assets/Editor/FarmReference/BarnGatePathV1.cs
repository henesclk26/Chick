using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. The barn-to-field fence and its gate moved north (to z ~10), but the dirt
    // path through it is painted into the terrain along OrganicFarmPaths' route for the old gate, so it crossed the
    // new fence under its rails, west of the gate. This turns that stretch back into meadow and paints the path
    // through the middle of the gate down to the crop field, in the same style (width, worn shoulder, soil mix).
    public static class BarnGatePathV1
    {
        const string Menu = "Tools/Chick/Crop Field/";
        const int Trodden = 4, OldEarth = 5, Worn = 7;

        struct Waypoint
        {
            public Vector2 Point; public float Width, Shoulder;
            public Waypoint(float x, float z, float width, float shoulder) { Point = new Vector2(x, z); Width = width; Shoulder = shoulder; }
        }

        // World XZ. OrganicFarmPaths.MainRoute from the barn road to its old gate at (82.2, 0).
        static readonly Waypoint[] OldRoute =
        {
            new Waypoint(76.2f, 12f, 1.28f, .72f), new Waypoint(78.1f, 9.5f, 1.22f, .66f),
            new Waypoint(80.6f, 5.4f, 1.17f, .61f), new Waypoint(82.2f, 1.8f, 1.30f, .78f), new Waypoint(82.2f, 0f, 1.36f, .82f)
        };
        // From the barn-to-coop road just north of Gate 1 (posts at x 79.7 and 84.7), straight south through its east
        // half, down to the field's north edge. Gate 1's leaf stands open to the north-west of the opening
        // (x 79.8..81.9, z 9.4..13.6); a route curving in from the west ran into it.
        static readonly Waypoint[] NewRoute =
        {
            new Waypoint(83.6f, 15.6f, 1.28f, .72f), new Waypoint(83.5f, 14.2f, 1.22f, .66f),
            new Waypoint(83.2f, 12.2f, 1.20f, .64f), new Waypoint(82.9f, 10.0f, 1.26f, .70f),
            new Waypoint(82.5f, 7.5f, 1.20f, .64f), new Waypoint(82.0f, 5.0f, 1.18f, .62f), new Waypoint(81.6f, 3.2f, 1.30f, .78f)
        };
        // The barn road the path leaves from stays as it is above this; the crop field's soil below the other.
        const float RoadZ = 11.4f, FieldZ = 3.2f;

        [MenuItem(Menu + "Route Barn Path Through Gate (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            if (data.alphamapLayers < 8 || data.terrainLayers[Trodden].name != "TroddenEarth")
                throw new InvalidOperationException("Unexpected terrain layers; this tool expects the MeadowVarietyV3 set.");

            var oldSegments = Sample(OldRoute);
            var newSegments = Sample(NewRoute);
            var trees = CropFieldRelocationV1.Trees(root);
            int w = data.alphamapWidth, h = data.alphamapHeight;
            var origin = terrain.transform.position;
            int x0 = Mathf.Max(0, (int)((72f - origin.x)/data.size.x*(w-1))), x1 = Mathf.Min(w-1, (int)((87f - origin.x)/data.size.x*(w-1)) + 1);
            int z0 = Mathf.Max(0, (int)((1f - origin.z)/data.size.z*(h-1))), z1 = Mathf.Min(h-1, (int)((15.5f - origin.z)/data.size.z*(h-1)) + 1);

            Undo.RegisterCompleteObjectUndo(data.alphamapTextures, "Route barn path through gate V1");
            var a = data.GetAlphamaps(x0, z0, x1-x0+1, z1-z0+1);
            int layers = data.alphamapLayers;
            var meadow = new float[layers];
            var target = new float[layers];
            int restored = 0, painted = 0;
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = origin.x + (ix+x0)*data.size.x/(w-1), z = origin.z + (iz+z0)*data.size.z/(h-1);
                var world = new Vector2(x, z);
                var p = root.transform.InverseTransformPoint(new Vector3(x, 0, z));
                CropFieldRelocationV1.Meadow(p, trees, meadow);

                // The old stretch, core and worn shoulder, between the road and the field.
                Evaluate(oldSegments, world, out float oldCore, out float oldShoulder);
                float restore = Mathf.Clamp01((oldCore + oldShoulder)*3f)
                    *CropFieldRelocationV1.Smooth(RoadZ + 1f, RoadZ, z)*CropFieldRelocationV1.Smooth(FieldZ, FieldZ + 1f, z);
                if (restore > 0)
                {
                    for (int i = 0; i < layers; i++) a[iz, ix, i] = Mathf.Lerp(a[iz, ix, i], meadow[i], restore);
                    restored++;
                }

                // The new stretch, weighted the way MeadowVarietyV3 paints OrganicFarmPaths, but laid over whatever is
                // there: replacing the ground with meadow put a strip of grass between the path and the field's soil.
                Evaluate(newSegments, world, out float core, out float shoulder);
                float dirt = core*.85f;
                float wear = shoulder*.82f*(1 - core) + core*.10f;
                // The worn shoulder is a grassy blend: none of it on the field's tilled soil.
                if (layers > CropFieldRelocationV1.Tilled) wear *= 1 - Mathf.Clamp01(a[iz, ix, CropFieldRelocationV1.Tilled]*1.5f);
                if (dirt + wear <= 0) continue;
                float oldDirt = .08f + .12f*Mathf.PerlinNoise(p.x*.17f + 4, p.z*.17f + 6);
                for (int i = 0; i < layers; i++) target[i] = a[iz, ix, i]*(1 - dirt - wear);
                target[Trodden] += dirt*(1 - oldDirt);
                target[OldEarth] += dirt*oldDirt;
                target[Worn] += wear;
                // Where it joins the barn road, never thin the road's own soil.
                if (z > RoadZ && a[iz, ix, Trodden] + a[iz, ix, OldEarth] > target[Trodden] + target[OldEarth]) continue;
                float total = 0; for (int i = 0; i < layers; i++) total += target[i];
                for (int i = 0; i < layers; i++) a[iz, ix, i] = target[i]/total;
                painted++;
            }
            data.SetAlphamaps(x0, z0, a); data.SetBaseMapDirty();
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            return "Barn path routed through the gate: " + restored + " cells back to meadow, " + painted + " cells of path.";
        }

        // Between the gate and the field the path dipped ~40 cm into the pond's carved west bank (10° down, 8° up),
        // though the barn yard and the field sit at the same height. Fill the path's strip back up to that level,
        // easing into the surrounding ground (and the pond bank) over GradeFalloff. Only raises, never lowers.
        const float GradeLevel = -11.75f, GradeFalloff = 1.5f, GradeNorth = 11.6f, GradeSouth = 2.6f;

        [MenuItem(Menu + "Level Barn Path Between Gate And Field (V1)")]
        static void MenuGrade() => Debug.Log(Grade());

        public static string Grade()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            var segments = Sample(NewRoute);
            int r = data.heightmapResolution;
            var origin = terrain.transform.position;
            float target = (GradeLevel - origin.y)/data.size.y;
            int x0 = Mathf.Max(0, (int)((76f - origin.x)/data.size.x*(r-1))), x1 = Mathf.Min(r-1, (int)((88f - origin.x)/data.size.x*(r-1)) + 1);
            int z0 = Mathf.Max(0, (int)((GradeSouth - 3f - origin.z)/data.size.z*(r-1))), z1 = Mathf.Min(r-1, (int)((GradeNorth + 3f - origin.z)/data.size.z*(r-1)) + 1);
            Undo.RegisterCompleteObjectUndo(data, "Level barn path V1");
            var heights = data.GetHeights(x0, z0, x1-x0+1, z1-z0+1);
            float maxRaise = 0;
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = origin.x + (ix+x0)*data.size.x/(r-1), z = origin.z + (iz+z0)*data.size.z/(r-1);
                var point = new Vector2(x, z);
                // Distance beyond the path's outer edge (core plus shoulder), over the stretch between gate and field.
                float outside = float.MaxValue;
                foreach (var s in segments)
                {
                    Vector2 delta = s.B - s.A;
                    float t = Mathf.Clamp01(Vector2.Dot(point - s.A, delta)/(delta.sqrMagnitude + 1e-6f));
                    Vector2 nearest = s.A + delta*t;
                    if (nearest.y > GradeNorth || nearest.y < GradeSouth) continue;
                    float edge = Mathf.Lerp(s.WidthA, s.WidthB, t) + Mathf.Lerp(s.ShoulderA, s.ShoulderB, t);
                    outside = Mathf.Min(outside, Vector2.Distance(point, nearest) - edge);
                }
                float weight = 1 - CropFieldRelocationV1.Smooth(0, GradeFalloff, outside);
                if (weight <= 0 || heights[iz, ix] >= target) continue;
                float raised = Mathf.Lerp(heights[iz, ix], target, weight);
                maxRaise = Mathf.Max(maxRaise, (raised - heights[iz, ix])*data.size.y);
                heights[iz, ix] = raised;
            }
            data.SetHeights(x0, z0, heights);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            return "Barn path levelled between gate and field (largest raise " + (maxRaise*100).ToString("F0") + " cm).";
        }

        struct Segment { public Vector2 A, B; public float WidthA, WidthB, ShoulderA, ShoulderB; }

        // Catmull-Rom through the waypoints in short segments, as OrganicFarmPaths.AppendRoute does.
        static List<Segment> Sample(Waypoint[] points)
        {
            var result = new List<Segment>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                Waypoint p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Length - 1, i + 2)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1.Point, p2.Point)/.42f));
                Vector2 previous = p1.Point;
                for (int s = 1; s <= steps; s++)
                {
                    float t = s/(float)steps;
                    float t2 = t*t, t3 = t2*t;
                    Vector2 next = .5f*((2f*p1.Point) + (-p0.Point + p2.Point)*t + (2f*p0.Point - 5f*p1.Point + 4f*p2.Point - p3.Point)*t2
                        + (-p0.Point + 3f*p1.Point - 3f*p2.Point + p3.Point)*t3);
                    float tPrev = (s - 1)/(float)steps;
                    result.Add(new Segment { A = previous, B = next,
                        WidthA = Mathf.Lerp(p1.Width, p2.Width, tPrev), WidthB = Mathf.Lerp(p1.Width, p2.Width, t),
                        ShoulderA = Mathf.Lerp(p1.Shoulder, p2.Shoulder, tPrev), ShoulderB = Mathf.Lerp(p1.Shoulder, p2.Shoulder, t) });
                    previous = next;
                }
            }
            return result;
        }

        // OrganicFarmPaths.Evaluate's core and shoulder profile, for these segments.
        static void Evaluate(List<Segment> segments, Vector2 point, out float core, out float shoulder)
        {
            core = 0; shoulder = 0;
            foreach (var segment in segments)
            {
                Vector2 delta = segment.B - segment.A;
                float t = Mathf.Clamp01(Vector2.Dot(point - segment.A, delta)/(delta.sqrMagnitude + 1e-6f));
                Vector2 nearest = segment.A + delta*t;
                float distance = Vector2.Distance(point, nearest);
                float width = Mathf.Lerp(segment.WidthA, segment.WidthB, t);
                float shoulderWidth = Mathf.Lerp(segment.ShoulderA, segment.ShoulderB, t);
                if (distance > width + shoulderWidth + .6f) continue;
                float coreWidth = width + (Mathf.PerlinNoise(nearest.x*.43f + 17.3f, nearest.y*.43f - 8.1f) - .5f)*.55f;
                float coreValue = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coreWidth - .35f, coreWidth + .2f, distance));
                float shoulderValue = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coreWidth, coreWidth + shoulderWidth + .18f, distance)))*(1f - coreValue);
                shoulderValue *= Mathf.Lerp(.62f, 1f, Mathf.PerlinNoise(nearest.x*.21f + 63.8f, nearest.y*.21f + 24.6f));
                core = Mathf.Max(core, coreValue);
                shoulder = Mathf.Max(shoulder, shoulderValue);
            }
        }
    }
}
