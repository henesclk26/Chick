using System.Collections.Generic;
using UnityEngine;

namespace FarmReferenceTools
{
    /// <summary>
    /// Cached, root-local farm path geometry for terrain texture evaluation.
    /// The returned values are independent weights: core describes the compact
    /// traveled surface, while shoulder describes worn soil beyond that core.
    /// </summary>
    public static class OrganicFarmPaths
    {
        public static readonly Vector2[] GateAnchors =
        {
            new Vector2(-12f, 0f),
            new Vector2(20f, 0f),
            new Vector2(5f, 17f)
        };

        const float SampleStep = 0.42f;
        const float FenceHalfThickness = 0.16f;
        const float GridCellSize = 4f;
        const float GridPadding = 3f;

        struct Waypoint
        {
            public Vector2 Point;
            public float Width;
            public float Shoulder;

            public Waypoint(float x, float z, float width, float shoulder)
            {
                Point = new Vector2(x, z);
                Width = width;
                Shoulder = shoulder;
            }
        }

        struct Segment
        {
            public Vector2 A;
            public Vector2 B;
            public float WidthA;
            public float WidthB;
            public float ShoulderA;
            public float ShoulderB;
            public float Length;

            public Segment(Vector2 a, Vector2 b, float widthA, float widthB,
                float shoulderA, float shoulderB)
            {
                A = a;
                B = b;
                WidthA = widthA;
                WidthB = widthB;
                ShoulderA = shoulderA;
                ShoulderB = shoulderB;
                Length = (b - a).magnitude;
            }
        }

        // A single continuous north field loop joins the two field gates and
        // the barn and coop approaches. The connector reaches the third gate
        // from the west and continues to the coop only after crossing at z=17.
        static readonly Waypoint[] MainRoute =
        {
            new Waypoint(-18f, 12f, 1.28f, .72f),
            new Waypoint(-16.1f, 9.5f, 1.22f, .66f),
            new Waypoint(-13.6f, 5.4f, 1.17f, .61f),
            new Waypoint(-12f, 1.8f, 1.30f, .78f),
            new Waypoint(-12f, 0f, 1.36f, .82f),
            new Waypoint(-13.6f, -2.3f, 1.25f, .70f),
            new Waypoint(-19.2f, -4.25f, 1.18f, .60f),
            new Waypoint(-25.1f, -4.85f, 1.24f, .69f),
            new Waypoint(-29.45f, -5.15f, 1.34f, .80f),
            new Waypoint(-30.05f, -8.4f, 1.25f, .66f),
            new Waypoint(-29.85f, -15.4f, 1.18f, .59f),
            new Waypoint(-29.35f, -22.6f, 1.24f, .69f),
            new Waypoint(-27.8f, -27.1f, 1.38f, .83f),
            new Waypoint(-23.4f, -29.05f, 1.25f, .67f),
            new Waypoint(-15f, -29.35f, 1.17f, .58f),
            new Waypoint(-5f, -29.25f, 1.28f, .73f),
            new Waypoint(5.8f, -29.2f, 1.22f, .64f),
            new Waypoint(16.2f, -29.15f, 1.30f, .76f),
            new Waypoint(24.4f, -28.7f, 1.20f, .62f),
            new Waypoint(28.05f, -26.9f, 1.39f, .84f),
            new Waypoint(29.4f, -22.1f, 1.25f, .69f),
            new Waypoint(29.62f, -14.5f, 1.16f, .57f),
            new Waypoint(29.5f, -7.2f, 1.24f, .68f),
            new Waypoint(28.1f, -4.45f, 1.36f, .81f),
            new Waypoint(24.3f, -2.75f, 1.22f, .65f),
            new Waypoint(21.15f, -1.25f, 1.18f, .61f),
            new Waypoint(20f, 0f, 1.36f, .82f),
            new Waypoint(19.7f, 4.4f, 1.22f, .67f),
            new Waypoint(18.9f, 9.5f, 1.18f, .60f),
            new Waypoint(19.1f, 13.3f, 1.24f, .72f),
            new Waypoint(20f, 17f, 1.30f, .76f)
        };

