using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmReferenceTools
{
    // Low-poly corn plant for the Chick/CropWind shader: five-sided stalk, alternating arching leaves
    // with a folded midrib, a tassel, and one or two husked cobs with silk. Parts are tinted by vertex colour.
    sealed class CornPlantBuilder
    {
        static readonly Color Stalk = new Color(.5f, .62f, .25f), Husk = new Color(.64f, .74f, .34f);
        static readonly Color Tassel = new Color(.86f, .74f, .42f), Silk = new Color(.58f, .34f, .16f);
        static readonly Color LeafDark = new Color(.33f, .53f, .17f), LeafLight = new Color(.52f, .66f, .24f), LeafDry = new Color(.72f, .66f, .34f);

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> sway = new List<Vector2>();   // x = bend weight, y = plant phase
        readonly List<int> triangles = new List<int>();
        Vector3 root; float height, phase;

        int Add(Vector3 p, Color c, float weight) { vertices.Add(p); colors.Add(c); sway.Add(new Vector2(weight, phase)); return vertices.Count - 1; }
        float Weight(Vector3 p) { return Mathf.Clamp((p.y - root.y)/height, 0, 1.2f); }
        void Quad(int a, int b, int c, int d) { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(a); triangles.Add(c); triangles.Add(d); }

        // Tube with rings perpendicular to the local axis (outward-facing winding).
        void Tube(Vector3[] centres, float[] radii, int sides, Color color, float twist, float extraWeight)
        {
            int prev = -1;
            for (int k = 0; k < centres.Length; k++)
            {
                var dir = (centres[Mathf.Min(k+1, centres.Length-1)] - centres[Mathf.Max(k-1, 0)]).normalized;
                var u = Vector3.Cross(dir, Mathf.Abs(dir.z) < .9f ? Vector3.forward : Vector3.right).normalized;
                var v = Vector3.Cross(u, dir);
                int first = vertices.Count;
                for (int s = 0; s < sides; s++)
                {
                    float a = twist + s*Mathf.PI*2/sides;
                    var p = centres[k] + (u*Mathf.Cos(a) + v*Mathf.Sin(a))*radii[k];
                    Add(p, color, Weight(p) + extraWeight);
                }
                if (prev >= 0) for (int s = 0; s < sides; s++) Quad(prev+s, first+s, first+(s+1)%sides, prev+(s+1)%sides);
                prev = first;
            }
        }

        // Long strap leaf: rises out, then droops; three vertices across give it a folded midrib.
        void Leaf(Vector3 start, Vector3 outDir, float length, float width, float droop, Color color, float baseWeight)
        {
            var side = Vector3.Cross(outDir, Vector3.up).normalized;
            int prev = -1; var last = start;
            for (int k = 0; k <= 6; k++)
            {
                float u = k/6f;
                var p = start + outDir*length*u + Vector3.down*droop*length*u*u;
                var along = k == 0 ? outDir : (p - last).normalized; last = p;
                var normal = Vector3.Cross(side, along).normalized;
                float w = width*Mathf.Min(1, .35f + u*3f)*Mathf.Pow(1 - u, .55f) + .002f;
                float weight = baseWeight + u*.45f;       // leaf tips flutter more than the stalk
                int l = Add(p + side*w*.5f, color, weight);
                Add(p - normal*w*.16f, color*1.08f, weight);
                Add(p - side*w*.5f, color, weight);
                if (prev >= 0) { Quad(prev, l, l+1, prev+1); Quad(prev+1, l+1, l+2, prev+2); }
                prev = l;
            }
        }

        // Thin two-vertex strip for tassel spikes and silk.
        void Strip(Vector3 start, Vector3 dir, float length, float width, float droop, Color color, float weight)
        {
            var side = Vector3.Cross(dir, Vector3.up).normalized; if (side.sqrMagnitude < .01f) side = Vector3.right;
            int prev = -1;
            for (int k = 0; k <= 3; k++)
            {
                float u = k/3f, w = width*(1 - u*.8f);
                var p = start + dir*length*u + Vector3.down*droop*u*u;
                int a = Add(p + side*w*.5f, color, weight + u*.15f); Add(p - side*w*.5f, color, weight + u*.15f);
                if (prev >= 0) Quad(prev, a, a+1, prev+1);
                prev = a;
            }
        }

        Vector3 StalkPoint(Vector3 lean, float t) { return root + Vector3.up*height*t + lean*height*t*t; }

        public void Plant(Vector3 rootPosition, float plantHeight, Vector3 lean, float yaw, Func<float, float, float> range)
        {
            root = rootPosition; height = plantHeight; phase = range(0, 1);
            var tint = Color.Lerp(Color.white, new Color(1.05f, 1f, .9f), range(0, 1));
            float[] ts = { 0, .25f, .5f, .75f, 1 };
            var centres = new Vector3[ts.Length]; var radii = new float[ts.Length];
            for (int k = 0; k < ts.Length; k++) { centres[k] = StalkPoint(lean, ts[k]); radii[k] = Mathf.Lerp(.032f, .012f, ts[k]); }
            Tube(centres, radii, 5, Stalk*tint, yaw, 0);

            // Leaves alternate sides up the stalk; lower ones are longer, flatter and sometimes drying.
            int leaves = 8 + (range(0, 1) < .5f ? 1 : 0);
            for (int i = 0; i < leaves; i++)
            {
                float t = Mathf.Lerp(.1f, .8f, i/(leaves - 1f));
                float a = yaw + i*Mathf.PI + range(-.4f, .4f);
                float tilt = (Mathf.Lerp(58, 32, t) + range(-8, 8))*Mathf.Deg2Rad;
                var outDir = new Vector3(Mathf.Cos(a)*Mathf.Sin(tilt), Mathf.Cos(tilt), Mathf.Sin(a)*Mathf.Sin(tilt));
                var color = i == 0 && range(0, 1) < .6f ? LeafDry : Color.Lerp(LeafDark, LeafLight, range(0, 1));
                Leaf(StalkPoint(lean, t), outDir, height*Mathf.Lerp(.42f, .28f, t)*range(.9f, 1.1f), Mathf.Lerp(.14f, .1f, t), range(.65f, 1f), color*tint, t);
            }

            // Tassel: a few pale spikes above the top leaf.
            var top = StalkPoint(lean, 1);
            for (int i = 0; i < 5; i++)
            {
                float a = yaw + i*Mathf.PI*2/5 + range(-.3f, .3f), spread = i == 0 ? 0 : range(.25f, .55f);
                var dir = new Vector3(Mathf.Cos(a)*spread, 1, Mathf.Sin(a)*spread).normalized;
                Strip(top, dir, range(.2f, .3f), .014f, range(.03f, .08f), Tassel*tint, 1.05f);
            }

            // One or two cobs angled out of the leaf axils, wrapped in husk with a silk tuft.
            int cobs = range(0, 1) < .55f ? 2 : 1;
            for (int i = 0; i < cobs; i++)
            {
                float t = i == 0 ? range(.44f, .52f) : range(.56f, .62f), a = yaw + Mathf.PI*.5f + i*Mathf.PI + range(-.4f, .4f);
                var start = StalkPoint(lean, t);
                var axis = (new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))*.55f + Vector3.up).normalized;
                float len = range(.2f, .26f);
                var cob = new[]{ start, start + axis*len*.2f, start + axis*len*.5f, start + axis*len*.8f, start + axis*len };
                Tube(cob, new[]{ .014f, .032f, .038f, .03f, .008f }, 6, Husk*tint, a, 0);
                for (int s = 0; s < 4; s++)
                {
                    var dir = (axis + new Vector3(range(-.6f, .6f), range(-.2f, .3f), range(-.6f, .6f))).normalized;
                    Strip(start + axis*len, dir, range(.05f, .08f), .01f, .03f, Silk, Weight(start));
                }
            }
        }

        public int VertexCount { get { return vertices.Count; } }

        public void Fill(Mesh mesh)
        {
            // Vertex colours are not colour-space converted by Unity; authored values are sRGB.
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < colors.Count && linear; i++) colors[i] = colors[i].linear;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, sway); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.Expand(.4f); mesh.bounds = bounds;   // room for the wind offset
        }
    }
}