        static readonly Waypoint[] GateConnector =
        {
            new Waypoint(-18f, 12f, 1.28f, .72f),
            new Waypoint(-14.5f, 14.8f, 1.20f, .64f),
            new Waypoint(-9.2f, 15.8f, 1.16f, .59f),
            new Waypoint(-2.2f, 16.1f, 1.26f, .71f),
            new Waypoint(3f, 17f, 1.29f, .72f),
            new Waypoint(5f, 17f, 1.36f, .83f),
            new Waypoint(7f, 17f, 1.29f, .72f),
            new Waypoint(12.2f, 18.1f, 1.20f, .65f),
            new Waypoint(16f, 18.2f, 1.18f, .68f),
            new Waypoint(20f, 17f, 1.30f, .76f)
        };

        static readonly Waypoint[] WestServiceSpur =
        {
            new Waypoint(-18f, 12f, 1.24f, .69f),
            new Waypoint(-21.2f, 12.3f, 1.18f, .62f),
            new Waypoint(-24.1f, 12.9f, 1.13f, .56f),
            new Waypoint(-27f, 13f, 1.20f, .65f)
        };

        static readonly Segment[] Segments = BuildSegments();
        static readonly Dictionary<long, int[]> SegmentGrid = BuildSegmentGrid();

        static Segment[] BuildSegments()
        {
            var result = new List<Segment>(640);
            AppendRoute(result, MainRoute);
            AppendRoute(result, GateConnector);
            AppendRoute(result, WestServiceSpur);
            return result.ToArray();
        }

        static Dictionary<long, int[]> BuildSegmentGrid()
        {
            var buckets = new Dictionary<long, List<int>>();
            for (int i = 0; i < Segments.Length; i++)
            {
                Segment segment = Segments[i];
                int minX = Mathf.FloorToInt((Mathf.Min(segment.A.x, segment.B.x) - GridPadding) / GridCellSize);
                int maxX = Mathf.FloorToInt((Mathf.Max(segment.A.x, segment.B.x) + GridPadding) / GridCellSize);
                int minZ = Mathf.FloorToInt((Mathf.Min(segment.A.y, segment.B.y) - GridPadding) / GridCellSize);
                int maxZ = Mathf.FloorToInt((Mathf.Max(segment.A.y, segment.B.y) + GridPadding) / GridCellSize);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        long key = CellKey(x, z);
                        List<int> bucket;
                        if (!buckets.TryGetValue(key, out bucket))
                        {
                            bucket = new List<int>(8);
                            buckets.Add(key, bucket);
                        }
                        bucket.Add(i);
                    }
                }
            }

            var grid = new Dictionary<long, int[]>(buckets.Count);
            foreach (KeyValuePair<long, List<int>> pair in buckets)
                grid.Add(pair.Key, pair.Value.ToArray());
            return grid;
        }

        static long CellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        static void AppendRoute(List<Segment> output, Waypoint[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Waypoint p0 = points[Mathf.Max(0, i - 1)];
                Waypoint p1 = points[i];
                Waypoint p2 = points[i + 1];
                Waypoint p3 = points[Mathf.Min(points.Length - 1, i + 2)];
                float span = Vector2.Distance(p1.Point, p2.Point);
                int steps = Mathf.Max(2, Mathf.CeilToInt(span / SampleStep));
                Vector2 previous = p1.Point;
                float previousWidth = p1.Width;
                float previousShoulder = p1.Shoulder;

                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 next = CatmullRom(p0.Point, p1.Point, p2.Point, p3.Point, t);
                    float nextWidth = Mathf.Lerp(p1.Width, p2.Width, t);
                    float nextShoulder = Mathf.Lerp(p1.Shoulder, p2.Shoulder, t);
                    if ((next - previous).sqrMagnitude > 0.000001f)
                        output.Add(new Segment(previous, next, previousWidth, nextWidth,
                            previousShoulder, nextShoulder));
                    previous = next;
                    previousWidth = nextWidth;
                    previousShoulder = nextShoulder;
                }
            }
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return .5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// Evaluates root-local XZ position. Core and shoulder are separate
        /// 0..1 masks so the caller can blend them into different terrain layers.
        /// </summary>
        public static void Evaluate(Vector2 rootLocalXZ, out float core, out float shoulder)
        {
            core = 0f;
            shoulder = 0f;
            if (IsCropBed(rootLocalXZ) || IsFence(rootLocalXZ))
                return;

            float bestCore = 0f;
            float bestShoulder = 0f;
            int cellX = Mathf.FloorToInt(rootLocalXZ.x / GridCellSize);
            int cellZ = Mathf.FloorToInt(rootLocalXZ.y / GridCellSize);
            int[] candidates;
            if (!SegmentGrid.TryGetValue(CellKey(cellX, cellZ), out candidates))
                return;

            for (int candidate = 0; candidate < candidates.Length; candidate++)
            {
                int i = candidates[candidate];
                Segment segment = Segments[i];
                Vector2 delta = segment.B - segment.A;
                float t = Mathf.Clamp01(Vector2.Dot(rootLocalXZ - segment.A, delta)
                    / (delta.sqrMagnitude + 0.000001f));
                Vector2 nearest = segment.A + delta * t;
                float distance = Vector2.Distance(rootLocalXZ, nearest);
                float width = Mathf.Lerp(segment.WidthA, segment.WidthB, t);
                float shoulderWidth = Mathf.Lerp(segment.ShoulderA, segment.ShoulderB, t);
                float outerWidth = width + shoulderWidth;
                if (distance > outerWidth + .2f)
                    continue;

                // Fine, low-amplitude variation keeps the center surface from
                // reading as a perfectly machined ribbon.
                float variation = (Mathf.PerlinNoise(nearest.x * .43f + 17.3f,
                    nearest.y * .43f - 8.1f) - .5f) * .55f;
                float coreWidth = width + variation;
                float coreValue = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(coreWidth - .35f, coreWidth + .2f, distance));

                outerWidth = coreWidth + shoulderWidth;
                float shoulderValue = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(coreWidth, outerWidth + .18f, distance));
                shoulderValue *= 1f - coreValue;

                // Soft longitudinal modulation makes shoulder wear irregular,
                // while preserving a continuous low-strength soil envelope.
                float worn = Mathf.PerlinNoise(nearest.x * .21f + 63.8f,
                    nearest.y * .21f + 24.6f);
                shoulderValue *= Mathf.Lerp(.62f, 1f, worn);
                bestCore = Mathf.Max(bestCore, coreValue);
                bestShoulder = Mathf.Max(bestShoulder, shoulderValue);
            }

            core = Mathf.Clamp01(bestCore);
            shoulder = Mathf.Clamp01(bestShoulder);
        }

        static bool IsCropBed(Vector2 p)
        {
            // Crop centers from the builder skeleton, with the requested 3.5 x
            // 3.3 metre half-bounds. The six-column pitch is approximately 9.2m.
            for (int row = 0; row < 2; row++)
            {
                float z = row == 0 ? -11.39f : -21.89f;
                for (int col = 0; col < 6; col++)
                {
                    float x = -22.83f + col * 9.2f;
                    if (Mathf.Abs(p.x - x) <= 3.5f && Mathf.Abs(p.y - z) <= 3.3f)
                        return true;
                }
            }
            return false;
        }

        static bool IsFence(Vector2 p)
        {
            // Outer fence rectangle. Values on or beyond the fence never paint.
            if (Mathf.Abs(p.x) >= 35f - FenceHalfThickness
                || p.y >= 36f - FenceHalfThickness
                || p.y <= -32f + FenceHalfThickness)
                return true;

            // Horizontal divider at z=0, interrupted only by the two 5m gates.
            if (NearHorizontalFence(p, 0f, -35f, -14.5f)
                || NearHorizontalFence(p, 0f, -9.5f, 17.5f)
                || NearHorizontalFence(p, 0f, 22.5f, 35f))
                return true;

            // Barn/coop divider at x=5, interrupted only by the 5m gate centered
            // at z=17. Its southern section starts at the horizontal fence.
            if (NearVerticalFence(p, 5f, 0f, 14.5f)
                || NearVerticalFence(p, 5f, 19.5f, 36f))
                return true;

            return false;
        }

        static bool NearHorizontalFence(Vector2 p, float z, float minX, float maxX)
        {
            return p.x >= minX && p.x <= maxX
                && Mathf.Abs(p.y - z) <= FenceHalfThickness;
        }

        static bool NearVerticalFence(Vector2 p, float x, float minZ, float maxZ)
        {
            return p.y >= minZ && p.y <= maxZ
                && Mathf.Abs(p.x - x) <= FenceHalfThickness;
        }
    }
}
